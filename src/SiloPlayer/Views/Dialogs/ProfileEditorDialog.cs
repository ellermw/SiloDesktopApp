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
    private readonly TextBlock _pinLabel = new() { FontSize = 13, FontWeight = FontWeights.SemiBold };
    private readonly Button _clearPinButton = new() { Content = "Remove PIN", Padding = new Thickness(8, 3, 8, 3) };
    private readonly ToggleSwitch _kidsToggle = new();
    private readonly ComboBox _ratingBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _qualityBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch _restrictLibrariesToggle = new();
    private readonly StackPanel _libraryRows = new() { Spacing = 8 };
    private readonly Grid _avatarStyleGrid = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private readonly TextBlock _avatarStyleStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
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
    private readonly TextBlock _avatarNamePreview = new() { FontSize = 13, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _validationText = new() { FontSize = 12, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<int, ToggleSwitch> _libraryChecks = [];

    private string _activeAvatarStyle = "identicon";
    private string _selectedAvatarPreset = "";
    private int _avatarBatch;
    private byte[]? _avatarFileBytes;
    private string? _avatarFileName;
    private string? _avatarContentType;
    private bool _removeUploadedAvatar;
    private bool _saving;
    private bool _clearPin;
    private bool _applyingKidsPreset;
    private bool _contentRatingTouched;
    private bool _libraryAccessTouched;

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
        _dialog.Closing += (_, args) => args.Cancel = _saving;
    }

    public Profile? SavedProfile { get; private set; }
    public string SubmittedPin { get; private set; } = "";

    public static async Task<Profile?> ShowAsync(XamlRoot xamlRoot, Profile? profile)
        => (await ShowWithContextAsync(xamlRoot, profile)).Profile;

    public static async Task<ProfileEditorResult> ShowWithContextAsync(XamlRoot xamlRoot, Profile? profile)
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
        return new ProfileEditorResult(editor.SavedProfile, editor.SubmittedPin);
    }

    private void InitializeValues()
    {
        _nameBox.Text = _profile?.Name ?? "";
        _pinBox.PlaceholderText = "4 digits";
        _pinLabel.Text = _profile?.HasPin == true ? "New PIN" : "PIN (optional)";
        _clearPinButton.Visibility = _profile?.HasPin == true ? Visibility.Visible : Visibility.Collapsed;
        _kidsToggle.IsOn = _profile?.IsChild ?? false;
        _restrictLibrariesToggle.IsOn = _profile?.LibraryRestrictionsEnabled ?? false;
        _libraryRows.Visibility = _restrictLibrariesToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

        foreach (var option in RatingOptions) _ratingBox.Items.Add(option.Label);
        _ratingBox.SelectedIndex = Math.Max(0, Array.FindIndex(RatingOptions,
            option => string.Equals(option.Value, _profile?.MaxContentRating ?? "", StringComparison.OrdinalIgnoreCase)));
        foreach (var option in QualityOptions) _qualityBox.Items.Add(option.Label);
        _qualityBox.SelectedIndex = CanonicalQualityIndex(_profile?.MaxPlaybackQuality);
        _selectedAvatarPreset = ParseAvatarPreset(_profile?.Avatar);
        var avatarParts = _selectedAvatarPreset.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (avatarParts.Length == 3 && avatarParts[0] == "dicebear")
            _activeAvatarStyle = avatarParts[1];
        _clearPinButton.Click += (_, _) =>
        {
            _clearPin = !_clearPin;
            _pinBox.IsEnabled = !_clearPin;
            if (_clearPin) _pinBox.Password = "";
            _pinLabel.Text = _clearPin ? "PIN will be removed" : "New PIN";
            _pinBox.PlaceholderText = _clearPin ? "PIN will be removed on save" : "4 digits";
            _clearPinButton.Content = _clearPin ? "Keep existing PIN" : "Remove PIN";
            ClearValidation();
        };
        _restrictLibrariesToggle.Toggled += (_, _) =>
        {
            _libraryRows.Visibility = _restrictLibrariesToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            if (!_applyingKidsPreset) _libraryAccessTouched = true;
            ClearValidation();
        };
        _kidsToggle.Toggled += (_, _) => ApplyKidsPreset();
        _ratingBox.SelectionChanged += (_, _) =>
        {
            if (!_applyingKidsPreset) _contentRatingTouched = true;
        };
        _nameBox.TextChanged += (_, _) => { UpdateAvatarPreview(); ClearValidation(); };
        _pinBox.PasswordChanged += (_, _) => ClearValidation();
    }

    private FrameworkElement BuildContent()
    {
        var root = new StackPanel { MaxWidth = 720, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Stretch };
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
        BuildAvatarStyles();
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
        _avatarNamePreview.TextAlignment = TextAlignment.Center;
        previewStack.Children.Add(_avatarNamePreview);
        _avatarStatus.Foreground = Brush("SecondaryTextBrush");
        _avatarStatus.TextAlignment = TextAlignment.Center;
        previewStack.Children.Add(_avatarStatus);
        Grid.SetColumn(previewStack, 0);
        basic.Children.Add(previewStack);

        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(Field("Name", _nameBox));
        var pinField = new StackPanel { Spacing = 6 };
        pinField.Children.Add(_pinLabel);
        pinField.Children.Add(_pinBox);
        pinField.Children.Add(_clearPinButton);
        fields.Children.Add(pinField);
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

        body.Children.Add(_avatarStyleGrid);
        var presetHeader = new Grid { ColumnSpacing = 10 };
        presetHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _avatarStyleStatus.Foreground = Brush("SecondaryTextBrush");
        _avatarStyleStatus.VerticalAlignment = VerticalAlignment.Center;
        presetHeader.Children.Add(_avatarStyleStatus);
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
            var unavailable = new StackPanel { Spacing = 3 };
            unavailable.Children.Add(new TextBlock { Text = "Custom uploads are unavailable", FontSize = 13, FontWeight = FontWeights.SemiBold });
            unavailable.Children.Add(new TextBlock
            {
                Text = "Configure private S3 avatar storage to enable uploaded profile avatars.",
                FontSize = 12,
                Foreground = Brush("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(new Border
            {
                BorderBrush = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(12, 9, 12, 9),
                Child = unavailable,
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
            var toggle = new ToggleSwitch
            {
                IsOn = allowed.Contains(library.Id),
            };
            toggle.Toggled += (_, _) =>
            {
                if (!_applyingKidsPreset) _libraryAccessTouched = true;
                ClearValidation();
            };
            _libraryChecks[library.Id] = toggle;

            var labels = new StackPanel { Spacing = 1 };
            labels.Children.Add(new TextBlock { Text = library.Name, FontSize = 13, FontWeight = FontWeights.SemiBold });
            labels.Children.Add(new TextBlock
            {
                Text = library.Type,
                FontSize = 11,
                Foreground = Brush("SecondaryTextBrush"),
            });
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(labels);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            _libraryRows.Children.Add(new Border
            {
                BorderBrush = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(12, 8, 12, 8),
                Child = row,
            });
        }
    }

    private void BuildAvatarStyles()
    {
        _avatarStyleGrid.Children.Clear();
        _avatarStyleGrid.ColumnDefinitions.Clear();
        _avatarStyleGrid.RowDefinitions.Clear();
        for (var column = 0; column < 4; column++)
            _avatarStyleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 2; row++)
            _avatarStyleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < AvatarStyles.Length; index++)
        {
            var style = AvatarStyles[index];
            var content = new StackPanel { Spacing = 3 };
            content.Children.Add(new TextBlock { Text = style.Label, FontSize = 13, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock
            {
                Text = AvatarStyleSummary(style.Id),
                FontSize = 11,
                Foreground = Brush("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
            });
            var selected = style.Id == _activeAvatarStyle;
            var button = new Button
            {
                Content = content,
                Tag = style.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Top,
                MinHeight = 70,
                Padding = new Thickness(12, 9, 12, 9),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = selected ? Brush("AccentBrush") : Brush("BorderBrush"),
            };
            button.Click += (_, _) =>
            {
                _activeAvatarStyle = style.Id;
                _avatarBatch = 0;
                BuildAvatarStyles();
                BuildAvatarPresets();
            };
            Grid.SetColumn(button, index % 4);
            Grid.SetRow(button, index / 4);
            _avatarStyleGrid.Children.Add(button);
        }

        _avatarStyleStatus.Text = $"{AvatarStyleSummary(_activeAvatarStyle)}. Showing 18 options right now.";
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
                Pin = _clearPin ? "" : string.IsNullOrWhiteSpace(_pinBox.Password) ? null : _pinBox.Password,
                IsChild = _kidsToggle.IsOn,
                MaxContentRating = RatingOptions[Math.Max(0, _ratingBox.SelectedIndex)].Value,
                MaxPlaybackQuality = QualityOptions[Math.Max(0, _qualityBox.SelectedIndex)].Value,
                LibraryRestrictionsEnabled = _restrictLibrariesToggle.IsOn,
                AllowedLibraryIds = _restrictLibrariesToggle.IsOn
                    ? _libraryChecks.Where(pair => pair.Value.IsOn).Select(pair => pair.Key).Order().ToList()
                    : [],
            };

            var saved = _profile == null
                ? await _authApi.CreateProfileAsync(request)
                : await _authApi.UpdateProfileAsync(_profile.Id, request);
            var toast = App.Services.GetRequiredService<SiloPlayer.Services.ToastService>();
            toast.Success(_profile == null ? "Profile created" : "Profile updated");

            if (_avatarFileBytes != null && _avatarFileName != null)
            {
                try
                {
                    saved = await _authApi.UploadProfileAvatarAsync(
                        saved.Id,
                        _avatarFileName,
                        _avatarFileBytes,
                        _avatarContentType ?? "application/octet-stream");
                    toast.Success("Avatar updated");
                }
                catch (Exception ex)
                {
                    toast.Error(string.IsNullOrWhiteSpace(ex.Message) ? "Failed to upload avatar" : ex.Message);
                }
            }
            else if (_profile?.AvatarSource == "upload" && _removeUploadedAvatar && _selectedAvatarPreset.Length == 0)
            {
                try
                {
                    saved = await _authApi.DeleteProfileAvatarAsync(saved.Id);
                    toast.Success("Avatar removed");
                }
                catch (Exception ex)
                {
                    toast.Error(string.IsNullOrWhiteSpace(ex.Message) ? "Failed to remove avatar" : ex.Message);
                }
            }

            SavedProfile = saved;
            SubmittedPin = request.Pin ?? "";
            _saving = false;
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
        if (!_clearPin && _pinBox.Password.Length > 0 && (_pinBox.Password.Length != 4 || _pinBox.Password.Any(ch => !char.IsDigit(ch))))
        {
            ShowValidation("PIN must be exactly 4 digits.");
            return false;
        }
        if (_restrictLibrariesToggle.IsOn && !_libraryChecks.Values.Any(check => check.IsOn))
        {
            ShowValidation("Choose at least one library.");
            return false;
        }
        _validationText.Visibility = Visibility.Collapsed;
        return true;
    }

    private void ApplyKidsPreset()
    {
        _applyingKidsPreset = true;
        try
        {
            if (_kidsToggle.IsOn)
            {
                if (!_contentRatingTouched && _ratingBox.SelectedIndex == 0) _ratingBox.SelectedIndex = 2;
                if (!_libraryAccessTouched && !_restrictLibrariesToggle.IsOn)
                {
                    _restrictLibrariesToggle.IsOn = true;
                    foreach (var check in _libraryChecks.Values) check.IsOn = false;
                }
                return;
            }

            _ratingBox.SelectedIndex = 0;
            _qualityBox.SelectedIndex = 0;
            _restrictLibrariesToggle.IsOn = false;
            foreach (var check in _libraryChecks.Values) check.IsOn = false;
            _contentRatingTouched = false;
            _libraryAccessTouched = false;
        }
        finally
        {
            _applyingKidsPreset = false;
        }
    }

    private void UpdateAvatarPreview()
    {
        _avatarNamePreview.Text = string.IsNullOrWhiteSpace(_nameBox.Text) ? "Preview" : _nameBox.Text.Trim();
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

    private void ClearValidation()
    {
        _validationText.Visibility = Visibility.Collapsed;
    }

    private static string AvatarStyleSummary(string style) => style switch
    {
        "initials" => "Clean letter-based avatars with bold backgrounds",
        "bottts-neutral" => "Cute modular robot-style icons",
        "fun-emoji" => "Big, colorful, instantly readable faces",
        "pixel-art-neutral" => "Retro pixel faces with lots of variation",
        _ => "Geometric, technical, high-contrast patterns",
    };

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

public sealed record ProfileEditorResult(Profile? Profile, string Pin);
