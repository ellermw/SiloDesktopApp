using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using System.Text.RegularExpressions;

namespace SiloPlayer.Controls;

/// <summary>Current WebUI manga file inspector.</summary>
public sealed partial class MangaFilesDialog : ContentDialog
{
    private static readonly Regex VolumeToken = new(@"^[vV]?(\d+(?:\.\d+)?)$", RegexOptions.Compiled);
    private readonly string _contentId;
    private readonly TextBlock _description;
    private readonly StackPanel _body;
    private readonly ProgressRing _progress;
    private readonly Grid _root;
    private readonly ScrollViewer _scroller;
    private readonly CancellationTokenSource _lifetimeCts = new();

    public MangaFilesDialog(string contentId, string? title)
    {
        _contentId = contentId;
        var heading = string.IsNullOrWhiteSpace(title) ? "Files" : $"{title} — Files";
        var header = new Grid { ColumnSpacing = 24 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titleText = new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTipService.SetToolTip(titleText, heading); header.Children.Add(titleText);
        var close = RefreshMetadataDialog.CornerCloseButton(); close.Click += (_, _) => Hide(); Grid.SetColumn(close, 1); header.Children.Add(close); Title = header;
        Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
        Resources["ContentDialogMaxWidth"] = 672d; Resources["ContentDialogMinWidth"] = 0d; Resources["ContentDialogCornerRadius"] = new CornerRadius(8);

        var root = _root = new Grid { RowSpacing = 16 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _description = new TextBlock
        {
            Text = "Local files backing this series.",
            FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        };
        root.Children.Add(_description);
        _body = new StackPanel { Spacing = 16 };
        var scroller = _scroller = new ScrollViewer
        {
            Content = _body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        _progress = new ProgressRing
        {
            IsActive = true,
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 40, 0, 40),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _body.Children.Add(_progress);
        Content = root;
        Opened += OnOpened;
        Closed += (_, _) => { _lifetimeCts.Cancel(); if (XamlRoot != null) XamlRoot.Changed -= RootChanged; };
        var escape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, args) => { Hide(); args.Handled = true; }; KeyboardAccelerators.Add(escape);
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        Reflow(); if (XamlRoot != null) XamlRoot.Changed += RootChanged;
        try
        {
            var data = await App.Services.GetRequiredService<CatalogApi>()
                .GetMangaSeriesFilesAsync(_contentId, _lifetimeCts.Token);
            if (!_lifetimeCts.IsCancellationRequested) Render(data);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch
        {
            if (_lifetimeCts.IsCancellationRequested) return;
            _progress.IsActive = false;
            _body.Children.Clear();
            _body.Children.Add(new TextBlock
            {
                Text = "Couldn't load file details. Try again later.",
                FontSize = 14, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
                Margin = new Thickness(0, 24, 0, 24),
            });
        }
    }

    private void Render(MangaSeriesFiles data)
    {
        _progress.IsActive = false;
        _body.Children.Clear();
        var files = data.Files ?? [];
        var totalBytes = files.Sum(file => file.FileSize);
        _description.Text = files.Count > 0
            ? $"{files.Count} {(files.Count == 1 ? "file" : "files")} · {FormatFileSize(totalBytes)}"
            : "Local files backing this series.";

        if (data.FolderPaths?.Count > 0)
        {
            var paths = new StackPanel { Spacing = 6 };
            foreach (var path in data.FolderPaths)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new FontIcon
                {
                    Glyph = "\uE8B7",
                    FontSize = 14,
                    Margin = new Thickness(0, 1, 0, 0),
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                var pathText = new TextBlock
                {
                    Text = path,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                };
                Grid.SetColumn(pathText, 1); row.Children.Add(pathText);
                paths.Children.Add(row);
            }
            _body.Children.Add(paths);
        }

        if (files.Count == 0)
        {
            _body.Children.Add(new TextBlock
            {
                Text = "No files found.",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 16, 0, 16),
            });
            return;
        }

        var list = new StackPanel();
        var border = new Border
        {
            BorderBrush = MutedBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = list,
        };
        foreach (var file in files)
        {
            if (list.Children.Count > 0) list.Children.Add(new Border { Height = 1, Background = MutedBorderBrush() });
            var row = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = FileRowLabel(file),
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var name = new TextBlock
            {
                Text = file.FileName,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTipService.SetToolTip(name, file.FilePath ?? file.FileName);
            var size = new TextBlock
            {
                Text = file.FileSize > 0 ? FormatFileSize(file.FileSize) : "",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(size, 2);
            row.Children.Add(label);
            row.Children.Add(name);
            row.Children.Add(size);
            list.Children.Add(row);
        }
        _body.Children.Add(border);
    }

    private static string FileRowLabel(MangaChapterFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.Volume))
        {
            var token = file.Volume.Trim();
            var match = VolumeToken.Match(token);
            return match.Success && double.TryParse(
                    match.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number)
                ? $"Volume {number.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : token;
        }
        if (file.ChapterIndex.HasValue)
            return $"Chapter {file.ChapterIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return string.IsNullOrWhiteSpace(file.Title) ? "Chapter" : file.Title.Trim();
    }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Reflow();
    private void Reflow()
    {
        if (XamlRoot == null) return;
        var alignment = XamlRoot.Size.Width < 640 ? TextAlignment.Center : TextAlignment.Left;
        if (Title is Grid header && header.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock heading) heading.TextAlignment = alignment;
        _description.TextAlignment = alignment;
        _root.Width = Math.Min(624, Math.Max(0, XamlRoot.Size.Width - 80));
        MaxHeight = Math.Max(100, XamlRoot.Size.Height * .85);
        _root.MaxHeight = Math.Max(60, MaxHeight - 100);
        _scroller.MaxHeight = Math.Max(40, _root.MaxHeight - 40);
    }
    private static Brush MutedBorderBrush() => Application.Current.Resources["BorderBrush"] is SolidColorBrush brush ? new SolidColorBrush(brush.Color) { Opacity = .4 } : (Brush)Application.Current.Resources["BorderBrush"];
    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "";
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024d * 1024 * 1024)).ToString("0.0", culture) + " GB";
        if (bytes >= 1024L * 1024) return (bytes / (1024d * 1024)).ToString("0.0", culture) + " MB";
        if (bytes >= 1024) return (bytes / 1024d).ToString("0.0", culture) + " KB";
        return bytes.ToString(culture) + " B";
    }
}
