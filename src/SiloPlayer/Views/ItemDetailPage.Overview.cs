using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage
{
    private long _overviewConfigurationRevision;
    private sealed record OverviewConfigurationOwner(MediaItemDetail Item, int? Library, ApiRequestContext Context,
        long Language, CancellationTokenSource? Navigation);
    private OverviewConfigurationOwner? _overviewConfiguredOwner;
    private DateTimeOffset? _overviewRetryAt;
    private bool _overviewRetryAttempted;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _overviewScopeTimer;

    private void StartOverviewScopeWatch()
    {
        if (_overviewScopeTimer == null)
        {
            // Access-context retirement has no notification. Watch only while this page is loaded.
            _overviewScopeTimer = DispatcherQueue.CreateTimer();
            _overviewScopeTimer.Interval = TimeSpan.FromMilliseconds(500);
            _overviewScopeTimer.Tick += (_, _) => RefreshRetiredOverviewScope();
        }
        _overviewScopeTimer.Start();
        RefreshRetiredOverviewScope();
    }

    private void RefreshRetiredOverviewScope()
    {
        if (!IsLoaded || _overviewConfiguredOwner is not { } owner ||
            !ReferenceEquals(owner.Item, ViewModel.Item) || !ReferenceEquals(owner.Navigation, _navigationCts) ||
            owner.Library != ViewModel.LibraryId || !IsCurrentDetail(owner.Item.ContentId)) return;
        var api = App.Services.GetRequiredService<CatalogApi>();
        if (api.IsCurrentContext(owner.Context) && api.LocalizedReadGeneration == owner.Language)
        {
            if (_overviewRetryAt is { } retryAt && DateTimeOffset.UtcNow >= retryAt)
            {
                _overviewRetryAt = null;
                _overviewRetryAttempted = true;
                _ = ConfigureOnViewTranslationAsync(owner.Item);
            }
            return;
        }
        RenderOverview();
        ConfigureOverviewIfNeeded();
    }

    private void ConfigureOverviewIfNeeded()
    {
        if (ViewModel.Item is not { } item || !IsCurrentDetail(item.ContentId)) return;
        var api = App.Services.GetRequiredService<CatalogApi>();
        var owner = new OverviewConfigurationOwner(item, ViewModel.LibraryId, api.CaptureContext(), api.LocalizedReadGeneration, _navigationCts);
        if (owner != _overviewConfiguredOwner) _ = ConfigureOnViewTranslationAsync(item);
    }

    private void RenderOverview()
    {
        var text = ViewModel.OverviewSource?.Overview ?? "";
        if (OverviewText.Text != text) OverviewText.Text = text;
        ApplyOverviewSpoilers();
    }

    private void ApplyOverviewSpoilers()
    {
        if (ViewModel.Item is not { } item) return;
        var source = ViewModel.OverviewSource;
        if (source == null && OverviewText.Text != "") OverviewText.Text = "";
        var hidden = IsOverviewHidden(item);
        OverviewText.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        RevealEpisodeOverviewButton.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
        OverviewTranslationLabel.Visibility = !hidden && !string.IsNullOrWhiteSpace(source?.Overview) &&
            MachineTranslationPresentation.HasField(source?.MachineTranslatedFields) ? Visibility.Visible : Visibility.Collapsed;
        if (hidden || source == null) TranslateOverviewButton.Visibility = Visibility.Collapsed;
    }

    private bool IsOverviewHidden(MediaItemDetail owner) =>
        owner.Type == "episode" && !string.IsNullOrWhiteSpace(owner.Overview) && _episodeSpoilerPreferences?.HideOverviews == true &&
        EpisodeSpoilerPolicy.IsUnwatched(owner.UserData?.Played, owner.UserData?.IsInProgress, owner.UserData?.PositionSeconds) &&
        _revealedEpisodeOverviewId != owner.ContentId;

    private async Task ConfigureOnViewTranslationAsync(MediaItemDetail? item)
    {
        if (item == null || !ReferenceEquals(item, ViewModel.Item) || !IsCurrentDetail(item.ContentId)) return;
        var revision = ++_overviewConfigurationRevision;
        _translationCts?.Cancel();
        _translateButtonMode = false;
        TranslateOverviewButton.Visibility = Visibility.Collapsed;
        TranslateOverviewButton.IsEnabled = true;
        TranslateOverviewButton.Content = "Translate description";
        OverviewText.Opacity = 1;
        var api = App.Services.GetRequiredService<CatalogApi>();
        var context = api.CaptureContext();
        var language = api.LocalizedReadGeneration;
        var library = ViewModel.LibraryId;
        var navigation = _navigationCts;
        var owner = new OverviewConfigurationOwner(item, library, context, language, navigation);
        if (owner != _overviewConfiguredOwner) _overviewRetryAttempted = false;
        _overviewRetryAt = null;
        _overviewConfiguredOwner = owner;
        bool Current() => revision == _overviewConfigurationRevision && ReferenceEquals(navigation, _navigationCts) &&
            ReferenceEquals(item, ViewModel.Item) && IsCurrentDetail(item.ContentId) && ViewModel.LibraryId == library &&
            api.IsCurrentContext(context) && api.LocalizedReadGeneration == language;
        try
        {
            var source = await ViewModel.ResolveOverviewAsync(navigation?.Token ?? default);
            if (!Current()) return;
            RenderOverview();
            // One backoff retry per owner recovers transient optional reads without a request loop.
            if (ViewModel.OverviewResolutionFailed && !_overviewRetryAttempted)
                _overviewRetryAt = DateTimeOffset.UtcNow.AddSeconds(1);
            if (source == null || string.IsNullOrWhiteSpace(source.PendingTranslationLanguage) || IsOverviewHidden(item)) return;
            var status = await api.GetMetadataAiStatusAsync(navigation?.Token ?? default);
            if (!Current() || !ReferenceEquals(source, ViewModel.OverviewSource) || !status.Enabled || IsOverviewHidden(item)) return;
            if (status.OnView.Equals("button", StringComparison.OrdinalIgnoreCase))
            {
                _translateButtonMode = true;
                TranslateOverviewButton.Visibility = Visibility.Visible;
                TranslateOverviewButton.IsEnabled = true;
                TranslateOverviewButton.Content = "Translate description";
            }
            else if (status.OnView.Equals("auto", StringComparison.OrdinalIgnoreCase))
                await TranslateOverviewAsync(source);
        }
        catch
        {
            if (Current()) TranslateOverviewButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void TranslateOverviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.OverviewSource is { } source) await TranslateOverviewAsync(source);
        else
        {
            RenderOverview();
            ConfigureOverviewIfNeeded();
        }
    }

    private async Task TranslateOverviewAsync(MediaItemDetail source)
    {
        if (_translationCts is { IsCancellationRequested: false }) return;
        var item = ViewModel.Item;
        if (item == null || !ReferenceEquals(source, ViewModel.OverviewSource) || string.IsNullOrWhiteSpace(source.PendingTranslationLanguage) ||
            !IsCurrentDetail(item.ContentId) || IsOverviewHidden(item)) return;
        var api = App.Services.GetRequiredService<CatalogApi>();
        var context = api.CaptureContext();
        var language = api.LocalizedReadGeneration;
        var library = ViewModel.LibraryId;
        var navigation = _navigationCts;
        var revision = _overviewConfigurationRevision;
        _translationCts?.Cancel();
        _translationCts?.Dispose();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(navigation?.Token ?? default);
        _translationCts = cts;
        cts.CancelAfter(TimeSpan.FromSeconds(45));
        var ct = cts.Token;
        var targetLanguage = source.PendingTranslationLanguage;
        bool ControlsCurrent() => ReferenceEquals(_translationCts, cts) &&
            revision == _overviewConfigurationRevision && ReferenceEquals(navigation, _navigationCts) &&
            ReferenceEquals(item, ViewModel.Item) && IsCurrentDetail(item.ContentId) && ViewModel.LibraryId == library;
        bool OwnerCurrent() => ControlsCurrent() &&
            ReferenceEquals(source, ViewModel.OverviewSource) && api.IsCurrentContext(context) &&
            api.LocalizedReadGeneration == language && !IsOverviewHidden(item);
        bool Current() => !ct.IsCancellationRequested && OwnerCurrent();
        TranslateOverviewButton.Visibility = Visibility.Visible;
        TranslateOverviewButton.IsEnabled = false;
        TranslateOverviewButton.Content = "Translating…";
        OverviewText.Opacity = 0.55;
        try
        {
            if (!Current()) return;
            await api.TranslateItemDescriptionAsync(source.ContentId, targetLanguage, ct);
            while (Current())
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                if (!Current()) return;
                await ViewModel.RefreshOverviewAsync(source, ct);
                if (!Current()) return;
                RenderOverview();
                if (string.IsNullOrWhiteSpace(source.PendingTranslationLanguage)) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (Current()) App.Services.GetRequiredService<Services.ToastService>().Error(ex);
        }
        finally
        {
            // Retired results cannot publish, but must release controls unless a newer owner took them.
            var restoreControls = ControlsCurrent();
            if (restoreControls)
            {
                OverviewText.Opacity = 1;
                TranslateOverviewButton.Visibility = _translateButtonMode && !IsOverviewHidden(item) &&
                    !string.IsNullOrWhiteSpace(ViewModel.OverviewSource?.PendingTranslationLanguage)
                    ? Visibility.Visible : Visibility.Collapsed;
                TranslateOverviewButton.IsEnabled = true;
                TranslateOverviewButton.Content = "Translate description";
            }
            if (ReferenceEquals(_translationCts, cts)) _translationCts = null;
            if (restoreControls)
            {
                RenderOverview();
                ConfigureOverviewIfNeeded();
            }
        }
    }
}
