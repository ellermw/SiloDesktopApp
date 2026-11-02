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

    private void ConfigureOverviewIfNeeded()
    {
        if (ViewModel.Item is not { } item || !IsCurrentDetail(item.ContentId)) return;
        var api = App.Services.GetRequiredService<CatalogApi>();
        var owner = new OverviewConfigurationOwner(item, ViewModel.LibraryId, api.CaptureContext(), api.LocalizedReadGeneration, _navigationCts);
        if (owner != _overviewConfiguredOwner) _ = ConfigureOnViewTranslationAsync(item);
    }

    private void RenderOverview()
    {
        OverviewText.Text = ViewModel.OverviewSource?.Overview ?? "";
        ApplyEpisodeSpoilers();
    }

    private bool IsOverviewHidden(MediaItemDetail owner, MediaItemDetail source) =>
        ReferenceEquals(owner, source) && owner.Type == "episode" && _episodeSpoilerPreferences?.HideOverviews == true &&
        EpisodeSpoilerPolicy.IsUnwatched(owner.UserData?.Played, owner.UserData?.IsInProgress, owner.UserData?.PositionSeconds) &&
        _revealedEpisodeOverviewId != owner.ContentId;

    private async Task ConfigureOnViewTranslationAsync(MediaItemDetail? item)
    {
        if (item == null || !ReferenceEquals(item, ViewModel.Item) || !IsCurrentDetail(item.ContentId)) return;
        var revision = ++_overviewConfigurationRevision;
        _translationCts?.Cancel();
        _translateButtonMode = false;
        TranslateOverviewButton.Visibility = Visibility.Collapsed;
        OverviewText.Opacity = 1;
        var api = App.Services.GetRequiredService<CatalogApi>();
        var context = api.CaptureContext();
        var language = api.LocalizedReadGeneration;
        var library = ViewModel.LibraryId;
        var navigation = _navigationCts;
        _overviewConfiguredOwner = new(item, library, context, language, navigation);
        bool Current() => revision == _overviewConfigurationRevision && ReferenceEquals(navigation, _navigationCts) &&
            ReferenceEquals(item, ViewModel.Item) && IsCurrentDetail(item.ContentId) && ViewModel.LibraryId == library &&
            api.IsCurrentContext(context) && api.LocalizedReadGeneration == language;
        try
        {
            var source = await ViewModel.ResolveOverviewAsync(navigation?.Token ?? default);
            if (!Current()) return;
            RenderOverview();
            if (source == null || string.IsNullOrWhiteSpace(source.PendingTranslationLanguage) || IsOverviewHidden(item, source)) return;
            var status = await api.GetMetadataAiStatusAsync(navigation?.Token ?? default);
            if (!Current() || !ReferenceEquals(source, ViewModel.OverviewSource) || !status.Enabled || IsOverviewHidden(item, source)) return;
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
    }

    private async Task TranslateOverviewAsync(MediaItemDetail source)
    {
        if (_translationCts is { IsCancellationRequested: false }) return;
        var item = ViewModel.Item;
        if (item == null || !ReferenceEquals(source, ViewModel.OverviewSource) || string.IsNullOrWhiteSpace(source.PendingTranslationLanguage) ||
            !IsCurrentDetail(item.ContentId) || IsOverviewHidden(item, source)) return;
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
        bool OwnerCurrent() => ReferenceEquals(_translationCts, cts) &&
            revision == _overviewConfigurationRevision && ReferenceEquals(navigation, _navigationCts) &&
            ReferenceEquals(item, ViewModel.Item) && IsCurrentDetail(item.ContentId) && ViewModel.LibraryId == library &&
            ReferenceEquals(source, ViewModel.OverviewSource) && api.IsCurrentContext(context) &&
            api.LocalizedReadGeneration == language && !IsOverviewHidden(item, source);
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
            // An old completion must never restore a newer item's action or opacity.
            if (OwnerCurrent())
            {
                OverviewText.Opacity = 1;
                TranslateOverviewButton.Visibility = _translateButtonMode && !string.IsNullOrWhiteSpace(source.PendingTranslationLanguage)
                    ? Visibility.Visible : Visibility.Collapsed;
                TranslateOverviewButton.IsEnabled = true;
                TranslateOverviewButton.Content = "Translate description";
            }
            if (ReferenceEquals(_translationCts, cts)) _translationCts = null;
        }
    }
}
