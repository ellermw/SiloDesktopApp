using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Controls;

/// <summary>WebUI-parity metadata refresh chooser with pending and error states.</summary>
public sealed class RefreshMetadataDialog : ContentDialog
{
    private readonly Func<string, Task> _onConfirm;
    private readonly Button _quickButton;
    private readonly Button _completeButton;
    private readonly TextBlock _errorText;
    private readonly List<(FontIcon Icon, ProgressRing Spinner)> _choiceIndicators = [];
    private bool _pending;

    public RefreshMetadataDialog(Func<string, Task> onConfirm)
    {
        _onConfirm = onConfirm;
        Title = "Refresh Metadata";
        CloseButtonText = "Cancel";
        Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
        Closing += (_, args) =>
        {
            if (_pending)
                args.Cancel = true;
        };

        var root = new StackPanel { Width = 480, Spacing = 14 };
        root.Children.Add(new TextBlock
        {
            Text = "Choose whether to refresh the existing item or rebuild it from the files on disk.",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        _quickButton = BuildChoice(
            "\uE72C",
            "Quick Refresh",
            "Keep the current item and refresh metadata using the existing scan scope.",
            "quick");
        _completeButton = BuildChoice(
            "\uE777",
            "Complete Refresh",
            "Clear the current match, re-scan, and rebuild the item from disk context. This can recreate the item with a new ID or type.",
            "complete");
        root.Children.Add(_quickButton);
        root.Children.Add(_completeButton);

        _errorText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFC, 0xA5, 0xA5)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        root.Children.Add(_errorText);
        Content = root;
    }

    private Button BuildChoice(string glyph, string title, string description, string mode)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 20,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 2, 0, 0),
        };
        var copy = new StackPanel { Spacing = 4 };
        copy.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        copy.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(copy, 1);
        grid.Children.Add(icon);
        grid.Children.Add(copy);
        var button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
        };
        var spinner = new ProgressRing
        {
            Width = 20,
            Height = 20,
            IsActive = false,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var iconHost = new Grid();
        iconHost.Children.Add(icon);
        iconHost.Children.Add(spinner);
        grid.Children.Remove(icon);
        Grid.SetColumn(iconHost, 0);
        grid.Children.Add(iconHost);
        _choiceIndicators.Add((icon, spinner));
        button.Click += async (_, _) => await RunAsync(mode);
        return button;
    }

    private async Task RunAsync(string mode)
    {
        if (_pending) return;
        _pending = true;
        _quickButton.IsEnabled = false;
        _completeButton.IsEnabled = false;
        _errorText.Visibility = Visibility.Collapsed;
        SetPendingIndicators(true);
        try
        {
            await _onConfirm(mode);
            Hide();
        }
        catch (Exception ex)
        {
            _errorText.Text = ex.Message;
            _errorText.Visibility = Visibility.Visible;
            _quickButton.IsEnabled = true;
            _completeButton.IsEnabled = true;
        }
        finally
        {
            SetPendingIndicators(false);
            _pending = false;
        }
    }

    private void SetPendingIndicators(bool pending)
    {
        foreach (var (icon, spinner) in _choiceIndicators)
        {
            icon.Visibility = pending ? Visibility.Collapsed : Visibility.Visible;
            spinner.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
            spinner.IsActive = pending;
        }
    }
}
