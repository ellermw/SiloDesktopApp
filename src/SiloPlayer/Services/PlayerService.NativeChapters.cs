using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services.PlaybackMenus;
using Windows.Storage;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private sealed record NativeChapterChoice(int Index, string Title, double Start, double End, string? ThumbnailUrl)
    {
        internal string Id => "chapter:" + Index.ToString(CultureInfo.InvariantCulture);
    }

    private PlaybackChoiceMenu CreateNativeChaptersMenu(NativeMenuOwner owner, PlaybackMenuRequest request,
        Action<PlaybackMenuCloseReason> finish)
    {
        var version = ActiveVersion ?? throw new InvalidOperationException("Chapter source unavailable.");
        var session = _playbackManager?.CurrentSession;
        var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        var dispatcher = App.MainWindowInstance?.DispatcherQueue ?? _nativeMenuDispatcher;
        PlaybackChoiceMenu? menu = null;
        var closed = false;
        var selected = false;
        var chapters = Snapshot();
        var active = ActiveChapter();
        var previewCache = new Dictionary<(int Index, string Url), BitmapImage>();
        var previewOrder = new Queue<(int Index, string Url)>();
        bool CanSeek() => !IsWatchTogetherPlayback ||
            App.Services.GetService<WatchTogetherCoordinator>()?.ActiveRoom?.Room?.SelfCanManageRoom == true;
        bool Owned() => owner.IsCurrent() && ReferenceEquals(version, ActiveVersion) &&
            session?.SessionId == _playbackManager?.CurrentSession?.SessionId;
        var openedIdle = !IsLoading && !_switchingContent;
        bool Current() => !closed && Owned() && (openedIdle || _sessionHasFramed) &&
            openedIdle == (!IsLoading && !_switchingContent) && _nativeMenuGate.IsCurrent(request);
        NativeChapterChoice[] Snapshot() => (version.Chapters ?? [])
            .Where(chapter => chapter.Index >= 0)
            .DistinctBy(chapter => chapter.Index)
            .Select(chapter => new NativeChapterChoice(chapter.Index,
                string.IsNullOrWhiteSpace(chapter.Title) ? AppStrings.Get("web.audiobooks.chapter.fallbackTitle",
                    new Dictionary<string, object?> { ["number"] = chapter.Index + 1 }, fallback: $"Chapter {chapter.Index + 1}") : chapter.Title,
                chapter.StartSeconds, chapter.EndSeconds, chapter.ThumbnailUrl)).ToArray();
        int ActiveChapter() => Array.FindIndex(chapters,
            chapter => Position >= chapter.Start && Position < chapter.End);
        IReadOnlyList<PlaybackMenuChoice> Choices()
        {
            var pictures = chapters.Any(chapter => !string.IsNullOrWhiteSpace(chapter.ThumbnailUrl));
            var canSeek = CanSeek() && !IsLoading && !_switchingContent;
            return chapters.Select((chapter, index) =>
            {
                var valid = double.IsFinite(chapter.Start) && chapter.Start >= 0;
                // ChaptersMenu.tsx: thumbnail or number, the title on one line, then
                // the start time; the chapter playing reads "0:00 · Now" in the accent.
                return new PlaybackMenuChoice(chapter.Id, chapter.Title, ChapterMenuTime(chapter.Start), [],
                    IsCurrent: index == active, IsEnabled: canSeek && valid,
                    DisabledReason: IsLoading || _switchingContent ? "A playback change is already in progress." : !canSeek ? AppStrings.Get("web.player.room.onlyHostCanSeek", fallback: "Only the host can seek the room.") : !valid ? "Chapter unavailable." : "")
                    { Check = false, SingleLine = true, MinHeight = 64, PreviewUrl = pictures ? chapter.ThumbnailUrl ?? "" : null, Accent = index == active ? ChapterNowText() : "" };
            }).ToArray();
        }
        void Refresh()
        {
            void Apply()
            {
                if (!Current() || menu == null) return;
                chapters = Snapshot();
                active = ActiveChapter();
                menu.UpdateChoices(Choices());
            }
            if (dispatcher?.HasThreadAccess == true) Apply();
            else dispatcher?.TryEnqueue(Apply);
        }
        void PositionUpdated(double position)
        {
            void Apply()
            {
                if (!Current() || menu == null) return;
                var next = ActiveChapter();
                if (next == active) return;
                active = next;
                menu.UpdateChoices(Choices());
            }
            if (dispatcher?.HasThreadAccess == true) Apply();
            else dispatcher?.TryEnqueue(Apply);
        }
        void Closed(PlaybackMenuCloseReason reason)
        {
            if (closed) return;
            closed = true;
            selected = reason is PlaybackMenuCloseReason.Selection or PlaybackMenuCloseReason.PointerSelection;
            ChaptersChanged -= Refresh;
            PositionChanged -= PositionUpdated;
            lifetime.Cancel();
            lifetime.Dispose();
            finish(reason);
        }
        void Invoke(string id)
        {
            // Select-close retires the presentation gate before this delegate.
            // Keep playback identity and the accepted chapter source authoritative.
            if (!selected || !Owned() || IsLoading || _switchingContent || !CanSeek()) return;
            var captured = chapters.FirstOrDefault(chapter => chapter.Id == id);
            var current = version.Chapters?.FirstOrDefault(chapter => chapter.Index == captured?.Index);
            if (captured == null || current == null || current.StartSeconds != captured.Start ||
                !double.IsFinite(captured.Start) || captured.Start < 0) return;
            RequestUserSeek(captured.Start, forceResume: !owner.Mpv.IsPaused);
        }
        FrameworkElement? Preview(PlaybackMenuChoice choice)
        {
            var chapter = chapters.FirstOrDefault(chapter => chapter.Id == choice.Id);
            if (chapter == null) return null;
            // Thumbnails only when the file has them; without any the list leads
            // with chapter numbers instead of a column of empty frames.
            var number = new TextBlock
            {
                Text = (Array.IndexOf(chapters, chapter) + 1).ToString(AppStrings.FormatCulture), Width = 28, FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = choice.IsCurrent ? PlaybackMenuStyle.Lamp() : PlaybackMenuStyle.Ash,
            };
            var placeholder = new TextBlock
            {
                Text = "", Opacity = .45, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var image = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
            var content = new Grid();
            content.Children.Add(placeholder); content.Children.Add(image);
            // .hud-chapter-thumb: 5.5rem wide, 16:9, and an accent ring on the chapter playing.
            var border = new Border
            {
                Width = 88, Height = 50, CornerRadius = new CornerRadius(6), Child = content,
                Background = PlaybackMenuStyle.Rgb(15, 255, 255, 255),
                BorderThickness = new Thickness(choice.IsCurrent ? 2 : 1),
                BorderBrush = choice.IsCurrent ? PlaybackMenuStyle.Lamp() : PlaybackMenuStyle.Rgb(20, 255, 255, 255),
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(border, "Chapter preview: " + chapter.Title);
            var slot = new Grid { VerticalAlignment = VerticalAlignment.Center };
            slot.Children.Add(number); slot.Children.Add(border);
            border.Visibility = choice.PreviewUrl != null ? Visibility.Visible : Visibility.Collapsed;
            number.Visibility = choice.PreviewUrl == null ? Visibility.Visible : Visibility.Collapsed;
            var index = chapter.Index;
            string? acceptedUrl = null;
            CancellationTokenSource? previewLifetime = null;
            async void RefreshPreview()
            {
                if (!Current() || slot.XamlRoot == null) return;
                var url = choice.PreviewUrl;
                if (string.IsNullOrWhiteSpace(url) || (image.Source != null && acceptedUrl == url)) return;
                previewLifetime?.Cancel(); previewLifetime?.Dispose();
                var loading = CancellationTokenSource.CreateLinkedTokenSource(token);
                previewLifetime = loading;
                bool AcceptImage() => !loading.IsCancellationRequested && Current() && slot.XamlRoot != null &&
                    version.Chapters?.Any(current => current.Index == index &&
                        string.Equals(current.ThumbnailUrl, url, StringComparison.Ordinal)) == true;
                try
                {
                    var key = (index, url);
                    if (!previewCache.TryGetValue(key, out var bitmap))
                    {
                        var path = await App.Services.GetRequiredService<ImageService>().GetImageDiskPathAsync(
                            $"chapter_{ContentId}_{index}", "chapter", url,
                            App.Services.GetRequiredService<HttpClient>(), loading.Token);
                        if (string.IsNullOrWhiteSpace(path) || !AcceptImage()) return;
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        if (!AcceptImage()) return;
                        using var stream = await file.OpenReadAsync();
                        if (!AcceptImage()) return;
                        // Stream decode sniffs bytes despite the cache's .img
                        // extension. Bound decoded preview memory per menu.
                        bitmap = new BitmapImage { DecodePixelWidth = 160 };
                        await bitmap.SetSourceAsync(stream);
                        if (!AcceptImage()) return;
                        previewCache[key] = bitmap;
                        previewOrder.Enqueue(key);
                        while (previewCache.Count > 24 && previewOrder.TryDequeue(out var oldest)) previewCache.Remove(oldest);
                    }
                    if (!AcceptImage()) return;
                    image.Source = bitmap;
                    acceptedUrl = url;
                    image.Visibility = Visibility.Visible;
                    placeholder.Visibility = Visibility.Collapsed;
                }
                catch (OperationCanceledException) { }
                catch (Exception) { /* Preview failure retains the usable chapter and fallback. */ }
            }
            slot.Loaded += (_, _) => RefreshPreview();
            slot.Unloaded += (_, _) =>
            {
                previewLifetime?.Cancel();
                previewLifetime?.Dispose();
                previewLifetime = null;
            };
            return slot;
        }
        try
        {
            menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, HudText("web.player.chaptersMenu.heading", "Chapters"), Choices(), Current,
                Invoke, Closed, root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root),
                leadingContent: Preview, allowDisabledTraversal: true);
            // Untitled chapter names and room reasons follow the interface language.
            menu.Localize(() => HudText("web.player.chaptersMenu.heading", "Chapters"), () => { chapters = Snapshot(); return Choices(); });
            ChaptersChanged += Refresh;
            PositionChanged += PositionUpdated;
            return menu;
        }
        catch
        {
            lifetime.Cancel(); lifetime.Dispose();
            throw;
        }
    }

    // The catalog words it "{time} · Now"; the list colours only the word.
    private static string ChapterNowText()
    {
        var text = AppStrings.Get("web.player.chaptersMenu.nowPlaying", new Dictionary<string, object?> { ["time"] = "" }, fallback: "Now")
            .Replace("·", "").Trim();
        return text.Length == 0 || text.Contains('<') || text.Contains('{') ? "Now" : text;
    }

    private static string ChapterMenuTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "Unavailable";
        var span = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds - 1));
        return span.TotalHours >= 1
            ? $"{(long)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(long)span.TotalMinutes}:{span.Seconds:00}";
    }
}

