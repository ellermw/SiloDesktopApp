using System.Globalization;
using System.Net.Http.Headers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed class LoggingDraft
{
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[] { "server.log_level", "server.log_quiet", "opslog.retention_days", "opslog.max_size_mb", "opslog.max_rows", "policy.decision_log_retention_days", "policy.decision_log_verbosity" });
    private readonly Dictionary<string, string> _baseline = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _versions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly LoggingDisclosure _serverAdvanced;
    private readonly LoggingDisclosure _retentionAdvanced;
    private LoggingRestartKeys _restartKeys = new();
    private string? _etag;

    public LoggingDraft(LoggingDisclosurePreferences? preferences = null)
    {
        preferences ??= new();
        _serverAdvanced = new(preferences, "logging.serverLog");
        _retentionAdvanced = new(preferences, "logging.retention");
    }

    public IReadOnlyDictionary<string, string> Values { get { lock (_gate) return new Dictionary<string, string>(_values); } }
    public IReadOnlyCollection<string> DirtyKeys { get { lock (_gate) return _dirty.ToArray(); } }
    public bool Loaded { get { lock (_gate) return _etag != null; } }
    public bool RestartRequired { get; private set; }
    public bool ServerAdvancedOpen { get { lock (_gate) return _serverAdvanced.Open; } }
    public bool RetentionAdvancedOpen { get { lock (_gate) return _retentionAdvanced.Open; } }
    public string Get(string key) { lock (_gate) return _values.GetValueOrDefault(key, ""); }
    public bool IsDirty(string key) { lock (_gate) return _dirty.Contains(key); }
    public bool NeedsRestart(string key) { lock (_gate) return _restartKeys.Has(key); }
    public void SetRestartKeys(LoggingRestartKeys keys) { lock (_gate) _restartKeys = keys; }
    public void ToggleServerAdvanced() { lock (_gate) _serverAdvanced.Toggle(); }
    public void ToggleRetentionAdvanced() { lock (_gate) _retentionAdvanced.Toggle(); }

    public void Load(LoggingSettingsSnapshot snapshot, bool preserveDraft = false)
    {
        RequireStrongETag(snapshot.ETag);
        lock (_gate)
        {
            foreach (var key in Keys)
            {
                _baseline[key] = snapshot.Values.GetValueOrDefault(key, "");
                if (!preserveDraft || !_dirty.Contains(key)) _values[key] = _baseline[key];
            }
            if (!preserveDraft) _dirty.Clear();
            _etag = snapshot.ETag;
            UpdateDisclosures();
        }
    }

    public void Set(string key, string value)
    {
        if (!Keys.Contains(key, StringComparer.Ordinal)) throw new ArgumentException("This setting is not part of Logging.", nameof(key));
        lock (_gate)
        {
            if (_etag == null) throw new InvalidOperationException("Reload settings before editing.");
            _values[key] = value;
            _versions[key] = _versions.GetValueOrDefault(key) + 1;
            _dirty.Add(key);
            UpdateDisclosures();
        }
    }

    public LoggingSaveIntent BeginSave()
    {
        lock (_gate)
        {
            if (_etag == null) throw new InvalidOperationException("Reload settings before saving.");
            var values = _dirty.ToDictionary(key => key, key => _values[key], StringComparer.Ordinal);
            Validate(values);
            return new(values, _etag, _dirty.ToDictionary(key => key, key => _versions.GetValueOrDefault(key), StringComparer.Ordinal));
        }
    }

    public void Acknowledge(LoggingSaveIntent intent, ApiResponse<LoggingSettingsResult> result)
    {
        RequireStrongETag(result.ETag);
        lock (_gate)
        {
            if (_etag != intent.ETag) throw new InvalidOperationException("This save no longer owns the Logging draft.");
            foreach (var (key, submitted) in intent.Values)
            {
                var acknowledged = result.Body.Values.GetValueOrDefault(key, submitted);
                _baseline[key] = acknowledged;
                if (_versions.GetValueOrDefault(key) == intent.Versions[key])
                {
                    _values[key] = acknowledged;
                    _dirty.Remove(key);
                }
                else if (!_dirty.Contains(key)) _values[key] = acknowledged;
            }
            _etag = result.ETag;
            RestartRequired |= result.Body.RestartRequired;
            UpdateDisclosures();
        }
    }

    public void Discard()
    {
        lock (_gate)
        {
            foreach (var (key, value) in _baseline) { _values[key] = value; _versions[key] = _versions.GetValueOrDefault(key) + 1; }
            _dirty.Clear();
            UpdateDisclosures();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _values.Clear(); _baseline.Clear(); _versions.Clear(); _dirty.Clear(); _etag = null;
            _restartKeys = new(); RestartRequired = false;
            _serverAdvanced.Reset(); _retentionAdvanced.Reset();
        }
    }

    public static void RequireStrongETag(string? etag)
    {
        if (!EntityTagHeaderValue.TryParse(etag, out var parsed) || parsed.IsWeak || parsed.Tag == "*")
            throw new InvalidDataException("Reload settings before saving: the server did not provide a strong settings revision.");
    }

    public static void Validate(IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            if (!Keys.Contains(key, StringComparer.Ordinal)) throw new ArgumentException("This setting is not part of Logging.");
            var valid = key switch
            {
                "server.log_level" => value is "debug" or "info" or "warn" or "error",
                "policy.decision_log_verbosity" => value is "digest" or "verbose",
                "server.log_quiet" => true,
                "opslog.retention_days" or "policy.decision_log_retention_days" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days >= 1,
                _ => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 1
            };
            if (!valid) throw new ArgumentException("Enter a valid value for " + key + ".");
        }
    }

    private void UpdateDisclosures()
    {
        _serverAdvanced.Force(_dirty.Contains("server.log_quiet"));
        _retentionAdvanced.Force(new[] { "opslog.max_rows", "policy.decision_log_retention_days", "policy.decision_log_verbosity" }.Any(_dirty.Contains));
    }

    private sealed class LoggingDisclosure(LoggingDisclosurePreferences preferences, string section)
    {
        private bool _persisted = preferences.Read(section);
        private bool? _override;
        private bool _forced;
        public bool Open => _override ?? (_persisted || _forced);
        public void Force(bool value) { if (value && !_forced) _override = null; _forced = value; }
        public void Toggle() { _persisted = !Open; _override = _persisted; preferences.Write(section, _persisted); }
        public void Reset() { _override = null; _forced = false; }
    }
}
