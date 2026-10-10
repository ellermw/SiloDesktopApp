using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using CommunityToolkit.Mvvm.Messaging;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private string CurrentHomePageLabel() => ViewModel.SelectedScope == "home" ? "Home"
        : ViewModel.LibraryCards.FirstOrDefault(card => $"library:{card.LibraryId}" == ViewModel.SelectedScope)?.LibraryName ?? "this page";

    private readonly SemaphoreSlim _homePeekBudget = new(3);
    private readonly Dictionary<string, List<MediaItem>> _homePeekCache = [];

    private void AttachHomeRowPeek(Grid art, SettingsSectionEntry row)
    {
        var saved = ViewModel.SavedHomeRow(row.Id);
        if (saved == null || saved.Hidden) return;
        var api = App.Services.GetRequiredService<SettingsApi>();
        var context = api.CaptureContext(); var scope = ViewModel.SelectedScope;
        var revision = ViewModel.HomeRowsRevision;
        var key = $"{context}:{scope}:{revision}:{saved.Id}:{saved.ItemLimit}:" + System.Text.Json.JsonSerializer.Serialize(saved.Config);
        CancellationTokenSource? cancellation = null;
        art.Unloaded += (_, _) => { cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; };
        art.Loaded += async (_, _) =>
        {
            if (art.Visibility == Visibility.Collapsed || !api.IsCurrentContext(context)) return;
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = new CancellationTokenSource(); var ct = cancellation.Token;
            try
            {
                await _homePeekBudget.WaitAsync(ct);
                try
                {
                    if (!api.IsCurrentContext(context) || scope != ViewModel.SelectedScope || revision != ViewModel.HomeRowsRevision) return;
                    if (!_homePeekCache.TryGetValue(key, out var items))
                    {
                        var response = scope.StartsWith("library:", StringComparison.Ordinal) && int.TryParse(scope[8..], out var id)
                            ? await App.Services.GetRequiredService<CatalogApi>().GetLibrarySectionItemsAsync(id, saved.Id, ct)
                            : await App.Services.GetRequiredService<HomeApi>().GetSectionItemsAsync(saved.Id, ct);
                        items = response.Section?.Items.Take(3).ToList() ?? [];
                        if (_homePeekCache.Count > 128) _homePeekCache.Clear();
                        _homePeekCache[key] = items;
                    }
                    var posters = new List<Image>();
                    foreach (var item in items.Where(item => !string.IsNullOrEmpty(item.PosterUrl)))
                    {
                        var path = await App.Services.GetRequiredService<ImageService>().GetImageDiskPathAsync(
                            item.ContentId, "poster", item.PosterUrl!, App.Services.GetRequiredService<HttpClient>(), ct);
                        if (path == null) continue;
                        posters.Add(new Image { Width = 36, Height = 54, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path)) { DecodePixelWidth = 72 } });
                        AutomationProperties.SetName(posters[^1], item.Title);
                    }
                    if (ct.IsCancellationRequested || !art.IsLoaded || !api.IsCurrentContext(context) || scope != ViewModel.SelectedScope || revision != ViewModel.HomeRowsRevision) return;
                    if (posters.Count == 0) return;
                    art.Children.Clear();
                    for (var index = 0; index < posters.Count; index++)
                    {
                        posters[index].HorizontalAlignment = HorizontalAlignment.Left;
                        posters[index].Margin = new Thickness(index * 16, 0, 0, 0); art.Children.Add(posters[index]);
                    }
                }
                finally { _homePeekBudget.Release(); }
            }
            catch (OperationCanceledException) { }
            catch { /* A failed poster peek keeps the row's icon and all editing controls. */ }
        };
    }

    private async Task RemoveHomeRowAsync(SettingsSectionEntry row)
    {
        if (!ViewModel.CanEditHomeSections) return;
        var api = App.Services.GetRequiredService<SettingsApi>(); var context = api.CaptureContext(); var scope = ViewModel.SelectedScope;
        var dialog = new ContentDialog
        {
            Title = row.IsCustom ? "Delete row?" : "Remove from my page?",
            Content = row.IsCustom ? $"Delete “{row.Title}” from this profile?" : $"Remove “{row.Title}” from this profile's page? You can bring it back by resetting the page.",
            PrimaryButtonText = row.IsCustom ? "Delete row" : "Remove row", CloseButtonText = "Cancel", XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close, PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"]
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || !_layoutTransferActive || !api.IsCurrentContext(context) || scope != ViewModel.SelectedScope || !ViewModel.CanEditHomeSections) return;
        var current = ViewModel.HomeSections.FirstOrDefault(section => section.Id == row.Id); if (current == null) return;
        ViewModel.RemoveSection(current); await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(row.Id);
    }

    private async void HomeRowsReload_Click(object sender, RoutedEventArgs args)
        => await ViewModel.LoadHomeSectionsCommand.ExecuteAsync(null);

    private FrameworkElement? _customizeHomeContent;
    private TextBlock? _customizeHomeHeading;
    private TextBlock? _customizeHomeCopy;
    private int _customizeHomeGeneration;
    private int _collectionRowEditorGeneration;
    private static string CustomizeScope(int? libraryId) => libraryId is { } id
        ? id > 0 ? $"library:{id.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : throw new ArgumentOutOfRangeException(nameof(libraryId))
        : "home";
    public async Task<FrameworkElement> CreateCustomizeHomeContentAsync(int? libraryId = null)
    {
        var api = App.Services.GetRequiredService<SettingsApi>(); var context = api.CaptureContext();
        var generation = ++_customizeHomeGeneration;
        var scope = CustomizeScope(libraryId);
        _layoutTransferActive = true;
        await ViewModel.WaitForHomeRowsWritesAsync();
        if (!_layoutTransferActive || generation != _customizeHomeGeneration || !api.IsCurrentContext(context)) throw new OperationCanceledException("The selected page changed.");
        ViewModel.SelectedScope = scope;
        await ViewModel.LoadHomeSectionsCommand.ExecuteAsync(null);
        if (!_layoutTransferActive || generation != _customizeHomeGeneration || !api.IsCurrentContext(context) || ViewModel.SelectedScope != scope) throw new OperationCanceledException("The selected page changed.");
        RebuildHomeSectionItems();
        var heading = libraryId.HasValue ? "Customize library" : "Customize home";
        var copy = libraryId.HasValue ? "Arrange the rows on this library page for your profile." : "Arrange the rows on your profile's Home screen.";
        if (_customizeHomeContent != null)
        {
            _customizeHomeHeading!.Text = heading; _customizeHomeCopy!.Text = copy;
            return _customizeHomeContent;
        }
        var sections = HomeRowsGroup;
        ((StackPanel)HomeScreenPanel.Content).Children.Remove(sections);
        var content = new StackPanel { MaxWidth = 768, Spacing = 20, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(_customizeHomeHeading = new TextBlock { Text = heading, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(_customizeHomeCopy = new TextBlock { Text = copy, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(sections);
        var export = new Button { Content = "Export layout", HorizontalAlignment = HorizontalAlignment.Left };
        export.Click += HomeExport_Click; content.Children.Add(export);
        return _customizeHomeContent = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    public void DeactivateCustomizeHome() => DeactivateLayoutTransfer();
    internal async Task OpenCollectionRowEditorAsync(CustomizeHomeNavigationArgs args)
    {
        var api = App.Services.GetRequiredService<SettingsApi>(); var context = api.CaptureContext();
        var generation = ++_collectionRowEditorGeneration;
        var scope = CustomizeScope(args.LibraryId);
        if (ViewModel.SelectedScope != scope || ViewModel.HomeRowsRevision == 0)
            await CreateCustomizeHomeContentAsync(args.LibraryId);
        bool Current() => _layoutTransferActive && generation == _collectionRowEditorGeneration && api.IsCurrentContext(context)
            && ViewModel.SelectedScope == scope && ViewModel.HomeRowsLoadedForCurrentScope;
        if (!Current()) return;
        var catalog = await LoadAllowedRecipeCatalogAsync();
        if (!Current()) return;
        var draft = new SettingsSectionEntry { SectionType = "collection", IsCustom = true, Title = args.Name, ItemLimit = 20, Config = new() { ["user_collection_id"] = args.CollectionId } };
        var configured = await SiloPlayer.Views.Dialogs.RecipeGalleryDialog.ShowEditorAsync(XamlRoot, catalog, draft, CurrentHomePageLabel());
        if (configured == null || !Current()) return;
        ViewModel.AddHomeSection(new SettingsSectionEntry
        { Id = Guid.NewGuid().ToString(), SectionType = configured.SectionType, Title = configured.Title, Featured = configured.Featured, ItemLimit = configured.ItemLimit,
          Hidden = false, IsCustom = true, Customized = true, Position = ViewModel.HomeSections.Count, Config = configured.Config });
        await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
    }
    private bool _layoutTransferBusy;
    private bool _layoutTransferActive = true;
    private ContentDialog? _layoutTransferPreview;
    private void DeactivateLayoutTransfer()
    { _layoutTransferActive = false; ++_customizeHomeGeneration; ++_collectionRowEditorGeneration; _layoutTransferPreview?.Hide(); }
    private HomeLayoutTransferService CreateLayoutTransfer() => new(App.Services.GetRequiredService<SettingsApi>(),
        App.Services.GetRequiredService<CatalogApi>(), App.Services.GetRequiredService<CollectionsApi>(),
        App.Services.GetRequiredService<AuthApi>());
    private async Task<RecipeCatalogResponse> LoadAllowedRecipeCatalogAsync()
    {
        var api = App.Services.GetRequiredService<SettingsApi>(); var context = api.CaptureContext();
        var catalog = await api.GetRecipeCatalogAsync();
        if (!api.IsCurrentContext(context)) throw new OperationCanceledException("The selected profile changed.");
        // The current profile adapter permits rule rows for every profile;
        // the server remains authoritative when the override set is saved.
        catalog.AllowAdminOnlyRecipes = true;
        return catalog;
    }
    private void TransferBusy(bool busy)
    { _layoutTransferBusy = busy; HomeScreenPanel.IsEnabled = !busy; if (_customizeHomeContent != null) SetSettingsEnabled(_customizeHomeContent, !busy); HomeExportButton.IsEnabled = HomeImportButton.IsEnabled = !busy; }
    private async void HomeExport_Click(object sender, RoutedEventArgs e)
    {
        if (_layoutTransferBusy || ViewModel.IsSavingHomeSections) return; TransferBusy(true);
        var api = App.Services.GetRequiredService<SettingsApi>(); var context = api.CaptureContext();
        try
        {
            var file = await CreateLayoutTransfer().ExportAsync();
            if (!_layoutTransferActive || !api.IsCurrentContext(context)) return;
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "Silo-Home-Layout.json" };
            picker.FileTypeChoices.Add("Silo Home layout", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var target = await picker.PickSaveFileAsync();
            if (target == null || !api.IsCurrentContext(context) || !_layoutTransferActive) return;
            await Windows.Storage.FileIO.WriteTextAsync(target, HomeLayoutTransfer.Serialize(file));
            App.Services.GetRequiredService<ToastService>().Success("Layout exported");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error($"Could not export layout: {ex.Message}"); }
        finally { TransferBusy(false); }
    }
    private async void HomeImport_Click(object sender, RoutedEventArgs e)
    {
        if (_layoutTransferBusy || ViewModel.IsSavingHomeSections) return; TransferBusy(true);
        var api = App.Services.GetRequiredService<SettingsApi>(); var context = api.CaptureContext();
        var transfer = CreateLayoutTransfer();
        try
        {
            var source = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 260, MinWidth = 360, PlaceholderText = "Paste exported Home layout JSON" };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var load = new Button { Content = "Load JSON file", HorizontalAlignment = HorizontalAlignment.Left };
            var content = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = "Paste a layout or load an exported JSON file. Review the mapped scopes before applying.", TextWrapping = TextWrapping.Wrap }, source, load, error } };
            var input = new ContentDialog { Title = "Import Home layout", Content = content, PrimaryButtonText = "Preview import", CloseButtonText = "Cancel", XamlRoot = XamlRoot, DefaultButton = ContentDialogButton.Close };
            _layoutTransferPreview = input;
            load.Click += async (_, _) =>
            {
                try
                {
                    var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add(".json");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
                    var selected = await picker.PickSingleFileAsync(); if (selected == null || !api.IsCurrentContext(context) || !_layoutTransferActive) return;
                    if ((await selected.GetBasicPropertiesAsync()).Size > HomeLayoutTransfer.MaximumBytes) throw new InvalidDataException("Layout exceeds the 5 MiB limit.");
                    source.Text = await Windows.Storage.FileIO.ReadTextAsync(selected);
                }
                catch (Exception ex) { error.Text = ex.Message; }
            };
            HomeLayoutPlan? plan = null;
            input.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral(); input.IsPrimaryButtonEnabled = false;
                try
                {
                    plan = await transfer.PreviewAsync(source.Text);
                    if (!api.IsCurrentContext(context) || !_layoutTransferActive) args.Cancel = true;
                }
                catch (Exception ex) { args.Cancel = true; error.Text = ex.Message; }
                finally { input.IsPrimaryButtonEnabled = true; deferral.Complete(); }
            };
            if (await input.ShowAsync() != ContentDialogResult.Primary || plan == null || !api.IsCurrentContext(context) || !_layoutTransferActive) return;
            var descriptions = plan.Pages.Select(page => $"{(page.Scope == "home" ? "Home" : $"Library {page.LibraryId}")}: replace saved overrides with {page.Overrides.Count} sections").ToList();
            if (plan.HideWatchedItems.HasValue) descriptions.Add($"Hide watched items: {(plan.HideWatchedItems.Value ? "On" : "Off")}");
            descriptions.Add("Saved legacy Trakt rows are preserved. Imports from another server also preserve this server's section hides and ordering.");
            if (plan.Skipped.Count > 0) descriptions.Add("Skipped:\n" + string.Join("\n", plan.Skipped));
            var preview = new ContentDialog { Title = "Import layout preview", PrimaryButtonText = "Apply layout", CloseButtonText = "Cancel",
                XamlRoot = XamlRoot, DefaultButton = ContentDialogButton.Close,
                IsPrimaryButtonEnabled = plan.Pages.Count > 0 || plan.HideWatchedItems.HasValue };
            var applyError = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"] };
            preview.Content = new ScrollViewer { MaxHeight = 450, Content = new StackPanel { Spacing = 12,
                Children = { new TextBlock { Text = string.Join("\n\n", descriptions), TextWrapping = TextWrapping.Wrap }, applyError } } };
            var applying = false;
            preview.Closing += (_, args) => { if (applying && _layoutTransferActive && api.IsCurrentContext(context)) args.Cancel = true; };
            preview.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true;
                if (applying || !api.IsCurrentContext(context) || !_layoutTransferActive) return;
                var deferral = args.GetDeferral(); applying = true;
                preview.IsPrimaryButtonEnabled = false; preview.PrimaryButtonText = "Importing…";
                var close = SiloPlayer.Helpers.EditorDialogPresentation.Descendants<Button>(preview).FirstOrDefault(button => button.Name == "CloseButton");
                if (close != null) close.IsEnabled = false;
                var saved = false;
                try
                {
                    await transfer.ApplyAsync(plan);
                    await ViewModel.LoadHomeSectionsCommand.ExecuteAsync(null);
                    if (!api.IsCurrentContext(context) || !_layoutTransferActive) return;
                    WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.HomeLayoutChanged, ""));
                    App.Services.GetRequiredService<ToastService>().Success(transfer.KeptSavedChanges
                        ? "Layout imported; saved server-section changes were preserved" : "Layout imported");
                    saved = true;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    applyError.Text = $"Applied {transfer.AppliedPages} layout pages. Import stopped: {ex.Message}";
                    applyError.Visibility = Visibility.Visible;
                    App.Services.GetRequiredService<ToastService>().Error(applyError.Text);
                }
                finally
                {
                    applying = false; preview.IsPrimaryButtonEnabled = true; preview.PrimaryButtonText = "Apply layout";
                    if (close != null) close.IsEnabled = true;
                    deferral.Complete();
                }
                if (saved) preview.Hide();
            };
            _layoutTransferPreview = preview;
            await preview.ShowAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error($"Applied {transfer.AppliedPages} layout pages. Import stopped: {ex.Message}"); }
        finally { _layoutTransferPreview = null; TransferBusy(false); }
    }
}

