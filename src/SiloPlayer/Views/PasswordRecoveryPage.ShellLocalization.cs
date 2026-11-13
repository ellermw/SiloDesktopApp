using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class PasswordRecoveryPage
{
    private Func<string> _localizedTitle = () => AppStrings.Get("web.auth.passwordRules.resetPasswordTitle");
    private Func<string> _localizedDescription = () => "";
    private Func<string>? _localizedStatus;

    private void SetRecoveryStatus(Func<string> render)
    {
        _localizedStatus = render;
        _status.Text = render();
    }

    private void RefreshRecoveryLanguage()
    {
        AppLocalization.ApplyDirection(this);
        _title.Text = _localizedTitle();
        _description.Text = _localizedDescription();
        if (_localizedStatus != null) _status.Text = _localizedStatus();
    }
}
