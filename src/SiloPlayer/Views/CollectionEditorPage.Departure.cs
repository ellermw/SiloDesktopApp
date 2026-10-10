using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Animation;
using SiloPlayer.Helpers;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private bool _confirmingDeparture;
    private bool _allowDeparture;

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        if (_allowDeparture || !_editorActive || ViewModel.IsReadOnly || _draftBaseline == null) return;
        if (ViewModel.IsSaving) { e.Cancel = true; return; }
        if (!CaptureDraft().Any(pair => _draftBaseline.GetValueOrDefault(pair.Key) != pair.Value)) return;
        e.Cancel = true;
        if (_confirmingDeparture || XamlRoot == null || Frame == null) return;
        var frame = Frame;
        var mode = e.NavigationMode;
        var target = e.SourcePageType;
        var parameter = e.Parameter;
        _confirmingDeparture = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var dialog = CreateDepartureDialog(XamlRoot);
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || !ReferenceEquals(frame.Content, this) || ViewModel.IsSaving) return;
                _allowDeparture = true;
                if (mode == NavigationMode.Back && frame.CanGoBack) frame.GoBack(new SuppressNavigationTransitionInfo());
                else if (mode == NavigationMode.Forward && frame.CanGoForward) frame.GoForward();
                else if (mode == NavigationMode.New) frame.Navigate(target, parameter, new SuppressNavigationTransitionInfo());
            }
            finally { _allowDeparture = false; _confirmingDeparture = false; }
        });
    }
    private static ContentDialog CreateDepartureDialog(XamlRoot root)
    {
        var title = new TextBlock { Text = "Discard unsaved changes?", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var content = new TextBlock { Text = "This page has edits that were never saved. Leaving now throws them away.", FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
        var dialog = new ContentDialog { XamlRoot = root, Title = title, Content = content, PrimaryButtonText = "Discard", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"];
        EditorDialogPresentation.Configure(dialog, 512, new Thickness(24));
        void Reflow()
        {
            var width = Math.Min(512, Math.Max(180, root.Size.Width - 32)); title.Width = content.Width = width - 50;
            EditorDialogPresentation.ReflowCommands(dialog, root.Size.Width < 640, new Thickness(24), buttonHeight: 36, stackNarrow: true);
            foreach (var primary in EditorDialogPresentation.Descendants<Button>(dialog).Where(button => button.Name == "PrimaryButton"))
            {
                primary.Style = (Style)Application.Current.Resources["DestructiveButtonStyle"];
                primary.Background = (Brush)Application.Current.Resources["ErrorBrush"];
                primary.Foreground = (Brush)Application.Current.Resources["DestructiveForegroundBrush"];
            }
        }
        void Changed(XamlRoot sender, XamlRootChangedEventArgs args) => Reflow();
        // Opened can precede the native command tree. The content Loaded event
        // owns the realized template; resize uses the same bounded arrangement.
        content.Loaded += (_, _) => Reflow();
        dialog.Opened += (_, _) => { Reflow(); root.Changed += Changed; };
        dialog.Closed += (_, _) => root.Changed -= Changed;
        return dialog;
    }
}
