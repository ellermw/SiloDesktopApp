using Windows.Networking.Connectivity;

namespace SiloPlayer.Services;

internal static class PlaybackConnectionNetwork
{
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
