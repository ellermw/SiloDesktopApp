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
    private readonly CancellationTokenSource _lifetimeCts = new();

    public MangaFilesDialog(string contentId, string? title)
    {
        _contentId = contentId;
        Title = string.IsNullOrWhiteSpace(title) ? "Files" : $"{title} — Files";
        CloseButtonText = "Close";
        Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];

        var root = new Grid { Width = 620, MaxHeight = 680, RowSpacing = 14 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _description = new TextBlock
        {
            Text = "Local files backing this series.",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        };
        root.Children.Add(_description);
        _body = new StackPanel { Spacing = 14 };
        var scroller = new ScrollViewer
        {
            Content = _body,
            MaxHeight = 600,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        _progress = new ProgressRing
        {
            IsActive = true,
            Width = 26,
            Height = 26,
            Margin = new Thickness(0, 28, 0, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _body.Children.Add(_progress);
        Content = root;
        Opened += OnOpened;
        Closed += (_, _) => _lifetimeCts.Cancel();
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        try
        {
            var data = await App.Services.GetRequiredService<CatalogApi>()
                .GetMangaSeriesFilesAsync(_contentId, _lifetimeCts.Token);
            Render(data);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch
        {
            _body.Children.Clear();
            _body.Children.Add(new TextBlock
            {
                Text = "Couldn't load file details. Try again later.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFC, 0xA5, 0xA5)),
                Margin = new Thickness(0, 18, 0, 18),
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
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new FontIcon
                {
                    Glyph = "\uE8B7",
                    FontSize = 14,
                    Margin = new Thickness(0, 1, 0, 0),
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                row.Children.Add(new TextBlock
                {
                    Text = path,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                paths.Children.Add(row);
            }
            _body.Children.Add(paths);
        }

        if (files.Count == 0)
        {
            _body.Children.Add(new TextBlock
            {
                Text = "No files found.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 12, 0, 12),
            });
            return;
        }

        var list = new StackPanel();
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = list,
        };
        foreach (var file in files)
        {
            var row = new Grid { Padding = new Thickness(12, 9, 12, 9), ColumnSpacing = 12 };
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
                ? $"Volume {number:0.##}"
                : token;
        }
        if (file.ChapterIndex.HasValue)
            return $"Chapter {file.ChapterIndex:0.##}";
        return string.IsNullOrWhiteSpace(file.Title) ? "Chapter" : file.Title.Trim();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
