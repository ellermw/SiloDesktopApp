using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views.Dialogs;

public sealed class EditPersonDialog : ContentDialog
{
    private readonly Person _person;
    private readonly Dictionary<string, TextBox> _inputs = [];
    private readonly Dictionary<string, string> _original = [];
    private readonly Dictionary<string, StackPanel> _groups = [];
    private readonly TextBlock _heading = new() { Text = "Edit Person Metadata", FontSize = 18, Height = 18, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly Grid _form = new() { RowSpacing = 16, ColumnSpacing = 16 };
    private bool _pending;
    public bool HasSaved { get; private set; }
    public EditPersonDialog(Person person)
    {
        _person = person;
        var header = new Grid(); header.Children.Add(_heading);
        var close = EditorDialogPresentation.CornerClose(this); close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, -8, -8, 0); header.Children.Add(close); Title = header;
        PrimaryButtonText = "Save"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
        EditorDialogPresentation.Configure(this, 672, new Thickness(24));
        _form.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _form.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Add("name", "Name", person.Name, true);
        Add("bio", "Bio", person.Bio, true);
        Add("birth_date", "Birth Date", person.BirthDate); Add("death_date", "Death Date", person.DeathDate);
        Add("birthplace", "Birthplace", person.Birthplace); Add("homepage", "Homepage", person.Homepage);
        Add("tmdb_id", "TMDB ID", person.TmdbId); Add("imdb_id", "IMDb ID", person.ImdbId); Add("tvdb_id", "TVDB ID", person.TvdbId);
        Content = new ScrollViewer { Content = _form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Opened += (_, _) => Reflow(); SizeChanged += (_, _) => Reflow();
        PrimaryButtonClick += Save;
    }
    private void Add(string key, string label, string? value, bool full = false)
    {
        var group = new StackPanel { Spacing = 6, Tag = full }; _groups[key] = group;
        group.Children.Add(new TextBlock { Text = label, FontSize = 14, Height = 14, LineHeight = 14,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = Microsoft.UI.Text.FontWeights.Medium });
        var input = new TextBox { Text = value ?? "", FontSize = 14, Tag = full, Height = 36, MinHeight = 36,
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"], CornerRadius = new CornerRadius(10) };
        if (key == "bio") { input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.MinHeight = input.Height = 128; }
        if (key.EndsWith("_date")) input.PlaceholderText = "YYYY-MM-DD";
        _original[key] = value ?? ""; _inputs[key] = input; group.Children.Add(input); _form.Children.Add(group);
    }
    private void Reflow()
    {
        var width = XamlRoot?.Size.Width ?? 800;
        var narrow = width < 640;
        _form.Width = Math.Max(220, Math.Min(622, width - (narrow ? 92 : 82)));
        var bodyWidth = _form.Width + (narrow ? 10 : 0);
        var scroller = (ScrollViewer)Content;
        scroller.Width = bodyWidth;
        scroller.HorizontalContentAlignment = HorizontalAlignment.Left;
        if (Title is Grid header) header.Width = bodyWidth;
        _heading.TextAlignment = narrow ? TextAlignment.Center : TextAlignment.Left;
        EditorDialogPresentation.ReflowCommands(this, narrow, new Thickness(24, 0, narrow ? 34 : 24, 24), buttonHeight: 36, stackNarrow: true);
        ((ScrollViewer)Content).MaxHeight = Math.Max(160, (XamlRoot?.Size.Height ?? 800) - 174);
        _form.RowDefinitions.Clear(); int row = 0, col = 0;
        foreach (var input in _groups.Values)
        {
            var full = (bool)input.Tag || width < 640;
            if (full && col != 0) { row++; col = 0; }
            while (_form.RowDefinitions.Count <= row) _form.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(input, row); Grid.SetColumn(input, col); Grid.SetColumnSpan(input, full ? 2 : 1);
            if (full || col == 1) { row++; col = 0; } else col++;
        }
    }
    private async void Save(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; if (_pending) return;
        if (!AuthorizationPolicy.IsActingAdmin(App.Services.GetRequiredService<AuthService>())) return;
        var changes = new Dictionary<string, object?>();
        foreach (var (key, input) in _inputs)
        {
            if (input.Text == _original[key]) continue;
            if (key.EndsWith("_date") && input.Text.Length > 0 && !DateOnly.TryParseExact(input.Text, "yyyy-MM-dd", out _))
            { App.Services.GetRequiredService<ToastService>().Error("Use YYYY-MM-DD for dates."); return; }
            changes[key] = key.EndsWith("_date") && input.Text.Length == 0 ? null : input.Text;
        }
        if (changes.Count == 0) { Hide(); return; }
        _pending = true; IsPrimaryButtonEnabled = false; PrimaryButtonText = "Saving...";
        try { await App.Services.GetRequiredService<PeopleApi>().UpdatePersonAsync(_person.Id, changes); HasSaved = true; Hide(); }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        finally { _pending = false; IsPrimaryButtonEnabled = true; PrimaryButtonText = "Save"; }
    }
}
