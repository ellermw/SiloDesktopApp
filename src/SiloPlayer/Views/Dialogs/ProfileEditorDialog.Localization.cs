using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Views.Dialogs;

/// <summary>
/// Interface language for the open profile editor. Fixed labels are keyed controls;
/// this refresh re-reads the text that depends on editor state (PIN, save action,
/// option lists, avatar summaries) without touching what the person entered.
/// The saved profile name and stored rating/quality values are never translated.
/// </summary>
public sealed partial class ProfileEditorDialog
{
    private readonly List<Action> _localeRefreshers = [];

    private void RefreshPinText()
    {
        var hasPin = _profile?.HasPin == true;
        _pinLabel.Text = _clearPin ? AppStrings.Get("web.profiles.editor.pinRemoved")
            : hasPin ? AppStrings.Get("web.profiles.editor.newPin") : AppStrings.Get("web.profiles.editor.pinOptional");
        _pinBox.PlaceholderText = _clearPin ? AppStrings.Get("web.profiles.editor.pinRemovedOnSave") : AppStrings.Get("web.profiles.editor.pinPlaceholder");
        _clearPinButton.Content = _clearPin ? AppStrings.Get("web.profiles.editor.keepPin") : AppStrings.Get("web.profiles.editor.removePin");
    }

    // Desktop-only wording for a stored rating outside the shared list; no catalog key exists.
    private static string SavedLimitLabel(string rating) => $"{rating} (saved limit)";

    private static IEnumerable<string> AdvisoryLabels()
    {
        yield return AppStrings.Get("web.profiles.helpers.advisoryAge.noLimit");
        for (var age = 1; age <= 21; age++)
            yield return AppStrings.Get("web.profiles.helpers.advisoryAge.agesAndUnder",
                new Dictionary<string, object?> { ["age"] = age, ["age_text"] = age.ToString("N0", AppStrings.FormatCulture) }, $"Ages {age} and under");
    }

    private void RefreshLocale()
    {
        RefreshPinText();
        _dialog.CloseButtonText = AppStrings.Get("common.cancel");
        if (_saving) _dialog.PrimaryButtonText = AppStrings.Get("web.profiles.editor.saving");
        else if (_addSession?.CreatedProfile == null) _dialog.PrimaryButtonText = AppStrings.Get("web.profiles.editor.save");
        // Relabelling is not a user choice and must not disable the kids preset.
        var applyingKidsPreset = _applyingKidsPreset;
        _applyingKidsPreset = true;
        try
        {
            ReplaceItems(_ratingBox, RatingOptions.Select(option => option.Label)
                .Concat(_ratingValues.Skip(RatingOptions.Length).Select(SavedLimitLabel)));
        }
        finally { _applyingKidsPreset = applyingKidsPreset; }
        ReplaceItems(_advisoryBox, AdvisoryLabels());
        ReplaceItems(_qualityBox, QualityOptions.Select(option => option.Label));
        BuildAvatarStyles();
        UpdateAvatarPreview();
        foreach (var refresh in _localeRefreshers) refresh();
    }

    /// <summary>Replaces display strings in place; the selected index (the stored value) is kept.</summary>
    private static void ReplaceItems(ComboBox box, IEnumerable<string> labels)
    {
        var selected = box.SelectedIndex;
        var index = 0;
        foreach (var label in labels)
        {
            if (index < box.Items.Count) box.Items[index] = label;
            index++;
        }
        box.SelectedIndex = selected;
    }
}
