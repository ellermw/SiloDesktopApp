using System.Globalization;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Services;

// The production OSC's interface text. This part depends only on Core so the
// unit tests compile it on its own and send its payload to the real Lua.
public partial class PlayerService
{
    /// <summary>
    /// Builds the explicit "osc-set-locale" payload for silo-osc.lua: ordinary
    /// script-message string arguments (generation, language, direction, then
    /// key/value pairs). Values are catalog text; the OSC substitutes named
    /// placeholders as plain text and never executes them.
    /// </summary>
    internal static class OscLocalization
    {
        internal const string MessageName = "osc-set-locale";

        /// <summary>Catalog keys the OSC draws, with the placeholders the OSC fills itself.</summary>
        internal static readonly IReadOnlyList<(string Key, string[] Placeholders)> Texts =
        [
            ("web.player.exit", []),
            ("web.player.chaptersMenu.trigger", []),
            ("web.player.chaptersMenu.heading", []),
            ("web.player.audioMenu.trigger", []),
            ("web.player.audioMenu.heading", []),
            ("web.player.audioMenu.defaultBadge", []),
            ("web.player.audioMenu.trackFallback", ["number"]),
            ("web.audiobooks.chapter.fallbackTitle", ["number"]),
            ("web.subtitles.menu.off", []),
            ("web.home.preferences.valueOn", []),
            ("web.player.quality.title", []),
            ("web.player.quality.auto", []),
            ("web.player.quality.version", []),
            ("web.player.quality.versionPlaying", []),
            ("web.player.quality.versionRequested", []),
            ("settingsManifest.downloads.default_quality.option.original", []),
            ("settingsManifest.player.video_gravity.option.fit", []),
            ("web.player.controls.fillScreen", []),
            ("web.player.controls.playbackInfo", []),
            ("web.player.controls.pictureInPictureItem", []),
            ("web.player.controls.nextEpisode", []),
            ("web.common.format.channels.stereo", []),
            ("web.common.format.bitrate.mbps", ["rate"]),
            ("web.common.format.bitrate.kbps", ["rate"]),
            ("web.common.format.sampleRate", ["rate"]),
            ("web.common.format.size.bytes", ["size"]),
            ("web.common.format.size.kib", ["size"]),
            ("web.common.format.size.mib", ["size"]),
            ("web.common.format.size.gib", ["size"]),
            ("web.markers.kind.intro", []),
            ("web.markers.kind.recap", []),
            ("web.markers.kind.credits", []),
            ("web.markers.kind.preview", []),
            ("introSkip.pill.skip", []),
            ("introSkip.pill.undo", []),
            ("marker.skipRecap", []),
            ("marker.skipCredits", []),
            ("web.player.nextEpisode.seasonEpisode", ["season", "episode"]),
            ("web.player.nextEpisode.playNow", []),
            ("common.cancel", []),
            ("common.retry", []),
            ("common.loading", []),
            ("web.player.volume.valueText", ["percent"]),
            ("web.player.subtitles.preparingTranslation", ["label"]),
            ("web.player.subtitles.preparingTranslated", []),
            ("web.player.watchTogether.title", []),
            ("web.player.watchTogether.codePending", []),
            ("web.watchTogether.connection.connected", []),
            ("web.watchTogether.connection.connecting", []),
            ("web.player.connection.reconnecting", []),
            ("watchParty.connection.disconnected", []),
            ("web.player.watchTogether.policy.guestsCanPause", []),
            ("web.player.watchTogether.policy.hostOnly", []),
            ("web.player.watchTogether.policy.youCanPause", []),
            ("web.player.watchTogether.policy.hostControls", []),
            ("web.player.watchTogether.invite", []),
            ("web.player.watchTogether.setHostOnly", []),
            ("web.player.watchTogether.allowPause", []),
            ("web.player.watchTogether.end", []),
            ("web.player.watchTogether.member.syncing", []),
            ("web.player.room.lowerQuality", []),
            ("web.player.room.catchingUp", []),
            ("web.player.room.syncing", []),
            ("web.watchTogether.endDialog.title", []),
            ("web.watchTogether.endDialog.description", []),
            ("watchParty.end.confirm", []),
            ("web.player.info.title", []),
            ("web.player.info.section.player", []),
            ("web.player.info.section.video", []),
            ("web.player.info.section.stream", []),
            ("web.player.info.section.source", []),
            ("web.player.info.player", []),
            ("web.player.info.playMethod", []),
            ("web.player.info.protocol", []),
            ("web.player.info.streamType", []),
            ("web.player.info.autoSwitchedFrom", []),
            ("web.player.info.droppedFrames", []),
            ("web.player.info.corruptedFrames", []),
            ("web.player.info.videoCodec", []),
            ("web.player.info.audioCodec", []),
            ("web.player.info.container", []),
            ("web.player.info.size", []),
            ("web.player.info.bitrate", []),
            ("web.player.info.videoBitrate", []),
            ("web.player.info.videoRangeType", []),
            ("web.player.info.colorRange", []),
            ("web.player.info.colorRangeFull", []),
            ("web.player.info.colorRangeLimited", []),
            ("web.player.info.colorRangeUnknown", []),
            ("web.languageName.unknown", []),
            ("web.player.info.audioBitrate", []),
            ("web.player.info.audioChannels", []),
            ("web.player.info.audioSampleRate", []),
            ("web.subtitles.menu.source.embedded", []),
            ("web.subtitles.menu.source.external", []),
            ("web.subtitles.menu.source.downloaded", []),
            ("web.subtitles.menu.syncing", []),
            ("web.subtitles.menu.syncToAudio", []),
            ("web.subtitles.menu.resetTiming", []),
            ("web.subtitles.menu.delay", []),
            ("web.subtitles.menu.delayValue", ["delay"]),
            ("web.subtitles.menu.resetDelay", []),
            ("web.subtitles.menu.addSubtitles", []),
            ("web.subtitles.menu.translateWithAi", []),
            ("web.subtitles.menu.appearance", []),
            ("bufferingQuality.prompt.title", []),
            ("bufferingQuality.cantLower", []),
            ("bufferingQuality.lowered", []),
            ("bufferingQuality.prompt.lower", []),
            ("bufferingQuality.prompt.keep", []),
            ("bufferingQuality.restore", []),
        ];

        /// <summary>Count-dependent messages, rendered per count ("key#N") because plural rules belong to the catalog.</summary>
        internal static readonly IReadOnlyList<(string Key, int Max)> Counts =
        [
            ("web.player.nextEpisode.upNextIn", 120),
            ("web.player.watchTogether.viewerCount", 99),
            ("web.common.format.channels.count", 32),
        ];

        /// <summary>ISO 639-1 media language codes the OSC can name in the interface language.</summary>
        internal static readonly IReadOnlyList<string> LanguageCodes =
        [
            "ar", "cs", "da", "de", "el", "en", "es", "fa", "fi", "fr", "he", "hi", "hu", "id", "is", "it",
            "ja", "ko", "nl", "no", "pl", "pt", "ro", "ru", "sv", "ta", "te", "th", "tr", "uk", "vi", "zh",
        ];

        internal static string[] Build(AppLocale locale)
        {
            ArgumentNullException.ThrowIfNull(locale);
            var args = new List<string>(Texts.Count * 2 + 400)
            {
                locale.Generation.ToString(CultureInfo.InvariantCulture),
                locale.Language,
                locale.IsRightToLeft ? "rtl" : "ltr",
            };
            void Add(string key, string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                args.Add(key); args.Add(value);
                if (!key.StartsWith("@", StringComparison.Ordinal) && !key.StartsWith("language.", StringComparison.Ordinal))
                {
                    args.Add("upper:" + key);
                    args.Add(value.ToUpper(CultureInfo.GetCultureInfo(locale.Language)));
                }
            }
            foreach (var (key, placeholders) in Texts)
            {
                // Placeholders stay literal for the OSC to fill with live values.
                var template = placeholders.Length == 0 ? null
                    : placeholders.ToDictionary(name => name, name => (object?)("{" + name + "}"), StringComparer.Ordinal);
                var value = locale.Get(key, template, fallback: "");
                if (placeholders.All(name => value.Contains("{" + name + "}", StringComparison.Ordinal))) Add(key, value);
            }
            foreach (var (key, max) in Counts)
                for (var count = 0; count <= max; count++)
                    Add($"{key}#{count.ToString(CultureInfo.InvariantCulture)}", locale.Get(key, new Dictionary<string, object?>
                    {
                        ["count"] = count,
                        ["count_text"] = count.ToString("N0", locale.FormatCulture),
                    }, fallback: ""));
            foreach (var code in LanguageCodes) Add("language." + code, locale.Get("language." + code, fallback: ""));
            var numbers = locale.FormatCulture.NumberFormat;
            var dates = locale.FormatCulture.DateTimeFormat;
            Add("@decimal", numbers.NumberDecimalSeparator);
            Add("@group", numbers.NumberGroupSeparator);
            Add("@am", dates.AMDesignator);
            Add("@pm", dates.PMDesignator);
            return [.. args];
        }
    }
}
