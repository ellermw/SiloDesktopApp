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
        bool CanSeek() => !IsWatchTogetherPlayback ||
            App.Services.GetService<WatchTogetherCoordinator>()?.ActiveRoom?.Room?.SelfCanManageRoom == true;
        bool Owned() => owner.IsCurrent() && ReferenceEquals(version, ActiveVersion) &&
            ReferenceEquals(session, _playbackManager?.CurrentSession);
        bool Current() => !closed && Owned() && _nativeMenuGate.IsCurrent(request);
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
            var canSeek = CanSeek();
            return chapters.Select((chapter, index) =>
            {
                var valid = double.IsFinite(chapter.Start) && chapter.Start >= 0;
                return new PlaybackMenuChoice(chapter.Id, chapter.Title, ChapterMenuTime(chapter.Start), [],
                    IsCurrent: index == active, IsEnabled: canSeek && valid,
                    DisabledReason: !canSeek ? "Only the host can seek the room." : !valid ? "Chapter unavailable." : "");
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
            selected = reason == PlaybackMenuCloseReason.Selection;
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
            if (!selected || !Owned() || !CanSeek()) return;
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
            CancellationTokenSource? previewLifetime = null;
            border.Loaded += async (_, _) =>
            {
                if (!Current() || string.IsNullOrWhiteSpace(chapter.ThumbnailUrl)) return;
                previewLifetime?.Cancel(); previewLifetime?.Dispose();
                var loading = CancellationTokenSource.CreateLinkedTokenSource(token);
                previewLifetime = loading;
                bool AcceptImage() => !loading.IsCancellationRequested && Current() && border.XamlRoot != null &&
                    version.Chapters?.Any(current => current.Index == chapter.Index &&
                        string.Equals(current.ThumbnailUrl, chapter.ThumbnailUrl, StringComparison.Ordinal)) == true;
                try
                {
                    var path = await App.Services.GetRequiredService<ImageService>().GetImageDiskPathAsync(
                        $"chapter_{ContentId}_{chapter.Index}", "chapter", chapter.ThumbnailUrl,
                        App.Services.GetRequiredService<HttpClient>(), loading.Token);
                    if (string.IsNullOrWhiteSpace(path) || !AcceptImage()) return;
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    if (!AcceptImage()) return;
                    using var stream = await file.OpenReadAsync();
                    if (!AcceptImage()) return;
                    // Stream decode sniffs the image bytes, including WebP
                    // cached with ImageService's generic .img extension.
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    if (!AcceptImage()) return;
                    image.Source = bitmap;
                    image.Visibility = Visibility.Visible;
                    placeholder.Visibility = Visibility.Collapsed;
                }
                catch (OperationCanceledException) { }
                catch (Exception) { /* Preview failure retains the usable chapter and fallback. */ }
            };
            border.Unloaded += (_, _) =>
            {
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
                leadingContent: Preview);
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

