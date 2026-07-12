namespace SiloPlayer.Core.Services;

public interface ICredentialStore
{
    void SaveCredential(string serverUrl, string key, string value);
    string? LoadCredential(string serverUrl, string key);
    void DeleteCredential(string serverUrl, string key);
    void DeleteAllForServer(string serverUrl);
}
