namespace SiloPlayer.Core.Services.Admin;

/// <summary>A captured settings write, including the versions that it may settle.</summary>
public sealed class AdminSettingsSaveIntent
{
    private readonly Dictionary<string, string> _values;
    private readonly Dictionary<string, long> _versions;
    private readonly IReadOnlyDictionary<string, string> _readOnlyValues;
    private readonly IReadOnlyDictionary<string, long> _readOnlyVersions;
    internal AdminSettingsSaveIntent(Dictionary<string, string> values, string etag, Dictionary<string, long> versions)
    {
        _values = values; ETag = etag; _versions = versions;
        _readOnlyValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(_values);
        _readOnlyVersions = new System.Collections.ObjectModel.ReadOnlyDictionary<string, long>(_versions);
    }
    public IReadOnlyDictionary<string, string> Values => _readOnlyValues;
    public string ETag { get; }
    public IReadOnlyDictionary<string, long> Versions => _readOnlyVersions;
    internal void Clear() { _values.Clear(); _versions.Clear(); }
    public override string ToString() => nameof(AdminSettingsSaveIntent);
}

/// <summary>
/// Page-owned server settings draft. Secret values returned by the server are
/// never retained. The owning controller must serialize access and retire this
/// object with its authority lease.
/// </summary>
public sealed class AdminSettingsForm
{
    private readonly HashSet<string> _keys;
    private readonly HashSet<string> _secrets;
    private readonly Dictionary<string, string> _baseline = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _versions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private string _etag;
    private AdminSettingsSaveIntent? _pending;

    public AdminSettingsForm(IEnumerable<string> keys, IEnumerable<string> secrets,
        IReadOnlyDictionary<string, string> values, string etag)
    {
        _keys = new(keys, StringComparer.Ordinal);
        _secrets = new(secrets, StringComparer.Ordinal);
        if (!_secrets.IsSubsetOf(_keys)) throw new ArgumentException("Secret keys must belong to the form.", nameof(secrets));
        RequireStrongETag(etag);
        _etag = etag;
        foreach (var key in _keys)
        {
            if (!_secrets.Contains(key)) _baseline[key] = values.GetValueOrDefault(key, "");
            _values[key] = _baseline.GetValueOrDefault(key, "");
        }
    }

    public IReadOnlyCollection<string> DirtyKeys => _dirty.ToArray();
    public bool RestartRequired { get; private set; }
    public string Get(string key) { RequireKey(key); return _values.GetValueOrDefault(key, ""); }
    public string GetPersisted(string key) { RequireKey(key); return _baseline.GetValueOrDefault(key, ""); }
    public bool IsClearStaged(string key) => _dirty.Contains(key) && Get(key) == "";

    public void Set(string key, string value, bool stage = false)
    {
        RequireKey(key);
        _values[key] = value;
        _versions[key] = _versions.GetValueOrDefault(key) + 1;
        if (!stage && _pending == null && _baseline.TryGetValue(key, out var saved) && saved == value) _dirty.Remove(key);
        else _dirty.Add(key);
    }

    public void Reset(string key)
    {
        RequireKey(key);
        _values[key] = GetPersisted(key);
        _versions[key] = _versions.GetValueOrDefault(key) + 1;
        _dirty.Remove(key);
    }

    public void Discard() { _pending?.Clear(); _pending = null; foreach (var key in _keys) Reset(key); }

    public void StageRedisUrl(string url)
    {
        Set("redis.url", url);
        var db = Get("redis.db");
        if (db != "") Set("redis.db", db, stage: true);
    }

    public void KeepSavedRedisUrl()
    {
        Reset("redis.url");
        if (Get("redis.db") == GetPersisted("redis.db")) Reset("redis.db");
    }

    public AdminSettingsSaveIntent CaptureSave(IEnumerable<string>? excludedKeys = null)
    {
        if (_pending != null) throw new InvalidOperationException("A settings save is already pending.");
        var excluded = new HashSet<string>(excludedKeys ?? [], StringComparer.Ordinal);
        var selected = _dirty.Where(key => !excluded.Contains(key)).ToArray();
        return _pending = new(selected.ToDictionary(key => key, Get, StringComparer.Ordinal), _etag,
            selected.ToDictionary(key => key, key => _versions.GetValueOrDefault(key), StringComparer.Ordinal));
    }

    public void Acknowledge(AdminSettingsSaveIntent intent, IReadOnlyDictionary<string, string> values, string etag, bool restartRequired)
    {
        RequireStrongETag(etag);
        if (!ReferenceEquals(_pending, intent) || _etag != intent.ETag) throw new InvalidOperationException("The settings baseline changed during the save.");
        foreach (var (key, submitted) in intent.Values)
        {
            RequireKey(key);
            if (_secrets.Contains(key))
            {
                if (_versions.GetValueOrDefault(key) == intent.Versions[key])
                {
                    _values[key] = "";
                    _dirty.Remove(key);
                }
                continue;
            }
            var saved = values.GetValueOrDefault(key, submitted);
            _baseline[key] = saved;
            if (_versions.GetValueOrDefault(key) == intent.Versions[key])
            {
                _values[key] = saved;
                _dirty.Remove(key);
            }
            else _dirty.Add(key);
        }
        _etag = etag;
        RestartRequired |= restartRequired;
        _pending = null;
        intent.Clear();
    }

    /// <summary>End an unacknowledged attempt; its draft remains staged for reconciliation.</summary>
    public void EndSave(AdminSettingsSaveIntent intent)
    {
        if (ReferenceEquals(_pending, intent)) { _pending = null; intent.Clear(); }
    }

    public void Reload(IReadOnlyDictionary<string, string> values, string etag, bool preserveDraft)
    {
        RequireStrongETag(etag);
        _pending?.Clear(); _pending = null;
        foreach (var key in _keys)
        {
            if (!_secrets.Contains(key)) _baseline[key] = values.GetValueOrDefault(key, "");
            if (!preserveDraft || !_dirty.Contains(key)) Reset(key);
        }
        _etag = etag;
    }

    private void RequireKey(string key)
    {
        if (!_keys.Contains(key)) throw new ArgumentException("This setting is not owned by the form.", nameof(key));
    }
    public static void RequireStrongETag(string? etag)
    {
        if (!AdminValidators.IsStrong(etag)) throw new InvalidDataException("Settings response is missing its version.");
    }
}
