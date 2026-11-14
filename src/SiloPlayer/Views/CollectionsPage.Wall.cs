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
        var focused = XamlRoot == null ? null : Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as Button;
        var focusedName = focused != null && DescendantButtons(CollectionsGrid).Contains(focused) ? AutomationProperties.GetName(focused) : null;
        EmptyState.Visibility = Visibility.Collapsed;
        MarkWallLocale();
        var profileId = App.Services.GetRequiredService<AuthService>().SelectedProfileId;
        var partition = PersonalCollectionOwnership.Split(ViewModel.Collections, profileId, ViewModel.Profiles.ToArray());
        // Only a successfully read one-profile account gets the short subtitle. Names never hide cards.
        var multiple = !ViewModel.ProfilesLoaded || ViewModel.Profiles.Count != 1 || partition.Shared.Count > 0;
        CollectionsSubtitle.Text = multiple
            ? CollectionText.Get("web.collections.page.subtitleShared", "Yours, the ones other profiles share with you, and the server's.")
            : CollectionText.Get("web.collections.page.subtitle", "Yours and the server's.");
        var shared = partition.Shared.SelectMany(group => group.Collections.Select(collection => (Collection: collection, Owner: group.Name))).ToArray();
        var libraries = ViewModel.ServerLibraries.ToArray();
        var personalIds = partition.Own.Select(collection => collection.Id).Concat(shared.Select(entry => entry.Collection.Id))
            .ToHashSet(StringComparer.Ordinal);

        var loading = new CollectionsWall.Loading(ViewModel.IsLoading, ViewModel.IsLoadingProfiles, ViewModel.IsLoadingServerCollections);
        _show = CollectionsWall.Resolve(_requestedShow, libraries, shared.Length, loading);
        var picked = CollectionsWall.Picked(_show, libraries);
        var showMine = CollectionsWall.ShowsMine(_show);
        var showShared = CollectionsWall.ShowsShared(_show, shared.Length);
        var showServer = CollectionsWall.ShowsServer(_show);
        var personalLoading = (showMine || _show == CollectionsWall.Shared) && ViewModel.IsLoading;
        var serverLoading = showServer && ViewModel.IsLoadingServerCollections;
        var canAddGroup = showMine && ViewModel.PersonalLoaded && !string.IsNullOrEmpty(profileId);
        if (!canAddGroup) AddGroupEditor.Visibility = Visibility.Collapsed;
        AddGroupButton.Visibility = canAddGroup && AddGroupEditor.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

        BuildFilterPills(libraries, partition.Own.Count, shared.Length, personalIds);
        var empty = CollectionsWall.EmptyMessage(_show, ViewModel.PersonalLoaded, ViewModel.ProfilesLoaded, ViewModel.ServerLoaded,
            partition.Own.Count, shared.Length, picked?.TotalCount ?? CollectionsWall.ServerCount(libraries));
        CollectionsEmptyLine.Text = empty == null ? "" : EmptyText(empty);
        CollectionsEmptyLine.Visibility = empty == null ? Visibility.Collapsed : Visibility.Visible;
        // The personal cards' place while they load; the server's don't wait for them.
        CollectionsLoadingShell.Visibility = personalLoading ? Visibility.Visible : Visibility.Collapsed;
        ServerCollectionsLoadingRows.Visibility = serverLoading && !personalLoading ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(CollectionsWallPanel, WallLabel(_show, picked));

        CollectionsGrid.Children.Clear();
        var cards = new List<FrameworkElement>();
        if (!personalLoading)
        {
            if (showMine && ViewModel.Groups.Count > 0)
            {
                // Keep existing personal groups editable, using the same responsive posters as the wall.
                foreach (var group in ViewModel.Groups.OrderBy(group => group.SortOrder))
                    CollectionsGrid.Children.Add(BuildCollectionGroupSection(group, group.Name,
                        partition.Own.Where(collection => collection.GroupId == group.Id).OrderBy(collection => collection.SortOrder).ToArray()));
                var ungrouped = partition.Own.Where(collection => !ViewModel.Groups.Any(group => group.Id == collection.GroupId))
                    .OrderBy(collection => collection.SortOrder).ToArray();
                // Desktop-only personal groups have no catalog messages; "Ungrouped" stays English.
                CollectionsGrid.Children.Add(BuildCollectionGroupSection(null, "Ungrouped", ungrouped));
            }
            else if (showMine) cards.AddRange(partition.Own.Select(BuildCollectionCard));
            if (showShared) cards.AddRange(shared.Select(entry => (FrameworkElement)BuildSharedCollectionCard(entry.Collection, entry.Owner)));
        }
        if (showServer)
            cards.AddRange(ViewModel.GetServerCollectionCards(picked?.LibraryId)
                // In All, keep the personal card (including its owner actions) instead of a second server card.
                // While personal cards load, the server cards remain independently available.
                .Where(card => _show != CollectionsWall.All || personalLoading || !personalIds.Contains(card.Collection.Id))
                .Select(card => (FrameworkElement)BuildServerCollectionCard(card.Library, card.Collection)));
        CollectionsGrid.Children.Add(BuildPosterBoard(cards));
        BuildWallStatus(showServer ? (picked == null ? libraries : [picked]) : [], picked, showServer);

        CollectionsReorderHint.Visibility = _show == CollectionsWall.Mine && ViewModel.Capabilities?.ItemReorder == true && partition.Own.Count > 1
            ? Visibility.Visible : Visibility.Collapsed;
        if (focusedName != null) DescendantButtons(CollectionsGrid).FirstOrDefault(button => AutomationProperties.GetName(button) == focusedName)?.Focus(FocusState.Programmatic);
        UpdateWallLoadAction();
        QueueWallAdmission();
    }

    private void BuildFilterPills(IReadOnlyList<ServerCollectionsLibrary> libraries, int ownCount, int sharedCount, IReadOnlyCollection<string> personalIds)
    {
        CollectionsFilterPills.Children.Clear();
        CollectionsFilterScroll.Visibility = CollectionsWall.ShowPills(libraries, sharedCount) ? Visibility.Visible : Visibility.Collapsed;
        if (CollectionsFilterScroll.Visibility == Visibility.Collapsed) return;
        // A slow or failed personal or server read leaves All without a count rather than a partial one.
        foreach (var pill in CollectionsWall.Pills(libraries, ownCount, sharedCount, personalIds, ViewModel.ServerLibraryRestIds,
            ViewModel.PersonalLoaded, ViewModel.ServerLoaded))
        {
            if (pill.Separated)
                CollectionsFilterPills.Children.Add(new Border { Width = 1, Height = 16, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Background = (Brush)Application.Current.Resources["BorderBrush"] });
            var active = pill.Value == _show;
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var pillLabel = PillLabel(pill);
            label.Children.Add(new TextBlock { Text = pillLabel, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            if (pill.Count is { } count) label.Children.Add(new TextBlock { Text = CollectionText.Number(count), FontSize = 11, Opacity = .7, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = label, Height = 32, MinHeight = 0, Padding = new Thickness(12, 0, 12, 0), FontSize = 13,
                Background = (Brush)Application.Current.Resources[active ? "PrimaryTextBrush" : "SurfaceBrush"],
                Foreground = (Brush)Application.Current.Resources[active ? "AppBackgroundBrush" : "SecondaryTextBrush"],
            };
            RoundedBadge.SetRadius(button, 999);
            AutomationProperties.SetName(button, pillLabel);
            AutomationProperties.SetItemStatus(button, active ? "Selected" : "");
            var value = pill.Value;
            button.Click += (_, _) => ChangeShow(value);
            CollectionsFilterPills.Children.Add(button);
        }
    }

    private void ChangeShow(string value)
    {
        ResetWallAdmission();
        _requestedShow = value;
        _autoLoadedLibrary = null;
        // A picked library loads its rest straight away: BuildWall starts that read once.
        BuildWall();
    }

    /// <summary>
    /// Under the wall: a library's rest on its way, a failed list with Retry and a link to that library's
    /// Collections tab (the first 20 stay), and the control that loads more of the server's collections.
    /// </summary>
    private void BuildWallStatus(IReadOnlyList<ServerCollectionsLibrary> shown, ServerCollectionsLibrary? picked, bool showServer)
    {
        ServerCollectionsRows.Children.Clear();
        if (showServer && ViewModel.ServerCollectionsErrorMessage is { } serverError)
        {
            var message = new TextBlock { Text = serverError, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = (Brush)Application.Current.Resources["ErrorBrush"] };
            AutomationProperties.SetLiveSetting(message, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            ServerCollectionsRows.Children.Add(message);
            var retry = new Button { Content = "Retry server collections", Command = ViewModel.LoadCollectionsCommand, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(retry, "Retry server collections");
            ServerCollectionsRows.Children.Add(retry);
        }
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
                var seeAll = new Button { Content = CollectionText.Get("web.collections.page.seeAllIn", $"See all {library.TotalCount} in {library.LibraryName}", ("count", CollectionText.Number(library.TotalCount)), ("library", library.LibraryName)), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
                AutomationProperties.SetName(seeAll, $"See all {library.TotalCount} {library.LibraryName} collections");
                seeAll.Click += (_, _) => NavigateToLibraryCollections(library);
                actions.Children.Add(seeAll);
                ServerCollectionsRows.Children.Add(actions);
            }
        }
        if (picked != null && picked.TotalCount > picked.Collections.Count && !ViewModel.HasServerLibraryRest(picked.LibraryId) && ViewModel.ServerLibraryError(picked.LibraryId) == null)
            ServerCollectionsRows.Children.Add(LoadButton(picked, "Load all " + picked.LibraryName + " collections"));
    }

    private Button LoadButton(ServerCollectionsLibrary library, string label)
    {
        var loading = ViewModel.IsServerLibraryLoading(library.LibraryId);
        var more = new Button { Content = loading ? CollectionText.Get("web.settings.sessions.loadingMore", "Loading…") : label, IsEnabled = !loading, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(more, label);
        more.Click += async (_, _) =>
        {
            await LoadWallLibraryAsync(library.LibraryId, true);
        };
        return more;
    }
}
