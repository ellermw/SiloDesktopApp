using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.ViewModels;

public partial class LoginViewModel
{
    private readonly Dictionary<string, (Func<string> Render, string Last)> _shellText = [];

    private string ShellText(string property, Func<string> render)
    {
        var text = render();
        _shellText[property] = (render, text);
        return text;
    }

    private static string ShellProblem(Exception exception, string english)
        => exception is ApiException problem ? ApiProblemText.Display(problem, english) : english;

    private static string ExternalSignInText(string reason) => AppStrings.Get(reason switch
    {
        "not_permitted" => "web.auth.oauthFailure.notPermitted",
        "account_required" => "web.auth.oauthFailure.accountRequired",
        "email_in_use" => "web.auth.oauthFailure.emailInUse",
        "identity_linked_elsewhere" => "web.auth.oauthFailure.identityLinkedElsewhere",
        "account_disabled" => "web.auth.oauthFailure.accountDisabled",
        "provider_unavailable" => "web.auth.oauthFailure.providerUnavailable",
        "state_invalid" => "web.auth.oauthFailure.stateInvalid",
        "session_expired" => "web.auth.oauthFailure.sessionExpired",
        "already_linked" => "web.auth.oauthFailure.alreadyLinked",
        _ => "web.auth.oauthFailure.fallback"
    }, fallback: SiloPlayer.Core.Services.ExternalSignInErrors.Describe(reason));

    private static string NetworkSignInText(string reason, string provider) => reason switch
    {
        "network_identity_required" => AppStrings.Get("web.auth.networkSignIn.addressRequired", new Dictionary<string, object?> { ["provider"] = provider }),
        "not_permitted" => AppStrings.Get("web.auth.networkSignIn.notPermitted", new Dictionary<string, object?> { ["provider"] = provider }),
        "email_in_use" => AppStrings.Get("web.auth.networkSignIn.emailInUse", new Dictionary<string, object?> { ["provider"] = provider }),
        "permission_denied" => ExternalSignInText("account_disabled"),
        _ => ExternalSignInText(reason)
    };

    public void RefreshLocalizedText()
    {
        foreach (var (property, text) in _shellText.ToArray())
        {
            switch (property)
            {
                case nameof(ErrorMessage) when ErrorMessage == text.Last: ErrorMessage = ShellText(property, text.Render); break;
                case nameof(DeviceStatusMessage) when DeviceStatusMessage == text.Last: DeviceStatusMessage = ShellText(property, text.Render); break;
                case nameof(DeviceBindingNotice) when DeviceBindingNotice == text.Last: DeviceBindingNotice = ShellText(property, text.Render); break;
                case nameof(LoginSubtitle) when LoginSubtitle == text.Last: LoginSubtitle = ShellText(property, text.Render); break;
                default: _shellText.Remove(property); break;
            }
        }
        OnPropertyChanged(nameof(SessionRestoreMessage)); OnPropertyChanged(nameof(EndedSessionTitle)); OnPropertyChanged(nameof(EndedSessionMessage));
        foreach (var option in NetworkProviders) option.RefreshLocalizedText();
    }
}
