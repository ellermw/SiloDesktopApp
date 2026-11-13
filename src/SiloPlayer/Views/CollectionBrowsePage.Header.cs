using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

/// <summary>
/// A collection page's header in the homepage hero's style (pinned WebUI <c>CollectionPageHero</c>): the backdrop behind the
/// title and the filter bar, a Collection eyebrow, the description, a read-only byline, and the actions the viewer may use.
/// Without a backdrop it is the same layout over the plain page.
/// </summary>
public sealed partial class CollectionBrowsePage
{
    private CancellationTokenSource? _headerCts;
    private CollectionPageHeader _header = CollectionPageHeader.Empty;
    private Microsoft.UI.Composition.SpriteVisual? _collectionTitleShadow;
    private Microsoft.UI.Composition.DropShadow? _collectionTextShadow;
    private bool _heroEffectsInitialized;

    private void InitializeHeroEffects()
    {
        if (_heroEffectsInitialized) return;
        _heroEffectsInitialized = true;
        foreach (var button in new[] { CollectionEditButton, CollectionAddToHomeButton, CollectionMoreButton })
        {
            button.Background = new SiloPlayer.Controls.CollectionGlassBrush();
            button.Template = (ControlTemplate)Resources["CollectionGlassButtonTemplate"];
        }
        TitleText.SizeChanged += (_, _) => UpdateCollectionTitleShadow();
        TitleText.Loaded += (_, _) => UpdateCollectionTitleShadow();
        TitleText.Unloaded += (_, _) => ClearCollectionTitleShadow();
    }

    private void ClearCollectionTitleShadow()
    {
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(CollectionTitleShadow, null);
        _collectionTitleShadow?.Dispose(); _collectionTitleShadow = null;
        _collectionTextShadow?.Dispose(); _collectionTextShadow = null;
    }

    private void UpdateCollectionTitleShadow()
    {
        if (!_header.HasBackdrop || !TitleText.IsLoaded || TitleText.ActualWidth <= 0 || TitleText.ActualHeight <= 0)
        {
            ClearCollectionTitleShadow();
            return;
        }
        if (_collectionTitleShadow == null)
        {
            var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TitleText).Compositor;
            _collectionTextShadow = compositor.CreateDropShadow();
            _collectionTextShadow.Color = Microsoft.UI.Colors.Black;
            _collectionTextShadow.Opacity = .45f;
            _collectionTextShadow.BlurRadius = 50;
            _collectionTextShadow.Offset = new System.Numerics.Vector3(0, 18, 0);
            _collectionTitleShadow = compositor.CreateSpriteVisual();
            _collectionTitleShadow.Shadow = _collectionTextShadow;
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(CollectionTitleShadow, _collectionTitleShadow);
        }
        _collectionTextShadow!.Mask = TitleText.GetAlphaMask();
        _collectionTitleShadow.Size = new System.Numerics.Vector2((float)TitleText.ActualWidth, (float)TitleText.ActualHeight);
    }

    private async Task LoadHeaderAsync(NavArgs args, int navigationGeneration, bool refresh = false)
    {
        _headerCts?.Cancel(); _headerCts?.Dispose();
        var owner = _headerCts = new CancellationTokenSource();
        var context = _apiClient.CaptureContext();
        // Nothing shows in these lines while the header loads, so the page never flashes a placeholder first.
        // A refresh after Sync now keeps the current header on screen until the new one arrives.
        _hero = null;
        if (!refresh) { ClearHeroActions(); ApplyHeader(CollectionPageHeader.Empty); }
        try
        {
            var header = await CollectionPageHeaderLoader.LoadAsync(_collectionsApi, _catalogApi, App.Services.GetService<AuthApi>(),
                args.CollectionId, args.IsUserCollection, args.LibraryId, App.Services.GetService<AuthService>()?.SelectedProfileId, owner.Token);
            if (owner.IsCancellationRequested || !ReferenceEquals(_headerCts, owner) || !_isNavigated || navigationGeneration != _navigationGeneration
                || _currentArgs != args || !_apiClient.IsCurrentContext(context)) return;
            _hero = new HeroScope(args, navigationGeneration, context, owner);
            ApplyHeader(header);
        }
        catch (OperationCanceledException) { }
        catch { /* An unreadable header is the plain page. */ }
    }

    private void ApplyHeader(CollectionPageHeader header)
    {
        InitializeHeroEffects();
        _header = header;
        if (header.HasBackdrop && _currentArgs is { } args) HeroBackdrop.Show(header.BackdropUrl, header.BackdropThumbhash, "collection-" + args.CollectionId);
        else HeroBackdrop.Clear();

        CollectionDescription.Text = header.Description ?? "";
        CollectionDescription.Visibility = header.Description == null ? Visibility.Collapsed : Visibility.Visible;

        ApplyByline(header);

        // The owner of a personal collection changes it; everyone else can add it to their own Home.
        CollectionEditButton.Visibility = header.Access.Kind == CollectionPageAccessKind.Manage && _currentArgs is { IsUserCollection: true } ? Visibility.Visible : Visibility.Collapsed;
        CollectionAddToHomeButton.Visibility = header.Access.Kind == CollectionPageAccessKind.ReadOnly ? Visibility.Visible : Visibility.Collapsed;
        UpdateMoreMenu(header.Access);
        UpdateHeroLayout(new Windows.Foundation.Size(ActualWidth, ActualHeight));
        UpdateCollectionTitleShadow();
    }

    /// <summary>The hero's responsive layout: title size, eyebrow, minimum height with a backdrop, and where the actions sit.</summary>
    private void UpdateHeroLayout(Windows.Foundation.Size size)
    {
        var width = size.Width > 0 ? size.Width : ActualWidth;
        var compact = width < 720;
        var backdrop = _header.HasBackdrop;
        // text-4xl / 5xl / 6xl on the hero's title; the eyebrow is hidden on a phone.
        TitleText.FontSize = width < 640 ? 36 : width < 1024 ? 48 : 60;
        TitleText.LineHeight = TitleText.FontSize * 1.05;
        CollectionEyebrow.Visibility = width < 640 ? Visibility.Collapsed : Visibility.Visible;
        // min-h-[clamp(13rem,30vh,22rem)] with the content at the bottom, only over a backdrop.
        CollectionHeaderGrid.MinHeight = backdrop ? Math.Clamp(size.Height * .30, 208, 352) : 0;
        CollectionTitleStack.VerticalAlignment = backdrop ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        Grid.SetRow(CollectionActionsPanel, compact ? 1 : 0);
        Grid.SetColumn(CollectionActionsPanel, compact ? 1 : 2);
        Grid.SetColumnSpan(CollectionActionsPanel, compact ? 2 : 1);
        CollectionActionsPanel.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private void CollectionEdit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isNavigated || _currentArgs is not { IsUserCollection: true } args) return;
        App.Services.GetRequiredService<NavigationService>().Navigate<CollectionEditorPage>(new CollectionEditorNavigationArgs { CollectionId = args.CollectionId });
    }

    private void CollectionAddToHome_Click(object sender, RoutedEventArgs e)
    {
        if (!_isNavigated || _currentArgs is not { } args) return;
        // Opens Settings > Home with Add row on this collection; nothing changes until that row is added.
        App.Services.GetRequiredService<NavigationService>().Navigate<CustomizeHomePage>(new CustomizeHomeNavigationArgs(args.CollectionId, args.Title, null, ServerCollection: !args.IsUserCollection));
    }
}
