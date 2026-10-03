using System.Text.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Services;

internal static class RecentPartyStore
{
    internal sealed record Entry(string RoomId, string Code, string Token, DateTimeOffset VisitedAt);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string PathFor(SiloApiClient client, AuthService auth, string? directory = null) => Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "recent-parties"),
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(client.BaseUrl + "|" + auth.CurrentUser?.Id + "|" + auth.SelectedProfileId))) + ".dat");
    public static async Task<List<Entry>> ReadAsync(SiloApiClient client, AuthService auth, string? directory = null)
    {
        var path = PathFor(client, auth, directory);
        try
        {
            if (!File.Exists(path)) return [];
            var buffer = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(path));
            var clear = await new DataProtectionProvider().UnprotectAsync(buffer);
            return JsonSerializer.Deserialize<List<Entry>>(CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, clear)) ?? [];
        }
        catch { return []; }
    }
    public static async Task RememberAsync(SiloApiClient client, AuthService auth, WatchTogetherRoomResponse response, string? directory = null)
    {
        if (string.IsNullOrWhiteSpace(response.RoomAccessToken)) return;
        var authority = client.CaptureContext(); var path = PathFor(client, auth, directory);
        await Gate.WaitAsync();
        try
        {
            if (!client.IsCurrentContext(authority)) return;
            var entries = await ReadAsync(client, auth, directory);
            entries.RemoveAll(e => e.RoomId == response.Room.RoomId);
            entries.Insert(0, new(response.Room.RoomId, response.Room.Code, response.RoomAccessToken, DateTimeOffset.UtcNow));
            var clear = CryptographicBuffer.ConvertStringToBinary(JsonSerializer.Serialize(entries.Take(8)), BinaryStringEncoding.Utf8);
            var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(clear);
            CryptographicBuffer.CopyToByteArray(encrypted, out var bytes);
            if (!client.IsCurrentContext(authority)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch { /* Optional history must not disrupt joining. */ }
        finally { Gate.Release(); }
    }
}
