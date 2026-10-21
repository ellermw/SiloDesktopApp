using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class InheritedDefaultsOct20NativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://inherited-native.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SelectProfile("viewer", profile: new Profile { Id = "viewer", IsPrimary = true });
        var api = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "inherited-settings"));
        var theme = new ThemeService(settings, api);
        var vm = new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client), new HistoryImportApi(client),
            new WatchProvidersApi(client), auth, theme, settings, new AccessibilityService(settings, theme));

        Check(vm.TitleArtExplanation == "" && !vm.TitleArtHasOwnChoice, "Pending title art must make no inherited-source claim.");
        await vm.LoadTitleArtAsync();
        Check(vm.CanEditTitleArt && !vm.ShowTitleArt && vm.TitleArtExplanation.Contains("server default") && !vm.TitleArtHasOwnChoice,
            "Confirmed default-off title art must honor effective value and source.");
        wire.TitleSource = "profile_device"; await vm.LoadTitleArtAsync();
        Check(vm.TitleArtHasOwnChoice, "Equal-to-default stored device value remains an explicit choice.");
        wire.Deletes.Clear(); await vm.ResetTitleArtCommand.ExecuteAsync(null);
        Check(wire.Deletes.SequenceEqual(new[] { "ui.title_art:profile_device" }) && vm.TitleArtExplanation.Contains("server default"),
            "Device reset must preserve other device/profile choices and reload inherited source.");
        wire.TitleSource = "profile"; await vm.LoadTitleArtAsync();
        wire.Deletes.Clear(); await vm.ResetTitleArtCommand.ExecuteAsync(null);
        Check(wire.Deletes.SequenceEqual(new[] { "ui.title_art:profile_device", "ui.title_art:profile" }),
            "Effective profile reset must clear current device before profile, tolerating missing values.");
        wire.FailRead = true; await vm.LoadTitleArtAsync();
        Check(vm.TitleArtReadFailed && vm.TitleArtExplanation == "" && !vm.TitleArtHasOwnChoice, "Failed title art read must make no inherited-source claim.");
        wire.FailRead = false;

        await vm.LoadNextUpPreferenceAsync();
        Check(vm.NextUpInheritanceExplanation.Contains("Separate row") && !vm.NextUpHasOwnChoice, "Home mode inherits the resolved server default.");
        wire.ModeSource = "profile"; await vm.LoadNextUpPreferenceAsync();
        Check(vm.NextUpHasOwnChoice, "Equal-valued explicit Next Up mode remains resettable.");
        wire.Deletes.Clear(); await vm.ResetNextUpModeCommand.ExecuteAsync(null);
        Check(wire.Deletes.SequenceEqual(new[] { "ui.next_up_mode:profile", "ui.next_up_mode:profile_device" }) && !vm.NextUpHasOwnChoice,
            "Next Up mode reset must reload inherited source.");
        await vm.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null);
        Check(vm.ShowNewEpisodesFirst && vm.NewEpisodesInheritanceExplanation.Contains("on") && !vm.NewEpisodesHasOwnChoice,
            "New episode preference inherits current server boolean.");
        wire.NewSource = "profile"; await vm.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null);
        Check(vm.NewEpisodesHasOwnChoice, "Equal-valued stored new episode choice remains resettable.");
        wire.Deletes.Clear(); await vm.ResetNewEpisodesPreferenceCommand.ExecuteAsync(null);
        Check(wire.Deletes.SequenceEqual(new[] { "ui.next_up_new_releases_first:profile" }) && !vm.NewEpisodesHasOwnChoice,
            "New episode reset only deletes this profile choice and reloads inherited source.");

        var notifications = new NotificationSettingsViewModel(new NotificationsApi(client), auth);
        auth.SetCurrentUser(new UserInfo { Id = "account", Role = "admin" });
        auth.SelectProfile("secondary", profile: new Profile { Id = "secondary", IsPrimary = false });
        Check(!notifications.CanManageDiscord, "Server administrator on a secondary profile cannot manage Discord.");
        var writes = wire.DiscordWrites;
        await notifications.StartDiscordLinkAsync(); await notifications.SaveDiscordModeAsync(); await notifications.UnlinkDiscordAsync();
        Check(wire.DiscordWrites == writes, "Secondary profile must send no Discord mutation.");
        auth.SelectProfile("primary", profile: new Profile { Id = "primary", IsPrimary = true });
        Check(notifications.CanManageDiscord, "Proven primary profile can manage account Discord.");
        await notifications.StartDiscordLinkAsync(); await notifications.SaveDiscordModeAsync(); await notifications.UnlinkDiscordAsync();
        Check(wire.DiscordWrites == writes + 3, "Primary link/mode/unlink use the actual context-bound Discord endpoints.");
        var deletes = wire.Deletes.Count;
        Check(!vm.CanEditTitleArt && !vm.CanChangeNextUpMode, "Profile switch must retire loaded settings authority even before page unload.");
        await vm.ResetTitleArtCommand.ExecuteAsync(null); await vm.ResetNextUpModeCommand.ExecuteAsync(null);
        Check(wire.Deletes.Count == deletes, "Stale settings controls must not clear another profile's values.");
        vm.CloseNewEpisodesPreference(); vm.DeactivateTitleArt(); vm.DeactivateInheritedPreferences();
        Program.Log("PASS: October20 native settings VM inherited title art/Home values, source-aware resets, failed-read source suppression, and primary-only Discord link/mode/unlink.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Wire : HttpMessageHandler
    {
        internal string TitleSource = "default", ModeSource = "default", NewSource = "default";
        internal bool FailRead; internal int DiscordWrites;
        internal List<string> Deletes = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "inherited-native.invalid", "Only isolated fixture requests are permitted.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/settings/contract/capabilities")
                return Task.FromResult(Json(new { api_version = 1, manifest_revision = 22, supports_batched_effective = true, supports_idempotent_writes = true }));
            if (path == "/api/v2/settings/values/effective")
            {
                if (FailRead) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                var key = query.GetValues("keys")!.Single();
                object value = key == "ui.next_up_mode" ? "separate" : key == "ui.next_up_new_releases_first";
                var source = key == "ui.title_art" ? TitleSource : key == "ui.next_up_mode" ? ModeSource : NewSource;
                return Task.FromResult(Json(new { items = new[] { new { key, value, source } } }));
            }
            if (request.Method == HttpMethod.Delete && path.StartsWith("/api/v2/settings/values/"))
            {
                var key = Uri.UnescapeDataString(path.Split('/').Last());
                var scope = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["scope"];
                Deletes.Add(key + ":" + scope);
                if (key == "ui.title_art") TitleSource = "default";
                else if (key == "ui.next_up_mode") ModeSource = "default";
                else NewSource = "default";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            if (path.Contains("discord"))
            {
                ++DiscordWrites;
                return Task.FromResult(Json(new { url = "https://discord.invalid/fixture", linked = true, mode = "off" }));
            }
            return Task.FromResult(Json(new { items = Array.Empty<object>() }));
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
