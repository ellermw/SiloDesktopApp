using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

internal static class LocalizedPropertiesNativeFixture
{
    internal static async Task RunAsync(Panel parent)
    {
        var locales = LocaleService.Shared;
        var before = locales.Current;
        var root = new StackPanel { Spacing = 8, Width = 500 };
        var title = new TextBlock { Text = "Save" };
        var save = new Button { Content = "Save" };
        var input = new TextBox { Header = "Name", PlaceholderText = "Search" };
        var slider = new Slider { Header = "Name", Minimum = 0, Maximum = 100, Value = 65 };
        var raw = new TextBlock { Text = "My Movie: Save" };
        var fallback = new TextBlock { Text = "Native fallback" };
        Localized.SetText(title, "common.save");
        Localized.SetContent(save, "common.save");
        Localized.SetName(save, "common.save");
        Localized.SetHelp(save, "common.cancel");
        Localized.SetToolTip(save, "common.cancel");
        Localized.SetHeader(input, "web.profiles.editor.name");
        Localized.SetPlaceholder(input, "web.search.label");
        Localized.SetHeader(slider, "web.profiles.editor.name");
        Localized.SetText(fallback, "desktop.missing.catalog.key");
        Localized.SetDirection(root, true);
        root.Children.Add(title); root.Children.Add(save); root.Children.Add(input);
        root.Children.Add(raw); root.Children.Add(fallback); root.Children.Add(slider);
        parent.Children.Add(root);
        try
        {
            await Settle(root);
            Apply("de"); await Settle(root);
            Require(title.Text == AppStrings.Get("common.save") && title.Text != "Save", "loaded TextBlock did not translate");
            Require((string)save.Content == title.Text && AutomationProperties.GetName(save) == title.Text, "button or accessible name did not translate");
            Require(AutomationProperties.GetHelpText(save) == AppStrings.Get("common.cancel"), "accessible help did not translate");
            Require((string)ToolTipService.GetToolTip(save) == AppStrings.Get("common.cancel"), "tooltip did not translate");
            Require((string)input.Header == AppStrings.Get("web.profiles.editor.name") && input.PlaceholderText == AppStrings.Get("web.search.label"), "input text did not translate");
            Require((string)slider.Header == AppStrings.Get("web.profiles.editor.name") && slider.Value == 65, "slider header did not translate or its value changed");
            Require(raw.Text == "My Movie: Save" && fallback.Text == "Native fallback", "raw data or missing-key fallback changed");
            Program.Log("PASS: explicit keyed native controls, automation and fallback after live language switch.");

            Localized.SetText(title, "common.cancel");
            Require(title.Text == AppStrings.Get("common.cancel"), "key change waited for another locale");
            Localized.SetText(title, null);
            Require(title.Text == "Save", "clearing a key did not restore the original English fallback");
            Apply("ar"); await Settle(root);
            Require(root.FlowDirection == FlowDirection.RightToLeft && root.Language == "ar", "keyed direction did not update");
            Require((string)save.Content == AppStrings.Get("common.save"), "retained button did not switch to Arabic");
            parent.Children.Remove(root); await Task.Delay(30);
            Apply("ja"); await Task.Delay(30);
            parent.Children.Add(root); await Settle(root);
            Require(root.FlowDirection == FlowDirection.LeftToRight && root.Language == "ja", "reloaded direction retained a detached locale");
            Require((string)save.Content == AppStrings.Get("common.save"), "reloaded keyed control retained a detached locale");
            Require((string)slider.Header == AppStrings.Get("web.profiles.editor.name") && slider.Value == 65, "reloaded slider retained a detached locale or changed value");
            Program.Log("PASS: key replacement/removal, Arabic direction and detached/reloaded locale refresh.");

            parent.Children.Remove(root); await Task.Delay(30);
            Localized.SetContent(save, "common.cancel");
            parent.Children.Add(root); await Settle(root);
            Require((string)save.Content == AppStrings.Get("common.cancel"), "key replaced while detached retained the previous text in the same locale");
            Localized.SetContent(save, "common.save");
            Program.Log("PASS: a key replaced while detached refreshes on reload without changing the locale.");

            var dialog = new ContentDialog { XamlRoot = parent.XamlRoot, Title = "Settings", PrimaryButtonText = "Save", SecondaryButtonText = "Cancel", CloseButtonText = "Close", Content = new TextBlock { Text = "Dialog fixture" } };
            Localized.SetTitle(dialog, "web.common.title.settings.index");
            Localized.SetPrimaryButton(dialog, "common.save");
            Localized.SetSecondaryButton(dialog, "common.cancel");
            Localized.SetCloseButton(dialog, "web.ui.close");
            Localized.SetDirection(dialog, true);
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Opened += (_, _) => opened.TrySetResult();
            var shown = dialog.ShowAsync();
            try
            {
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Apply("ar"); await Settle(dialog);
                Require((string)dialog.Title == AppStrings.Get("web.common.title.settings.index"), "dialog title did not translate: " + dialog.Title + "; expected=" + AppStrings.Get("web.common.title.settings.index"));
                Require(dialog.PrimaryButtonText == AppStrings.Get("common.save") && dialog.SecondaryButtonText == AppStrings.Get("common.cancel") && dialog.CloseButtonText == AppStrings.Get("web.ui.close"), "dialog actions did not translate");
                Require(dialog.FlowDirection == FlowDirection.RightToLeft, "open dialog did not follow direction");
            }
            finally { dialog.Hide(); await shown; }
            Program.Log("PASS: actual open dialog title/actions/direction follow language changes.");
            var closedTitle = dialog.Title;
            Apply("de"); await Settle(root);
            Require(Equals(dialog.Title, closedTitle), "closed dialog retained its active locale subscription");
            var reopened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Opened += (_, _) => reopened.TrySetResult();
            var shownAgain = dialog.ShowAsync();
            try
            {
                await reopened.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require((string)dialog.Title == AppStrings.Get("web.common.title.settings.index"), "reopened dialog did not catch up");
            }
            finally { dialog.Hide(); await shownAgain; }

            var menu = new MenuFlyout();
            var menuItem = new MenuFlyoutItem { Text = "Save" };
            Localized.SetText(menuItem, "common.save");
            Localized.SetDirection(menuItem, true);
            menu.Items.Add(menuItem);
            var menuOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            menu.Opened += (_, _) => menuOpened.TrySetResult();
            menu.ShowAt(save);
            try
            {
                await menuOpened.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Apply("ar"); await Settle(menuItem);
                Require(menuItem.Text == AppStrings.Get("common.save") && menuItem.FlowDirection == FlowDirection.RightToLeft,
                    "open native flyout item did not update text/direction");
            }
            finally { menu.Hide(); }
            Program.Log("PASS: closed/reopened dialog catches up and open native menu follows locale.");
        }
        finally
        {
            parent.Children.Remove(root);
            locales.Apply(locales.Create(before.Resolution));
        }
    }

    private static void Apply(string language) => LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(language, ["en-US"], "en-US")));
    private static async Task Settle(FrameworkElement element)
    {
        await Task.Delay(100);
        element.UpdateLayout();
        await Task.Delay(30);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
