using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.ViewModels;

public sealed partial class NetworkSignInOption(AuthProvider provider) : ObservableObject
{
    public AuthProvider Provider { get; } = provider;
    public string Owner => Provider.NetworkIdentity?.Name ?? "";
    public string? Via => Owner.Length == 0 ? null : AppStrings.Get("web.auth.login.network.via", new Dictionary<string, object?> { ["provider"] = Provider.DisplayName }, "via " + Provider.DisplayName);
    public double IconGap => string.IsNullOrEmpty(Provider.IconUrl) ? 0 : 12;
    public string Label => IsPending ? AppStrings.Get("web.auth.login.network.signingIn") : Owner.Length == 0
        ? AppStrings.Get("web.auth.login.network.continueWith", new Dictionary<string, object?> { ["provider"] = Provider.DisplayName }, "Continue with " + Provider.DisplayName)
        : AppStrings.Get("web.auth.login.network.continueAs", new Dictionary<string, object?> { ["name"] = Owner }, "Continue as " + Owner);
    public void RefreshLocalizedText() { OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(Via)); }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isPending;
}
