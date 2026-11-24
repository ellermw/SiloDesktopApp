using System.ComponentModel;
using SiloPlayer.Core.Localization;
using SiloPlayer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

/// <summary>
/// The Activate device page, matched to the official WebUI page (web/src/pages/ActivateDevice.tsx,
/// silo-server 3b2b32168, #4652). A person enters, or arrives with, the code a device shows, checks it
/// against the device and approves or declines that device's sign-in. The code input is grouped 4+4 as it
/// is typed. The state and message rules are in <see cref="SiloPlayer.Core.Services.DeviceActivation"/>.
/// </summary>
/// <remarks>
/// Navigation parameter can be:
/// <list type="bullet">
///   <item>A <see cref="DeviceActivationParameters"/> with token/code when launched from a deep link.</item>
///   <item><c>null</c> — the person enters the code manually.</item>
/// </list>
/// The web page's "Open in the Silo app" link is for phones that have the Silo app; it does not apply to
/// a desktop app and is not offered.
/// </remarks>
public sealed partial class ActivateDevicePage : Page
{
    public ActivateDeviceViewModel ViewModel { get; }

    // Re-entrancy guard for the CodeBox TextChanged handler. Updating the bound
    // property from inside the handler fires TextChanged again; without this we'd
    // recurse until StackOverflow.
    private bool _suppressCodeChange;
    private string _profileChoicesSignature = "";

    public ActivateDevicePage()
    {
        ViewModel = App.Services.GetRequiredService<ActivateDeviceViewModel>();
        this.InitializeComponent();
        // Official #4287: Enter confirming an IME candidate must not submit the device code.
        SiloPlayer.Helpers.ImeCompositionGuard.Attach(CodeBox);
        SizeChanged += (_, e) => ActivateLayout.Width = Math.Min(448, Math.Max(1, e.NewSize.Width - 48));
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Loaded += (_, _) => { ViewModel.PropertyChanged -= ViewModel_PropertyChanged; ViewModel.PropertyChanged += ViewModel_PropertyChanged; SyncDynamicParts(); };
        AppLocalization.Register(this, () => { AppLocalization.ApplyDirection(this); ViewModel.RefreshLocalizedText(); SyncDynamicParts(); });
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        string? token = null;
        string? code = null;
        if (e.Parameter is DeviceActivationParameters p)
        {
            token = p.Token;
            code = p.Code;
        }

        await ViewModel.InitializeAsync(token, code);
        SyncDynamicParts();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Leave();
        base.OnNavigatedFrom(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) => SyncDynamicParts();

    /// <summary>Parts of the page that are built from more than one value: the "signed in as" sentence and the Quick Connect profile list.</summary>
    private void SyncDynamicParts()
    {
        RenderSignedInAs();
        RenderProfileChoices();
        if (ViewModel.QuickConnectPin.Length == 0 && QuickConnectPinBox.Password.Length > 0) QuickConnectPinBox.Password = "";
    }

    /// <summary>
    /// "You'll sign it in to {server} as <b>{username}</b>. <link>Not you? Switch account</link>": the sentence
    /// stays one translatable message; its two tags style the account name and make the last part a link.
    /// </summary>
    private void RenderSignedInAs()
    {
        var username = ViewModel.SignedInUsername;
        var block = SignedInAsText;
        if (!ViewModel.ShowApproval || string.IsNullOrEmpty(username))
        {
            if (block.Inlines.Count > 0) block.Inlines.Clear();
            // The next approval card builds the sentence again, also when its wording is the same.
            block.Tag = null;
            return;
        }
        var arguments = new Dictionary<string, object?> { ["server"] = ViewModel.DisplayServerName, ["username"] = username };
        string text;
        IReadOnlyList<MessageTagSpan> spans;
        if (!AppStrings.TryGetTagged("web.auth.activate.signInAs", arguments, out var formatted, out spans) || formatted == null)
        {
            // The official English, with the same two tags.
            var prefix = $"You'll sign it in to {ViewModel.DisplayServerName} as ";
            text = prefix + username + ". Not you? Switch account";
            spans = [new("name", prefix.Length, username.Length), new("switch", text.Length - "Not you? Switch account".Length, "Not you? Switch account".Length)];
        }
        else text = formatted;

        var signature = text + "|" + string.Join(",", spans.Select(span => $"{span.Name}:{span.Start}:{span.Length}"));
        if (Equals(block.Tag, signature)) return;
        block.Tag = signature;
        block.Inlines.Clear();
        var position = 0;
        foreach (var span in spans.OrderBy(span => span.Start))
        {
            if (span.Start < position || span.Start + span.Length > text.Length) continue;
            if (span.Start > position) block.Inlines.Add(new Run { Text = text[position..span.Start] });
            var content = text.Substring(span.Start, span.Length);
            switch (span.Name)
            {
                case "name":
                    block.Inlines.Add(new Run { Text = content, FontWeight = Microsoft.UI.Text.FontWeights.Medium });
                    break;
                case "switch":
                    var link = new Hyperlink { UnderlineStyle = UnderlineStyle.Single };
                    link.Inlines.Add(new Run { Text = content });
                    link.Click += SwitchAccount_Click;
                    block.Inlines.Add(link);
                    break;
                default:
                    block.Inlines.Add(new Run { Text = content });
                    break;
            }
            position = span.Start + span.Length;
        }
        if (position < text.Length) block.Inlines.Add(new Run { Text = text[position..] });
    }

    private void RenderProfileChoices()
    {
        if (!ViewModel.ShowQuickConnectChoices)
        {
            if (ProfileChoices.Children.Count > 0) ProfileChoices.Children.Clear();
            _profileChoicesSignature = "";
            return;
        }
        // The radios are built once per profile list. A choice, or a decision starting or ending, updates them
        // in place: rebuilding would remove the radio that has focus, from inside its own Checked handler.
        var selectedId = ViewModel.QuickConnectSelected?.Profile.Id;
        var canDecide = ViewModel.CanDecide;
        var signature = string.Join("|", ViewModel.QuickConnectProfiles.Select(choice => $"{choice.Profile.Id}:{choice.Name}:{choice.Profile.HasPin}"));
        if (signature != _profileChoicesSignature || ProfileChoices.Children.Count != ViewModel.QuickConnectProfiles.Count)
        {
            _profileChoicesSignature = signature;
            ProfileChoices.Children.Clear();
            foreach (var choice in ViewModel.QuickConnectProfiles)
            {
                var label = new StackPanel { Spacing = 2 };
                label.Children.Add(new TextBlock { Text = choice.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
                if (choice.Hint.Length > 0)
                    label.Children.Add(new TextBlock { Text = choice.Hint, FontSize = 12, Style = (Style)Application.Current.Resources["SecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });
                var id = choice.Profile.Id;
                var radio = new RadioButton
                {
                    GroupName = "QuickConnectProfile",
                    Content = label,
                    IsChecked = id == selectedId,
                    IsEnabled = canDecide,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Tag = id,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(radio, choice.Hint.Length > 0 ? choice.Name + ", " + choice.Hint : choice.Name);
                radio.Checked += (_, _) => { if (ViewModel.QuickConnectSelected?.Profile.Id != id) ViewModel.ChooseQuickConnectProfile(id); };
                ProfileChoices.Children.Add(radio);
            }
        }
        // The view model's selection, not each choice's IsSelected, which is marked only after the choice is announced.
        foreach (var radio in ProfileChoices.Children.OfType<RadioButton>())
        {
            var isSelected = Equals(radio.Tag, selectedId);
            if (radio.IsChecked != isSelected) radio.IsChecked = isSelected;
            if (radio.IsEnabled != canDecide) radio.IsEnabled = canDecide;
        }
    }

    private void QuickConnectPinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel.QuickConnectPin != QuickConnectPinBox.Password) ViewModel.QuickConnectPin = QuickConnectPinBox.Password;
    }

    private async void SwitchAccount_Click(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        try
        {
            await ViewModel.SwitchAccountAsync();
            App.Services.GetRequiredService<Helpers.NavigationService>().Navigate<LoginPage>();
        }
        catch
        {
            // Navigation not configured for this flow — signing out already returned the app to its sign-in route.
        }
    }

    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressCodeChange) return;
        if (sender is not TextBox box) return;

        var normalized = ActivateDeviceViewModel.NormalizeCode(box.Text);
        if (normalized == box.Text) return;

        _suppressCodeChange = true;
        try
        {
            var caretAtEnd = box.SelectionStart >= box.Text.Length;
            box.Text = normalized;
            ViewModel.CodeInput = normalized;
            if (caretAtEnd)
            {
                box.SelectionStart = box.Text.Length;
            }
        }
        finally
        {
            _suppressCodeChange = false;
        }
    }

    private void CodeBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (SiloPlayer.Helpers.ImeCompositionGuard.ShouldIgnore(SiloPlayer.Helpers.ImeCompositionGuard.Attach(CodeBox), e)) return;
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.SubmitCodeCommand.CanExecute(null))
        {
            ViewModel.SubmitCodeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        // Deliberately routed via the NavigationService with no nav entries added —
        // this matches the web flow where the Sign-in prompt sends the user to /login.
        // If a caller has wired up LoginPage as a reachable page, this will work; if
        // not it will silently no-op (the caller is expected to wire navigation).
        try
        {
            var nav = App.Services.GetRequiredService<Helpers.NavigationService>();
            nav.Navigate<LoginPage>();
        }
        catch
        {
            // Navigation not configured for this flow — nothing we can do from here.
        }
    }
}

/// <summary>
/// Navigation parameter for <see cref="ActivateDevicePage"/>. Populated from a
/// verification URI (<c>token</c>) or a short user code (<c>code</c>). Exactly one
/// should be set; both <c>null</c> means the user will enter the code manually.
/// </summary>
public class DeviceActivationParameters
{
    public string? Token { get; set; }
    public string? Code { get; set; }
}
