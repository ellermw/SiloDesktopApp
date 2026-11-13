using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.ViewModels;

internal static partial class MediaLocalizationNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var before = LocaleService.Shared.Current;
        var raw = "Save فيلم · 東京";
        var model = new RecommendationsViewModel(SiloPlayer.App.Services.GetRequiredService<CatalogApi>(),
            new RecommendationsApi(SiloPlayer.App.Services.GetRequiredService<SiloApiClient>()));
        var mounted = new RecommendationRowDisplay { Label = raw, Type = "recommended", SectionKind = "genre", SectionKey = "42" };
        model.Rows.Add(mounted);
        var fresh = new RecommendationRowDisplay { Label = raw, Type = mounted.Type, SectionKind = mounted.SectionKind,
            SectionKey = mounted.SectionKey, TitleKey = "home.section.forYou", TitleArgs = new() { ["name"] = "Raw person" } };
        typeof(RecommendationsViewModel).GetMethod("ReconcileRows", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(model, new object[] { new[] { fresh } });
        Require(ReferenceEquals(model.Rows[0], mounted) && mounted.TitleKey == fresh.TitleKey
            && mounted.TitleArgs?["name"] == "Raw person", "retained recommendation discarded fresh title metadata");
        fresh.TitleArgs!["name"] = "changed";
        Require(mounted.TitleArgs!["name"] == "Raw person", "title arguments were not copied");
        var section = new HomeSectionWithItems { Id = "localized", Title = raw, TitleKey = "home.section.forYou", KeyedTitle = raw };
        var row = new SectionRow { Section = section, Width = 700, UseTitleViewLink = true, OnViewAll = () => { } };
        parent.Children.Add(row);
        try
        {
            Apply("de"); await Task.Delay(150); row.UpdateLayout();
            Require(((TextBlock)row.FindName("SectionTitle")).Text == AppStrings.Get("home.section.forYou"), "section title did not translate");
            Require(((TextBlock)row.FindName("TitleViewSuffix")).Text == AppStrings.Get("web.catalog.carousel.view"), "section action did not translate");
            Apply("ar"); await Task.Delay(150); row.UpdateLayout();
            Require(row.FlowDirection == FlowDirection.RightToLeft, "media row did not mirror navigation");
            Require(section.Title == raw, "locale change overwrote raw title");
            Program.Log("PASS: retained recommendation metadata, copied arguments, mounted section action and RTL preserve raw titles.");
            parent.Children.Remove(row);
            await VerifyOwnedPagesAsync(parent, raw, model);
            await VerifyCatalogHeadingAsync(raw);
        }
        finally
        {
            parent.Children.Remove(row);
            LocaleService.Shared.Apply(LocaleService.Shared.Create(before.Resolution));
        }
    }

    private static void Apply(string language) => LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(language, ["en-US"], "en-US")));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
