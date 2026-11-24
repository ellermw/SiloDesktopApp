using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views;

/// <summary>
/// Settings > Playback > Show Next Up (playback.next_up_prompt_seconds), the choices
/// official offers: At end, 10 s, 30 s, 1 min and 2 min before the end of the file
/// (silo-server 3b2b32168, #4502). A value another client stored off the list is
/// named in the box instead of being added as a choice.
/// </summary>
public sealed partial class SettingsPage
{
    private static string NextUpPromptChoiceLabel(int seconds)
    {
        var (key, count) = NextUpPromptTiming.LabelKey(seconds);
        return count is { } n
            ? AppStrings.Get(key, Args(("count", n), ("count_text", n.ToString(AppStrings.FormatCulture))),
                NextUpPromptTiming.EnglishLabel(seconds))
            : AppStrings.Get(key, fallback: NextUpPromptTiming.EnglishLabel(seconds));
    }

    /// <summary>Builds the choices on first use and renames them after a language change.</summary>
    private void BuildNextUpPromptChoices()
    {
        var previous = _refreshingInlineChoices;
        _refreshingInlineChoices = true;
        try
        {
            if (NextUpPromptComboBox.Items.Count == 0)
                foreach (var seconds in NextUpPromptTiming.Choices)
                    NextUpPromptComboBox.Items.Add(new ComboBoxItem { Tag = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            foreach (var item in NextUpPromptComboBox.Items.OfType<ComboBoxItem>())
                item.Content = NextUpPromptChoiceLabel(int.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture));
            SyncNextUpPromptComboBox();
        }
        finally { _refreshingInlineChoices = previous; }
    }

    private void SyncNextUpPromptComboBox()
    {
        var seconds = ViewModel.NextUpPromptSeconds;
        var suppressed = _suppressEvents; _suppressEvents = true;
        try
        {
            var match = NextUpPromptComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => (string)item.Tag == seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            NextUpPromptComboBox.SelectedItem = match;
            // Off the list: no choice is selected and the box names the stored value.
            NextUpPromptComboBox.PlaceholderText = match is null ? NextUpPromptChoiceLabel(seconds) : "";
        }
        finally { _suppressEvents = suppressed; }
    }

    private void NextUpPromptComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingInlineChoices || _suppressEvents) return;
        if (NextUpPromptComboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            ViewModel.NextUpPromptSeconds = seconds;
    }
}
