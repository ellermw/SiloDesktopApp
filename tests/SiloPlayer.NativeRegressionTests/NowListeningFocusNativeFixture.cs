using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;

/// <summary>
/// Official Silo Server #4296 (385d90755): while the expanded audiobook player
/// (Now Listening) is open the covered shell is keyboard-inert, Tab and Shift+Tab stay
/// inside it, focus starts on Back, and collapsing returns focus to the mini
/// player's cover tile. The shell mirrors MainWindow.RootGrid's layering and
/// drives the production <see cref="NowListeningFocusScope"/> the same way
/// MainWindow.SyncNowListeningFocusScope does.
/// Selector: SILO_NATIVE_TEST_ONLY=media-parity, SILO_NATIVE_TEST_MEDIA_ACTION_CASE=now-listening-focus.
/// </summary>
internal static class NowListeningFocusNativeFixture
{
    private sealed record Shell(
        Grid Root, NavigationView Navigation, NavigationViewItem NavigationItem, ContentControl Frame, Button PageAction, TextBlock PageText,
        Button Activity, AudiobookNowListening NowListening, MiniPlayerBar Mini, Button Toast)
    {
        public Button Back => (Button)NowListening.FindName("CollapseButton");
        public Button Cover => (Button)Mini.FindName("AudiobookCoverButton");
        public UIElement[] CoveredTopLevel => [Navigation, Activity, Toast];
        public UIElement[] CoveredControls => [Navigation, NavigationItem, PageAction, Activity, Toast];
    }

    internal static async Task RunAsync(StackPanel parent)
    {
        var shell = Build();
        var scope = new NowListeningFocusScope();
        parent.Children.Add(shell.Root);
        try
        {
            await SettleAsync(shell);
            Check(shell.Mini.FocusNowListeningOpener() && ReferenceEquals(Focused(shell), shell.Cover),
                "the mini player's Open Now Listening cover tile takes focus before expanding");

            // Open, collapse and repeated expansion return focus to the cover tile every time.
            for (var round = 1; round <= 3; round++)
            {
                await OpenAsync(shell, scope);
                await AssertOpenAsync(shell, scope, round);
                Check(!scope.Open(shell.Root, shell.NowListening, Focused(shell), shell.NowListening.FocusCollapseButton),
                    $"round {round}: a repeated expand while open is a no-op");
                await SettleAsync(shell);
                Check(ReferenceEquals(Focused(shell), shell.Back), $"round {round}: a repeated expand keeps focus on Back");

                await CollapseAsync(shell, scope, stop: false);
                Check(ReferenceEquals(Focused(shell), shell.Cover), $"round {round}: collapse returns focus to the cover tile that opened Now Listening");
                AssertRestored(shell, scope, $"round {round} collapse");
                Program.Log($"TRACE: now-listening focus round {round}: Back on open, cover tile on collapse, shell restored.");
            }

            // A page action (e.g. the E accelerator), not the cover tile, can be the opener.
            // Force the newly shown cover's first attempt to fail to exercise layout retry ordering.
            Check(NowListeningFocusScope.TryFocus(shell.PageAction), "the page action takes focus before expanding");
            await OpenAsync(shell, scope);
            shell.NowListening.Visibility = Visibility.Collapsed;
            shell.Mini.Visibility = Visibility.Visible;
            var coverAttempts = 0;
            Check(scope.Close(shell.NowListening, returnFocus: true,
                preferred: () => ++coverAttempts > 1 && shell.Mini.FocusNowListeningOpener(), fallbackScope: shell.Frame),
                "collapse from a page opener ends the scope");
            Check(coverAttempts == 1, "the cover tile is attempted synchronously before layout");
            await SettleAsync(shell);
            Check(coverAttempts == 2 && ReferenceEquals(Focused(shell), shell.Cover),
                "the remembered page opener cannot preempt the cover tile's layout retry");
            AssertRestored(shell, scope, "page opener collapse");

            // Stop while expanded: no mini player remains, so focus falls back to the page.
            Check(shell.Mini.FocusNowListeningOpener(), "the cover tile takes focus before the stop case");
            await OpenAsync(shell, scope);
            await AssertOpenAsync(shell, scope, 4);
            await CollapseAsync(shell, scope, stop: true);
            AssertRestored(shell, scope, "stop");
            Check(ReferenceEquals(Focused(shell), shell.PageAction), "stop while expanded returns focus to the first control of the restored page");

            // A control that was already disabled by its owner stays disabled after collapse.
            shell.Activity.IsEnabled = false;
            shell.Mini.Visibility = Visibility.Visible; // playback resumes in the mini player
            await SettleAsync(shell);
            Check(shell.Mini.FocusNowListeningOpener(), "the cover tile takes focus before the owner-state case");
            await OpenAsync(shell, scope);
            await CollapseAsync(shell, scope, stop: false);
            Check(!shell.Activity.IsEnabled && shell.Navigation.IsEnabled, "collapse restores only what the scope changed");
            shell.Activity.IsEnabled = true;

            await AssertFocusEligibilityAsync(shell, scope);

            Program.Log("PASS: NOW_LISTENING_FOCUS_COMPLETED covered shell keyboard-inert (Raw siblings, disabled controls, Tab/Shift+Tab contained; page text remains in UIA content view as a recorded deviation), Back focused on open, cover tile focused on collapse including page openers, inactive focus skipped, stop and owner state restored.");
        }
        finally
        {
            scope.Close(shell.NowListening, returnFocus: false);
            parent.Children.Remove(shell.Root);
            await Task.Delay(100);
        }
    }

    private static Shell Build()
    {
        var root = new Grid { Width = 1280, Height = 720 };
        var pageAction = new Button { Content = "Page action" };
        AutomationProperties.SetName(pageAction, "Page action");
        var pageText = new TextBlock { Text = "Covered page heading" };
        AutomationProperties.SetName(pageText, "Covered page heading");
        var frame = new ContentControl { Content = new StackPanel { Children = { pageText, pageAction } } };
        var navigationItem = new NavigationViewItem { Content = "Home" };
        var navigation = new NavigationView { IsSettingsVisible = false, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed, Content = frame };
        navigation.MenuItems.Add(navigationItem);
        var activity = new Button { Content = "Server activity", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        Canvas.SetZIndex(activity, 40);
        var nowListening = new AudiobookNowListening
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Canvas.SetZIndex(nowListening, 55);
        var mini = new MiniPlayerBar { VerticalAlignment = VerticalAlignment.Bottom };
        Canvas.SetZIndex(mini, 60);
        ((FrameworkElement)mini.FindName("VideoBar")).Visibility = Visibility.Collapsed;
        ((FrameworkElement)mini.FindName("AudiobookBar")).Visibility = Visibility.Visible;
        // Toasts and the update button follow Now Listening in document order but sit beneath it (z0).
        var toast = new Button { Content = "Toast action", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        root.Children.Add(navigation);
        root.Children.Add(activity);
        root.Children.Add(nowListening);
        root.Children.Add(mini);
        root.Children.Add(toast);
        return new Shell(root, navigation, navigationItem, frame, pageAction, pageText, activity, nowListening, mini, toast);
    }

    private static async Task AssertFocusEligibilityAsync(Shell shell, NowListeningFocusScope scope)
    {
        // Simulate the shell's live activation predicate without activating another test window.
        foreach (var initiallyActive in new[] { false, true })
        {
            var active = initiallyActive;
            var attempts = 0;
            bool RequestFocus() { attempts++; return false; }
            shell.Mini.Visibility = Visibility.Collapsed;
            shell.NowListening.Visibility = Visibility.Visible;
            Check(scope.Open(shell.Root, shell.NowListening, shell.PageAction, RequestFocus, canMoveFocus: () => active),
                "focus eligibility does not prevent applying inertness");
            Check(scope.IsOpen && !shell.Navigation.IsEnabled, "inactive open still disables the covered shell");
            active = false; // also suppress any queued retry after deactivation
            await SettleAsync(shell);
            var expected = initiallyActive ? 1 : 0;
            Check(attempts == expected, "open skips inactive focus, including a retry after deactivation");

            shell.NowListening.Visibility = Visibility.Collapsed;
            shell.Mini.Visibility = Visibility.Visible;
            active = initiallyActive;
            Check(scope.Close(shell.NowListening, returnFocus: true, preferred: RequestFocus,
                fallbackScope: shell.Frame, canMoveFocus: () => active), "focus eligibility does not prevent restoration");
            active = false;
            await SettleAsync(shell);
            Check(attempts == expected * 2, "close skips inactive focus, including a retry after deactivation");
            AssertRestored(shell, scope, "inactive transition");
        }
    }

    // MainWindow.OnPlayerStateChanged(Expanded): capture the opener, collapse the mini player, show Now Listening, sync.
    private static async Task OpenAsync(Shell shell, NowListeningFocusScope scope)
    {
        var opener = Focused(shell);
        shell.Mini.Visibility = Visibility.Collapsed;
        shell.NowListening.Visibility = Visibility.Visible;
        Check(scope.Open(shell.Root, shell.NowListening, opener, shell.NowListening.FocusCollapseButton), "opening Now Listening starts the inert scope");
        await SettleAsync(shell);
    }

    // MainWindow.OnPlayerStateChanged(Minimized | Idle).
    private static async Task CollapseAsync(Shell shell, NowListeningFocusScope scope, bool stop)
    {
        shell.NowListening.Visibility = Visibility.Collapsed;
        shell.Mini.Visibility = stop ? Visibility.Collapsed : Visibility.Visible;
        Check(scope.Close(shell.NowListening, returnFocus: true, preferred: shell.Mini.FocusNowListeningOpener, fallbackScope: shell.Frame),
            "collapsing Now Listening ends the inert scope");
        await SettleAsync(shell);
    }

    private static async Task AssertOpenAsync(Shell shell, NowListeningFocusScope scope, int round)
    {
        Check(scope.IsOpen && scope.InertWrites == 6, $"round {round}: three covered siblings each get Raw view and disabled ({scope.InertWrites} writes)");
        Check(ReferenceEquals(Focused(shell), shell.Back), $"round {round}: opening Now Listening focuses Back (focused={Describe(Focused(shell))})");

        foreach (var element in shell.CoveredTopLevel)
            Check(AutomationProperties.GetAccessibilityView(element) == AccessibilityView.Raw
                  && Peer(element)?.IsControlElement() != true,
                $"round {round}: {Describe(element)} leaves the UIA control view while covered");
        // Recorded deviation from upstream inert: Raw is not inherited. Non-Control
        // page text is still a content element, so Narrator navigation is NOT contained.
        Check(AutomationProperties.GetAccessibilityView(shell.PageText) == AccessibilityView.Content
              && Peer(shell.PageText)?.IsContentElement() == true,
            $"round {round}: covered page text remains in the UIA content view (known inert parity gap)");
        foreach (var element in shell.CoveredControls)
        {
            var peer = Peer(element);
            // Observed in this WinUI version: a disabled control whose own AccessibilityView is Raw keeps
            // a peer reporting IsEnabled=true (NavigationView and the Server activity Button alike). Its
            // covered state is still proven by the disabled control, the Raw view (outside the UIA control
            // view) and a peer that is not keyboard focusable. Covered controls that are not themselves Raw
            // must report disabled through UIA.
            var peerDisabled = peer != null && (!peer.IsEnabled()
                || element is Control { IsEnabled: false } && AutomationProperties.GetAccessibilityView(element) == AccessibilityView.Raw);
            Check(peer != null && peerDisabled && !peer.IsKeyboardFocusable(),
                $"round {round}: UIA reports covered {Describe(element)} unavailable and not keyboard focusable " +
                $"(peer={peer?.GetType().Name ?? "null"}, peerEnabled={peer?.IsEnabled()}, peerFocusable={peer?.IsKeyboardFocusable()}, " +
                $"elementEnabled={(element as Control)?.IsEnabled}, view={AutomationProperties.GetAccessibilityView(element)})");
        }
        Check(AutomationProperties.GetAccessibilityView(shell.Mini) == AccessibilityView.Content && shell.Mini.IsEnabled,
            $"round {round}: the mini player above Now Listening is not touched");
        Check(!shell.PageAction.Focus(FocusState.Programmatic) && ReferenceEquals(Focused(shell), shell.Back),
            $"round {round}: covered page controls cannot take focus");

        await AssertTabContainedAsync(shell, FocusNavigationDirection.Next, round);
        await AssertTabContainedAsync(shell, FocusNavigationDirection.Previous, round);
        Check(NowListeningFocusScope.TryFocus(shell.Back), $"round {round}: Back takes focus again after the Tab walk");
        await SettleAsync(shell);
    }

    /// <summary>Tab (Next) and Shift+Tab (Previous) cycle within Now Listening and never reach the covered shell.</summary>
    private static async Task AssertTabContainedAsync(Shell shell, FocusNavigationDirection direction, int round)
    {
        Check(KeyboardFocus(shell.Back), $"round {round}: Back takes keyboard focus before the {direction} walk");
        var options = new FindNextElementOptions { SearchRoot = shell.Root.XamlRoot.Content };
        var visited = new List<DependencyObject>();
        var wrapped = false;
        for (var step = 0; step < 48 && !wrapped; step++)
        {
            var next = FocusManager.FindNextElement(direction, options);
            Check(next != null, $"round {round}: {direction} step {step} found a focus candidate");
            Check(IsWithin(next!, shell.NowListening), $"round {round}: {direction} step {step} escaped Now Listening to {Describe(next)}");
            Check(next is UIElement candidate && KeyboardFocus(candidate), $"round {round}: {direction} step {step} could focus {Describe(next)}");
            await Task.Delay(15);
            visited.Add(next!);
            wrapped = ReferenceEquals(next, shell.Back);
        }
        Check(wrapped && visited.Count > 2, $"round {round}: {direction} cycles back to Back inside Now Listening ({visited.Count} stops)");
        Program.Log($"TRACE: now-listening {direction} cycle round {round}: {visited.Count} stops, all inside Now Listening.");
    }

    private static void AssertRestored(Shell shell, NowListeningFocusScope scope, string label)
    {
        Check(!scope.IsOpen && scope.InertWrites == 0, $"{label}: the inert scope holds no writes");
        foreach (var element in shell.CoveredTopLevel)
            Check(AutomationProperties.GetAccessibilityView(element) == AccessibilityView.Content,
                $"{label}: {Describe(element)} returns to the UIA content view");
        Check(shell.Navigation.IsEnabled && shell.Activity.IsEnabled && shell.Toast.IsEnabled, $"{label}: covered controls are enabled again");
        foreach (var element in shell.CoveredControls)
            Check(Peer(element)?.IsEnabled() == true, $"{label}: UIA reports {Describe(element)} available again");
    }

    private static AutomationPeer? Peer(UIElement element) =>
        FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);

    private static DependencyObject? Focused(Shell shell) =>
        FocusManager.GetFocusedElement(shell.Root.XamlRoot) as DependencyObject;

    // Keyboard-state focus as a Tab press sets it; Focus() also returns false when it
    // only set logical focus (fixture window in the background), so read the state back.
    private static bool KeyboardFocus(UIElement element) =>
        element.Focus(FocusState.Keyboard) || element.FocusState != FocusState.Unfocused;

    private static bool IsWithin(DependencyObject element, UIElement root)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, root)) return true;
        return false;
    }

    private static async Task SettleAsync(Shell shell)
    {
        shell.Root.UpdateLayout();
        await Task.Delay(150);
        shell.Root.UpdateLayout();
    }

    private static string Describe(object? element) => element switch
    {
        null => "nothing",
        FrameworkElement { Name.Length: > 0 } named => $"{named.GetType().Name}#{named.Name}",
        ContentControl { Content: string text } content => $"{content.GetType().Name}('{text}')",
        _ => element!.GetType().Name,
    };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Now Listening focus: " + message);
    }
}
