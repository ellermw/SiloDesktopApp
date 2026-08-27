using System.Security.Cryptography;
using System.Text;

namespace SiloPlayer.Core.Services;

public readonly record struct AppInstanceNames(string MutexName, string PipeName)
{
    private const string ProductionMutex = @"Local\SiloDesktopPlayer-6F4EE0EA-4DA3-49D0-940D-461F977BA343";
    private const string ProductionPipe = "SiloDesktopPlayer-Activation-6F4EE0EA";

    public static AppInstanceNames Create(string? qaInstanceId)
    {
        if (string.IsNullOrWhiteSpace(qaInstanceId))
            return new AppInstanceNames(ProductionMutex, ProductionPipe);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(qaInstanceId.Trim())))[..12];
        return new AppInstanceNames($"{ProductionMutex}-QA-{hash}", $"{ProductionPipe}-QA-{hash}");
    }
}
