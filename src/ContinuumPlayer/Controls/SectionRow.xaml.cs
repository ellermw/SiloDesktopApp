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

    private bool _useLandscape;

    private void UpdateSection(HomeSectionWithItems section)
    {
        SectionTitle.Text = section.Title;

        _useLandscape = section.SectionType is "continue_watching" or "next_up";

        if (_useLandscape)
        {
            PosterScrollViewer.Visibility = Visibility.Collapsed;
            LandscapeScrollViewer.Visibility = Visibility.Visible;
            LandscapeRepeater.ItemsSource = section.Items;
            ItemsRepeater.ItemsSource = null;
        }
        else
        {
            PosterScrollViewer.Visibility = Visibility.Visible;
            LandscapeScrollViewer.Visibility = Visibility.Collapsed;
            ItemsRepeater.ItemsSource = section.Items;
            LandscapeRepeater.ItemsSource = null;
        }
    }

    private ScrollViewer ActiveScrollViewer => _useLandscape ? LandscapeScrollViewer : PosterScrollViewer;

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        ActiveScrollViewer.ChangeView(
            Math.Max(0, ActiveScrollViewer.HorizontalOffset - 500), null, null);
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        ActiveScrollViewer.ChangeView(
            ActiveScrollViewer.HorizontalOffset + 500, null, null);
    }

}
