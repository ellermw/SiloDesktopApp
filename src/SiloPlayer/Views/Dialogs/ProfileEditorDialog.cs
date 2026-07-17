using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace SiloPlayer.Views.Dialogs;

/// <summary>
/// Native counterpart of the current WebUI ProfileEditorDialog. It intentionally
/// keeps profile identity, access restrictions, playback limits, and avatar
/// operations in one save surface so desktop changes produce the same request.
/// </summary>
public sealed class ProfileEditorDialog
{
    private static readonly (string Value, string Label)[] RatingOptions =
    [
        ("", "Any content"),
        ("G", "G / TV-G"),
        ("PG", "PG / TV-PG / TV-Y7"),
        ("PG-13", "PG-13 / TV-14"),
        ("R", "R / TV-MA / NC-17"),
    ];

    private static readonly (string Value, string Label)[] QualityOptions =
    [
        ("", "Any"),
        ("1080p", "Standard"),
        ("2160p", "4K"),
    ];

    private static readonly (string Id, string Label)[] AvatarStyles =
    [
        ("identicon", "Identicon"),
        ("initials", "Initials"),
        ("bottts-neutral", "Bottts Neutral"),
        ("fun-emoji", "Fun Emoji"),
        ("pixel-art-neutral", "Pixel Art Neutral"),
    ];

    private static readonly string[] SeedAdjectives =
    [
        "cosmic", "jelly", "starlight", "mango", "bubble", "neon", "marble", "comet",
        "snappy", "pepper", "glimmer", "ripple", "pocket", "candy", "ember", "mochi",
        "sprout", "twinkle", "whiz", "plasma", "mint", "velvet", "crunchy", "sunbeam",
        "toffee", "splash", "boomer", "lunar", "fizzy", "biscuit", "rocket", "cobalt",
    ];

    private static readonly string[] SeedNouns =
    [
        "otter", "rocket", "fox", "cookie", "gecko", "bunny", "penguin", "puffin",
        "tiger", "panda", "saturn", "wizard", "skater", "nebula", "robot", "pirate",
        "meteor", "sprite", "pebble", "nova", "dragon", "donut", "parrot", "panther",
        "goblin", "muffin", "laser", "pickle", "falcon", "sunset", "toaster", "bandit",
    ];

    private readonly Profile? _profile;
    private readonly IReadOnlyList<Library> _libraries;
    private readonly bool _avatarUploadEnabled;
    private readonly AuthApi _authApi;
    private readonly ContentDialog _dialog;
    private readonly TextBox _nameBox = new() { PlaceholderText = "Alex" };
    private readonly PasswordBox _pinBox = new() { PlaceholderText = "4 digits", MaxLength = 4 };
    private readonly ToggleSwitch _clearPinToggle = new() { OffContent = "Keep existing PIN", OnContent = "Remove existing PIN" };
    private readonly ToggleSwitch _kidsToggle = new();
    private readonly ComboBox _ratingBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _qualityBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch _restrictLibrariesToggle = new();
    private readonly StackPanel _libraryRows = new() { Spacing = 8 };
    private readonly ComboBox _avatarStyleBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Grid _avatarPresetGrid = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private readonly Image _avatarPreview = new() { Width = 96, Height = 96, Stretch = Stretch.UniformToFill };
    private readonly TextBlock _avatarFallback = new()
    {
        FontSize = 32,
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _avatarStatus = new() { FontSize = 12 };
    private readonly TextBlock _validationText = new() { FontSize = 12, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<int, CheckBox> _libraryChecks = [];

    private string _activeAvatarStyle = "identicon";
    private string _selectedAvatarPreset = "";
    private int _avatarBatch;
    private byte[]? _avatarFileBytes;
    private string? _avatarFileName;
    private string? _avatarContentType;
    private bool _removeUploadedAvatar;
    private bool _saving;

    private ProfileEditorDialog(
        XamlRoot xamlRoot,
        Profile? profile,
        IReadOnlyList<Library> libraries,
        bool avatarUploadEnabled,
        AuthApi authApi)
    {
        _profile = profile;
        _libraries = libraries.OrderBy(library => library.SortOrder).ToList();
        _avatarUploadEnabled = avatarUploadEnabled;
        _authApi = authApi;

        _dialog = new ContentDialog
        {
            Title = profile == null ? "New profile" : "Edit profile",
            PrimaryButtonText = "Save profile",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            MaxWidth = 780,
        };

        InitializeValues();
        _dialog.Content = BuildContent();
        _dialog.PrimaryButtonClick += SaveButton_Click;
    }

    public Profile? SavedProfile { get; private set; }

    public static async Task<Profile?> ShowAsync(XamlRoot xamlRoot, Profile? profile)
    {
        var authApi = App.Services.GetRequiredService<AuthApi>();
        var catalogApi = App.Services.GetRequiredService<CatalogApi>();
        var profileTask = authApi.GetProfilesAsync();
        var libraryTask = catalogApi.GetLibrariesAsync();
        await Task.WhenAll(profileTask, libraryTask);

        var profileResponse = await profileTask;
        var editor = new ProfileEditorDialog(
            xamlRoot,
            profile,
            await libraryTask,
            profileResponse.AvatarUploadEnabled,
            authApi);
        await editor._dialog.ShowAsync();
        return editor.SavedProfile;
    }

    private void InitializeValues()
    {
        _nameBox.Text = _profile?.Name ?? "";
        _pinBox.PlaceholderText = _profile?.HasPin == true ? "New PIN" : "4 digits";
        _clearPinToggle.Visibility = _profile?.HasPin == true ? Visibility.Visible : Visibility.Collapsed;
        _kidsToggle.IsOn = _profile?.IsChild ?? false;
        _restrictLibrariesToggle.IsOn = _profile?.LibraryRestrictionsEnabled ?? false;
        _libraryRows.Visibility = _restrictLibrariesToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

        foreach (var option in RatingOptions) _ratingBox.Items.Add(option.Label);
        _ratingBox.SelectedIndex = Math.Max(0, Array.FindIndex(RatingOptions,
            option => string.Equals(option.Value, _profile?.MaxContentRating ?? "", StringComparison.OrdinalIgnoreCase)));
        foreach (var option in QualityOptions) _qualityBox.Items.Add(option.Label);
        _qualityBox.SelectedIndex = CanonicalQualityIndex(_profile?.MaxPlaybackQuality);
        foreach (var style in AvatarStyles) _avatarStyleBox.Items.Add(style.Label);

        _selectedAvatarPreset = ParseAvatarPreset(_profile?.Avatar);
        var avatarParts = _selectedAvatarPreset.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (avatarParts.Length == 3 && avatarParts[0] == "dicebear")
            _activeAvatarStyle = avatarParts[1];
        _avatarStyleBox.SelectedIndex = Math.Max(0, Array.FindIndex(AvatarStyles,
            style => style.Id == _activeAvatarStyle));

        _clearPinToggle.Toggled += (_, _) =>
        {
            _pinBox.IsEnabled = !_clearPinToggle.IsOn;
            if (_clearPinToggle.IsOn) _pinBox.Password = "";
        };
        _restrictLibrariesToggle.Toggled += (_, _) =>
        {
            _libraryRows.Visibility = _restrictLibrariesToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        };
        _kidsToggle.Toggled += (_, _) => ApplyKidsPreset();
        _avatarStyleBox.SelectionChanged += (_, _) =>
        {
            if (_avatarStyleBox.SelectedIndex < 0) return;
            _activeAvatarStyle = AvatarStyles[_avatarStyleBox.SelectedIndex].Id;
            _avatarBatch = 0;
            BuildAvatarPresets();
        };
        _nameBox.TextChanged += (_, _) => UpdateAvatarPreview();
    }

    private FrameworkElement BuildContent()
    {
        var root = new StackPanel { Width = 720, Spacing = 18 };
        root.Children.Add(new TextBlock
        {
            Text = "Set the avatar, name, PIN, and access rules for this profile.",
            FontSize = 13,
            Foreground = Brush("SecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(BuildProfileSection());
        root.Children.Add(BuildAccessSection());
        _validationText.Foreground = Brush("SystemFillColorCriticalBrush", "SecondaryTextBrush");
        root.Children.Add(_validationText);

        BuildLibraryRows();
        BuildAvatarPresets();
        UpdateAvatarPreview();

        return new ScrollViewer
        {
            MaxHeight = 690,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root,
        };
    }

    private Border BuildProfileSection()
    {
        var section = Section("Profile", "Choose an avatar and basic details.");
        var body = (StackPanel)section.Child;

        var basic = new Grid { ColumnSpacing = 18 };
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var previewBorder = new Border
        {
            Width = 112,
            Height = 112,
            CornerRadius = new CornerRadius(56),
            Background = Brush("SurfaceBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Grid { Children = { _avatarFallback, _avatarPreview } },
        };
        var previewStack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        previewStack.Children.Add(previewBorder);
        _avatarStatus.Foreground = Brush("SecondaryTextBrush");
        _avatarStatus.TextAlignment = TextAlignment.Center;
        previewStack.Children.Add(_avatarStatus);
        Grid.SetColumn(previewStack, 0);
        basic.Children.Add(previewStack);

        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(Field("Name", _nameBox));
        fields.Children.Add(Field(_profile?.HasPin == true ? "New PIN" : "PIN (optional)", _pinBox));
        fields.Children.Add(_clearPinToggle);
        Grid.SetColumn(fields, 1);
        basic.Children.Add(fields);
        body.Children.Add(basic);

        body.Children.Add(Label("Preset avatars"));
        body.Children.Add(new TextBlock
        {
            Text = "Pick a DiceBear style, shuffle more options, or leave it blank to use initials.",
            FontSize = 12,
            Foreground = Brush("SecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        var presetHeader = new Grid { ColumnSpacing = 10 };
        presetHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        presetHeader.Children.Add(_avatarStyleBox);
        var moreButton = new Button { Content = "More options", Padding = new Thickness(12, 6, 12, 6) };
        moreButton.Click += (_, _) => { _avatarBatch++; BuildAvatarPresets(); };
        Grid.SetColumn(moreButton, 1);
        presetHeader.Children.Add(moreButton);
        body.Children.Add(presetHeader);
        body.Children.Add(_avatarPresetGrid);
        body.Children.Add(BuildUploadRow());
        return section;
    }

    private Border BuildAccessSection()
    {
        var section = Section("Access", "Content limits and library visibility for this profile.");
        var body = (StackPanel)section.Child;
        body.Children.Add(ToggleRow("Kids profile", "Seeds a safer default rating and library setup.", _kidsToggle));

        var limits = new Grid { ColumnSpacing = 14 };
        limits.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        limits.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        limits.Children.Add(Field("Maximum content rating", _ratingBox));
        var quality = Field("Maximum playback quality", _qualityBox);
        Grid.SetColumn(quality, 1);
        limits.Children.Add(quality);
        body.Children.Add(limits);
        body.Children.Add(ToggleRow("Restrict libraries", "Limit this profile to a specific set of libraries.", _restrictLibrariesToggle));
        body.Children.Add(_libraryRows);
        return section;
    }

    private FrameworkElement BuildUploadRow()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("Custom upload"));
        if (!_avatarUploadEnabled)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Custom uploads are unavailable. Configure private S3 avatar storage to enable them.",
                FontSize = 12,
                Foreground = Brush("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return panel;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var choose = new Button { Content = "Choose image", Padding = new Thickness(12, 6, 12, 6) };
        var remove = new Button { Content = "Remove", Padding = new Thickness(12, 6, 12, 6) };
        choose.Click += async (_, _) => await PickAvatarAsync();
        remove.Click += (_, _) =>
        {
            _avatarFileBytes = null;
            _avatarFileName = null;
            _avatarContentType = null;
            _selectedAvatarPreset = "";
            _removeUploadedAvatar = true;
            UpdateAvatarPreview();
            BuildAvatarPresets();
        };
        row.Children.Add(choose);
        row.Children.Add(remove);
        panel.Children.Add(row);
        return panel;
    }

    private void BuildLibraryRows()
    {
        _libraryRows.Children.Clear();
        _libraryChecks.Clear();
        if (_libraries.Count == 0)
        {
            _libraryRows.Children.Add(new TextBlock { Text = "No libraries available.", Foreground = Brush("SecondaryTextBrush") });
            return;
        }

        var allowed = (_profile?.AllowedLibraryIds ?? []).ToHashSet();
        foreach (var library in _libraries)
        {
            var check = new CheckBox
            {
                IsChecked = allowed.Contains(library.Id),
                Content = $"{library.Name}  ·  {library.Type}",
            };
            _libraryChecks[library.Id] = check;
            _libraryRows.Children.Add(check);
        }
    }

    private void BuildAvatarPresets()
    {
        _avatarPresetGrid.Children.Clear();
        _avatarPresetGrid.ColumnDefinitions.Clear();
        _avatarPresetGrid.RowDefinitions.Clear();
        for (var column = 0; column < 6; column++)
            _avatarPresetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 3; row++)
            _avatarPresetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < 18; index++)
        {
            var seed = BuildSeed(_activeAvatarStyle, _avatarBatch, index);
            var preset = $"dicebear:{_activeAvatarStyle}:{seed}";
            var image = new Image
            {
                Width = 62,
                Height = 62,
                Stretch = Stretch.UniformToFill,
                Source = new BitmapImage(new Uri(BuildDiceBearUrl(_activeAvatarStyle, seed))),
            };
            var button = new Button
            {
                Content = image,
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                BorderThickness = new Thickness(preset == _selectedAvatarPreset ? 2 : 1),
                BorderBrush = preset == _selectedAvatarPreset ? Brush("AccentBrush") : Brush("BorderBrush"),
            };
            button.Click += (_, _) =>
            {
                _selectedAvatarPreset = _selectedAvatarPreset == preset ? "" : preset;
                _avatarFileBytes = null;
                _avatarFileName = null;
                _avatarContentType = null;
                _removeUploadedAvatar = true;
                UpdateAvatarPreview();
                BuildAvatarPresets();
            };
            Grid.SetColumn(button, index % 6);
            Grid.SetRow(button, index / 6);
            _avatarPresetGrid.Children.Add(button);
        }
    }

    private async Task PickAvatarAsync()
    {
        var picker = new FileOpenPicker();
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp" })
            picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file == null) return;

        var buffer = await FileIO.ReadBufferAsync(file);
        _avatarFileBytes = buffer.ToArray();
        _avatarFileName = file.Name;
        _avatarContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        _selectedAvatarPreset = "";
        _removeUploadedAvatar = true;
        _avatarPreview.Source = new BitmapImage(new Uri(file.Path));
        _avatarPreview.Visibility = Visibility.Visible;
        _avatarFallback.Visibility = Visibility.Collapsed;
        _avatarStatus.Text = $"Custom upload · {file.Name}";
        BuildAvatarPresets();
    }

    private async void SaveButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_saving) return;
        var deferral = args.GetDeferral();
        try
        {
            if (!Validate()) return;
            _saving = true;
            sender.IsPrimaryButtonEnabled = false;
            sender.PrimaryButtonText = "Saving...";

            var preservingUpload = _profile?.AvatarSource == "upload" &&
                !_removeUploadedAvatar && _avatarFileBytes == null && string.IsNullOrEmpty(_selectedAvatarPreset);
            var request = new CreateProfileRequest
            {
                Name = _nameBox.Text.Trim(),
                Avatar = preservingUpload ? null : (_selectedAvatarPreset.Length == 0 ? "" : $"preset:{_selectedAvatarPreset}"),
                Pin = _clearPinToggle.IsOn ? "" : string.IsNullOrWhiteSpace(_pinBox.Password) ? null : _pinBox.Password,
                IsChild = _kidsToggle.IsOn,
                MaxContentRating = RatingOptions[Math.Max(0, _ratingBox.SelectedIndex)].Value,
                MaxPlaybackQuality = QualityOptions[Math.Max(0, _qualityBox.SelectedIndex)].Value,
                LibraryRestrictionsEnabled = _restrictLibrariesToggle.IsOn,
                AllowedLibraryIds = _restrictLibrariesToggle.IsOn
                    ? _libraryChecks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).Order().ToList()
                    : [],
            };

            var saved = _profile == null
                ? await _authApi.CreateProfileAsync(request)
                : await _authApi.UpdateProfileAsync(_profile.Id, request);

            if (_avatarFileBytes != null && _avatarFileName != null)
            {
                saved = await _authApi.UploadProfileAvatarAsync(
                    saved.Id,
                    _avatarFileName,
                    _avatarFileBytes,
                    _avatarContentType ?? "application/octet-stream");
            }
            else if (_profile?.AvatarSource == "upload" && _removeUploadedAvatar && _selectedAvatarPreset.Length == 0)
            {
                saved = await _authApi.DeleteProfileAvatarAsync(saved.Id);
            }

            SavedProfile = saved;
            sender.Hide();
        }
        catch (Exception ex)
        {
            ShowValidation(ex.Message);
            sender.IsPrimaryButtonEnabled = true;
            sender.PrimaryButtonText = "Save profile";
        }
        finally
        {
            _saving = false;
            deferral.Complete();
        }
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            ShowValidation("Enter a profile name.");
            return false;
        }
        if (_pinBox.Password.Length > 0 && (_pinBox.Password.Length != 4 || _pinBox.Password.Any(ch => !char.IsDigit(ch))))
        {
            ShowValidation("PIN must be exactly 4 digits.");
            return false;
        }
        if (_restrictLibrariesToggle.IsOn && !_libraryChecks.Values.Any(check => check.IsChecked == true))
        {
            ShowValidation("Choose at least one library.");
            return false;
        }
        _validationText.Visibility = Visibility.Collapsed;
        return true;
    }

    private void ApplyKidsPreset()
    {
        if (_kidsToggle.IsOn)
        {
            if (_ratingBox.SelectedIndex == 0) _ratingBox.SelectedIndex = 2;
            if (!_restrictLibrariesToggle.IsOn)
            {
                _restrictLibrariesToggle.IsOn = true;
                foreach (var check in _libraryChecks.Values) check.IsChecked = false;
            }
            return;
        }

        _ratingBox.SelectedIndex = 0;
        _qualityBox.SelectedIndex = 0;
        _restrictLibrariesToggle.IsOn = false;
        foreach (var check in _libraryChecks.Values) check.IsChecked = false;
    }

    private void UpdateAvatarPreview()
    {
        if (_avatarFileBytes != null) return;
        var selectedParts = _selectedAvatarPreset.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (selectedParts.Length == 3 && selectedParts[0] == "dicebear")
        {
            _avatarPreview.Source = new BitmapImage(new Uri(BuildDiceBearUrl(selectedParts[1], selectedParts[2])));
            _avatarPreview.Visibility = Visibility.Visible;
            _avatarFallback.Visibility = Visibility.Collapsed;
            _avatarStatus.Text = "Preset avatar";
            return;
        }
        if (!_removeUploadedAvatar && !string.IsNullOrWhiteSpace(_profile?.AvatarUrl))
        {
            _avatarPreview.Source = new BitmapImage(new Uri(_profile.AvatarUrl));
            _avatarPreview.Visibility = Visibility.Visible;
            _avatarFallback.Visibility = Visibility.Collapsed;
            _avatarStatus.Text = "Custom upload";
            return;
        }

        _avatarPreview.Visibility = Visibility.Collapsed;
        _avatarFallback.Visibility = Visibility.Visible;
        _avatarFallback.Text = string.IsNullOrWhiteSpace(_nameBox.Text) ? "?" : _nameBox.Text.Trim()[0].ToString().ToUpperInvariant();
        _avatarStatus.Text = "Initials fallback";
    }

    private void ShowValidation(string message)
    {
        _validationText.Text = message;
        _validationText.Visibility = Visibility.Visible;
    }

    private static Border Section(string title, string description)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Child = stack,
        };
    }

    private static StackPanel Field(string label, Control control)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Label(label));
        stack.Children.Add(control);
        return stack;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
    };

    private static Border ToggleRow(string title, string description, ToggleSwitch toggle)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(Label(title));
        text.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return new Border
        {
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 9, 12, 9),
            Child = grid,
        };
    }

    private static Brush Brush(string key, string? fallback = null)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
            return brush;
        if (fallback != null && Application.Current.Resources.TryGetValue(fallback, out value) && value is Brush fallbackBrush)
            return fallbackBrush;
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private static int CanonicalQualityIndex(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "2160p" or "4k" or "uhd" or "4320p" => 2,
        "1080p" or "720p" or "480p" or "standard" => 1,
        _ => 0,
    };

    private static string ParseAvatarPreset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return value.StartsWith("preset:", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;
    }

    private static string BuildSeed(string style, int batch, int index)
    {
        var adjective = SeedAdjectives[(batch * 7 + index * 3 + style.Length) % SeedAdjectives.Length];
        var noun = SeedNouns[(batch * 11 + index * 5 + style.Length * 2) % SeedNouns.Length];
        return $"{adjective}-{noun}";
    }

    private static string BuildDiceBearUrl(string style, string seed) =>
        $"https://api.dicebear.com/9.x/{Uri.EscapeDataString(style)}/svg?seed={Uri.EscapeDataString(seed)}&size=128&radius=24&backgroundType=gradientLinear";
}
