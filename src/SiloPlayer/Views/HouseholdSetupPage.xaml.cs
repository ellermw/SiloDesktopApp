using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views.Dialogs;

namespace SiloPlayer.Views;

public sealed partial class HouseholdSetupPage : Page
{
    private readonly AuthApi _authApi;
    private readonly AuthService _authService;
    private readonly SettingsApi _settingsApi;
    private readonly SiloApiClient _apiClient;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _lifetime;

    public ObservableCollection<Profile> Profiles { get; } = [];
    public ObservableCollection<HouseholdProfileTile> ProfileTiles { get; } = [];

    public HouseholdSetupPage()
    {
        InitializeComponent();
        _authApi = App.Services.GetRequiredService<AuthApi>();
        _authService = App.Services.GetRequiredService<AuthService>();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();
        _apiClient = App.Services.GetRequiredService<SiloApiClient>();
        _settingsService = App.Services.GetRequiredService<SettingsService>();
        ProfilesRepeater.ItemsSource = ProfileTiles;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!_authService.IsLoggedIn)
        {
            App.Services.GetRequiredService<NavigationService>().Navigate<ServerSelectPage>();
            return;
        }
        _lifetime = new CancellationTokenSource();
        await LoadAsync(_lifetime.Token);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        var prior = Interlocked.Exchange(ref _lifetime, null);
        prior?.Cancel();
        prior?.Dispose();
        base.OnNavigatedFrom(e);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        ContentScroll.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        try
        {
            var profilesTask = _authApi.GetProfilesAsync(ct);
            var brandingTask = _settingsApi.GetServerBrandingAsync(ct);
            var response = await profilesTask;
            Profiles.Clear();
            foreach (var profile in response.Profiles) Profiles.Add(profile);
            RebuildProfileTiles();
            SkipButton.Content = Profiles.Count > 1 ? "Skip for now" : "Just me for now";

            try
            {
                var branding = await brandingTask;
                var background = _apiClient.ResolveServerUrl(branding.LoginBackgroundUrl);
                if (!string.IsNullOrWhiteSpace(background))
                {
                    LoginBackgroundImage.Source = new BitmapImage(new Uri(background));
                    LoginBackgroundImage.Visibility = Visibility.Visible;
                    BackgroundScrim.Visibility = Visibility.Visible;
                }
            }
            catch when (!ct.IsCancellationRequested) { }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            ErrorText.Text = ex is HttpRequestException
                ? "Unable to load household profiles. Check your connection and try again."
                : ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            ContentScroll.Visibility = Visibility.Visible;
        }
    }

    private async void ProfileCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Tag is not Profile profile)
        {
            AddProfile_Click(sender, e);
            return;
        }
        try
        {
            await ProfileEditorDialog.ShowAsync(XamlRoot, profile);
            if (_lifetime != null) await ReloadProfilesAsync(_lifetime.Token);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ProfileEditorDialog.ShowAsync(XamlRoot, null);
            if (_lifetime != null) await ReloadProfilesAsync(_lifetime.Token);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task ReloadProfilesAsync(CancellationToken ct)
    {
        var response = await _authApi.GetProfilesAsync(ct);
        Profiles.Clear();
        foreach (var profile in response.Profiles) Profiles.Add(profile);
        RebuildProfileTiles();
        SkipButton.Content = Profiles.Count > 1 ? "Skip for now" : "Just me for now";
    }

    private void RebuildProfileTiles()
    {
        ProfileTiles.Clear();
        foreach (var profile in Profiles) ProfileTiles.Add(new HouseholdProfileTile(profile));
        ProfileTiles.Add(HouseholdProfileTile.AddProfile);
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (Profiles.Count == 1 && !Profiles[0].HasPin)
        {
            var profile = Profiles[0];
            _authService.SelectProfile(profile.Id, profile: profile);
            var settings = _settingsService.Load();
            settings.LastProfileId = profile.Id;
            _settingsService.Save(settings);
            Exception? failure = null;
            if (App.MainWindowInstance?.TryEnterAuthenticatedPage(typeof(HomePage), null, out failure) == true)
                return;
            ShowError(failure?.Message ?? "Unable to open the home page.");
            return;
        }

        App.Services.GetRequiredService<NavigationService>().Navigate<ProfileSelectPage>();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}

public sealed class HouseholdProfileTile
{
    public static HouseholdProfileTile AddProfile { get; } = new(null);

    public HouseholdProfileTile(Profile? profile) => Profile = profile;

    public Profile? Profile { get; }
    public bool IsAdd => Profile == null;
    public string DisplayName => Profile?.Name ?? "Add profile";
    public string AccessibleName => Profile?.EditProfileAccessibleName ?? "Add profile";
    public Windows.UI.Text.FontWeight LabelWeight => IsAdd
        ? Microsoft.UI.Text.FontWeights.Medium
        : Microsoft.UI.Text.FontWeights.SemiBold;
    public Brush LabelBrush => IsAdd
        ? (Brush)Application.Current.Resources["SecondaryTextBrush"]
        : (Brush)Application.Current.Resources["PrimaryTextBrush"];
}
