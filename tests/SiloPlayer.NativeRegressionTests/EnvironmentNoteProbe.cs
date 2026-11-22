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

    /// <summary>The settings row that holds the control: the shared <c>AdminSettingsRow</c>, or the Integrations page's two-column field grid.</summary>
    internal static DependencyObject? RowOf(DependencyObject control)
    {
        for (var node = VisualTreeHelper.GetParent(control); node != null; node = VisualTreeHelper.GetParent(node))
            if (node.GetType().Name == "AdminSettingsRow" || node is Grid { ColumnSpacing: 20 } grid && grid.ColumnDefinitions.Count == 2) return node;
        return null;
    }

    /// <summary>Visible itself and through every ancestor up to <paramref name="page"/>, and laid out with a non-zero size.</summary>
    internal static bool EffectivelyShown(FrameworkElement element, DependencyObject page)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        for (DependencyObject? node = element; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(node, page)) return true;
        }
        return false;
    }

    /// <summary>Null when the managed row is correct, else what is wrong.</summary>
    internal static string? Verify(DependencyObject page, Control control, string note)
    {
        var problem = VerifyCore(page, control, note);
        if (problem != null) Program.Log("NOTE PROBE: " + problem);
        return problem;
    }

    private static string? VerifyCore(DependencyObject page, Control control, string note)
    {
        (page as UIElement)?.UpdateLayout();
        if (control.IsEnabled) return "the control is still enabled";
        // The note must be in this control's own row and really shown: it and every ancestor up to the page visible, and laid out with a size.
        var row = RowOf(control);
        if (row == null) return "the control is not inside a settings row";
        if (!Descendants(row).OfType<TextBlock>().Any(text => text.Text == note && EffectivelyShown(text, page)))
            return "no shown TextBlock in the control's row reads \"" + note + "\"";
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
