using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;
using Windows.Graphics.Imaging;
using Windows.Storage;

// Shares only the enclosing fixture's strictly fake service scope and Window.
internal static class AccountAdvancedNativeFixture
{
    internal static async Task RunAsync(Frame frame, SettingsPage page, SettingsViewModel vm, State wire)
    {
        var selected = Environment.GetEnvironmentVariable("SILO_ACCOUNT_ADVANCED_CASE");
        if (selected is null or "devices") await DevicesAsync(frame, page, wire);
        if (selected is null or "profile") await ProfileAsync(frame, wire);
        if (selected is null or "tour") await TourAsync(frame, wire);
        if (selected is null or "recipe") await RecipeAsync(frame);
        if (selected is null or "plugins") await PluginsAsync(frame, page, vm);
        if (selected is null or "overlays") await OverlaysAsync(frame, page, wire);
        if (selected is null or "languages") await LanguagesAsync(vm, wire);
        if (selected is null or "home-import") await HomeImportAsync(frame, page, vm, wire);
        if (selected is null or "home-save") await HomeSaveAsync(frame, page, vm, wire);
        if (selected is null or "profile-layout") await ProfileLayoutAsync(frame);
    }
    private static async Task ProfileLayoutAsync(Frame frame)
    {
        frame.Width = 900; await Layout(frame);
        var editor = typeof(ProfileEditorDialog).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single()
            .Invoke([frame.XamlRoot, new Profile { Id = "fixture", Name = "Primary", IsPrimary = true }, Array.Empty<Library>(), false, App.Services.GetRequiredService<AuthApi>(), true, false]);
        var dialog = (ContentDialog)Read(editor, "_dialog")!;
        foreach (var button in ((Grid)Read(editor, "_avatarPresetGrid")!).Children.OfType<Button>())
            if (button.Content is Image image) image.Source = null;
        var showing = dialog.ShowAsync(); await Until(() => All<Button>(dialog).Any(button => button.Name == "CloseButton"));
        var styleGrid = (Grid)Read(editor, "_avatarStyleGrid")!; var presets = (Grid)Read(editor, "_avatarPresetGrid")!;
        foreach (var width in new[] { 900, 460 })
        {
            frame.Width = width; await Layout(frame);
            var client = ((Window)frame.Tag).AppWindow.ClientSize;
            Program.Log($"TRACE Profile responsive requested={width}x740, WindowClient={client.Width}x{client.Height}, Frame={frame.ActualWidth}x{frame.ActualHeight}, FrameXamlRoot={frame.XamlRoot.Size.Width}x{frame.XamlRoot.Size.Height}, DialogXamlRoot={dialog.XamlRoot.Size.Width}x{dialog.XamlRoot.Size.Height}, DialogRender={dialog.ActualWidth}x{dialog.ActualHeight}, styles={styleGrid.ColumnDefinitions.Count}, presets={presets.ColumnDefinitions.Count}");
            Check(client.Width == width && client.Height == 740 && Math.Abs(frame.ActualWidth - width) < 1 && Math.Abs(frame.XamlRoot.Size.Width - width) < 1 && Math.Abs(dialog.XamlRoot.Size.Width - width) < 1,
                "Profile-layout fixture is not operating in the exact requested Window/XamlRoot viewport");
            await Capture(dialog, $"account-profile-editor-matched-{width}.png");
            var shell = All<Border>(dialog).Single(border => border.Name == "BackgroundElement");
            Program.Log($"TRACE Profile actual shell={shell.ActualWidth}x{shell.ActualHeight}, width={shell.Width}, maxHeight={shell.MaxHeight}");
            Check(Math.Abs(shell.ActualWidth - Math.Min(768, width - 32)) < 1 && shell.ActualHeight <= 676 && shell.CornerRadius.TopLeft == 12,
                "Profile editor shell does not fill the pinned768/viewport-minus32 width or respect the676px client height cap");
            var primaryCommand = All<Button>(dialog).Single(button => button.Name == "PrimaryButton");
            var cancelCommand = All<Button>(dialog).Single(button => button.Name == "CloseButton");
            var primaryOrigin = primaryCommand.TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point());
            var cancelOrigin = cancelCommand.TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point());
            Program.Log($"TRACE Profile footer primary={primaryOrigin.X},{primaryOrigin.Y}/{primaryCommand.ActualWidth}x{primaryCommand.ActualHeight}, cancel={cancelOrigin.X},{cancelOrigin.Y}/{cancelCommand.ActualWidth}x{cancelCommand.ActualHeight}");
            Check(primaryCommand.ActualHeight == 36 && cancelCommand.ActualHeight == 36 && primaryCommand.CornerRadius.TopLeft == 10 && cancelCommand.CornerRadius.TopLeft == 10 &&
                (width < 640 ? Math.Abs(cancelOrigin.Y - primaryOrigin.Y - 44) < 1 && Math.Abs(cancelCommand.ActualWidth - primaryCommand.ActualWidth) < 1 && primaryCommand.ActualWidth > 300
                    : Math.Abs(cancelOrigin.Y - primaryOrigin.Y) < 1 && cancelOrigin.X < primaryOrigin.X),
                "Profile real commands do not stack Save aboveCancel with8px gap on narrow, or preserve Cancel/Save order on wide");
            Check(styleGrid.ColumnDefinitions.Count == (width < 640 ? 1 : 2) && presets.ColumnDefinitions.Count == (width < 640 ? 3 : 4),
                "Profile preset styles/options use content-width columns instead of the pinned viewport breakpoints");
            var previewCard = All<Border>(dialog).Single(border => border.Name == "ProfileAvatarPreviewCard");
            var fallback = (TextBlock)Read(editor, "_avatarFallback")!;
            var profileSection = All<Border>(dialog).Single(border => border.Name == "ProfileEditorProfileSection");
            var titleLines = ((StackPanel)dialog.Title).Children.OfType<TextBlock>().ToArray();
            var ring = All<Border>(dialog).Single(border => border.Name == "ProfileAvatarPreviewRing");
            var previewOrigin = previewCard.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point());
            var accent = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            Program.Log($"TRACE Profile preview={previewCard.ActualWidth}x{previewCard.ActualHeight}, radius={previewCard.CornerRadius.TopLeft}, fallbackFont={fallback.FontSize}, fallbackColor={((SolidColorBrush)fallback.Foreground).Color}");
            Check(Math.Abs(previewCard.ActualWidth - (profileSection.ActualWidth - 34)) < 1 && Math.Abs(previewCard.ActualHeight - 178) < 2 && previewCard.CornerRadius.TopLeft == 16 && previewCard.BorderThickness.Left == 1 &&
                fallback.FontSize == 30 && ((SolidColorBrush)fallback.Foreground).Color == accent.Color,
                "Profile preview card/ring/initial does not match the pinned presentation");
            Check(titleLines[0].ActualHeight == 18 && titleLines[1].ActualHeight == 20 && Math.Abs(previewOrigin.Y - 163) < 1 &&
                ring.ActualWidth == 100 && ring.ActualHeight == 100 && ring.BorderThickness.Left == 2 && ((ScrollViewer)dialog.Content).Margin.Right == 8,
                "Profile native max line boxes, inner ring or missing scrollbar gutter shift its matched body geometry");
            Check(styleGrid.Children.OfType<Button>().All(button => button.CornerRadius.TopLeft == 20 && button.Padding.Left == 16 && button.BorderThickness.Left == 1) &&
                presets.Children.OfType<Button>().All(button => button.Width == 80 && button.Height == 80 && button.CornerRadius.TopLeft == 20 && button.Content is Image image && image.Width == 64),
                "Profile styles/options retain the earlier compact square presentation");
            Check(profileSection.CornerRadius.TopLeft == 10 && ((TextBox)Read(editor, "_nameBox")!).CornerRadius.TopLeft == 10 && ((PasswordBox)Read(editor, "_pinBox")!).CornerRadius.TopLeft == 10 &&
                presets.Children.OfType<Button>().All(button => button.Content is Image image &&
                    Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(image).Clip is Microsoft.UI.Composition.CompositionGeometricClip clip &&
                    clip.Geometry is Microsoft.UI.Composition.CompositionRoundedRectangleGeometry shape && shape.CornerRadius.X == 16),
                "Profile section/field/preset image corners do not use the current computed10/16px contract");
            var contentScroll = (ScrollViewer)dialog.Content;
            contentScroll.ChangeView(null, contentScroll.ScrollableHeight, null, true); await Layout(frame);
            await Capture(dialog, $"account-profile-editor-access-matched-{width}.png");
            var limits = All<Grid>(dialog).Single(grid => grid.Name == "ProfileAccessLimits");
            Check(limits.Children.Count == 3 && Grid.GetRow((FrameworkElement)limits.Children[1]) == (width < 640 ? 1 : 0) && Grid.GetColumn((FrameworkElement)limits.Children[1]) == (width < 640 ? 0 : 1) &&
                Grid.GetRow((FrameworkElement)limits.Children[2]) == (width < 640 ? 2 : 1),
                "Profile advisory/playback controls do not follow the pinned access grid order");
            contentScroll.ChangeView(null, 0, null, true); await Layout(frame);
        }
        Click(All<Button>(dialog).Single(button => button.Name == "CloseButton")); await showing;
    }
    private static async Task HomeImportAsync(Frame frame, SettingsPage page, SettingsViewModel vm, State wire)
    {
        frame.Width = 900; await Layout(frame); Show(page, "HomeScreen");
        await Until(() => vm.CanEditHomeSections && !vm.IsLoadingHomeSections);
        var launch = (Button)page.FindName("HomeImportButton"); Click(launch);
        await Until(() => Read(page, "_layoutTransferPreview") is ContentDialog);
        var input = (ContentDialog)Read(page, "_layoutTransferPreview")!;
        await Until(() => All<Button>(input).Any(button => button.Name == "PrimaryButton"));
        var source = All<TextBox>(input).Single(box => box.AcceptsReturn);
        source.Text = "{invalid-fixture"; Click(All<Button>(input).Single(button => button.Name == "PrimaryButton"));
        await Until(() => input.IsPrimaryButtonEnabled && All<TextBlock>(input).Any(text => text.Text.Contains("invalid") || text.Text.Contains("expected")));
        Check(ReferenceEquals(Read(page, "_layoutTransferPreview"), input) && source.Text == "{invalid-fixture" && wire.HomeWrites == 0,
            "Invalid pasted layout closed the dialog, lost text or wrote a section");
        var pasted = HomeLayoutTransfer.Serialize(new HomeLayoutFile { ServerId = "fixture-server", Pages = [new HomeLayoutPage { Overrides = [new SectionOverride { IsUserAdded = true, UserSectionType = "recently_added", UserTitle = "Imported fixture", UserConfig = new() }] }] });
        source.Text = pasted; Click(All<Button>(input).Single(button => button.Name == "PrimaryButton"));
        await Until(() => Read(page, "_layoutTransferPreview") is ContentDialog dialog && !ReferenceEquals(dialog, input));
        var preview = (ContentDialog)Read(page, "_layoutTransferPreview")!;
        await Until(() => All<Button>(preview).Any(button => button.Name == "PrimaryButton"));
        Check(All<TextBlock>(preview).Any(text => text.Text.Contains("Home: replace saved overrides with 1 sections")), "Import preview did not describe the mapped scope/section count");
        await Capture(preview, "account-home-import-preview-wide.png"); frame.Width = 460; await Layout(frame);
        await Capture(preview, "account-home-import-preview-narrow.png");
        wire.HomeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(All<Button>(preview).Single(button => button.Name == "PrimaryButton")); await Until(() => wire.HomeWrites == 1);
        var pending = !preview.IsPrimaryButtonEnabled && ((bool)Read(page, "_layoutTransferBusy")!);
        wire.HomeGate.SetResult(Reply(new { error = "validation_failed", message = "Fixture import rejection" }, HttpStatusCode.UnprocessableEntity));
        await Until(() => !((bool)Read(page, "_layoutTransferBusy")!) || preview.IsPrimaryButtonEnabled && All<TextBlock>(preview).Any(text => text.Text.Contains("Fixture import rejection")));
        wire.HomeGate = null;
        Check(pending, "Home import duplicate Apply remains available while pending");
        Check(ReferenceEquals(Read(page, "_layoutTransferPreview"), preview) && Popups(frame).SelectMany(All<ContentDialog>).Contains(preview),
            "Rejected Home import closed its preview instead of retaining the pasted draft for retry");
        await Capture(preview, "account-home-import-rejected.png");
        Click(All<Button>(preview).Single(button => button.Name == "PrimaryButton"));
        await Until(() => wire.HomeWrites == 2 && !((bool)Read(page, "_layoutTransferBusy")!));
        Check(wire.HomePayload!.Value.GetProperty("overrides").EnumerateArray().Any(row => row.GetProperty("user_title").GetString() == "Imported fixture"), "Import retry lost its typed mapped section");
        Click(launch); await Until(() => Read(page, "_layoutTransferPreview") is ContentDialog);
        var reopened = (ContentDialog)Read(page, "_layoutTransferPreview")!;
        await Until(() => All<Button>(reopened).Any(button => button.Name == "CloseButton"));
        Check(All<TextBox>(reopened).Single(box => box.AcceptsReturn).Text.Length == 0, "Completed Home import leaked its pasted layout into a new import");
        All<TextBox>(reopened).Single(box => box.AcceptsReturn).Text = pasted;
        Click(All<Button>(reopened).Single(button => button.Name == "CloseButton")); await Until(() => !((bool)Read(page, "_layoutTransferBusy")!));
        Check(wire.HomeWrites == 2, "Canceled reopened Home import performed a write");
        Program.Log("PASS actual Home import paste validation/mapped preview/pending/rejection retained draft/retry/success/reopen/Cancel no-write");
    }
    private static async Task HomeSaveAsync(Frame frame, SettingsPage page, SettingsViewModel vm, State wire)
    {
        frame.Width = 900; await Layout(frame); Show(page, "HomeScreen"); await Until(() => vm.CanEditHomeSections && vm.HomeSections.Count == 1);
        var original = vm.HomeSections.Single(); wire.HomeStatus = HttpStatusCode.UnprocessableEntity;
        var editing = (Task)Invoke(page, "EditHomeSectionAsync", original)!;
        await Until(() => Popups(frame).Any(node => All<TextBlock>(node).Any(text => text.Text == "Edit Section")));
        var sheet = Popups(frame).Single(node => All<TextBlock>(node).Any(text => text.Text == "Edit Section"));
        await Until(() => All<TextBox>(sheet).Any(box => Equals(box.Header, "Title")));
        All<TextBox>(sheet).Single(box => Equals(box.Header, "Title")).Text = "Rejected Home edit";
        Click(Buttons(sheet).Single(button => Equals(button.Content, "Save"))); await editing;
        Check(wire.HomeWrites == 1 && vm.HomeSections.Single().Title == "Saved Home fixture" && vm.ErrorMessage?.Contains("Fixture Home rejection") == true,
            "Rejected Home section edit did not restore server-confirmed state with a visible error");
        wire.HomeStatus = HttpStatusCode.OK; editing = (Task)Invoke(page, "EditHomeSectionAsync", vm.HomeSections.Single())!;
        await Until(() => Popups(frame).Any(node => All<TextBlock>(node).Any(text => text.Text == "Edit Section")));
        sheet = Popups(frame).Single(node => All<TextBlock>(node).Any(text => text.Text == "Edit Section"));
        await Until(() => All<TextBox>(sheet).Any(box => Equals(box.Header, "Title")));
        All<TextBox>(sheet).Single(box => Equals(box.Header, "Title")).Text = "Retried Home edit";
        Click(Buttons(sheet).Single(button => Equals(button.Content, "Save"))); await editing;
        Check(wire.HomeWrites == 2 && vm.HomeSections.Single().Title == "Retried Home edit", "Home section edit retry did not save the native editor result");
        Program.Log("PASS actual Home section editor422 rollback/reopen/retry wire");
    }
    private static async Task LanguagesAsync(SettingsViewModel vm, State wire)
    {
        typeof(SettingsViewModel).GetField("_metadataLanguageOverrides", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["pt-br"] = "sr-latn" });
        wire.MetadataStatus = HttpStatusCode.UnprocessableEntity;
        await vm.SetMetadataLanguageOverrideAsync("pt-BR", "en-GB");
        Check(wire.MetadataWrites == 1 && vm.MetadataLanguageOverrides.GetValueOrDefault("pt-br") == "sr-latn",
            "Rejected metadata-language exception leaves the optimistic replacement visible instead of the saved off-list tag");
        wire.MetadataGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = vm.SetMetadataLanguageOverrideAsync("pt-BR", "zh-Hant");
        await Until(() => wire.MetadataWrites == 2);
        var second = vm.SetMetadataLanguageOverrideAsync("pt-BR", null);
        await Task.Delay(100);
        Check(wire.MetadataWrites == 2, "Metadata-language exception writes overlap instead of preserving rollback order");
        wire.MetadataGate.SetResult(Reply(new { error = "validation_failed", message = "Fixture metadata-language rejection" }, HttpStatusCode.UnprocessableEntity));
        await Task.WhenAll(first, second); wire.MetadataGate = null;
        Check(wire.MetadataWrites == 3 && vm.MetadataLanguageOverrides.GetValueOrDefault("pt-br") == "sr-latn",
            "Concurrent rejected replacement/removal leaves an unpersisted exception draft");
        wire.MetadataStatus = HttpStatusCode.OK;
        await vm.SetMetadataLanguageOverrideAsync("pt-BR", "en-GB");
        Check(wire.MetadataWrites == 4 && vm.MetadataLanguageOverrides.GetValueOrDefault("pt-br") == "en-gb"
            && wire.MetadataPayload!.Value.GetProperty("value").GetProperty("pt-br").GetString() == "en-gb",
            "Metadata-language retry did not persist/commit the normalized typed mapping");
        Program.Log("PASS actual metadata-language exception422 rollback/off-list preservation/serialized concurrent failure/retry payload");
    }
    private static async Task DevicesAsync(Frame frame, SettingsPage page, State wire)
    {
        Show(page, "Devices"); await Until(() => Buttons(page).Any(button => button.Tag is UserDevice));
        var list = (StackPanel)page.FindName("DevicesContentHost");
        var rows = Buttons(list).Where(button => button.Tag is UserDevice).ToArray();
        Check(rows.Length == 3 && rows.All(button => ((UserDevice)button.Tag).DeviceId != "dormant"), "Dormant zero-override device was not hidden while current/customized remained visible");
        var reveal = Buttons(list).Single(button => Equals(button.Content, "Show 1 inactive devices")); Click(reveal);
        await Until(() => Buttons(list).Count(button => button.Tag is UserDevice) == 4);
        var search = All<TextBox>(list).Single(box => box.PlaceholderText.StartsWith("Search "));
        search.Text = "Dormant"; await Until(() => Buttons(list).Count(button => button.Tag is UserDevice) == 1);
        Check(((UserDevice)Buttons(list).Single(button => button.Tag is UserDevice).Tag).DeviceId == "dormant", "Device search does not reveal a dormant match");
        search.Text = ""; await Until(() => Buttons(list).Count(button => button.Tag is UserDevice) == 4);
        Click(Buttons(list).Single(button => button.Content?.ToString()?.StartsWith("Everyone") == true));
        await Until(() => All<TextBlock>(list).Any(text => text.Text == "PRIMARY (3)"));
        var picker = All<ComboBox>(list).Single(combo => Equals(combo.PlaceholderText, "All profiles"));
        Check(((ComboBoxItem)picker.Items[1]).Tag?.ToString() == "fixture", "Household profile picker does not place current profile first");
        Click(Buttons(list).First(button => button.Tag is UserDevice { DeviceId: "current" }));
        await Until(() => All<TextBlock>(list).Any(text => text.Text == "Locked by your household"));
        var locked = Row(list, "HDR"); Check(All<ToggleSwitch>(locked).Single().IsEnabled == false, "Household-locked HDR control permits editing");
        frame.Width = 1400; await Layout(frame); await Capture(frame, "account-devices-wide.png");
        var back = Buttons(list).Single(button => Equals(button.Content, "Back to devices"));
        Check(back.Visibility == Visibility.Collapsed, "Wide device browser uses the narrow list/detail path rather than both panes");
        frame.Width = 460; await Layout(frame); Check(back.Visibility == Visibility.Visible, "Narrow device detail lacks Back to devices");
        await Capture(frame, "account-device-detail-narrow.png"); Click(back);
        Check(back.Visibility == Visibility.Collapsed, "Back to devices did not return to the narrow list");
        Click(Buttons(list).First(button => button.Tag is UserDevice { DeviceId: "current" }));
        await Until(() => All<TextBlock>(list).Any(text => text.Text == "Theme music"));
        var toggle = All<ToggleSwitch>(Row(list, "Theme music")).Single(); wire.DeviceGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        toggle.IsOn = true; await Until(() => wire.DeviceWrites == 1); Check(!toggle.IsEnabled, "Device mutation leaves its control enabled while pending");
        wire.DeviceGate.SetResult(Reply(new { error = "validation_failed", message = "Fixture device rejection" }, HttpStatusCode.UnprocessableEntity));
        await Until(() => wire.EffectiveReads >= 1 && All<ToggleSwitch>(Row(list, "Theme music")).Single().IsEnabled);
        wire.DeviceGate = null;
        Check(!All<ToggleSwitch>(Row(list, "Theme music")).Single().IsOn, "Rejected device save did not reload inherited effective value");
        All<ToggleSwitch>(Row(list, "Theme music")).Single().IsOn = true;
        await Until(() => wire.DeviceWrites == 2 && All<ToggleSwitch>(Row(list, "Theme music")).Single().IsOn && All<ToggleSwitch>(Row(list, "Theme music")).Single().IsEnabled);
        var inherited = Row(list, "Show forced subtitles"); Click(Buttons(inherited).Single(button => Equals(button.Content, "Use your setting")));
        await Until(() => wire.DeviceDeletes == 1 && !Buttons(Row(list, "Show forced subtitles")).Any(button => Equals(button.Content, "Use your setting")));
        Check(wire.LastDeviceQuery?.Contains("scope=profile_device") == true && wire.LastDeviceQuery.Contains("device_id=current") && wire.LastDeviceQuery.Contains("profile_id=fixture"), "Device reset did not send scoped inheritance DELETE");
        Program.Log("PASS actual devices dormant/search/household/current-first/list-detail/locked/pending/rejection/retry/inheritance DELETE");
    }
    private static async Task ProfileAsync(Frame frame, State wire)
    {
        frame.Width = 1100; await Layout(frame);
        var constructor = typeof(ProfileEditorDialog).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var editor = constructor.Invoke([frame.XamlRoot, new Profile { Id = "fixture", Name = "Primary", IsPrimary = true, MaxContentRating = "future-ceiling", MaxAdvisoryAge = 12 }, Array.Empty<Library>(), false, App.Services.GetRequiredService<AuthApi>(), true, false]);
        var dialog = (ContentDialog)Read(editor, "_dialog")!;
        // Preset thumbnails use an external artwork source. Clear only fixture
        // images before showing; acceptance here concerns native geometry/actions.
        foreach (var button in ((Grid)Read(editor, "_avatarPresetGrid")!).Children.OfType<Button>())
            if (button.Content is Image image) image.Source = null;
        ((Image)Read(editor, "_avatarPreview")!).Source = null;
        var showing = dialog.ShowAsync(); await Until(() => All<Button>(dialog).Any(button => button.Name == "PrimaryButton"));
        var avatar = (Image)Read(editor, "_avatarPreview")!; Check(avatar.Width == 96 && avatar.Height == 96, "Profile avatar is not source96px");
        await Capture(dialog, "account-profile-editor-wide.png"); frame.Width = 460; await Layout(frame); await Capture(dialog, "account-profile-editor-narrow.png");
        var name = (TextBox)Read(editor, "_nameBox")!; name.Text = "Edited fixture"; wire.ProfileGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(All<Button>(dialog).Single(button => button.Name == "PrimaryButton")); await Until(() => wire.ProfileWrites == 1);
        Check(!dialog.IsPrimaryButtonEnabled, "Profile Save remains available during request");
        wire.ProfileGate.SetResult(Reply(new { error = "validation_failed", message = "Fixture profile rejection" }, HttpStatusCode.UnprocessableEntity));
        await Until(() => dialog.IsPrimaryButtonEnabled); wire.ProfileGate = null;
        Check(name.Text == "Edited fixture" && showing.Status == Windows.Foundation.AsyncStatus.Started, "Rejected profile loses draft or closes");
        await Capture(dialog, "account-profile-editor-rejected.png");
        Click(All<Button>(dialog).Single(button => button.Name == "PrimaryButton")); await showing;
        Program.Log($"TRACE profile retry writes={wire.ProfileWrites}, saved={((Profile?)ReadProperty(editor, "SavedProfile"))?.Name}");
        Check(wire.ProfileWrites == 2 && ((Profile?)ReadProperty(editor, "SavedProfile"))?.Name == "Edited fixture", "Profile retry failed to save actual draft");
        Check(wire.ProfilePayload?.GetProperty("max_content_rating").GetString() == "future-ceiling", "Profile Save lost unknown saved rating");
        var cancelEditor = constructor.Invoke([frame.XamlRoot, new Profile { Id = "fixture", Name = "Primary", IsPrimary = true }, Array.Empty<Library>(), false, App.Services.GetRequiredService<AuthApi>(), true, false]);
        foreach (var button in ((Grid)Read(cancelEditor, "_avatarPresetGrid")!).Children.OfType<Button>())
            if (button.Content is Image image) image.Source = null;
        var cancelDialog = (ContentDialog)Read(cancelEditor, "_dialog")!;
        var cancelShowing = cancelDialog.ShowAsync(); await Until(() => All<Button>(cancelDialog).Any(button => button.Name == "CloseButton"));
        ((TextBox)Read(cancelEditor, "_nameBox")!).Text = "Unsaved fixture";
        Click(All<Button>(cancelDialog).Single(button => button.Name == "CloseButton")); await cancelShowing;
        Check(wire.ProfileWrites == 2 && ReadProperty(cancelEditor, "SavedProfile") == null, "Profile Cancel wrote the unsaved draft");
        Program.Log("PASS actual ProfileEditor geometry/pending/422 retained draft/retry/future-rating payload");
    }
    private static async Task TourAsync(Frame frame, State wire)
    {
        frame.Width = 900; await Layout(frame);
        var tour = new FeatureTourDialog(new() { TourId = "fixture-tour", Steps = [new() { Id = "one", Kind = "welcome", Illustration = "welcome", Title = "Welcome to Fixture Silo", Body = "Make this profile feel like yours." }, new() { Id = "two", Kind = "feature_card", Title = "Second step", Body = "Fixture final step" }] }) { XamlRoot = frame.XamlRoot };
        var showing = tour.ShowAsync(); await Until(() => All<Button>(tour).Any(button => button.Name == "PrimaryButton"));
        foreach (var width in new[] { 900, 460 })
        {
            frame.Width = width; await Layout(frame);
            var heading = All<TextBlock>(tour).Single(text => text.Text == "Welcome to Fixture Silo");
            var background = All<Border>(tour).Single(border => border.Name == "BackgroundElement");
            Program.Log($"TRACE Tour requested={width}, Frame={frame.ActualWidth}, XamlRoot={tour.XamlRoot.Size.Width}, heading={heading.FontSize}/{heading.LineHeight}, background={background.ActualWidth}x{background.ActualHeight}, requestedBackground={background.Width}, maxBackground={background.MaxWidth}, corner={background.CornerRadius.TopLeft}, dialogCorner={tour.CornerRadius.TopLeft}");
            await Capture(tour, $"account-tour-{width}.png");
            Check(heading.FontSize == (width < 640 ? 20 : 24) && Math.Abs(background.ActualWidth - (width < 640 ? width - 32 : 512)) < 1 && background.CornerRadius.TopLeft == 20,
                "Tour realized heading/card width/radius differs from the pinned responsive shell");
            Check(!All<TextBlock>(tour).Any(text => text.Text == "Feature tour"), "Tour renders an extra visible heading outside its server step");
            var tile = All<Border>(tour).Single(border => border.Name == "TourIllustration");
            var secondaryColor = ((SolidColorBrush)Application.Current.Resources["SecondaryBackgroundBrush"]).Color;
            Check(tile.ActualWidth == 48 && tile.ActualHeight == 48 && tile.CornerRadius.TopLeft == 16 && ((SolidColorBrush)tile.Background).Color == secondaryColor,
                "Tour48px illustration tile uses card/surface instead of the pinned secondary background");
        }
        wire.TourGate = new(TaskCreationOptions.RunContinuationsAsynchronously); Click(All<Button>(tour).Single(button => button.Name == "PrimaryButton"));
        await Until(() => wire.TourWrites == 1);
        var skipPending = !All<Button>(tour).Single(button => button.Name == "CloseButton").IsEnabled;
        wire.TourGate.SetResult(Reply(new { error = "validation_failed", message = "Fixture tour rejection" }, HttpStatusCode.UnprocessableEntity));
        await Until(() => tour.IsPrimaryButtonEnabled); wire.TourGate = null;
        Check(skipPending && (int)Read(tour, "_index")! == 0, "Tour advanced or leaves Skip available during a pending progress write");
        Check((int)Read(tour, "_index")! == 0 && showing.Status == Windows.Foundation.AsyncStatus.Started && ((TextBlock)Read(tour, "_saveError")!).Text.Length > 0, "Failed Next dismissed/advanced tour or hid failure");
        await Capture(tour, "account-tour-rejected.png"); Click(All<Button>(tour).Single(button => button.Name == "PrimaryButton"));
        await Until(() => (int)Read(tour, "_index")! == 1); wire.TourGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(All<Button>(tour).Single(button => button.Name == "PrimaryButton")); await Until(() => wire.TourWrites == 3);
        wire.TourGate.SetResult(Reply(new { error = "validation_failed", message = "Fixture Done rejection" }, HttpStatusCode.UnprocessableEntity));
        await Until(() => tour.IsPrimaryButtonEnabled); wire.TourGate = null;
        Check(showing.Status == Windows.Foundation.AsyncStatus.Started && !(bool)Read(tour, "_finished")!, "Failed Done dismissed tour");
        Click(All<Button>(tour).Single(button => button.Name == "PrimaryButton")); await showing;
        Check(wire.TourPayload!.Value.GetProperty("completed").GetBoolean(), "Tour Done omitted completed progress");
        var skip = new FeatureTourDialog(new() { TourId = "fixture-skip", Steps = [new() { Id = "one", Kind = "welcome", Title = "Skip fixture" }] }) { XamlRoot = frame.XamlRoot };
        var skipping = skip.ShowAsync(); await Until(() => All<Button>(skip).Any(button => button.Name == "CloseButton"));
        wire.TourGate = new(TaskCreationOptions.RunContinuationsAsynchronously); Click(All<Button>(skip).Single(button => button.Name == "CloseButton"));
        await Until(() => wire.TourWrites == 5); wire.TourGate.SetResult(Reply(new { error = "validation_failed", message = "Fixture Skip rejection" }, HttpStatusCode.UnprocessableEntity));
        await Until(() => skip.IsPrimaryButtonEnabled); wire.TourGate = null;
        Check(skipping.Status == Windows.Foundation.AsyncStatus.Started && !(bool)Read(skip, "_finished")!, "Failed Skip dismissed tour");
        Click(All<Button>(skip).Single(button => button.Name == "CloseButton")); await skipping;
        Check(wire.TourPayload!.Value.GetProperty("skipped").GetBoolean(), "Tour Skip omitted skipped progress");
        Program.Log("PASS actual Tour Next/Done/Skip pending/rejection/retry and revision-checked wire");
    }
    private static async Task RecipeAsync(Frame frame)
    {
        frame.Width = 900; await Layout(frame);
        var groups = JsonSerializer.Deserialize<JsonElement>("""[{"match":"all","rules":[{"field":"year","op":"gte","value":2000}]},{"match":"any","rules":[{"field":"genre","op":"is","value":"Drama"}]}]""");
        var section = new SettingsSectionEntry { Id = "recipe-fixture", IsCustom = true, SectionType = "custom_filter", Title = "Grouped fixture", ItemLimit = 20, Config = new() { ["match"] = "any", ["groups"] = groups, ["media_scope"] = "movie" } };
        var original = JsonSerializer.Serialize(section.Config);
        var catalog = new RecipeCatalogResponse();
        var showing = RecipeGalleryDialog.ShowEditorAsync(frame.XamlRoot, catalog, section);
        await Until(() => Popups(frame).Any(root => All<TextBlock>(root).Any(text => text.Text == "Edit Section")));
        var root = Popups(frame).Single(root => All<TextBlock>(root).Any(text => text.Text == "Edit Section"));
        var sheet = All<Grid>(root).Single(grid => grid.HorizontalAlignment == HorizontalAlignment.Right && grid.Width == 512);
        await Until(() => sheet.ActualWidth > 0); await Layout(frame); ((FrameworkElement)root).UpdateLayout();
        Program.Log($"TRACE Home sheet wide client={frame.ActualWidth}x{frame.ActualHeight}, xamlRoot={frame.XamlRoot.Size.Width}x{frame.XamlRoot.Size.Height}, sheet={sheet.ActualWidth}x{sheet.ActualHeight}");
        Check(Math.Abs(sheet.ActualWidth - 512) < 1, "Home editor did not realize source512px right sheet");
        await Capture((FrameworkElement)root, "account-home-sheet-wide.png"); frame.Width = 460; await Layout(frame);
        Check(Math.Abs(sheet.ActualWidth - 460) < 1, "Home editor did not fit narrow viewport"); await Capture((FrameworkElement)root, "account-home-sheet-narrow.png");
        Click(Buttons(root).Single(button => Equals(button.Content, "Cancel"))); Check(await showing == null && JsonSerializer.Serialize(section.Config) == original, "Canceled Home recipe mutates saved grouped config");
        showing = RecipeGalleryDialog.ShowEditorAsync(frame.XamlRoot, catalog, section);
        await Until(() => Popups(frame).Any(node => All<TextBlock>(node).Any(text => text.Text == "Edit Section")));
        root = Popups(frame).Single(node => All<TextBlock>(node).Any(text => text.Text == "Edit Section"));
        await Until(() => All<TextBox>(root).Any(input => Equals(input.Header, "Title")));
        All<TextBox>(root).Single(input => Equals(input.Header, "Title")).Text = "Edited grouped fixture";
        Click(Buttons(root).Single(button => Equals(button.Content, "Save"))); var result = await showing;
        Check(result?.Title == "Edited grouped fixture" && JsonSerializer.Serialize(result.Config["groups"]) == groups.GetRawText(), "Recipe Save flattened grouped filters or ignored edited title");
        Program.Log("PASS actual Home right sheet wide/narrow/Cancel grouped preservation/Save result");
    }
    private static async Task PluginsAsync(Frame frame, SettingsPage page, SettingsViewModel vm)
    {
        Show(page, "Plugins"); await Until(() => !vm.IsLoadingPlugins && vm.PluginSettingsList.Count == 1);
        Check(All<TextBox>((DependencyObject)page.FindName("PluginCardsContainer")).Any(input => Equals(input.Header, "Schema-only text")), "Schema-only plugin definition has no editable field");
        var routes = Buttons((DependencyObject)page.FindName("PluginCardsContainer"));
        Check(routes.Any(button => Equals(button.Content, "Fixture plugin page")) && !routes.Any(button => Equals(button.Content, "Admin only")), "Plugin routes do not respect non-admin navigability");
        foreach (var width in new[] { 1400, 460 })
        { frame.Width = width; await Layout(frame); await Capture(frame, $"account-plugin-schema-route-{width}.png"); }
        Program.Log("PASS schema-only plugin field/non-admin route availability wide/narrow");
    }
    private static async Task OverlaysAsync(Frame frame, SettingsPage page, State wire)
    {
        var service = App.Services.GetRequiredService<CardOverlayService>(); wire.OverlayMaster = false; service.Invalidate();
        Show(page, "CardOverlays"); await Until(() => service.IsLoaded);
        Check(((FrameworkElement)page.FindName("CardOverlaysDisabledBanner")).Visibility == Visibility.Visible && !((FrameworkElement)page.FindName("CardOverlaysContentHost")).IsHitTestVisible, "Overlay administrator master disable leaves card controls available");
        await Capture(frame, "account-overlay-master-disabled.png"); wire.OverlayMaster = true; wire.OverlayProfileEnabled = false; await service.RefreshPreferencesAsync();
        await (Task)Invoke(page, "LoadCardOverlaySettingsAsync")!;
        Check(!((FrameworkElement)page.FindName("CardOverlaysContentHost")).IsHitTestVisible, "Profile badge disable leaves overlay body available");
        wire.OverlayProfileEnabled = true; wire.OverlayOverride = true; await service.RefreshPreferencesAsync(); await (Task)Invoke(page, "LoadCardOverlaySettingsAsync")!;
        wire.OverlayGeneralGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var mode = All<ComboBox>((DependencyObject)page.FindName("CardOverlaysGeneralHost")).Single();
        mode.SelectedIndex = 1; await Until(() => wire.OverlayGeneralWrites == 1);
        var pending = !mode.IsEnabled;
        wire.OverlayGeneralGate.SetResult(Reply(new { error = "validation_failed", message = "Fixture General rejection" }, HttpStatusCode.UnprocessableEntity));
        await Until(() => All<ComboBox>((DependencyObject)page.FindName("CardOverlaysGeneralHost")).Single().IsEnabled); wire.OverlayGeneralGate = null;
        mode = All<ComboBox>((DependencyObject)page.FindName("CardOverlaysGeneralHost")).Single();
        Check(pending && ((ComboBoxItem)mode.SelectedItem).Tag?.ToString() == "both" && page.ViewModel.ErrorMessage?.Contains("Fixture General rejection") == true,
            "Overlay General pending/rejection did not restore the server-confirmed quick-action mode with an error");
        mode.SelectedIndex = 2; await Until(() => wire.OverlayGeneralWrites == 2 && service.QuickActionMode == "watched");
        foreach (var width in new[] { 1400, 460 })
        {
            frame.Width = width; await Layout(frame);
            foreach (var variant in new[] { "Movie", "Show", "Requested" })
            {
                var button = (Button)page.FindName("Overlay" + variant + "PreviewButton");
                Check(button.Visibility == Visibility.Visible, "Overlay " + variant + " preview is unavailable despite the active capability");
                Click(button); await Layout(frame);
                Check((string)Read(page, "_cardOverlayPreviewVariant")! == variant.ToLowerInvariant(), "Overlay preview variant click did not update the real badge data");
                await Capture(frame, $"account-overlay-{variant.ToLowerInvariant()}-{width}.png");
            }
        }
        var restore = Buttons((DependencyObject)page.FindName("CardOverlaysGeneralHost")).Single(button => Equals(button.Content, "Restore default server settings")); Click(restore);
        await Until(() => Popups(frame).Any(node => All<ContentDialog>(node).Any()));
        var confirm = Popups(frame).SelectMany(All<ContentDialog>).Single(); Click(All<Button>(confirm).Single(button => button.Name == "PrimaryButton"));
        await Until(() => wire.OverlayDeletes == 1 && !service.HasProfileOverride);
        Check(!Buttons((DependencyObject)page.FindName("CardOverlaysGeneralHost")).Any(button => Equals(button.Content, "Restore default server settings")), "Overlay DELETE did not restore inherited state");
        await Capture(frame, "account-overlay-defaults-restored.png"); Program.Log("PASS actual overlay General pending/rejection/retry/movie/show/requested/master/profile disable/confirmed default inheritance DELETE");
    }
    private static Grid Row(DependencyObject root, string label) => All<Grid>(root).First(grid => grid.Children.FirstOrDefault() is StackPanel copy && copy.Children.OfType<TextBlock>().Any(text => text.Text == label));
    private static void Show(SettingsPage page, string tab) => Invoke(page, "ShowSettingsDetail", page.FindName(tab + "Tab"));
    private static object? Read(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static object? ReadProperty(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);
    private static object? Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static IEnumerable<Button> Buttons(DependencyObject root) => All<Button>(root);
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject { if (root is T value) yield return value; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static IEnumerable<DependencyObject> Popups(Frame frame) => VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot).Select(popup => popup.Child);
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 160; i++) { try { if (ready()) return; } catch (InvalidOperationException) { } await Task.Delay(25); } throw new TimeoutException("Account advanced native state timed out"); }
    private static async Task Layout(Frame frame) { ((Window)frame.Tag).AppWindow.ResizeClient(new((int)frame.Width, 740)); frame.UpdateLayout(); await Task.Delay(150); frame.UpdateLayout(); }
    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(150); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Program.Log($"TRACE advanced capture {name}: actual={element.ActualWidth}x{element.ActualHeight}, bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}");
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "Advanced native render was empty");
        var file = await (await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory))).CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
    private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK)
    { var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") }; response.Headers.ETag = new("\"fixture\""); return response; }
    internal sealed class State
    {
        internal int DeviceWrites, DeviceDeletes, EffectiveReads, ProfileWrites, TourWrites, OverlayDeletes, MetadataWrites, HomeWrites, OverlayGeneralWrites;
        internal bool ThemeMusic, DeviceOverride = true, OverlayMaster = true, OverlayProfileEnabled = true, OverlayOverride;
        internal string? LastDeviceQuery;
        internal JsonElement? ProfilePayload, TourPayload, MetadataPayload, HomePayload;
        internal HttpStatusCode HomeStatus = HttpStatusCode.OK;
        internal TaskCompletionSource<HttpResponseMessage>? HomeGate, OverlayGeneralGate;
        internal string OverlayQuickActionMode = "both";
        internal HttpStatusCode MetadataStatus = HttpStatusCode.OK;
        internal TaskCompletionSource<HttpResponseMessage>? MetadataGate;
        internal TaskCompletionSource<HttpResponseMessage>? DeviceGate, ProfileGate, TourGate;
        private readonly Dictionary<string, JsonElement> _defaults;
        internal State()
        { using var stream = typeof(DeviceSettingDisplay).Assembly.GetManifestResourceStream("SiloPlayer.Core.Models.Settings.device-settings.json")!; using var document = JsonDocument.Parse(stream); _defaults = document.RootElement.EnumerateArray().ToDictionary(row => row.GetProperty("key").GetString()!, row => row.GetProperty("default_value").Clone()); }
        internal Task<HttpResponseMessage>? Handle(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; var method = request.Method.Method;
            if (path == "/api/v2/system/identity") return Task.FromResult(Reply(new { server_id = "fixture-server" }));
            if (path == "/api/v2/profile/sections/flags") return Task.FromResult(Reply(new { allow_profile_custom_sections = true }));
            if (path == "/api/v2/sections/recipes") return Task.FromResult(Reply(new { categories = new[] { new { category = "Browse", recipes = new[] { new { type = "recently_added", name = "Recently added", admin_only = false } } } } }));
            if (path == "/api/v2/profile/sections/settings") return Task.FromResult(Reply(new { items = new[] { new { id = "home-fixture", section_type = "recently_added", title = "Saved Home fixture", item_limit = 20, is_custom = true, position = 0, config = new { } } } }));
            if (path == "/api/v2/profile/sections" && method == "PUT") return HomeWrite(request, ct);
            if (path == "/api/v2/profile/sections") return Task.FromResult(Reply(new { items = Array.Empty<object>() }));
            if (path == "/api/v2/devices") return Task.FromResult(Reply(new { items = new[] { Device("current", "Current workstation", "fixture", "Primary", true, 1, DateTimeOffset.UtcNow.ToString("O")), Device("dormant", "Dormant tablet", "fixture", "Primary", false, 0, "2025-01-01T00:00:00Z"), Device("customized", "Customized old TV", "fixture", "Primary", false, 1, "2025-01-01T00:00:00Z"), Device("other", "Other profile desktop", "child", "Restricted", false, 0, DateTimeOffset.UtcNow.ToString("O")) }, page = new { has_more = false } }));
            if (path == "/api/v2/settings/values/effective")
            {
                var device = request.RequestUri.Query.Contains("device_id="); if (device) EffectiveReads++;
                var keys = request.RequestUri.Query.TrimStart('?').Split('&').Where(part => part.StartsWith("keys=")).Select(part => Uri.UnescapeDataString(part[5..]));
                return Task.FromResult(Reply(new { items = keys.Select(key => new { key, value = JsonSerializer.SerializeToElement<object?>(key switch { "ui.theme_music_enabled" when device => ThemeMusic, "playback.show_forced_subtitles" when device && DeviceOverride => false, "ui.card_overlays_enabled" => OverlayProfileEnabled, "ui.card_quick_actions" => OverlayQuickActionMode, _ => _defaults.GetValueOrDefault(key, JsonSerializer.SerializeToElement<object?>(null)) }), source = device && key == "playback.show_forced_subtitles" && DeviceOverride || !device && key == "ui.card_overlays" && OverlayOverride ? "profile_device" : "default", scope = device && key == "playback.show_forced_subtitles" && DeviceOverride ? "profile_device" : !device && key == "ui.card_overlays" && OverlayOverride ? "profile" : "default", constrained = device && key == "player.hdr_enabled", constraint_kind = device && key == "player.hdr_enabled" ? "locked" : null }).ToArray(), revision = 15 }));
            }
            if (path.StartsWith("/api/v2/settings/values/") && method is "PUT" or "DELETE") return SettingWrite(request, ct);
            if (path == "/api/v2/settings/overlay-config") return Task.FromResult(Reply(new { enabled = OverlayMaster }));
            if (path == "/api/v2/profiles/fixture" && method == "PATCH") return ProfileWrite(request, ct);
            if (path == "/api/v2/onboarding/state") return Task.FromResult(Reply(new { }));
            if (path == "/api/v2/onboarding/progress" && method == "PUT") return TourWrite(request, ct);
            if (path == "/api/v2/settings/plugins") return Task.FromResult(Reply(new { items = new[] { new { id = 77, plugin_id = "fixture-plugin", version = "1.0", user_config_schema = new[] { new { key = "plain", title = "Schema-only text", description = "Fixture schema without admin-form", json_schema = "{\"type\":\"string\"}" } }, routes = new[] { new { path = "/fixture", navigation_label = "Fixture plugin page", navigable = true, access = "user" }, new { path = "/admin", navigation_label = "Admin only", navigable = true, access = "admin" } } } } }));
            if (path == "/api/v2/settings/plugins/77") return Task.FromResult(Reply(new { values = new Dictionary<string, string> { ["plain"] = "Fixture value" } }));
            return null;
        }
        private static object Device(string id, string name, string profile, string owner, bool current, int changes, string seen) => new { device_id = id, device_name = name, device_platform = "Windows desktop", profile_id = profile, profile_name = owner, is_current_device = current, changed_count = changes, last_seen_at = seen };
        private async Task<HttpResponseMessage> SettingWrite(HttpRequestMessage request, CancellationToken ct)
        {
            var key = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath[24..]);
            if (key == "catalog.metadata_language_overrides" && request.Method == HttpMethod.Put)
            {
                MetadataPayload = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); MetadataWrites++;
                return MetadataGate != null ? await MetadataGate.Task.WaitAsync(ct) : Reply(new { error = "validation_failed", message = "Fixture metadata-language rejection" }, MetadataStatus);
            }
            if (key == "ui.card_quick_actions" && request.Method == HttpMethod.Put)
            {
                var payload = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); OverlayGeneralWrites++;
                var result = OverlayGeneralGate != null ? await OverlayGeneralGate.Task.WaitAsync(ct) : Reply(new { });
                if (result.IsSuccessStatusCode) OverlayQuickActionMode = payload.GetProperty("value").GetString()!;
                return result;
            }
            if (request.RequestUri.Query.Contains("scope=profile_device"))
            {
                LastDeviceQuery = request.RequestUri.Query;
                if (request.Method == HttpMethod.Delete) { DeviceDeletes++; DeviceOverride = false; return Reply(new { }, HttpStatusCode.NoContent); }
                DeviceWrites++; var result = DeviceGate != null ? await DeviceGate.Task.WaitAsync(ct) : Reply(new { });
                if (result.IsSuccessStatusCode && key == "ui.theme_music_enabled") ThemeMusic = true;
                return result;
            }
            if (request.Method == HttpMethod.Delete && key == "ui.card_overlays") { OverlayDeletes++; OverlayOverride = false; return Reply(new { }, HttpStatusCode.NoContent); }
            return Reply(new { });
        }
        private async Task<HttpResponseMessage> ProfileWrite(HttpRequestMessage request, CancellationToken ct)
        { ProfilePayload = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); ProfileWrites++; return ProfileGate != null ? await ProfileGate.Task.WaitAsync(ct) : Reply(new { id = "fixture", name = ProfilePayload.Value.GetProperty("name").GetString(), is_primary = true, allowed_library_ids = Array.Empty<string>() }); }
        private async Task<HttpResponseMessage> TourWrite(HttpRequestMessage request, CancellationToken ct)
        { TourPayload = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); TourWrites++; return TourGate != null ? await TourGate.Task.WaitAsync(ct) : Reply(new { }); }
        private async Task<HttpResponseMessage> HomeWrite(HttpRequestMessage request, CancellationToken ct)
        { HomePayload = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); HomeWrites++; return HomeGate != null ? await HomeGate.Task.WaitAsync(ct) : Reply(new { error = "validation_failed", message = "Fixture Home rejection" }, HomeStatus); }
    }
}
