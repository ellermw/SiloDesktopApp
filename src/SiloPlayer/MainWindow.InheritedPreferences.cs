using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, CancellationTokenSource> _inheritedPreferenceRefreshes = new(StringComparer.Ordinal);

    private void InitializeInheritedPreferenceRefresh()
    {
        WeakReferenceMessenger.Default.Register<HomePreferenceChanged>(this, (recipient, message) =>
            ((MainWindow)recipient).DispatcherQueue.TryEnqueue(async () =>
            {
                if (!_apiClient.IsCurrentContext(message.Context) || !CanExposeAuthenticatedNavigation) return;
                _navigationService.InvalidateLocalizedPageCaches();
                if (ContentFrame.Content is LibraryPage library)
                {
                    try { await library.RefreshLocalizedContentAsync(); }
                    catch (Exception ex) { LocalLog.AppendException("settings_refresh.txt", ex, "home_preference_library_refresh"); }
                }
            }));
    }

    private void ReleaseInheritedPreferenceRefresh()
    {
        WeakReferenceMessenger.Default.Unregister<HomePreferenceChanged>(this);
        foreach (var owner in _inheritedPreferenceRefreshes.Values) { owner.Cancel(); owner.Dispose(); }
        _inheritedPreferenceRefreshes.Clear();
    }

    private void NotifyInheritedPreferenceFromRealtime(string channel, string eventName, System.Text.Json.JsonElement data)
    {
        if (!InheritedPreferencePolicy.TryReadRealtimeChange(channel, eventName, data, _authService.SelectedProfileId, out var change)) return;
        var context = _apiClient.CaptureContext();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_apiClient.IsCurrentContext(context) || !CanExposeAuthenticatedNavigation) return;
            if (_inheritedPreferenceRefreshes.Remove(change.Key, out var previous)) { previous.Cancel(); previous.Dispose(); }
            var owner = new CancellationTokenSource();
            _inheritedPreferenceRefreshes[change.Key] = owner;
            _ = RefreshInheritedPreferenceAsync(context, change, owner);
        });
    }

    private async Task RefreshInheritedPreferenceAsync(ApiRequestContext context, InheritedPreferenceChange change, CancellationTokenSource owner)
    {
        bool Current() => !owner.IsCancellationRequested && _apiClient.IsCurrentContext(context) && CanExposeAuthenticatedNavigation;
        async Task RefreshAsync()
        {
            if (!Current()) return;
            if (change.Key == "ui.title_art")
                App.Services.GetRequiredService<SettingsViewModel>().PublishTitleArtPreferenceChanged();
            else
            {
                WeakReferenceMessenger.Default.Send(new HomePreferenceChanged(context, ReloadLayout: change.Key == "ui.next_up_mode"));
            }
            if (Current() && ContentFrame.Content is SettingsPage settings) await settings.RefreshInheritedPreferencesAsync(change.Key);
        }
        async Task RefreshSafelyAsync()
        {
            try { await RefreshAsync(); }
            catch (OperationCanceledException) when (!Current()) { }
            catch (Exception ex) { LocalLog.AppendException("settings_refresh.txt", ex, "inherited_preference_refresh"); }
        }
        try
        {
            await RefreshSafelyAsync();
            if (change.NeedsSettlingRead)
            {
                await Task.Delay(InheritedPreferencePolicy.DefaultSettleDelay, owner.Token);
                await RefreshSafelyAsync();
            }
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch (Exception ex) { LocalLog.AppendException("settings_refresh.txt", ex, "inherited_preference_refresh"); }
        finally
        {
            if (_inheritedPreferenceRefreshes.TryGetValue(change.Key, out var current) && ReferenceEquals(current, owner))
            { _inheritedPreferenceRefreshes.Remove(change.Key); owner.Dispose(); }
        }
    }
}
