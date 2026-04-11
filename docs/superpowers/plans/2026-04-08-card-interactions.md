# Card Interactions Parity — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Match the WebUI's card interaction behaviors: play overlay on hover for Continue Watching cards, click-image-to-play / click-text-for-detail split, 3-dot context menu on all cards with Watched/Favorite/Watchlist actions, and horizontal scroll support on section rows.

**Architecture:** Four independent changes to existing controls — LandscapeCard (play overlay + split click targets), PosterCard (context menu), SectionRow (enable scrolling), and a shared MenuFlyout helper. No new pages or services needed. All API methods already exist in CatalogApi.

**Tech Stack:** WinUI 3 XAML + C# code-behind, existing CatalogApi for watched/favorite/watchlist toggling.

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `Controls/LandscapeCard.xaml` | Modify | Add play overlay, split image/text into separate hit targets |
| `Controls/LandscapeCard.xaml.cs` | Modify | Play overlay hover, image-tap starts playback, text-tap goes to detail, heading logic change |
| `Controls/PosterCard.xaml` | Modify | Add 3-dot menu button overlay |
| `Controls/PosterCard.xaml.cs` | Modify | Context menu with Watched/Favorite/Watchlist actions |
| `Controls/SectionRow.xaml` | Modify | Enable horizontal scroll on both ScrollViewers |
| `Controls/SectionRow.xaml.cs` | Modify | Add mouse wheel forwarding for horizontal scroll |

---

### Task 1: LandscapeCard — Play Overlay on Hover + Image Scale

**Files:**
- Modify: `src/ContinuumPlayer/Controls/LandscapeCard.xaml`
- Modify: `src/ContinuumPlayer/Controls/LandscapeCard.xaml.cs`

**WebUI reference:** `ContinueWatchingCard.tsx:91-118` — image area has a play button circle that fades in on hover with dark overlay, image scales to 105%.

- [ ] **Step 1: Add play overlay elements to XAML**

In `LandscapeCard.xaml`, inside the `<Grid Grid.Row="0" CornerRadius="12">` block, after the `<!-- Remaining time badge -->` section (after the closing `</Border>` for RemainingBadge), add:

```xml
            <!-- Play overlay on hover (WebUI: centered play circle + dark bg, fades in) -->
            <Border
                x:Name="PlayOverlay"
                Background="#00000000"
                CornerRadius="12"
                Opacity="0"
                IsHitTestVisible="False">
                <Border
                    x:Name="PlayCircle"
                    Width="44" Height="44"
                    CornerRadius="22"
                    Background="{StaticResource AccentBrush}"
                    HorizontalAlignment="Center"
                    VerticalAlignment="Center">
                    <FontIcon
                        Glyph="&#xE768;"
                        FontSize="20"
                        Foreground="White"
                        HorizontalAlignment="Center"
                        VerticalAlignment="Center"
                        Margin="2,0,0,0" />
                </Border>
            </Border>
```

- [ ] **Step 2: Update hover handlers to show/hide play overlay**

In `LandscapeCard.xaml.cs`, replace the `OnPointerEntered` and `OnPointerExited` methods:

```csharp
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CardBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["SurfaceHoverBrush"];
        PlayOverlay.Opacity = 1;
        PlayOverlay.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(0x4D, 0, 0, 0)); // bg-black/30
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        CardBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["CardBackgroundBrush"];
        PlayOverlay.Opacity = 0;
        PlayOverlay.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Release -r win-x64`
Expected: 0 errors

- [ ] **Step 4: Commit**

```
feat: add play overlay on hover to LandscapeCard (CW/NextUp cards)
```

---

### Task 2: LandscapeCard — Split Click Targets (Image = Play, Text = Detail)

**Files:**
- Modify: `src/ContinuumPlayer/Controls/LandscapeCard.xaml`
- Modify: `src/ContinuumPlayer/Controls/LandscapeCard.xaml.cs`

**WebUI reference:** `ContinueWatchingCard.tsx:91,152` — image area links to `/watch/{id}` (playback), text area links to `/item/{id}` (detail page).

- [ ] **Step 1: Remove the card-level Tapped handler from XAML**

In `LandscapeCard.xaml`, on the root `<UserControl>` element, remove the `Tapped="OnCardTapped"` attribute. The line should become:

```xml
<UserControl
    x:Class="ContinuumPlayer.Controls.LandscapeCard"
    ...
    Width="280"
    PointerEntered="OnPointerEntered"
    PointerExited="OnPointerExited">
```

- [ ] **Step 2: Add separate Tapped handlers to image area and text area**

In `LandscapeCard.xaml`, add `Tapped="OnImageTapped"` to the image area Grid:

```xml
        <Grid Grid.Row="0" CornerRadius="12" Tapped="OnImageTapped">
```

And add `Tapped="OnTextTapped"` to the text StackPanel:

```xml
        <StackPanel Grid.Row="1" Margin="4,8,4,0" Spacing="2" Tapped="OnTextTapped">
```

- [ ] **Step 3: Replace OnCardTapped with OnImageTapped and OnTextTapped**

In `LandscapeCard.xaml.cs`, replace the `OnCardTapped` method with two new methods:

```csharp
    private void OnImageTapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null) return;

        // Image tap = start playback (WebUI: links to /watch/{id})
        var playerService = App.Services.GetRequiredService<PlayerService>();
        _ = playerService.PlayAsync(MediaItem.ContentId);
    }

    private void OnTextTapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null) return;

        // Text tap = navigate to detail page (WebUI: links to /item/{id})
        var nav = App.Services.GetRequiredService<NavigationService>();
        var targetId = !string.IsNullOrEmpty(MediaItem.SeriesId)
            ? MediaItem.SeriesId
            : MediaItem.ContentId;
        nav.Navigate<ItemDetailPage>(targetId);
    }
```

Add the required using at top if not present:

```csharp
using ContinuumPlayer.Services;
```

- [ ] **Step 4: Update heading logic to match WebUI**

In `LandscapeCard.xaml.cs`, in the `UpdateContent` method, replace the title assignment and subtitle logic:

```csharp
        // WebUI: heading = series title for episodes, item title otherwise
        bool hasEpisodeMeta = item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue;
        TitleText.Text = hasEpisodeMeta && !string.IsNullOrEmpty(item.SeriesTitle)
            ? item.SeriesTitle
            : item.Title;

        // WebUI: subtitle = "Season X Episode Y . Episode Title"
        if (hasEpisodeMeta)
        {
            var episodeLabel = $"Season {item.SeasonNumber} Episode {item.EpisodeNumber}";
            var episodeMeta = !string.IsNullOrEmpty(item.SeriesTitle) && !string.IsNullOrEmpty(item.Title)
                ? $"{episodeLabel} \u2022 {item.Title}"
                : episodeLabel;
            SubtitleText.Text = episodeMeta;
            SubtitleText.Visibility = Visibility.Visible;
        }
        else
        {
            SubtitleText.Visibility = Visibility.Collapsed;
        }
```

Also add "Next Episode" label for next_up items. After the progress bar section, replace the remaining time badge logic with:

```csharp
        // "Next Episode" label for next_up, "X min left" for continue_watching
        bool isNextUp = item.ItemSource == "next_up";
        if (isNextUp)
        {
            RemainingText.Text = "Next Episode";
            RemainingBadge.Visibility = Visibility.Visible;
            // Hide progress bar for next_up items
            ProgressContainer.Visibility = Visibility.Collapsed;
        }
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Release -r win-x64`
Expected: 0 errors

- [ ] **Step 6: Commit**

```
feat: split LandscapeCard click targets — image plays, text goes to detail
```

---

### Task 3: PosterCard — 3-Dot Context Menu

**Files:**
- Modify: `src/ContinuumPlayer/Controls/PosterCard.xaml`
- Modify: `src/ContinuumPlayer/Controls/PosterCard.xaml.cs`

**WebUI reference:** `MediaItemMenu.tsx:52-113,167-171` — 3-dot button appears on hover, dropdown with Mark Watched/Unwatched, Add/Remove Favorite, Add/Remove Watchlist.

- [ ] **Step 1: Add menu button overlay to XAML**

In `PosterCard.xaml`, inside the poster image area Grid (the `<Grid Grid.Row="0" CornerRadius="{StaticResource PosterCornerRadius}">` block), after the `BadgesPanel` StackPanel closing tag and before the closing `</Grid>`, add:

```xml
            <!-- 3-dot context menu (WebUI: MediaItemMenu, appears on hover) -->
            <Button
                x:Name="MenuButton"
                Width="32" Height="32"
                CornerRadius="6"
                Padding="0"
                HorizontalAlignment="Right"
                VerticalAlignment="Bottom"
                Margin="0,0,6,6"
                Opacity="0"
                Background="#99000000"
                BorderBrush="#26FFFFFF"
                BorderThickness="1">
                <FontIcon Glyph="&#xE712;" FontSize="14" Foreground="White" />
                <Button.Flyout>
                    <MenuFlyout x:Name="CardMenuFlyout" />
                </Button.Flyout>
            </Button>
```

- [ ] **Step 2: Show/hide menu button on hover and build menu items**

In `PosterCard.xaml.cs`, update the hover handlers to show/hide the menu button:

```csharp
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        PosterBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["SurfaceHoverBrush"];
        MenuButton.Opacity = 1;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        PosterBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["CardBackgroundBrush"];
        // Only hide if flyout isn't open
        if (!CardMenuFlyout.IsOpen)
            MenuButton.Opacity = 0;
    }
```

Add a method to build menu items dynamically when the flyout opens. Add this subscription in the constructor after `InitializeComponent()`:

```csharp
        CardMenuFlyout.Opening += OnMenuOpening;
        CardMenuFlyout.Closed += (_, _) =>
        {
            // Hide button when flyout closes if pointer isn't over card
            MenuButton.Opacity = 0;
        };
```

Add the menu building method:

```csharp
    private void OnMenuOpening(object? sender, object e)
    {
        if (MediaItem == null) return;

        CardMenuFlyout.Items.Clear();
        var state = MediaItem.UserState;

        if (state != null)
        {
            var watchedItem = new MenuFlyoutItem
            {
                Text = state.Played ? "Mark Unwatched" : "Mark Watched",
                Icon = new FontIcon { Glyph = state.Played ? "\uE73A" : "\uE73E" }
            };
            watchedItem.Click += async (_, _) =>
            {
                var api = App.Services.GetRequiredService<CatalogApi>();
                if (state.Played)
                    await api.MarkUnwatchedAsync(MediaItem.ContentId);
                else
                    await api.MarkWatchedAsync(MediaItem.ContentId);
                state.Played = !state.Played;
            };
            CardMenuFlyout.Items.Add(watchedItem);

            var favItem = new MenuFlyoutItem
            {
                Text = state.IsFavorite ? "Remove from Favorites" : "Add to Favorites",
                Icon = new FontIcon { Glyph = state.IsFavorite ? "\uE735" : "\uE734" }
            };
            favItem.Click += async (_, _) =>
            {
                var api = App.Services.GetRequiredService<CatalogApi>();
                if (state.IsFavorite)
                    await api.RemoveFavoriteAsync(MediaItem.ContentId);
                else
                    await api.AddFavoriteAsync(MediaItem.ContentId);
                state.IsFavorite = !state.IsFavorite;
            };
            CardMenuFlyout.Items.Add(favItem);

            var watchlistItem = new MenuFlyoutItem
            {
                Text = state.InWatchlist ? "Remove from Watchlist" : "Add to Watchlist",
                Icon = new FontIcon { Glyph = state.InWatchlist ? "\uE74D" : "\uE710" }
            };
            watchlistItem.Click += async (_, _) =>
            {
                var api = App.Services.GetRequiredService<CatalogApi>();
                if (state.InWatchlist)
                    await api.RemoveFromWatchlistAsync(MediaItem.ContentId);
                else
                    await api.AddToWatchlistAsync(MediaItem.ContentId);
                state.InWatchlist = !state.InWatchlist;
            };
            CardMenuFlyout.Items.Add(watchlistItem);
        }
    }
```

Add the required using:

```csharp
using ContinuumPlayer.Core.Api;
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Release -r win-x64`
Expected: 0 errors

- [ ] **Step 4: Commit**

```
feat: add 3-dot context menu to PosterCard with watched/favorite/watchlist
```

---

### Task 4: SectionRow — Enable Horizontal Scroll

**Files:**
- Modify: `src/ContinuumPlayer/Controls/SectionRow.xaml`
- Modify: `src/ContinuumPlayer/Controls/SectionRow.xaml.cs`

**WebUI reference:** `MediaCarousel.tsx` (Embla carousel) — supports drag scroll, mouse wheel horizontal scroll, smooth snapping.

- [ ] **Step 1: Enable scroll mode on both ScrollViewers in XAML**

In `SectionRow.xaml`, change `HorizontalScrollMode="Disabled"` to `"Enabled"` on BOTH ScrollViewers. The PosterScrollViewer (line ~56):

```xml
            HorizontalScrollMode="Enabled"
```

And the LandscapeScrollViewer (line ~78):

```xml
            HorizontalScrollMode="Enabled"
```

- [ ] **Step 2: Add mouse wheel horizontal scroll forwarding**

In `SectionRow.xaml.cs`, add a handler that converts vertical mouse wheel to horizontal scroll. Add this in the constructor after `InitializeComponent()`:

```csharp
        PosterScrollViewer.PointerWheelChanged += OnHorizontalWheelScroll;
        LandscapeScrollViewer.PointerWheelChanged += OnHorizontalWheelScroll;
```

Add the handler method:

```csharp
    private void OnHorizontalWheelScroll(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        var delta = e.GetCurrentPoint(sv).Properties.MouseWheelDelta;
        if (delta == 0) return;

        double direction = delta > 0 ? -1 : 1;
        double target = sv.HorizontalOffset + (direction * 300);
        target = Math.Clamp(target, 0, sv.ScrollableWidth);
        sv.ChangeView(target, null, null, false);
        e.Handled = true;
    }
```

- [ ] **Step 3: Update section title font size to match WebUI**

In `SectionRow.xaml`, change the section title FontSize from 14 to 20:

```xml
                FontSize="20"
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Release -r win-x64`
Expected: 0 errors

- [ ] **Step 5: Commit**

```
feat: enable horizontal scroll on section rows, match WebUI title size
```

---

### Task 5: Final Build + Publish

- [ ] **Step 1: Full publish**

```bash
cd F:/ContinuumPlayer
find src/ContinuumPlayer/obj -name "*.xbf" -delete 2>/dev/null
find src/ContinuumPlayer/obj -name "*.pri" -delete 2>/dev/null
find src/ContinuumPlayer/obj -name "*.Up2Date" -delete 2>/dev/null
rm -rf publish
dotnet publish src/ContinuumPlayer/ContinuumPlayer.csproj -c Release -r win-x64 --self-contained
mkdir -p publish
cp -r src/ContinuumPlayer/bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/* publish/
```

- [ ] **Step 2: Verify launch**

```bash
F:/ContinuumPlayer/publish/ContinuumPlayer.exe
```

- [ ] **Step 3: Commit all changes**

```
feat: card interactions parity — play overlay, split click, context menu, scroll
```
