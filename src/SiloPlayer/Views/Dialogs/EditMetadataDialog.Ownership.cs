using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views.Dialogs;

public sealed partial class EditMetadataDialog
{
    private bool _observingWrite, _writeRefreshFailed, _updatingForm, _reconcileAgain;
    private readonly TextBlock _writeStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _retryWriteRefresh = new() { Visibility = Visibility.Collapsed };
    private StackPanel? _writeNotice;
    private Action? _refreshImageSelection;
    private bool WritePending => _pending || _imagePending || _observingWrite || _writeRefreshFailed
        || _api.PendingWrites.IsPending(_identityOwner, _item.ContentId);

    private void RefreshWriteControls()
    {
        IsPrimaryButtonEnabled = CurrentOwner && !WritePending;
        _reset.IsEnabled = CanSubmitReset;
        PrimaryButtonText = AppStrings.Get(WritePending ? "web.item.dialog.saving" : "web.item.editMetadata.saveChanges");
        _writeStatus.Text = AppStrings.Get(_writeRefreshFailed ? "web.common.ui.loadError.generic" : "web.item.dialog.saving");
        _retryWriteRefresh.Content = AppStrings.Get("common.retry");
        _retryWriteRefresh.Visibility = _writeRefreshFailed ? Visibility.Visible : Visibility.Collapsed;
        if (_writeNotice != null) _writeNotice.Visibility = WritePending ? Visibility.Visible : Visibility.Collapsed;
        _refreshImageSelection?.Invoke();
    }

    private void MetadataWriteStarted(ApiIdentityContext owner, string itemId)
    {
        if (owner != _identityOwner || itemId != _item.ContentId) return;
        // Capture the shared task before posting to the dispatcher: even a
        // write that finishes before that post runs must reach this opening.
        var pending = _api.PendingWrites.WaitForIdleAsync(owner, itemId, _lifetime.Token);
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => _ = FollowPendingWritesAsync(pending: pending));
            return;
        }
        _ = FollowPendingWritesAsync(pending: pending);
    }

    private async Task FollowPendingWritesAsync(bool retry = false, Task<MediaItemDetail?>? pending = null)
    {
        if (!CurrentOwner) return;
        if (_observingWrite) { _reconcileAgain = true; return; }
        if (!retry && pending == null && !_api.PendingWrites.IsPending(_identityOwner, _item.ContentId)) return;
        _observingWrite = true; _writeRefreshFailed = false; RefreshWriteControls();
        try
        {
            MediaItemDetail? refreshed;
            do
            {
                _reconcileAgain = false;
                refreshed = await (pending ?? _api.PendingWrites.WaitForIdleAsync(_identityOwner, _item.ContentId, _lifetime.Token));
                pending = null;
                if (!CurrentOwner) return;
                // A failed reconciliation must never expose stale locks to Save.
                if (refreshed == null)
                {
                    using var read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    read.CancelAfter(TimeSpan.FromSeconds(30));
                    refreshed = await App.Services.GetRequiredService<CatalogApi>().GetItemDetailAsync(_identityOwner, _item.ContentId, read.Token);
                }
                if (!CurrentOwner) return;
                RebaseItem(refreshed);
            } while (_reconcileAgain || _api.PendingWrites.IsPending(_identityOwner, _item.ContentId));
        }
        catch (OperationCanceledException) when (!CurrentOwner) { }
        catch (Exception)
        {
            // Reconciliation is not the write outcome. Keep Retry visible without
            // reporting an already accepted Save/Apply/Reset as a failed write.
            _writeRefreshFailed = true;
        }
        finally { _observingWrite = false; RefreshWriteControls(); }
    }

    private void RebaseItem(MediaItemDetail refreshed)
    {
        _updatingForm = true;
        try
        {
            _state.Rebase(refreshed); _item = refreshed;
            foreach (var (key, input) in _textInputs) input.Text = _state.Fields[key]?.ToString() ?? "";
            foreach (var (key, input) in _numberInputs) input.Value = _state.Fields[key] is double value ? value : double.NaN;
            foreach (var (key, input) in _choiceInputs) input.SelectedItem = _state.Fields[key];
            foreach (var (key, input) in _tagInputs) input.SetValues((string[])_state.Fields[key]!);
            RefreshLocks();
        }
        finally { _updatingForm = false; }
    }

    private async Task<MediaItemDetail?> RunOwnedWriteAsync(Func<CancellationToken, Task> write)
    {
        var owner = _identityOwner;
        var access = _client.CaptureContext().AccessGeneration;
        var itemId = _item.ContentId;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var auth = App.Services.GetRequiredService<AuthService>();
        using var authority = new CancellationTokenSource();
        bool CurrentIdentity() => _client.IsCurrentIdentityContext(owner) && _client.CaptureContext().AccessGeneration == access
            && AuthorizationPolicy.CanCurateMetadata(auth);
        void RetireWrite() { if (!CurrentIdentity()) authority.Cancel(); }
        _client.ContextInvalidated += RetireWrite;
        auth.UserChanged += RetireWrite; auth.ProfileChanged += RetireWrite; auth.LoggedOut += RetireWrite;
        try
        {
            RetireWrite();
            return await _api.PendingWrites.RunAsync(owner, itemId, async ct =>
            {
                await write(ct);
                if (CurrentOwner) HasSaved = true;
                // Publish the committed write even if its reconciliation fails
                // or the initiating dialog has already closed.
                if (CurrentIdentity()) DispatcherQueue.TryEnqueue(() => { if (CurrentIdentity()) Changed(); });
            }, async ct =>
            {
                if (!CurrentIdentity()) throw new OperationCanceledException(ct);
                var refreshed = await catalog.GetItemDetailAsync(owner, itemId, ct);
                if (!CurrentIdentity()) throw new OperationCanceledException(ct);
                return refreshed;
            }, _lifetime.Token, authority.Token);
        }
        finally
        {
            _client.ContextInvalidated -= RetireWrite;
            auth.UserChanged -= RetireWrite; auth.ProfileChanged -= RetireWrite; auth.LoggedOut -= RetireWrite;
        }
    }
}
