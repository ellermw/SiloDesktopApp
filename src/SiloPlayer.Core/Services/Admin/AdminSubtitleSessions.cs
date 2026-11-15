using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>State of the metadata editor for one render.</summary>
public sealed record AdminSubtitleEditorState
{
    public AdminStoredSubtitle Row { get; init; } = new();
    public bool Loading { get; init; }
    public string? LoadError { get; init; }
    /// <summary>The record and strong validator the next save is guarded by.</summary>
    public AdminVersioned<AdminSubtitleMetadata>? Current { get; init; }
    public string Language { get; init; } = "";
    public string ReleaseName { get; init; } = "";
    public bool HearingImpaired { get; init; }
    public bool Saving { get; init; }
    /// <summary>The unconfirmed save's text. Save stays disabled until the record is reloaded (never replayed).</summary>
    public string? Failure { get; init; }
    public bool Saved { get; init; }
    public bool Retired { get; init; }

    public AdminSubtitlePatch Patch => Current == null ? new(null, null, null) : AdminSubtitlePatch.Between(Current.Value, Language, ReleaseName, HearingImpaired);
    public bool IsDirty => Current != null && !Saved && !Patch.IsEmpty;
    public bool LanguageChanged => Current != null && !string.Equals(Language, Current.Value.Language, StringComparison.Ordinal);
    /// <summary>
    /// Field validation before sending: the release name's 4,096-character
    /// bound. Language is chosen from a list (or kept as stored, which the
    /// server already returned in canonical form); the API rechecks its pattern.
    /// </summary>
    public string? Validation => ReleaseName.Length > AdminSubtitlePatch.MaxReleaseNameLength
        ? AdminSubtitlesText.T("web.common.schemaForm.maxLength", ("label", AdminSubtitlesText.T("web.admin.common.subtitles.edit.releaseName")),
            ("count", AdminSubtitlePatch.MaxReleaseNameLength), ("count_text", AdminSubtitlePatch.MaxReleaseNameLength.ToString("N0", Localization.AppStrings.FormatCulture)))
        : null;
    public bool CanSave => !Retired && Current is { CanWrite: true } && !Loading && !Saving && !Saved && Failure == null && IsDirty && Validation == null;
    public bool CanReload => !Retired && !Loading && !Saving && !Saved && (LoadError != null || Failure != null);
}

/// <summary>
/// One Edit-subtitle sheet (WebUI <c>AdminSubtitleEditSheet</c>): reads the
/// record with its strong validator, stages language / release name /
/// hearing-impaired edits, and sends only the changed fields with that exact
/// validator. A failed or uncertain save keeps the draft and disables Save
/// until the current record is reloaded in place (the WebUI asks to close and
/// reopen); a 412 is never rebased or retried automatically. The whole
/// session is bound to one action lease captured when Edit was chosen.
/// </summary>
public sealed class AdminSubtitleEditorSession : IDisposable
{
    private readonly AdminSubtitlesController _controller;
    private readonly AdminSubtitlesApi _api;
    private readonly MonitorAuthorityLease _action;
    private readonly CancellationTokenSource _reads;
    private readonly object _gate = new();
    private AdminSubtitleEditorState _state;
    private bool _hasDraft;
    private long _readSequence;
    private bool _closeRequested;
    private bool _disposed;

    internal AdminSubtitleEditorSession(AdminSubtitlesController controller, AdminSubtitlesApi api, MonitorAuthorityLease action, AdminStoredSubtitle row)
    {
        _controller = controller; _api = api; _action = action;
        _reads = CancellationTokenSource.CreateLinkedTokenSource(action.Token);
        _state = new AdminSubtitleEditorState { Row = row };
    }

    public event Action? Changed;
    public AdminSubtitleEditorState State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease ActionLease => _action;

    /// <summary>Reads the current record (first open, Retry or Reload). An existing draft is kept for review against the new record.</summary>
    public async Task LoadAsync()
    {
        long sequence;
        lock (_gate)
        {
            if (_disposed || _state.Saving || _state.Saved) return;
            if (!_controller.IsActionCurrent(_action)) { _state = _state with { Retired = true }; sequence = -1; }
            else
            {
                sequence = ++_readSequence;
                _state = _state with { Loading = true, LoadError = null };
            }
        }
        Changed?.Invoke();
        if (sequence < 0) return;
        try
        {
            var current = await _api.GetMetadataAsync(_action, _state.Row, _reads.Token).ConfigureAwait(false);
            Update(sequence, () =>
            {
                // Reload keeps only what the user changed; untouched fields take the server's
                // current values, so a later save never reverts someone else's edit.
                var previous = _hasDraft ? _state.Current?.Value : null;
                var fresh = current.Value;
                _state = _state with
                {
                    Loading = false, Current = current, Failure = null,
                    Language = previous == null || _state.Language == previous.Language ? fresh.Language : _state.Language,
                    ReleaseName = previous == null || _state.ReleaseName == previous.ReleaseName ? fresh.ReleaseName : _state.ReleaseName,
                    HearingImpaired = previous == null || _state.HearingImpaired == previous.HearingImpaired ? fresh.HearingImpaired : _state.HearingImpaired,
                };
                _hasDraft = true;
            });
        }
        catch (Exception error) when (AdminFailures.Classify(error, _action) is AdminFailureKind.Retired or AdminFailureKind.Cancelled)
        {
            Update(sequence, () => _state = _state with { Loading = false, Retired = !_controller.IsActionCurrent(_action) });
        }
        catch (Exception error)
        {
            Update(sequence, () => _state = _state with { Loading = false, LoadError = AdminSubtitlesFailures.Describe(error, "web.admin.common.subtitles.edit.loadFailed") });
        }
    }

    public void SetLanguage(string language) => Edit(state => state with { Language = language });
    public void SetReleaseName(string releaseName) => Edit(state => state with { ReleaseName = releaseName });
    public void SetHearingImpaired(bool hearingImpaired) => Edit(state => state with { HearingImpaired = hearingImpaired });

    /// <summary>Drops staged edits back to the loaded record (leave-guard Discard).</summary>
    public void Discard() => Edit(state => state.Current == null ? state : state with
    {
        Language = state.Current.Value.Language, ReleaseName = state.Current.Value.ReleaseName, HearingImpaired = state.Current.Value.HearingImpaired,
    });

    private void Edit(Func<AdminSubtitleEditorState, AdminSubtitleEditorState> change)
    {
        lock (_gate)
        {
            if (_disposed || _state.Current == null || _state.Saving || _state.Saved || _state.Retired) return;
            _state = change(_state);
        }
        Changed?.Invoke();
    }

    /// <summary>Sends the changed fields once. True when the server confirmed the save.</summary>
    public async Task<bool> SaveAsync()
    {
        AdminVersioned<AdminSubtitleMetadata> current;
        AdminSubtitlePatch patch;
        lock (_gate)
        {
            if (_disposed || !_state.CanSave || !_controller.IsActionCurrent(_action)) return false;
            current = _state.Current!;
            patch = _state.Patch;
            _readSequence++;
            _state = _state with { Saving = true, Failure = null };
        }
        Changed?.Invoke();
        try
        {
            // The write is not tied to the read token: closing the sheet must not abandon a sent save.
            var saved = await _api.UpdateMetadataAsync(_action, _state.Row, current, patch).ConfigureAwait(false);
            lock (_gate) _state = _state with { Saving = false, Saved = true, Current = saved };
            Changed?.Invoke();
            if (_controller.IsActionCurrent(_action))
            {
                _controller.Announce(AdminSubtitlesText.T("web.admin.subtitles.toast.updated"), error: false);
                _controller.AfterMutation(_action);
            }
            return true;
        }
        catch (Exception error) when (AdminFailures.Classify(error, _action) is AdminFailureKind.Retired or AdminFailureKind.Cancelled)
        {
            lock (_gate) _state = _state with { Saving = false, Retired = !_controller.IsActionCurrent(_action), Failure = AdminSubtitlesFailures.Describe(error, "web.admin.common.subtitles.edit.saveFailed") };
            Changed?.Invoke();
            return false;
        }
        catch (Exception error)
        {
            lock (_gate) _state = _state with { Saving = false, Failure = AdminSubtitlesFailures.Describe(error, "web.admin.common.subtitles.edit.saveFailed") };
            Changed?.Invoke();
            return false;
        }
        finally { CompleteDeferredClose(); }
    }

    private void Update(long sequence, Action update)
    {
        lock (_gate)
        {
            if (_disposed || sequence != _readSequence) return;
            update();
        }
        Changed?.Invoke();
    }

    /// <summary>Ends the session. A save already sent is allowed to settle first; its lease is released afterwards.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_state.Saving) { _closeRequested = true; _reads.Cancel(); Changed = null; return; }
            _disposed = true;
        }
        Release();
    }

    private void CompleteDeferredClose()
    {
        lock (_gate)
        {
            if (!_closeRequested || _disposed) return;
            _disposed = true;
        }
        Release();
    }

    private void Release()
    {
        Changed = null;
        try { _reads.Cancel(); } catch (ObjectDisposedException) { }
        _reads.Dispose();
        _action.Dispose();
    }
}

/// <summary>State of the delete confirmation for one render.</summary>
public sealed record AdminSubtitleDeletionState
{
    public AdminStoredSubtitle Row { get; init; } = new();
    public bool Loading { get; init; } = true;
    public AdminVersioned<AdminSubtitleMetadata>? Reviewed { get; init; }
    public string? Error { get; init; }
    public bool Attempted { get; init; }
    public bool Deleted { get; init; }
    public bool Retired { get; init; }
    public bool CanConfirm => !Retired && !Loading && Reviewed is { CanWrite: true } && !Attempted && Error == null;
}

/// <summary>
/// One delete confirmation (WebUI <c>AdminSubtitleDeleteDialog</c> and
/// <c>prepareAdminSubtitleDeletion</c>): the current record is read first
/// and its validator owns at most one DELETE. A failure is shown and the
/// confirmation stays disabled; the user closes and reviews again.
/// </summary>
public sealed class AdminSubtitleDeletionSession : IDisposable
{
    private readonly AdminSubtitlesApi _api;
    private readonly MonitorAuthorityLease _action;
    private readonly AdminSubtitlesController _controller;
    private readonly CancellationTokenSource _reads;
    private readonly object _gate = new();
    private AdminSubtitleDeletionState _state;
    private bool _deleting;
    private bool _closeRequested;
    private bool _disposed;

    internal AdminSubtitleDeletionSession(AdminSubtitlesApi api, MonitorAuthorityLease action, AdminStoredSubtitle row, AdminSubtitlesController controller)
    {
        _api = api; _action = action; _controller = controller;
        _reads = CancellationTokenSource.CreateLinkedTokenSource(action.Token);
        _state = new AdminSubtitleDeletionState { Row = row };
    }

    public event Action? Changed;
    public AdminSubtitleDeletionState State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease ActionLease => _action;

    public async Task PrepareAsync()
    {
        try
        {
            var reviewed = await _api.GetMetadataAsync(_action, _state.Row, _reads.Token).ConfigureAwait(false);
            Set(state => state with { Loading = false, Reviewed = reviewed });
        }
        catch (Exception error) when (AdminFailures.Classify(error, _action) is AdminFailureKind.Retired or AdminFailureKind.Cancelled)
        {
            Set(state => state with { Loading = false, Retired = !_controller.IsActionCurrent(_action) });
        }
        catch (Exception error)
        {
            Set(state => state with { Loading = false, Error = AdminSubtitlesFailures.Describe(error, "web.admin.common.subtitles.delete.loadFailed") });
        }
    }

    /// <summary>Sends the one DELETE this confirmation owns. True when the server confirmed it.</summary>
    public async Task<bool> ConfirmAsync()
    {
        AdminVersioned<AdminSubtitleMetadata>? reviewed = null;
        lock (_gate)
        {
            if (_disposed) return false;
            if (_state.Attempted) _state = _state with { Error = AdminSubtitlesText.T("web.admin.errors.subtitleDeleteAttempted") };
            else if (!_state.CanConfirm || !_controller.IsActionCurrent(_action)) return false;
            else
            {
                reviewed = _state.Reviewed!;
                _state = _state with { Attempted = true };
                _deleting = true;
            }
        }
        Changed?.Invoke();
        if (reviewed == null) return false;
        try
        {
            await _api.DeleteAsync(_action, _state.Row, reviewed).ConfigureAwait(false);
            Set(state => state with { Deleted = true });
            if (_controller.IsActionCurrent(_action))
            {
                _controller.Announce(AdminSubtitlesText.T("web.admin.common.subtitles.delete.deleted"), error: false);
                _controller.AfterMutation(_action);
            }
            return true;
        }
        catch (Exception error) when (AdminFailures.Classify(error, _action) is AdminFailureKind.Retired or AdminFailureKind.Cancelled)
        {
            Set(state => state with { Retired = !_controller.IsActionCurrent(_action), Error = AdminSubtitlesFailures.Describe(error, "web.admin.common.subtitles.delete.confirmFailed") });
            return false;
        }
        catch (Exception error)
        {
            Set(state => state with { Error = AdminSubtitlesFailures.Describe(error, "web.admin.common.subtitles.delete.confirmFailed") });
            return false;
        }
        finally
        {
            bool release;
            lock (_gate) { _deleting = false; release = _closeRequested && !_disposed; if (release) _disposed = true; }
            if (release) Release();
        }
    }

    private void Set(Func<AdminSubtitleDeletionState, AdminSubtitleDeletionState> change)
    {
        lock (_gate)
        {
            if (_disposed && !_deleting) return;
            _state = change(_state);
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_deleting) { _closeRequested = true; _reads.Cancel(); Changed = null; return; }
            _disposed = true;
        }
        Release();
    }

    private void Release()
    {
        Changed = null;
        try { _reads.Cancel(); } catch (ObjectDisposedException) { }
        _reads.Dispose();
        _action.Dispose();
    }
}
