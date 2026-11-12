using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
        var previewRefreshers = new Dictionary<int, Action>();
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
                string.IsNullOrWhiteSpace(chapter.Title) ? $"Chapter {chapter.Index + 1}" : chapter.Title,
                chapter.StartSeconds, chapter.EndSeconds, chapter.ThumbnailUrl)).ToArray();
        int ActiveChapter() => Array.FindIndex(chapters,
            chapter => Position >= chapter.Start && Position < chapter.End);
        IReadOnlyList<PlaybackMenuChoice> Choices()
        {
            var canSeek = CanSeek() && !IsLoading && !_switchingContent;
            return chapters.Select((chapter, index) =>
            {
                var valid = double.IsFinite(chapter.Start) && chapter.Start >= 0;
                return new PlaybackMenuChoice(chapter.Id, chapter.Title, ChapterMenuTime(chapter.Start), [],
                    IsCurrent: index == active, IsEnabled: canSeek && valid,
                    DisabledReason: IsLoading || _switchingContent ? "A playback change is already in progress." : !canSeek ? "Only the host can seek the room." : !valid ? "Chapter unavailable." : "");
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
                // A ready URL is image state, not visible row text. The host
                // intentionally retains text-identical containers.
                foreach (var refreshPreview in previewRefreshers.Values.ToArray()) refreshPreview();
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
            var placeholder = new TextBlock
            {
                Text = "CH", Opacity = .45, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var image = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
            var content = new Grid();
            content.Children.Add(placeholder); content.Children.Add(image);
            var border = new Border
            {
                Width = 80, Height = 48, CornerRadius = new CornerRadius(4), Child = content,
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 36, 36, 36)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(border, "Chapter preview: " + chapter.Title);
            var index = chapter.Index;
            string? acceptedUrl = null;
            CancellationTokenSource? previewLifetime = null;
            async void RefreshPreview()
            {
                if (!Current() || !border.IsLoaded) return;
                var currentChapter = chapters.FirstOrDefault(item => item.Index == index);
                var url = currentChapter?.ThumbnailUrl;
                if (string.IsNullOrWhiteSpace(url) || (image.Source != null && acceptedUrl == url)) return;
                previewLifetime?.Cancel(); previewLifetime?.Dispose();
                var loading = CancellationTokenSource.CreateLinkedTokenSource(token);
                previewLifetime = loading;
                bool AcceptImage() => !loading.IsCancellationRequested && Current() && border.XamlRoot != null &&
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
            Action refresh = RefreshPreview;
            border.Loaded += (_, _) => { previewRefreshers[index] = refresh; refresh(); };
            border.Unloaded += (_, _) =>
            {
                if (previewRefreshers.TryGetValue(index, out var current) && current == refresh) previewRefreshers.Remove(index);
                previewLifetime?.Cancel();
                previewLifetime?.Dispose();
                previewLifetime = null;
            };
            return border;
        }
        try
        {
            menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, "Chapters", Choices(), Current,
                Invoke, Closed, root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root),
                leadingContent: Preview, allowDisabledTraversal: true);
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

    private static string ChapterMenuTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "Unavailable";
        var span = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds - 1));
        return span.TotalHours >= 1
            ? $"{(long)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(long)span.TotalMinutes}:{span.Seconds:00}";
    }
}

