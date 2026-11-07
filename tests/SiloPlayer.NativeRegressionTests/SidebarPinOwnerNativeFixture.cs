using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

internal static partial class SidebarReviewNativeFixture
{
    private static async Task CheckPinOwnerRetirementAsync(MainWindow window, IServiceProvider provider, bool holdWrite)
    {
        var auth = provider.GetRequiredService<AuthService>();
        var api = provider.GetRequiredService<SiloApiClient>();
        var priorPins = Pins;
        Pins = new Dictionary<string, object> { ["1"] = new[] { new { type = "section", id = "pin", label = "Pinned section" } } };
        await window.RefreshSidebarPinsAsync();
        var row = new SectionRow { Width = 900, LibraryId = 1,
            Section = new() { Id = "pin", Title = "Pinned section", SectionType = "recently_added", LoadCompleted = true } };
        var fixture = new Window { Content = row };
        fixture.AppWindow.Move(new(-20000, -20000)); fixture.AppWindow.Show(false);
        try
        {
            await Until(() => row.IsLoaded);
            typeof(SectionRow).GetMethod("SyncPinState", Instance)!.Invoke(row, null);
            var button = (Button)row.FindName("PinSectionBtn");
            var icon = (FontIcon)row.FindName("PinSectionIcon");
            Check(icon.Glyph == "\uE841" && button.IsEnabled && row.IsLoaded, "real section pin starts pinned and loaded");
            var originalGlyph = icon.Glyph;
            var originalTip = ToolTipService.GetToolTip(button);
            var originalContext = api.CaptureContext();
            var shell = typeof(MainWindow).GetField("_hydratedShellKey", Instance)!;
            var originalShell = shell.GetValue(window);
            var reads = PinReads; var writes = PinWrites;
            if (holdWrite) HeldPinWrite = Hold(); else HeldPins = Hold();
            Invoke(button);
            await Until(() => !button.IsEnabled && (holdWrite ? PinWrites == writes + 1 : PinReads > reads));
            // Profile metadata changes shell ownership without replacing API
            // identity/context. The actual section remains mounted throughout.
            var selected = auth.SelectedProfile!;
            auth.RefreshSelectedProfile(new Profile { Id = selected.Id, Name = selected.Name + " updated",
                IsPrimary = selected.IsPrimary, IsChild = selected.IsChild });
            await Until(() => !Equals(originalShell, shell.GetValue(window)));
            Check(api.CaptureContext() == originalContext && row.IsLoaded && icon.Glyph == originalGlyph,
                "metadata retires the captured shell while the old pin presentation remains mounted and unchanged");
            if (holdWrite) HeldPinWrite!.TrySetResult(new { }); else HeldPins!.TrySetResult(new { value = Pins });
            await Until(() => button.IsEnabled);
            Check(row.IsLoaded && icon.Glyph == originalGlyph && Equals(originalTip, ToolTipService.GetToolTip(button)) &&
                (bool)typeof(SectionRow).GetField("_isPinned", Instance)!.GetValue(row)!,
                "retired pin " + (holdWrite ? "write" : "read") + " completion preserves actual section glyph, tooltip and pinned state");
            Check(PinWrites == writes + (holdWrite ? 1 : 0), "retired pin read never writes; accepted retired write is not retried");
        }
        finally
        {
            HeldPins?.TrySetResult(new { value = Pins }); HeldPins = null;
            HeldPinWrite?.TrySetResult(new { }); HeldPinWrite = null;
            Pins = priorPins; fixture.Close();
        }
    }
}
