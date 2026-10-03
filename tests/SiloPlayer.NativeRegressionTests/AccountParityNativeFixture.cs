using System.Text.Json;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class AccountParityNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        using var handler = new Wire(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://account-parity-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-access", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true });
        var api = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "account-parity-settings"));
        var theme = new ThemeService(settings, api);
        var vm = new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client), new HistoryImportApi(client), new WatchProvidersApi(client), auth, theme, settings, new AccessibilityService(settings, theme));
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(api).AddSingleton(settings)
            .AddSingleton(new AuthApi(client)).AddSingleton(vm).AddSingleton(new NavigationService()).AddSingleton(new ToastService())
            .AddSingleton<LoginViewModel>().AddSingleton<SignupViewModel>()
            .AddSingleton(new CardOverlayService(api)).AddSingleton(new UICustomizationService(api)).BuildServiceProvider();
        field.SetValue(null, services);
        SettingsPage? page = null; FrameworkElement? authPage = null;
        try
        {
            await AuthBackdropGeometryAsync(parent);
            await PasswordRevealAsync(parent);
            await LoginSignupAsync(parent, services);
            await RequiredPasswordPendingAsync(parent, handler, auth, client);
            await services.GetRequiredService<UICustomizationService>().EnsureLoadedAsync();
            page = new SettingsPage { Width = 1400, Height = 740 };
            parent.Children.Add(page); await LayoutAsync(page);
            Invoke(page, "SyncInterfaceControls");
            Invoke(page, "ShowSettingsDetail", page.FindName("InterfaceTab"));
            var choices = (WrapPanel)page.FindName("InterfacePosterSizeChoices");
            if (choices.Children.OfType<RadioButton>().Count() != 3 || choices.Children.OfType<RadioButton>().Count(radio => radio.IsChecked == true) != 1)
                throw new InvalidOperationException("Interface does not expose one selected poster size among three visible choices.");
            var navigation = (ScrollViewer)page.FindName("SettingsNavigationScroller");
            if (navigation.Visibility != Visibility.Visible || ((Grid)page.FindName("SettingsLayoutGrid")).ColumnDefinitions[0].Width.Value != 240)
                throw new InvalidOperationException("Wide settings rail has incorrect responsive geometry.");
            var ui = services.GetRequiredService<UICustomizationService>();
            if (!ui.IsSupported || ui.IsUnavailable || !((FrameworkElement)page.FindName("InterfaceControls")).IsHitTestVisible
                || ((FrameworkElement)page.FindName("InterfaceStatusCard")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Interface available fixture did not load authoritative defaults: supported=" + ui.IsSupported + ", unavailable=" + ui.IsUnavailable);
            await CaptureAsync(page, "account-interface-wide.png");
            page.Width = 460; await LayoutAsync(page);
            if (navigation.Visibility != Visibility.Collapsed || ((FrameworkElement)page.FindName("SettingsHeaderGrid")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Narrow settings detail did not hide the overview header and rail.");
            var presets = new[] { "BalancedCardPreset", "CompactCardPreset", "CinemaCardPreset", "ArtworkCardPreset" }.Select(name => (Button)page.FindName(name)).ToArray();
            if (presets.Any(button => !button.IsEnabled) || presets.Select(Grid.GetColumn).Any(column => column != 0)
                || !presets.Select(Grid.GetRow).SequenceEqual(new[] { 0, 1, 2, 3 }))
                throw new InvalidOperationException("Narrow Interface presets do not expose four enabled single-column choices.");
            if (((FrameworkElement)page.FindName("SettingsBackButton")).Visibility != Visibility.Collapsed
                || ((Grid)page.FindName("SettingsLayoutGrid")).BorderThickness.Left != 0)
                throw new InvalidOperationException("Narrow settings still paints desktop outer chrome.");
            var title = Descendants<TextBlock>(page).Single(text => text.Text == "Navigation & cards");
            var titlePoint = title.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
            Program.Log("TRACE Interface heading " + titlePoint.X + "," + titlePoint.Y + " " + title.ActualWidth + "x" + title.ActualHeight);
            if (Math.Abs(titlePoint.X - 32) > 1 || Math.Abs(titlePoint.Y - 76) > 1)
                throw new InvalidOperationException("Narrow Interface heading is not at the reference32,76 origin.");
            var selectedContent = (Grid)presets[0].Content;
            var selectedIcon = selectedContent.Children.OfType<Viewbox>().Single();
            var selectedText = selectedContent.Children.OfType<StackPanel>().Single();
            var iconOrigin = selectedIcon.TransformToVisual(presets[0]).TransformPoint(new Windows.Foundation.Point());
            var textOrigin = selectedText.TransformToVisual(presets[0]).TransformPoint(new Windows.Foundation.Point());
            if (Grid.GetColumn(selectedIcon) != 1 || Grid.GetRow(selectedIcon) != 0 || iconOrigin.X < textOrigin.X + selectedText.ActualWidth
                || Math.Abs(iconOrigin.Y - textOrigin.Y) > 1)
                throw new InvalidOperationException("Preset selected check is stacked below text instead of its fixed icon row.");
            if (presets.Count(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetItemStatus(button) == "Selected") != 1
                || !Descendants<Viewbox>(presets[0]).Any(icon => icon.Visibility == Visibility.Visible))
                throw new InvalidOperationException("Current preset selection is not visibly indicated.");
            await CaptureAsync(page, "account-interface-narrow.png");
            Set(page, "_suppressEvents", true);
            var language = (ComboBox)page.FindName("SpokenLanguageComboBox");
            typeof(SettingsPage).GetMethod("SelectComboBoxByTag", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [language, "sr-Latn"]);
            if (!language.IsEditable || (language.SelectedItem as ComboBoxItem)?.Tag as string != "sr-Latn")
                throw new InvalidOperationException("Saved language outside the preset list was lost by the native editor.");
            parent.Children.Remove(page); page = null;

            var draft = new Dictionary<string, object?>();
            var form = new ProviderSchemaForm(new PluginAdminForm { Fields = [new() { Key = "enabled", Label = "Enabled", Control = "TOGGLE", DefaultValue = true }] }, draft);
            parent.Children.Add(form); await LayoutAsync(form);
            if (draft.Count != 0 || !Descendants<ToggleSwitch>(form).Single().IsOn)
                throw new InvalidOperationException("Rendering a default activated an optional provider configuration.");
            Descendants<ToggleSwitch>(form).Single().IsOn = false;
            if (!draft.TryGetValue("enabled", out var enabled) || !Equals(enabled, false))
                throw new InvalidOperationException("Native provider form did not retain an explicitly disabled toggle.");
            parent.Children.Remove(form);

            authPage = new ChoosePasswordPage { Width = 900, Height = 740 }; parent.Children.Add(authPage); await LayoutAsync(authPage);
            var card = Descendants<Border>(authPage).Single(border => border.MaxWidth == 384);
            if (card.Padding.Left != 52 || card.CornerRadius.TopLeft != 12 || Descendants<TextBlock>(card).Any(text => text.Text == "Fixture Silo"))
                throw new InvalidOperationException("Required-password page did not use the current inset auth-card geometry.");
            await CaptureAsync(authPage, "account-required-password-wide.png");
            authPage.Width = 460; await LayoutAsync(authPage);
            if (Math.Abs(card.ActualWidth - 384) > 1) throw new InvalidOperationException("Narrow auth card does not fit the viewport.");
            if (card.ActualHeight > 560 || card.ActualHeight < 480
                || !Descendants<TextBlock>(card).Any(text => text.Text == "Choose a new password" && text.TextWrapping == TextWrapping.Wrap))
                throw new InvalidOperationException("Required-password card stretches or clips its wrapped title: height=" + card.ActualHeight);
            Program.Log("TRACE Required password card " + card.ActualWidth + "x" + card.ActualHeight);
            await CaptureAsync(authPage, "account-required-password-narrow.png");
            parent.Children.Remove(authPage); authPage = null;
            Program.Log("PASS account parity native settings wide/narrow, visible choices, saved regional language, optional provider default and required-password layout");
        }
        finally
        {
            if (page != null) parent.Children.Remove(page);
            if (authPage != null) parent.Children.Remove(authPage);
            field.SetValue(null, previous);
        }
    }
    private static async Task AuthBackdropGeometryAsync(StackPanel parent)
    {
        var page = new ChoosePasswordPage { Width = 460, Height = 740 };
        parent.Children.Add(page);
        try
        {
            await LayoutAsync(page);
            var glows = Descendants<Border>(page).Where(border => border.Background is RadialGradientBrush).ToArray();
            if (glows.Length != 2)
                throw new InvalidOperationException("Actual required-password auth shell lacks the two current WebUI radial glows.");
            var primary = (RadialGradientBrush)glows.Single(border => Math.Abs(((RadialGradientBrush)border.Background).GradientStops[1].Offset - .28) < .001).Background;
            var ambient = (RadialGradientBrush)glows.Single(border => Math.Abs(((RadialGradientBrush)border.Background).GradientStops[1].Offset - .26) < .001).Background;
            if (Math.Abs(primary.Center.X - 82.8) > .1 || Math.Abs(primary.Center.Y - 133.2) > .1
                || Math.Abs(ambient.Center.X - 377.2) > .1 || Math.Abs(ambient.Center.Y - 88.8) > .1
                || primary.GradientStops[1].Color.A != 0 || ambient.GradientStops[1].Color.A != 0)
                throw new InvalidOperationException("Auth glows have incorrect source centers or transparency.");
            var primaryBitmap = new RenderTargetBitmap(); await primaryBitmap.RenderAsync(glows.Single(border => ReferenceEquals(border.Background, primary)));
            var pixels = (await primaryBitmap.GetPixelsAsync()).ToArray();
            if (primaryBitmap.PixelWidth == 0 || pixels.Length != primaryBitmap.PixelWidth * primaryBitmap.PixelHeight * 4)
                throw new InvalidOperationException("Actual auth glow could not be rendered.");
            var pixelX = (int)Math.Round(primaryBitmap.PixelWidth * .18);
            var pixelY = (int)Math.Round(primaryBitmap.PixelHeight * .18);
            var centerOffset = (pixelY * primaryBitmap.PixelWidth + pixelX) * 4;
            var bottomOffset = (primaryBitmap.PixelHeight - 1) * primaryBitmap.PixelWidth * 4;
            var alpha = pixels[centerOffset + 3];
            var primaryLayer = glows.Single(border => ReferenceEquals(border.Background, primary));
            Program.Log($"TRACE auth primary bitmap={primaryBitmap.PixelWidth}x{primaryBitmap.PixelHeight}, layer={primaryLayer.ActualWidth}x{primaryLayer.ActualHeight}, opacity={primaryLayer.Opacity}, center={primary.Center}, radius={primary.RadiusX},{primary.RadiusY}, stops={primary.GradientStops[0].Color}/{primary.GradientStops[1].Color}, centerBGRA={pixels[centerOffset]},{pixels[centerOffset+1]},{pixels[centerOffset+2]},{alpha}, bottomBGRA={pixels[bottomOffset]},{pixels[bottomOffset+1]},{pixels[bottomOffset+2]},{pixels[bottomOffset+3]}");
            await CaptureAsync(primaryLayer, "account-auth-primary-diagnostic.png");
            var shell = (Grid)page.Content;
            var formScroller = shell.Children.OfType<ScrollViewer>().Single();
            var formVisibility = formScroller.Visibility;
            try
            {
                formScroller.Visibility = Visibility.Collapsed; await LayoutAsync(page);
                var composite = new RenderTargetBitmap(); await composite.RenderAsync(shell);
                var compositePixels = (await composite.GetPixelsAsync()).ToArray();
                if (composite.PixelWidth == 0 || compositePixels.Length != composite.PixelWidth * composite.PixelHeight * 4)
                    throw new InvalidOperationException("Auth composite shell could not be captured.");
                var sample = ((int)Math.Round(composite.PixelHeight * .18) * composite.PixelWidth + (int)Math.Round(composite.PixelWidth * .18)) * 4;
                var edge = (composite.PixelHeight - 1) * composite.PixelWidth * 4;
                Program.Log($"TRACE auth composite={composite.PixelWidth}x{composite.PixelHeight}, shellBackground={((SolidColorBrush)shell.Background).Color}, primaryCenterBGRA={compositePixels[sample]},{compositePixels[sample+1]},{compositePixels[sample+2]},{compositePixels[sample+3]}, bottomBGRA={compositePixels[edge]},{compositePixels[edge+1]},{compositePixels[edge+2]},{compositePixels[edge+3]}");
                await CaptureAsync(shell, "account-auth-composite-diagnostic.png");
                void CompositePixel(int x, int y, byte red, byte green, byte blue)
                {
                    var offset = (y * composite.PixelWidth + x) * 4;
                    if (Math.Abs(compositePixels[offset] - blue) > 2 || Math.Abs(compositePixels[offset + 1] - green) > 2
                        || Math.Abs(compositePixels[offset + 2] - red) > 2 || compositePixels[offset + 3] != 255)
                        throw new InvalidOperationException($"Actual auth composite glow pixel at{x},{y} differs from the source Cobalt CSS blend.");
                }
                CompositePixel((int)Math.Round(composite.PixelWidth * .18), (int)Math.Round(composite.PixelHeight * .18), 35, 50, 73);
                CompositePixel((int)Math.Round(composite.PixelWidth * .82), (int)Math.Round(composite.PixelHeight * .12), 32, 46, 67);
                CompositePixel(0, composite.PixelHeight - 1, 16, 23, 34);
            }
            finally { formScroller.Visibility = formVisibility; await LayoutAsync(page); }
            if (Math.Abs(alpha - 46) > 2 || pixels[(primaryBitmap.PixelHeight - 1) * primaryBitmap.PixelWidth * 4 + 3] != 0)
                throw new InvalidOperationException("Auth primary glow rendered opacity differs from18%/transparent CSS stops.");
            await CaptureAsync(page, "account-auth-glows-narrow.png");
            page.Width = 600; page.Height = 800; await LayoutAsync(page);
            if (Math.Abs(primary.Center.X - 108) > .1 || Math.Abs(primary.Center.Y - 144) > .1)
                throw new InvalidOperationException("Actual auth shell glows did not follow viewport resize.");
            Program.Log("PASS actual auth shell two radial glows, source centers/transparency, rendered opacity and resize.");
        }
        finally { parent.Children.Remove(page); }
    }
    private static async Task PasswordRevealAsync(StackPanel parent)
    {
        var page = new ChoosePasswordPage { Width = 460, Height = 740 };
        parent.Children.Add(page);
        try
        {
            await LayoutAsync(page);
            var inputs = new[] { "_temporary", "_password", "_confirmation" }.Select(name => (PasswordBox)typeof(ChoosePasswordPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!).ToArray();
            if (inputs[0].FocusState == FocusState.Unfocused)
                throw new InvalidOperationException("Required-password temporary field did not receive the source initial focus.");
            var reveals = Descendants<Button>(page).Where(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Show password").ToArray();
            if (reveals.Length != 3 || reveals.Any(button => button.Visibility != Visibility.Visible || button.ActualWidth != 40 || button.ActualHeight != 36))
                throw new InvalidOperationException("Required-password empty idle fields lack three explicit40px password reveal actions.");
            var iconBitmap = new RenderTargetBitmap(); await iconBitmap.RenderAsync((FrameworkElement)reveals[0].Content);
            var iconPixels = (await iconBitmap.GetPixelsAsync()).ToArray();
            var expected = ((SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]).Color;
            var colored = false;
            for (var offset = 0; offset + 3 < iconPixels.Length; offset += 4)
            {
                var alpha = iconPixels[offset + 3];
                if (alpha < 100) continue;
                if (Math.Abs(iconPixels[offset] * 255d / alpha - expected.B) < 4 && Math.Abs(iconPixels[offset + 1] * 255d / alpha - expected.G) < 4 && Math.Abs(iconPixels[offset + 2] * 255d / alpha - expected.R) < 4) colored = true;
            }
            if (!colored) throw new InvalidOperationException("Actual password eye glyph does not render its muted theme foreground.");
            inputs[0].Password = "fixture-secret";
            var invoke = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(reveals[0]).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)!;
            invoke.Invoke(); await LayoutAsync(page);
            if (inputs[0].PasswordRevealMode != PasswordRevealMode.Visible || inputs[0].Password != "fixture-secret" || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(reveals[0]) != "Hide password")
                throw new InvalidOperationException("Explicit password reveal did not preserve the value/change its accessible state.");
            invoke.Invoke(); await LayoutAsync(page);
            if (inputs[0].PasswordRevealMode != PasswordRevealMode.Hidden || inputs[0].Password != "fixture-secret")
                throw new InvalidOperationException("Explicit password masking did not preserve the value.");
            inputs[0].IsEnabled = false; await LayoutAsync(page);
            if (!reveals[0].IsEnabled) throw new InvalidOperationException("Password reveal lost the source independent reveal action while input is disabled.");
            await CaptureAsync(page, "account-required-password-explicit-reveal.png");
            Program.Log("PASS password idle reveal actions, explicit toggled masking/accessibility and value retention.");
        }
        finally { parent.Children.Remove(page); }
    }
    private static async Task LoginSignupAsync(StackPanel parent, IServiceProvider services)
    {
        var loginVm = services.GetRequiredService<LoginViewModel>();
        var signupVm = services.GetRequiredService<SignupViewModel>();
        await loginVm.LoadAuthInfoCommand.ExecuteAsync(null);
        await signupVm.CheckSignupStatusCommand.ExecuteAsync(null);
        foreach (var signup in new[] { false, true })
        {
            Page page = signup ? new SignupPage() : new LoginPage();
            page.Width = 900; page.Height = 740; parent.Children.Add(page);
            try
            {
                await LayoutAsync(page);
                var inputs = Descendants<PasswordBox>(page).ToArray();
                var reveals = Descendants<Button>(page).Where(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Show password").ToArray();
                if (inputs.Length != (signup ? 2 : 1) || reveals.Length != inputs.Length || reveals.Any(button => button.Visibility != Visibility.Visible || button.ActualWidth != 40))
                    throw new InvalidOperationException("Actual " + (signup ? "signup" : "login") + " does not expose all idle reveal controls.");
                inputs[0].Password = "fixture-wrapper-password";
                Program.Log($"TRACE auth binding page={page.GetType().Name}, namedInput={ReferenceEquals(inputs[0], page.FindName("PasswordBox"))}, vmIdentity={ReferenceEquals(signup ? ((SignupPage)page).ViewModel : ((LoginPage)page).ViewModel, signup ? signupVm : loginVm)}, immediateFieldLength={inputs[0].Password.Length}, immediateVmLength={(signup ? signupVm.Password : loginVm.Password).Length}");
                await UntilAsync(() => (signup ? signupVm.Password : loginVm.Password) == inputs[0].Password);
                Program.Log($"TRACE auth binding settled page={page.GetType().Name}, fieldLength={inputs[0].Password.Length}, vmLength={(signup ? signupVm.Password : loginVm.Password).Length}");
                foreach (var button in reveals)
                {
                    var invoke = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)!;
                    invoke.Invoke(); await LayoutAsync(page);
                    if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) != "Hide password") throw new InvalidOperationException("Auth reveal action does not expose its toggled state.");
                    invoke.Invoke(); await LayoutAsync(page);
                }
                if (inputs.Any(input => input.PasswordRevealMode != PasswordRevealMode.Hidden)) throw new InvalidOperationException("Auth field did not mask after toggle.");
                inputs[0].Password = "";
                await UntilAsync(() => (signup ? signupVm.Password : loginVm.Password) == "");
                var card = (Border)page.FindName(signup ? "SignupCard" : "LoginCard");
                void CardGeometry(bool narrow)
                {
                    var expectedWidth = signup ? 384 : narrow ? 412 : 448;
                    var expectedHeight = signup ? 614 : narrow ? 531 : 511;
                    Program.Log($"TRACE auth geometry {page.GetType().Name} {page.ActualWidth}x{page.ActualHeight} card={card.ActualWidth}x{card.ActualHeight}, field={inputs[0].ActualWidth}x{inputs[0].ActualHeight}, radius={card.CornerRadius.TopLeft}");
                    if (Math.Abs(card.ActualWidth - expectedWidth) > 1 || Math.Abs(card.ActualHeight - expectedHeight) > 2 || card.CornerRadius.TopLeft != 12
                        || card.Padding.Left != 52 || card.Padding.Top != 24 || card.BorderThickness.Left != 0 || Math.Abs(inputs[0].ActualWidth - (expectedWidth - 104)) > 1)
                        throw new InvalidOperationException("Actual auth card/field geometry differs from corrected-CSS reference: " + page.GetType().Name);
                }
                CardGeometry(false);
                await CaptureAsync(page, signup ? "account-signup-wide.png" : "account-login-wide.png");
                page.Width = 460; await LayoutAsync(page);
                CardGeometry(true);
                foreach (var input in inputs)
                {
                    var point = input.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
                    if (point.X < 0 || point.X + input.ActualWidth > 460 || input.ActualWidth <= 0)
                        throw new InvalidOperationException("Narrow auth password field overflows or is hidden.");
                }
                await CaptureAsync(page, signup ? "account-signup-narrow.png" : "account-login-narrow.png");
                if (signup) signupVm.ErrorMessage = "Fixture signup rejection"; else loginVm.ErrorMessage = "Fixture sign-in rejection";
                await LayoutAsync(page);
                await CaptureAsync(page, signup ? "account-signup-rejected-narrow.png" : "account-login-rejected-narrow.png");
                if (signup) signupVm.ErrorMessage = null; else loginVm.ErrorMessage = null;
            }
            finally { parent.Children.Remove(page); }
        }
        Program.Log("PASS actual Login/Signup idle reveal, toggled accessibility, password binding, masking and responsive rejection captures.");
    }
    private static async Task RequiredPasswordPendingAsync(StackPanel parent, Wire handler, AuthService auth, SiloApiClient client)
    {
        var original = auth.CurrentUser!;
        auth.SetCurrentUser(new() { Id = original.Id, Username = original.Username, Role = original.Role, PasswordChangeRequired = true });
        var page = new ChoosePasswordPage { Width = 460, Height = 740 };
        parent.Children.Add(page); await LayoutAsync(page);
        var inputs = new[] { "_temporary", "_password", "_confirmation" }.Select(name => (PasswordBox)typeof(ChoosePasswordPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!).ToArray();
        inputs[0].Password = "fixture-temporary"; inputs[1].Password = inputs[2].Password = "fixture-new-password";
        Set(page, "_flow", new RequiredPasswordTransition(auth, client));
        var save = (Button)typeof(ChoosePasswordPage).GetField("_save", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        var signOut = Descendants<HyperlinkButton>(page).Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Sign out");
        handler.PasswordGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var disabled = false;
        try
        {
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(save).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)!).Invoke();
            await UntilAsync(() => handler.PasswordWrites == 1);
            disabled = inputs.All(input => !input.IsEnabled) && signOut.IsEnabled && !save.IsEnabled && Equals(save.Content, "Saving...");
            await CaptureAsync(page, "account-required-password-pending.png");
        }
        finally
        {
            handler.PasswordGate.TrySetResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("{\"error\":\"validation_failed\",\"message\":\"Fixture password rejection\"}") });
            await UntilAsync(() => save.IsEnabled);
            handler.PasswordGate = null;
            parent.Children.Remove(page); auth.SetCurrentUser(original);
        }
        if (!disabled) throw new InvalidOperationException("Required-password pending Save does not disable fields/show Saving or removes the source sign-out handoff.");
        if (inputs.Any(input => !input.IsEnabled) || !signOut.IsEnabled || inputs[0].Password != "fixture-temporary" || inputs[1].Password != "fixture-new-password")
            throw new InvalidOperationException("Required-password rejection did not restore its input draft and actions.");
        Program.Log("PASS required-password native pending fields/source sign-out handoff and rejected draft retry state");
    }
    private static async Task UntilAsync(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 80; attempt++) { if (ready()) return; await Task.Delay(25); }
        throw new InvalidOperationException("Account native pending state did not complete.");
    }

    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static async Task LayoutAsync(FrameworkElement element) { element.UpdateLayout(); await Task.Delay(120); element.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException("Account parity fixture was not laid out for capture.");
        var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal TaskCompletionSource<HttpResponseMessage>? PasswordGate;
        internal int PasswordWrites;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; object body = new { items = Array.Empty<object>() };
            if (path == "/api/v2/account/password" && PasswordGate is not null)
            { PasswordWrites++; return PasswordGate.Task.WaitAsync(ct); }

            if (path == "/api/v2/theme/branding") body = new { server_name = "Fixture Silo", login_subtitle = "Sign in with an existing account." };
            else if (path == "/api/v2/auth/signup") body = new { enabled = true };
            else if (path == "/api/v2/auth/providers") body = new { items = new[] { new { id = "local", display_name = "Password", mode = "credentials" } } };
            else if (path == "/api/v2/capabilities/password-reset") body = new { state = "available" };
            else if (path == "/api/v2/settings/contract/capabilities") body = new { api_version = 1, manifest_revision = 15, supports_batched_effective = true, supports_idempotent_writes = true, supports_atomic_shortcuts = true, client_families = new[] { "desktop", "web" } };
            else if (path == "/api/v2/settings/values/effective")
            {
                using var stream = typeof(DeviceSettingDisplay).Assembly.GetManifestResourceStream("SiloPlayer.Core.Models.Settings.device-settings.json")!;
                using var document = JsonDocument.Parse(stream);
                var defaults = document.RootElement.EnumerateArray().ToDictionary(value => value.GetProperty("key").GetString()!, value => value.GetProperty("default_value").Clone());
                var keys = request.RequestUri.Query.TrimStart('?').Split('&').Where(value => value.StartsWith("keys=")).Select(value => Uri.UnescapeDataString(value[5..]));
                body = new { items = keys.Select(key => new { key, value = defaults.TryGetValue(key, out var value) ? value : JsonSerializer.SerializeToElement<object?>(null), source = "default" }).ToArray(), revision = 15 };
            }
            Program.Log("TRACE account fixture " + path + request.RequestUri.Query + " reply=" + JsonSerializer.Serialize(body));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) });
        }
    }
}
