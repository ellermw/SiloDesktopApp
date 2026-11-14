namespace SiloPlayer.Core.Services.Admin;

public sealed class LoggingDisclosurePreferences(string? directory = null)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, bool> _session = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Sections = new(StringComparer.Ordinal) { "logging.serverLog", "logging.retention" };

    public bool Read(string section)
    {
        if (!Sections.Contains(section)) throw new ArgumentException("Unknown Logging disclosure.", nameof(section));
        lock (_gate)
        {
            if (_session.TryGetValue(section, out var cached)) return cached;
            if (directory == null) return false;
            try
            {
                var file = new FileInfo(Path.Combine(directory, section + ".pref"));
                return file.Exists && file.Length == 4 && File.ReadAllText(file.FullName) == "true";
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    public void Write(string section, bool open)
    {
        if (!Sections.Contains(section)) throw new ArgumentException("Unknown Logging disclosure.", nameof(section));
        lock (_gate)
        {
            _session[section] = open;
            if (directory == null) return;
            string? temporary = null;
            try
            {
                Directory.CreateDirectory(directory);
                var target = Path.Combine(directory, section + ".pref");
                temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, open ? "true" : "false");
                File.Move(temporary, target, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
