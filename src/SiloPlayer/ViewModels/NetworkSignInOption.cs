using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.ViewModels;

public sealed partial class NetworkSignInOption(AuthProvider provider) : ObservableObject
{
    public AuthProvider Provider { get; } = provider;
    public string Owner => Provider.NetworkIdentity?.Name ?? "";
    public string? Via => Owner.Length == 0 ? null : "via " + Provider.DisplayName;
    public double IconGap => string.IsNullOrEmpty(Provider.IconUrl) ? 0 : 12;
    public string Label => IsPending ? "Signing in…" : Owner.Length == 0 ? "Continue with " + Provider.DisplayName : "Continue as " + Owner;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isPending;
}
