using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Dialogs;

public sealed partial class FeatureTourDialog
{
    private void RefreshTourLanguage()
    {
        AppLocalization.ApplyDirection(this);
        CloseButtonText = AppStrings.Get("web.onboarding.tour.skip");
        SecondaryButtonText = _index > 0 ? AppStrings.Get("common.back") : "";
        PrimaryButtonText = _steps.Count == 0 ? AppStrings.Get("common.done")
            : _steps[_index].Kind == "handoff" ? _steps[_index].Title ?? AppStrings.Get("web.onboarding.tour.finish")
            : _index == _steps.Count - 1 ? AppStrings.Get("common.done")
            : _index == 0 ? AppStrings.Get("web.onboarding.tour.showMe") : AppStrings.Get("web.onboarding.tour.next");
        if (_saveError.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
            _saveError.Text = AppStrings.Get("web.onboarding.tour.saveFailed") + " " + _progress.Error;
        ReflowPresentation();
    }
}
