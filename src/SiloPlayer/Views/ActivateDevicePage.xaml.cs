using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

/// <summary>
/// Mirror of the web <c>ActivateDevice</c> page. An already-signed-in user enters
/// a short code shown on another device (phone, TV, second PC) and approves or
/// denies that device's sign-in request. The code input is normalized to the
/// "ABCD-EFGH" format as the user types, matching the web implementation.
/// </summary>
/// <remarks>
/// Navigation parameter can be:
/// <list type="bullet">
///   <item>A <see cref="DeviceActivationParameters"/> with token/code when launched from a deep link.</item>
///   <item><c>null</c> — the user enters the code manually.</item>
/// </list>
/// </remarks>
public sealed partial class ActivateDevicePage : Page
{
    public ActivateDeviceViewModel ViewModel { get; }

    // Re-entrancy guard for the CodeBox TextChanged handler. Updating the bound
    // property from inside the handler fires TextChanged again; without this we'd
    // recurse until StackOverflow.
    private bool _suppressCodeChange;

    public ActivateDevicePage()
    {
        ViewModel = App.Services.GetRequiredService<ActivateDeviceViewModel>();
        this.InitializeComponent();
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

        UpdateSignedInText();
    }

    private void UpdateSignedInText()
    {
        var username = ViewModel.SignedInUsername;
        SignedInAsText.Text = string.IsNullOrEmpty(username)
            ? ""
            : $"Signed in as {username}.";
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
