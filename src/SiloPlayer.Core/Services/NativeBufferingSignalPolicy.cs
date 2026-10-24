namespace SiloPlayer.Core.Services;

public static class NativeBufferingSignalPolicy
{
    public static bool IsOutage(bool waiting, bool terminalTransportFailure, double sinceLastByteMs, bool segmented = false)
        => waiting && (terminalTransportFailure || sinceLastByteMs >= (segmented ? 15000 : 4000));
}
