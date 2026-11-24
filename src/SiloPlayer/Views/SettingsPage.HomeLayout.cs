using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private string CurrentHomePageLabel() => ViewModel.SelectedScope == "home" ? L("web.home.rows.pages.home", "Home")
        : ViewModel.LibraryCards.FirstOrDefault(card => $"library:{card.LibraryId}" == ViewModel.SelectedScope)?.LibraryName ?? L("web.home.addRow.pages.thisPage", "this page");

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
                        // A row of collections peeks at its collections' posters (official fetchRowPreview).
                        items = response.Section is { HoldsCollections: true } held
                            ? held.Collections.Take(3).Select(card => new MediaItem { ContentId = "collection:" + card.Key, Title = card.Title, PosterUrl = card.PosterUrl }).ToList()
                            : response.Section?.Items.Take(3).ToList() ?? [];
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
            Content = row.IsCustom ? $"Delete “{row.DisplayTitle}” from this profile?" : $"Remove “{row.DisplayTitle}” from this profile's page? You can bring it back by resetting the page.",
            PrimaryButtonText = row.IsCustom ? L("web.home.rows.deleteRow", "Delete row") : L("web.home.rows.remove.confirm", "Remove row"), CloseButtonText = L("common.cancel", "Cancel"), XamlRoot = XamlRoot,
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
        var export = new Button { Content = L("web.settings.homeScreen.export", "Export layout"), HorizontalAlignment = HorizontalAlignment.Left };
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
        var draft = new SettingsSectionEntry { SectionType = "collection", IsCustom = true, Title = args.Name, ItemLimit = 20, Config = new() { [args.ServerCollection ? "library_collection_id" : "user_collection_id"] = args.CollectionId } };
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
            // The web names the download silo-home-layout-YYYY-MM-DD.json, by the export's UTC date.
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "silo-home-layout-" + file.ExportedAt[..10] };
            picker.FileTypeChoices.Add("Silo Home layout", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var target = await picker.PickSaveFileAsync();
            if (target == null || !api.IsCurrentContext(context) || !_layoutTransferActive) return;
            await Windows.Storage.FileIO.WriteTextAsync(target, HomeLayoutTransfer.Serialize(file));
            App.Services.GetRequiredService<ToastService>().Success(L("web.home.layout.transfer.exported", "Home layout exported"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var reason = ex.DisplayMessage();
            App.Services.GetRequiredService<ToastService>().Error(ex, L("web.home.layout.transfer.exportFailed", $"Failed to export the home layout: {reason}", Args(("reason", reason))));
        }
        finally { TransferBusy(false); }
    }
    private async void HomeImport_Click(object sender, RoutedEventArgs e)
    {
        if (_layoutTransferBusy || ViewModel.IsSavingHomeSections) return; TransferBusy(true);
        var api = App.Services.GetRequiredService<SettingsApi>(); var context = api.CaptureContext();
        var transfer = CreateLayoutTransfer();
        try
        {
            var fileLabel = L("web.home.layout.fileLabel", "Layout file");
            var source = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 200, MinWidth = 360, FontFamily = new FontFamily("Consolas"), FontSize = 12,
                PlaceholderText = L("web.home.layout.pastePlaceholder", "Or paste the exported JSON here") };
            AutomationProperties.SetName(source, fileLabel);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            void ShowStatus(string text, bool failure)
            {
                status.Text = text; status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                status.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[failure ? "ErrorBrush" : "SecondaryTextBrush"];
            }
            var load = new Button { Content = L("web.home.layout.chooseFile", "Choose file…"), HorizontalAlignment = HorizontalAlignment.Right };
            var fileRow = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
            fileRow.Children.Add(new TextBlock { Text = fileLabel, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(load, 1); fileRow.Children.Add(load);
            var content = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = L("web.home.layout.description", "Load a layout exported from another profile or another Silo server. You'll see what changes before anything is saved."), TextWrapping = TextWrapping.Wrap },
                fileRow, source, status } };
            var input = new ContentDialog { Title = L("web.home.layout.title", "Import home layout"), Content = content, PrimaryButtonText = "Preview import", CloseButtonText = L("common.cancel", "Cancel"), XamlRoot = XamlRoot, DefaultButton = ContentDialogButton.Close };
            _layoutTransferPreview = input;
            load.Click += async (_, _) =>
            {
                try
                {
                    var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add(".json");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
                    var selected = await picker.PickSingleFileAsync(); if (selected == null || !api.IsCurrentContext(context) || !_layoutTransferActive) return;
                    if ((await selected.GetBasicPropertiesAsync()).Size > HomeLayoutTransfer.MaximumBytes) { ShowStatus(L("web.home.layout.tooLarge", "This file is too large to be a home layout export."), true); return; }
                    source.Text = await Windows.Storage.FileIO.ReadTextAsync(selected); ShowStatus("", false);
                }
                catch (Exception) { ShowStatus(L("web.home.layout.readFailed", "Couldn't read that file"), true); }
            };
            source.TextChanged += (_, _) => ShowStatus("", false);
            HomeLayoutPlan? plan = null;
            input.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral(); input.IsPrimaryButtonEnabled = false;
                try
                {
                    ShowStatus(L("web.home.layout.checking", "Checking the layout against this server…"), false);
                    plan = await transfer.PreviewAsync(source.Text);
                    if (!api.IsCurrentContext(context) || !_layoutTransferActive) args.Cancel = true; else ShowStatus("", false);
                }
                catch (InvalidDataException ex) { args.Cancel = true; ShowStatus(ex.Message, true); }
                catch (OperationCanceledException) { args.Cancel = true; ShowStatus("", false); }
                catch (Exception) { args.Cancel = true; ShowStatus(L("web.home.layout.serverDetailsFailed", "Couldn't load this server's details. Close the dialog and try again."), true); }
                finally { input.IsPrimaryButtonEnabled = true; deferral.Complete(); }
            };
            if (await input.ShowAsync() != ContentDialogResult.Primary || plan == null || !api.IsCurrentContext(context) || !_layoutTransferActive) return;
            var importLabel = L("web.home.layout.import", "Import");
            var summary = new SiloPlayer.Views.Dialogs.HomeLayoutImportSummary(plan);
            // Import applies only what the preview shows, so it follows the rows left checked.
            var preview = new ContentDialog { Title = L("web.home.layout.title", "Import home layout"), PrimaryButtonText = importLabel, CloseButtonText = L("common.cancel", "Cancel"),
                XamlRoot = XamlRoot, DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = summary.HasChanges };
            var applyError = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"] };
            preview.Content = new ScrollViewer { MaxHeight = 450, Content = new StackPanel { Spacing = 12, Children = { summary.Root, applyError } } };
            var applying = false;
            summary.Changed += () => { if (!applying) preview.IsPrimaryButtonEnabled = summary.HasChanges; };
            preview.Closing += (_, args) => { if (applying && _layoutTransferActive && api.IsCurrentContext(context)) args.Cancel = true; };
            preview.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true;
                if (applying || !_layoutTransferActive) return;
                // The plan was checked against this profile's libraries and collections.
                if (!api.IsCurrentContext(context))
                {
                    applyError.Text = L("web.home.layout.profileChanged", "The active profile changed. Close this dialog and import again."); applyError.Visibility = Visibility.Visible; return;
                }
                var deferral = args.GetDeferral(); applying = true; summary.SetEnabled(false);
                preview.IsPrimaryButtonEnabled = false; preview.PrimaryButtonText = L("web.home.layout.importing", "Importing…");
                var close = SiloPlayer.Helpers.EditorDialogPresentation.Descendants<Button>(preview).FirstOrDefault(button => button.Name == "CloseButton");
                if (close != null) close.IsEnabled = false;
                var saved = false;
                try
                {
                    await transfer.ApplyAsync(plan);
                    await ViewModel.LoadHomeSectionsCommand.ExecuteAsync(null);
                    if (!api.IsCurrentContext(context) || !_layoutTransferActive) return;
                    WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.HomeLayoutChanged, ""));
                    App.Services.GetRequiredService<ToastService>().Success(L("web.home.layout.imported", "Home layout imported"));
                    saved = true;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    // The import stops at the first step that fails, so the viewer is told what did not import; earlier pages stay saved.
                    applyError.Text = ApplyFailureText(transfer, ex);
                    applyError.Visibility = Visibility.Visible;
                    App.Services.GetRequiredService<ToastService>().Error(applyError.Text);
                }
                finally
                {
                    applying = false; preview.IsPrimaryButtonEnabled = summary.HasChanges; preview.PrimaryButtonText = importLabel; summary.SetEnabled(true);
                    if (close != null) close.IsEnabled = true;
                    deferral.Complete();
                }
                if (saved) preview.Hide();
            };
            _layoutTransferPreview = preview;
            await preview.ShowAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex, ApplyFailureText(transfer, ex)); }
        finally { _layoutTransferPreview = null; TransferBusy(false); }
    }
    // "Some parts didn't import: Movies (reason)": the page, or the Hide watched items setting, that stopped the import.
    private static string ApplyFailureText(HomeLayoutTransferService transfer, Exception ex)
    {
        if (transfer.FailedPage == null && !transfer.FailedHideWatched) return ex.DisplayMessage();
        var reason = ex.DisplayMessage();
        var failure = transfer.FailedPage is { } page
            ? L("web.home.layout.pageFailed", $"{page} ({reason})", Args(("page", page), ("error", reason)))
            : L("web.home.layout.hideWatchedFailed", $"Hide watched items ({reason})", Args(("error", reason)));
        return L("web.home.layout.partlyFailed", $"Some parts didn't import: {failure}", Args(("failures", failure)));
    }
}

