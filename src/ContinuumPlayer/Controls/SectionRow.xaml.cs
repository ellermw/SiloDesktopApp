using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Controls;

public sealed partial class SectionRow : UserControl
{
    public static readonly DependencyProperty SectionProperty =
        DependencyProperty.Register(
            nameof(Section),
            typeof(HomeSectionWithItems),
            typeof(SectionRow),
            new PropertyMetadata(null, OnSectionChanged));

    public HomeSectionWithItems? Section
    {
        get => (HomeSectionWithItems?)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    public SectionRow()
    {
        this.InitializeComponent();
    }

    private static void OnSectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SectionRow row && e.NewValue is HomeSectionWithItems section)
        {
            row.UpdateSection(section);
        }
    }

    private void UpdateSection(HomeSectionWithItems section)
    {
        SectionTitle.Text = section.Title;
        ItemsRepeater.ItemsSource = section.Items;
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        PosterScrollViewer.ChangeView(
            Math.Max(0, PosterScrollViewer.HorizontalOffset - 500), null, null);
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        PosterScrollViewer.ChangeView(
            PosterScrollViewer.HorizontalOffset + 500, null, null);
    }

}
