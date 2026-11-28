using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Views.Dialogs;

/// <summary>
/// Edit Metadata names every input by its visible label (official #5118) and each lock toggle "Lock {field}" with its locked state (#5295).
/// Real dialog with a synthetic server, at 1100 and 460 wide.
/// </summary>
internal static partial class ConditionalEditorsNativeFixture
{
    private static async Task FieldNamesAsync(FrameworkElement owner, Wire wire, string size)
    {
        wire.Reset();
        var dialog = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(200);
        try
        {
            var labels = Field<Dictionary<string, TextBlock>>(dialog, "_labelBlocks");
            // Realize every section so each input has its place in the tree, as a person tabbing through would.
            foreach (var section in new[] { "Dates & Ratings", "Tags & Genres", "External IDs", "General" })
            {
                try { Section(dialog, section); } catch (InvalidOperationException) { continue; }
                await Task.Delay(100);
            }
            string NameOf(UIElement element) => FrameworkElementAutomationPeer.CreatePeerForElement(element).GetName();

            var texts = Field<Dictionary<string, TextBox>>(dialog, "_textInputs");
            foreach (var (key, input) in texts)
                Check(NameOf(input) == labels[key].Text && labels[key].Text.Length > 0, $"{size}: the {key} input is named '{NameOf(input)}', expected its label '{labels[key].Text}'.");
            var numbers = Field<Dictionary<string, NumberBox>>(dialog, "_numberInputs");
            foreach (var (key, input) in numbers)
            {
                var named = NameOf(input) == labels[key].Text || Descendants<TextBox>(input).Any(box => NameOf(box) == labels[key].Text);
                Check(named && labels[key].Text.Length > 0, $"{size}: the {key} number input is named '{NameOf(input)}', expected its label '{labels[key].Text}'.");
            }
            var tags = Field<Dictionary<string, MetadataTagsInput>>(dialog, "_tagInputs");
            Check(tags.Count > 0, $"{size}: the movie editor has tag inputs to name.");
            foreach (var (key, input) in tags)
            {
                var entry = Descendants<TextBox>(input).Single();
                Check(NameOf(entry) == labels[key].Text && labels[key].Text.Length > 0, $"{size}: the {key} tag input is named '{NameOf(entry)}', expected its label '{labels[key].Text}'.");
            }
            Program.Log($"FIELD NAMES {size}: {texts.Count} text, {numbers.Count} number and {tags.Count} tag inputs named by their labels.");

            // The lock toggles: "Lock {field label}" whatever the state, and the state as the toggle state.
            var state = Field<MetadataEditState>(dialog, "_state");
            var locks = Field<List<(string Field, ToggleButton Button)>>(dialog, "_lockButtons");
            Check(locks.Count > 0, $"{size}: the movie editor has lock toggles.");
            var effective = state.EffectiveLocks(Movie().LockedFields);
            foreach (var (key, button) in locks)
            {
                var expectedName = "Lock " + labels[key].Text;
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
                var toggle = peer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                var locked = effective.Contains(state.FieldLockMap[key]);
                Check(peer.GetName() == expectedName, $"{size}: the {key} lock is named '{peer.GetName()}', expected '{expectedName}'.");
                Check(toggle is not null && (toggle.ToggleState == ToggleState.On) == locked, $"{size}: the {key} lock reports {toggle?.ToggleState}, locked is {locked}.");
            }
            var title = locks.First(pair => pair.Field == "title").Button;
            var titlePeer = FrameworkElementAutomationPeer.CreatePeerForElement(title);
            var before = ((IToggleProvider)titlePeer.GetPattern(PatternInterface.Toggle)!).ToggleState;
            ((IToggleProvider)titlePeer.GetPattern(PatternInterface.Toggle)!).Toggle(); await Task.Delay(100);
            var after = ((IToggleProvider)titlePeer.GetPattern(PatternInterface.Toggle)!).ToggleState;
            Check(before != after && titlePeer.GetName() == "Lock " + labels["title"].Text, $"{size}: toggling the title lock must flip its state and keep its name (was {before}, now {after}, name '{titlePeer.GetName()}').");
            Section(dialog, "General"); await Task.Delay(80);
            await CaptureAsync(dialog, $"edit-metadata-locks-{size}.png");
        }
        finally { Close(dialog); await showing; }
    }
}
