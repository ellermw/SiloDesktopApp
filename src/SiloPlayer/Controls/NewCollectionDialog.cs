using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Helpers;

namespace SiloPlayer.Controls;

/// <summary>The current personal collection type picker. Choosing a type does not create anything.</summary>
public sealed class NewCollectionDialog : ContentDialog
{
    private readonly Grid _choices = new() { ColumnSpacing = 14, RowSpacing = 14 };
    private readonly Border _synced = new();
    private readonly List<FrameworkElement> _stages = [];
    private readonly List<(StackPanel Copy, Viewbox Chevron)> _cardBodies = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CollectionsApi _collections;
    private readonly SiloApiClient _client;
    private bool _checking;
    private readonly TextBlock _footer = new() { Text = "Next: name it and fill it in, on its own page.", FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly Button _cornerClose;
    public string? SelectedKind { get; private set; }
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public NewCollectionDialog(CollectionsApi collections, SiloApiClient client)
    {
        _collections = collections; _client = client;
        Title = new TextBlock { Text = "New collection", FontSize = 20, LineHeight = 28, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold, CharacterSpacing = -20 }; CloseButtonText = "Cancel";
        _cornerClose = EditorDialogPresentation.CornerClose(this); _cornerClose.Content = WebUiIcon.Create("x", 18, Brush("SecondaryTextBrush"));
        DefaultButton = ContentDialogButton.None;
        var body = new StackPanel { Spacing = 20 };
        body.Children.Add(new TextBlock { Text = "What decides what's in it? Only you can change it. Next you'll name it, fill it and choose who sees it.",
            FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        _choices.ColumnDefinitions.Add(new()); _choices.ColumnDefinitions.Add(new()); _choices.ColumnDefinitions.Add(new());
        _choices.RowDefinitions.Add(new() { Height = GridLength.Auto }); _choices.RowDefinitions.Add(new() { Height = GridLength.Auto }); _choices.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _choices.Children.Add(Choice("manual")); var smart = Choice("smart"); Grid.SetColumn(smart, 1); _choices.Children.Add(smart);
        Grid.SetColumn(_synced, 2); _choices.Children.Add(_synced); body.Children.Add(_choices);
        _footer.Foreground = Brush("SecondaryTextBrush");
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        EditorDialogPresentation.Configure(this, 1000, new Thickness(28, 4, 28, 24));
        Resources["ContentDialogTitleMargin"] = new Thickness(0, 0, 0, 6);
        CornerRadius = new(20); Resources["OverlayCornerRadius"] = new CornerRadius(20);
        Opened += async (_, _) => { Reflow(); await CheckSyncedAsync(); };
        Closed += (_, _) => _lifetime.Cancel();
        SizeChanged += (_, _) => Reflow();
        Checking();
    }
    private void Reflow()
    {
        var wide = XamlRoot?.Size.Width >= 1024;
        var rows = wide ? 1 : 3;
        if (_choices.RowDefinitions.Count != rows) { _choices.RowDefinitions.Clear(); for (var index = 0; index < rows; index++) _choices.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
        for (var index = 0; index < _choices.Children.Count; index++)
            { var child = (FrameworkElement)_choices.Children[index]; Grid.SetColumn(child, wide ? index : 0); Grid.SetRow(child, wide ? 0 : index); Grid.SetColumnSpan(child, wide ? 1 : 3); }
        foreach (var stage in _stages) stage.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (copy, chevron) in _cardBodies) { copy.Padding = new(20, 18, wide ? 20 : 44, 20); chevron.Opacity = wide ? 0 : 1; }
        var gutter = XamlRoot?.Size.Width < 640 ? 20 : 28;
        Resources["ContentDialogPadding"] = new Thickness(gutter, gutter == 20 ? 20 : 24, gutter, 24);
        var background = EditorDialogPresentation.Descendants<Border>(this).FirstOrDefault(element => element.Name == "BackgroundElement");
        if (background != null)
        {
            background.MaxHeight = Math.Max(160, (XamlRoot?.Size.Height ?? 900) - (wide ? 64 : 40));
            background.Width = wide ? Math.Min(1000, (XamlRoot?.Size.Width ?? 1280) - 48) : XamlRoot?.Size.Width ?? double.NaN;
            background.VerticalAlignment = wide ? VerticalAlignment.Center : VerticalAlignment.Bottom;
            background.CornerRadius = wide ? new(20) : new(20, 20, 0, 0);
            EditorDialogPresentation.PlaceCornerClose(this, null, _cornerClose);
            _cornerClose.Margin = new(18);
        }
        _cornerClose.Width = _cornerClose.Height = wide ? 34 : 44;
        var commands = EditorDialogPresentation.Descendants<Grid>(this).FirstOrDefault(element => element.Name == "CommandSpace");
        if (commands != null)
        {
            if (commands.ColumnDefinitions.Count >= 5) { commands.ColumnDefinitions[0].Width = new(1, GridUnitType.Star); commands.ColumnDefinitions[1].Width = new(0); commands.ColumnDefinitions[2].Width = new(0); commands.ColumnDefinitions[3].Width = GridLength.Auto; commands.ColumnDefinitions[4].Width = new(0); }
            commands.Padding = new(gutter, 16, gutter, 16); commands.BorderThickness = new(0, 1, 0, 0); commands.BorderBrush = Brush("BorderBrush"); commands.Background = TintBrush("SurfaceBrush", .55);
            if (_footer.Parent == null) { Grid.SetColumnSpan(_footer, 3); commands.Children.Add(_footer); }
            _footer.VerticalAlignment = VerticalAlignment.Center; _footer.Margin = new(0, 0, 16, 0);
            var cancel = EditorDialogPresentation.Descendants<Button>(commands).FirstOrDefault(button => button.Name == "CloseButton");
            if (cancel != null) { cancel.Style = (Style)Application.Current.Resources["OutlineButtonStyle"]; cancel.Height = cancel.MinHeight = 36; cancel.MinWidth = 0; Grid.SetColumn(cancel, 3); cancel.HorizontalAlignment = HorizontalAlignment.Right; }
        }
    }
    private void Checking()
    {
        var loading = new StackPanel { Spacing = 12, Padding = new Thickness(20) };
        var picture = new Border { Height = 128, CornerRadius = new(16), Background = Brush("SurfaceRaisedBrush") }; _stages.Add(picture); loading.Children.Add(picture);
        loading.Children.Add(new SkeletonText { Width = 128, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        loading.Children.Add(new SkeletonText { Height = 16 });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(loading, "Checking Synced lists…"); _synced.Child = StateCard(loading);
    }
    private async Task CheckSyncedAsync()
    {
        if (_checking || _lifetime.IsCancellationRequested) return;
        _checking = true; Checking(); var context = _client.CaptureContext();
        try
        {
            var capabilities = await _collections.GetCollectionCapabilitiesAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested || !_client.IsCurrentContext(context)) return;
            _synced.Child = capabilities.ImportSources?.Count > 0 ? Choice("synced") : Choice("synced", "Synced lists are off on this server."); Reflow();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (_lifetime.IsCancellationRequested || !_client.IsCurrentContext(context)) return;
            var error = new StackPanel { Spacing = 12, Padding = new Thickness(20) };
            error.Children.Add(new TextBlock { Text = "Couldn't check whether Synced lists are on.", FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Brush("SecondaryTextBrush") });
            var retry = new Button { Content = "Retry", HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.Resources["OutlineButtonStyle"] };
            retry.Height = 32; retry.MinHeight = 0;
            retry.Click += async (_, _) => await CheckSyncedAsync(); error.Children.Add(retry); _synced.Child = StateCard(error);
        }
        finally { _checking = false; }
    }
    private static Border StateCard(FrameworkElement content) => new() { Child = content, CornerRadius = new(18), BorderThickness = new(1), BorderBrush = TintBrush("BorderBrush", .9), Background = TintBrush("SurfaceBrush", .6) };
    private FrameworkElement Choice(string kind, string? unavailable = null)
    {
        var label = kind switch { "manual" => "Manual", "smart" => "Smart", _ => "Synced list" };
        var description = kind switch { "manual" => "You pick the titles and put them in order.", "smart" => "Titles that match your rules. It fills itself and keeps up as titles are added.", _ => "Follows a list from MDBList or TMDB and updates on a schedule." };
        var examples = kind switch { "manual" => "a director's best, movie night, a watch order.", "smart" => "90s comedies, unwatched 4K, Christmas movies.", _ => "IMDb Top 250, Netflix Originals, trending this week." };
        var stack = new StackPanel(); var stage = Stage(kind); _stages.Add(stage); stack.Children.Add(stage);
        var copy = new StackPanel { Padding = new Thickness(20, 18, 20, 20), Spacing = 6 };
        var tint = kind switch { "manual" => Windows.UI.Color.FromArgb(255, 95, 116, 238), "smart" => Windows.UI.Color.FromArgb(255, 22, 158, 136), _ => Windows.UI.Color.FromArgb(255, 191, 127, 34) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new Border { Width = 32, Height = 32, CornerRadius = new(9), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(46, tint.R, tint.G, tint.B)), Child = WebUiIcon.Create(kind switch { "manual" => "list-ordered", "smart" => "wand-sparkles", _ => "refresh-cw" }, 16, new SolidColorBrush(tint)) });
        heading.Children.Add(new TextBlock { Text = label, FontSize = 17, FontWeight = FontWeights.SemiBold, CharacterSpacing = -15, VerticalAlignment = VerticalAlignment.Center }); copy.Children.Add(heading);
        copy.Children.Add(new TextBlock { Text = description, FontSize = 13.5, LineHeight = 20.25, TextWrapping = TextWrapping.Wrap, Foreground = TintBrush("PrimaryTextBrush", .75) });
        var note = new TextBlock { FontSize = 12.5, LineHeight = 18.75, TextWrapping = TextWrapping.Wrap, Foreground = Brush("SecondaryTextBrush"), Margin = new Thickness(0, 6, 0, 0) };
        if (unavailable != null) note.Text = unavailable;
        else { note.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Good for ", FontWeight = FontWeights.Medium, Foreground = TintBrush("PrimaryTextBrush", .7) }); note.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = examples }); }
        copy.Children.Add(note);
        stack.Children.Add(copy);
        var content = new Grid(); content.Children.Add(stack);
        void ClipContent() { var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(content); var geometry = visual.Compositor.CreateRoundedRectangleGeometry(); geometry.Size = new((float)content.ActualWidth, (float)content.ActualHeight); geometry.CornerRadius = new(18); visual.Clip = visual.Compositor.CreateGeometricClip(geometry); }
        content.Loaded += (_, _) => ClipContent(); content.SizeChanged += (_, _) => ClipContent();
        var chevron = WebUiIcon.Create("chevron-right", 16, Brush("SecondaryTextBrush")); chevron.HorizontalAlignment = HorizontalAlignment.Right; chevron.VerticalAlignment = VerticalAlignment.Bottom; chevron.Margin = new(0, 0, 18, 20);
        if (unavailable == null) content.Children.Add(chevron);
        _cardBodies.Add((copy, chevron));
        if (unavailable != null)
        {
            var off = new Border { Child = content, CornerRadius = new(18), BorderThickness = new(1), BorderBrush = TintBrush("BorderBrush", .9), Background = TintBrush("SurfaceBrush", .6), Opacity = .6 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(off, label); return off;
        }
        var button = new Button { Content = content, Padding = new Thickness(0), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), BorderBrush = TintBrush("BorderBrush", .9),
            Background = TintBrush("SurfaceBrush", .6), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            IsEnabled = unavailable == null, Opacity = unavailable == null ? 1 : .6 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(button, description + " " + (unavailable ?? "Good for " + examples));
        button.Resources["ButtonBackgroundPointerOver"] = Brush("AccentBackgroundBrush");
        button.Resources["ButtonBorderBrushPointerOver"] = TintBrush("PrimaryTextBrush", .35);
        if (unavailable == null)
        {
            var over = false;
            void Reveal() { var reveal = over || button.FocusState != FocusState.Unfocused; chevron.Opacity = reveal || XamlRoot?.Size.Width < 1024 ? 1 : 0; button.RenderTransform = new TranslateTransform { Y = reveal ? -2 : 0 }; }
            button.PointerEntered += (_, _) => { over = true; Reveal(); }; button.PointerExited += (_, _) => { over = false; Reveal(); };
            button.GotFocus += (_, _) => Reveal(); button.LostFocus += (_, _) => Reveal();
        }
        button.Click += (_, _) => { SelectedKind = kind; Hide(); }; return button;
    }
    private static FrameworkElement Stage(string kind)
    {
        var tint = kind switch { "manual" => Windows.UI.Color.FromArgb(56, 95, 116, 238), "smart" => Windows.UI.Color.FromArgb(56, 22, 158, 136), _ => Windows.UI.Color.FromArgb(56, 191, 127, 34) };
        var copy = new StackPanel { Spacing = kind == "manual" ? 7 : kind == "smart" ? 12 : 10, Padding = kind == "manual" ? new(22, 20, 22, 0) : new Thickness(18, 18, 18, 0) };
        if (kind == "manual")
        {
            var index = 0;
            foreach (var title in new[] { "Spirited Away", "My Neighbor Totoro", "Kiki's Delivery Service" })
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new() { Width = new(14) }); row.ColumnDefinitions.Add(new() { Width = new(22) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.Children.Add(WebUiIcon.Create("grip-vertical", 13, Brush("SecondaryTextBrush")));
                var poster = DecorativePoster(index++, 22); Grid.SetColumn(poster, 1); row.Children.Add(poster);
                var name = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.Medium, Foreground = TintBrush("PrimaryTextBrush", .85), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(name, 2); row.Children.Add(name);
                copy.Children.Add(new Border { Height = 36, CornerRadius = new(10), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(index == 2 ? (byte)23 : (byte)13, 255, 255, 255)), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255)), Padding = new(10, 0, 10, 0), Child = row,
                    RenderTransformOrigin = new(.5, .5), RenderTransform = index == 2 ? new CompositeTransform { TranslateX = 10, Rotation = -1.5 } : null });
            }
        }
        else
        {
            if (kind == "smart")
            {
                var rules = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var label in new[] { "Genre is Comedy", "Year 1990–1999", "+1 rule" })
                    rules.Children.Add(new Border { Height = 24, Padding = new(9, 0, 9, 0), CornerRadius = new(7), Background = label == "+1 rule" ? new SolidColorBrush(Windows.UI.Color.FromArgb(13, 255, 255, 255)) : new SolidColorBrush(tint),
                        Child = new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center } });
                copy.Children.Add(rules);
            }
            else foreach (var (mark, color, title) in new[] { ("M", Windows.UI.Color.FromArgb(255, 31, 111, 235), "IMDb Top 250 Movies"), ("T", Windows.UI.Color.FromArgb(255, 13, 138, 106), "Trending this week") })
            {
                var row = new Grid { ColumnSpacing = 10 }; row.ColumnDefinitions.Add(new() { Width = new(24) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.Children.Add(new Border { Width = 24, Height = 24, CornerRadius = new(10), Background = new SolidColorBrush(color), Child = new TextBlock { Text = mark, FontSize = 10, FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                var name = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(name, 1); row.Children.Add(name);
                var daily = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { WebUiIcon.Create("refresh-cw", 12, Brush("SecondaryTextBrush")), new TextBlock { Text = "Daily", FontSize = 12.5, FontWeight = FontWeights.Medium, Foreground = Brush("SecondaryTextBrush") } } }; Grid.SetColumn(daily, 2); row.Children.Add(daily);
                copy.Children.Add(new Border { Height = 36, CornerRadius = new(11), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(13, 255, 255, 255)), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255)), BorderThickness = new(1), Padding = new(12, 0, 12, 0), Child = row });
            }
            var posters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            for (var index = 0; index < 5; index++)
            {
                var poster = DecorativePoster(kind == "smart" ? index : index + 2, kind == "smart" ? 50 : 36); poster.CornerRadius = new(10);
                if (kind == "smart" && index == 3) { poster.BorderThickness = new(2); var ring = tint; ring.A = 255; poster.BorderBrush = new SolidColorBrush(ring); }
                posters.Children.Add(poster);
            }
            copy.Children.Add(posters);
        }
        var stage = new Border { Height = 168, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = TintBrush("BorderBrush", .8), Child = copy,
            Background = new RadialGradientBrush { Center = new(0, 0), RadiusX = 1.2, RadiusY = 1.2, GradientStops = { new GradientStop { Offset = 0, Color = tint }, new GradientStop { Offset = .62, Color = Windows.UI.Color.FromArgb(0, tint.R, tint.G, tint.B) } } } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(stage, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        stage.Loaded += (_, _) => { var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(stage); visual.Clip = visual.Compositor.CreateInsetClip(); };
        return stage;
    }
    private static Border DecorativePoster(int index, double width)
    {
        var colors = new[] { (Windows.UI.Color.FromArgb(255, 143, 183, 216), Windows.UI.Color.FromArgb(255, 60, 91, 120)), (Windows.UI.Color.FromArgb(255, 216, 181, 74), Windows.UI.Color.FromArgb(255, 122, 90, 24)), (Windows.UI.Color.FromArgb(255, 79, 163, 224), Windows.UI.Color.FromArgb(255, 27, 74, 122)), (Windows.UI.Color.FromArgb(255, 184, 50, 58), Windows.UI.Color.FromArgb(255, 74, 16, 20)), (Windows.UI.Color.FromArgb(255, 42, 42, 48), Windows.UI.Color.FromArgb(255, 12, 12, 15)) };
        var pair = colors[index % colors.Length];
        return new Border { Width = width, Height = width * 1.5, CornerRadius = new(4), Background = new LinearGradientBrush { StartPoint = new(.318, 0), EndPoint = new(.682, 1), GradientStops = { new GradientStop { Color = pair.Item1, Offset = 0 }, new GradientStop { Color = pair.Item2, Offset = 1 } } } };
    }
    private static Brush TintBrush(string key, double opacity) { var color = ((SolidColorBrush)Brush(key)).Color; color.A = (byte)Math.Round(color.A * opacity); return new SolidColorBrush(color); }
}
