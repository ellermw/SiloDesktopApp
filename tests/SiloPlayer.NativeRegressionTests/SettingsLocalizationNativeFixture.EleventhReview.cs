using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

internal static partial class SettingsLocalizationNativeFixture
{
    private static async Task ReviewEleventh(StackPanel parent, Wire wire, SiloApiClient client)
    {
        var collection = new Collection { Id = "raw-collection", Name = "Raw collection 日本語", CreatorProfileId = "primary", CollectionType = "manual" };
        var session = new TitleAllowanceSession(client, new() { Id = "primary", IsPrimary = true }, () => true);
        Apply("de");
        var dialog = new AllowCollectionDialog(session, collection) { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(dialog, async () =>
        {
            foreach (var language in new[] { "de", "ar", "ja", "en" })
            {
                Apply(language); await Settle(dialog);
                Check(Equals(dialog.Title, AppStrings.Get("web.profiles.allow.title", new Dictionary<string, object?> { ["name"] = collection.Name })), "Collection allowance heading lost parameterized formatting.");
                Check(dialog.CloseButtonText == AppStrings.Get("common.done"), "Collection allowance Done action did not refresh.");
                Check(collection.Name == "Raw collection 日本語" && collection.Id == "raw-collection", "Collection refresh changed raw data.");
            }
        });
        var host = new Button { Content = "Collection menu fixture" }; parent.Children.Add(host);
        var menu = new MenuFlyout();
        try
        {
            await Settle(host); wire.AllowanceMenuCase = true; wire.AllowanceAvailabilityFails = true;
            var seam = typeof(AllowCollectionDialog).Assembly.GetType("SiloPlayer.Controls.CollectionAllowanceMenu")!;
            seam.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [menu, host, collection, (Func<bool>)(() => true)]);
            var action = menu.Items.OfType<MenuFlyoutItem>().Single();
            Apply("de"); menu.ShowAt(host);
            await Until(() => action.Visibility == Visibility.Visible && action.Text == AppStrings.Get("common.retry"));
            foreach (var language in new[] { "ar", "ja", "de" })
            {
                Apply(language); await Settle(host);
                Check(action.Text == AppStrings.Get("common.retry") && AutomationProperties.GetName(action) == action.Text, "Collection allowance retry stayed in the old language.");
            }
            wire.AllowanceAvailabilityFails = false; var reads = wire.AllowanceCapabilityReads;
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(action).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.AllowanceCapabilityReads > reads && action.Text == AppStrings.Get("web.collections.actions.allowFor"));
            menu.ShowAt(host); Apply("ar"); await Settle(host);
            Check(action.Text == AppStrings.Get("web.collections.actions.allowFor"), "Successful allowance retry lost retained action localization.");
        }
        finally { menu.Hide(); wire.AllowanceMenuCase = false; wire.AllowanceAvailabilityFails = false; parent.Children.Remove(host); }
        Apply("de");
        var tonight = new WatchTonightDialog { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(tonight, async () =>
        {
            await Settle(tonight);
            await Until(() => Descendants(tonight).OfType<Button>().Any(button => SiloPlayer.Services.Localized.GetName(button) == "web.ui.close"));
            var close = Descendants(tonight).OfType<Button>().Single(button => SiloPlayer.Services.Localized.GetName(button) == "web.ui.close");
            foreach (var language in new[] { "de", "ar", "ja", "en" })
            {
                Apply(language); await Settle(tonight);
                Check(AutomationProperties.GetName(close) == AppStrings.Get("web.ui.close"), "Watch Tonight close accessible name did not refresh in " + language + ": " + AutomationProperties.GetName(close));
                Check(Equals(ToolTipService.GetToolTip(close), AppStrings.Get("web.ui.close")), "Watch Tonight close tooltip did not refresh in " + language + ": " + ToolTipService.GetToolTip(close));
            }
        });
        Apply("en");
        Program.Log("PASS collection heading/Done, failed collection allowance menu and successful retry, Watch Tonight close accessibility through en/de/ar/ja; raw collection identity retained.");
    }
}
