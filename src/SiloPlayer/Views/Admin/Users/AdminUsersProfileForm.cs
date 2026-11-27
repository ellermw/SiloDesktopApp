using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersFormKit;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The profile editor of an account (official <c>ProfileEditorDialog</c>): a Profile box (preview, name, PIN, preset avatars) over an Access box (kids profile, content
/// rating, advisory age, playback quality, library access, requests). Add profile and Edit profile are this one form.
/// </summary>
internal sealed class AdminUsersProfileForm : IDisposable
{
    private readonly AdminProfileSupport _support;
    private readonly JsonObject? _saved;
    private readonly bool _isPrimary;
    private readonly IReadOnlyList<JsonObject> _libraries;
    private readonly AdminUsersAvatarPicker? _avatars;
    private AdminProfileDraft _draft;
    private bool _ratingTouched, _librariesTouched, _requestsTouched, _syncing;
    private readonly TextBox _name = new() { MaxLength = 64 };
    private readonly PasswordBox _pin = new() { MaxLength = 4 };
    private readonly TextBlock _nameProblem = Problem(), _pinProblem = Problem(), _libraryProblem = Problem();
    private readonly TextBlock _previewName = Text("", 14), _previewLabel = Text("", 12, true), _initial = Text("?", 30);
    private readonly Image _previewImage = new() { Stretch = Stretch.UniformToFill };
    private readonly Button _pinToggle;
    private readonly TextBlock _pinLabel = Text("", 14);
    private readonly ToggleSwitch _kids = Switch(), _requests = Switch(), _restrict = Switch(), _requireAge = Switch();
    private readonly ComboBox _rating = new() { HorizontalAlignment = HorizontalAlignment.Stretch }, _age = new() { HorizontalAlignment = HorizontalAlignment.Stretch }, _quality = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Dictionary<string, ToggleSwitch> _libraryToggles = new(StringComparer.Ordinal);
    private readonly StackPanel _libraryList = new() { Spacing = 8 };
    private Border _requireAgeRow = null!;
    private TextBlock _ageHelp = null!;

    internal FrameworkElement Content { get; }
    internal event Action? Changed;
    internal AdminProfileDraft Draft => _draft;

    private static string S(string key, string fallback, params (string Name, object? Value)[] args) =>
        AppStrings.Get(key, args.Length == 0 ? null : args.ToDictionary(pair => pair.Name, pair => pair.Value), fallback);

    internal AdminUsersProfileForm(JsonObject? saved, bool isPrimary, IReadOnlyList<JsonObject> libraries, AdminProfileSupport support,
        Func<string, CancellationToken, Task<byte[]>>? loadAvatar, CancellationToken owner)
    {
        _saved = saved; _isPrimary = isPrimary; _libraries = libraries.OrderBy(row => row.Count("sort_order")).ToArray(); _support = support;
        _draft = AdminProfileDraft.From(saved);
        if (support.Avatars && loadAvatar != null) { _avatars = new AdminUsersAvatarPicker(_draft.Avatar.Length > 0 ? _draft.Avatar : null, loadAvatar, owner); _avatars.Changed += AvatarChanged; }
        _pinToggle = ActionButton("", style: "GhostButtonStyle", height: 28);
        var root = new StackPanel { Spacing = 20 };
        root.Children.Add(ProfileSection());
        root.Children.Add(AccessSection());
        Content = root;
        AutomationProperties.SetAutomationId(root, "ProfileForm");
        Refresh();
    }

    // ── Profile ─────────────────────────────────────────────────────────────────────────────────

    private FrameworkElement ProfileSection()
    {
        var stack = new StackPanel { Spacing = 16 };
        var top = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        // Preview card: avatar, name and where the avatar comes from.
        var disc = new Border { Width = 96, Height = 96, CornerRadius = new(48), Background = Brush("SurfaceBrush"), BorderThickness = new(2), BorderBrush = Tint("BorderBrush", 1), HorizontalAlignment = HorizontalAlignment.Center };
        var inner = new Grid { Width = 92, Height = 92 };
        _initial.HorizontalAlignment = HorizontalAlignment.Center; _initial.VerticalAlignment = VerticalAlignment.Center; _initial.FontWeight = Microsoft.UI.Text.FontWeights.Bold; _initial.Foreground = Brush("AccentBrush");
        inner.Children.Add(_initial); inner.Children.Add(_previewImage);
        inner.Clip = new RectangleGeometry { Rect = new(0, 0, 92, 92) };
        disc.Child = inner;
        var card = new StackPanel { Spacing = 12, Width = 168 };
        card.Children.Add(disc);
        var caption = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
        _previewName.TextAlignment = TextAlignment.Center; _previewName.FontWeight = Microsoft.UI.Text.FontWeights.Medium; _previewLabel.TextAlignment = TextAlignment.Center;
        caption.Children.Add(_previewName); caption.Children.Add(_previewLabel);
        card.Children.Add(caption);
        var cardBox = new Border { Child = card, Padding = new(20, 16, 20, 16), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), VerticalAlignment = VerticalAlignment.Top };
        top.Children.Add(cardBox);

        _name.Text = _draft.Name; AutomationProperties.SetAutomationId(_name, "ProfileName"); AutomationProperties.SetName(_name, S("web.profiles.editor.name", "Name"));
        _name.TextChanged += (_, _) => { _draft = _draft with { Name = _name.Text }; Say(_nameProblem, null); Refresh(); Changed?.Invoke(); };
        _pin.PlaceholderText = S("web.profiles.editor.pinPlaceholder", "4 digits"); AutomationProperties.SetAutomationId(_pin, "ProfilePin"); AutomationProperties.SetName(_pin, S("web.profiles.editor.pinOptional", "PIN (optional)"));
        _pin.PasswordChanged += (_, _) =>
        {
            if (_syncing) return;
            var digits = new string(_pin.Password.Where(char.IsDigit).Take(4).ToArray());
            if (digits != _pin.Password) { _syncing = true; _pin.Password = digits; _syncing = false; }
            _draft = _draft with { Pin = digits }; Say(_pinProblem, null); Changed?.Invoke();
        };
        var fields = new StackPanel { Spacing = 16 };
        var nameField = new StackPanel { Spacing = 6 };
        nameField.Children.Add(Text(S("web.profiles.editor.name", "Name"), 14)); nameField.Children.Add(_name); nameField.Children.Add(_nameProblem);
        var pinField = new StackPanel { Spacing = 6 };
        pinField.Children.Add(_pinLabel); pinField.Children.Add(_pin);
        if (_saved?.Flag("has_pin") == true)
        {
            _pinToggle.Click += (_, _) =>
            {
                var next = !_draft.ClearPin;
                _draft = _draft with { ClearPin = next, Pin = next ? "" : _draft.Pin };
                if (next) { _syncing = true; _pin.Password = ""; _syncing = false; }
                Say(_pinProblem, null); Refresh(); Changed?.Invoke();
            };
            pinField.Children.Add(_pinToggle);
        }
        pinField.Children.Add(_pinProblem);
        fields.Children.Add(nameField); fields.Children.Add(pinField);
        Grid.SetColumn(fields, 1); top.Children.Add(fields);
        // Narrow: the preview card sits above the fields.
        var stacked = false;
        top.SizeChanged += (_, _) =>
        {
            var narrow = top.ActualWidth > 0 && top.ActualWidth < 480;
            if (narrow == stacked) return;
            stacked = narrow;
            top.RowDefinitions.Clear();
            if (narrow)
            {
                top.RowDefinitions.Add(new() { Height = GridLength.Auto }); top.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetColumnSpan(cardBox, 2); Grid.SetRow(fields, 1); Grid.SetColumn(fields, 0); Grid.SetColumnSpan(fields, 2); cardBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            else { Grid.SetColumnSpan(cardBox, 1); Grid.SetRow(fields, 0); Grid.SetColumn(fields, 1); Grid.SetColumnSpan(fields, 1); cardBox.HorizontalAlignment = HorizontalAlignment.Left; }
        };
        stack.Children.Add(top);
        if (_avatars != null) stack.Children.Add(_avatars.Content);
        return Section(S("web.profiles.editor.profileSection", "Profile"), S("web.profiles.editor.profileSectionHint", ""), stack);
    }

    private void AvatarChanged()
    {
        _draft = _draft with { Avatar = _avatars?.Reference ?? "" };
        Refresh(); Changed?.Invoke();
    }

    private void Refresh()
    {
        var name = _draft.Name.Trim();
        _previewName.Text = name.Length > 0 ? name : S("web.profiles.editor.preview", "Preview");
        _initial.Text = (name.Length > 0 ? name : _saved?.Text("name") is { Length: > 0 } saved ? saved : "?")[..1].ToUpperInvariant();
        var preview = _avatars?.PreviewSource;
        _previewImage.Source = preview; _previewImage.Visibility = preview == null ? Visibility.Collapsed : Visibility.Visible;
        _previewLabel.Text = _avatars?.SelectedLabel ?? S("web.profiles.editor.initialsFallback", "Initials fallback");
        _pinLabel.Text = _draft.ClearPin ? S("web.profiles.editor.pinRemoved", "PIN will be removed") : _saved?.Flag("has_pin") == true ? S("web.profiles.editor.newPin", "New PIN") : S("web.profiles.editor.pinOptional", "PIN (optional)");
        _pin.IsEnabled = !_draft.ClearPin;
        _pin.PlaceholderText = _draft.ClearPin ? S("web.profiles.editor.pinRemovedOnSave", "PIN will be removed on save") : S("web.profiles.editor.pinPlaceholder", "4 digits");
        ((Button)_pinToggle).Content = new TextBlock { Text = _draft.ClearPin ? S("web.profiles.editor.keepPin", "Keep existing PIN") : S("web.profiles.editor.removePin", "Remove PIN"), FontSize = 12, TextWrapping = TextWrapping.WrapWholeWords };
        AutomationProperties.SetName(_pinToggle, _draft.ClearPin ? S("web.profiles.editor.keepPin", "Keep existing PIN") : S("web.profiles.editor.removePin", "Remove PIN"));
    }

    // ── Access ──────────────────────────────────────────────────────────────────────────────────

    private FrameworkElement AccessSection()
    {
        var stack = new StackPanel { Spacing = 16 };
        _kids.IsOn = _draft.IsChild; AutomationProperties.SetAutomationId(_kids, "ProfileKids");
        _kids.Toggled += (_, _) =>
        {
            if (_syncing) return;
            _draft = _draft.WithKids(_kids.IsOn, _ratingTouched, _librariesTouched, _requestsTouched, _isPrimary);
            SyncAccess(); Changed?.Invoke();
        };
        stack.Children.Add(SwitchRow(S("web.profiles.editor.kids", "Kids profile"), S("web.profiles.editor.kidsHint", ""), _kids));

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        foreach (var (value, label) in AdminProfileDraft.ContentRatings)
            _rating.Items.Add(new ComboBoxItem { Content = value == "" ? S("web.profiles.helpers.contentRating.any", "Any content") : label, Tag = value });
        AutomationProperties.SetAutomationId(_rating, "ProfileRating"); AutomationProperties.SetName(_rating, S("web.profiles.editor.maxContentRating", "Maximum content rating"));
        _rating.SelectionChanged += (_, _) =>
        {
            if (_syncing || _rating.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
            _ratingTouched = true; _draft = _draft with { MaxContentRating = tag }; Changed?.Invoke();
        };
        grid.Children.Add(Field(S("web.profiles.editor.maxContentRating", "Maximum content rating"), _rating));
        if (_support.AdvisoryAge)
        {
            _age.Items.Add(new ComboBoxItem { Content = S("web.profiles.helpers.advisoryAge.noLimit", "No limit"), Tag = "" });
            for (var age = AdminProfileDraft.MinAdvisoryAge; age <= AdminProfileDraft.MaxAdvisoryAge21; age++)
                _age.Items.Add(new ComboBoxItem { Content = S("web.profiles.helpers.advisoryAge.agesAndUnder", $"Ages {age} and under", ("age", age), ("age_text", age)), Tag = age.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            AutomationProperties.SetAutomationId(_age, "ProfileAdvisoryAge"); AutomationProperties.SetName(_age, S("web.profiles.editor.maxAdvisoryAge", "Maximum advisory age"));
            _ageHelp = Text("", 12, true);
            _age.SelectionChanged += (_, _) =>
            {
                if (_syncing || _age.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
                _draft = _draft with { MaxAdvisoryAge = tag == "" ? null : long.Parse(tag, System.Globalization.CultureInfo.InvariantCulture) }; SyncAccess(); Changed?.Invoke();
            };
            var ageField = Field(S("web.profiles.editor.maxAdvisoryAge", "Maximum advisory age"), _age, _ageHelp);
            if (_support.RequireAdvisoryAge)
            {
                _requireAge.Toggled += (_, _) => { if (_syncing) return; _draft = _draft with { RequireAdvisoryAge = _requireAge.IsOn }; SyncAccess(); Changed?.Invoke(); };
                _requireAgeRow = SwitchRow(S("web.profiles.editor.hideWithoutAge", "Hide titles without an advisory age"), S("web.profiles.editor.hideWithoutAgeHint", ""), _requireAge);
                ageField.Children.Add(_requireAgeRow);
            }
            grid.Children.Add(ageField);
        }
        foreach (var (tag, label) in new[] { ("any", S("web.profiles.helpers.quality.any", "Any")), ("standard", S("web.profiles.helpers.quality.standard", "Standard")), ("4k", "4K") })
            _quality.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        AutomationProperties.SetAutomationId(_quality, "ProfileQuality"); AutomationProperties.SetName(_quality, S("web.profiles.editor.maxPlaybackQuality", "Maximum playback quality"));
        _quality.SelectionChanged += (_, _) =>
        {
            if (_syncing || _quality.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
            _draft = _draft with { Quality = tag }; Changed?.Invoke();
        };
        grid.Children.Add(Field(S("web.profiles.editor.maxPlaybackQuality", "Maximum playback quality"), _quality));
        AdaptiveColumns(grid);
        stack.Children.Add(grid);
        stack.Children.Add(LibraryAccess());
        if (_support.RequestsAllowed)
        {
            _requests.Toggled += (_, _) => { if (_syncing) return; _requestsTouched = true; _draft = _draft with { RequestsAllowed = _requests.IsOn }; Changed?.Invoke(); };
            AutomationProperties.SetAutomationId(_requests, "ProfileRequests");
            stack.Children.Add(SwitchRow(S("web.profiles.editor.requestsAllowed", "Allow requests"), S("web.profiles.editor.requestsAllowedHint", ""), _requests));
        }
        SyncAccess();
        return Section(S("web.profiles.editor.accessSection", "Access"), S("web.profiles.editor.accessSectionHint", ""), stack);
    }

    private FrameworkElement LibraryAccess()
    {
        var box = new StackPanel { Spacing = 12 };
        if (_isPrimary)
        {
            box.Children.Add(Text(S("web.settings.library.title", "Libraries"), 14));
            box.Children.Add(Text(S("web.profiles.libraryAccess.primaryNote", ""), 12, true));
        }
        else
        {
            AutomationProperties.SetAutomationId(_restrict, "ProfileRestrictLibraries");
            _restrict.Toggled += (_, _) =>
            {
                if (_syncing) return;
                _librariesTouched = true; _draft = _draft with { LibraryRestrictions = _restrict.IsOn }; Say(_libraryProblem, null); SyncAccess(); Changed?.Invoke();
            };
            var texts = new StackPanel { Spacing = 2 };
            texts.Children.Add(Text(S("web.profiles.editor.restrictLibraries", "Restrict libraries"), 14)); texts.Children.Add(Text(S("web.profiles.editor.restrictLibrariesHint", ""), 12, true));
            AutomationProperties.SetName(_restrict, S("web.profiles.editor.restrictLibraries", "Restrict libraries"));
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(texts); Grid.SetColumn(_restrict, 1); row.Children.Add(_restrict);
            box.Children.Add(row);
            if (_libraries.Count == 0) _libraryList.Children.Add(Text(S("web.profiles.noLibraries", "No libraries yet."), 14, true));
            foreach (var library in _libraries)
            {
                var id = library["id"]?.ToString() ?? "";
                var name = new StackPanel { Spacing = 2 };
                name.Children.Add(Text(library.Text("name"), 14));
                var type = library.Text("type");
                if (type.Length > 0) name.Children.Add(Text(string.Join(" ", type.Split('_', '-').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..])), 12, true));
                var toggle = Switch(false);
                AutomationProperties.SetName(toggle, library.Text("name")); AutomationProperties.SetAutomationId(toggle, "ProfileLibrary_" + id);
                toggle.Toggled += (_, _) =>
                {
                    if (_syncing) return;
                    _librariesTouched = true;
                    var ids = _draft.AllowedLibraries.Where(existing => existing != id).ToList();
                    if (toggle.IsOn) ids.Add(id);
                    _draft = _draft with { AllowedLibraries = ids.OrderBy(value => long.TryParse(value, out var number) ? number : long.MaxValue).ToArray() };
                    Say(_libraryProblem, null); Changed?.Invoke();
                };
                _libraryToggles[id] = toggle;
                _libraryList.Children.Add(Card(name, toggle));
            }
            box.Children.Add(_libraryList); box.Children.Add(_libraryProblem);
        }
        return new Border { Child = box, Padding = new(12), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush("BorderBrush") };
    }

    /// <summary>Puts the controls on the draft (a kids switch moves the rating, libraries and requests together).</summary>
    private void SyncAccess()
    {
        _syncing = true;
        _kids.IsOn = _draft.IsChild;
        _rating.SelectedItem = _rating.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == _draft.MaxContentRating)
            ?? AddRating(_draft.MaxContentRating);
        if (_support.AdvisoryAge)
        {
            _age.SelectedItem = _age.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (_draft.MaxAdvisoryAge?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));
            _requireAge.IsOn = _draft.RequireAdvisoryAge;
            var showRequire = _support.RequireAdvisoryAge && _draft.MaxAdvisoryAge != null;
            if (_requireAgeRow != null) _requireAgeRow.Visibility = showRequire ? Visibility.Visible : Visibility.Collapsed;
            _ageHelp.Text = showRequire && _draft.RequireAdvisoryAge ? S("web.profiles.editor.advisoryAgeHelp", "") : S("web.profiles.editor.advisoryAgeHelpFallback", "");
        }
        _quality.SelectedItem = _quality.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == _draft.Quality);
        _requests.IsOn = _draft.RequestsAllowed;
        if (!_isPrimary)
        {
            _restrict.IsOn = _draft.LibraryRestrictions;
            _libraryList.Visibility = _draft.LibraryRestrictions ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (id, toggle) in _libraryToggles) toggle.IsOn = _draft.AllowedLibraries.Contains(id);
        }
        _syncing = false;
    }

    private ComboBoxItem AddRating(string value)
    {
        var item = new ComboBoxItem { Content = value, Tag = value };
        _rating.Items.Add(item);
        return item;
    }

    // ── Reading the form ────────────────────────────────────────────────────────────────────────

    /// <summary>Shows what is wrong at its field and focuses the first; true when the form can be saved.</summary>
    internal bool Validate()
    {
        var (name, pin, libraries) = _draft.Problems(_isPrimary);
        Say(_nameProblem, name ? S("web.profiles.editor.nameRequired", "Enter a profile name.") : null);
        Say(_pinProblem, pin ? S("web.profiles.editor.pinInvalid", "PIN must be exactly 4 digits.") : null);
        Say(_libraryProblem, libraries ? S("web.profiles.editor.librariesRequired", "Choose at least one library.") : null);
        if (name) _name.Focus(FocusState.Programmatic); else if (pin) _pin.Focus(FocusState.Programmatic);
        return !name && !pin && !libraries;
    }

    internal JsonObject Body() => _saved == null ? _draft.CreateBody(_support, _isPrimary) : _draft.UpdateBody(_saved, _support, _isPrimary);

    public void Dispose() => _avatars?.Dispose();
}
