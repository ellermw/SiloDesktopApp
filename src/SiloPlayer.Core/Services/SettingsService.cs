using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SiloPlayer.Core.Models;

namespace SiloPlayer.Core.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly string _filePath;
    private readonly object _lock = new();
    private AppSettings? _cached;

    public SettingsService(string appDataDir)
    {
        Directory.CreateDirectory(appDataDir);
        _filePath = Path.Combine(appDataDir, "settings.json");
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (_cached != null)
                return _cached;

            if (!File.Exists(_filePath))
            {
                _cached = new AppSettings();
                return _cached;
            }
            var json = File.ReadAllText(_filePath);
            _cached = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return _cached;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            _cached = settings;
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
    }

    public void AddServer(string url, string name)
    {
        url = ServerUrlIdentity.Normalize(url);
        var settings = Load();
        if (settings.Servers.Any(s =>
                string.Equals(ServerUrlIdentity.Normalize(s.Url), url, StringComparison.OrdinalIgnoreCase)))
            return;
        settings.Servers.Add(new ServerEntry { Url = url, Name = name, LastUsed = DateTime.UtcNow });
        Save(settings);
    }

    public void RemoveServer(string url)
    {
        var normalized = ServerUrlIdentity.Normalize(url);
        var settings = Load();
        settings.Servers.RemoveAll(s =>
            string.Equals(ServerUrlIdentity.Normalize(s.Url), normalized, StringComparison.OrdinalIgnoreCase));
        Save(settings);
    }

    public void UpdateLastUsed(string url)
    {
        var normalized = ServerUrlIdentity.Normalize(url);
        var settings = Load();
        var server = settings.Servers.FirstOrDefault(s =>
            string.Equals(ServerUrlIdentity.Normalize(s.Url), normalized, StringComparison.OrdinalIgnoreCase));
        if (server != null)
        {
            server.LastUsed = DateTime.UtcNow;
            Save(settings);
        }
    }
}
