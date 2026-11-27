using System.Text.Json;
using SiloPlayer;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static partial class PlaybackHudCompletionNativeFixture
{
    private static async Task VerifyLivePlaybackSettings(PlayerService service, Wire wire, MainWindow window,
        SettingsViewModel settings)
    {
        var mpv = service.Mpv!;
        var failures = new List<string>();
        async Task<bool> Expect(string label, Func<JsonElement, bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < until)
            {
                if (condition(await QueryOsc(mpv)))
                {
                    Program.Log("PASS: " + label);
                    return true;
                }
                await Task.Delay(25);
            }
            failures.Add(label);
            Program.Log("FAIL: " + label);
            return false;
        }

        void Realtime(string key, string profile = "fixture") => Call(window, "OnShellEvent", "user_settings",
            "user_settings.changed", JsonSerializer.SerializeToElement(new { key, profile_id = profile }));

        bool Credits(JsonElement data) => data.GetProperty("auto_skip_credits").GetBoolean();
        bool Prompt(JsonElement data) => data.GetProperty("skip_label").GetString() == "Skip Credits";
        Call(window, "EnsureNotificationSubscription", "live-settings-fixture");
        var starts = wire.Starts;
        var load = (long)Field(service, "_fileLoadGeneration")!;
        mpv.Seek(20.5);
        await Expect("A real credits marker shows Skip Credits with auto-skip disabled.", Prompt);
        settings.AutoSkipCredits = true;
        await Until(() => wire.CreditsWrites == 1, "Settings did not persist the credits change.");
        if (await Expect("Settings sends enabled credits auto-skip to the current OSC.", Credits))
            await Expect("Enabling auto-skip during the visible credits prompt skips the remaining marker.", _ => mpv.Position >= 27.5);

        wire.AutoSkipCredits = false;
        Realtime("playback.auto_skip_credits");
        await Expect("Realtime disables credits auto-skip in the current OSC.", data => !Credits(data));
        wire.AutoSkipCredits = true;
        Realtime("playback.auto_skip_credits", "other-profile");
        await Task.Delay(350);
        await Expect("Another profile cannot update the current OSC.", data => !Credits(data));
        Realtime("playback.auto_skip_credits");
        await Expect("Realtime enables credits auto-skip in the current OSC.", Credits);

        wire.IntroMode = "never";
        wire.AutoPlay = false;
        wire.NextUpSeconds = 10;
        wire.ShowMarkerSource = true;
        wire.VideoSkipForward = 45;
        wire.PlaybackSpeed = 1.25;
        wire.SubtitleOffsetMs = 900;
        wire.BufferingMode = "never";
        var reads = wire.LiveSettingsReads;
        Realtime("playback.intro_skip_mode");
        Realtime("playback.auto_play_next");
        Realtime("playback.next_up_prompt_seconds");
        Realtime("playback.show_marker_source");
        Realtime("player.video_skip_forward_seconds");
        Realtime("player.playback_speed");
        Realtime("player.subtitle_sync_ms");
        Realtime("playback.buffering_quality_mode");
        await Expect("Realtime updates intro mode.", data => data.GetProperty("intro_mode").GetString() == "never");
        await Expect("Realtime updates autoplay.", data => data.GetProperty("autoplay").GetString() == "false");
        await Expect("Realtime updates the host's Next Up lead time.", _ => (int?)Field(service, "_nextUpPromptSeconds") == 10);
        await Expect("Realtime updates the credits marker details in OSC.", data => data.GetProperty("credits_details").GetString() != "");
        await Expect("Realtime updates the OSC seek interval.", data => data.GetProperty("forward").GetInt32() == 45);
        await Expect("Realtime updates native playback speed.", _ => Math.Abs(mpv.GetPropertyDouble("speed") - 1.25) < .01);
        await Expect("Realtime updates native subtitle timing.", _ => Math.Abs(mpv.GetPropertyDouble("sub-delay") - .9) < .01);
        await Expect("Realtime updates the buffering policy without opening a new stream.",
            _ => Field(service, "_playbackManager") is PlaybackManager { BufferingQualityMode: "never" });
        Check(wire.LiveSettingsReads == reads + 1, "The realtime burst did not coalesce into one effective-settings read.");
        Program.Log("PASS: one effective-settings read handled the realtime burst.");

        settings.IntroSkipMode = "always";
        settings.NextUpPromptSeconds = 60;
        // Move the Settings property to the wire's false value before re-enabling it.
        settings.AutoPlayNext = false;
        await Until(() => wire.AutoPlayProfileWrites == 1, "The Settings autoplay setter did not save.");
        settings.AutoPlayNext = true;
        await settings.OpenMarkerDetailsSettingsAsync();
        settings.ShowMarkerSource = false;
        await Expect("Settings updates intro mode.", data => data.GetProperty("intro_mode").GetString() == "always");
        await Expect("Settings updates autoplay.", data => data.GetProperty("autoplay").GetString() == "true");
        await Expect("Settings updates the host's Next Up lead time.", _ => (int?)Field(service, "_nextUpPromptSeconds") == 60);
        await Expect("Settings clears credits marker details in OSC.", data => data.GetProperty("credits_details").GetString() == "");
        Check(wire.Starts == starts && (long)Field(service, "_fileLoadGeneration")! == load,
            "Applying a setting replaced the native file or stream.");

        await service.PlayAsync("episode-2", fromStart: true);
        await WaitForOscOwner(service);
        await Expect("The next file rereads the last credits setting without an app restart.", Credits);
        settings.AutoSkipCredits = false;
        await Until(() => wire.CreditsWrites == 2, "Settings could not disable credits auto-skip.");
        if (await Expect("Disabling auto-skip reaches the successor file's OSC.", data => !Credits(data)))
        {
            mpv.Seek(20.5);
            await Expect("With auto-skip turned off, the credits prompt shows and playback stays inside the marker.",
                data => Prompt(data) && mpv.Position >= 20 && mpv.Position < 27);
            await Task.Delay(500);
            Check(mpv.Position is >= 20 and < 27, "Disabled credits auto-skip jumped ahead after showing the prompt.");
        }

        await service.CloseAsync();
        reads = wire.LiveSettingsReads;
        settings.AutoSkipCredits = true;
        await Until(() => wire.CreditsWrites == 3, "The idle Settings change did not save.");
        await Task.Delay(350);
        Check(wire.LiveSettingsReads == reads, "An idle setting change started a live playback read.");
        wire.IntroMode = "ask";
        wire.SubtitleOffsetMs = 1200;
        await settings.RefreshPlaybackSettingsSectionAsync(subtitles: true, CancellationToken.None);
        Check(settings.IntroSkipMode == "ask" && settings.SubtitleOffsetMs == 1200,
            "Reopening the player settings retained old playback or subtitle values.");
        Program.Log("PASS: reopening player settings reads current values while idle.");
        await service.PlayAsync("episode-3", fromStart: true);
        await WaitForOscOwner(service);
        await Expect("A Settings change made while idle applies at the next start.", Credits);
        Check(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
