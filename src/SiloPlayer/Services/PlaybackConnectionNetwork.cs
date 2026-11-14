using Windows.Networking.Connectivity;

namespace SiloPlayer.Services;

internal static class PlaybackConnectionNetwork
{
    // Speed tests remain conservative; quality caps apply only to an identified link.
    internal static (string Key, bool Metered, string Transport) CaptureForQuality()
    {
        var network = CaptureSafely();
        return network.Key is "offline" or "unavailable" ? (network.Key, false, network.Transport) : network;
    }

    internal static (string Key, bool Metered, string Transport) CaptureSafely()
    {
        try { return Capture(); }
        catch { return ("unavailable", true, "other"); }
    }

    internal static (string Key, bool Metered, string Transport) Capture()
    {
        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile == null) return ("offline", true, "other");
        var cost = profile.GetConnectionCost();
        var metered = cost.Roaming || cost.OverDataLimit || cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable;
        var transport = profile.IsWwanConnectionProfile ? "cellular" : profile.IsWlanConnectionProfile ? "wifi" : profile.NetworkAdapter?.IanaInterfaceType == 6 ? "ethernet" : "other";
        return ($"{profile.NetworkAdapter?.NetworkAdapterId}:{profile.GetNetworkConnectivityLevel()}", metered || transport == "cellular", transport);
    }
}
