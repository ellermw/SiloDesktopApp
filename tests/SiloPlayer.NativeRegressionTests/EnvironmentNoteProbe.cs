using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

/// <summary>
/// Shared native check for a setting the server's environment fixes: the control is disabled (as in the official
/// <c>disabled={managedByEnv != null}</c>), a visible note under the row carries the official text, and the same text is in
/// the control's help text so a screen reader reads it with the row. Expected text is spelled out, not read from the catalog.
/// </summary>
internal static class EnvironmentNoteProbe
{
    /// <summary>The official note for a named variable (en.json <c>web.admin.settings.fields.managedByEnv</c>).</summary>
    internal static string Named(string variable) => "Set by " + variable + " in the server’s environment.";
    /// <summary>The official field message used when the server named no variable.</summary>
    internal const string Unnamed = "Set by the environment. Change it there and restart the affected service.";

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    /// <summary>Null when the managed row is correct, else what is wrong.</summary>
    internal static string? Verify(DependencyObject page, Control control, string note)
    {
        if (control.IsEnabled) return "the control is still enabled";
        if (!Descendants(page).OfType<TextBlock>().Any(text => text.Text == note && text.Visibility == Visibility.Visible))
            return "no visible TextBlock reads \"" + note + "\"";
        var help = AutomationProperties.GetHelpText(control);
        return help.Contains(note, StringComparison.Ordinal) ? null : "the control's help text lacks the note (it is \"" + help + "\")";
    }

    /// <summary>Null when an editable control is enabled and its help text carries no environment note.</summary>
    internal static string? VerifyEditable(Control control)
    {
        if (!control.IsEnabled) return "the control is disabled";
        var help = AutomationProperties.GetHelpText(control);
        return help.Contains("in the server’s environment.", StringComparison.Ordinal) || help.Contains(Unnamed, StringComparison.Ordinal) ? "the help text carries a note" : null;
    }
}
