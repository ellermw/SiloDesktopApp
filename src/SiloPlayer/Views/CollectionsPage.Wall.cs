using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

/// <summary>
/// The Collections page's one poster wall (pinned WebUI <c>Collections.tsx</c>): a Show pill row, one
/// full-width poster grid of the viewer's own collections, the ones other profiles share and the server's,
/// and an empty line only once what the view shows has actually loaded.
/// </summary>
public sealed partial class CollectionsPage
{
    // What the viewer asked to see; the shown filter resolves against what the account actually has.
    private string? _requestedShow;
    private string _show = CollectionsWall.All;
    private int? _autoLoadedLibrary;

    private double PageWidth => ActualWidth > 0 ? ActualWidth : double.IsNaN(Width) ? 1000 : Width;
    private double Gutter => PageWidth < 640 ? 16 : PageWidth < 1024 ? 24 : 40;
    private double ContentWidth => Math.Max(0, PageWidth - Gutter * 2);
    private string PosterSize => App.Services.GetService<UICustomizationService>()?.CardPresentation.PosterSize ?? "standard";
    /// <summary>The viewer's poster-size setting decides the columns, as it does for every other poster grid.</summary>
    private int PosterColumnCount => UICustomizationService.PosterColumnCount(PosterSize, ContentWidth);
    private double PosterGap => PosterSize == "large" ? 16 : 12;
    private double PosterWidth => Math.Max(60, (ContentWidth - PosterGap * (PosterColumnCount - 1)) / PosterColumnCount);

    private void BuildWall()
    {
        EmptyState.Visibility = AddGroupButton.Visibility = AddGroupEditor.Visibility = Visibility.Collapsed;
        var profileId = App.Services.GetRequiredService<AuthService>().SelectedProfileId;
        var partition = PersonalCollectionOwnership.Split(ViewModel.Collections, profileId, ViewModel.Profiles.ToArray());
        // Only a successfully read one-profile account gets the short subtitle. Names never hide cards.
        var multiple = !ViewModel.ProfilesLoaded || ViewModel.Profiles.Count != 1 || partition.Shared.Count > 0;
        CollectionsSubtitle.Text = multiple ? "Yours, the ones other profiles share with you, and the server's." : "Yours and the server's.";
        var shared = partition.Shared.SelectMany(group => group.Collections.Select(collection => (Collection: collection, Owner: group.Name))).ToArray();
        var libraries = ViewModel.ServerLibraries.ToArray();

        var loading = new CollectionsWall.Loading(ViewModel.IsLoading, ViewModel.IsLoadingProfiles, ViewModel.IsLoadingServerCollections);
        _show = CollectionsWall.Resolve(_requestedShow, libraries, shared.Length, loading);
        var picked = CollectionsWall.Picked(_show, libraries);
        var showMine = CollectionsWall.ShowsMine(_show);
        var showShared = CollectionsWall.ShowsShared(_show, shared.Length);
        var showServer = CollectionsWall.ShowsServer(_show);
        var personalLoading = (showMine || _show == CollectionsWall.Shared) && ViewModel.IsLoading;
        var serverLoading = showServer && ViewModel.IsLoadingServerCollections;

        BuildFilterPills(libraries, partition.Own.Count, shared.Length);
        var empty = CollectionsWall.EmptyMessage(_show, ViewModel.PersonalLoaded, ViewModel.ProfilesLoaded, ViewModel.ServerLoaded,
            partition.Own.Count, shared.Length, picked?.TotalCount ?? CollectionsWall.ServerCount(libraries));
        CollectionsEmptyLine.Text = empty ?? "";
        CollectionsEmptyLine.Visibility = empty == null ? Visibility.Collapsed : Visibility.Visible;
        // The personal cards' place while they load; the server's don't wait for them.
        CollectionsLoadingShell.Visibility = personalLoading ? Visibility.Visible : Visibility.Collapsed;
        ServerCollectionsLoadingRows.Visibility = serverLoading && !personalLoading ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(CollectionsWallPanel, CollectionsWall.Label(_show, picked));

        var cards = new List<FrameworkElement>();
        if (!personalLoading)
        {
            if (showMine) cards.AddRange(partition.Own.Select(BuildCollectionCard));
            if (showShared) cards.AddRange(shared.Select(entry => (FrameworkElement)BuildSharedCollectionCard(entry.Collection, entry.Owner)));
        }
        if (showServer)
            cards.AddRange(ViewModel.GetServerCollectionCards(picked?.LibraryId).Select(card => (FrameworkElement)BuildServerCollectionCard(card.Library, card.Collection)));
        CollectionsGrid.Children.Clear();
        CollectionsGrid.Children.Add(BuildPosterBoard(cards));
        BuildWallStatus(showServer ? (picked == null ? libraries : [picked]) : [], picked);

        CollectionsReorderHint.Visibility = _show == CollectionsWall.Mine && ViewModel.Capabilities?.ItemReorder == true && partition.Own.Count > 1
            ? Visibility.Visible : Visibility.Collapsed;
        // A library asked for by navigation loads its rest straight away, as a pill press does.
        if (picked != null && _autoLoadedLibrary != picked.LibraryId && !ViewModel.IsServerLibraryLoading(picked.LibraryId)
            && !ViewModel.HasServerLibraryRest(picked.LibraryId) && ViewModel.ServerLibraryError(picked.LibraryId) == null)
        {
            _autoLoadedLibrary = picked.LibraryId;
            _ = ViewModel.LoadServerLibraryAsync(picked.LibraryId);
        }
    }

    private void BuildFilterPills(IReadOnlyList<ServerCollectionsLibrary> libraries, int ownCount, int sharedCount)
    {
        CollectionsFilterPills.Children.Clear();
        CollectionsFilterScroll.Visibility = CollectionsWall.ShowPills(libraries, sharedCount) ? Visibility.Visible : Visibility.Collapsed;
        if (CollectionsFilterScroll.Visibility == Visibility.Collapsed) return;
        foreach (var pill in CollectionsWall.Pills(libraries, ownCount, sharedCount))
        {
            if (pill.Separated)
                CollectionsFilterPills.Children.Add(new Border { Width = 1, Height = 16, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Background = (Brush)Application.Current.Resources["BorderBrush"] });
            var active = pill.Value == _show;
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            label.Children.Add(new TextBlock { Text = pill.Label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            if (pill.Count is { } count) label.Children.Add(new TextBlock { Text = count.ToString(), FontSize = 11, Opacity = .7, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = label, Height = 32, MinHeight = 0, Padding = new Thickness(12, 0, 12, 0), FontSize = 13,
                Background = (Brush)Application.Current.Resources[active ? "PrimaryTextBrush" : "SurfaceBrush"],
                Foreground = (Brush)Application.Current.Resources[active ? "AppBackgroundBrush" : "SecondaryTextBrush"],
            };
            RoundedBadge.SetRadius(button, 999);
            AutomationProperties.SetName(button, pill.Label);
            AutomationProperties.SetItemStatus(button, active ? "Selected" : "");
            var value = pill.Value;
            button.Click += (_, _) => ChangeShow(value);
            CollectionsFilterPills.Children.Add(button);
        }
    }

    private void ChangeShow(string value)
    {
        _requestedShow = value;
        _autoLoadedLibrary = null;
        // A picked library loads its rest straight away: BuildWall starts that read once.
        BuildWall();
    }

    /// <summary>
    /// Under the wall: a library's rest on its way, a failed list with Retry and a link to that library's
    /// Collections tab (the first 20 stay), and the control that loads more of the server's collections.
    /// </summary>
    private void BuildWallStatus(IReadOnlyList<ServerCollectionsLibrary> shown, ServerCollectionsLibrary? picked)
    {
        ServerCollectionsRows.Children.Clear();
        foreach (var library in shown)
        {
            if (ViewModel.IsServerLibraryLoading(library.LibraryId))
            {
                var progress = new TextBlock { Text = $"Loading {library.LibraryName} collections…", FontSize = 13, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
                AutomationProperties.SetLiveSetting(progress, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
                ServerCollectionsRows.Children.Add(progress);
            }
            if (ViewModel.ServerLibraryError(library.LibraryId) is { } error)
            {
                var message = new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                AutomationProperties.SetLiveSetting(message, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
                ServerCollectionsRows.Children.Add(message);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                actions.Children.Add(LoadButton(library, "Retry " + library.LibraryName));
                var seeAll = new Button { Content = $"See all {library.TotalCount} in {library.LibraryName}", Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
                AutomationProperties.SetName(seeAll, $"See all {library.TotalCount} {library.LibraryName} collections");
                seeAll.Click += (_, _) => NavigateToLibraryCollections(library);
                actions.Children.Add(seeAll);
                ServerCollectionsRows.Children.Add(actions);
            }
        }
        if (picked == null && ViewModel.NextServerLibrary is { } next && shown.Count > 0) ServerCollectionsRows.Children.Add(LoadButton(next, "Load more server collections"));
        else if (picked != null && picked.TotalCount > picked.Collections.Count && !ViewModel.HasServerLibraryRest(picked.LibraryId) && ViewModel.ServerLibraryError(picked.LibraryId) == null)
            ServerCollectionsRows.Children.Add(LoadButton(picked, "Load all " + picked.LibraryName + " collections"));
    }

    private Button LoadButton(ServerCollectionsLibrary library, string label)
    {
        var loading = ViewModel.IsServerLibraryLoading(library.LibraryId);
        var more = new Button { Content = loading ? "Loading…" : label, IsEnabled = !loading, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(more, label);
        more.Click += async (_, _) =>
        {
            var before = CollectionsGrid.Children.OfType<Grid>().FirstOrDefault()?.Children.Count ?? 0;
            await ViewModel.LoadServerLibraryAsync(library.LibraryId);
            // The rebuild replaces the pressed button. Keep keyboard focus in the wall: on the first card
            // the press added, or on the failure's own controls when nothing arrived.
            BuildWall();
            var board = CollectionsGrid.Children.OfType<Grid>().FirstOrDefault();
            var target = board != null && board.Children.Count > before ? DescendantButtons(board.Children[before]).FirstOrDefault() : DescendantButtons(ServerCollectionsRows).FirstOrDefault();
            target?.Focus(FocusState.Programmatic);
        };
        return more;
    }
}
