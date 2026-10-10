namespace SiloPlayer.Controls;

/// <summary>The catalog's content-width select triggers, independent of dropdown option widths.</summary>
internal static class CatalogToolbarChoices
{
    internal static void Apply(params ComboBox[] choices)
    {
        foreach (var choice in choices)
        {
            choice.MinWidth = 0; choice.Height = 36; choice.MinHeight = 0; choice.FontSize = 14;
            choice.HorizontalAlignment = HorizontalAlignment.Left;
            choice.Padding = new(12, 0, 12, 0);
            void Resize()
            {
                var selected = choice.SelectedItem is ComboBoxItem item ? item.Content : choice.SelectedItem;
                var label = selected?.GetType().GetProperty("Label")?.GetValue(selected)?.ToString() ?? selected?.ToString() ?? "";
                var text = new TextBlock { Text = label, FontFamily = choice.FontFamily, FontSize = choice.FontSize, FontWeight = choice.FontWeight };
                text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                // WinUI reserves32px for its chevron plus24px content margin.
                // Include both borders so the selected text survives rounding.
                choice.Width = Math.Ceiling(text.DesiredSize.Width) + 58;
            }
            choice.RegisterPropertyChangedCallback(Control.FontFamilyProperty, (_, _) => Resize());
            choice.RegisterPropertyChangedCallback(Control.FontSizeProperty, (_, _) => Resize());
            choice.RegisterPropertyChangedCallback(Control.FontWeightProperty, (_, _) => Resize());
            choice.SelectionChanged += (_, _) => Resize(); choice.Loaded += (_, _) => Resize(); Resize();
        }
    }
}
