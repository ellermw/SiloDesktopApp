using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using CommunityToolkit.Mvvm.Messaging;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private FrameworkElement? _customizeHomeContent;
    public async Task<FrameworkElement> CreateCustomizeHomeContentAsync()
    {
        _layoutTransferActive = true;
        await ViewModel.LoadHomeSectionsCommand.ExecuteAsync(null);
        RebuildHomeSectionItems();
        if (_customizeHomeContent != null) return _customizeHomeContent;
        var sections = ((StackPanel)HomeScreenPanel.Content).Children.OfType<Border>()
            .First(border => border.Child is StackPanel stack && stack.Children.OfType<TextBlock>().Any(text => text.Text == "Sections"));
        ((StackPanel)HomeScreenPanel.Content).Children.Remove(sections);
        var content = new StackPanel { MaxWidth = 768, Spacing = 20, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock { Text = "Customize home", FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Arrange the sections on your profile's Home screen.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(sections);
        var export = new Button { Content = "Export layout", HorizontalAlignment = HorizontalAlignment.Left };
        export.Click += HomeExport_Click; content.Children.Add(export);
        return _customizeHomeContent = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    public void DeactivateCustomizeHome() => DeactivateLayoutTransfer();
    private bool _layoutTransferBusy;
    private bool _layoutTransferActive = true;
    private ContentDialog? _layoutTransferPreview;
    private void DeactivateLayoutTransfer() { _layoutTransferActive = false; _layoutTransferPreview?.Hide(); }
    private HomeLayoutTransferService CreateLayoutTransfer() => new(App.Services.GetRequiredService<SettingsApi>(),
        App.Services.GetRequiredService<CatalogApi>(), App.Services.GetRequiredService<CollectionsApi>(),
        App.Services.GetRequiredService<AuthApi>(), App.Services.GetRequiredService<AuthService>());
    private async Task<RecipeCatalogResponse> LoadAllowedRecipeCatalogAsync()
    {
        var api = App.Services.GetRequiredService<SettingsApi>(); var context = api.CaptureContext();
        var catalog = await api.GetRecipeCatalogAsync(); var flags = await api.GetSectionFlagsAsync();
        if (!api.IsCurrentContext(context)) throw new OperationCanceledException("The selected profile changed.");
        catalog.AllowAdminOnlyRecipes = App.Services.GetRequiredService<AuthService>().CurrentUser?.Role == "admin" || flags.AllowProfileCustomSections;
        return catalog;
    }
    private void TransferBusy(bool busy)
    { _layoutTransferBusy = busy; HomeScreenPanel.IsEnabled = !busy; if (_customizeHomeContent != null) SetSettingsEnabled(_customizeHomeContent, !busy); HomeExportButton.IsEnabled = HomeImportButton.IsEnabled = !busy; }
    private async void HomeExport_Click(object sender, RoutedEventArgs e)
    {
        if (_layoutTransferBusy) return; TransferBusy(true);
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
        if (_layoutTransferBusy) return; TransferBusy(true);
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

