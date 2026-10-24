using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Views;

internal static class Oct24TrackChoicesNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://oct24-track-choices.invalid"); api.SetProfile("fixture");
        var playback = new PlaybackApi(api);
        slot.SetValue(null, new Services(previous, api, new CatalogApi(api), playback));
        var page = new ItemDetailPage { Width = 900, Height = 700 };
        var version = new FileVersion
        {
            FileId = 42, EffectiveAudioTrackIndex = 0,
            AudioTracks = [new() { Language = "en", Default = true }, new() { Language = "jpn", Title = "Main", EmbeddedTitle = "Original", Codec = "flac", Channels = 6, Layout = "5.1" }],
            SubtitleTracks = [new() { Index = 0, Language = "eng", Codec = "srt" }, new() { Index = 1, Language = "spa", Codec = "srt" }],
        };
        var detail = new WatchDetailResponse { ContentId = "episode", SeriesId = "series", Versions = [version], EffectiveSubtitleLanguage = "es", EffectiveSubtitleMode = "auto", EffectiveShowForcedSubtitles = true };
        Set(page, "_selectedVersion", version); Set(page, "_watchDetail", detail);
        parent.Children.Add(page);
        try
        {
            await Task.Delay(100);
            bool VersionPinned() => (bool)typeof(ItemDetailPage).GetProperty("IsSelectedVersionPinned", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Require(!VersionPinned(), "Detail's automatic default version was treated as a viewer pin.");
            var otherVersion = new FileVersion { FileId = 43, Resolution = "720p", AudioTracks = [new() { Language = "en", Default = true }] };
            var versions = new List<FileVersion> { otherVersion, version };
            Call(page, "BuildVersionFlyout", versions, false, null, null);
            var versionFlyout = (MenuFlyout)page.FindName("VersionFlyout"); versionFlyout.ShowAt(page); await Task.Delay(60);
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(versionFlyout.Items.OfType<MenuFlyoutItem>().First()).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(VersionPinned() && Get<FileVersion>(page, "_selectedVersion")?.FileId == 43,
                "Actual detail version picker did not record an explicit viewer choice.");
            Require(((FileVersion)Call(page, "RetainExplicitVersion", versions, version)!).FileId == 43,
                "A later automatic watch response replaced the viewer's explicit version.");
            api.SetProfile("replacement");
            Require(!VersionPinned(), "A previous profile's version pin leaked into a new owner.");
            api.SetProfile("fixture"); Set(page, "_selectedVersion", version);
            Call(page, "RetainExplicitVersion", new List<FileVersion> { version }, version);
            Require(!VersionPinned(), "An automatic default after owner retirement retained an old pin.");
            Program.Log("PASS: detail automatic default stays adaptable; real version picker pins; asynchronous default refresh preserves the pick; profile retirement clears its authority.");
            Call(page, "BuildAudioTracksFlyout", version);
            var flyout = (Flyout)page.FindName("AudioTracksFlyout");
            var panel = (StackPanel)page.FindName("AudioTracksFlyoutContent");
            flyout.ShowAt((FrameworkElement)page.FindName("AudioTracksButton")); await Task.Delay(60);
            Invoke(panel.Children.OfType<Button>().ElementAt(2));
            await Until(() => wire.Audio.Count == 1 && Get<int?>(page, "_selectedAudioTrackIndex") == 1);
            var put = wire.Audio[0]; Require(put.Method == HttpMethod.Put && put.Body!.Value.GetProperty("track_signature").GetProperty("embedded_title").GetString() == "Original", "Actual detail audio click did not save the selected signature.");
            flyout.ShowAt((FrameworkElement)page.FindName("AudioTracksButton")); await Task.Delay(60);
            Invoke(panel.Children.OfType<Button>().First());
            await Until(() => wire.Audio.Count == 2 && Get<int?>(page, "_selectedAudioTrackIndex") == null);
            Require(wire.Audio[1].Method == HttpMethod.Delete && ((TextBlock)page.FindName("AudioTracksSummary")).Text.StartsWith("Auto:", StringComparison.Ordinal), "Actual Auto audio click did not delete the saved preference.");
            wire.FailNext = true;
            flyout.ShowAt((FrameworkElement)page.FindName("AudioTracksButton")); await Task.Delay(60);
            Invoke(panel.Children.OfType<Button>().ElementAt(2));
            await Until(() => wire.Audio.Count == 3 && Get<int?>(page, "_selectedAudioTrackIndex") == null);
            Require(((TextBlock)page.FindName("AudioTracksSummary")).Text.StartsWith("Auto:", StringComparison.Ordinal), "Failed native audio write left an unsaved selection visible.");

            var automatic = (VersionSubtitleTrack?)Call(page, "ResolveAutoSubtitle", version.SubtitleTracks);
            Require(automatic?.Language == "spa", "Native Auto preview compared English profile/audio instead of Spanish subtitle preference.");
            detail.EffectiveSubtitleLanguage = "";
            Require(Call(page, "ResolveAutoSubtitle", version.SubtitleTracks) == null, "Explicit empty subtitle language was replaced by profile defaults.");
            detail.EffectiveSubtitleLanguage = "es";
            var signature = new SubtitleTrackSignature { Source = "embedded", Language = "spa", Codec = "srt", Label = "Spanish" };
            await (Task)Call(page, "PersistPrePlaySubtitlePreferenceAsync", signature, 1, "always")!;
            Require(wire.Subtitles.Count == 1 && !wire.Subtitles[0].TryGetProperty("show_forced_subtitles", out _), "Detail subtitle pick pinned inherited forced-subtitle state.");
            wire.GlobalForced = false;
            Require(!wire.EffectiveForced, "A later global forced-subtitle change did not reach a series without an override.");
            await playback.SaveSubtitlePrefsAsync("series", new() { SubtitleMode = "always", SubtitleTrackIndex = 1, ShowForcedSubtitles = true });
            await (Task)Call(page, "PersistPrePlaySubtitlePreferenceAsync", signature, 1, "always")!;
            Require(wire.EffectiveForced, "A subtitle pick removed a deliberate forced-subtitle override.");
            var range = typeof(ItemDetailPage).GetMethod("FormatTrackDynamicRange", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [new VersionVideoTrack { DolbyVision = "Profile 8", VideoRangeType = "DOVIWithHDR10Plus", Hdr10Plus = true }]) as string;
            Require(range?.Contains("HDR10 compatible", StringComparison.Ordinal) == true && range.Contains("HDR10+", StringComparison.Ordinal), "Dolby Vision/HDR10+ detail omits HDR10 compatibility.");
            Program.Log("PASS: actual native audio click saves signature, Auto deletes, failed save rolls back; subtitle preview respects Spanish/empty preferences; detail save omits inherited forced state and preserves intentional override; DV/HDR10+ detail includes HDR10 compatibility.");
        }
        finally
        {
            ((Flyout)page.FindName("AudioTracksFlyout")).Hide(); parent.Children.Remove(page); slot.SetValue(null, previous);
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 150 && !ready(); i++) await Task.Delay(20); Require(ready(), "Native track choice did not settle."); }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static T? Get<T>(object target, string name) => (T?)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private sealed class Services(IServiceProvider fallback, SiloApiClient api, CatalogApi catalog, PlaybackApi playback) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(SiloApiClient) ? api : type == typeof(CatalogApi) ? catalog : type == typeof(PlaybackApi) ? playback : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        public List<(HttpMethod Method, JsonElement? Body)> Audio = [];
        public List<JsonElement> Subtitles = [];
        public bool FailNext, GlobalForced = true;
        private bool? _forcedOverride;
        public bool EffectiveForced => _forcedOverride ?? GlobalForced;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "oct24-track-choices.invalid") throw new InvalidOperationException("Native track fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            JsonElement? body = request.Content == null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct)).RootElement.Clone();
            if (path.Contains("/audio-prefs/", StringComparison.Ordinal))
            {
                Audio.Add((request.Method, body));
                if (FailNext) { FailNext = false; return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"code\":\"fixture_refused\",\"message\":\"Fixture refused\"}}") }; }
            }
            if (path.Contains("/subtitle-prefs/", StringComparison.Ordinal) && body.HasValue)
            {
                Subtitles.Add(body.Value);
                if (body.Value.TryGetProperty("show_forced_subtitles", out var forced)) _forcedOverride = forced.GetBoolean();
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
