using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Converters;
using SiloPlayer.Helpers;
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
    private static readonly UrlToImageSourceConverter RemoteImageConverter = new();
    private static readonly (string Value, string Label)[] RatingOptions =
    [
        ("", "Any content"),
        ("G", "G / TV-G"),
        ("6", "6 / FSK 6"),
        ("PG", "PG / TV-PG / TV-Y7"),
        ("12", "12 / 12A / FSK 12"),
        ("PG-13", "PG-13 / TV-14"),
        ("15", "15 / MA 15+"),
        ("16", "16 / FSK 16"),
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
    private readonly TextBox _nameBox = new() { PlaceholderText = "Alex", Height = 36 };
    private readonly PasswordBox _pinBox = new() { PlaceholderText = "4 digits", MaxLength = 4, Height = 36 };
    private readonly TextBlock _pinLabel = new() { FontSize = 14, Height = 14, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.Medium };
    private readonly Button _clearPinButton = new() { Content = "Remove PIN", Height = 28, MinHeight = 0, FontSize = 12, Padding = new Thickness(8, 3, 8, 3) };
    private readonly ToggleSwitch _kidsToggle = new();
    private readonly ComboBox _ratingBox = new() { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _advisoryBox = new() { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch _requireAdvisory = new();
    private readonly bool _ageSupported;
    private readonly bool _requireAgeSupported;
    private readonly List<string> _ratingValues = RatingOptions.Select(option => option.Value).ToList();
    private readonly ComboBox _qualityBox = new() { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch _restrictLibrariesToggle = new();
    private readonly StackPanel _libraryRows = new() { Spacing = 8 };
    private readonly Grid _avatarStyleGrid = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private readonly TextBlock _avatarStyleStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Grid _avatarPresetGrid = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private readonly Image _avatarPreview = new() { Width = 96, Height = 96, Stretch = Stretch.UniformToFill };
    private readonly TextBlock _avatarFallback = new()
    {
        FontSize = 30,
        LineHeight = 36,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _avatarStatus = new() { FontSize = 12, LineHeight = 16 };
    private readonly TextBlock _avatarNamePreview = new() { FontSize = 14, LineHeight = 20, FontWeight = FontWeights.Medium };
    private readonly TextBlock _validationText = new() { FontSize = 12, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<int, ToggleSwitch> _libraryChecks = [];
    private readonly List<Action> _viewportReflows = [];

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
        AuthApi authApi, bool ageSupported, bool requireAgeSupported)
    {
        _profile = profile;
        _libraries = libraries.OrderBy(library => library.SortOrder).ToList();
        _avatarUploadEnabled = avatarUploadEnabled;
        _authApi = authApi;
        _ageSupported = ageSupported; _requireAgeSupported = requireAgeSupported;

        _dialog = new ContentDialog
        {
            Title = profile == null ? "New profile" : "Edit profile",
            PrimaryButtonText = "Save profile",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };
        EditorDialogPresentation.Configure(_dialog, 768, new Thickness(24, 24, 24, 0));
        _dialog.CornerRadius = new CornerRadius(12); _dialog.Resources["OverlayCornerRadius"] = new CornerRadius(12);
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(new TextBlock { Text = profile == null ? "New profile" : "Edit profile", FontSize = 18, Height = 18, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Set the avatar, name, PIN, and access rules for this profile.", FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.Normal, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        _dialog.Title = heading;
        void Reflow()
        {
            foreach (var text in heading.Children.OfType<TextBlock>()) text.TextAlignment = xamlRoot.Size.Width < 640 ? TextAlignment.Center : TextAlignment.Left;
            _nameBox.FontSize = _pinBox.FontSize = xamlRoot.Size.Width < 768 ? 16 : 14;
            ReflowAvatarGrids(); foreach (var apply in _viewportReflows) apply();
            EditorDialogPresentation.ReflowCommands(_dialog, xamlRoot.Size.Width < 640, new Thickness(24), buttonHeight: 36, stackNarrow: true);
            foreach (var button in EditorDialogPresentation.Descendants<Button>(_dialog).Where(button => button.Name is "PrimaryButton" or "CloseButton")) button.CornerRadius = new CornerRadius(10);
            var background = EditorDialogPresentation.Descendants<Border>(_dialog).FirstOrDefault(border => border.Name == "BackgroundElement");
            if (background != null)
            {
                background.Width = Math.Min(768, Math.Max(0, xamlRoot.Size.Width - 32));
                background.MaxHeight = Math.Max(160, xamlRoot.Size.Height - 64);
            }
        }
        void ViewportChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Reflow();
        _dialog.Opened += (_, _) => { xamlRoot.Changed += ViewportChanged; Reflow(); };
        _dialog.SizeChanged += (_, _) => Reflow();
        _dialog.Closed += (_, _) => xamlRoot.Changed -= ViewportChanged;

        _avatarPresetGrid.SizeChanged += (_, _) => ReflowAvatarGrids();
        _avatarStyleGrid.SizeChanged += (_, _) => ReflowAvatarGrids();
        foreach (var control in new Control[] { _nameBox, _pinBox })
        {
            control.Background = Brush("AppBackgroundBrush"); control.BorderBrush = Brush("BorderBrush");
            control.CornerRadius = new CornerRadius(10); control.Padding = new Thickness(12, 4, 12, 4);
        }
        _clearPinButton.Style = (Style)Application.Current.Resources["GhostButtonStyle"]; _clearPinButton.Foreground = Brush("SecondaryTextBrush");
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
            authApi, profileResponse.MaxAdvisoryAgeSupported, profileResponse.RequireAdvisoryAgeSupported);
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
        if (_profile != null && !_ratingValues.Contains(_profile.MaxContentRating, StringComparer.OrdinalIgnoreCase))
        {
            _ratingValues.Add(_profile.MaxContentRating); _ratingBox.Items.Add($"{_profile.MaxContentRating} (saved limit)");
            _ratingBox.SelectedIndex = _ratingValues.Count - 1;
        }
        _advisoryBox.Items.Add("No limit");
        for (var age = 1; age <= 21; age++) _advisoryBox.Items.Add($"Ages {age} and under");
        _advisoryBox.SelectedIndex = _profile?.MaxAdvisoryAge is >= 1 and <= 21 ? _profile.MaxAdvisoryAge.Value : 0;
        _requireAdvisory.IsOn = _profile?.RequireAdvisoryAge == true;
        _requireAdvisory.IsEnabled = _advisoryBox.SelectedIndex > 0;
        _advisoryBox.SelectionChanged += (_, _) => _requireAdvisory.IsEnabled = _advisoryBox.SelectedIndex > 0;
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
        var root = new StackPanel { MaxWidth = 720, Spacing = 20, HorizontalAlignment = HorizontalAlignment.Stretch };
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
            Margin = new Thickness(0, 0, 8, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root,
        };
    }

    private Border BuildProfileSection()
    {
        var section = Section("Profile", "Choose an avatar and basic details.");
        var body = (StackPanel)section.Child;

        var basic = new Grid { ColumnSpacing = 16 };
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(208) });
        basic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var previewBorder = new Border
        {
            Name = "ProfileAvatarPreviewRing",
            Width = 100,
            Height = 100,
            Margin = new Thickness(-2),
            CornerRadius = new CornerRadius(50),
            Background = Brush("SurfaceBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Grid { Children = { _avatarFallback, _avatarPreview } },
        };
        _avatarFallback.Foreground = Brush("AccentBrush");
        var previewStack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        previewStack.Children.Add(new Grid { Width = 96, Height = 96, Children = { previewBorder } });
        _avatarNamePreview.TextAlignment = TextAlignment.Center;
        _avatarStatus.Foreground = Brush("SecondaryTextBrush");
        _avatarStatus.TextAlignment = TextAlignment.Center;
        previewStack.Children.Add(new StackPanel { Children = { _avatarNamePreview, _avatarStatus } });
        var previewCard = new Border { Name = "ProfileAvatarPreviewCard", CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), BorderBrush = Brush("BorderBrush"), Padding = new Thickness(20, 16, 20, 16), Child = previewStack };
        Grid.SetColumn(previewCard, 0);
        basic.Children.Add(previewCard);

        var fields = new StackPanel { Spacing = 16 };
        fields.Children.Add(Field("Name", _nameBox));
        var pinField = new StackPanel { Spacing = 8 };
        pinField.Children.Add(_pinLabel);
        pinField.Children.Add(_pinBox);
        pinField.Children.Add(_clearPinButton);
        fields.Children.Add(pinField);
        Grid.SetColumn(fields, 1);
        basic.Children.Add(fields);
        basic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        basic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void ReflowBasic()
        {
            var narrow = (_dialog.XamlRoot?.Size.Width ?? basic.ActualWidth) < 1024;
            basic.ColumnSpacing = narrow ? 0 : 16;
            basic.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(208);
            basic.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(fields, narrow ? 0 : 1); Grid.SetRow(fields, narrow ? 1 : 0);
            basic.RowSpacing = narrow ? 16 : 0;
        }
        basic.SizeChanged += (_, _) => ReflowBasic(); _viewportReflows.Add(ReflowBasic);
        body.Children.Add(basic);

        var presetsSection = new StackPanel { Spacing = 12 };
        var presetsHeading = new StackPanel { Spacing = 4 };
        presetsHeading.Children.Add(Label("Preset avatars"));
        presetsHeading.Children.Add(new TextBlock
        {
            Text = "Pick a DiceBear style, shuffle fun options, or leave it blank to keep initials. A custom upload overrides presets.",
            FontSize = 12,
            LineHeight = 16,
            Foreground = Brush("SecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        presetsSection.Children.Add(presetsHeading);
        presetsSection.Children.Add(_avatarStyleGrid);
        var presetHeader = new Grid { ColumnSpacing = 10 };
        presetHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _avatarStyleStatus.Foreground = Brush("SecondaryTextBrush");
        _avatarStyleStatus.VerticalAlignment = VerticalAlignment.Center;
        presetHeader.Children.Add(_avatarStyleStatus);
        var moreContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE72C", FontSize = 16 }, new TextBlock { Text = "More options", FontSize = 14, FontWeight = FontWeights.Medium } } };
        var moreButton = new Button { Content = moreContent, Height = 32, MinHeight = 0, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(moreButton, "More options");
        moreButton.Click += (_, _) => { _avatarBatch++; BuildAvatarPresets(); };
        Grid.SetColumn(moreButton, 1);
        presetHeader.Children.Add(moreButton);
        presetsSection.Children.Add(presetHeader);
        presetsSection.Children.Add(_avatarPresetGrid);
        body.Children.Add(presetsSection);
        body.Children.Add(BuildUploadRow());
        return section;
    }

    private Border BuildAccessSection()
    {
        var section = Section("Access", "Content limits and library visibility for this profile.");
        var body = (StackPanel)section.Child;
        body.Children.Add(ToggleRow("Kids profile", "Seeds a safer default rating and library setup.", _kidsToggle));

        var limits = new Grid { Name = "ProfileAccessLimits", ColumnSpacing = 16, RowSpacing = 16 };
        limits.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        limits.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        limits.Children.Add(Field("Maximum content rating", _ratingBox));
        if (_ageSupported)
        {
            var advisory = Field("Maximum advisory age", _advisoryBox);
            var help = new TextBlock { FontSize = 12, LineHeight = 16, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap };
            advisory.Children.Add(help);
            var requireRow = ToggleRow("Hide titles without an advisory age",
                "Shows only titles rated at or under this age. Ages are looked up over time, so this profile may see few titles at first.", _requireAdvisory);
            requireRow.BorderThickness = new Thickness(0); requireRow.Padding = new Thickness(0, 4, 0, 0);
            if (_requireAgeSupported) advisory.Children.Add(requireRow);
            void ReflowAdvisory()
            {
                requireRow.Visibility = _advisoryBox.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
                help.Text = "Hides titles an advisory service such as Common Sense Media recommends for older viewers." +
                    (_requireAgeSupported && _advisoryBox.SelectedIndex > 0 && _requireAdvisory.IsOn ? "" : " Titles without an advisory age are limited by the content rating alone.");
            }
            _advisoryBox.SelectionChanged += (_, _) => ReflowAdvisory(); _requireAdvisory.Toggled += (_, _) => ReflowAdvisory(); ReflowAdvisory();
            limits.Children.Add(advisory);
        }
        limits.Children.Add(Field("Maximum playback quality", _qualityBox));
        limits.RowDefinitions.Add(new() { Height = GridLength.Auto }); limits.RowDefinitions.Add(new() { Height = GridLength.Auto });
        void ReflowLimits()
        {
            var narrow = (_dialog.XamlRoot?.Size.Width ?? limits.ActualWidth) < 640;
            limits.ColumnSpacing = narrow ? 0 : 16;
            limits.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            while (limits.RowDefinitions.Count < limits.Children.Count) limits.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var index = 0; index < limits.Children.Count; index++)
            {
                Grid.SetColumn((FrameworkElement)limits.Children[index], narrow ? 0 : index % 2);
                Grid.SetRow((FrameworkElement)limits.Children[index], narrow ? index : index / 2);
            }
        }
        limits.SizeChanged += (_, _) => ReflowLimits(); _viewportReflows.Add(ReflowLimits);
        body.Children.Add(limits);
        var libraryGroup = new StackPanel { Spacing = 12 };
        var libraryToggle = ToggleRow("Restrict libraries", "Limit this profile to a specific set of libraries.", _restrictLibrariesToggle);
        libraryToggle.BorderThickness = new Thickness(0); libraryToggle.Padding = new Thickness(0);
        libraryGroup.Children.Add(libraryToggle); libraryGroup.Children.Add(_libraryRows);
        body.Children.Add(new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = libraryGroup });
        return section;
    }

    private FrameworkElement BuildUploadRow()
    {
        var panel = new StackPanel { Spacing = 8 };
        if (!_avatarUploadEnabled)
        {
            var unavailable = new StackPanel { Spacing = 4 };
            unavailable.Children.Add(new TextBlock { Text = "Custom uploads are unavailable", FontSize = 14, LineHeight = 20, FontWeight = FontWeights.Medium });
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
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Child = unavailable,
            });
            return panel;
        }

        panel.Children.Add(Label("Custom upload"));
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
            labels.Children.Add(new TextBlock { Text = library.Name, FontSize = 14, LineHeight = 20, FontWeight = FontWeights.Medium });
            labels.Children.Add(new TextBlock
            {
                Text = library.Type,
                FontSize = 12,
                LineHeight = 16,
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
                CornerRadius = new CornerRadius(10),
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
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock { Text = style.Label, FontSize = 14, LineHeight = 20, FontWeight = FontWeights.Medium });
            content.Children.Add(new TextBlock
            {
                Text = AvatarStyleSummary(style.Id),
                FontSize = 12,
                LineHeight = 16,
                Foreground = Brush("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            var selected = style.Id == _activeAvatarStyle;
            var button = new Button
            {
                Content = content,
                Tag = style.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(16, 12, 16, 12),
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(1),
                BorderBrush = selected ? Brush("AccentBrush") : Brush("BorderBrush"),
                Background = AvatarSelectionBackground(selected, 0.08),
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

        ReflowAvatarGrids();
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
                Width = 64,
                Height = 64,
                Stretch = Stretch.UniformToFill,
                Source = (ImageSource)RemoteImageConverter.Convert(
                    BuildDiceBearUrl(_activeAvatarStyle, seed),
                    typeof(ImageSource),
                    null!,
                    string.Empty),
            };
            image.Loaded += (_, _) =>
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(image);
                var shape = visual.Compositor.CreateRoundedRectangleGeometry();
                shape.Size = new System.Numerics.Vector2(64); shape.CornerRadius = new System.Numerics.Vector2(16);
                visual.Clip = visual.Compositor.CreateGeometricClip(shape);
            };
            var button = new Button
            {
                Content = image,
                Padding = new Thickness(8), Width = 80, Height = 80,
                CornerRadius = new CornerRadius(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                BorderThickness = new Thickness(1),
                BorderBrush = preset == _selectedAvatarPreset ? Brush("AccentBrush") : Brush("BorderBrush"),
                Background = AvatarSelectionBackground(preset == _selectedAvatarPreset, 0.05),
            };
            ToolTipService.SetToolTip(button, AvatarPresetLabel(_activeAvatarStyle, seed));
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
        ReflowAvatarGrids();
    }

    private void ReflowAvatarGrids()
    {
        var width = _dialog.XamlRoot?.Size.Width ?? 900;
        ReflowAvatarGrid(_avatarStyleGrid, width >= 1280 ? 4 : width >= 640 ? 2 : 1);
        ReflowAvatarGrid(_avatarPresetGrid, width >= 1280 ? 6 : width >= 1024 ? 5 : width >= 640 ? 4 : 3);
    }
    private static void ReflowAvatarGrid(Grid grid, int columns)
    {
        var rows = (int)Math.Ceiling(grid.Children.Count / (double)columns);
        if (grid.ColumnDefinitions.Count == columns && grid.RowDefinitions.Count == rows) return;
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < rows; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < grid.Children.Count; index++) if (grid.Children[index] is FrameworkElement child) { Grid.SetColumn(child, index % columns); Grid.SetRow(child, index / columns); }
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
                MaxContentRating = _ratingValues[Math.Max(0, _ratingBox.SelectedIndex)],
                MaxAdvisoryAgeSupported = _ageSupported, RequireAdvisoryAgeSupported = _requireAgeSupported,
                MaxAdvisoryAge = _advisoryBox.SelectedIndex > 0 ? _advisoryBox.SelectedIndex : null,
                RequireAdvisoryAge = _requireAdvisory.IsOn,
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
            _avatarPreview.Source = (ImageSource)RemoteImageConverter.Convert(
                BuildDiceBearUrl(selectedParts[1], selectedParts[2]),
                typeof(ImageSource),
                null!,
                string.Empty);
            _avatarPreview.Visibility = Visibility.Visible;
            _avatarFallback.Visibility = Visibility.Collapsed;
            _avatarStatus.Text = AvatarPresetLabel(selectedParts[1], selectedParts[2]);
            return;
        }
        if (!_removeUploadedAvatar && !string.IsNullOrWhiteSpace(_profile?.AvatarUrl))
        {
            _avatarPreview.Source = (ImageSource)RemoteImageConverter.Convert(
                _profile.AvatarUrl,
                typeof(ImageSource),
                null!,
                string.Empty);
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

    private static string AvatarPresetLabel(string style, string seed)
        => string.Join(" ", seed.Split('-').Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));

    private static Border Section(string title, string description)
    {
        var stack = new StackPanel { Spacing = 16 };
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock { Text = title, FontSize = 14, LineHeight = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = description, FontSize = 14, LineHeight = 20, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(heading);
        return new Border
        {
            Name = $"ProfileEditor{title}Section",
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = stack,
        };
    }

    private static StackPanel Field(string label, Control control)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Label(label));
        stack.Children.Add(control);
        return stack;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 14,
        Height = 14,
        LineHeight = 14,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = FontWeights.Medium,
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
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
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

    private static Brush AvatarSelectionBackground(bool selected, double opacity)
        => selected && Brush("AccentBrush") is SolidColorBrush accent
            ? new SolidColorBrush(accent.Color) { Opacity = opacity }
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

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
