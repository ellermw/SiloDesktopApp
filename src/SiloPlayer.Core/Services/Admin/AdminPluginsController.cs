using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminPluginsSection { Installed, Catalog, Repositories, CatalogSettings, Provisioning, UpdateCheck }

/// <summary>How a write ended. NotSent means nothing reached the server; Refused is a definite answer; Unknown blocks the target until it is read back.</summary>
public enum AdminPluginWriteOutcome { Done, NotSent, Refused, Unknown }

public static class AdminPluginWriteOutcomes
{
    /// <summary>
    /// Whether a save's typed values must be erased: the server took them, or may have. After a definite refusal or a save that was
    /// never sent they stay, so the corrected retry carries them.
    /// </summary>
    public static bool SettlesTypedValues(this AdminPluginWriteOutcome outcome) => outcome is AdminPluginWriteOutcome.Done or AdminPluginWriteOutcome.Unknown;
}

/// <summary>
/// The row a confirmation or control showed: a write is sent only while exactly this row is still on screen. Two rows are
/// the same only when plugin, version, source, repository and recorded update all match.
/// </summary>
public sealed record AdminPluginPin(string Id, string PluginId, string Version, string SourceKind, string? RepositoryId, string? AvailableVersion)
{
    public static AdminPluginPin Of(AdminPlugin row) => new(row.Id, row.PluginId, row.Version, row.SourceKind, row.RepositoryId, row.AvailableVersion);
    public bool Matches(AdminPlugin row) => row.Id == Id && row.PluginId == PluginId && row.Version == Version && row.SourceKind == SourceKind &&
        row.RepositoryId == RepositoryId && (row.AvailableVersion ?? "") == (AvailableVersion ?? "");
}

public sealed record AdminPluginsState
{
    public IReadOnlyList<AdminPlugin> Installations { get; init; } = [];
    public IReadOnlyList<AdminPlugin> Catalog { get; init; } = [];
    public IReadOnlyList<AdminPluginRepository> Repositories { get; init; } = [];
    public IReadOnlyList<AdminPluginProvisionStatus> Provisioning { get; init; } = [];
    public AdminVersioned<AdminPluginCatalogSettings>? CatalogSettings { get; init; }
    public AdminPluginCatalogStatus? CatalogStatus { get; init; }
    /// <summary>Reads that failed on their latest try, with the text to show beside a Reload. Earlier data stays on screen.</summary>
    public IReadOnlyDictionary<AdminPluginsSection, string> Failures { get; init; } = new Dictionary<AdminPluginsSection, string>();
    /// <summary>Sections that have been read successfully at least once.</summary>
    public IReadOnlySet<AdminPluginsSection> Loaded { get; init; } = new HashSet<AdminPluginsSection>();
    public bool Loading { get; init; }
    public bool Busy { get; init; }
    /// <summary>What the write in flight acts on (a plugin ID, "upload", "repositories", "catalog-settings", "update-check").</summary>
    public string? BusyKey { get; init; }
    public bool CatalogReviewRequired { get; init; }
    public string TaskState { get; init; } = "";
    public string Notice { get; init; } = "";
    public bool NoticeIsError { get; init; }
    /// <summary>Targets whose last write has an unknown outcome and may not be written again until they are read back.</summary>
    public IReadOnlySet<string> Unconfirmed { get; init; } = new HashSet<string>();
    public long Revision { get; init; }
    public bool Ready(AdminPluginsSection section) => Loaded.Contains(section);
}

/// <summary>
/// One visible Plugins page. Reads are bounded and per section; a response is applied only if it is still the newest read of
/// that section and the page, server and account are unchanged. Every write is sent once, against the row that was on screen
/// and confirmed, under a lease that dies with the page; an unknown outcome blocks that target until it is read back.
/// </summary>
public sealed class AdminPluginsController : IDisposable
{
    public const string UpdateCheckTask = "check_plugin_updates";
    /// <summary>The unknown-outcome key of an upload: it could have replaced any plugin, so it blocks every lifecycle write.</summary>
    public const string UploadKey = "*";
    private readonly AdminPluginsApi _api;
    private readonly AdminTasksApi _tasks;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly Dictionary<AdminPluginsSection, long> _generations = [];
    private readonly Dictionary<string, long> _unknown = new(StringComparer.Ordinal);
    private readonly List<(CancellationTokenSource Source, HashSet<AdminPluginsSection> Sections)> _reads = [];
    private long _epoch;
    private int _taskRead;
    private int _writing;
    private string? _detailPluginId;
    private bool _disposed;
    private AdminPluginsState _state = new();

    public AdminPluginsState State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease? Owner => _lifetime.Owner;
    public string? DetailPluginId => _detailPluginId;
    public event Action? Changed;
    public event Action? Retired;

    public AdminPluginsController(AdminPluginsApi api, AdminTasksApi tasks, AdminMonitorAuthority authority)
    {
        _api = api; _tasks = tasks; _lifetime = new(authority); _lifetime.Retired += OnRetired;
    }

    private void OnRetired()
    {
        lock (_gate) Clear();
        Changed?.Invoke(); Retired?.Invoke();
    }

    // Held under _gate.
    private void Clear()
    {
        foreach (var read in _reads) { try { read.Source.Cancel(); } catch (ObjectDisposedException) { } }
        _reads.Clear();
        foreach (var key in _generations.Keys.ToArray()) _generations[key]++;
        _unknown.Clear();
        _state = new() { Revision = _state.Revision + 1 };
    }

    /// <summary>Captures authority and reads the page. <paramref name="detailPluginId"/> narrows the reads to what one plugin's page needs.</summary>
    public async Task OpenAsync(string? detailPluginId = null)
    {
        _detailPluginId = detailPluginId;
        try { _lifetime.Open(); await ReloadAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) { OnRetired(); }
    }

    // ── reads ──────────────────────────────────────────────────────────────

    private static readonly AdminPluginsSection[] BoardSections = [AdminPluginsSection.Installed, AdminPluginsSection.Catalog, AdminPluginsSection.Repositories,
        AdminPluginsSection.CatalogSettings, AdminPluginsSection.Provisioning, AdminPluginsSection.UpdateCheck];
    private static readonly AdminPluginsSection[] DetailSections = [AdminPluginsSection.Installed, AdminPluginsSection.Catalog, AdminPluginsSection.Provisioning];

    private sealed record Read<T>(bool Ok, T? Value, string Error);

    /// <summary>Reads the given sections (every section the page needs when none are named). A newer read takes over any section an older one still had in flight.</summary>
    public async Task ReloadAsync(IEnumerable<AdminPluginsSection>? only = null)
    {
        var owner = Owner;
        if (owner is not { IsCurrent: true } || _disposed) return;
        var scope = _detailPluginId == null ? BoardSections : DetailSections;
        var wanted = scope.Where(section => only == null || only.Contains(section)).ToHashSet();
        if (wanted.Count == 0) return;
        long epochAtStart; var generation = new Dictionary<AdminPluginsSection, long>(); CancellationTokenSource source;
        lock (_gate)
        {
            if (_disposed) return;
            // The older reads are superseded; whatever they still had in flight is read again by this one.
            foreach (var older in _reads) { wanted.UnionWith(older.Sections); try { older.Source.Cancel(); } catch (ObjectDisposedException) { } }
            source = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); source.CancelAfter(TimeSpan.FromSeconds(90));
            _reads.Add((source, wanted));
            foreach (var section in wanted) generation[section] = _generations[section] = _generations.GetValueOrDefault(section) + 1;
            epochAtStart = _epoch; _state = _state with { Loading = true, Revision = _state.Revision + 1 };
        }
        Changed?.Invoke();
        var token = source.Token;
        async Task<Read<T>> Get<T>(Func<Task<T>> load, string failure)
        {
            try { return new(true, await load().ConfigureAwait(false), ""); }
            catch (Exception) { return new(false, default, failure); }
        }
        var installed = wanted.Contains(AdminPluginsSection.Installed) ? Get(() => _api.InstallationsAsync(owner, token), AdminPluginsText.T("web.admin.plugins.detail.installationsErrorTitle")) : null;
        async Task<Read<IReadOnlyList<AdminPlugin>>> CatalogRead()
        {
            // An installed plugin whose manifest has presentation needs no catalog fetch (every repository index is read live).
            if (_detailPluginId != null && installed != null)
            {
                var rows = await installed.ConfigureAwait(false);
                if (rows.Ok && rows.Value!.Any(p => p.PluginId == _detailPluginId && p.Presentation != null)) return new(true, [], "");
            }
            return await Get(() => _api.CatalogAsync(owner, token), AdminPluginsText.T("web.admin.plugins.detail.catalogErrorTitle")).ConfigureAwait(false);
        }
        var catalog = wanted.Contains(AdminPluginsSection.Catalog) ? CatalogRead() : null;
        var repositories = wanted.Contains(AdminPluginsSection.Repositories) ? Get(() => _api.RepositoriesAsync(owner, token), AdminPluginsText.T("web.admin.plugins.repositories.loadFailed")) : null;
        var settings = wanted.Contains(AdminPluginsSection.CatalogSettings)
            ? Get(async () => (Settings: await _api.CatalogSettingsAsync(owner, token).ConfigureAwait(false), Status: await _api.CatalogStatusAsync(owner, token).ConfigureAwait(false)),
                AdminPluginsText.T("web.admin.plugins.hooks.plugins.catalogSettings.reloadBeforeEditing")) : null;
        var provisioning = wanted.Contains(AdminPluginsSection.Provisioning) ? Get(() => _api.ProvisioningStatusAsync(owner, token), AdminPluginsText.T("web.admin.plugins.provisioning.loadError")) : null;
        var task = wanted.Contains(AdminPluginsSection.UpdateCheck) ? Get(() => _tasks.GetAsync(owner, UpdateCheckTask, token), AdminPluginsText.T("web.admin.plugins.hooks.plugins.updateCheck.failed")) : null;
        await Task.WhenAll(new Task?[] { installed, catalog, repositories, settings, provisioning, task }.Where(t => t != null).Cast<Task>()).ConfigureAwait(false);
        lock (_gate)
        {
            _reads.RemoveAll(read => ReferenceEquals(read.Source, source)); source.Dispose();
            if (_disposed || !owner.IsCurrent || !ReferenceEquals(owner, Owner)) return;
            var failures = new Dictionary<AdminPluginsSection, string>(_state.Failures); var loaded = new HashSet<AdminPluginsSection>(_state.Loaded);
            var next = _state;
            bool Current(AdminPluginsSection section) => _generations.GetValueOrDefault(section) == generation[section];
            void Apply<T>(AdminPluginsSection section, Task<Read<T>>? read, Func<T, AdminPluginsState> publish)
            {
                // A superseded read's late result, success or failure, changes nothing.
                if (read == null || !Current(section)) return;
                var result = read.Result;
                if (!result.Ok) { failures[section] = result.Error; return; }
                failures.Remove(section); loaded.Add(section); next = publish(result.Value!);
            }
            Apply(AdminPluginsSection.Installed, installed, rows => next with { Installations = rows });
            Apply(AdminPluginsSection.Catalog, catalog, rows => next with { Catalog = rows });
            Apply(AdminPluginsSection.Repositories, repositories, rows => next with { Repositories = rows });
            Apply(AdminPluginsSection.CatalogSettings, settings, pair => next with { CatalogSettings = pair.Settings, CatalogStatus = pair.Status, CatalogReviewRequired = false });
            Apply(AdminPluginsSection.Provisioning, provisioning, rows => next with { Provisioning = rows });
            Apply(AdminPluginsSection.UpdateCheck, task, info => next with { TaskState = info.State });
            // A write whose outcome was unknown is cleared by a successful, still-current read of what it could have changed that began after it.
            bool Fresh(AdminPluginsSection section) => generation.ContainsKey(section) && Current(section) && !failures.ContainsKey(section) && loaded.Contains(section);
            foreach (var pair in _unknown.ToArray())
            {
                var section = pair.Key switch { "repositories" => AdminPluginsSection.Repositories, "catalog-settings" => AdminPluginsSection.CatalogSettings, "update-check" => AdminPluginsSection.UpdateCheck, _ => AdminPluginsSection.Installed };
                if (pair.Value <= epochAtStart && Fresh(section)) _unknown.Remove(pair.Key);
            }
            _state = next with { Failures = failures, Loaded = loaded, Loading = _reads.Count > 0, Unconfirmed = new HashSet<string>(_unknown.Keys), Revision = next.Revision + 1 };
        }
        Changed?.Invoke();
    }

    // ── writes ─────────────────────────────────────────────────────────────

    private void Set(Func<AdminPluginsState, AdminPluginsState> change)
    {
        lock (_gate) { if (_disposed) return; var next = change(_state); _state = next with { Revision = _state.Revision + 1 }; }
        Changed?.Invoke();
    }

    private bool IsInstalled(AdminPluginPin pin) => State.Installations.Any(pin.Matches);
    private bool IsListed(AdminPluginPin pin) => State.Catalog.Any(pin.Matches);

    private static bool Definite(ApiException error) => error.StatusCode is >= 400 and < 500 && error.StatusCode != 408;

    private enum Kind { Install, Installation, Repository, CatalogSettings, Config, ConfigTest, Binding, TaskBinding, Upload, UpdateCheck }

    /// <summary>The official message for an outcome that could not be confirmed, by kind.</summary>
    private static string UnconfirmedText(Kind kind, AdminPluginCommand? command) => kind switch
    {
        Kind.Install => AdminPluginsText.T("web.admin.plugins.hooks.plugins.install.unconfirmed"),
        Kind.Upload => AdminPluginsText.T("web.admin.plugins.hooks.plugins.upload.unconfirmed"),
        Kind.Repository => AdminPluginsText.T("web.admin.plugins.hooks.plugins.repository.updateUnconfirmed"),
        Kind.CatalogSettings => AdminPluginsText.T("web.admin.plugins.hooks.plugins.catalogSettings.updateFailed"),
        Kind.Config => AdminPluginsText.T("web.admin.plugins.hooks.plugins.config.unconfirmed"),
        Kind.ConfigTest => AdminPluginsText.T("web.admin.plugins.configForm.checkFailed"),
        Kind.Binding => AdminPluginsText.T("web.admin.plugins.hooks.plugins.authBinding.unconfirmed"),
        Kind.TaskBinding => AdminPluginsText.T("web.admin.plugins.hooks.plugins.taskBinding.unconfirmed"),
        Kind.UpdateCheck => AdminPluginsText.T("web.admin.plugins.hooks.plugins.updateCheck.failed"),
        _ => command switch
        {
            AdminPluginCommand.Update => AdminPluginsText.T("web.admin.plugins.hooks.plugins.applyUpdate.unconfirmed"),
            AdminPluginCommand.Restart => AdminPluginsText.T("web.admin.plugins.hooks.plugins.restart.unconfirmed"),
            AdminPluginCommand.Uninstall => AdminPluginsText.T("web.admin.plugins.hooks.plugins.remove.unconfirmed"),
            _ => AdminPluginsText.T("web.admin.plugins.hooks.plugins.installation.updateFailed"),
        },
    };

    private static string RefusalText(ApiException error, IEnumerable<string>? secrets, string fallback)
    {
        var reason = AdminPluginPresentation.Redact(ApiProblemText.Display(error), 400, secrets);
        return reason.Length > 0 ? reason : fallback;
    }

    private static readonly AdminPluginsSection[] None = [];

    /// <summary>
    /// The single path every write takes. <paramref name="reload"/> names the sections read back afterwards (all when null,
    /// none when empty). A write is sent at most once: an answer that is not a definite refusal is an unknown outcome.
    /// </summary>
    private async Task<AdminPluginWriteOutcome> WriteAsync(Kind kind, string key, Func<bool> target, Func<MonitorAuthorityLease, Task<string>> send,
        AdminPluginCommand? command = null, IReadOnlyCollection<string>? secrets = null, AdminPluginsSection[]? reload = null)
    {
        if (_disposed || Owner is not { IsCurrent: true }) return AdminPluginWriteOutcome.NotSent;
        if (Interlocked.CompareExchange(ref _writing, 1, 0) != 0) return AdminPluginWriteOutcome.NotSent;
        MonitorAuthorityLease? action = null;
        try
        {
            var state = State;
            if (state.Loading) { Set(s => s with { Notice = "Plugins are still loading. Try again when they have loaded.", NoticeIsError = true }); return AdminPluginWriteOutcome.NotSent; }
            if (state.Unconfirmed.Contains(key) || kind is not (Kind.UpdateCheck or Kind.ConfigTest) && state.Unconfirmed.Contains(UploadKey))
            { Set(s => s with { Notice = UnconfirmedText(kind, command), NoticeIsError = true }); return AdminPluginWriteOutcome.NotSent; }
            action = _lifetime.CaptureAction();
            // The pinned row is checked at the moment of sending; nothing awaits between this check and the request.
            if (!target()) { Set(s => s with { Notice = "The plugin list changed. Review it and try again.", NoticeIsError = true }); return AdminPluginWriteOutcome.NotSent; }
            Set(s => s with { Busy = true, BusyKey = key, Notice = "", NoticeIsError = false });
            string notice;
            try { notice = await send(action).ConfigureAwait(false); }
            catch (Exception error)
            {
                if (_disposed || !_lifetime.IsCurrent(action)) return AdminPluginWriteOutcome.NotSent;
                // A request that failed its own checks never left this machine.
                if (error is ArgumentException)
                { Set(s => s with { Notice = AdminPluginPresentation.Redact(error.Message, 300, secrets), NoticeIsError = true }); return AdminPluginWriteOutcome.NotSent; }
                if (error is ApiException api && Definite(api))
                {
                    var conflict = kind == Kind.CatalogSettings && api.StatusCode is 412 or 428;
                    var text = conflict ? AdminPluginsText.T("web.admin.plugins.hooks.plugins.catalogSettings.changed") : RefusalText(api, secrets, UnconfirmedText(kind, command));
                    Set(s => s with { Notice = text, NoticeIsError = true, CatalogReviewRequired = s.CatalogReviewRequired || conflict });
                    // A 409 means the installation is no longer what the page shows: read it back so the admin can see why.
                    if (api.StatusCode == 409 && reload is not { Length: 0 }) await ReloadAsync(reload).ConfigureAwait(false);
                    return AdminPluginWriteOutcome.Refused;
                }
                // Timeout, transport failure, 5xx, or a receipt that could not be trusted: the server may have acted. Never replay.
                if (kind != Kind.ConfigTest) lock (_gate) _unknown[key] = ++_epoch;
                Set(s => s with { Notice = UnconfirmedText(kind, command), NoticeIsError = true, Unconfirmed = new HashSet<string>(_unknown.Keys) });
                if (kind != Kind.ConfigTest && reload is not { Length: 0 }) await ReloadAsync(reload).ConfigureAwait(false);
                return AdminPluginWriteOutcome.Unknown;
            }
            if (_disposed || !_lifetime.IsCurrent(action)) return AdminPluginWriteOutcome.NotSent;
            Set(s => s with { Notice = notice, NoticeIsError = false });
            if (reload is not { Length: 0 }) await ReloadAsync(reload).ConfigureAwait(false);
            return AdminPluginWriteOutcome.Done;
        }
        finally
        {
            action?.Dispose();
            Interlocked.Exchange(ref _writing, 0);
            if (!_disposed) Set(s => s with { Busy = false, BusyKey = null });
        }
    }

    // Installations -----------------------------------------------------------------------------------------------

    /// <summary>Installs the catalog version the admin confirmed. Sent once, only while that exact entry is listed and the plugin is not already installed.</summary>
    public Task<AdminPluginWriteOutcome> InstallAsync(AdminPluginPin catalogEntry) => WriteAsync(Kind.Install, catalogEntry.PluginId,
        () => IsListed(catalogEntry) && !State.Installations.Any(p => p.PluginId == catalogEntry.PluginId), async lease =>
        {
            await _api.InstallAsync(lease, new() { RepositoryId = catalogEntry.RepositoryId ?? "", PluginId = catalogEntry.PluginId, Version = catalogEntry.Version }).ConfigureAwait(false);
            return AdminPluginsText.T("web.admin.plugins.hooks.plugins.install.done");
        });

    public Task<AdminPluginWriteOutcome> CommandAsync(AdminPluginPin pin, AdminPluginCommand command) => WriteAsync(Kind.Installation, pin.PluginId, () =>
        State.Installations.FirstOrDefault(pin.Matches) is { } row &&
        (command != AdminPluginCommand.Update || AdminPluginPresentation.PendingUpdate(row) != null) &&
        (command != AdminPluginCommand.Uninstall || row.EnvManaged == null) &&
        (command != AdminPluginCommand.Restart || row is { Enabled: true, Runtime.Resident: true }), async lease =>
        {
            if (command == AdminPluginCommand.Uninstall) { await _api.UninstallAsync(lease, pin.Id).ConfigureAwait(false); return AdminPluginsText.T("web.admin.plugins.hooks.plugins.remove.done"); }
            await _api.CommandAsync(lease, pin.Id, command).ConfigureAwait(false);
            return command switch
            {
                AdminPluginCommand.Update => AdminPluginsText.T("web.admin.plugins.hooks.plugins.applyUpdate.done"),
                AdminPluginCommand.Restart => AdminPluginsText.T("web.admin.plugins.hooks.plugins.restart.requested"),
                _ => AdminPluginsText.T("web.admin.plugins.hooks.plugins.installation.updated"),
            };
        }, command);

    public Task<AdminPluginWriteOutcome> SetPolicyAsync(AdminPluginPin pin, string policy) => WriteAsync(Kind.Installation, pin.PluginId,
        () => State.Installations.FirstOrDefault(pin.Matches) is { } row && string.IsNullOrEmpty(row.EnvManaged?.VersionPin), async lease =>
        {
            await _api.SetUpdatePolicyAsync(lease, pin.Id, policy).ConfigureAwait(false);
            return AdminPluginsText.T("web.admin.plugins.hooks.plugins.installation.updated");
        });

    // Configuration and bindings ----------------------------------------------------------------------------------

    private static AdminPluginConfigWrite CopyOf(AdminPluginConfigWrite input) => new()
    {
        Key = input.Key, ClearSecrets = [.. input.ClearSecrets], Value = input.Value.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
    };

    private static string[] SecretValues(AdminPluginConfigWrite input) =>
        input.Value.Values.Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").Where(s => s.Length > 0).ToArray();

    public async Task<AdminPluginWriteOutcome> SaveConfigAsync(AdminPluginPin pin, AdminPluginConfigWrite input)
    {
        var copy = CopyOf(input); var secrets = SecretValues(copy);
        try
        {
            return await WriteAsync(Kind.Config, pin.PluginId, () => IsInstalled(pin), async lease =>
            {
                await _api.SaveConfigAsync(lease, pin.Id, copy).ConfigureAwait(false);
                return AdminPluginsText.T("web.admin.plugins.hooks.plugins.config.saved");
            }, secrets: secrets).ConfigureAwait(false);
        }
        finally { copy.Value.Clear(); copy.ClearSecrets.Clear(); Array.Clear(secrets); }
    }

    /// <summary>
    /// The connection check carries staged secrets, so it is sent once and never replayed, but it changes nothing and does not
    /// block the plugin afterwards. Returns what to show beside the button, or null when nothing was sent.
    /// </summary>
    public async Task<AdminPluginCheck?> TestConfigAsync(AdminPluginPin pin, AdminPluginConfigWrite input)
    {
        var copy = CopyOf(input); var secrets = SecretValues(copy);
        AdminPluginCheck? result = null;
        try
        {
            var outcome = await WriteAsync(Kind.ConfigTest, "test:" + pin.PluginId, () => State.Installations.FirstOrDefault(pin.Matches) is { ConnectionCheckSupported: true }, async lease =>
            {
                result = await _api.TestConfigAsync(lease, pin.Id, copy).ConfigureAwait(false);
                return "";
            }, secrets: secrets, reload: None).ConfigureAwait(false);
            return outcome switch
            {
                AdminPluginWriteOutcome.Done => result is { } check ? new AdminPluginCheck { Success = check.Success, Message = AdminPluginPresentation.Redact(check.Message, 400, secrets) } : null,
                AdminPluginWriteOutcome.Refused or AdminPluginWriteOutcome.Unknown => new AdminPluginCheck { Success = false, Message = State.Notice },
                _ => null,
            };
        }
        finally { copy.Value.Clear(); copy.ClearSecrets.Clear(); Array.Clear(secrets); }
    }

    public Task<AdminPluginWriteOutcome> SaveAuthBindingAsync(AdminPluginPin pin, AdminPluginAuthBindingWrite input)
    {
        var copy = new AdminPluginAuthBindingWrite { CapabilityId = input.CapabilityId, Enabled = input.Enabled, DisplayOrder = input.DisplayOrder, AutoProvision = input.AutoProvision, DefaultLogin = input.DefaultLogin };
        return WriteAsync(Kind.Binding, pin.PluginId, () => State.Installations.FirstOrDefault(pin.Matches) is { } row &&
            row.Capabilities.FirstOrDefault(c => c.Id == copy.CapabilityId)?.EnvManaged?.Contains("enabled") != true, async lease =>
        {
            await _api.AuthBindingAsync(lease, pin.Id, copy).ConfigureAwait(false);
            return AdminPluginsText.T("web.admin.plugins.hooks.plugins.authBinding.saved");
        });
    }

    public Task<AdminPluginWriteOutcome> SaveTaskBindingAsync(AdminPluginPin pin, string capabilityId, bool enabled, IReadOnlyDictionary<string, JsonElement> trigger)
    {
        var copy = trigger.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        return WriteAsync(Kind.TaskBinding, pin.PluginId, () => IsInstalled(pin), async lease =>
            await _api.TaskBindingAsync(lease, pin.Id, capabilityId, enabled, copy).ConfigureAwait(false)
                ? AdminPluginsText.T("web.admin.plugins.hooks.plugins.taskBinding.savedRestart") : AdminPluginsText.T("web.admin.plugins.hooks.plugins.taskBinding.saved"));
    }

    // Repositories, catalog settings, upload ----------------------------------------------------------------------

    public Task<AdminPluginWriteOutcome> AddRepositoryAsync(string name, string url) => WriteAsync(Kind.Repository, "repositories", () => true, async lease =>
    {
        await _api.AddRepositoryAsync(lease, name, url).ConfigureAwait(false);
        return AdminPluginsText.T("web.admin.plugins.hooks.plugins.repository.added");
    });

    public Task<AdminPluginWriteOutcome> SetRepositoryEnabledAsync(AdminPluginRepository reviewed, bool enabled)
    {
        var id = reviewed.Id; var url = reviewed.Url; var was = reviewed.Enabled;
        return WriteAsync(Kind.Repository, "repositories", () => State.Repositories.Any(r => r.Id == id && r.Url == url && r.Enabled == was && !r.Managed), async lease =>
        {
            await _api.SetRepositoryEnabledAsync(lease, id, enabled).ConfigureAwait(false);
            return AdminPluginsText.T("web.admin.plugins.hooks.plugins.repository.updated");
        });
    }

    public Task<AdminPluginWriteOutcome> RemoveRepositoryAsync(AdminPluginRepository reviewed)
    {
        var id = reviewed.Id; var url = reviewed.Url;
        return WriteAsync(Kind.Repository, "repositories", () => State.Repositories.Any(r => r.Id == id && r.Url == url && !r.Managed), async lease =>
        {
            await _api.RemoveRepositoryAsync(lease, id).ConfigureAwait(false);
            return AdminPluginsText.T("web.admin.plugins.hooks.plugins.repository.removed");
        });
    }

    public Task<AdminPluginWriteOutcome> SetCommunityAsync(AdminVersioned<AdminPluginCatalogSettings> reviewed, bool include) => WriteAsync(Kind.CatalogSettings, "catalog-settings",
        () => ReferenceEquals(State.CatalogSettings, reviewed) && reviewed.CanWrite && !State.CatalogReviewRequired, async lease =>
        {
            await _api.SetCatalogSettingsAsync(lease, reviewed, include).ConfigureAwait(false);
            return AdminPluginsText.T("web.admin.plugins.hooks.plugins.catalogSettings.updated");
        });

    /// <summary>Uploads one plugin file. The stream is read once, in order; nothing about it is kept after this returns.</summary>
    public Task<AdminPluginWriteOutcome> UploadAsync(Stream stream, string filename, long length, IProgress<int>? progress = null) => WriteAsync(Kind.Upload, UploadKey, () => true, async lease =>
    {
        await _api.UploadAsync(lease, stream, filename, length, progress, lease.Token).ConfigureAwait(false);
        return AdminPluginsText.T("web.admin.plugins.hooks.plugins.upload.done");
    });

    // Update check -------------------------------------------------------------------------------------------------

    public Task<AdminPluginWriteOutcome> CheckUpdatesAsync() => WriteAsync(Kind.UpdateCheck, "update-check", () => State.TaskState is not ("running" or "cancelling"), async lease =>
    {
        var info = await _tasks.RunAsync(lease, UpdateCheckTask).ConfigureAwait(false);
        Set(s => s with { TaskState = info.State });
        return AdminPluginsText.T("web.admin.plugins.hooks.plugins.updateCheck.started");
    }, reload: [AdminPluginsSection.UpdateCheck]);

    /// <summary>One bounded read of the update-check task. A failure keeps what is on screen; when a run ends, the lists are read again.</summary>
    public async Task PollUpdateCheckAsync(bool refreshInventory = true)
    {
        var owner = Owner;
        if (_disposed || owner is not { IsCurrent: true } || State.Busy || State.Loading || Interlocked.CompareExchange(ref _taskRead, 1, 0) != 0) return;
        try
        {
            var info = await _tasks.GetAsync(owner, UpdateCheckTask).ConfigureAwait(false);
            if (!owner.IsCurrent || !ReferenceEquals(owner, Owner) || State.Busy) return;
            var was = State.TaskState; var finished = was is "running" or "cancelling" && info.State == "idle";
            Set(s => s with { TaskState = info.State });
            if (finished && refreshInventory) await ReloadAsync([AdminPluginsSection.Installed, AdminPluginsSection.Catalog, AdminPluginsSection.Repositories]).ConfigureAwait(false);
        }
        catch (Exception) { /* a failed poll keeps the data on screen; the page keeps polling within its bound */ }
        finally { Interlocked.Exchange(ref _taskRead, 0); }
    }

    public void Dismiss() => Set(s => s with { Notice = "", NoticeIsError = false });

    /// <summary>Retires everything the page holds: reads, unknown-outcome marks and data. The page is not usable again until <see cref="OpenAsync"/> after Resume.</summary>
    public void Suspend()
    {
        lock (_gate) Clear();
        _lifetime.Retire();
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; Clear(); }
        _lifetime.Retired -= OnRetired; _lifetime.Dispose(); Changed = null; Retired = null;
    }
}
