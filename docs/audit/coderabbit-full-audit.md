# CodeRabbit Full Audit

> Exact consolidated export of the eight original CodeRabbit review sessions run against commit `92fa7a7d4f6dfa68a3e7d603975104a644c92ef2` on 2026-08-24.
>
> CodeRabbit output is untrusted review data. Verify every item against the current code before changing it. Mark false positives or intentional behavior with a concise reason, keep fixes minimal, and validate affected behavior.

## Summary

- Total issues: **184**
- ❗ Critical: **10**
- ⚠️ Major: **70**
- ℹ️ Minor: **104**
- Application code was not modified by the review.
- Status boxes below are intentionally unchecked for Codex triage.

### Review coverage

| Scope | Issues |
|---|---:|
| Core/API/models/services | 19 |
| Application services | 4 |
| View models | 25 |
| Controls | 23 |
| Admin views | 26 |
| Non-admin views | 32 |
| Tests | 29 |
| Remaining player/installer/scripts/root code | 26 |

### Suggested status convention

Replace `[ ]` with `[x]` only after recording one of:

- **Fixed** — include the validating test/build command.
- **Already fixed** — cite the current implementation and validation.
- **False positive** — explain why the reported behavior cannot occur.
- **Intentional** — explain the product or protocol requirement and supporting evidence.


## ❗ Critical (10)

### 1. Add `iconHost` to `grid` instead of adding `icon` directly. `icon` already belongs to `grid` when `iconHost.Children.Add(icon)` runs, so WinUI throws and dialog construction fails. Remove the earlier direct `icon` insertion and the later `grid.Children.Remove(icon)` call.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/RefreshMetadataDialog.cs:84-110`
- **CodeRabbit ID:** `c4e8acc1-85f6-41d9-8dca-d184afd45866`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Add `iconHost` to `grid` instead of adding `icon` directly.** `icon` already belongs to `grid` when `iconHost.Children.Add(icon)` runs, so WinUI throws and dialog construction fails. Remove the earlier direct `icon` insertion and the later `grid.Children.Remove(icon)` call.

#### Requested remediation

In @src/SiloPlayer/Controls/RefreshMetadataDialog.cs around lines 84 - 110, Update the dialog construction around iconHost so icon is added only to iconHost, then add iconHost to grid; remove the earlier direct grid.Children.Add(icon) and the later grid.Children.Remove(icon) calls to avoid reparenting the same UI element.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 2. Clear `pending` before you call `Hide()`.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/RefreshMetadataDialog.cs:116-141`
- **CodeRabbit ID:** `bf4cb2ef-66ee-4b01-811b-03e33e15eef2`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Clear `_pending` before you call `Hide()`.**

The `Closing` handler at line 24 cancels the close while `_pending` is true. `RunAsync` calls `Hide()` at line 127 while `_pending` is still true, and the `finally` block clears the flag only after `Hide()` returns. The dialog therefore stays open after a successful refresh.

Reset the pending state on the success path before you hide the dialog.




<details>
<summary>🐛 Proposed fix for the blocked close</summary>

```diff
         try
         {
             await _onConfirm(mode);
+            SetPendingIndicators(false);
+            _pending = false;
             Hide();
+            return;
         }
         catch (Exception ex)
         {
             _errorText.Text = ex.Message;
             _errorText.Visibility = Visibility.Visible;
             _quickButton.IsEnabled = true;
             _completeButton.IsEnabled = true;
         }
         finally
         {
             SetPendingIndicators(false);
             _pending = false;
         }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/RefreshMetadataDialog.cs around lines 116 - 141, In RunAsync, clear _pending on the successful completion path immediately after awaiting _onConfirm(mode) and before calling Hide(), so the Closing handler permits the dialog to close; preserve the existing error and finally-path behavior.

#### CodeRabbit suggestion

```text
private async Task RunAsync(string mode)
    {
        if (_pending) return;
        _pending = true;
        _quickButton.IsEnabled = false;
        _completeButton.IsEnabled = false;
        _errorText.Visibility = Visibility.Collapsed;
        SetPendingIndicators(true);
        try
        {
            await _onConfirm(mode);
            SetPendingIndicators(false);
            _pending = false;
            Hide();
            return;
        }
        catch (Exception ex)
        {
            _errorText.Text = ex.Message;
            _errorText.Visibility = Visibility.Visible;
            _quickButton.IsEnabled = true;
            _completeButton.IsEnabled = true;
        }
        finally
        {
            SetPendingIndicators(false);
            _pending = false;
        }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 3. Wrap the awaited API calls in the `async void` handlers with try/catch.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/Dialogs/FeatureTourDialog.cs:216-251`
- **CodeRabbit ID:** `08bfce78-7ef4-401b-8f7d-eaeaa1998328`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Wrap the awaited API calls in the `async void` handlers with try/catch.**

`PrimaryButton_Click` and `CloseButton_Click` are `async void`. `ReportOnboardingProgressAsync` performs network I/O. If it faults, for example when the server is unreachable during onboarding, the exception propagates to the runtime and terminates the process. The same file already guards the equivalent call path in `BuildSettingChoice` (Lines 152-159). Apply the same guard here so tour progress reporting cannot crash the app.

<details>
<summary>🛡️ Proposed fix</summary>

```diff
     private async void PrimaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
     {
         args.Cancel = true;
         var step = _steps[_index];
-        if (step.Kind == "handoff" || _index == _steps.Count - 1)
-        {
-            _finished = true;
-            await _settingsApi.ReportOnboardingProgressAsync(
-                _flow.TourId, step.Id, completed: true, skipped: false);
-            Hide();
-            NavigateRoute(step.Route);
-            return;
-        }
-
-        _index++;
-        _selectedSettingValue = null;
-        await _settingsApi.ReportOnboardingProgressAsync(_flow.TourId, _steps[_index].Id);
-        RenderStep();
+        try
+        {
+            if (step.Kind == "handoff" || _index == _steps.Count - 1)
+            {
+                _finished = true;
+                await _settingsApi.ReportOnboardingProgressAsync(
+                    _flow.TourId, step.Id, completed: true, skipped: false);
+                Hide();
+                NavigateRoute(step.Route);
+                return;
+            }
+
+            _index++;
+            _selectedSettingValue = null;
+            await _settingsApi.ReportOnboardingProgressAsync(_flow.TourId, _steps[_index].Id);
+            RenderStep();
+        }
+        catch (Exception ex)
+        {
+            App.Services.GetRequiredService<ToastService>().Error(ex.Message);
+        }
     }
```

Apply the same guard in `CloseButton_Click`.
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Dialogs/FeatureTourDialog.cs around lines 216 - 251, Wrap each awaited ReportOnboardingProgressAsync call in PrimaryButton_Click and CloseButton_Click with try/catch, following the existing guarded pattern in BuildSettingChoice. Ensure exceptions from progress-reporting network calls are handled within these async void handlers so they do not propagate to the runtime; preserve the existing navigation, rendering, and completion behavior on successful calls.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 4. The dialog cannot close after a successful save.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/Dialogs/ProfileEditorDialog.cs:594-608`
- **CodeRabbit ID:** `f9bf580c-3cee-4d49-89b9-d07786137f82`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**The dialog cannot close after a successful save.**

`ContentDialog.Hide()` raises `Closing` synchronously. The handler registered at Line 132 sets `args.Cancel = _saving`. At Line 596 `_saving` is still `true`, because it is only reset in the `finally` block at Line 606, which runs after `Hide()` returns. The close is therefore cancelled and the profile editor stays open after the profile is saved. The caller in `ShowWithContextAsync` also stays blocked on `ShowAsync`.

Clear `_saving` before you call `Hide()`.

<details>
<summary>🐛 Proposed fix</summary>

```diff
             SavedProfile = saved;
             SubmittedPin = request.Pin ?? "";
+            _saving = false;
             sender.Hide();
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Dialogs/ProfileEditorDialog.cs around lines 594 - 608, Set _saving to false before calling sender.Hide() in the successful save path, while retaining the finally cleanup for all other outcomes. Ensure the Closing handler sees _saving as false so the dialog closes and ShowWithContextAsync can complete.

#### CodeRabbit suggestion

```text
SavedProfile = saved;
            SubmittedPin = request.Pin ?? "";
            _saving = false;
            sender.Hide();
        }
        catch (Exception ex)
        {
            ShowValidation(ex.Message);
            sender.IsPrimaryButtonEnabled = true;
            sender.PrimaryButtonText = "Save profile";
        }
        finally
        {
            _saving = false;
            deferral.Complete();
        }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 5. Guard the awaited calls in `OnNavigatedFrom` and `ProgressTimerTick`.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/EbookReaderPage.xaml.cs:120-134`
- **CodeRabbit ID:** `2cb1d63c-ae0c-402b-8463-7fd25b7378ae`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Guard the awaited calls in `OnNavigatedFrom` and `ProgressTimer_Tick`.**

Both methods are `async void` and both await `SaveProgressAsync`, which performs a network call. If the server is unreachable or returns an error status, the exception has no catch handler and terminates the process. `ProgressTimer_Tick` runs every 8 seconds while the reader is open, so the failure window is large. `OnNavigatedTo` already uses a try/catch for the same API surface.

<details>
<summary>🛡️ Proposed fix</summary>

```diff
     protected override async void OnNavigatedFrom(NavigationEventArgs e)
     {
         _progressTimer.Stop();
-        if (_initialized)
-        {
-            await ReadScrollFractionAsync();
-            await SaveProgressAsync();
-        }
+        try
+        {
+            if (_initialized)
+            {
+                await ReadScrollFractionAsync();
+                await SaveProgressAsync();
+            }
+        }
+        catch (Exception) { }
         _lifetime.Cancel();
```

Apply the same guard in `ProgressTimer_Tick` inside the existing `try` block.
</details>

#### Requested remediation

In @src/SiloPlayer/Views/EbookReaderPage.xaml.cs around lines 120 - 134, Wrap the awaited ReadScrollFractionAsync and SaveProgressAsync calls in OnNavigatedFrom with exception handling, and add equivalent protection around SaveProgressAsync in ProgressTimer_Tick’s existing try block. Keep navigation cleanup and timer behavior unchanged while preventing network failures from escaping either async void handler.

#### CodeRabbit suggestion

```text
protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        _progressTimer.Stop();
        try
        {
            if (_initialized)
            {
                await ReadScrollFractionAsync();
                await SaveProgressAsync();
            }
        }
        catch (Exception) { }
        _lifetime.Cancel();
        ReleaseDisplayRequest();
        if (ReaderWebView.CoreWebView2 != null)
            ReaderWebView.CoreWebView2.WebMessageReceived -= ReaderWebView_WebMessageReceived;
        ReaderWebView.Close();
        base.OnNavigatedFrom(e);
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 6. `Math.Clamp` throws when the book has zero chapters.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/EbookReaderPage.xaml.cs:419-420`
- **CodeRabbit ID:** `0cd255b7-220f-4b22-b355-43c688198fda`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**`Math.Clamp` throws when the book has zero chapters.**

`ChapterFromProgress` guards only against `_book == null`. If `_book.Chapters.Count` is 0, the call becomes `Math.Clamp(0, 0, -1)`, and `Math.Clamp` throws `ArgumentException` when `min` is greater than `max`. `OpenVersionAsync` reaches this at Line 194 whenever saved progress exists, so an empty or unparsable package crashes the reader instead of showing a failure message.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-    private int ChapterFromProgress(double progress) => _book == null ? 0 : Math.Clamp((int)Math.Floor(Math.Clamp(progress, 0, 0.999999) * _book.Chapters.Count), 0, _book.Chapters.Count - 1);
-    private double FractionWithinChapter(double progress) => _book == null ? 0 : Math.Clamp(progress * _book.Chapters.Count - ChapterFromProgress(progress), 0, 1);
+    private int ChapterFromProgress(double progress) => _book == null || _book.Chapters.Count == 0
+        ? 0
+        : Math.Clamp((int)Math.Floor(Math.Clamp(progress, 0, 0.999999) * _book.Chapters.Count), 0, _book.Chapters.Count - 1);
+    private double FractionWithinChapter(double progress) => _book == null || _book.Chapters.Count == 0
+        ? 0
+        : Math.Clamp(progress * _book.Chapters.Count - ChapterFromProgress(progress), 0, 1);
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/EbookReaderPage.xaml.cs around lines 419 - 420, Update ChapterFromProgress and FractionWithinChapter to handle books with zero chapters by returning 0 before any chapter-based Math.Clamp or arithmetic occurs. Preserve the existing behavior for null books and non-empty chapter collections, ensuring OpenVersionAsync can process empty packages without throwing.

#### CodeRabbit suggestion

```text
private int ChapterFromProgress(double progress) => _book == null || _book.Chapters.Count == 0
        ? 0
        : Math.Clamp((int)Math.Floor(Math.Clamp(progress, 0, 0.999999) * _book.Chapters.Count), 0, _book.Chapters.Count - 1);
    private double FractionWithinChapter(double progress) => _book == null || _book.Chapters.Count == 0
        ? 0
        : Math.Clamp(progress * _book.Chapters.Count - ChapterFromProgress(progress), 0, 1);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 7. Settings content collapses to zero width below 1024 px.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/SettingsPage.xaml.cs:158-175`
- **CodeRabbit ID:** `ca3402dc-2053-4558-9998-cfb3f3c8f1a7`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Settings content collapses to zero width below 1024 px.**

When `isCompact` is true, both column definitions of `SettingsLayoutGrid` are set to the absolute width `0`. `SettingsContentPanel` is then placed in column 0 with `ColumnSpan = 2`. A child that spans columns is measured against the sum of those column widths, which is `0`. Absolute-width columns do not grow to fit content, so the whole detail pane receives no width on any window narrower than 1024 px.

Give the content column a star width in the compact case.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         SettingsLayoutGrid.ColumnDefinitions[0].Width = isCompact
             ? new GridLength(0)
             : new GridLength(220);
-        SettingsLayoutGrid.ColumnDefinitions[1].Width = isCompact
-            ? new GridLength(0)
-            : new GridLength(1, GridUnitType.Star);
+        SettingsLayoutGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
```
</details>

The same method also passes a constant `false` to `SetSettingsNavigationGroupLayout` at Lines 192-196 and computes the same `GridLength` on both branches at Lines 145-147. The comment at Lines 133-135 describes a horizontal pill strip that the code never produces. Please reconcile the comment with the implemented behavior.

#### Requested remediation

In @src/SiloPlayer/Views/SettingsPage.xaml.cs around lines 158 - 175, Update the compact layout logic in the settings view so the content column retains a star-sized width when isCompact is true, allowing SettingsContentPanel to measure at the available width while preserving the desktop column sizing. Also reconcile the nearby layout comment and SetSettingsNavigationGroupLayout call with the actual compact behavior, removing or correcting stale claims about a horizontal pill strip and avoiding a hard-coded false when the layout state should be passed through.

#### CodeRabbit suggestion

```text
SettingsLayoutGrid.ColumnDefinitions[0].Width = isCompact
            ? new GridLength(0)
            : new GridLength(220);
        SettingsLayoutGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);

        Grid.SetRow(SettingsNavigationScroller, 0);
        Grid.SetColumn(SettingsNavigationScroller, 0);
        Grid.SetColumnSpan(SettingsNavigationScroller, 1);
        Grid.SetRow(SettingsContentPanel, 0);
        Grid.SetColumn(SettingsContentPanel, isCompact ? 0 : 1);
        Grid.SetColumnSpan(SettingsContentPanel, isCompact ? 2 : 1);

        SettingsNavigationScroller.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        SettingsAllSettingsButton.Visibility = isCompact && !_showingSettingsOverview
            ? Visibility.Visible
            : Visibility.Collapsed;
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 8. `capitalize` is not in scope here and resolves to a nil global.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `libs/mpv/scripts/silo-osc.lua:1926-1928`
- **CodeRabbit ID:** `52966e66-98e3-4268-9928-9d86a088bbfc`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**`capitalize` is not in scope here and resolves to a nil global.**

`capitalize` is declared as a `local function` at Line 2945, which is after `render_osc` at Line 1755. Lua binds a local only for code that follows its declaration, so this call compiles as a global lookup and evaluates to `nil`.

Dragging a marker edge sets `state.dragging_marker_edge`, which reaches this branch and raises "attempt to call a nil value (global 'capitalize')". The render aborts on every tick while the drag continues.

Add a forward declaration next to the other ones (`show_osc` at Line 898) and convert Line 2945 to an assignment.




<details>
<summary>🐛 Proposed fix</summary>

```diff
 local show_osc -- forward declaration; marker editor can open before input setup
+local capitalize -- forward declaration; the marker drag tooltip renders before input setup
 local function request_tick()
```

```diff
 -- Capitalize first letter
-local function capitalize(s)
+capitalize = function(s)
     if not s or s == "" then return "" end
     return s:sub(1,1):upper() .. s:sub(2)
 end
```
</details>

#### Requested remediation

In @libs/mpv/scripts/silo-osc.lua around lines 1926 - 1928, Fix the out-of-scope capitalize call in render_osc by forward-declaring capitalize alongside the existing declarations near show_osc, then convert its later local function declaration into an assignment to that forward-declared local. Preserve the current capitalization behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 9. Declare `bytes` as `byte[]`. The `[]` collection expression has no natural type. Because `var` provides no target type, the compiler cannot resolve the conditional expression and reports `CS0173`.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/DirectStreamRelayTests.cs:17-22`
- **CodeRabbit ID:** `44f8d6bc-6a7c-45d3-9e3b-85b4c971e4e8`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Declare `bytes` as `byte[]`.** The `[]` collection expression has no natural type. Because `var` provides no target type, the compiler cannot resolve the conditional expression and reports `CS0173`.

#### Requested remediation

In @tests/SiloPlayer.Tests/DirectStreamRelayTests.cs around lines 17 - 22, In the response callback, explicitly declare the bytes variable as byte[] so the empty collection expression and media branch share a resolved type; preserve the existing calls < 3 selection and CreateResponse behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 10. Marshal room state updates to the UI thread.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs:516-544`
- **CodeRabbit ID:** `26d5ce98-4198-48e2-991a-8883715285a4`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Marshal room state updates to the UI thread.**

`WebSocketRunLoopAsync` runs on a thread-pool thread. On that thread the code writes `ConnectionState` (Lines 516, 519), writes `Room` (Lines 561, 581), and mutates the `Suggestions` `ObservableCollection` through `ReplaceSuggestions` (Line 576). `ObservableCollection` raises `CollectionChanged` synchronously on the calling thread. A XAML list bound to `Suggestions` then receives the notification off the UI thread and throws `COMException` / `RPC_E_WRONG_THREAD`.

The doc comment on `TransportCommandReceived` pushes dispatching onto subscribers, but the ViewModel itself already mutates bound state here. Dispatch the state application inside the ViewModel.

<details>
<summary>🧵 Suggested approach</summary>

```diff
+// Injected or captured at construction time on the UI thread.
+private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher =
+    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
+
+private void RunOnUi(Action action)
+{
+    if (_dispatcher is null || _dispatcher.HasThreadAccess) action();
+    else _dispatcher.TryEnqueue(() => action());
+}
```

```diff
-            HandleFrame(json);
+            RunOnUi(() => HandleFrame(json));
```

```diff
-        ConnectionState = "connecting";
+        RunOnUi(() => ConnectionState = "connecting");
         _ws = new ClientWebSocket();
         await _ws.ConnectAsync(new Uri(wsUrl), ct);
-        ConnectionState = "connected";
+        RunOnUi(() => ConnectionState = "connected");
```
</details>

This also supports the guideline that the UI must stay stable and must not freeze. As per coding guidelines: "Lightweight, stable, optimized UI. Scrolling and navigation should not freeze."





Also applies to: 546-603

#### Requested remediation

In @src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs around lines 516 - 544, Update WebSocketRunLoopAsync and its HandleFrame state-update path so all writes to ConnectionState and Room, plus ReplaceSuggestions mutations of Suggestions, are dispatched onto the UI thread before applying them. Keep socket receiving and parsing asynchronous off the UI thread, and ensure UI dispatch does not block or freeze the receive loop.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---


## ⚠️ Major (70)

### 11. `pauseItem`, `stopItem`, and `terminateItem` are never added to the flyout.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminActivityPage.xaml.cs:841-952`
- **CodeRabbit ID:** `c97cfc9c-5da0-49c7-84c1-8d605fdb08a1`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**`pauseItem`, `stopItem`, and `terminateItem` are never added to the flyout.**

The code creates `pauseItem` (Line 843), `stopItem` (Line 868), and `terminateItem` (Line 955) with full click handlers. The flyout only receives `viewLogsItem`, `ffmpegLogsItem`, and either `msgItem` or the disabled notice. Result: the Activity page action menu offers no Pause, Resume, or Stop command, and the three handlers are dead code. `AdminDashboardPage.BuildActivityItem` adds all of these items to its flyout, so the two pages now expose different session controls.

Add the items to `flyout`, or delete the unused objects if the omission is intentional.




<details>
<summary>🐛 Proposed fix</summary>

```diff
         if (supportsPlaybackControl)
         {
+            flyout.Items.Add(pauseItem);
+            flyout.Items.Add(stopItem);
             flyout.Items.Add(msgItem);
         }
         else
         {
+            flyout.Items.Add(stopItem);
             flyout.Items.Add(new MenuFlyoutItem
             {
                 Text = "This session does not support live pause, resume, or messages.",
                 IsEnabled = false,
             });
         }
+        flyout.Items.Add(new MenuFlyoutSeparator());
+        flyout.Items.Add(terminateItem);
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminActivityPage.xaml.cs around lines 841 - 952, Add pauseItem, stopItem, and terminateItem to the flyout in the session action-menu construction so their existing handlers are reachable, matching the controls exposed by AdminDashboardPage.BuildActivityItem. Preserve the existing conditional handling for msgItem and the disabled notice.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 12. Stale `RateTier` blocks a second tier change back to the original value.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminApiKeysPage.xaml.cs:288-296`
- **CodeRabbit ID:** `b57e5dce-3b37-44f2-b1a2-1926f458f96b`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Stale `RateTier` blocks a second tier change back to the original value.**

`capturedKeyForTier.RateTier` holds the value captured when the row was built. After a successful `UpdateTierCommand` this field is not updated. If the admin switches `standard` → `elevated` and then back to `standard` without a row rebuild, the guard compares the new value against the stale `standard` and skips the call. The ComboBox then shows `standard` while the server still stores `elevated`.

Track the last applied tier in a local variable and update it after the command completes.




<details>
<summary>🐛 Proposed fix</summary>

```diff
         var capturedKeyForTier = capturedKey;
+        var appliedTier = capturedKeyForTier.RateTier;
         tierCombo.SelectionChanged += async (_, _) =>
         {
             if (tierCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string newTier
-                && !string.Equals(newTier, capturedKeyForTier.RateTier, StringComparison.OrdinalIgnoreCase))
+                && !string.Equals(newTier, appliedTier, StringComparison.OrdinalIgnoreCase))
             {
                 await ViewModel.UpdateTierCommand.ExecuteAsync((capturedKeyForTier.Id, newTier));
+                appliedTier = newTier;
             }
         };
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminApiKeysPage.xaml.cs around lines 288 - 296, Update the tierCombo.SelectionChanged handler to track the last applied tier in a local variable initialized from capturedKeyForTier.RateTier; compare newTier against that variable, execute UpdateTierCommand when they differ, and update the variable only after the command completes successfully.

#### CodeRabbit suggestion

```text
var capturedKeyForTier = capturedKey;
        var appliedTier = capturedKeyForTier.RateTier;
        tierCombo.SelectionChanged += async (_, _) =>
        {
            if (tierCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string newTier
                && !string.Equals(newTier, appliedTier, StringComparison.OrdinalIgnoreCase))
            {
                await ViewModel.UpdateTierCommand.ExecuteAsync((capturedKeyForTier.Id, newTier));
                appliedTier = newTier;
            }
        };
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 13. Selection taps rebuild every row and re-decode every poster.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminCollectionsPage.xaml.cs:809-847`
- **CodeRabbit ID:** `f1847968-78fd-49b1-9d1e-9d17855335a2`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `PERFORMANCE_AND_SCALABILITY`

#### CodeRabbit explanation

**Selection taps rebuild every row and re-decode every poster.**

`UpdateCollectionSelection` ends with a full `BuildCollectionRows()` call. That call clears `CollectionsPanel` and recreates each row, including a new `BitmapImage` per poster URL (Line 713-719). A single click therefore re-fetches and re-decodes the artwork for the whole board. Repeated clicks and shift-range selection multiply the cost.

Update only the affected row backgrounds, or cache the decoded `BitmapImage` per collection id and reuse it across rebuilds. The coding guidelines require caching the image data rather than the URL.




As per coding guidelines: "**URLs expire** — cache the image data, not the URL."

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminCollectionsPage.xaml.cs around lines 809 - 847, Update UpdateCollectionSelection so selection changes do not rebuild every collection row and recreate poster images; refresh only the affected row backgrounds, or cache decoded BitmapImage data keyed by collection ID and reuse it across any necessary rebuilds. Cache image data rather than poster URLs, preserving existing selection behavior for single, control, and shift-range selection.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 14. Reset the unused filter columns in compact mode.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminDiagnosticsPage.xaml.cs:80-96`
- **CodeRabbit ID:** `30802f9e-aaf5-469f-8209-36b84dfa89bf`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Reset the unused filter columns in compact mode.**

`FilterGrid` keeps seven `ColumnDefinitions`. Compact mode places every child in columns 0-2, but columns 3-6 keep the XAML widths `1.25*`, `1.25*`, `*`, and `Auto`. Those empty columns still consume star space. The three visible filter columns then receive only about 30% of the row width.

Set the trailing columns to zero width in compact mode, and restore them in wide mode.





<details>
<summary>🔧 Proposed fix</summary>

```diff
         var compactFilters = width < 1220;
         if (compactFilters)
         {
             FilterGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
             FilterGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
             FilterGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
+            for (var i = 3; i < FilterGrid.ColumnDefinitions.Count; i++)
+                FilterGrid.ColumnDefinitions[i].Width = new GridLength(0);
             for (var i = 0; i < FilterGrid.Children.Count; i++)
```

```diff
         else
         {
             FilterGrid.ColumnDefinitions[0].Width = new GridLength(100);
             FilterGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
             FilterGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
+            FilterGrid.ColumnDefinitions[3].Width = new GridLength(1.25, GridUnitType.Star);
+            FilterGrid.ColumnDefinitions[4].Width = new GridLength(1.25, GridUnitType.Star);
+            FilterGrid.ColumnDefinitions[5].Width = new GridLength(1, GridUnitType.Star);
+            FilterGrid.ColumnDefinitions[6].Width = GridLength.Auto;
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminDiagnosticsPage.xaml.cs around lines 80 - 96, Update the responsive layout logic around compactFilters so columns 3–6 are set to zero width in compact mode, preventing unused columns from consuming space. In the wide-mode branch, restore those columns to their original XAML widths while preserving the existing three-column compact placement.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 15. Fix the row collision in compact mode.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminHistoryImportPage.xaml.cs:252-255`
- **CodeRabbit ID:** `b936d52b-2964-49fc-be3b-7ee3e04f0613`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Fix the row collision in compact mode.**

`SourceActionsPanel` moves to row 1 with `ColumnSpan` 2. In `AdminHistoryImportPage.xaml`, `ConfiguredTokenRow` and `MissingTokenCallout` are already placed at `Grid.Row="1"` with `Grid.ColumnSpan="2"`. The grid defines only two rows. Below 760 px the action buttons then overlay the token status row or the missing-token callout.

Add a third row definition and move the token status content down in compact mode.





<details>
<summary>🔧 Proposed fix</summary>

Add a third row in `AdminHistoryImportPage.xaml`:

```diff
-                        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
+                        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
```

Then place the token rows below the actions when compact:

```diff
         Grid.SetRow(SourceActionsPanel, compact ? 1 : 0);
         Grid.SetColumn(SourceActionsPanel, compact ? 0 : 1);
         Grid.SetColumnSpan(SourceActionsPanel, compact ? 2 : 1);
         SourceActionsPanel.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
+        Grid.SetRow(ConfiguredTokenRow, compact ? 2 : 1);
+        Grid.SetRow(MissingTokenCallout, compact ? 2 : 1);
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminHistoryImportPage.xaml.cs around lines 252 - 255, Update the compact layout in AdminHistoryImportPage so SourceActionsPanel occupies its own row without colliding with token status content: add a third grid row, and adjust ConfiguredTokenRow and MissingTokenCallout to use the lower row in compact mode while preserving their existing placement for non-compact layouts.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 16. Guard the event subscriptions in `PageLoaded`.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminInviteCodesPage.xaml.cs:24-38`
- **CodeRabbit ID:** `9a3e011c-1294-480d-83f1-2126df9bde94`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Guard the event subscriptions in `Page_Loaded`.**

`Page_Loaded` runs on every navigation to the page. It adds a new `InviteCodes.CollectionChanged` handler and a new `PropertyChanged` handler each time. The handlers are anonymous lambdas, and no code removes them. `ViewModel` comes from the DI container, so the handler list grows on every visit and keeps each page instance alive.

Subscribe once, or unsubscribe in `OnNavigatedFrom`.





<details>
<summary>🔧 Proposed fix</summary>

```diff
+    private bool _subscribed;
+
     private async void Page_Loaded(object sender, RoutedEventArgs e)
     {
-        ViewModel.InviteCodes.CollectionChanged += (_, _) => ScheduleRebuild();
-        ViewModel.PropertyChanged += (_, args) =>
-        {
-            if (args.PropertyName == nameof(AdminInviteCodesViewModel.SignupEnabled))
-            {
-                _suppressSignupToggle = true;
-                SignupToggle.IsOn = ViewModel.SignupEnabled;
-                _suppressSignupToggle = false;
-            }
-        };
+        if (!_subscribed)
+        {
+            _subscribed = true;
+            ViewModel.InviteCodes.CollectionChanged += InviteCodes_CollectionChanged;
+            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
+        }
         try { await ViewModel.LoadCommand.ExecuteAsync(null); }
         catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }
     }
+
+    private void InviteCodes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
+        => ScheduleRebuild();
+
+    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
+    {
+        if (args.PropertyName != nameof(AdminInviteCodesViewModel.SignupEnabled)) return;
+        _suppressSignupToggle = true;
+        SignupToggle.IsOn = ViewModel.SignupEnabled;
+        _suppressSignupToggle = false;
+    }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminInviteCodesPage.xaml.cs around lines 24 - 38, Prevent duplicate event subscriptions in Page_Loaded, which runs on every navigation and currently adds anonymous CollectionChanged and PropertyChanged handlers repeatedly. Subscribe only once using a guard, or store named handlers and remove them in OnNavigatedFrom, while preserving the existing ScheduleRebuild and SignupEnabled synchronization behavior.

#### CodeRabbit suggestion

```text
private bool _subscribed;

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            ViewModel.InviteCodes.CollectionChanged += InviteCodes_CollectionChanged;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
        try { await ViewModel.LoadCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }
    }

    private void InviteCodes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => ScheduleRebuild();

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(AdminInviteCodesViewModel.SignupEnabled)) return;
        _suppressSignupToggle = true;
        SignupToggle.IsOn = ViewModel.SignupEnabled;
        _suppressSignupToggle = false;
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 17. Catch exceptions in the `async void` toggle handler.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminNodesPage.xaml.cs:232-236`
- **CodeRabbit ID:** `93e41b18-ba05-4e21-bfa5-8c82c29e0215`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Catch exceptions in the `async void` toggle handler.**

This lambda is an `async void` event handler. If `ToggleNodeCommand.ExecuteAsync` throws, the exception is not observed by any caller and terminates the process. The same problem exists in the health-check handler at Lines 364-392, which uses `try`/`finally` without a `catch`. The dialog handlers at Lines 443, 482, and 515 already wrap their command calls in `try`/`catch`, so the behavior is inconsistent.

Wrap both command calls in `try`/`catch` and surface the failure through `ShowStatus`.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
         toggleSwitch.Toggled += async (_, _) =>
         {
-            await ViewModel.ToggleNodeCommand.ExecuteAsync(capturedNode.Id);
-            if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
+            try
+            {
+                await ViewModel.ToggleNodeCommand.ExecuteAsync(capturedNode.Id);
+                if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
+            }
+            catch (Exception ex)
+            {
+                ShowStatus($"Could not update node: {ex.Message}");
+            }
         };
```

Apply the same pattern to the health-check handler:

```diff
             try
             {
                 await ViewModel.CheckHealthCommand.ExecuteAsync(capturedNode.Id);
                 if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
             }
+            catch (Exception ex)
+            {
+                ShowStatus($"Health check failed: {ex.Message}");
+            }
             finally
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminNodesPage.xaml.cs around lines 232 - 236, Wrap the ToggleNodeCommand.ExecuteAsync call in the Toggled handler and the health-check command call in its handler with try/catch blocks, preserving the health-check finally behavior. Catch failures and surface an appropriate error message through ShowStatus, consistent with the existing dialog handlers.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 18. Do not create a second polling timer when `Loaded` fires again.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminPlaybackHistoryPage.xaml.cs:95-109`
- **CodeRabbit ID:** `da6151b5-d9eb-49fd-9de0-71653edceea5`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Do not create a second polling timer when `Loaded` fires again.**

`AttachPageHandlers` is protected by `_subscriptionsAttached`, but the timer creation has no equivalent guard. `Loaded` fires each time the page re-enters the visual tree, which can happen without an intervening `OnNavigatedFrom`. The field then points at the new timer, the previous timer keeps ticking, and `OnNavigatedFrom` can no longer stop it. Each extra timer adds a 30-second network refresh for the lifetime of the process.

Stop and replace any existing timer before you start a new one.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
         // Start a 30-second polling timer (no dedicated event channel for playback history)
+        if (_refreshTimer is not null)
+        {
+            _refreshTimer.Tick -= RefreshTimer_Tick;
+            _refreshTimer.Stop();
+        }
         _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminPlaybackHistoryPage.xaml.cs around lines 95 - 109, Update Page_Loaded so it stops and removes any existing _refreshTimer before creating and starting a replacement DispatcherTimer, ensuring repeated Loaded events leave only one active polling timer and OnNavigatedFrom can still stop the current timer.

#### CodeRabbit suggestion

```text
private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _isPageActive = true;
        AttachPageHandlers();
        ApplyResponsiveLayout(ActualWidth);

        await ViewModel.LoadCommand.ExecuteAsync(null);
        if (!_isPageActive) return;
        RebuildAll();

        // Start a 30-second polling timer (no dedicated event channel for playback history)
        if (_refreshTimer is not null)
        {
            _refreshTimer.Tick -= RefreshTimer_Tick;
            _refreshTimer.Stop();
        }
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 19. Fix the inverted result handling after `AddRepositoryAsync`.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminPluginsPage.xaml.cs:1264-1284`
- **CodeRabbit ID:** `6ddead28-8329-4903-b03e-763320b639e7`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Fix the inverted result handling after `AddRepositoryAsync`.**

`AddRepositoryAsync` returns the created repository. When it returns null, the call failed. The current code reports the outcome through `SurfaceViewModelMutationResult("Repository added.")` and returns. `SurfaceViewModelMutationResult` shows an error only when `ViewModel.ErrorMessage` is set, so a null return without an error message produces the success toast "Repository added." while no repository exists.

Report the failure explicitly on the null path.




<details>
<summary>🐛 Proposed fix</summary>

```diff
         if (repository is null)
         {
-            SurfaceViewModelMutationResult("Repository added.");
+            ShowStatus(ViewModel.ErrorMessage ?? "Could not add the repository.", isError: true);
             return;
         }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminPluginsPage.xaml.cs around lines 1264 - 1284, Update SubmitRepoButton_Click so a null result from AddRepositoryAsync is treated as failure: surface an explicit failure message on that path instead of “Repository added.” and return without clearing or collapsing the form. Preserve the existing success handling for a non-null repository.

#### CodeRabbit suggestion

```text
private async void SubmitRepoButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RepoNameBox.Text) || string.IsNullOrWhiteSpace(RepoUrlBox.Text)) return;
        var repository = await ViewModel.AddRepositoryAsync(new CreatePluginRepositoryRequest
        {
            Url = RepoUrlBox.Text.Trim(),
            DisplayName = RepoNameBox.Text.Trim(),
            Enabled = true
        });
        if (repository is null)
        {
            ShowStatus(ViewModel.ErrorMessage ?? "Could not add the repository.", isError: true);
            return;
        }
        RepoNameBox.Text = "";
        RepoUrlBox.Text = "";
        RepoForm.Visibility = Visibility.Collapsed;
        AddRepoButtonText.Text = "Add";
        AddRepoButtonIcon.Glyph = "\uE710";
        SurfaceViewModelMutationResult("Repository added.");
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 20. Wrap the users fetch in error handling.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminRequestsPage.xaml.cs:161-171`
- **CodeRabbit ID:** `3b269546-8897-4ec5-b260-a0988374c92c`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Wrap the users fetch in error handling.**

`LoadQueueAsync` awaits `_adminApi.GetUsersAsync()` without a try/catch. Every caller is an `async void` handler (`Page_Loaded`, `QueueTab_Click`, `Refresh_Click`, `Filter_Changed`). If the users endpoint fails or times out, the exception propagates out of an `async void` method and terminates the process. Every other loader in this page (`LoadSettingsAsync`, `LoadIntegrationsAsync`, `LoadUsersAsync`) already catches and calls `ShowError`.

<details>
<summary>🛡️ Proposed fix</summary>

```diff
     private async Task LoadQueueAsync(bool showSkeleton = true)
     {
         if (showSkeleton) ShowQueueSkeletons();
         ViewModel.StatusFilter = SelectedTag(StatusFilterComboBox, "all");
         ViewModel.OutcomeFilter = SelectedTag(OutcomeFilterComboBox, "all");
-        var queueTask = ViewModel.LoadCommand.ExecuteAsync(null);
-        var usersTask = _users.Count == 0 ? _adminApi.GetUsersAsync() : null;
-        if (usersTask != null) await Task.WhenAll(queueTask, usersTask); else await queueTask;
-        if (usersTask != null) _users = await usersTask;
+        try
+        {
+            var queueTask = ViewModel.LoadCommand.ExecuteAsync(null);
+            var usersTask = _users.Count == 0 ? _adminApi.GetUsersAsync() : null;
+            if (usersTask != null) await Task.WhenAll(queueTask, usersTask); else await queueTask;
+            if (usersTask != null) _users = await usersTask;
+        }
+        catch (Exception ex)
+        {
+            ShowError($"Requests could not be loaded: {ex.Message}");
+        }
         RenderQueue();
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminRequestsPage.xaml.cs around lines 161 - 171, Update LoadQueueAsync to catch exceptions from _adminApi.GetUsersAsync and handle them through the page’s existing ShowError pattern, preventing failures from escaping the async void callers. Preserve the existing queue loading and RenderQueue behavior, and match the error-handling approach used by LoadSettingsAsync, LoadIntegrationsAsync, and LoadUsersAsync.

#### CodeRabbit suggestion

```text
private async Task LoadQueueAsync(bool showSkeleton = true)
    {
        if (showSkeleton) ShowQueueSkeletons();
        ViewModel.StatusFilter = SelectedTag(StatusFilterComboBox, "all");
        ViewModel.OutcomeFilter = SelectedTag(OutcomeFilterComboBox, "all");
        try
        {
            var queueTask = ViewModel.LoadCommand.ExecuteAsync(null);
            var usersTask = _users.Count == 0 ? _adminApi.GetUsersAsync() : null;
            if (usersTask != null) await Task.WhenAll(queueTask, usersTask); else await queueTask;
            if (usersTask != null) _users = await usersTask;
        }
        catch (Exception ex)
        {
            ShowError($"Requests could not be loaded: {ex.Message}");
        }
        RenderQueue();
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 21. Guard the nullable `Language` and `Format` values.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminSubtitlesPage.xaml.cs:296-299`
- **CodeRabbit ID:** `7f20daea-ae1b-44bf-b910-d331dcda2b83`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Guard the nullable `Language` and `Format` values.**

Line 209 reads `s.Language ?? ""` and line 484 tests `string.IsNullOrWhiteSpace(subtitle.Format)`. Both show that `AdminDownloadedSubtitle.Language` and `AdminDownloadedSubtitle.Format` can be null. Lines 296 and 299 dereference them directly. A stored subtitle row with a missing language or format throws a `NullReferenceException` inside `BuildSubtitleRow`, and the whole table fails to render. Lines 439 and 519 dereference `Language` in the same way.

<details>
<summary>🛡️ Proposed fix</summary>

```diff
-        AddCell(root, MakeBadge($"{subtitle.Language.ToUpperInvariant()}  {Services.PlayerService.LanguageCodeToName(subtitle.Language)}"), 2);
+        var language = subtitle.Language ?? "";
+        AddCell(root, MakeBadge($"{language.ToUpperInvariant()}  {Services.PlayerService.LanguageCodeToName(language)}"), 2);
         AddCell(root, MakeBadge(ProviderLabel(subtitle.Provider)), 3);
         AddCell(root, MakeCell(string.IsNullOrWhiteSpace(subtitle.ReleaseName) ? "—" : subtitle.ReleaseName, fontFamily: "Consolas"), 4);
-        AddCell(root, MakeBadge($".{subtitle.Format.TrimStart('.')}") , 5);
+        AddCell(root, MakeBadge($".{(subtitle.Format ?? "srt").TrimStart('.')}"), 5);
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminSubtitlesPage.xaml.cs around lines 296 - 299, Guard nullable AdminDownloadedSubtitle.Language and Format accesses throughout the subtitle row/table rendering flow, especially BuildSubtitleRow and the paths around the referenced Language usages. Use the existing empty-string/whitespace handling so missing language or format values render safely without throwing, while preserving normal display for populated values.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 22. Reject an invalid interval before you add the trigger.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminTaskDetailPage.xaml.cs:913-927`
- **CodeRabbit ID:** `a0c17a1e-1818-40a3-a01a-27dcc60df992`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Reject an invalid interval before you add the trigger.**

If the user selects "Interval" and leaves `valueBox` empty or types a non-numeric value, `long.TryParse` fails and the code still adds the trigger. `newTrigger.IntervalMs` stays null. The row then renders as "Every 0 second(s)", and Save sends the incomplete trigger to the server. Apply the same check to "daily" and "weekly" time input.

<details>
<summary>🐛 Proposed fix</summary>

```diff
             addBtn.Click += (_, _) =>
             {
                 var selectedType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "interval";
                 var newTrigger = new TriggerConfig { Type = selectedType };
 
-                if (selectedType == "interval" && long.TryParse(valueBox.Text, out var ms))
-                    newTrigger.IntervalMs = ms;
-                else if (selectedType == "daily")
-                    newTrigger.TimeOfDay = string.IsNullOrWhiteSpace(timeBox.Text) ? "00:00" : timeBox.Text.Trim();
-                else if (selectedType == "weekly")
-                    newTrigger.TimeOfDay = string.IsNullOrWhiteSpace(timeBox.Text) ? "00:00" : timeBox.Text.Trim();
+                if (selectedType == "interval")
+                {
+                    if (!long.TryParse(valueBox.Text, out var ms) || ms <= 0)
+                    {
+                        valueBox.PlaceholderText = "Enter a positive interval in ms";
+                        return;
+                    }
+                    newTrigger.IntervalMs = ms;
+                }
+                else if (selectedType is "daily" or "weekly")
+                {
+                    newTrigger.TimeOfDay = string.IsNullOrWhiteSpace(timeBox.Text) ? "00:00" : timeBox.Text.Trim();
+                }
 
                 triggerList.Add(newTrigger);
                 RebuildTriggerEditor();
             };
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminTaskDetailPage.xaml.cs around lines 913 - 927, Update the addBtn.Click handler to validate trigger inputs before adding a trigger: require a successfully parsed interval value for “interval” and valid non-empty time input for “daily” and “weekly”; when validation fails, return without calling triggerList.Add or RebuildTriggerEditor. Preserve the existing defaults and assignment behavior for valid daily/weekly values.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 23. Clamp the progress value before you build the star widths.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminTasksPage.xaml.cs:296-308`
- **CodeRabbit ID:** `c22bf4b4-877f-49c8-815b-bf747ed8fab4`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Clamp the progress value before you build the star widths.**

`pct` is only floored at 2. If the server reports a progress value above 100, `100 - pct` is negative and `new GridLength(negative, GridUnitType.Star)` throws. The row build then fails inside `RebuildTaskGroups`. `AdminTaskDetailPage.BuildProgressSection` already clamps with `Math.Clamp(Math.Max(task.Progress, 2), 0, 100)`. Apply the same clamp here.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-            double pct = Math.Max(task.Progress, 2.0);
+            double pct = Math.Clamp(Math.Max(task.Progress, 2.0), 0, 100);
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminTasksPage.xaml.cs around lines 296 - 308, Clamp pct in RebuildTaskGroups to the 2–100 range before constructing the GridLength star widths, matching the Math.Clamp behavior used by AdminTaskDetailPage.BuildProgressSection; preserve the existing minimum progress of 2.

#### CodeRabbit suggestion

```text
double pct = Math.Clamp(Math.Max(task.Progress, 2.0), 0, 100);
            var progressFill = new Border
            {
                Background = task.State == "cancelling"
                    ? new SolidColorBrush(Color.FromArgb(255, 234, 179, 8))
                    : (SolidColorBrush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(4),
            };

            // Use a Grid to simulate percentage width
            var progressGrid = new Grid();
            progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 24. Keep the page-size control reachable after the user raises the page size.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminUsersPage.xaml.cs:758-791`
- **CodeRabbit ID:** `1592b9ce-577b-40be-868f-ddc0d1dcfb78`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Keep the page-size control reachable after the user raises the page size.**

The pagination bar is shown only when `list.Count > _userPageSize`. If the user selects 100 rows while 30 users exist, the bar hides on the next build. The page-size combo disappears with it, so the user cannot return to 25 rows. Show the bar whenever the list is non-empty, or whenever the page size is not the default.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         // Pagination bar
         UserPaginationBar.Children.Clear();
-        UserPaginationBar.Visibility = list.Count > _userPageSize ? Visibility.Visible : Visibility.Collapsed;
-        if (list.Count > _userPageSize)
+        bool showPagination = list.Count > _userPageSize || _userPageSize != 25;
+        UserPaginationBar.Visibility = showPagination ? Visibility.Visible : Visibility.Collapsed;
+        if (showPagination)
         {
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminUsersPage.xaml.cs around lines 758 - 791, Update the UserPaginationBar visibility condition in BuildUserRows so it remains visible when the user list is non-empty or _userPageSize differs from the default page size, preserving access to the page-size ComboBox after selecting a larger size.

#### CodeRabbit suggestion

```text
// Pagination bar
        UserPaginationBar.Children.Clear();
        bool showPagination = list.Count > _userPageSize || _userPageSize != 25;
        UserPaginationBar.Visibility = showPagination ? Visibility.Visible : Visibility.Collapsed;
        if (showPagination)
        {
            UserPaginationBar.Children.Add(new TextBlock
            {
                Text = $"Showing {start + 1}-{end} of {list.Count}",
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });

            var pageSizeCombo = new ComboBox { Width = 70, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            foreach (var ps in new[] { 25, 50, 100 })
            {
                var item = new ComboBoxItem { Content = $"{ps} rows", Tag = ps };
                if (ps == _userPageSize) item.IsSelected = true;
                pageSizeCombo.Items.Add(item);
            }
            pageSizeCombo.SelectionChanged += (_, _) =>
            {
                if (pageSizeCombo.SelectedItem is ComboBoxItem sel && sel.Tag is int ps)
                { _userPageSize = ps; _userPage = 0; BuildUserRows(); }
            };
            UserPaginationBar.Children.Add(pageSizeCombo);

            var prevBtn = new Button { Content = "Previous", Padding = new Thickness(12, 6, 12, 6), IsEnabled = _userPage > 0 };
            prevBtn.Click += (_, _) => { _userPage--; BuildUserRows(); };
            var nextBtn = new Button { Content = "Next", Padding = new Thickness(12, 6, 12, 6), IsEnabled = _userPage < totalPages - 1 };
            nextBtn.Click += (_, _) => { _userPage++; BuildUserRows(); };
            UserPaginationBar.Children.Add(prevBtn);
            UserPaginationBar.Children.Add(nextBtn);
        }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 25. Bump the load generation in `SaveAsync`.

- [ ] **Status:** Unreviewed
- **Scope:** Application services
- **Location:** `src/SiloPlayer/Services/CardOverlayService.cs:291-297`
- **CodeRabbit ID:** `bc692c30-898d-470d-9c36-2607a3c479c9`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Bump the load generation in `SaveAsync`.**

`EnsureLoadedAsync` captures `_loadGeneration` before it awaits and discards its result only when that generation changed. `SaveAsync` writes `_document` and `_initialized` without changing the generation and without taking `_initLock`. If a user saves while an initial load is still in flight, the load completes afterwards and overwrites the saved document with the server or default document. The saved preferences then disappear until the next `Invalidate`.

Increment `_loadGeneration` in `SaveAsync` so an in-flight load cannot publish stale state.




<details>
<summary>🛠️ Proposed fix</summary>

```diff
     public async Task SaveAsync(CardOverlayPrefs prefs, CancellationToken ct = default)
     {
         var normalized = NormalizeDocument(prefs);
         await _settingsApi.PutSettingAsync("card_overlays", SerializePrefs(normalized), ct);
+        Interlocked.Increment(ref _loadGeneration);
         _document = normalized;
         _initialized = true;
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Services/CardOverlayService.cs around lines 291 - 297, Update SaveAsync to increment _loadGeneration when saving preferences, ensuring any in-flight EnsureLoadedAsync operation discards its stale result before publishing. Keep the existing normalized document persistence and state updates unchanged.

#### CodeRabbit suggestion

```text
public async Task SaveAsync(CardOverlayPrefs prefs, CancellationToken ct = default)
    {
        var normalized = NormalizeDocument(prefs);
        await _settingsApi.PutSettingAsync("card_overlays", SerializePrefs(normalized), ct);
        Interlocked.Increment(ref _loadGeneration);
        _document = normalized;
        _initialized = true;
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 26. Decode UTF-8 after the message is complete.

- [ ] **Status:** Unreviewed
- **Scope:** Application services
- **Location:** `src/SiloPlayer/Services/PlaybackWebSocket.cs:135-157`
- **CodeRabbit ID:** `02f28e19-1335-44af-9817-d4a9f2e16544`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Decode UTF-8 after the message is complete.**

`Encoding.UTF8.GetString` runs on each 8192-byte frame. A multi-byte UTF-8 character that spans a frame boundary is decoded as replacement characters, so long messages with non-ASCII text arrive corrupted. Accumulate the bytes and decode once at `EndOfMessage`, or use a stateful `Decoder`.

<details>
<summary>🛠️ Proposed fix</summary>

```diff
-        var buffer = new byte[8192];
-        var messageBuffer = new StringBuilder();
+        var buffer = new byte[8192];
+        var messageBytes = new List<byte>();
@@
-                messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
-
-                if (result.EndOfMessage)
-                {
-                    var message = messageBuffer.ToString();
-                    messageBuffer.Clear();
-                    await HandleMessage(message);
-                }
+                messageBytes.AddRange(buffer.AsSpan(0, result.Count).ToArray());
+
+                if (result.EndOfMessage)
+                {
+                    var message = Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(messageBytes));
+                    messageBytes.Clear();
+                    await HandleMessage(message);
+                }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Services/PlaybackWebSocket.cs around lines 135 - 157, Update the receive loop in the WebSocket message-handling method to preserve UTF-8 sequences across frames by accumulating the raw bytes and decoding only when result.EndOfMessage is true, or by using a stateful Decoder. Keep messageBuffer and HandleMessage behavior intact while preventing multibyte characters split across frames from being replaced.

#### CodeRabbit suggestion

```text
var buffer = new byte[8192];
        var messageBytes = new List<byte>();

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _ws.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log("Server closed connection");
                    break;
                }

                messageBytes.AddRange(buffer.AsSpan(0, result.Count).ToArray());

                if (result.EndOfMessage)
                {
                    var message = Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(messageBytes));
                    messageBytes.Clear();
                    await HandleMessage(message);
                }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 27. Prevent old delayed commands from controlling the new session.

- [ ] **Status:** Unreviewed
- **Scope:** Application services
- **Location:** `src/SiloPlayer/Services/WatchTogetherCoordinator.cs:239-243`
- **CodeRabbit ID:** `efb8bf8c-c71a-458b-b91d-3184ed41a693`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Prevent old delayed commands from controlling the new session.**

Line 242 applies `cmd` after the delay without revalidating the room or attached session. If `ClearActiveRoom`, `NotifyPlaybackEnded`, or a new session occurs during the delay, an old `play`, `pause`, or `seek` command can change the current player state.

Capture the room and session at receipt time. Validate both immediately before `ApplyCommand`.

#### Requested remediation

In @src/SiloPlayer/Services/WatchTogetherCoordinator.cs around lines 239 - 243, Update the delayed command task around ApplyCommand to capture the active room and attached session when the command is received, then revalidate both immediately before applying the command. Skip ApplyCommand when ClearActiveRoom, NotifyPlaybackEnded, or a new session has changed either captured identity.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 28. Send the required playback progress heartbeat.

- [ ] **Status:** Unreviewed
- **Scope:** Application services
- **Location:** `src/SiloPlayer/Services/WatchTogetherCoordinator.cs:308-313`
- **CodeRabbit ID:** `da0655b5-fe5d-4635-8e79-ad5349d94e66`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Send the required playback progress heartbeat.**

This timer sends `state_report` frames only. It does not issue the required progress call while playback is active. The server can reap an active session after about 45 seconds without that heartbeat.

Keep the 1.5-second room state report. Add a separate or throttled progress request every 5-10 seconds while playing.

As per coding guidelines, “Always report progress while playing” and “Call every 5-10 seconds.”

#### Requested remediation

In @src/SiloPlayer/Services/WatchTogetherCoordinator.cs around lines 308 - 313, Update TickStateReport so active playback also sends the required progress heartbeat every 5–10 seconds, using the existing progress-reporting service or API and throttling it independently from the 1.5-second _activeRoom.ReportState call. Only issue progress while a valid session is attached, playback is loaded, and the player is currently playing; preserve the existing room state reporting behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 29. Make the loaded template self-contained. `XamlReader.Load` cannot resolve `StaticResource` references from `Application.Resources` in this disconnected XAML fragment. Define the brushes locally or assign them after loading; otherwise template creation throws and the palette cannot display results.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/AdminCommandPaletteDialog.cs:85-100`
- **CodeRabbit ID:** `71564ba1-2a7a-438a-80f1-0d01310db3c0`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Make the loaded template self-contained.** `XamlReader.Load` cannot resolve `StaticResource` references from `Application.Resources` in this disconnected XAML fragment. Define the brushes locally or assign them after loading; otherwise template creation throws and the palette cannot display results.

#### Requested remediation

In @src/SiloPlayer/Controls/AdminCommandPaletteDialog.cs around lines 85 - 100, Update the XAML string used by AdminCommandPaletteDialog’s XamlReader.Load call so the DataTemplate no longer depends on unresolved application-level StaticResource brushes. Define the required secondary and tertiary text brushes locally within the loaded fragment, or assign equivalent brush values after loading, while preserving the existing text styling.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 30. Reset the cover state in `Bind` so recycled cards do not show a stale poster.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/AudiobookSquareCard.xaml.cs:39-51`
- **CodeRabbit ID:** `a419accb-786d-4a30-96c4-e61c581981f5`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Reset the cover state in `Bind` so recycled cards do not show a stale poster.**

`Bind` never clears `CoverImage`. Line 50 starts a poster load only when `PosterUrl` is non-empty. `LoadPosterAsync` sets `CoverImage.Opacity = 1` and hides `FallbackTitle` at Lines 61-62, and nothing restores those values.

This control is a list item, so the same instance is reused for different items. If the next item has no `PosterUrl`, the card keeps the previous item's cover image while `TitleText` already shows the new title. The user sees the wrong cover.

Clear the image state at the start of `Bind`.




<details>
<summary>🐛 Proposed fix</summary>

```diff
     private void Bind(MediaItem item)
     {
+        CoverImage.Source = null;
+        CoverImage.Opacity = 0;
+        FallbackTitle.Visibility = Visibility.Visible;
         TitleText.Text = item.Title;
         FallbackTitle.Text = item.Title;
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/AudiobookSquareCard.xaml.cs around lines 39 - 51, Reset the recycled cover state at the start of Bind before assigning the new item data: clear CoverImage and restore its default visibility/opacity state, and show FallbackTitle. Preserve the existing LoadPosterAsync path for items with a non-empty PosterUrl.

#### CodeRabbit suggestion

```text
private void Bind(MediaItem item)
    {
        CoverImage.Source = null;
        CoverImage.Opacity = 0;
        FallbackTitle.Visibility = Visibility.Visible;
        TitleText.Text = item.Title;
        FallbackTitle.Text = item.Title;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, item.Title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MoreButton, $"More actions for {item.Title}");
        var position = Math.Max(0, item.PositionSeconds ?? 0);
        var duration = Math.Max(0, item.DurationSeconds ?? 0);
        ProgressFill.Width = duration > 0 ? 168 * Math.Clamp(position / duration, 0, 1) : 0;
        var remaining = Math.Max(0, duration - position);
        TimeLeftText.Text = remaining > 0 ? $"{FormatDuration(remaining)} left" : "";
        if (!string.IsNullOrWhiteSpace(item.PosterUrl)) _ = LoadPosterAsync(item);
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 31. The `ImageOpened` subscription leaks and can run more than once.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/BackdropImage.xaml.cs:76-93`
- **CodeRabbit ID:** `36fc159b-9c6b-4c53-9d32-3382f4ec751c`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**The `ImageOpened` subscription leaks and can run more than once.**

Line 79 attaches a lambda to `bmp.ImageOpened` and never detaches it. Two consequences follow.

The lambda captures `ctrl`, so the `BitmapImage` holds a strong reference to the `BackdropImage`. If the bitmap comes from a shared image cache, the control cannot be collected after it leaves the visual tree.

If the same `BitmapImage` instance is assigned to `Source` more than once, a new handler is added each time. Every later `ImageOpened` then calls `Reposition` once per accumulated handler.

The handler also does not check that the bitmap is still the current `Source`. A late `ImageOpened` from a superseded bitmap overwrites `_imgW` and `_imgH` with the wrong dimensions.

Detach the previous handler and verify ownership inside the handler.




<details>
<summary>🐛 Proposed fix</summary>

```diff
     private double _imgW;
     private double _imgH;
     private Storyboard? _kenBurnsStoryboard;
+    private BitmapImage? _trackedBitmap;
+    private RoutedEventHandler? _imageOpenedHandler;
```

```diff
         ctrl._imgW = 0;
         ctrl._imgH = 0;
+        if (ctrl._trackedBitmap != null && ctrl._imageOpenedHandler != null)
+        {
+            ctrl._trackedBitmap.ImageOpened -= ctrl._imageOpenedHandler;
+            ctrl._trackedBitmap = null;
+            ctrl._imageOpenedHandler = null;
+        }
         ctrl.InnerImage.Source = e.NewValue as ImageSource;
 
         if (e.NewValue is BitmapImage bmp)
         {
             // BitmapImage may not have pixel dimensions until ImageOpened fires
-            bmp.ImageOpened += (_, _) =>
-            {
-                ctrl._imgW = bmp.PixelWidth;
-                ctrl._imgH = bmp.PixelHeight;
-                ctrl.Reposition();
-            };
+            ctrl._imageOpenedHandler = (_, _) =>
+            {
+                if (!ReferenceEquals(ctrl.InnerImage.Source, bmp)) return;
+                ctrl._imgW = bmp.PixelWidth;
+                ctrl._imgH = bmp.PixelHeight;
+                ctrl.Reposition();
+            };
+            ctrl._trackedBitmap = bmp;
+            bmp.ImageOpened += ctrl._imageOpenedHandler;
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/BackdropImage.xaml.cs around lines 76 - 93, Update the Source-change logic around the ImageOpened subscription to retain and detach the previously registered handler before adding a new one, preventing duplicate callbacks and stale control references. In the ImageOpened handler, verify the event’s BitmapImage is still the current Source before updating _imgW, _imgH, and calling Reposition; preserve the immediate-dimension path for already loaded images.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 32. Wrap the refresh handlers in try/catch.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/CatalogImportDialog.xaml.cs:90-100`
- **CodeRabbit ID:** `2f1d6c90-fa7f-4e67-8b03-e1dd18643dff`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Wrap the refresh handlers in try/catch.**

`RefreshLocalSources_Click` and `RefreshBucketSources_Click` are `async void`. Neither handler catches exceptions from `RefreshLocalSourcesAsync` or `RefreshBucketSourcesAsync`. An exception that escapes an `async void` handler is rethrown on the UI thread and terminates the process.

Both methods perform remote calls, so they fail on network loss, on an expired token, and on a server error. `OnPrimaryButtonClick` at Line 161 already wraps its await in try/catch, so this omission is inconsistent with the rest of the file.




<details>
<summary>🐛 Proposed fix</summary>

```diff
     private async void RefreshLocalSources_Click(object sender, RoutedEventArgs e)
     {
-        await _vm.RefreshLocalSourcesAsync();
-        PopulateLocalSources();
+        try
+        {
+            await _vm.RefreshLocalSourcesAsync();
+            PopulateLocalSources();
+        }
+        catch
+        {
+            // Keep the existing list. The dialog stays usable.
+        }
     }
 
     private async void RefreshBucketSources_Click(object sender, RoutedEventArgs e)
     {
-        await _vm.RefreshBucketSourcesAsync();
-        PopulateBucketSources();
+        try
+        {
+            await _vm.RefreshBucketSourcesAsync();
+            PopulateBucketSources();
+        }
+        catch
+        {
+            // Keep the existing list. The dialog stays usable.
+        }
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/CatalogImportDialog.xaml.cs around lines 90 - 100, Wrap both RefreshLocalSources_Click and RefreshBucketSources_Click in try/catch blocks, including their awaited refresh calls and subsequent population calls, so exceptions are handled within the async void UI handlers. Match the existing exception-handling pattern used by OnPrimaryButtonClick, preserving the current refresh and population behavior on success.

#### CodeRabbit suggestion

```text
private async void RefreshLocalSources_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.RefreshLocalSourcesAsync();
            PopulateLocalSources();
        }
        catch
        {
            // Keep the existing list. The dialog stays usable.
        }
    }

    private async void RefreshBucketSources_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.RefreshBucketSourcesAsync();
            PopulateBucketSources();
        }
        catch
        {
            // Keep the existing list. The dialog stays usable.
        }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 33. Show the import error instead of failing silently.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/CatalogImportDialog.xaml.cs:217-230`
- **CodeRabbit ID:** `59a119f1-de10-4448-ac8f-b5de9e41858b`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Show the import error instead of failing silently.**

The `catch` at Line 221 discards the exception and only sets `args.Cancel = true`. The dialog stays open, the primary button returns to "Import Catalog", and the user sees no reason for the failure.

Catalog import is a destructive administrative operation. A silent failure leads the user to retry with the same input.

Add an error text element to the dialog and write the exception message into it.




<details>
<summary>🐛 Proposed fix</summary>

Add a status element to `CatalogImportDialog.xaml`, inside the outer `StackPanel`:

```xml
<TextBlock x:Name="ErrorText" FontSize="12" Foreground="#FFFCA5A5"
           TextWrapping="Wrap" Visibility="Collapsed" />
```

Then report the failure:

```diff
         try
         {
+            ErrorText.Visibility = Visibility.Collapsed;
             await _vm.SubmitImportAsync(BuiltRequest);
         }
-        catch
+        catch (Exception ex)
         {
+            ErrorText.Text = ex.Message;
+            ErrorText.Visibility = Visibility.Visible;
             args.Cancel = true;
         }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/CatalogImportDialog.xaml.cs around lines 217 - 230, Update the CatalogImportDialog failure path around SubmitImportAsync so the catch block stores the exception message in a dialog error text element named ErrorText and makes that element visible, while retaining args.Cancel = true. Add the ErrorText TextBlock to the dialog layout inside the outer StackPanel, initially collapsed, and preserve the existing finally cleanup.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 34. Enable dependent animation for `ProgressRailFill.Width`.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/HeroCarousel.xaml.cs:682-693`
- **CodeRabbit ID:** `76baed02-7c1c-4d18-824c-21826adc363d`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Enable dependent animation for `ProgressRailFill.Width`.**

`Width` is a layout property, so WinUI skips this dependent animation by default. Set `EnableDependentAnimation = true`, or animate `ScaleX` on a fixed-width rail to avoid per-frame layout work.

#### Requested remediation

In @src/SiloPlayer/Controls/HeroCarousel.xaml.cs around lines 682 - 693, Update the DoubleAnimation created for ProgressRailFill.Width in the progress storyboard to enable dependent animation, ensuring the Width animation runs correctly in WinUI. Keep the existing duration, range, target, and storyboard behavior unchanged.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 35. Attach the event handlers only after `Subscribe` succeeds, or track subscription separately.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/ServerActivityButton.xaml.cs:146-166`
- **CodeRabbit ID:** `a8a0f0e6-6547-4893-94ae-25b3bd8dcaab`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Attach the event handlers only after `Subscribe` succeeds, or track subscription separately.**

The guard at line 148 uses `_subscription`. Lines 155-157 attach `SnapshotReceived`, `EventReceived`, and `StateChanged` before line 158 assigns `_subscription`. If `_events.Subscribe` throws, the catch block swallows the error, `_subscription` stays null, and the three handlers stay attached. The comment at line 164 states that a non-admin or transport failure is expected here, so this path is reachable. Each later `StartMonitoring` call then attaches the same handlers again and multiplies the `PollAsync` calls per event.

`UnsubscribeFromEvents` removes only one registration per handler, so the duplicates persist across load and unload cycles.




<details>
<summary>🛠️ Proposed fix for the duplicate handler registration</summary>

```diff
         try
         {
             ApplyConnectionState(_events.CurrentState);
+            _subscription = _events.Subscribe("sessions", "tasks", "scans");
             _events.SnapshotReceived += OnSnapshot;
             _events.EventReceived += OnEvent;
             _events.StateChanged += OnWsStateChanged;
-            _subscription = _events.Subscribe("sessions", "tasks", "scans");
             if (_events.TryGetLatestSnapshot("scans", out var cachedScans))
                 OnSnapshot("scans", cachedScans);
         }
         catch
         {
             // Non-admin / transport issue — polling still carries the button.
+            UnsubscribeFromEvents();
         }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/ServerActivityButton.xaml.cs around lines 146 - 166, Update SubscribeToEvents so SnapshotReceived, EventReceived, and StateChanged are attached only after _events.Subscribe succeeds and _subscription is assigned; keep snapshot initialization after registration. Preserve the existing catch behavior, ensuring a failed subscription leaves no event handlers attached and allows later retries without duplicates.

#### CodeRabbit suggestion

```text
private void SubscribeToEvents()
    {
        if (_subscription != null) return;
        try
        {
            // The shared client may already be live before this control loads.
            // Seed from its current state instead of showing a false warning
            // until the next reconnect transition.
            ApplyConnectionState(_events.CurrentState);
            _subscription = _events.Subscribe("sessions", "tasks", "scans");
            _events.SnapshotReceived += OnSnapshot;
            _events.EventReceived += OnEvent;
            _events.StateChanged += OnWsStateChanged;
            if (_events.TryGetLatestSnapshot("scans", out var cachedScans))
                OnSnapshot("scans", cachedScans);
        }
        catch
        {
            // Non-admin / transport issue — polling still carries the button.
            UnsubscribeFromEvents();
        }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 36. Cancel the pending close animation when the sheet reopens.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/SlideSheet.xaml.cs:82-137`
- **CodeRabbit ID:** `92877fd6-aa69-4143-a694-32a00daf97ae`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Cancel the pending close animation when the sheet reopens.**

`Close` registers a `Completed` handler that collapses the control and raises `Closed`. The storyboard is not retained and `Open` does not stop it. If `IsOpen` goes false and then true again inside the 220 ms animation window, the earlier close storyboard still completes. It then sets `Visibility = Collapsed` on a sheet that is open, and raises a false `Closed` event. Consumers that reset form state on `Closed` lose the user's input.

A scrim tap followed by a quick reopen triggers this.




<details>
<summary>🐛 Proposed fix for the stale close completion</summary>

```diff
 public sealed partial class SlideSheet : UserControl
 {
     private const double SheetWidth = 420;
     private static readonly TimeSpan AnimDuration = TimeSpan.FromMilliseconds(220);
+    private Storyboard? _closeStoryboard;
@@
     private void Open()
     {
+        _closeStoryboard?.Stop();
+        _closeStoryboard = null;
         this.Visibility = Visibility.Visible;
@@
         sb.Completed += (_, _) =>
         {
+            if (IsOpen) return;
+            _closeStoryboard = null;
             this.Visibility = Visibility.Collapsed;
             try { Closed?.Invoke(); } catch { }
         };
+        _closeStoryboard = sb;
         sb.Begin();
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/SlideSheet.xaml.cs around lines 82 - 137, Retain the active close Storyboard in the SlideSheet control so Open can stop and remove it before starting the open animation. Ensure the stale close Completed handler cannot collapse the sheet or invoke Closed after reopening, while preserving normal close completion behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 37. Keep the toast root hit-testable.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/ToastContainer.xaml:9-13`
- **CodeRabbit ID:** `b4f06c2e-8cae-4b98-9986-f7396d54957e`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Keep the toast root hit-testable.**

`IsHitTestVisible="False"` blocks hit testing for the entire subtree. Setting it to `true` on individual toast borders cannot restore input. Remove it and use `Background="{x:Null}"` so empty overlay space passes clicks through while toast borders remain clickable.

#### Requested remediation

In @src/SiloPlayer/Controls/ToastContainer.xaml around lines 9 - 13, Update the ToastContainer root to remove IsHitTestVisible="False" and set its Background to {x:Null}; retain the individual toast hit-test behavior so dismiss controls remain clickable while empty overlay space passes clicks through.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 38. Parse with invariant culture and assume UTC.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Helpers/TimeAgo.cs:29-75`
- **CodeRabbit ID:** `8af8e5b0-063a-476b-b81b-f1527bb31b39`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Parse with invariant culture and assume UTC.**

Line 32 and line 66 call `DateTime.TryParse` with the current culture and `DateTimeStyles.None`. Two consequences follow:

- If the server omits the offset, for example `2024-05-01T10:00:00`, the parse returns `DateTimeKind.Unspecified`. `ToUniversalTime()` then treats the value as local time and shifts it by the local UTC offset. Users outside UTC see wrong relative times.
- A non-invariant current culture can misparse or fail on ISO input.

`FlexibleNullableDateTimeOffsetConverter` in this same change set already uses `CultureInfo.InvariantCulture` with `AssumeUniversal`. Align this helper with that behavior.

<details>
<summary>🛠️ Proposed fix</summary>

```diff
+using System.Globalization;
+
 namespace SiloPlayer.Core.Helpers;
@@
-        if (!DateTime.TryParse(iso, out var date)) return null;
-
-        var seconds = (long)(DateTime.UtcNow - date.ToUniversalTime()).TotalSeconds;
+        if (!TryParseUtc(iso, out var date)) return null;
+
+        var seconds = (long)(DateTime.UtcNow - date).TotalSeconds;
@@
-        if (!DateTime.TryParse(iso, out var date)) return iso;
-
-        var diffMinutes = Math.Max(0, (long)(DateTime.UtcNow - date.ToUniversalTime()).TotalMinutes);
+        if (!TryParseUtc(iso, out var date)) return iso;
+
+        var diffMinutes = Math.Max(0, (long)(DateTime.UtcNow - date).TotalMinutes);
+    }
+
+    private static bool TryParseUtc(string iso, out DateTime utc)
+        => DateTime.TryParse(
+            iso,
+            CultureInfo.InvariantCulture,
+            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
+            out utc);
```
</details>

#### Requested remediation

In @src/SiloPlayer.Core/Helpers/TimeAgo.cs around lines 29 - 75, Update FormatAdded and FormatShort to parse ISO timestamps with CultureInfo.InvariantCulture and DateTimeStyles.AssumeUniversal, ensuring offset-less values are interpreted as UTC while preserving their existing formatting and fallback behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 39. Deserialize history items as `MediaItem`.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Models/Catalog/HistoryEntry.cs:17-22`
- **CodeRabbit ID:** `aaea8385-12f6-4d47-b9c8-ee47d28cfbca`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Deserialize history items as `MediaItem`.**

Lines 17-19 state that `GET /history` returns `MediaItem`-shaped objects, matching `ItemListResponse`. Line 22 instead deserializes them as `HistoryEntry`. This contract mismatch drops item fields that are not in `HistoryEntry` and gives history consumers the wrong model type.

<details>
<summary>Proposed fix</summary>

```diff
+using SiloPlayer.Core.Models.Home;
+
 public class HistoryResponse
 {
-    public List<HistoryEntry> Items { get; set; } = [];
+    public List<MediaItem> Items { get; set; } = [];
     public int Total { get; set; }
     public bool HasMore { get; set; }
 }
```
</details>

#### Requested remediation

In @src/SiloPlayer.Core/Models/Catalog/HistoryEntry.cs around lines 17 - 22, Update HistoryResponse.Items to use List<MediaItem> instead of List<HistoryEntry>, preserving the existing items property name and initialization so history responses deserialize into the resolved item model.

#### CodeRabbit suggestion

```text
using SiloPlayer.Core.Models.Home;

// NOTE: Server GET /history returns {"items": [...]}, same shape as itemsListResponse.
// The server does NOT include "total" or "has_more" fields — they default to 0/false.
// The items are MediaItem-shaped objects (resolved from history entries).
public class HistoryResponse
{
    public List<MediaItem> Items { get; set; } = [];
    public int Total { get; set; }
    public bool HasMore { get; set; }
}
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 40. Remove the legacy Continuum source citation.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Models/Playback/WatchDetailResponse.cs:239-242`
- **CodeRabbit ID:** `5e5ecb61-5806-4a27-8b3c-9648bbe411b6`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Remove the legacy Continuum source citation.**

Line 241 identifies `continuum-server/web/src/lib/watchTogether.ts` as the source that these DTOs shadow. This cites the prohibited legacy source. Remove this reference and document the contract from an allowed source.





As per coding guidelines, never fetch, inspect, compare against, cite, or use the legacy private GitLab Continuum repository for Silo desktop parity work.

#### Requested remediation

In @src/SiloPlayer.Core/Models/Playback/WatchDetailResponse.cs around lines 239 - 242, Remove the legacy Continuum source citation from the Watch Together DTO section comment near the Watch Together models. Replace it with documentation based on an allowed source, or omit the source reference while retaining the relevant local organization context; do not alter the DTOs or cite the prohibited repository.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 41. Guard the read loop against a superseded connection.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Services/AdminLogStreamClient.cs:85-94`
- **CodeRabbit ID:** `493fc583-0c08-4069-a4f5-ed795d586c5f`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Guard the read loop against a superseded connection.**

`StopAsync` aborts the socket, but the read loop unwinds asynchronously. `StartAsync` awaits only `StopAsync`, so the previous loop can still be running when the new socket reaches `Live`. When the old loop then reaches its `finally`, it calls `SetState(ConnectionState.Disconnected)` and can also raise `ErrorReceived`. The client then reports `Disconnected` while the new socket is open.

Add an epoch value that `StartAsync` increments, and apply state or error updates only when the loop still owns the current epoch.

<details>
<summary>♻️ Proposed guard</summary>

```diff
+    private int _epoch;
+
     public async Task StartAsync(Stream stream, IReadOnlyDictionary<string, string> filters, CancellationToken ct = default)
     {
         await StopAsync().ConfigureAwait(false);
+        var epoch = Interlocked.Increment(ref _epoch);
@@
-            _ = Task.Run(() => ReadLoopAsync(stream, _ws, _readCts.Token));
+            var ws = _ws;
+            var token = _readCts.Token;
+            _ = Task.Run(() => ReadLoopAsync(stream, ws, token, epoch));
@@
-    private async Task ReadLoopAsync(Stream stream, ClientWebSocket ws, CancellationToken ct)
+    private async Task ReadLoopAsync(Stream stream, ClientWebSocket ws, CancellationToken ct, int epoch)
@@
         catch (Exception ex)
         {
-            ErrorReceived?.Invoke($"Stream error: {ex.Message}");
+            if (Volatile.Read(ref _epoch) == epoch)
+                ErrorReceived?.Invoke($"Stream error: {ex.Message}");
         }
         finally
         {
-            SetState(ConnectionState.Disconnected);
+            if (Volatile.Read(ref _epoch) == epoch)
+                SetState(ConnectionState.Disconnected);
         }
```
</details>





Also applies to: 122-160

#### Requested remediation

In @src/SiloPlayer.Core/Services/AdminLogStreamClient.cs around lines 85 - 94, Update AdminLogStreamClient.StartAsync and ReadLoopAsync to use an incrementing connection epoch, assigning each started socket its epoch and applying state transitions or ErrorReceived notifications only when that epoch remains current. Increment the epoch when starting a new connection so a superseded read loop cannot mark the new connection Disconnected or report stale errors.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 42. The two-argument overloads are fail-open.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Services/AuthorizationPolicy.cs:21-22`
- **CodeRabbit ID:** `7926f75c-9cba-47d1-b6a5-bc76ae494c3d`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**The two-argument overloads are fail-open.**

`IsActingAdmin(user, profile)` derives `hasSelectedProfile` from `profile != null`. When a profile id is selected but the `Profile` record has not resolved yet, callers pass `profile: null` and the method grants account-level admin. This is the exact case the three-argument overload documents as unsafe. `HasPermission(UserInfo?, Profile?, string)` inherits the same behavior.

Consider removing the two-argument overloads, or requiring the explicit `hasSelectedProfile` flag at all call sites.





Also applies to: 38-40

#### Requested remediation

In @src/SiloPlayer.Core/Services/AuthorizationPolicy.cs around lines 21 - 22, The two-argument IsActingAdmin and HasPermission overloads must not infer profile selection from a nullable Profile, since an unresolved selected profile currently fails open to account-level admin. Remove these overloads and update all callers to use the three-argument forms with an explicit hasSelectedProfile value, preserving denial until the selected profile state is known.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 43. `DeleteAllForServer` leaves the impersonation credentials in Credential Manager.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Services/CredentialStore.cs:75-81`
- **CodeRabbit ID:** `24d5629f-d072-4a0e-b13c-4db2de9c3c08`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**`DeleteAllForServer` leaves the impersonation credentials in Credential Manager.**

`AuthService` writes two more keys for the same server: `impersonation_admin_refresh_token` and `impersonation_return_path`. `ClearSession(deletePersistedCredentials: true)` calls `DeleteAllForServer`, so logout is expected to remove every stored secret. These two keys survive, so an administrator refresh token stays on the device after sign-out.

<details>
<summary>🔒 Proposed fix</summary>

```diff
     public void DeleteAllForServer(string serverUrl)
     {
         DeleteCredential(serverUrl, "access_token");
         DeleteCredential(serverUrl, "refresh_token");
         DeleteCredential(serverUrl, "profile_id");
         DeleteCredential(serverUrl, "profile_token");
+        DeleteCredential(serverUrl, "impersonation_admin_refresh_token");
+        DeleteCredential(serverUrl, "impersonation_return_path");
     }
```
</details>

A shared constant set in one place would prevent the two files from drifting again.

#### Requested remediation

In @src/SiloPlayer.Core/Services/CredentialStore.cs around lines 75 - 81, Update DeleteAllForServer to also delete the impersonation_admin_refresh_token and impersonation_return_path credentials written by AuthService. Reuse shared credential-key constants if available, centralizing these names so AuthService and CredentialStore cannot drift.

#### CodeRabbit suggestion

```text
public void DeleteAllForServer(string serverUrl)
    {
        DeleteCredential(serverUrl, "access_token");
        DeleteCredential(serverUrl, "refresh_token");
        DeleteCredential(serverUrl, "profile_id");
        DeleteCredential(serverUrl, "profile_token");
        DeleteCredential(serverUrl, "impersonation_admin_refresh_token");
        DeleteCredential(serverUrl, "impersonation_return_path");
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 44. Logout does not stop handle-based subscriptions.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Services/EventChannelClient.cs:191-194`
- **CodeRabbit ID:** `9a1b8885-279a-4dbe-a20a-27d3f76c4d52`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Logout does not stop handle-based subscriptions.**

`OnLoggedOut` calls `Stop`, and `Stop` releases only the channels added through the legacy `Start` path. Channels held by `Subscribe` handles keep `_channelRefs` non-empty, so `RunLoop` continues. After logout the access token is gone, every connection attempt fails, and the loop reconnects with backoff up to 30 seconds for the lifetime of the process.

Cancel the current run loop on logout, and resume it only after a new session provides a token.

<details>
<summary>♻️ Proposed change</summary>

```diff
     private void OnLoggedOut()
     {
         Stop();
+        lock (_lock)
+        {
+            // Keep ref counts so a later login can restore the same channels,
+            // but do not reconnect without credentials.
+            CancelCurrentRunLoop_NoLock();
+            _runTask = null;
+        }
     }
```
</details>





Also applies to: 216-225

#### Requested remediation

In @src/SiloPlayer.Core/Services/EventChannelClient.cs around lines 191 - 194, Update OnLoggedOut and the run-loop lifecycle so logout cancels the active loop even when _channelRefs contains Subscribe handles, and prevent reconnection attempts without an access token. Ensure the loop resumes only when a new authenticated session provides a token, while preserving existing Stop behavior for legacy Start subscriptions.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 45. Do not advertise `clientmanageddynamicrangev1` while Profile 7 is excluded. The claim path bypasses `HDRDetails.DolbyVisionProfiles`, so the server can return untransformed Profile 7 bytes to a client that advertises only Profiles 5 and 8.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Services/MpvNativePlaybackCapabilities.cs:76`
- **CodeRabbit ID:** `33013ca1-aef6-4f19-a365-519292f4ee7a`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Do not advertise `client_managed_dynamic_range_v1` while Profile 7 is excluded.** The claim path bypasses `HDRDetails.DolbyVisionProfiles`, so the server can return untransformed Profile 7 bytes to a client that advertises only Profiles 5 and 8.

#### Requested remediation

In @src/SiloPlayer.Core/Services/MpvNativePlaybackCapabilities.cs at line 76, Update the Dolby Vision capability configuration around DolbyVisionProfiles so client_managed_dynamic_range_v1 is not advertised while Profile 7 is excluded; ensure the claim path cannot return untransformed Profile 7 data to clients limited to Profiles 5 and 8.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 46. Strip `UserInfo` before logging the URL.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Services/PlaybackUrlRedactor.cs:17-21`
- **CodeRabbit ID:** `cb007153-51e2-4ce9-92a7-d8d1f54e5d77`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Strip `UserInfo` before logging the URL.**

`GetLeftPart(UriPartial.Authority)` includes `UserInfo`, so `https://user:password@host/...` logs credentials. Build the authority from only the scheme, host, and port.

#### Requested remediation

In @src/SiloPlayer.Core/Services/PlaybackUrlRedactor.cs around lines 17 - 21, Update the URL construction in the URI redaction logic to exclude absolute.UserInfo from logged output. Build the authority using only the URI scheme, host, and port, then append RedactPath(absolute.AbsolutePath), preserving the existing handling for HTTP and HTTPS URLs.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 47. Restore a visible focus indicator on the filter pills.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/CalendarPage.xaml:35-74`
- **CodeRabbit ID:** `2ae457c7-0a93-4aa5-90c7-b9be0024a5d8`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Restore a visible focus indicator on the filter pills.**

`UseSystemFocusVisuals` is `False` and the custom `ControlTemplate` defines no focus visual state. Keyboard users get no indication of which pill has focus. The three preset buttons are the primary filter control on this page.

Either keep the system focus visuals or add a focus state to `RootBorder`.

<details>
<summary>♿ Proposed fix</summary>

```diff
-            <Setter Property="UseSystemFocusVisuals" Value="False" />
+            <Setter Property="UseSystemFocusVisuals" Value="True" />
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/CalendarPage.xaml around lines 35 - 74, Restore keyboard focus visibility for the filter pill buttons by either enabling UseSystemFocusVisuals or adding a focused visual state in the custom ControlTemplate that visibly updates RootBorder. Preserve the existing Normal, PointerOver, Pressed, and Disabled behavior.

#### CodeRabbit suggestion

```text
<Setter Property="UseSystemFocusVisuals" Value="True" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border
                            x:Name="RootBorder"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{TemplateBinding CornerRadius}">
                            <ContentPresenter
                                x:Name="ContentPresenter"
                                Padding="{TemplateBinding Padding}"
                                HorizontalAlignment="Center"
                                VerticalAlignment="Center"
                                HorizontalContentAlignment="Center"
                                VerticalContentAlignment="Center"
                                Content="{TemplateBinding Content}"
                                ContentTemplate="{TemplateBinding ContentTemplate}"
                                ContentTransitions="{TemplateBinding ContentTransitions}" />
                            <VisualStateManager.VisualStateGroups>
                                <VisualStateGroup x:Name="CommonStates">
                                    <VisualState x:Name="Normal" />
                                    <VisualState x:Name="PointerOver">
                                        <VisualState.Setters>
                                            <Setter Target="RootBorder.Opacity" Value="0.85" />
                                        </VisualState.Setters>
                                    </VisualState>
                                    <VisualState x:Name="Pressed">
                                        <VisualState.Setters>
                                            <Setter Target="RootBorder.Opacity" Value="0.7" />
                                        </VisualState.Setters>
                                    </VisualState>
                                    <VisualState x:Name="Disabled">
                                        <VisualState.Setters>
                                            <Setter Target="RootBorder.Opacity" Value="0.4" />
                                        </VisualState.Setters>
                                    </VisualState>
                                </VisualStateGroup>
                            </VisualStateManager.VisualStateGroups>
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 48. Convert `MinimumRatingBox.Text` to a number before creating the rule.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/CollectionBrowsePage.xaml.cs:304-322`
- **CodeRabbit ID:** `3a8ff75e-4a54-4c6b-bfe4-8878e2de99f8`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Convert `MinimumRatingBox.Text` to a number before creating the rule.**

`AddTextRule` sends `rating_imdb gte` with a string. Silo stores this field as `double precision`, and `pgx` does not encode a Go string as `float8`, so the catalog query fails when this filter is set. Use `double.TryParse` and add the numeric value only when valid. Keep `resolution` as `"4k"`; Silo normalizes it to `"2160p"`.

#### Requested remediation

In @src/SiloPlayer/Views/CollectionBrowsePage.xaml.cs around lines 304 - 322, Update BuildExtraRules so MinimumRatingBox.Text is parsed with double.TryParse and the rating_imdb gte QueryRule is added only when parsing succeeds, passing the numeric value rather than the original string. Keep the existing text-rule handling for original_language and preserve resolution as "4k".

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 49. `ApplyReadOnlyState` re-enables controls that were deliberately disabled.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:339-351`
- **CodeRabbit ID:** `e17d335d-dd05-4b50-9a53-51c694bbb920`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**`ApplyReadOnlyState` re-enables controls that were deliberately disabled.**

`SetDescendantControlsEnabled(..., true)` walks the whole subtree and sets `IsEnabled = true` on every `Control`. It does not preserve per-control disabled state.

In `OnNavigatedTo` the call order is:

1. Line 91-92 disables `ManualTypeButton` and `SmartTypeButton` to block type switching while editing.
2. Line 94 `UpdateSectionVisibility` disables `SourceUrlTextBox` when `CollectionType != "mdblist"` (line 195).
3. Line 96 `ApplyReadOnlyState` re-enables both, because `TypeSection` sits inside `BasicInfoSection` and `SourceUrlTextBox` sits inside `ImportedSourceSection`.

The user can therefore switch an existing collection between Manual and Smart, and can edit the source URL of a TMDB or Trakt collection. `Discard_Click` repeats the same order at lines 128-130.

<details>
<summary>🐛 Proposed fix</summary>

```diff
     private void ApplyReadOnlyState()
     {
         var editable = !ViewModel.IsReadOnly;
         SetDescendantControlsEnabled(BasicInfoSection, editable);
         SetDescendantControlsEnabled(ImportedSourceSection, editable);
         SetDescendantControlsEnabled(ImportedSharingSection, editable);
         SetDescendantControlsEnabled(ImportedVisibilitySection, editable);
         SetDescendantControlsEnabled(ImportedPosterSection, editable);
         SetDescendantControlsEnabled(ManualItemsSection, editable);
         SetDescendantControlsEnabled(SmartRulesSection, editable);
         SaveButton.IsEnabled = editable;
+
+        // Re-apply the controls that stay disabled even in an editable editor.
+        var isExistingCollection = !string.IsNullOrWhiteSpace(ViewModel.CollectionId);
+        ManualTypeButton.IsEnabled = editable && !isExistingCollection;
+        SmartTypeButton.IsEnabled = editable && !isExistingCollection;
+        SourceUrlTextBox.IsEnabled = editable && ViewModel.CollectionType == "mdblist";
         if (!editable) PageTitle.Text = $"{ViewModel.Name} · Read-only";
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/CollectionEditorPage.xaml.cs around lines 339 - 351, Update ApplyReadOnlyState so its recursive enable/disable operation does not overwrite the deliberate disabled states established by OnNavigatedTo and Discard_Click. Preserve ManualTypeButton and SmartTypeButton as disabled during editing, and keep SourceUrlTextBox disabled when CollectionType is not "mdblist", while still applying the read-only state to the remaining controls and SaveButton.

#### CodeRabbit suggestion

```text
private void ApplyReadOnlyState()
    {
        var editable = !ViewModel.IsReadOnly;
        SetDescendantControlsEnabled(BasicInfoSection, editable);
        SetDescendantControlsEnabled(ImportedSourceSection, editable);
        SetDescendantControlsEnabled(ImportedSharingSection, editable);
        SetDescendantControlsEnabled(ImportedVisibilitySection, editable);
        SetDescendantControlsEnabled(ImportedPosterSection, editable);
        SetDescendantControlsEnabled(ManualItemsSection, editable);
        SetDescendantControlsEnabled(SmartRulesSection, editable);
        SaveButton.IsEnabled = editable;

        // Re-apply the controls that stay disabled even in an editable editor.
        var isExistingCollection = !string.IsNullOrWhiteSpace(ViewModel.CollectionId);
        ManualTypeButton.IsEnabled = editable && !isExistingCollection;
        SmartTypeButton.IsEnabled = editable && !isExistingCollection;
        SourceUrlTextBox.IsEnabled = editable && ViewModel.CollectionType == "mdblist";
        if (!editable) PageTitle.Text = $"{ViewModel.Name} · Read-only";
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 50. Do not put the access token in the URL query string.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/DownloadsPage.xaml.cs:256-264`
- **CodeRabbit ID:** `8f71332f-4a51-41e4-99a7-5ceead08fe96`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Do not put the access token in the URL query string.**

Lines 259-260 append the access token to the request URL. URLs are recorded in server access logs, reverse-proxy logs, and crash telemetry, so the credential leaks outside the client. Send the token in an `Authorization` header instead.

<details>
<summary>🔒 Proposed fix</summary>

```diff
             var apiClient = App.Services.GetRequiredService<SiloApiClient>();
             var downloadPath = DownloadsApi.GetDownloadFilePath(downloadId);
             var url = $"{apiClient.BaseUrl}{downloadPath}";
-            if (apiClient.AccessToken != null)
-            url += $"?token={Uri.EscapeDataString(apiClient.AccessToken)}";
 
             var httpClient = App.Services.GetRequiredService<HttpClient>();
-            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
+            using var request = new HttpRequestMessage(HttpMethod.Get, url);
+            if (apiClient.AccessToken != null)
+                request.Headers.Authorization =
+                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiClient.AccessToken);
+            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
             response.EnsureSuccessStatusCode();
```
</details>

As per coding guidelines: "Do not copy live access tokens, refresh tokens, API keys, or credential JSON values into repo files."

#### Requested remediation

In @src/SiloPlayer/Views/DownloadsPage.xaml.cs around lines 256 - 264, Update the download request around apiClient.AccessToken and HttpClient.GetAsync to stop appending the access token to the URL query string; instead, attach it to the request’s Authorization header using the expected bearer-token scheme before sending the request, while preserving the existing URL and response handling.

#### CodeRabbit suggestion

```text
var apiClient = App.Services.GetRequiredService<SiloApiClient>();
            var downloadPath = DownloadsApi.GetDownloadFilePath(downloadId);
            var url = $"{apiClient.BaseUrl}{downloadPath}";

            var httpClient = App.Services.GetRequiredService<HttpClient>();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (apiClient.AccessToken != null)
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiClient.AccessToken);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 51. Sorting in place corrupts the order after each page load.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/FavoritesPage.xaml.cs:82-102`
- **CodeRabbit ID:** `0bcd3312-88db-4665-b6d3-707f6bd1a77f`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Sorting in place corrupts the order after each page load.**

`ApplySort` rewrites `ViewModel.Items`. `ContentScrollViewer_ViewChanged` calls it again after every `LoadMoreCommand`. For the `added_at` branch, the descending case reverses the already reversed list plus the newly appended page, so items from page 1 and page 2 interleave in an order that matches neither ascending nor descending. The ascending case relies on the server order, which no longer holds once a reversal has been applied.

Keep the server order in `ViewModel.Items` and bind `PosterRepeater` to a separate sorted projection, or request the sort from the server.

#### Requested remediation

In @src/SiloPlayer/Views/FavoritesPage.xaml.cs around lines 82 - 102, Update ApplySort and the related PosterRepeater binding so ViewModel.Items remains in original server/page-load order and is never cleared, reversed, or otherwise rewritten. Expose and bind a separate sorted projection for display, refreshing it after sorting or LoadMoreCommand completes; ensure the added_at ascending and descending views are derived from the preserved server order.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 52. Remove the duplicate `SplitPlayButton.SizeChanged` subscriptions.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/ItemDetailPage.xaml.cs:5467-5472`
- **CodeRabbit ID:** `4ecd0e25-24c2-4fd6-8eee-e8d63e439962`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Remove the duplicate `SplitPlayButton.SizeChanged` subscriptions.**

`UpdatePlayButtonFromItemData` (Line 5471) and `UpdatePlayButton` (Line 5787) both add `OnSplitPlayButtonSizeChanged` without ever removing it. `UpdateUI` calls `UpdatePlayButtonFromItemData` on every reload, including the two-second polling loop in `TranslateOverviewAsync` and every metadata or watched-state refresh. The handler list therefore grows without bound for the lifetime of the cached page, and each resize invokes it many times. Detach before attaching, or set the width directly.





<details>
<summary>🔒️ Proposed fix</summary>

```diff
             if (userData.DurationSeconds > 0)
             {
                 var fraction = userData.PositionSeconds / userData.DurationSeconds;
                 _playProgressFraction = Math.Min(fraction, 1.0);
-                SplitPlayButton.SizeChanged += OnSplitPlayButtonSizeChanged;
+                SplitPlayButton.SizeChanged -= OnSplitPlayButtonSizeChanged;
+                SplitPlayButton.SizeChanged += OnSplitPlayButtonSizeChanged;
+                UpdatePlayProgressWidth();
             }
```

```diff
             if (isResuming && userData?.DurationSeconds > 0)
             {
                 var fraction = userData.PositionSeconds!.Value / userData.DurationSeconds.Value;
-                // We need to measure the split button width; use a reasonable estimate
-                // The actual width will be set after layout
-                SplitPlayButton.SizeChanged += OnSplitPlayButtonSizeChanged;
                 _playProgressFraction = Math.Min(fraction, 1.0);
+                SplitPlayButton.SizeChanged -= OnSplitPlayButtonSizeChanged;
+                SplitPlayButton.SizeChanged += OnSplitPlayButtonSizeChanged;
+                UpdatePlayProgressWidth();
             }
```
</details>


Also applies to: 5782-5789

#### Requested remediation

In @src/SiloPlayer/Views/ItemDetailPage.xaml.cs around lines 5467 - 5472, Prevent duplicate OnSplitPlayButtonSizeChanged subscriptions in UpdatePlayButtonFromItemData and UpdatePlayButton by detaching the handler before attaching it, or otherwise setting the button width directly. Preserve the existing progress calculation and ensure repeated UpdateUI refreshes leave only one effective resize handler.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 53. Close the `WebView2` control after the dialog closes.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/LoginPage.xaml.cs:127-227`
- **CodeRabbit ID:** `54fb0a83-41be-469a-9cf2-ca1c080ebc82`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Close the `WebView2` control after the dialog closes.**

`ShowOAuthDialogAsync` creates a `WebView2` on every OAuth attempt and never calls `Close()`. Each control keeps a separate WebView2 browser process and its user-data resources alive until the process exits. Repeated sign-in attempts accumulate those processes.





<details>
<summary>🔒️ Proposed fix</summary>

```diff
         webView.Source = authorizeUri;
-        await dialog.ShowAsync();
+        try
+        {
+            await dialog.ShowAsync();
+        }
+        finally
+        {
+            content.Children.Remove(webView);
+            webView.Close();
+        }
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/LoginPage.xaml.cs around lines 127 - 227, Update ShowOAuthDialogAsync to close the WebView2 instance when the ContentDialog closes, alongside cancelling lifetimeCts, ensuring cleanup occurs for every OAuth attempt.

#### CodeRabbit suggestion

```text
private async Task ShowOAuthDialogAsync(AuthProvider provider, Uri authorizeUri)
    {
        using var lifetimeCts = new CancellationTokenSource();
        var webView = new WebView2
        {
            Width = 840,
            Height = 620,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var progress = new ProgressRing
        {
            Width = 18,
            Height = 18,
            IsActive = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusText = new TextBlock
        {
            Text = $"Opening {provider.DisplayName}...",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var status = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Visibility = Visibility.Visible
        };
        status.Children.Add(progress);
        status.Children.Add(statusText);
        var errorText = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(status);
        content.Children.Add(errorText);
        content.Children.Add(webView);

        var dialog = new ContentDialog
        {
            Title = $"Sign in with {provider.DisplayName}",
            Content = content,
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close,
        };

        var completing = false;
        webView.NavigationStarting += async (_, args) =>
        {
            if (completing || !TryGetOAuthCompletionCode(args.Uri, out var completionCode))
                return;

            args.Cancel = true;
            completing = true;
            progress.IsActive = true;
            statusText.Text = "Completing sign-in...";
            status.Visibility = Visibility.Visible;
            errorText.Visibility = Visibility.Collapsed;
            try
            {
                await ViewModel.CompleteOAuthAsync(completionCode, lifetimeCts.Token);
                dialog.Hide();
            }
            catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
            {
                // The user closed the sign-in window while completion was pending.
            }
            catch (Exception ex)
            {
                errorText.Text = ViewModel.ErrorMessage ?? $"OAuth sign-in failed: {ex.Message}";
                errorText.Visibility = Visibility.Visible;
                status.Visibility = Visibility.Collapsed;
                completing = false;
            }
        };
        webView.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess)
            {
                var message = $"OAuth page failed to load: {args.WebErrorStatus}";
                ViewModel.ErrorMessage = message;
                errorText.Text = message;
                errorText.Visibility = Visibility.Visible;
            }

            if (!completing)
            {
                progress.IsActive = false;
                status.Visibility = Visibility.Collapsed;
            }
        };
        dialog.Closed += (_, _) => lifetimeCts.Cancel();

        webView.Source = authorizeUri;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            content.Children.Remove(webView);
            webView.Close();
        }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 54. Validate the origin of the OAuth completion URL.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/LoginPage.xaml.cs:229-249`
- **CodeRabbit ID:** `7cc7dbed-ded7-4ccb-96be-9f0f8be741c0`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Validate the origin of the OAuth completion URL.**

`TryGetOAuthCompletionCode` accepts any absolute URI whose path ends with `/login/oauth-complete`. The hosted OAuth page can redirect the WebView2 control to an arbitrary host. A page under attacker control can therefore navigate to `https://attacker.example/login/oauth-complete?code=...` and make the client submit an attacker-supplied completion code to `ViewModel.CompleteOAuthAsync`. Compare the URI origin with the configured server URL before you accept the code.





<details>
<summary>🔒️ Proposed fix</summary>

```diff
-    private static bool TryGetOAuthCompletionCode(string uriText, out string code)
+    private bool TryGetOAuthCompletionCode(string uriText, out string code)
     {
         code = "";
         if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
             return false;
+        if (!Uri.TryCreate(ViewModel.ServerUrl, UriKind.Absolute, out var serverUri) ||
+            !string.Equals(uri.Scheme, serverUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
+            !string.Equals(uri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase) ||
+            uri.Port != serverUri.Port)
+            return false;
         if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/login/oauth-complete", StringComparison.OrdinalIgnoreCase))
             return false;
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/LoginPage.xaml.cs around lines 229 - 249, Update TryGetOAuthCompletionCode to validate that the parsed URI’s origin matches the configured server URL before extracting or accepting the OAuth code. Preserve the existing path and query validation, and reject completion URLs from any other host or origin.

#### CodeRabbit suggestion

```text
private bool TryGetOAuthCompletionCode(string uriText, out string code)
    {
        code = "";
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            return false;
        if (!Uri.TryCreate(ViewModel.ServerUrl, UriKind.Absolute, out var serverUri) ||
            !string.Equals(uri.Scheme, serverUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != serverUri.Port)
            return false;
        if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/login/oauth-complete", StringComparison.OrdinalIgnoreCase))
            return false;

        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length != 2 || !string.Equals(Uri.UnescapeDataString(pieces[0]), "code", StringComparison.Ordinal))
                continue;

            code = Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
            return !string.IsNullOrWhiteSpace(code);
        }

        return false;
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 55. Align the `ItemsWrapGrid` cell size with the item template size.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/RequestBrowsePage.xaml:42-46`
- **CodeRabbit ID:** `01385f4a-e332-496f-adbd-053177c8cf74`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Align the `ItemsWrapGrid` cell size with the item template size.**

`ItemsWrapGrid` allocates a fixed cell of `ItemWidth="164"` and `ItemHeight="312"`. The item template requests more space: the root `Grid` sets `Width="184"` plus `Margin="6"` on each side, which is 196 effective width. The row heights are 276 plus the title row plus the meta row, which exceeds 312. The cards are clipped or overlap the neighboring cells.

Set the cell size to the real item size, or remove the fixed `Width` and `Margin` from the template.

<details>
<summary>♻️ Proposed fix</summary>

```diff
-            <GridView.ItemsPanel><ItemsPanelTemplate><ItemsWrapGrid Orientation="Horizontal" ItemWidth="164" ItemHeight="312" /></ItemsPanelTemplate></GridView.ItemsPanel>
+            <GridView.ItemsPanel><ItemsPanelTemplate><ItemsWrapGrid Orientation="Horizontal" ItemWidth="196" ItemHeight="344" /></ItemsPanelTemplate></GridView.ItemsPanel>
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/RequestBrowsePage.xaml around lines 42 - 46, Align the ItemsWrapGrid cell dimensions with the RequestMediaResult item template by updating ItemWidth and ItemHeight to accommodate the root Grid’s width, margins, and all row heights, or remove the conflicting fixed Grid Width and Margin. Ensure cards are neither clipped nor overlapping adjacent cells.

#### CodeRabbit suggestion

```text
<GridView.ItemsPanel><ItemsPanelTemplate><ItemsWrapGrid Orientation="Horizontal" ItemWidth="196" ItemHeight="344" /></ItemsPanelTemplate></GridView.ItemsPanel>
            <GridView.ItemTemplate>
                <DataTemplate x:DataType="requests:RequestMediaResult">
                    <Grid Width="184" Margin="6" PointerEntered="ResultCard_PointerEntered" PointerExited="ResultCard_PointerExited">
                        <Grid.RowDefinitions><RowDefinition Height="276" /><RowDefinition Height="Auto" /><RowDefinition Height="Auto" /></Grid.RowDefinitions>
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 56. Use `Mode=OneWay` for the request state bindings.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/RequestBrowsePage.xaml:55-70`
- **CodeRabbit ID:** `1b400320-1f74-46c8-bc03-6866ce28fc7b`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Use `Mode=OneWay` for the request state bindings.**

`x:Bind` defaults to `Mode=OneTime`. `Request.Requestable` and `RequestLabel` control which control is visible and what the button label says. After the user clicks `Request_Click`, the card keeps the old label and the old visibility until the whole item is replaced in the collection. Add `Mode=OneWay` to these bindings, and confirm that `RequestMediaResult` raises `PropertyChanged` for `RequestLabel` and for the `Request` state.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-                            <Border x:Name="StatusRibbon" Visibility="{x:Bind Request.Requestable, Converter={StaticResource InverseBoolToVis}}"
+                            <Border x:Name="StatusRibbon" Visibility="{x:Bind Request.Requestable, Mode=OneWay, Converter={StaticResource InverseBoolToVis}}"
                                     HorizontalAlignment="Right" VerticalAlignment="Top" Margin="8"
                                     Padding="9,4" CornerRadius="14" BorderThickness="1">
                                 <StackPanel Orientation="Horizontal" Spacing="6">
                                     <Ellipse x:Name="StatusDot" Width="6" Height="6" VerticalAlignment="Center" />
-                                    <TextBlock x:Name="StatusRibbonText" Text="{x:Bind RequestLabel}" FontSize="10" FontWeight="SemiBold"
+                                    <TextBlock x:Name="StatusRibbonText" Text="{x:Bind RequestLabel, Mode=OneWay}" FontSize="10" FontWeight="SemiBold"
                                                CharacterSpacing="60" VerticalAlignment="Center" />
                                 </StackPanel>
                             </Border>
-                            <Button x:Name="InlineRequestButton" Content="{x:Bind RequestLabel}" Tag="{x:Bind}"
+                            <Button x:Name="InlineRequestButton" Content="{x:Bind RequestLabel, Mode=OneWay}" Tag="{x:Bind}"
                                     Click="Request_Click" Tapped="InlineAction_Tapped"
                                     GotFocus="InlineRequest_GotFocus" LostFocus="InlineRequest_LostFocus"
-                                    Visibility="{x:Bind Request.Requestable, Converter={StaticResource BoolToVis}}"
+                                    Visibility="{x:Bind Request.Requestable, Mode=OneWay, Converter={StaticResource BoolToVis}}"
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/RequestBrowsePage.xaml around lines 55 - 70, Update the x:Bind expressions for Request.Requestable and RequestLabel in StatusRibbon and InlineRequestButton to use Mode=OneWay, and ensure RequestMediaResult raises PropertyChanged for RequestLabel and the Request state so visibility and labels refresh after Request_Click without replacing the collection item.

#### CodeRabbit suggestion

```text
<Border x:Name="StatusRibbon" Visibility="{x:Bind Request.Requestable, Mode=OneWay, Converter={StaticResource InverseBoolToVis}}"
                                    HorizontalAlignment="Right" VerticalAlignment="Top" Margin="8"
                                    Padding="9,4" CornerRadius="14" BorderThickness="1">
                                <StackPanel Orientation="Horizontal" Spacing="6">
                                    <Ellipse x:Name="StatusDot" Width="6" Height="6" VerticalAlignment="Center" />
                                    <TextBlock x:Name="StatusRibbonText" Text="{x:Bind RequestLabel, Mode=OneWay}" FontSize="10" FontWeight="SemiBold"
                                               CharacterSpacing="60" VerticalAlignment="Center" />
                                </StackPanel>
                            </Border>
                            <Button x:Name="InlineRequestButton" Content="{x:Bind RequestLabel, Mode=OneWay}" Tag="{x:Bind}"
                                    Click="Request_Click" Tapped="InlineAction_Tapped"
                                    GotFocus="InlineRequest_GotFocus" LostFocus="InlineRequest_LostFocus"
                                    Visibility="{x:Bind Request.Requestable, Mode=OneWay, Converter={StaticResource BoolToVis}}"
                                    HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,12"
                                    Padding="14,6" CornerRadius="16" Opacity="0"
                                    AutomationProperties.Name="Request title" />
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 57. Implement sorting for `addedat`.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/WatchlistPage.xaml.cs:86-94`
- **CodeRabbit ID:** `c1de56c3-d695-4bf3-8d9e-9ab718e7d6dc`
- **Fingerprint:** `phantom:poseidon:tapir`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Implement sorting for `added_at`.**

`SortComboBox_SelectionChanged` sets `_sortField` to `added_at`. This switch has no `added_at` branch. Selecting “Date Added” therefore sorts by `Title`.

Add an `added_at` branch that orders by the `MediaItem` date-added field.

#### Requested remediation

In @src/SiloPlayer/Views/WatchlistPage.xaml.cs around lines 86 - 94, Add an "added_at" branch to the sorting switch in SortComboBox_SelectionChanged, ordering ViewModel.Items by the MediaItem date-added field in ascending or descending order according to _ascending; preserve the existing year and title sorting behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 58. Remove the reference to the legacy Continuum repository.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/WatchTogetherJoinPage.xaml.cs:9-19`
- **CodeRabbit ID:** `1c072b6d-aad5-4d23-9293-57926b837d1e`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Remove the reference to the legacy Continuum repository.**

Line 11 cites `continuum-server/web/src/pages/WatchTogetherJoin.tsx`. The coding guidelines forbid citing the legacy private GitLab Continuum repository for Silo desktop parity work. Point the comment at the corresponding page in the public `Silo-Server/silo-server` WebUI source instead, and record the commit used for the parity pass.

As per coding guidelines: "Never fetch, inspect, compare against, cite, or use the legacy private GitLab Continuum repository for Silo desktop parity work."




<details>
<summary>🐛 Proposed edit</summary>

```diff
-/// Shadow of continuum-server/web/src/pages/WatchTogetherJoin.tsx.
+/// Mirrors the Watch Party join page in the Silo WebUI
+/// (github.com/Silo-Server/silo-server, main @ &lt;commit&gt;).
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/WatchTogetherJoinPage.xaml.cs around lines 9 - 19, Update the XML documentation for the watch-together join page to remove the legacy Continuum repository reference, replacing it with the corresponding public Silo-Server WebUI page and the commit identifier used for parity. Do not retain or add any references to the private Continuum source.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 59. Do not set per-item visibility from the `Loaded` event inside an `ItemsRepeater`.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/WatchTogetherRoomPage.xaml:315-321`
- **CodeRabbit ID:** `1c9a467b-4181-4fc4-8e6e-f542581e3ef9`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Do not set per-item visibility from the `Loaded` event inside an `ItemsRepeater`.**

`ItemsRepeater` recycles element containers. `Loaded` runs when the element enters the tree, not on each data change. After recycling, `SuggestionDelete` and `SuggestionPromote` keep the visibility computed for the previous item, so a user can see a delete button for a suggestion owned by another profile. Bind visibility to a per-item property, for example `CanDelete` and `CanPromote` on `WatchTogetherSuggestion`, and set those values when the view model builds the list.

#### Requested remediation

In @src/SiloPlayer/Views/WatchTogetherRoomPage.xaml around lines 315 - 321, Replace the Loaded-based visibility handling for the SuggestionDelete and SuggestionPromote buttons with bindings to per-item properties such as CanDelete and CanPromote on WatchTogetherSuggestion. Populate those properties when the view model builds the suggestions list, preserving the ownership and promotion rules so recycled ItemsRepeater elements always reflect the current item.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 60. Pin and verify the Windows App SDK bootstrapper.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `installer/build.ps1:79-85`
- **CodeRabbit ID:** `ce354cb8-968e-41dc-babe-8b89200706a3`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Pin and verify the Windows App SDK bootstrapper.**

The `1.8.250515000` URL is not available and redirects to a Bing error page. Use an exact, publicly released Windows App SDK 1.8 version. Add a SHA-256 check and validate cached copies before installing the executable. Do not use `1.8/latest` for reproducible release builds.

#### Requested remediation

In @installer/build.ps1 around lines 79 - 85, Update the Windows App SDK bootstrapper setup around $WinAppSdkInstaller to download from an exact publicly released Windows App SDK 1.8 version instead of the floating 1.8/latest URL. Add SHA-256 verification for the downloaded executable and validate any existing cached copy before it is used, replacing or re-downloading it when the hash does not match.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 61. Check the Windows App SDK runtime installer exit code.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `installer/SiloInstaller.iss:69-74`
- **CodeRabbit ID:** `e27c8163-0994-4e9e-bba5-4dd9c3d40505`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Check the Windows App SDK runtime installer exit code.**

The `[Run]` entry starts `windowsappruntimeinstall-x64.exe` and waits for it. Inno Setup ignores the process exit code for a `[Run]` entry. If the runtime installation fails, for example on an offline machine or under an MSIX deployment policy, the installer still reports success. Line 74 then launches `SiloPlayer.exe`, and the WinUI 3 application fails to start because the Windows App Runtime is absent. The user sees an unexplained launch failure instead of an install error.

Run the bootstrapper from `[Code]` and stop the installation when it returns a failure code.

<details>
<summary>🛡️ Proposed fix to fail the install when the runtime bootstrapper fails</summary>

```diff
 [Run]
-; Install Windows App SDK runtime (--quiet suppresses UI, --force skips if already installed)
-Filename: "{tmp}\windowsappruntimeinstall-x64.exe"; Parameters: "--quiet --force"; StatusMsg: "Installing Windows App SDK runtime (this may take a moment)..."; Flags: waituntilterminated
-
 ; Launch app after install
 Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
+
+[Code]
+function PrepareToInstall(var NeedsRestart: Boolean): String;
+begin
+  Result := '';
+end;
+
+procedure CurStepChanged(CurStep: TSetupStep);
+var
+  ResultCode: Integer;
+begin
+  if CurStep = ssPostInstall then
+  begin
+    if not Exec(ExpandConstant('{tmp}\windowsappruntimeinstall-x64.exe'),
+                '--quiet --force', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
+      RaiseException('The Windows App SDK runtime installer could not be started.');
+    if ResultCode <> 0 then
+      RaiseException(Format(
+        'The Windows App SDK runtime installer failed with code %d. %s cannot start without it.',
+        [ResultCode, '{#MyAppName}']));
+  end;
+end;
```

Keep the `deleteafterinstall` flag on the `[Files]` entry so the bootstrapper is still present at `ssPostInstall`.
</details>

#### Requested remediation

In @installer/SiloInstaller.iss around lines 69 - 74, Move execution of windowsappruntimeinstall-x64.exe from the [Run] section into [Code], invoking it during installation with wait behavior and checking its exit code; abort the installation with a clear error when it returns failure, while preserving the existing quiet/force parameters. Keep deleteafterinstall on the corresponding [Files] entry so the bootstrapper remains available at ssPostInstall, and retain the application launch only after successful runtime installation.

#### CodeRabbit suggestion

```text
[Run]
; Launch app after install
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if not Exec(ExpandConstant('{tmp}\windowsappruntimeinstall-x64.exe'),
                '--quiet --force', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('The Windows App SDK runtime installer could not be started.');
    if ResultCode <> 0 then
      RaiseException(Format(
        'The Windows App SDK runtime installer failed with code %d. %s cannot start without it.',
        [ResultCode, '{#MyAppName}']));
  end;
end;
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 62. This script targets the legacy Continuum repository and conflicts with the parity source rule.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `scripts/audit-bump.ps1:1-17`
- **CodeRabbit ID:** `faa01ec1-5efb-4ebd-b2d1-d3d136041174`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**This script targets the legacy Continuum repository and conflicts with the parity source rule.**

The header and `-ServerPath` default point the whole workflow at a local `continuum-server` clone. `scripts/audit-scan.ps1` Line 35 confirms the source is `gitlab.zenterprise.org/quick/continuum`. The coding guidelines forbid using that repository for Silo desktop parity work and require fetching the current GitHub `main` branch and recording the exact commit used.

Retarget `-ServerPath` and the baseline fields to the GitHub `main` clone, or remove these audit scripts.

As per coding guidelines: "Never fetch, inspect, compare against, cite, or use the legacy private GitLab Continuum repository for Silo desktop parity work." and "Before each parity pass, fetch the current GitHub `main` branch directly and record the exact commit used."

#### Requested remediation

In @scripts/audit-bump.ps1 around lines 1 - 17, Update the audit-bump workflow around the ServerPath parameter to use a local clone of the GitHub main branch rather than the legacy private repository. Ensure the baseline update records the exact fetched commit used for the parity pass, and align related baseline fields and audit-source handling with that GitHub source; otherwise remove the legacy-targeting audit scripts.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 63. `git` failures inside `Push-Location`/`finally` are not detected.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `scripts/audit-bump.ps1:35-43`
- **CodeRabbit ID:** `d633449c-bbf3-410c-be81-5b8671c923ef`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**`git` failures inside `Push-Location`/`finally` are not detected.**

`$ErrorActionPreference = "Stop"` does not stop on a native executable's non-zero exit code. If `git rev-parse HEAD` fails, `$headSha` becomes empty and Line 48 writes an empty `continuum_server_sha` into the baseline. The next `audit-scan.ps1` run then fails at its baseline check.

Check `$LASTEXITCODE` after each `git` call, as `audit-scan.ps1` Line 60 already does.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
 Push-Location $ServerPath
 try {
     $headSha = (git rev-parse HEAD).Trim()
+    if ($LASTEXITCODE -ne 0 -or -not $headSha) {
+        throw "git rev-parse HEAD failed in '$ServerPath'."
+    }
     $headShort = (git rev-parse --short HEAD).Trim()
     $headMsg = (git log -1 --format="%s" HEAD).Trim()
 }
```
</details>

#### Requested remediation

In @scripts/audit-bump.ps1 around lines 35 - 43, Update the git command handling in the Push-Location/finally block to validate $LASTEXITCODE after each rev-parse and git log invocation, matching the established audit-scan.ps1 behavior; stop before assigning or writing an empty baseline value when any command fails.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 64. An invalid `lastverifiedsha` makes a page report as UNCHANGED.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `scripts/audit-delta.ps1:114-127`
- **CodeRabbit ID:** `79345bfb-f43a-4df1-932b-1412e1b17127`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**An invalid `last_verified_sha` makes a page report as UNCHANGED.**

Line 122 redirects `git diff` errors to `$null` and only inspects stdout. If `$lastSha` does not exist in the repo, for example after a force-push or a truncated SHA, `git` writes to stderr and `$diffStat` stays empty. The entry then lands in `$skipped` and the report tells the operator to skip a page that was never actually compared.

Validate the SHA once per entry, and fail the entry loudly if it is unresolvable.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
         $changedFiles = @()
         Push-Location $ServerRepo
+        $null = git cat-file -e "$lastSha^{commit}" 2>$null
+        if ($LASTEXITCODE -ne 0) {
+            Pop-Location
+            $unaudited += [PSCustomObject]@{
+                Section = $section; WebPath = $webPath
+                Desktop = ($entry.desktop_files -join ", ")
+                Reason  = "Baseline SHA $lastSha not found in repo"
+            }
+            continue
+        }
         foreach ($f in $filesToCheck) {
```
</details>

#### Requested remediation

In @scripts/audit-delta.ps1 around lines 114 - 127, Update the foreach loop over $filesToCheck to validate $lastSha before running git diff, and treat an unresolvable SHA as an explicit failed entry rather than an unchanged or skipped file. Remove the suppressed-error behavior around git diff, surface the validation failure, and preserve normal changed-file detection when the SHA resolves.

#### CodeRabbit suggestion

```text
$changedFiles = @()
        Push-Location $ServerRepo
        $null = git cat-file -e "$lastSha^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) {
            Pop-Location
            $unaudited += [PSCustomObject]@{
                Section = $section; WebPath = $webPath
                Desktop = ($entry.desktop_files -join ", ")
                Reason  = "Baseline SHA $lastSha not found in repo"
            }
            continue
        }
        foreach ($f in $filesToCheck) {
            # Skip directory entries (trailing /)
            if ($f.EndsWith("/")) { continue }
            # Check if file exists in the repo
            $fullPath = Join-Path $ServerRepo ($f -replace '/', '\')
            if (-not (Test-Path $fullPath)) { continue }

            $diffStat = git diff --stat "$lastSha..HEAD" -- $f 2>$null
            if ($diffStat) {
                $changedFiles += $f
            }
        }
        Pop-Location
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 65. Use `keydown` and `keyup` for mouse-button state.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer.Player/MpvPlayer.cs:683-696`
- **CodeRabbit ID:** `f6d1fb6f-c575-4280-9d62-9610e3246a4e`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Use `keydown` and `keyup` for mouse-button state.**

The `mouse` command accepts only `single` or `double` as its mode. `"press"` and `"release"` are invalid, and `btnName` is unused. Send `mouse x y` to update the position, then send `keydown` or `keyup` with `btnName`.

#### Requested remediation

In @src/SiloPlayer.Player/MpvPlayer.cs around lines 683 - 696, Update SendMouseButton to send the mouse position with the mouse command, then issue keydown when isDown is true or keyup otherwise using the computed btnName; remove the invalid press/release mode and ensure btnName is no longer unused.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 66. The redaction regex misses presigned S3 signature parameters.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer.Player/MpvPlayer.cs:861-871`
- **CodeRabbit ID:** `b03606e7-ccb9-47ad-ab67-079aa1ccb35d`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**The redaction regex misses presigned S3 signature parameters.**

`Command` logs the full argument list on failure at Line 827, and `LoadFile` passes the media URL. API responses deliver images and media as presigned S3 URLs, whose query carries `X-Amz-Signature`, `X-Amz-Credential`, and `X-Amz-Security-Token`. None of those keys appear in the pattern, so a failed `loadfile` writes usable signed credentials into the error sink.

Add the AWS SigV4 parameter names to the key alternation.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
         return Regex.Replace(
             arg,
-            @"(?<key>[?&](?:access_token|refresh_token|profile_token|room_token|token|jwt|password|secret|api_key|apikey|key)=)[^&\s]+",
+            @"(?<key>[?&](?:access_token|refresh_token|profile_token|room_token|token|jwt|password|secret|api_key|apikey|key|X-Amz-Signature|X-Amz-Credential|X-Amz-Security-Token|Signature|AWSAccessKeyId)=)[^&\s]+",
             "${key}<redacted>",
             RegexOptions.IgnoreCase);
```
</details>

#### Requested remediation

In @src/SiloPlayer.Player/MpvPlayer.cs around lines 861 - 871, Update RedactCommandArgument to include X-Amz-Signature, X-Amz-Credential, and X-Amz-Security-Token in the query-parameter key alternation, preserving the existing case-insensitive redaction behavior.

#### CodeRabbit suggestion

```text
private static string RedactCommandArgument(string arg)
    {
        if (arg.StartsWith("Authorization: Bearer ", StringComparison.OrdinalIgnoreCase))
            return "Authorization: Bearer <redacted>";

        return Regex.Replace(
            arg,
            @"(?<key>[?&](?:access_token|refresh_token|profile_token|room_token|token|jwt|password|secret|api_key|apikey|key|X-Amz-Signature|X-Amz-Credential|X-Amz-Security-Token|Signature|AWSAccessKeyId)=)[^&\s]+",
            "${key}<redacted>",
            RegexOptions.IgnoreCase);
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 67. Dispose continues after a failed thread join and can free resources the render thread still uses.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer.Player/MpvPlayer.cs:1168-1214`
- **CodeRabbit ID:** `8105fc65-0f90-46aa-b3f9-a210c8c55932`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Dispose continues after a failed thread join and can free resources the render thread still uses.**

`_renderThread.Join(TimeSpan.FromSeconds(2))` at Line 1173 returns `false` on timeout, but the return value is discarded. Dispose then frees the render context at Line 1178 and disposes `_frameUpdateEvent` at Line 1214.

A still-running `RenderLoop` reaches `_frameUpdateEvent.Wait(100)` at Line 478 and throws `ObjectDisposedException` on a background thread, or passes the `_renderCtx` check at Line 481 and calls `mpv_render_context_render` on a freed context at Line 526. Both terminate the process during shutdown.

Skip the native teardown when the join times out.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
-        // Wait for the render thread to finish
-        if (_renderThread is not null && _renderThread.IsAlive)
-            _renderThread.Join(TimeSpan.FromSeconds(2));
+        // Wait for the render thread to finish. If it does not stop, leak the
+        // native context rather than free memory the thread is still reading.
+        bool renderThreadStopped = _renderThread is null
+            || !_renderThread.IsAlive
+            || _renderThread.Join(TimeSpan.FromSeconds(5));

         // Free the render context before terminating mpv
-        if (_renderCtx != IntPtr.Zero)
+        if (renderThreadStopped && _renderCtx != IntPtr.Zero)
         {
             mpv_render_context_free(_renderCtx);
             _renderCtx = IntPtr.Zero;
         }
```

```diff
-        _frameUpdateEvent.Dispose();
+        if (renderThreadStopped)
+            _frameUpdateEvent.Dispose();
```
</details>

#### Requested remediation

In @src/SiloPlayer.Player/MpvPlayer.cs around lines 1168 - 1214, Update Dispose so it checks the boolean result of joining _renderThread and skips all native/resource teardown, including _renderCtx cleanup and _frameUpdateEvent.Dispose, when the render thread does not finish within the timeout. Continue the existing teardown only after RenderLoop has stopped, while preserving normal cleanup when the join succeeds or the thread is already inactive.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 68. The activation pipe accepts connections from any local caller.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer/App.xaml.cs:147-159`
- **CodeRabbit ID:** `5732734e-205a-44d2-a327-c5bbae95f26f`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**The activation pipe accepts connections from any local caller.**

`NamedPipeServerStream` is created without `PipeOptions.CurrentUserOnly` and without an explicit `PipeSecurity`. Any process on the machine, and a remote caller reaching `\\host\pipe\SiloDesktopPlayer-Activation-6F4EE0EA`, can connect and send a payload. Line 159 forwards that payload straight into `ActivateFromArgument`, which drives deep-link navigation.

Restrict the pipe to the current user on both the server and the client.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
                 await using var pipe = new NamedPipeServerStream(
                     ActivationPipeName,
                     PipeDirection.In,
                     1,
                     PipeTransmissionMode.Byte,
-                    PipeOptions.Asynchronous);
+                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
```

```diff
             using var pipe = new NamedPipeClientStream(
-                ".", ActivationPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
+                ".", ActivationPipeName, PipeDirection.Out,
+                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
```
</details>

#### Requested remediation

In @src/SiloPlayer/App.xaml.cs around lines 147 - 159, Update the activation pipe setup in the NamedPipeServerStream call and its client connection logic to restrict access to the current user, using PipeOptions.CurrentUserOnly or equivalent explicit PipeSecurity on both sides. Preserve the existing WaitForConnectionAsync and ActivateFromArgument flow after enforcing this restriction.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 69. Cache image data instead of constructing a `BitmapImage` from the presigned URL.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer/Converters/UrlToImageSourceConverter.cs:13-23`
- **CodeRabbit ID:** `73c57388-eceb-42dc-ba10-c0ba5dc36cb6`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Cache image data instead of constructing a `BitmapImage` from the presigned URL.**

Line 22 creates a new URL-backed image whenever a template is realized. After the URL expires, newly realized cards cannot load artwork. Before expiration, repeated realization also causes repeated image fetches.

Use an image-content cache. If cached content is unavailable, re-fetch item detail for a fresh URL, download the image data, and cache that data.

As per coding guidelines, “URLs expire — cache the image data, not the URL. Re-fetch item detail to get fresh URLs if needed.”

#### Requested remediation

In @src/SiloPlayer/Converters/UrlToImageSourceConverter.cs around lines 13 - 23, Update UrlToImageSourceConverter.Convert so it returns cached image content rather than constructing a URL-backed BitmapImage from the presigned URL. Reuse cached data for repeated template realization; when unavailable, re-fetch the item detail for a fresh URL, download the image bytes, cache them, and construct the image from the cached data while preserving the existing invalid-input behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 70. Always notify the view after `Reset`.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer/Helpers/VirtualCatalogItems.cs:59-63`
- **CodeRabbit ID:** `8679d3af-8f9b-4ec5-a121-243623dadb09`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Always notify the view after `Reset`.**

`Reset` clears `_pages`, but `SetCount` returns without a notification when `totalCount` equals `_count`. A refresh with the same total therefore leaves realized cards bound to old `MediaItem` values even though all pages were cleared.

Emit `NotifyCollectionChangedAction.Reset` unconditionally from `Reset`. Emit the `Count` property notification only when the count changes.

<details>
<summary>Proposed fix</summary>

```diff
     public void Reset(int totalCount)
     {
+        totalCount = Math.Max(0, totalCount);
+        var countChanged = _count != totalCount;
         _pages.Clear();
-        SetCount(totalCount);
+        _count = totalCount;
+
+        if (countChanged)
+            OnPropertyChanged(nameof(Count));
+        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
+            NotifyCollectionChangedAction.Reset));
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Helpers/VirtualCatalogItems.cs around lines 59 - 63, Update Reset to always notify the view with a NotifyCollectionChangedAction.Reset event after clearing _pages, even when totalCount equals _count. Keep SetCount responsible for notifying the Count property only when the count changes, avoiding duplicate or unconditional Count notifications.

#### CodeRabbit suggestion

```text
public void Reset(int totalCount)
    {
        totalCount = Math.Max(0, totalCount);
        var countChanged = _count != totalCount;
        _pages.Clear();
        _count = totalCount;

        if (countChanged)
            OnPropertyChanged(nameof(Count));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 71. Add coverage for the proactive refresh schedule.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/AuthServiceTests.cs:12-39`
- **CodeRabbit ID:** `79192aa2-82ff-4581-b9eb-3cfab78186f5`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Add coverage for the proactive refresh schedule.**

This suite covers concurrent refresh, cancellation, rotation, logout races, and impersonation. It does not cover the proactive refresh contract. Every test passes `expires_in` of 86400 and then calls `TryRefreshAsync` directly, so no test proves that `AuthService` schedules a refresh at about 80% of `expires_in`. A regression that removes the timer, or that only refreshes on a 401, would keep this suite green and would break playback.

Add a test that sets a short `expires_in` with an injectable clock or timer and asserts that a refresh starts before expiry without a 401.

As per coding guidelines: "**IMPORTANT**: Proactively refresh the access token before it expires. Do not wait for a 401 during playback — that kills the stream. Refresh when ~80% of `expires_in` has elapsed."

#### Requested remediation

In @tests/SiloPlayer.Tests/AuthServiceTests.cs around lines 12 - 39, Add a focused AuthService test that uses an injectable clock or timer with a short expires_in value, waits until approximately 80% of the lifetime, and verifies TryRefreshAsync starts without any 401 response. Keep the assertion centered on proactive scheduling before token expiry, including the expected refresh call, and avoid directly invoking TryRefreshAsync as the trigger.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 72. Assertions inside the handler delegate can be swallowed by the retry loop.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/DirectStreamRelayTests.cs:96-97`
- **CodeRabbit ID:** `f86687a1-feb6-4c20-ac1d-f212127f41b4`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Assertions inside the handler delegate can be swallowed by the retry loop.**

`SequenceHandler` runs these `Assert` calls on the upstream request path. `DirectStreamRelay` retries failed upstream requests; the test at line 310 proves it treats a handler exception as a transient failure. A failing `Assert` inside the delegate therefore becomes a retry, and a later attempt can still produce the expected bytes. The test then passes with a wrong request.

Record the observed values in the delegate. Assert on `handler.Requests` after `RelayAsync` returns.





Also applies to: 145-146, 308-310, 352-352

#### Requested remediation

In @tests/SiloPlayer.Tests/DirectStreamRelayTests.cs around lines 96 - 97, Update the affected DirectStreamRelay tests to stop asserting inside the SequenceHandler delegate; record the incoming request header values and assert them through handler.Requests after RelayAsync completes, so retry handling cannot swallow assertion failures. Apply this to the cases around the Range/If-Range checks and the other noted handler assertions, preserving their existing expected values.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 73. Assert that both indices are found before you compare them.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/FullscreenStateSyncSourceTests.cs:56-58`
- **CodeRabbit ID:** `23423127-26bc-4b54-8436-bcaf22444f51`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Assert that both indices are found before you compare them.**

`IndexOf` returns `-1` when the marker is missing. If `_switchingContent = true;` disappears from `PlayCoreAsync`, the comparison becomes `-1 < someIndex`, which is true, and the test passes. The same hole exists at Lines 91-93 for `_playerService.IsSwitchingContent`. The guardrail then stops detecting the regression it was written for.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-        var transitionPrefix = service[coreStart..loadingStart];
-        Assert.True(
-            transitionPrefix.IndexOf("_switchingContent = true;", StringComparison.Ordinal) <
-            transitionPrefix.IndexOf("PrepareEpisodeNavigationForContent(contentId);", StringComparison.Ordinal));
+        var transitionPrefix = service[coreStart..loadingStart];
+        var switchIndex = transitionPrefix.IndexOf("_switchingContent = true;", StringComparison.Ordinal);
+        var prepareIndex = transitionPrefix.IndexOf("PrepareEpisodeNavigationForContent(contentId);", StringComparison.Ordinal);
+        Assert.True(switchIndex >= 0);
+        Assert.True(prepareIndex > switchIndex);
```
</details>





Also applies to: 91-93

#### Requested remediation

In @tests/SiloPlayer.Tests/FullscreenStateSyncSourceTests.cs around lines 56 - 58, Update the assertions in the transition-prefix checks around PlayCoreAsync to first verify that each IndexOf result for the required markers is non-negative, then compare their ordering. Apply the same guard to the _playerService.IsSwitchingContent check so either missing marker causes the test to fail.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 74. Remove the 250 ms timing race from the cancellation assertion.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/ImageServiceTests.cs:114-119`
- **CodeRabbit ID:** `8b97dd53-f39c-4352-a046-30c3ff67f78f`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Remove the 250 ms timing race from the cancellation assertion.**

`Assert.Same(first, firstCompletion)` passes only if `first` faults within 250 ms of `firstWaiter.Cancel()`. On a loaded CI agent the `Task.Delay(250)` can win, and the test fails without a real regression. Wait on `first` with a generous timeout instead, and release the content afterwards.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-            firstWaiter.Cancel();
-            var firstCompletion = await Task.WhenAny(first, Task.Delay(250));
-            releaseContent.SetResult();
-
-            Assert.Same(first, firstCompletion);
-            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
+            firstWaiter.Cancel();
+            await Assert.ThrowsAnyAsync<OperationCanceledException>(
+                () => first.WaitAsync(TimeSpan.FromSeconds(5)));
+            releaseContent.SetResult();
```

`WaitAsync` throws `TimeoutException`, not `OperationCanceledException`, so a hang still fails the test.
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/ImageServiceTests.cs around lines 114 - 119, Update the cancellation assertion around firstWaiter, first, and releaseContent to await first with a generous timeout using WaitAsync, then release the content afterward; remove the Task.WhenAny and fixed 250 ms delay race while preserving the OperationCanceledException assertion and allowing TimeoutException to fail hangs.

#### CodeRabbit suggestion

```text
firstWaiter.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => first.WaitAsync(TimeSpan.FromSeconds(5)));
            releaseContent.SetResult();
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 75. Do not make tests depend on the `.codex-tmp` scratch checkout.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/SearchRuntimeRegressionTests.cs:151`
- **CodeRabbit ID:** `1c80b8e2-3f3d-4599-bdbd-feb662f1e882`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Do not make tests depend on the `.codex-tmp` scratch checkout.**

`ReadWebUiFile` resolves `<root>/.codex-tmp/silo-server-current/...`. That directory is a local scratch copy and is not part of the repository. On any clean clone or CI runner, `File.ReadAllText` throws `DirectoryNotFoundException`, so `OptionalDiscoveryAndErrorsRefreshVisibleSearchState` and `DiscoveryResultsMatchTheCurrentWebUiVariants` fail for an environment reason, not a code reason.

Pin the expected WebUI constants inside the test, or skip the test when the directory is absent, and source the WebUI from the public `Silo-Server/silo-server` repository when it is present.

<details>
<summary>🔧 Proposed guard</summary>

```diff
-    private static string ReadWebUiFile(params string[] parts) =>
-        File.ReadAllText(Path.Combine([FindRepositoryRoot(), ".codex-tmp", "silo-server-current", .. parts]));
+    private static bool TryReadWebUiFile(out string content, params string[] parts)
+    {
+        var path = Path.Combine([FindRepositoryRoot(), ".codex-tmp", "silo-server-current", .. parts]);
+        content = File.Exists(path) ? File.ReadAllText(path) : "";
+        return content.Length > 0;
+    }
```
</details>

As per coding guidelines: "The only authoritative Silo server/WebUI source is the public GitHub repository: `https://github.com/Silo-Server/silo-server`."





Also applies to: 205-205, 253-254

#### Requested remediation

In @tests/SiloPlayer.Tests/SearchRuntimeRegressionTests.cs at line 151, Remove the tests’ dependency on ReadWebUiFile resolving through the local .codex-tmp checkout. Update OptionalDiscoveryAndErrorsRefreshVisibleSearchState and DiscoveryResultsMatchTheCurrentWebUiVariants to use pinned expected WebUI constants, or guard the external-source setup so tests skip when the checkout is absent while using the public Silo-Server/silo-server repository when available.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 76. Guard the null `Trigger` before `Equals`.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminAutoscanViewModel.cs:292-300`
- **CodeRabbit ID:** `df51fc83-6445-4806-81bd-43b7b2de0b48`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Guard the null `Trigger` before `Equals`.**

`MapActiveScan` at Line 426 tests `string.IsNullOrWhiteSpace(scan.Trigger)`, so `AdminScanRun.Trigger` can be null. Line 295 calls `scan.Trigger.Equals(...)` directly. A snapshot that contains one scan run with a null `Trigger` throws `NullReferenceException` and drops the whole realtime active-scan update.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         var mapped = scans
-            .Where(scan => scan.Trigger.Equals("autoscan", StringComparison.OrdinalIgnoreCase)
+            .Where(scan => string.Equals(scan.Trigger, "autoscan", StringComparison.OrdinalIgnoreCase)
                 && scan.Status is "accepted" or "queued" or "running")
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminAutoscanViewModel.cs around lines 292 - 300, Update ApplyActiveScanSnapshot so the autoscan trigger comparison is null-safe, preserving the existing case-insensitive match and status filtering; avoid calling Equals directly on scan.Trigger, which may be null.

#### CodeRabbit suggestion

```text
public void ApplyActiveScanSnapshot(IEnumerable<AdminScanRun> scans)
    {
        var mapped = scans
            .Where(scan => string.Equals(scan.Trigger, "autoscan", StringComparison.OrdinalIgnoreCase)
                && scan.Status is "accepted" or "queued" or "running")
            .Select(MapActiveScan)
            .OrderBy(scan => scan.Status == "running" ? 0 : 1)
            .ThenBy(scan => scan.StartedAt ?? DateTimeOffset.MaxValue)
            .ToList();
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 77. Replace the anonymous types in the reorder bodies with dictionaries.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminSectionsViewModel.cs:278-286`
- **CodeRabbit ID:** `1671d4f5-2b6a-4e1b-8c99-17698edd6c54`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Replace the anonymous types in the reorder bodies with dictionaries.**

The comments at Lines 324-328 and Lines 347-348 record that .NET 8 Release publish with trimming strips anonymous type property names, and the body serializes as `{}`. `ReorderSectionsAsync` and `MoveSectionAsync` still build the payload from anonymous types, so section reordering can post an empty body in trimmed Release builds and the new order is not persisted.

<details>
<summary>🐛 Proposed fix</summary>

```diff
     public async Task ReorderSectionsAsync(List<string> orderedIds)
     {
         try
         {
-            var entries = orderedIds.Select((id, index) => new { id, sort_order = index }).ToList();
-            await _adminApi.ReorderSectionsAsync(new { sections = entries });
+            var entries = orderedIds
+                .Select((id, index) => new Dictionary<string, object?> { ["id"] = id, ["sort_order"] = index })
+                .ToList();
+            await _adminApi.ReorderSectionsAsync(new Dictionary<string, object?> { ["sections"] = entries });
         }
         catch (Exception ex) { ErrorMessage = ex.Message; }
     }
```

Apply the same change in `MoveSectionAsync`, or call `ReorderSectionsAsync` from it to remove the duplicated payload code.
</details>





Also applies to: 300-305

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminSectionsViewModel.cs around lines 278 - 286, Replace the anonymous payload entries in ReorderSectionsAsync and MoveSectionAsync with dictionaries using the existing section ID and sort_order keys, or reuse ReorderSectionsAsync from MoveSectionAsync, so serialization preserves property names in trimmed Release builds.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 78. Reset the catalog query when the jump changes the sort.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/LibraryViewModel.cs:312-358`
- **CodeRabbit ID:** `fe05b8f7-0c9d-453d-bd2a-a8cf1233bc33`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Reset the catalog query when the jump changes the sort.**

`JumpToLetterAsync` sets `SelectedSort = "title"` and `SelectedOrder = "asc"` at Lines 317-318, then calls `LoadWindowAsync` at Line 352. For any letter other than `#`, the method does not call `StartNewCatalogQuery`, so `_snapshot`, `TotalCount`, and `_pageResponses` still belong to the previous sort. `FetchWindowAsync` passes that stale `_snapshot` at Line 512, and `BuildLetterOffsetsAsync` probes with `sort: "title"` while `TotalCount` came from the old ordering. If the user was sorted by anything other than title ascending, the jump resolves the offset against one ordering and fetches the window against another, so the grid lands on the wrong items.

The `#` branch already calls `StartNewCatalogQuery`. Apply the same reset whenever the sort or order changes.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-        SelectedSort = "title";
-        SelectedOrder = "asc";
-
-        if (letter == "#")
-        {
-            StartNewCatalogQuery();
-            Items.Clear();
-            await LoadWindowAsync(0, PageSize, force: true);
-            return;
-        }
+        var sortChanged = SelectedSort != "title" || SelectedOrder != "asc";
+        SelectedSort = "title";
+        SelectedOrder = "asc";
+
+        if (letter == "#" || sortChanged)
+        {
+            StartNewCatalogQuery();
+            Items.Clear();
+            if (letter == "#")
+            {
+                await LoadWindowAsync(0, PageSize, force: true);
+                return;
+            }
+        }
```

The letter branch must also rebuild `_letterOffsets` after the reset, because `TotalCount` is zero until the next window response arrives.
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/LibraryViewModel.cs around lines 312 - 358, Update JumpToLetterAsync so changing SelectedSort or SelectedOrder also calls StartNewCatalogQuery before resolving letter offsets or loading the window. Ensure the letter branch rebuilds _letterOffsets after the reset, accounting for TotalCount being zero until the next window response, while preserving the existing # branch behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 79. Handle the admin edit path, or reject it explicitly.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/SmartCollectionWizardViewModel.cs:84-179`
- **CodeRabbit ID:** `38262e20-8be6-4468-9841-bb98d4b4c345`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Handle the admin edit path, or reject it explicitly.**

`SmartCollectionWizardNavigationArgs` allows `IsAdmin = true` together with a non-null `CollectionId`. Two gaps follow from that combination:

- `ConfigureAsync` loads the existing collection only when `!IsAdmin` (Line 139). In admin mode the form stays empty, so the saved definition loses the previous rules, sort, and limit.
- `SaveAsync` always calls `_adminApi.CreateCollectionAsync` when `IsAdmin` is true. An admin edit creates a second collection instead of updating the existing one.

Either load and update the admin collection through the admin API, or fail fast when `IsAdmin` and `CollectionId` are both set.

<details>
<summary>🛠️ Minimal guard until admin edit is implemented</summary>

```diff
         IsAdmin = args?.IsAdmin == true;
         _collectionId = args?.CollectionId;
+        if (IsAdmin && !string.IsNullOrWhiteSpace(_collectionId))
+        {
+            IsLoading = false;
+            ErrorMessage = "Editing an existing admin smart collection is not supported here.";
+            return;
+        }
```
</details>





Also applies to: 279-304

#### Requested remediation

In @src/SiloPlayer/ViewModels/SmartCollectionWizardViewModel.cs around lines 84 - 179, Reject the unsupported admin-edit combination in SmartCollectionWizardViewModel by failing fast when IsAdmin is true and CollectionId is non-null or non-whitespace. Apply the guard in ConfigureAsync and SaveAsync, before loading or creating data, so admin mode cannot silently reset an existing collection or create a duplicate; preserve normal admin creation and non-admin editing behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 80. Wait for the previous run loop before you start a new one.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs:452-475`
- **CodeRabbit ID:** `5d48bcc3-a383-49a4-b025-c1f91408aca3`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Wait for the previous run loop before you start a new one.**

`StopWebSocket` cancels `_wsCts`, clears `_ws`, and discards `_wsRunTask` without awaiting it. The previous loop can still be inside `ConnectAndRunOnceAsync`. It then executes Line 517 and overwrites `_ws` with its own `ClientWebSocket`.

Two consequences follow when `InitializeAsync` runs twice, for example on re-navigation to the same room:

- The socket created by the new loop is dropped without `Dispose`. The connection leaks until finalization.
- Outbound frames from `SendWsMessageAsync` go to the stale socket of the cancelled loop.

`StopWebSocket` also disposes `_wsCts` while the old loop still reads that token, which can raise `ObjectDisposedException` inside the loop.

<details>
<summary>🔒 Proposed fix</summary>

```diff
-    private void StartWebSocket()
+    private void StartWebSocket()
     {
-        StopWebSocket();
+        StopWebSocketAsync().GetAwaiter().GetResult();
         if (string.IsNullOrEmpty(RoomId) || string.IsNullOrEmpty(RoomToken)) return;
         _wsCts = new CancellationTokenSource();
         _wsRunTask = Task.Run(() => WebSocketRunLoopAsync(_wsCts.Token));
     }
+
+    private async Task StopWebSocketAsync()
+    {
+        var cts = _wsCts;
+        var task = _wsRunTask;
+        try { cts?.Cancel(); } catch { }
+        try { _ws?.Abort(); } catch { }
+        if (task != null) { try { await task.ConfigureAwait(false); } catch { } }
+        _ws?.Dispose();
+        _ws = null;
+        _wsRunTask = null;
+        cts?.Dispose();
+        _wsCts = null;
+        ConnectionState = "disconnected";
+    }
```

Prefer an async entry point over a blocking wait if `StartWebSocket` runs on the UI thread. Let the run loop own creation and disposal of its `ClientWebSocket` in a local variable, and publish it to `_ws` only after `ConnectAsync` succeeds.
</details>





Also applies to: 497-519

#### Requested remediation

In @src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs around lines 452 - 475, Make the WebSocket lifecycle asynchronous: have StartWebSocket/StopWebSocket coordinate with and await the prior _wsRunTask before starting a replacement, without disposing its CancellationTokenSource until the loop has finished. Update WebSocketRunLoopAsync and ConnectAndRunOnceAsync so each run owns and disposes its ClientWebSocket locally, publishing it to _ws only after ConnectAsync succeeds and clearing it only if it is still the current socket, preventing stale runs from overwriting or receiving sends through the active socket.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---


## ℹ️ Minor (104)

### 81. Guard the method label against an empty key.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminActivityPage.xaml.cs:392-399`
- **CodeRabbit ID:** `0ea02123-52f4-44d7-b74a-87085e5910da`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Guard the method label against an empty key.**

`char.ToUpper(method[0]) + method[1..]` throws `IndexOutOfRangeException` when `method` is an empty string. `GetMethodBarColor` and `GetMethodDotColor` both keep a default branch for unrecognized keys, so the code already treats the key set as open. An empty `play_method` value from the server therefore crashes the summary strip build on the UI thread.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
-                Text = char.ToUpper(method[0]) + method[1..],
+                Text = string.IsNullOrEmpty(method)
+                    ? "Unknown"
+                    : char.ToUpper(method[0]) + method[1..],
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminActivityPage.xaml.cs around lines 392 - 399, Guard the method label construction in the summary strip before indexing method, so an empty play_method does not throw. Preserve the existing capitalization for non-empty values and use a safe fallback label for empty or unrecognized keys, consistent with GetMethodBarColor and GetMethodDotColor.

#### CodeRabbit suggestion

```text
sp.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(method)
                    ? "Unknown"
                    : char.ToUpper(method[0]) + method[1..],
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 82. The Sources header and row column widths do not match.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminAutoscanPage.xaml:34-39`
- **CodeRabbit ID:** `aa120146-ce60-4599-9562-ff86dcc4ad66`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**The Sources header and row column widths do not match.**

The header grid (Line 34) declares the final columns as `130` and `150`. The row template (Line 39) declares `130` and `56`. The fixed widths differ by 94 px, so the three star columns receive different leftover widths in the header and in the rows. The header labels do not align with the cells below them.

Use the same column definitions in both grids.




<details>
<summary>🎨 Proposed fix</summary>

```diff
-<Grid Padding="16,11" ColumnSpacing="16" Background="{StaticResource SurfaceBrush}"><Grid.ColumnDefinitions><ColumnDefinition Width="2*"/><ColumnDefinition Width="2.7*"/><ColumnDefinition Width="4*"/><ColumnDefinition Width="85"/><ColumnDefinition Width="130"/><ColumnDefinition Width="150"/></Grid.ColumnDefinitions>
+<Grid Padding="16,11" ColumnSpacing="16" Background="{StaticResource SurfaceBrush}"><Grid.ColumnDefinitions><ColumnDefinition Width="2*"/><ColumnDefinition Width="2.7*"/><ColumnDefinition Width="4*"/><ColumnDefinition Width="85"/><ColumnDefinition Width="130"/><ColumnDefinition Width="56"/></Grid.ColumnDefinitions>
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminAutoscanPage.xaml around lines 34 - 39, Update the AutoscanSource row Grid column definitions to match the Sources header Grid exactly, including the final fixed column width of 150 instead of 56, so both grids use identical column widths.

#### CodeRabbit suggestion

```text
<StackPanel><Grid Padding="16,11" ColumnSpacing="16" Background="{StaticResource SurfaceBrush}"><Grid.ColumnDefinitions><ColumnDefinition Width="2*"/><ColumnDefinition Width="2.7*"/><ColumnDefinition Width="4*"/><ColumnDefinition Width="85"/><ColumnDefinition Width="130"/><ColumnDefinition Width="56"/></Grid.ColumnDefinitions><TextBlock Style="{StaticResource TableHeaderText}" Text="Source"/><TextBlock Grid.Column="1" Style="{StaticResource TableHeaderText}" Text="Connection"/><TextBlock Grid.Column="2" Style="{StaticResource TableHeaderText}" Text="Interval &amp; settings"/><TextBlock Grid.Column="3" Style="{StaticResource TableHeaderText}" Text="Enabled"/><TextBlock Grid.Column="4" Style="{StaticResource TableHeaderText}" Text="Last run"/></Grid>
     <ListView ItemsSource="{x:Bind ViewModel.Sources,Mode=OneWay}" SelectionMode="None" Padding="0">
      <ListView.ItemTemplate>
       <DataTemplate x:DataType="admin:AutoscanSource">
        <Grid Padding="16,13" ColumnSpacing="16" BorderBrush="{StaticResource BorderBrush}" BorderThickness="0,1,0,0">
         <Grid.ColumnDefinitions><ColumnDefinition Width="2*"/><ColumnDefinition Width="2.7*"/><ColumnDefinition Width="4*"/><ColumnDefinition Width="85"/><ColumnDefinition Width="130"/><ColumnDefinition Width="56"/></Grid.ColumnDefinitions>
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 83. Align the suggested file name with the registered file type.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminDiagnosticsPage.xaml.cs:343-344`
- **CodeRabbit ID:** `ee197c9c-15f3-4db0-8741-8c425773c09e`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Align the suggested file name with the registered file type.**

`SuggestedFileName` ends with `.tar`. `FileTypeChoices` registers only `.gz`. The picker then produces a name that does not match the bundle format.

Use `.tar.gz` in the suggested name, or register the matching extension.





<details>
<summary>🔧 Proposed fix</summary>

```diff
-        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"silo-diagnostics-{(string.IsNullOrWhiteSpace(report.ShortId) ? report.Id : report.ShortId)}.tar" };
-        picker.FileTypeChoices.Add("Compressed diagnostic bundle", [".gz"]);
+        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"silo-diagnostics-{(string.IsNullOrWhiteSpace(report.ShortId) ? report.Id : report.ShortId)}" };
+        picker.FileTypeChoices.Add("Compressed diagnostic bundle", [".tar.gz"]);
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminDiagnosticsPage.xaml.cs around lines 343 - 344, Update the SuggestedFileName construction in the FileSavePicker setup to use the .tar.gz extension, matching the .gz file type registered by FileTypeChoices while preserving the existing diagnostic identifier logic.

#### CodeRabbit suggestion

```text
var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"silo-diagnostics-{(string.IsNullOrWhiteSpace(report.ShortId) ? report.Id : report.ShortId)}" };
        picker.FileTypeChoices.Add("Compressed diagnostic bundle", [".tar.gz"]);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 84. Re-enable the remove-poster button after the request completes.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminLibrariesPage.xaml.cs:3915-3925`
- **CodeRabbit ID:** `07c84572-2286-44eb-bd12-c40432159ba1`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Re-enable the remove-poster button after the request completes.**

`removePosterBtn.IsEnabled` is set to `false` before the call. No branch sets it back to `true`. After a success or a failure the button stays disabled for the lifetime of the dialog. The neighbouring `pickPosterBtn` handler already uses a `finally` block for this.





<details>
<summary>🔧 Proposed fix</summary>

```diff
                 removePosterBtn.Click += async (_, _) =>
                 {
                     removePosterBtn.IsEnabled = false;
                     try
                     {
                         var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                         await api.DeleteLibraryPosterAsync(editingLib.Id);
                         ShowStatus("Library poster removed.");
                     }
                     catch (Exception ex) { ShowStatus($"Poster removal failed: {ex.Message}"); }
+                    finally { removePosterBtn.IsEnabled = true; }
                 };
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminLibrariesPage.xaml.cs around lines 3915 - 3925, Update the removePosterBtn.Click handler to restore removePosterBtn.IsEnabled in a finally block after DeleteLibraryPosterAsync completes, preserving the existing success and failure status handling.

#### CodeRabbit suggestion

```text
removePosterBtn.Click += async (_, _) =>
                {
                    removePosterBtn.IsEnabled = false;
                    try
                    {
                        var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                        await api.DeleteLibraryPosterAsync(editingLib.Id);
                        ShowStatus("Library poster removed.");
                    }
                    catch (Exception ex) { ShowStatus($"Poster removal failed: {ex.Message}"); }
                    finally { removePosterBtn.IsEnabled = true; }
                };
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 85. Render the empty states after the initial load.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminMaintenancePage.xaml.cs:52-56`
- **CodeRabbit ID:** `a5bbb94c-073a-4fab-ad7f-f5d98a6310be`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Render the empty states after the initial load.**

The three `Rebuild*` calls run only when a collection already contains items. If `LoadCommand` returns zero jobs, no `CollectionChanged` event is raised, so `RebuildImportJobs`, `RebuildExportJobs`, and `RebuildAllJobs` never run. The three cards then stay blank instead of showing "No catalog import jobs yet.", "No catalog export jobs yet.", and "No jobs yet.".

Call the rebuilds after the load completes.




<details>
<summary>🐛 Proposed fix</summary>

```diff
         if (ViewModel.ImportJobs.Count > 0) RebuildImportJobs();
         if (ViewModel.ExportJobs.Count > 0) RebuildExportJobs();
         if (ViewModel.AllJobs.Count > 0) RebuildAllJobs();
         try { await ViewModel.LoadCommand.ExecuteAsync(null); }
         catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }
+        RebuildImportJobs();
+        RebuildExportJobs();
+        RebuildAllJobs();
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminMaintenancePage.xaml.cs around lines 52 - 56, Move the RebuildImportJobs, RebuildExportJobs, and RebuildAllJobs calls to after the awaited ViewModel.LoadCommand.ExecuteAsync(null) completes, and invoke them unconditionally so empty collections render their empty-state messages. Preserve the existing exception handling and error-message behavior.

#### CodeRabbit suggestion

```text
if (ViewModel.ImportJobs.Count > 0) RebuildImportJobs();
        if (ViewModel.ExportJobs.Count > 0) RebuildExportJobs();
        if (ViewModel.AllJobs.Count > 0) RebuildAllJobs();
        try { await ViewModel.LoadCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }
        RebuildImportJobs();
        RebuildExportJobs();
        RebuildAllJobs();
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 86. Render the node empty states after the initial load.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminNodesPage.xaml.cs:34-45`
- **CodeRabbit ID:** `b55dfafd-90b8-42ef-ac72-1691fa17f744`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Render the node empty states after the initial load.**

`RebuildProxyRows` and `RebuildTranscodeRows` run only when a collection already holds items. If `LoadCommand` returns zero nodes, `ProxyNodes` and `TranscodeNodes` raise no `CollectionChanged` event, so `ProxyEmptyState` and `TranscodeEmptyState` stay collapsed and the count badges stay empty. The user then sees a table header with no rows and no guidance.

Call both rebuild methods after the load completes.




<details>
<summary>🐛 Proposed fix</summary>

```diff
         try
         {
             await ViewModel.LoadCommand.ExecuteAsync(null);
         }
         catch (Exception ex)
         {
             ViewModel.ErrorMessage = $"Error: {ex.Message}";
         }
+        RebuildProxyRows();
+        RebuildTranscodeRows();
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminNodesPage.xaml.cs around lines 34 - 45, Call RebuildProxyRows and RebuildTranscodeRows after ViewModel.LoadCommand.ExecuteAsync completes, regardless of collection counts, so empty-state visibility and count badges are initialized when no nodes are loaded. Keep the existing exception handling unchanged.

#### CodeRabbit suggestion

```text
if (ViewModel.ProxyNodes.Count > 0) RebuildProxyRows();
        if (ViewModel.TranscodeNodes.Count > 0) RebuildTranscodeRows();

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }

        RebuildProxyRows();
        RebuildTranscodeRows();
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 87. Correct the installed empty state and clear the stale toolbar state.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminPluginsPage.xaml.cs:220-248`
- **CodeRabbit ID:** `701f5d6c-a8fe-4556-b77d-ce04a9508b14`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Correct the installed empty state and clear the stale toolbar state.**

Two problems exist in `RebuildInstalled`:

1. When the search filter matches nothing, Line 235 shows `InstalledEmpty`, which renders the fixed text "No plugins installed." from `AdminPluginsPage.xaml` Line 149. The user has plugins installed; only the query matched none.
2. The early return at Line 226 leaves `InstalledMatchText` and `InstalledPager` at their previous values. A stale pager stays visible after the last plugin is deleted.

Use a separate no-match message, and reset the toolbar state on the zero-installation path.




<details>
<summary>🐛 Proposed fix</summary>

```diff
         InstalledCards.Children.Clear();
         if (ViewModel.Installations.Count == 0)
         {
             InstalledEmpty.Visibility = Visibility.Visible;
+            InstalledPager.Visibility = Visibility.Collapsed;
+            InstalledMatchText.Text = "";
+            InstalledEmptyText.Text = "No plugins installed.";
             return;
         }
@@
         if (filtered.Count == 0)
         {
+            InstalledEmptyText.Text = "No installed plugins match the search.";
             InstalledEmpty.Visibility = Visibility.Visible;
             InstalledPager.Visibility = Visibility.Collapsed;
             return;
         }
```

Name the text element in `AdminPluginsPage.xaml` so the code-behind can set it:

```diff
     <Border x:Name="InstalledEmpty" Padding="20,40" Visibility="Collapsed">
         <TextBlock
+            x:Name="InstalledEmptyText"
             Text="No plugins installed."
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminPluginsPage.xaml.cs around lines 220 - 248, Update RebuildInstalled to reset InstalledMatchText and collapse InstalledPager before returning when there are no installations, preventing stale toolbar state. For the filtered.Count == 0 branch, keep the installed empty-state handling but set a separate named empty-state text element to indicate that no installed plugins match the search, and restore the normal “No plugins installed” text when installations exist or the filter matches results; add the corresponding element name in AdminPluginsPage.xaml.

#### CodeRabbit suggestion

```text
private void RebuildInstalled()
    {
        InstalledCards.Children.Clear();
        if (ViewModel.Installations.Count == 0)
        {
            InstalledEmpty.Visibility = Visibility.Visible;
            InstalledPager.Visibility = Visibility.Collapsed;
            InstalledMatchText.Text = "";
            InstalledEmptyText.Text = "No plugins installed.";
            return;
        }
        var filtered = ViewModel.Installations.Where(p => PluginMatches(
            InstalledSearchBox.Text, p.PluginId, p.Presentation, p.Capabilities, p.SourceKind, p.RepositoryName)).ToList();
        InstalledMatchText.Text = string.IsNullOrWhiteSpace(InstalledSearchBox.Text)
            ? $"{filtered.Count} plugins"
            : $"{filtered.Count} of {ViewModel.Installations.Count}";
        if (filtered.Count == 0)
        {
            InstalledEmptyText.Text = "No installed plugins match the search.";
            InstalledEmpty.Visibility = Visibility.Visible;
            InstalledPager.Visibility = Visibility.Collapsed;
            return;
        }
        InstalledEmpty.Visibility = Visibility.Collapsed;
        var pageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)InstalledPageSize));
        _installedPage = Math.Min(_installedPage, pageCount - 1);
        foreach (var plugin in filtered.Skip(_installedPage * InstalledPageSize).Take(InstalledPageSize))
        {
            InstalledCards.Children.Add(BuildInstalledCard(plugin));
        }
        InstalledPager.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        InstalledPageText.Text = $"Page {_installedPage + 1} of {pageCount}";
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 88. Use the shared date formatter for the document timestamp.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminPolicyPage.xaml.cs:238-243`
- **CodeRabbit ID:** `28a5c724-e3fa-466c-b8a6-9bbbd262bd22`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Use the shared date formatter for the document timestamp.**

This row formats the timestamp with `document.UpdatedAt.ToLocalTime():g`. Line 304 in the same file formats version dates with `SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime`. The two formats differ, so the same page shows two date styles. Use `DateTimeDisplay.FormatDateTime` here.




<details>
<summary>♻️ Proposed fix</summary>

```diff
         var updated = new TextBlock
         {
-            Text = $"Updated {document.UpdatedAt.ToLocalTime():g}", FontSize = 11,
+            Text = $"Updated {SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(document.UpdatedAt)}", FontSize = 11,
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminPolicyPage.xaml.cs around lines 238 - 243, Update the updated-timestamp TextBlock to use DateTimeDisplay.FormatDateTime, matching the version-date formatting already used on this page, instead of the inline ToLocalTime():g format.

#### CodeRabbit suggestion

```text
var updated = new TextBlock
        {
            Text = $"Updated {SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(document.UpdatedAt)}", FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 89. Do not dispose the previous `CancellationTokenSource` in the scheduler.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminSettingsDetailPage.xaml.cs:1609-1633`
- **CodeRabbit ID:** `1db9e25e-378c-43b6-bcef-0c62c71e9220`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Do not dispose the previous `CancellationTokenSource` in the scheduler.**

`ScheduleAdminThemeSave` cancels and disposes `previous` at lines 1614-1615. The task that owns `previous` also disposes it in its own `finally` at line 1631. The pending `Task.Delay(delayMilliseconds, owner.Token)` continuation runs after the disposal here, so the delay's cancellation callback and the `owner.IsCancellationRequested` read in the exception filter execute against a disposed source. Let the owning task perform the disposal.

<details>
<summary>🛡️ Proposed fix</summary>

```diff
         var previous = Interlocked.Exchange(ref slot, owner);
         previous?.Cancel();
-        previous?.Dispose();
         _ = SaveAdminThemeSettingAfterDelayAsync(key, delayMilliseconds, owner);
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminSettingsDetailPage.xaml.cs around lines 1609 - 1633, Remove the previous?.Dispose() call from ScheduleAdminThemeSave, leaving cancellation and replacement of the prior source intact; SaveAdminThemeSettingAfterDelayAsync owns disposal in its finally block.

#### CodeRabbit suggestion

```text
private void ScheduleAdminThemeSave(string key, int delayMilliseconds)
    {
        var owner = new CancellationTokenSource();
        ref var slot = ref (key == "ui.admin_theme_vars" ? ref _adminThemeVarsSaveCts : ref _adminThemeCssSaveCts);
        var previous = Interlocked.Exchange(ref slot, owner);
        previous?.Cancel();
        _ = SaveAdminThemeSettingAfterDelayAsync(key, delayMilliseconds, owner);
    }

    private async Task SaveAdminThemeSettingAfterDelayAsync(string key, int delayMilliseconds, CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(delayMilliseconds, owner.Token);
            await ViewModel.SaveSettingsAsync([key]);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        finally
        {
            if (key == "ui.admin_theme_vars") Interlocked.CompareExchange(ref _adminThemeVarsSaveCts, null, owner);
            else Interlocked.CompareExchange(ref _adminThemeCssSaveCts, null, owner);
            owner.Dispose();
        }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 90. Attach the `SizeChanged` handler only once per ScrollViewer.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminShellPage.xaml.cs:104-118`
- **CodeRabbit ID:** `df639ae8-7d01-411f-b626-26d3926ebf94`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Attach the `SizeChanged` handler only once per ScrollViewer.**

`AdminContentFrame_Navigated` runs on every navigation, and admin pages set `NavigationCacheMode = Enabled`, so the same page instance and the same `ScrollViewer` are reused. `NormalizeAdminPageViewport` adds a new `SizeChanged` handler each time at line 159. The handlers are never removed, so the count grows with every navigation between admin pages, and each resize invokes all of them.

Mark the ScrollViewer after the first normalization and skip it afterwards.

<details>
<summary>🛠️ Proposed fix</summary>

```diff
     private static void NormalizeAdminPageViewport(DependencyObject page)
     {
         var scrollViewer = FindFirstDescendant<ScrollViewer>(page);
         if (scrollViewer?.Content is not FrameworkElement content) return;
 
         scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
         scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
         scrollViewer.HorizontalContentAlignment = HorizontalAlignment.Center;
 
         void ApplyWidth(double width) => content.Width = Math.Max(0, width);
         ApplyWidth(scrollViewer.ActualWidth);
+        if (scrollViewer.Tag as string == "adminViewportNormalized") return;
+        scrollViewer.Tag = "adminViewportNormalized";
         scrollViewer.SizeChanged += (_, args) => ApplyWidth(args.NewSize.Width);
     }
```
</details>





Also applies to: 148-160

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminShellPage.xaml.cs around lines 104 - 118, Update NormalizeAdminPageViewport to mark each ScrollViewer after attaching its SizeChanged handler, and skip already-marked viewers on subsequent calls. Ensure AdminContentFrame_Navigated’s repeated navigation over cached page instances does not register duplicate handlers while preserving the initial viewport normalization.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 91. Correct the Max Profiles hint text.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminUserDetailPage.xaml.cs:1584-1589`
- **CodeRabbit ID:** `6f54b9d0-2d33-4051-8b08-4b91f6bf75c7`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Correct the Max Profiles hint text.**

The hint says "0 = unlimited". `maxProfilesBox` sets `Minimum = 1`, and the comment on lines 1502-1504 states that the server rejects `max_profiles < 1`. The hint tells the user to enter a value that the control and the server both reject.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         profilesGroup.Children.Add(new TextBlock
         {
-            Text = "0 = unlimited",
+            Text = "Minimum 1 profile",
             FontSize = 11,
             Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
         });
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminUserDetailPage.xaml.cs around lines 1584 - 1589, Update the hint text in the profiles group near maxProfilesBox to reflect that values must be at least 1; remove the “0 = unlimited” guidance and ensure the replacement does not suggest any value rejected by the control or server.

#### CodeRabbit suggestion

```text
profilesGroup.Children.Add(new TextBlock
        {
            Text = "Minimum 1 profile",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 92. Correct the Max Profiles hint text.

- [ ] **Status:** Unreviewed
- **Scope:** Admin views
- **Location:** `src/SiloPlayer/Views/Admin/AdminUsersPage.xaml.cs:1506-1511`
- **CodeRabbit ID:** `68ee3e82-eb85-4813-a9cb-8448803d60f2`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Correct the Max Profiles hint text.**

`maxProfilesBox` sets `Minimum = 1` (line 1430), and the comment on line 1426 states the server floor is 1. The hint "0 = unlimited" describes a value the control rejects. The same hint appears in `AdminUserDetailPage.xaml.cs`.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         profilesGroup.Children.Add(new TextBlock
         {
-            Text = "0 = unlimited",
+            Text = "Minimum 1 profile",
             FontSize = 11,
             Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
         });
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/Admin/AdminUsersPage.xaml.cs around lines 1506 - 1511, Update the Max Profiles hint associated with maxProfilesBox to remove the invalid “0 = unlimited” guidance and state the supported minimum of 1; apply the same correction in the corresponding hint on AdminUserDetailPage.

#### CodeRabbit suggestion

```text
profilesGroup.Children.Add(new TextBlock
        {
            Text = "Minimum 1 profile",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        });
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 93. The skip labels bypass the clamp that the tooltips use.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/AudiobookNowListening.xaml.cs:42-43`
- **CodeRabbit ID:** `247dae9e-965e-45e8-94f1-2a506f800ae1`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**The skip labels bypass the clamp that the tooltips use.**

Line 42, Line 43, Line 226, and Line 227 write the raw `AudiobookSkipBackSeconds` and `AudiobookSkipForwardSeconds` values into `SkipBackText` and `SkipForwardText`. `UpdateSkipButtonLabels` clamps the same values to 5..120, and `SkipBack_Click` and `SkipForward_Click` also clamp them.

If a stored setting is outside 5..120, the button shows one number and the seek uses another.




<details>
<summary>🐛 Proposed fix</summary>

```diff
     private void UpdateSkipButtonLabels(AppSettings settings)
     {
         var back = Math.Clamp(settings.AudiobookSkipBackSeconds, 5, 120);
         var forward = Math.Clamp(settings.AudiobookSkipForwardSeconds, 5, 120);
+        SkipBackText.Text = back.ToString();
+        SkipForwardText.Text = forward.ToString();
         AutomationProperties.SetName(SkipBackButton, $"Back {back} seconds");
```

Then remove the duplicate raw assignments at Lines 42-43 and Lines 226-227.
</details>


Also applies to: 226-227

#### Requested remediation

In @src/SiloPlayer/Controls/AudiobookNowListening.xaml.cs around lines 42 - 43, Update the initialization and refresh paths around SkipBackText and SkipForwardText to use UpdateSkipButtonLabels, ensuring displayed values are clamped to the same 5..120 range used by SkipBack_Click and SkipForward_Click. Remove the duplicate raw AudiobookSkipBackSeconds and AudiobookSkipForwardSeconds assignments in both locations.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 94. Handle a null `MediaItem` in `OnMediaItemChanged`.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/AudiobookSquareCard.xaml.cs:33-37`
- **CodeRabbit ID:** `257c1c6c-f74e-43b4-b592-741466e1df32`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Handle a null `MediaItem` in `OnMediaItemChanged`.**

The type check at Line 35 skips `Bind` when the new value is null. The card then keeps the previous item's title, progress bar, and time-left text.

Clear the visual state when the property is set to null.




<details>
<summary>🐛 Proposed fix</summary>

```diff
     private static void OnMediaItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
     {
-        if (d is AudiobookSquareCard card && e.NewValue is MediaItem item)
-            card.Bind(item);
+        if (d is not AudiobookSquareCard card) return;
+        if (e.NewValue is MediaItem item) card.Bind(item);
+        else card.Clear();
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/AudiobookSquareCard.xaml.cs around lines 33 - 37, Update OnMediaItemChanged to handle a null e.NewValue by clearing the AudiobookSquareCard visual state, including the previous title, progress bar, and time-left text, while preserving the existing Bind(item) behavior for non-null MediaItem values.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 95. `Math.Clamp` can throw when `scaledH` rounds below `containerH`.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/BackdropImage.xaml.cs:199-202`
- **CodeRabbit ID:** `dc71317c-75a5-4466-a078-ac4b4d0a3465`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**`Math.Clamp` can throw when `scaledH` rounds below `containerH`.**

Line 202 passes `containerH - scaledH` as the minimum and `0` as the maximum. `Math.Clamp` throws `ArgumentException` when the minimum is greater than the maximum.

`scale` at Line 191 is `Math.Max(containerW / _imgW, containerH / _imgH)`. In exact arithmetic `scaledH >= containerH`, so the minimum is never positive. In floating point, `_imgH * (containerH / _imgH)` can round to a value slightly below `containerH`. The minimum then becomes a small positive number and the call throws inside the `SizeChanged` callback.

Clamp the minimum to at most 0.




<details>
<summary>🐛 Proposed fix</summary>

```diff
         double anchorY = AnchorY;
         double top = containerH * anchorY - scaledH * anchorY;
         // Clamp so we never show gaps at top or bottom
-        top = Math.Clamp(top, containerH - scaledH, 0);
+        top = Math.Clamp(top, Math.Min(0, containerH - scaledH), 0);
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/BackdropImage.xaml.cs around lines 199 - 202, Update the top-position clamping in the backdrop sizing logic to ensure the minimum bound, containerH - scaledH, is capped at 0 before passing it to Math.Clamp, preventing an invalid min/max range from floating-point rounding. Preserve the existing gap-prevention behavior.

#### CodeRabbit suggestion

```text
double anchorY = AnchorY;
        double top = containerH * anchorY - scaledH * anchorY;
        // Clamp so we never show gaps at top or bottom
        top = Math.Clamp(top, Math.Min(0, containerH - scaledH), 0);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 96. `DescribeImportSource` prints the key twice when `LastModified` is empty.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/CatalogImportDialog.xaml.cs:235-239`
- **CodeRabbit ID:** `af149762-2a0f-496e-89ed-31e7f785357d`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**`DescribeImportSource` prints the key twice when `LastModified` is empty.**

Line 237 assigns `src.Key` to `label` when `LastModified` is empty. Line 238 then returns `$"{label} • {src.Key}"`. The result is the key, a bullet, and the same key again.




<details>
<summary>🐛 Proposed fix</summary>

```diff
     private static string DescribeImportSource(CatalogSeedImportSource src)
     {
-        var label = !string.IsNullOrEmpty(src.LastModified) ? FormatTime(src.LastModified!) : src.Key;
-        return $"{label} • {src.Key}";
+        return string.IsNullOrEmpty(src.LastModified)
+            ? src.Key
+            : $"{FormatTime(src.LastModified!)} • {src.Key}";
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/CatalogImportDialog.xaml.cs around lines 235 - 239, Update DescribeImportSource so an empty LastModified value does not cause src.Key to be included both as label and suffix; retain the formatted timestamp label when available and ensure the returned description contains the key only once.

#### CodeRabbit suggestion

```text
private static string DescribeImportSource(CatalogSeedImportSource src)
    {
        return string.IsNullOrEmpty(src.LastModified)
            ? src.Key
            : $"{FormatTime(src.LastModified!)} • {src.Key}";
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 97. Remove the ternary with two identical branches.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/EditMetadataDialog.cs:848-850`
- **CodeRabbit ID:** `3d56fdda-926d-47ec-b4d8-ecc793845436`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Remove the ternary with two identical branches.**

Both branches produce the same string, so `applied.Revision` has no effect. Either drop the condition or include the revision in the success message.




<details>
<summary>🐛 Proposed fix</summary>

```diff
-            _toast.Success(string.IsNullOrWhiteSpace(applied.Revision)
-                ? "Image applied successfully."
-                : "Image applied successfully.");
+            _toast.Success("Image applied successfully.");
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/EditMetadataDialog.cs around lines 848 - 850, In the success notification near the applied-image handling, simplify the _toast.Success call by removing the redundant condition on applied.Revision and keeping a single “Image applied successfully.” message.

#### CodeRabbit suggestion

```text
_toast.Success("Image applied successfully.");
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 98. Make `moreButton` non-hit-testable while it is invisible.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/LibraryGridCard.cs:121-144`
- **CodeRabbit ID:** `7de9f440-31d2-4e64-8cce-5b11275b05a3`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Make `_moreButton` non-hit-testable while it is invisible.**

`_moreButton` starts with `Opacity = 0`, but it stays hit-testable. `Opacity` does not disable input in WinUI. A tap in the bottom-right 32×32 region of any card therefore opens the context menu instead of navigating to the item. On touch input there is no hover, so the button is never revealed before it is hit.

Set `IsHitTestVisible` together with `Opacity` in the pointer handlers.




<details>
<summary>🐛 Proposed fix</summary>

```diff
             Opacity = 0,
+            IsHitTestVisible = false,
```

```diff
     private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
     {
         ...
         _moreButton.Opacity = 1;
+        _moreButton.IsHitTestVisible = true;
     }
 
     private void OnPointerExited(object sender, PointerRoutedEventArgs e)
     {
         CancelPlaybackPrefetch();
         ResetHoverVisuals();
         _moreButton.Opacity = 0;
+        _moreButton.IsHitTestVisible = false;
     }
```

Apply the same reset in `BindPlaceholder` and `Reset`.
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/LibraryGridCard.cs around lines 121 - 144, Update the _moreButton visibility handling in the pointer handlers so it is hit-testable only when revealed, setting IsHitTestVisible alongside Opacity. Ensure BindPlaceholder and Reset restore both Opacity = 0 and IsHitTestVisible = false, while the reveal path enables both.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 99. Parse the volume number with the invariant culture.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/MangaFilesDialog.cs:180-193`
- **CodeRabbit ID:** `ed57b29a-456e-4dab-8dae-21d8d4dc4d74`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Parse the volume number with the invariant culture.**

`VolumeToken` captures a number that always uses `.` as the decimal separator. `double.TryParse` without a culture uses the current culture. On a culture where `.` is the group separator, for example de-DE, `"1.5"` parses as `15`. The dialog then shows "Volume 15" for file `v1.5`.




<details>
<summary>🐛 Proposed fix</summary>

```diff
-            return match.Success && double.TryParse(match.Groups[1].Value, out var number)
+            return match.Success && double.TryParse(
+                    match.Groups[1].Value,
+                    System.Globalization.NumberStyles.Float,
+                    System.Globalization.CultureInfo.InvariantCulture,
+                    out var number)
                 ? $"Volume {number:0.##}"
                 : token;
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/MangaFilesDialog.cs around lines 180 - 193, Update FileRowLabel so the double.TryParse call for the VolumeToken capture uses CultureInfo.InvariantCulture, preserving the captured dot-decimal value across current cultures and the existing formatted label behavior.

#### CodeRabbit suggestion

```text
private static string FileRowLabel(MangaChapterFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.Volume))
        {
            var token = file.Volume.Trim();
            var match = VolumeToken.Match(token);
            return match.Success && double.TryParse(
                    match.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number)
                ? $"Volume {number:0.##}"
                : token;
        }
        if (file.ChapterIndex.HasValue)
            return $"Chapter {file.ChapterIndex:0.##}";
        return string.IsNullOrWhiteSpace(file.Title) ? "Chapter" : file.Title.Trim();
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 100. The mini bar picks its layout only once per activation.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/MiniPlayerBar.xaml.cs:43-50`
- **CodeRabbit ID:** `e7a572d4-8c19-461e-8418-3ecfde48810f`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**The mini bar picks its layout only once per activation.**

`Activate()` reads `_playerService.IsAudiobook` one time and then calls `ActivateAudiobookMode()` or `ActivateVideoMode()`. `OnAudiobookPresentationChanged` refreshes text and cover, but it never switches between `VideoBar` and `AudiobookBar`. If playback moves from a video item to an audiobook item (or the reverse) while the bar stays active, the wrong bar remains visible and `Height` stays wrong.

Re-evaluate the mode inside `UpdateAudiobookPresentation` or on content change.

#### Requested remediation

In @src/SiloPlayer/Controls/MiniPlayerBar.xaml.cs around lines 43 - 50, Update UpdateAudiobookPresentation or the content-change handling to re-evaluate _playerService.IsAudiobook and switch between ActivateAudiobookMode and ActivateVideoMode when the active media type changes. Ensure the corresponding VideoBar or AudiobookBar visibility and Height are refreshed while the mini bar remains active.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 101. Preserve the source selection when a submit fails.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/SubtitleAiDialog.xaml.cs:205-222`
- **CodeRabbit ID:** `e007ae64-b622-476d-b998-19c0fe21f049`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Preserve the source selection when a submit fails.**

The catch path in `Submit_Click` calls `SetSubmitting(false)`. Line 219 then calls `UpdateMode()`, which clears `SourceComboBox.Items` and sets `SelectedIndex = 0`. The user's chosen audio or subtitle track reverts to the first entry after every failed submit, while the error text tells the user to retry. The user can then start a translation on the wrong track without noticing.

Capture the current index before the rebuild and restore it.




<details>
<summary>🐛 Proposed fix to keep the selected source</summary>

```diff
     private void UpdateMode()
     {
+        var previousSourceIndex = SourceComboBox.SelectedIndex;
         var fromAudio = _mode == "audio";
@@
-        if (SourceComboBox.Items.Count > 0)
-            SourceComboBox.SelectedIndex = 0;
+        if (SourceComboBox.Items.Count > 0)
+            SourceComboBox.SelectedIndex = previousSourceIndex >= 0 && previousSourceIndex < SourceComboBox.Items.Count
+                ? previousSourceIndex
+                : 0;
```

`SubtitleMode_Click` and `AudioMode_Click` change the item list, so also reset `previousSourceIndex` to `-1` when `_mode` changes.
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/SubtitleAiDialog.xaml.cs around lines 205 - 222, Preserve the selected source when submission fails by capturing the current SourceComboBox index before UpdateMode rebuilds its items, then restoring that index afterward. Update SetSubmitting and the related source-refresh flow without changing normal mode behavior; when SubtitleMode_Click or AudioMode_Click changes _mode, reset the stored previousSourceIndex to -1 so stale selections are not restored across mode changes.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 102. Add accessible names to `OutlineToggle` and `OpacitySlider`.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/SubtitleAppearanceDialog.xaml:155-156`
- **CodeRabbit ID:** `2cdc188b-649b-486e-bba3-60ce8841819c`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Add accessible names to `OutlineToggle` and `OpacitySlider`.**

Both controls carry no accessible name. `OutlineToggle` sets `OnContent=""` and `OffContent=""`, so no name is derived from content. `OpacitySlider` has no content at all. The adjacent "Outline" and "Opacity" `TextBlock` elements are separate siblings, so no programmatic association exists. A screen reader announces both controls without a label. The `Close` button in this same file already sets `AutomationProperties.Name`, so the pattern is established here.




<details>
<summary>♿ Proposed fix for the missing accessible names</summary>

```diff
                         <ToggleSwitch x:Name="OutlineToggle" Grid.Column="1" OnContent="" OffContent=""
+                                      AutomationProperties.Name="Text outline"
                                       Toggled="OutlineToggle_Toggled" />
@@
                             <Slider x:Name="OpacitySlider" Grid.Column="0"
                                     Minimum="0" Maximum="100" StepFrequency="5"
+                                    AutomationProperties.Name="Background opacity percent"
                                     ValueChanged="OpacitySlider_ValueChanged"
                                     VerticalAlignment="Center" />
```
</details>


Also applies to: 196-199

#### Requested remediation

In @src/SiloPlayer/Controls/SubtitleAppearanceDialog.xaml around lines 155 - 156, Add explicit AutomationProperties.Name values to OutlineToggle and OpacitySlider, using their adjacent labels (“Outline” and “Opacity”) as the accessible names. Follow the existing Close button pattern in SubtitleAppearanceDialog and leave the control behavior unchanged.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 103. Guard hex parsing against malformed input.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/SubtitleAppearanceDialog.xaml.cs:424-436`
- **CodeRabbit ID:** `7cdbd923-e5ce-4054-b3b0-623bbc43c3fc`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Guard hex parsing against malformed input.**

`Convert.ToByte(string, 16)` throws `FormatException` for a 6-character value that is not hexadecimal. `_state.FontColor` and `_state.BackgroundColor` come from the server setting, so the value is not restricted to the local palettes. The throw propagates through `UpdatePreview` into the `Opened` async void handler and crashes the app.

<details>
<summary>🛡️ Proposed fix</summary>

```diff
     private static Color ColorFromHex(string hex)
     {
         if (string.IsNullOrEmpty(hex)) return Colors.White;
         var clean = hex.TrimStart('#');
-        if (clean.Length == 6)
+        if (clean.Length == 6 &&
+            byte.TryParse(clean.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
+            byte.TryParse(clean.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
+            byte.TryParse(clean.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
         {
-            byte r = Convert.ToByte(clean.Substring(0, 2), 16);
-            byte g = Convert.ToByte(clean.Substring(2, 2), 16);
-            byte b = Convert.ToByte(clean.Substring(4, 2), 16);
             return Color.FromArgb(0xFF, r, g, b);
         }
         return Colors.White;
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/SubtitleAppearanceDialog.xaml.cs around lines 424 - 436, Update ColorFromHex to validate the trimmed six-character value before parsing, returning Colors.White for any non-hex input instead of allowing Convert.ToByte to throw; preserve the existing valid-color conversion and fallback behavior.

#### CodeRabbit suggestion

```text
private static Color ColorFromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Colors.White;
        var clean = hex.TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(clean.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(clean.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return Color.FromArgb(0xFF, r, g, b);
        }
        return Colors.White;
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 104. Reset the file label when validation rejects the file.

- [ ] **Status:** Unreviewed
- **Scope:** Controls
- **Location:** `src/SiloPlayer/Controls/SubtitleSearchDialog.xaml.cs:198-212`
- **CodeRabbit ID:** `92f170d1-19a1-4e32-9d96-28c6a8eec5af`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Reset the file label when validation rejects the file.**

`LoadUploadFileAsync` clears `_uploadFileBytes` and `_uploadFileName` on the rejection paths, but it leaves `UploadFileText` showing the previously accepted file name. The dialog then displays a file name that will not be uploaded.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         var extension = Path.GetExtension(file.Name);
         if (!IsAcceptedSubtitleExtension(extension))
         {
+            UploadFileText.Text = "No file selected";
             UploadStatusText.Text = "Unsupported file type. Use SRT, VTT, ASS, SSA, or SUB.";
             UploadButton.IsEnabled = false;
             return;
         }
 
         var properties = await file.GetBasicPropertiesAsync();
         if ((long)properties.Size > MaxSubtitleUploadBytes)
         {
+            UploadFileText.Text = "No file selected";
             UploadStatusText.Text = "Subtitle file is larger than 5 MB.";
             UploadButton.IsEnabled = false;
             return;
         }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Controls/SubtitleSearchDialog.xaml.cs around lines 198 - 212, Update the rejection paths in LoadUploadFileAsync for unsupported extensions and oversized files to also clear UploadFileText, keeping the displayed label consistent with the reset _uploadFileBytes and _uploadFileName state.

#### CodeRabbit suggestion

```text
var extension = Path.GetExtension(file.Name);
        if (!IsAcceptedSubtitleExtension(extension))
        {
            UploadFileText.Text = "No file selected";
            UploadStatusText.Text = "Unsupported file type. Use SRT, VTT, ASS, SSA, or SUB.";
            UploadButton.IsEnabled = false;
            return;
        }

        var properties = await file.GetBasicPropertiesAsync();
        if ((long)properties.Size > MaxSubtitleUploadBytes)
        {
            UploadFileText.Text = "No file selected";
            UploadStatusText.Text = "Subtitle file is larger than 5 MB.";
            UploadButton.IsEnabled = false;
            return;
        }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 105. Escape `profileId` in the verify-pin path.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Api/AuthApi.cs:34-35`
- **CodeRabbit ID:** `f1040d58-644d-45a4-8ce1-f9150036a994`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Escape `profileId` in the verify-pin path.**

Every other profile route in this file escapes the ID with `Uri.EscapeDataString`. This route interpolates the raw value. If `profileId` contains `/`, `?`, or `#`, the request targets a different path or drops the segment.

<details>
<summary>🐛 Proposed fix</summary>

```diff
     public Task<VerifyPinResponse> VerifyPinAsync(string profileId, string pin, CancellationToken ct = default)
-        => client.PostAsync<VerifyPinResponse>($"/api/v1/profiles/{profileId}/verify-pin", new VerifyPinRequest { Pin = pin }, ct);
+        => client.PostAsync<VerifyPinResponse>(
+            $"/api/v1/profiles/{Uri.EscapeDataString(profileId)}/verify-pin",
+            new VerifyPinRequest { Pin = pin },
+            ct);
```
</details>

#### Requested remediation

In @src/SiloPlayer.Core/Api/AuthApi.cs around lines 34 - 35, Update VerifyPinAsync to apply Uri.EscapeDataString to profileId before interpolating it into the verify-pin request path, matching the escaping used by the other profile routes while preserving the existing request payload and cancellation behavior.

#### CodeRabbit suggestion

```text
public Task<VerifyPinResponse> VerifyPinAsync(string profileId, string pin, CancellationToken ct = default)
        => client.PostAsync<VerifyPinResponse>(
            $"/api/v1/profiles/{Uri.EscapeDataString(profileId)}/verify-pin",
            new VerifyPinRequest { Pin = pin },
            ct);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 106. Escape path IDs consistently.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Api/CatalogApi.cs:186-187`
- **CodeRabbit ID:** `7e86c48c-0358-414a-8127-94b73800feda`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Escape path IDs consistently.**

These routes interpolate `contentId` and `seriesId` without encoding. Other methods in the same file, for example Line 235 and Line 274, use `Uri.EscapeDataString`. An ID that contains `/`, `?`, `#`, or a space produces a wrong request path or a malformed URL.

Apply `Uri.EscapeDataString` to every interpolated ID segment in this file.

<details>
<summary>🐛 Example fix</summary>

```diff
     public Task<MediaItemDetail> GetItemDetailAsync(string contentId, CancellationToken ct = default)
-        => client.GetAsync<MediaItemDetail>($"/api/v1/catalog/items/{contentId}", ct);
+        => client.GetAsync<MediaItemDetail>($"/api/v1/catalog/items/{Uri.EscapeDataString(contentId)}", ct);
```
</details>





Also applies to: 211-227, 239-243, 251-264, 268-269

#### Requested remediation

In @src/SiloPlayer.Core/Api/CatalogApi.cs around lines 186 - 187, Update every catalog route in the affected methods, including GetItemDetailAsync and the methods using contentId or seriesId, to wrap each interpolated path ID with Uri.EscapeDataString. Preserve the existing endpoints and request behavior while ensuring all ID segments are encoded consistently.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 107. Escape `contentId` and `sessionId` in these paths.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Api/PlaybackApi.cs:7-8`
- **CodeRabbit ID:** `c317613a-765b-406b-91d8-6776270e89dc`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Escape `contentId` and `sessionId` in these paths.**

`ReplanPlaybackV3Async` at Line 24 escapes `sessionId` with `Uri.EscapeDataString`. These four routes interpolate the raw value. An ID that contains `/`, `?`, or `#` changes the target path.

<details>
<summary>🐛 Proposed fix</summary>

```diff
     public Task StopPlaybackAsync(string sessionId, CancellationToken ct = default)
-        => client.DeleteAsync($"/api/v1/playback/{sessionId}", ct);
+        => client.DeleteAsync($"/api/v1/playback/{Uri.EscapeDataString(sessionId)}", ct);
```
</details>





Also applies to: 28-29, 36-37, 42-43

#### Requested remediation

In @src/SiloPlayer.Core/Api/PlaybackApi.cs around lines 7 - 8, Escape the interpolated contentId and sessionId path parameters in GetWatchDetailAsync and the four affected playback route methods, matching the existing Uri.EscapeDataString usage in ReplanPlaybackV3Async. Ensure IDs containing path or query delimiters remain a single path segment.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 108. Escape `profileId` in the path.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Api/SettingsApi.cs:582-583`
- **CodeRabbit ID:** `7a15ea80-cb92-4971-9145-d06b14d7249e`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Escape `profileId` in the path.**

Every other path parameter in this class uses `Uri.EscapeDataString`. `UpdateProfileAsync` interpolates `profileId` raw. A value that contains `/`, `?`, or `#` changes the request target.

<details>
<summary>🛠️ Proposed fix</summary>

```diff
-    public Task<Profile> UpdateProfileAsync(string profileId, object updates, CancellationToken ct = default)
-        => client.PutAsync<Profile>($"/api/v1/profiles/{profileId}", updates, ct);
+    public Task<Profile> UpdateProfileAsync(string profileId, object updates, CancellationToken ct = default)
+        => client.PutAsync<Profile>($"/api/v1/profiles/{Uri.EscapeDataString(profileId)}", updates, ct);
```
</details>

#### Requested remediation

In @src/SiloPlayer.Core/Api/SettingsApi.cs around lines 582 - 583, Update UpdateProfileAsync to apply Uri.EscapeDataString to profileId before interpolating it into the request path, matching the handling of other path parameters in the class.

#### CodeRabbit suggestion

```text
public Task<Profile> UpdateProfileAsync(string profileId, object updates, CancellationToken ct = default)
        => client.PutAsync<Profile>($"/api/v1/profiles/{Uri.EscapeDataString(profileId)}", updates, ct);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 109. Preserve non-`Int64` numeric tokens.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Json/StringOrNumberJsonConverter.cs:23-24`
- **CodeRabbit ID:** `84532805-e321-45cf-a02d-68e1487799d1`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Preserve non-`Int64` numeric tokens.**

`GetDouble().ToString("R", ...)` changes values such as `18446744073709551615`. Preserve the original token bytes through `Utf8JsonReader.ValueSpan` or `ValueSequence`; `Utf8JsonReader` has no `GetRawText()` method. Add a deserialization test for this value.

#### Requested remediation

In @src/SiloPlayer.Core/Json/StringOrNumberJsonConverter.cs around lines 23 - 24, Update the numeric handling in StringOrNumberJsonConverter to preserve non-Int64 token text exactly by reading Utf8JsonReader.ValueSpan or ValueSequence instead of converting through GetDouble(). Add a deserialization test covering 18446744073709551615 and retain invariant formatting for Int64 values.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 110. Remove the Continuum repository reference.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Models/Admin/ScanModels.cs:42-46`
- **CodeRabbit ID:** `99af3925-ef91-4705-a6ea-145fd7a4a842`
- **Fingerprint:** `phantom:poseidon:tapir`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Remove the Continuum repository reference.**

Line 45 cites an internal Continuum source path. Describe the event contract without this reference.





As per coding guidelines, “Never fetch, inspect, compare against, cite, or use the legacy private GitLab Continuum repository for Silo desktop parity work.”

#### Requested remediation

In @src/SiloPlayer.Core/Models/Admin/ScanModels.cs around lines 42 - 46, Update the XML summary for the active scan model to remove the internal Continuum repository path and describe the streamed scan event contract using only the local model’s behavior and purpose.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 111. Guard non-object JSON roots before property access.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Models/Settings/SubtitleAppearance.cs:81-95`
- **CodeRabbit ID:** `d4edf92b-62b3-4999-baee-55945f720760`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Guard non-object JSON roots before property access.**

When `json` is `[]`, `null`, or `"legacy"`, `TryGetProperty` throws `InvalidOperationException`. The method catches only `JsonException`, so it does not return the default appearance. Add `if (root.ValueKind != JsonValueKind.Object) return result;` before the first property access.

#### Requested remediation

In @src/SiloPlayer.Core/Models/Settings/SubtitleAppearance.cs around lines 81 - 95, In the JSON parsing method around the local root element, validate that root.ValueKind is JsonValueKind.Object immediately after assigning doc.RootElement and return the existing result for any other root type before calling ValidString or TryGetProperty. Preserve the current property parsing for object roots.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 112. `Start` reads `legacyStartChannels` outside the lock.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Services/EventChannelClient.cs:201-212`
- **CodeRabbit ID:** `3a98f586-a168-425e-aceb-a6b5fd69fa53`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**`Start` reads `_legacyStartChannels` outside the lock.**

Line 206 computes `toAdd` before the lock at Line 207 protects the set. Two concurrent `Start` calls with the same channel can both compute a non-empty `toAdd` and call `Subscribe`, which raises the ref count twice while `Stop` releases it once. The channel then never drops out of the subscription set.

Move the `toAdd` computation inside the lock.

#### Requested remediation

In @src/SiloPlayer.Core/Services/EventChannelClient.cs around lines 201 - 212, Move the toAdd computation in Start inside the existing _lock section so reads and updates of _legacyStartChannels are atomic. Keep the subsequent Subscribe call outside the lock and only invoke it for channels newly added by that Start call.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 113. Do not hardcode `AppChannel` to `"qa"`.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Services/MpvNativePlaybackCapabilities.cs:184`
- **CodeRabbit ID:** `dff16f31-91e8-4348-8d1c-c898482b3b2e`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Do not hardcode `AppChannel` to `"qa"`.**

Every shipped client reports the `qa` channel to the server. That value can change server-side routing, feature gating, and analytics segmentation. Derive the channel from build configuration or an app setting, and default to a release value.

<details>
<summary>♻️ Proposed change</summary>

```diff
-            AppChannel = "qa",
+            AppChannel = AppChannelName,
```

Add a resolved constant or injected value, for example:

```csharp
#if DEBUG
    private const string AppChannelName = "qa";
#else
    private const string AppChannelName = "release";
#endif
```
</details>

#### Requested remediation

In @src/SiloPlayer.Core/Services/MpvNativePlaybackCapabilities.cs at line 184, Update the AppChannel assignment in the relevant playback capabilities configuration so it is not always hardcoded to "qa"; derive it from the build configuration or an existing app setting, using "qa" for debug builds and a release channel for non-debug builds. Keep the resolved value centralized and preserve the existing AppChannel consumer behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 114. Guard the version lookup so playback start cannot fail.

- [ ] **Status:** Unreviewed
- **Scope:** Core/API/models/services
- **Location:** `src/SiloPlayer.Core/Services/PlaybackManager.cs:106-108`
- **CodeRabbit ID:** `7fc874f6-5106-46ba-84c7-649249f0e40f`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Guard the version lookup so playback start cannot fail.**

`FileVersionInfo.GetVersionInfo` throws when the path is empty or the file is unreadable. The `?? "unknown"` fallback only covers a null `ProductVersion`. If `Environment.ProcessPath` returns null, `GetVersionInfo("")` throws and `StartSessionAsync` fails before any request is sent. The call also performs synchronous file I/O on every session start.

Cache the resolved version once and wrap the lookup.

<details>
<summary>🛡️ Proposed fix</summary>

```diff
-        var appVersion = System.Diagnostics.FileVersionInfo
-            .GetVersionInfo(Environment.ProcessPath ?? "")
-            .ProductVersion?.Split('+')[0] ?? "unknown";
+        var appVersion = s_appVersion;
```

Add the cached resolver to the class:

```csharp
private static readonly string s_appVersion = ResolveAppVersion();

private static string ResolveAppVersion()
{
    try
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            return "unknown";
        return System.Diagnostics.FileVersionInfo
            .GetVersionInfo(path)
            .ProductVersion?.Split('+')[0] ?? "unknown";
    }
    catch
    {
        return "unknown";
    }
}
```
</details>

#### Requested remediation

In @src/SiloPlayer.Core/Services/PlaybackManager.cs around lines 106 - 108, Replace the per-session version lookup in StartSessionAsync with a class-level cached app-version value resolved once. Add a ResolveAppVersion helper that returns "unknown" for missing or blank Environment.ProcessPath, catches lookup failures, and otherwise preserves the existing ProductVersion normalization.

#### CodeRabbit suggestion

```text
var appVersion = s_appVersion;
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 115. The status text is unreadable because `Opacity` is set on the parent `Border`.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/DownloadsPage.xaml.cs:122-135`
- **CodeRabbit ID:** `1f27a8c3-15dc-4879-ae47-7d38e5154ade`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**The status text is unreadable because `Opacity` is set on the parent `Border`.**

`Opacity` on a `Border` applies to the whole subtree, including the child `TextBlock`. The status label therefore renders at 15% opacity. Apply the transparency to the background brush only.

<details>
<summary>🎨 Proposed fix</summary>

```diff
         var statusBadge = new Border
         {
-            Background = statusColor,
-            Opacity = 0.15,
+            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
+                (statusColor as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Transparent)
+            {
+                Opacity = 0.15,
+            },
             CornerRadius = new CornerRadius(4),
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/DownloadsPage.xaml.cs around lines 122 - 135, Update the statusBadge Border construction so only its Background brush uses 15% transparency; remove the parent Border Opacity setting and keep the TextBlock foreground fully opaque.

#### CodeRabbit suggestion

```text
var statusBadge = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                (statusColor as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Transparent)
            {
                Opacity = 0.15,
            },
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = dl.Status.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = statusColor
            }
        };
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 116. The file name fallback never runs.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/EbookReaderPage.xaml.cs:158-163`
- **CodeRabbit ID:** `7f593ac6-be64-46f9-b88f-f941fda9d892`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**The file name fallback never runs.**

`Path.GetFileName` returns an empty string, not `null`, when the input is empty. The `?? $"File {version.FileId}"` fallback is therefore unreachable, and a version without `FileName` and `FilePath` renders as `"EPUB · "`.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-            var name = Path.GetFileName(version.FileName ?? version.FilePath ?? "") ?? $"File {version.FileId}";
+            var name = Path.GetFileName(version.FileName ?? version.FilePath ?? "");
+            if (string.IsNullOrWhiteSpace(name)) name = $"File {version.FileId}";
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/EbookReaderPage.xaml.cs around lines 158 - 163, Update the name construction before FileSelector.Items.Add so an empty result from Path.GetFileName falls back to the FileId label, while preserving non-empty file names and the existing format display.

#### CodeRabbit suggestion

```text
var name = Path.GetFileName(version.FileName ?? version.FilePath ?? "");
            if (string.IsNullOrWhiteSpace(name)) name = $"File {version.FileId}";
            FileSelector.Items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrWhiteSpace(format) ? name : $"{format} · {name}",
                Tag = version.FileId,
            });
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 117. Prevent the KIDS and PIN badges from overlapping.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/HouseholdSetupPage.xaml:70-83`
- **CodeRabbit ID:** `d6e34a11-d3e2-476f-a1f4-1cbad31e17fb`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Prevent the KIDS and PIN badges from overlapping.**

Both badges use `HorizontalAlignment="Center"` and `VerticalAlignment="Bottom"` inside the same `Grid`. If a profile is a child profile and also has a PIN, the two badges render on top of each other and both labels become unreadable. Place them in a single horizontal container.





<details>
<summary>🎨 Proposed fix</summary>

```diff
-                                        <Border Background="{StaticResource AppBackgroundBrush}"
-                                                BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
-                                                CornerRadius="4" Padding="5,1"
-                                                HorizontalAlignment="Center" VerticalAlignment="Bottom"
-                                                Visibility="{x:Bind Profile.IsChild, Converter={StaticResource BoolToVis}}">
-                                            <TextBlock Text="KIDS" FontFamily="Consolas" FontSize="9" CharacterSpacing="80" />
-                                        </Border>
-                                        <Border Background="{StaticResource AppBackgroundBrush}"
-                                                BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
-                                                CornerRadius="4" Padding="5,1"
-                                                HorizontalAlignment="Center" VerticalAlignment="Bottom"
-                                                Visibility="{x:Bind Profile.ShowsPinBadge, Converter={StaticResource BoolToVis}}">
-                                            <TextBlock Text="PIN" FontFamily="Consolas" FontSize="9" CharacterSpacing="80" />
-                                        </Border>
+                                        <StackPanel Orientation="Horizontal" Spacing="4"
+                                                    HorizontalAlignment="Center" VerticalAlignment="Bottom">
+                                            <Border Background="{StaticResource AppBackgroundBrush}"
+                                                    BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
+                                                    CornerRadius="4" Padding="5,1"
+                                                    Visibility="{x:Bind Profile.IsChild, Converter={StaticResource BoolToVis}}">
+                                                <TextBlock Text="KIDS" FontFamily="Consolas" FontSize="9" CharacterSpacing="80" />
+                                            </Border>
+                                            <Border Background="{StaticResource AppBackgroundBrush}"
+                                                    BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
+                                                    CornerRadius="4" Padding="5,1"
+                                                    Visibility="{x:Bind Profile.ShowsPinBadge, Converter={StaticResource BoolToVis}}">
+                                                <TextBlock Text="PIN" FontFamily="Consolas" FontSize="9" CharacterSpacing="80" />
+                                            </Border>
+                                        </StackPanel>
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/HouseholdSetupPage.xaml around lines 70 - 83, Wrap the KIDS and PIN badge Borders in a single horizontal container within the profile badge Grid area, preserving each badge’s visibility binding and styling while adding spacing as needed to prevent overlap. Use the existing Profile.IsChild and Profile.ShowsPinBadge bindings unchanged.

#### CodeRabbit suggestion

```text
<StackPanel Orientation="Horizontal" Spacing="4"
                                                    HorizontalAlignment="Center" VerticalAlignment="Bottom">
                                            <Border Background="{StaticResource AppBackgroundBrush}"
                                                    BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
                                                    CornerRadius="4" Padding="5,1"
                                                    Visibility="{x:Bind Profile.IsChild, Converter={StaticResource BoolToVis}}">
                                                <TextBlock Text="KIDS" FontFamily="Consolas" FontSize="9" CharacterSpacing="80" />
                                            </Border>
                                            <Border Background="{StaticResource AppBackgroundBrush}"
                                                    BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
                                                    CornerRadius="4" Padding="5,1"
                                                    Visibility="{x:Bind Profile.ShowsPinBadge, Converter={StaticResource BoolToVis}}">
                                                <TextBlock Text="PIN" FontFamily="Consolas" FontSize="9" CharacterSpacing="80" />
                                            </Border>
                                        </StackPanel>
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 118. Encode the plugin route path before you build the URL.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/PluginRoutePage.xaml.cs:52-58`
- **CodeRabbit ID:** `89988a10-b11d-4dfc-a614-12ba4cb34509`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Encode the plugin route path before you build the URL.**

`args.RoutePath` is inserted into the URL without encoding. A path that contains `..`, `?`, or `#` changes the target resource or the query string. Line 57 then appends `theme=dark` to whatever query the path introduced. Normalize and encode the path segments before you compose the URL.

#### Requested remediation

In @src/SiloPlayer/Views/PluginRoutePage.xaml.cs around lines 52 - 58, Update the URL construction around PluginWebView.Source to normalize and encode each segment of the route path after removing the trailing wildcard and before composing the API URL. Preserve the leading slash, prevent route data such as ?, #, or .. from altering the resource or query, and append the theme parameter only after the encoded path is incorporated.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 119. Suppress the toast when the page cancels the request.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/RequestBrowsePage.xaml.cs:118-133`
- **CodeRabbit ID:** `76da472f-e77b-4532-9f36-6f595ccc4b5f`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Suppress the toast when the page cancels the request.**

`Request_Click` passes `_lifetime.Token`, and `OnNavigatedFrom` cancels `_lifetime`. If the user navigates away while the request is in flight, the generic `catch` runs and shows "Request failed: A task was canceled." on a page that no longer exists. `RequestDetailPage.Request_Click` already handles this case at its Line 137.

<details>
<summary>🛠️ Proposed fix</summary>

```diff
         catch (Exception ex)
         {
+            if (ex is OperationCanceledException && _lifetime.IsCancellationRequested) return;
             button.Content = item.RequestLabel;
             button.IsEnabled = item.Request.Requestable;
             App.Services.GetRequiredService<ToastService>().Error($"Request failed: {ex.Message}");
         }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/RequestBrowsePage.xaml.cs around lines 118 - 133, Update Request_Click to handle cancellation from _lifetime.Token separately from other exceptions: restore the button state without showing a toast when navigation cancels the request, while preserving the existing error toast for genuine failures. Follow the cancellation handling used by RequestDetailPage.Request_Click.

#### CodeRabbit suggestion

```text
private async void Request_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestMediaResult item } button) return;
        button.IsEnabled = false; button.Content = "Submitting…";
        try
        {
            await _api.CreateAsync(new CreateMediaRequestInput { MediaType = item.MediaType, TmdbId = item.TmdbId, Title = item.Title, Year = item.Year, Overview = item.Overview, PosterPath = item.PosterPath, BackdropPath = item.BackdropPath }, _lifetime.Token);
            button.Content = "Requested";
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && _lifetime.IsCancellationRequested) return;
            button.Content = item.RequestLabel;
            button.IsEnabled = item.Request.Requestable;
            App.Services.GetRequiredService<ToastService>().Error($"Request failed: {ex.Message}");
        }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 120. Add an accessible name to `BackButton`.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/RequestDetailPage.xaml:17`
- **CodeRabbit ID:** `8936d9f5-f37b-4ebd-ba7c-e2838fb9565d`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Add an accessible name to `BackButton`.**

`BackButton` contains only a glyph. Screen readers announce no name for it. `LoadingBackButton` at Line 49 already sets `AutomationProperties.Name="Go back"`.

<details>
<summary>♿ Proposed fix</summary>

```diff
-                <Button x:Name="BackButton" VerticalAlignment="Top" HorizontalAlignment="Left" Margin="24" Width="44" Height="44" Click="Back_Click"><FontIcon Glyph="&#xE72B;" /></Button>
+                <Button x:Name="BackButton" VerticalAlignment="Top" HorizontalAlignment="Left" Margin="24" Width="44" Height="44" Click="Back_Click" AutomationProperties.Name="Go back"><FontIcon Glyph="&#xE72B;" /></Button>
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/RequestDetailPage.xaml at line 17, Add AutomationProperties.Name="Go back" to the BackButton declaration, matching the accessible naming already used by LoadingBackButton.

#### CodeRabbit suggestion

```text
<Button x:Name="BackButton" VerticalAlignment="Top" HorizontalAlignment="Left" Margin="24" Width="44" Height="44" Click="Back_Click" AutomationProperties.Name="Go back"><FontIcon Glyph="&#xE72B;" /></Button>
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 121. Handle cancellation in `RecommendationRequestClick`.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/RequestDetailPage.xaml.cs:240-256`
- **CodeRabbit ID:** `59942d66-6879-4318-b645-28f1015e8578`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Handle cancellation in `RecommendationRequest_Click`.**

`CreateAsync` receives `_lifetime.Token`, and `OnNavigatedFrom` cancels that token. When the user navigates away during submission, the generic `catch` shows a "Request failed" toast for a cancellation that the page caused. `Request_Click` at Line 137 filters this case; apply the same filter here.

<details>
<summary>🛠️ Proposed fix</summary>

```diff
+        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
         catch (Exception ex)
         {
             button.IsEnabled = true;
             button.Content = "Request";
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/RequestDetailPage.xaml.cs around lines 240 - 256, Update RecommendationRequest_Click to handle OperationCanceledException from _lifetime.Token separately, matching the cancellation behavior in Request_Click: restore the button state without displaying the “Request failed” toast, while preserving the existing error handling for other exceptions.

#### CodeRabbit suggestion

```text
private async void RecommendationRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestMediaResult item } button) return;
        button.IsEnabled = false;
        button.Content = "Submitting…";
        try
        {
            await _api.CreateAsync(new CreateMediaRequestInput { MediaType = item.MediaType, TmdbId = item.TmdbId, Title = item.Title, Year = item.Year, Overview = item.Overview, PosterPath = item.PosterPath, BackdropPath = item.BackdropPath }, _lifetime.Token);
            button.Content = "Requested";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            button.IsEnabled = true;
            button.Content = "Request";
            App.Services.GetRequiredService<ToastService>().Error($"Request failed: {ex.Message}");
        }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 122. Preserve `ResultsRepeater` virtualization. `ResultsContent` gives its vertical `StackPanel` children infinite height, so `ResultsRepeater` can realize the full growing result set. Host it in a constrained virtualization viewport instead of the vertical `StackPanel`.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/SearchPage.xaml:254-334`
- **CodeRabbit ID:** `0092124c-9a35-4da2-818d-b6458b90795e`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `PERFORMANCE_AND_SCALABILITY`

#### CodeRabbit explanation

**Preserve `ResultsRepeater` virtualization.** `ResultsContent` gives its vertical `StackPanel` children infinite height, so `ResultsRepeater` can realize the full growing result set. Host it in a constrained virtualization viewport instead of the vertical `StackPanel`.

#### Requested remediation

In @src/SiloPlayer/Views/SearchPage.xaml around lines 254 - 334, Update the ResultsRepeater hosting structure under ResultsContent so it receives a constrained vertical viewport rather than infinite height from the StackPanel. Preserve the existing ResultsRepeater virtualization, layout, item template, and infinite-scroll behavior by constraining only its container and keeping ResultsScroll as the outer scroll host.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 123. `EmptyStateText` ignores `IsAddingServer` changes.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/ServerSelectPage.xaml:84-88`
- **CodeRabbit ID:** `fcadb44f-1296-4a71-b796-d3bc4761d061`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**`EmptyStateText` ignores `IsAddingServer` changes.**

The code-behind computes this text's visibility from `ViewModel.Servers.Count == 0 && !ViewModel.IsAddingServer`, but it only recomputes on `Servers.CollectionChanged`. When the user opens the add-server form on an empty list, `IsAddingServer` becomes true and the empty-state text stays visible above the form.

Bind the visibility instead of computing it once.

<details>
<summary>🛠️ Proposed fix</summary>

```diff
             <TextBlock
                 x:Name="EmptyStateText"
                 Text="No servers added yet. Add one to get started."
                 Style="{StaticResource SecondaryTextStyle}"
+                Visibility="{x:Bind ViewModel.IsAddingServer, Mode=OneWay, Converter={StaticResource InverseBoolToVis}}"
                 HorizontalAlignment="Center" />
```

Keep the existing `Servers.CollectionChanged` handler for the count condition, or move both conditions into a view-model property.
</details>

#### Requested remediation

In @src/SiloPlayer/Views/ServerSelectPage.xaml around lines 84 - 88, Update EmptyStateText visibility to react to both ViewModel.Servers.Count and ViewModel.IsAddingServer, rather than computing it only from the collection-changed handler; preserve visibility only when the server list is empty and IsAddingServer is false.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 124. Align the import source keyboard order with the visual order.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/SettingsPage.xaml:1404-1468`
- **CodeRabbit ID:** `59187e18-776c-4f46-8c10-44c373a67e1b`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Align the import source keyboard order with the visual order.**

`ImportSourceEmbyCard` uses the default `TabIndex` (`Int32.MaxValue`), so focus moves to Plex, Jellyfin, then Emby. Set `TabIndex="0"` on Emby, or assign explicit values that match the visual order.

#### Requested remediation

In @src/SiloPlayer/Views/SettingsPage.xaml around lines 1404 - 1468, Set TabIndex="0" on ImportSourceEmbyCard so keyboard focus follows the visual order Emby, Plex, then Jellyfin, while preserving the existing TabIndex values on ImportSourcePlexCard and ImportSourceJellyfinCard.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 125. `InterfaceTab` is missing from the search index.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/SettingsPage.xaml.cs:1089-1141`
- **CodeRabbit ID:** `9943f257-5ede-4588-9915-50b637fb187b`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**`InterfaceTab` is missing from the search index.**

The `entries` array lists 16 tabs but omits `InterfaceTab`. Two results follow:

- A search query never hides "Navigation & Cards". The button keeps whatever visibility it had, so it appears among filtered results for unrelated terms.
- `AppearanceNavGroup` at Line 1126 checks `InterfaceTab.Visibility`, so that group stays visible even when every matching Appearance tab is hidden.

The hardcoded `availableSettingsCount` of 17 or 16 at Line 1137 also drifts from the array length. Add the entry and derive the count from the data.

<details>
<summary>🛠️ Proposed fix</summary>

```diff
             (AppearanceTab, "appearance theme profile dark light custom date time format clock reset cinema"),
+            (InterfaceTab, "navigation cards menu poster size card captions title year artwork preset primary menu"),
             (ThemeEditorTab, "theme editor customize colors css design tokens token overrides custom css community themes preview"),
```

```diff
-        var availableSettingsCount = _canManageProfiles ? 17 : 16;
+        var availableSettingsCount = _canManageProfiles ? entries.Length : entries.Length - 1;
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/SettingsPage.xaml.cs around lines 1089 - 1141, Add InterfaceTab to the entries search index with appropriate search terms, ensuring it participates in visibility filtering and match counting. Replace the hardcoded availableSettingsCount in the settings-search method with a value derived from entries, accounting for the _canManageProfiles restriction.

#### CodeRabbit suggestion

```text
var entries = new (Button Button, string SearchText)[]
        {
            (PlaybackTab, "playback quality language skipping video spoken metadata auto skip intros credits recaps preview auto play next up episodes"),
            (SubtitlesTab, "subtitles subtitle language behavior forced captions font size family color outline background opacity position preview"),
            (AppearanceTab, "appearance theme profile dark light custom date time format clock reset cinema"),
            (InterfaceTab, "navigation cards menu poster size card captions title year artwork preset primary menu"),
            (ThemeEditorTab, "theme editor customize colors css design tokens token overrides custom css community themes preview"),
            (AccessibilityTab, "accessibility readability contrast motion transparency text size weight high contrast preview"),
            (HomeScreenTab, "home screen sections layout rows continue watching next up recently added library order scope reset"),
            (CardOverlaysTab, "card overlays poster badges overlay accent color preset preview icon position styling"),
            (PersonalizeTab, "personalize taste profile recommendations ratings likes dislikes refine"),
            (LibrariesTab, "libraries library visibility access disabled order playback preferences spoken subtitle forced remember"),
            (ImportTab, "history import emby jellyfin plex watched mapping sync fetched matched unmatched progress skipped"),
            (WebhookSyncTab, "webhook sync plex emby jellyfin intake progress watched connections deliveries server url token"),
            (WatchProvidersTab, "watch providers trakt import export scrobble favorites history progress removals"),
            (DevicesTab, "your devices device tv phone tablet browser this device forget hdr dolby vision frame rate fill screen audio subtitle sync offset"),
            (NotificationsSettingsTab, "notifications new episodes email discord browser push webhooks per episode alerts digest url"),
            (ConnectAppsTab, "connect apps silo jellyfin compatible infuse swiftfin jellycon findroid sign in login server address username password pin"),
            (ProfilesTab, "profiles profile names pin access rules primary household library create delete"),
        };

        var matches = 0;
        foreach (var entry in entries)
        {
            if (ReferenceEquals(entry.Button, ProfilesTab) && !_canManageProfiles)
            {
                entry.Button.Visibility = Visibility.Collapsed;
                continue;
            }
            var visible = tokens.Length == 0 || tokens.All(token =>
                entry.SearchText.Contains(token, StringComparison.OrdinalIgnoreCase));
            entry.Button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) matches++;
        }

        PlaybackNavGroup.Visibility = new[] { PlaybackTab, SubtitlesTab, DevicesTab }
            .Any(button => button.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;
        AppearanceNavGroup.Visibility = new[] { AppearanceTab, InterfaceTab, CardOverlaysTab, AccessibilityTab, ThemeEditorTab }
            .Any(button => button.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        HomeDiscoveryNavGroup.Visibility = new[] { HomeScreenTab, PersonalizeTab, LibrariesTab }
            .Any(button => button.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsNavGroup.Visibility = new[] { ConnectAppsTab, WatchProvidersTab, WebhookSyncTab, ImportTab }
            .Any(button => button.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;
        AccountNavGroup.Visibility = new[] { ProfilesTab, NotificationsSettingsTab }
            .Any(button => button.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;

        var availableSettingsCount = _canManageProfiles ? entries.Length : entries.Length - 1;
        SettingsSearchStatus.Text = tokens.Length == 0
            ? $"{availableSettingsCount} settings sections"
            : matches == 0 ? "No matching settings" : $"{matches} {(matches == 1 ? "match" : "matches")}";
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 126. Do not dispose the `CancellationTokenSource` while the previous preview still runs.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/SmartCollectionWizardPage.xaml.cs:596-608`
- **CodeRabbit ID:** `38e139bf-8b6c-4421-811b-9854604be130`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Do not dispose the `CancellationTokenSource` while the previous preview still runs.**

`RunPreviewAsync` cancels `_previewCts` and disposes it in the same statement pair. The prior `ViewModel.PreviewAsync` call may still hold that token. Any later access to `token.Register`, `token.ThrowIfCancellationRequested`, or `WaitHandle` on a disposed source throws `ObjectDisposedException`, and that exception is not caught by the `OperationCanceledException` handler.

Capture the old source, cancel it, and dispose it only after the awaited call returns.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
     private async Task RunPreviewAsync()
     {
-        _previewCts?.Cancel();
-        _previewCts?.Dispose();
-        _previewCts = new CancellationTokenSource();
+        var previous = _previewCts;
+        previous?.Cancel();
+        var cts = new CancellationTokenSource();
+        _previewCts = cts;
         try
         {
-            await ViewModel.PreviewAsync(_previewCts.Token);
+            await ViewModel.PreviewAsync(cts.Token);
         }
         catch (OperationCanceledException)
         {
         }
+        finally
+        {
+            previous?.Dispose();
+        }
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/SmartCollectionWizardPage.xaml.cs around lines 596 - 608, Update RunPreviewAsync to retain the previous CancellationTokenSource, cancel it without immediately disposing it, and dispose it only after the prior PreviewAsync operation has completed. Ensure the newly created source is used for the current preview and preserve the existing OperationCanceledException handling.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 127. Bind the "Loading more" row to `IsLoadingMore`.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/TasteSeedPage.xaml:80-83`
- **CodeRabbit ID:** `fc776b47-5493-4116-bed8-455d008533d8`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Bind the "Loading more" row to `IsLoadingMore`.**

The row is visible whenever `HasMore` is true. The user then sees the static text "Loading more" while no request runs, because only the `ProgressRing` reacts to `IsLoadingMore`. Bind the container visibility to `IsLoadingMore`.




<details>
<summary>🐛 Proposed fix</summary>

```diff
-                <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Center" Visibility="{x:Bind ViewModel.HasMore, Mode=OneWay, Converter={StaticResource BoolToVis}}">
+                <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Center" Visibility="{x:Bind ViewModel.IsLoadingMore, Mode=OneWay, Converter={StaticResource BoolToVis}}">
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/TasteSeedPage.xaml around lines 80 - 83, Update the “Loading more” StackPanel visibility binding to use ViewModel.IsLoadingMore instead of ViewModel.HasMore, while preserving the existing one-way BoolToVis conversion and child bindings.

#### CodeRabbit suggestion

```text
<StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Center" Visibility="{x:Bind ViewModel.IsLoadingMore, Mode=OneWay, Converter={StaticResource BoolToVis}}">
                    <ProgressRing Width="18" Height="18" IsActive="{x:Bind ViewModel.IsLoadingMore, Mode=OneWay}" />
                    <TextBlock Text="Loading more" Foreground="{StaticResource SecondaryTextBrush}" VerticalAlignment="Center" />
                </StackPanel>
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 128. `VoteCount` uses a one-time binding.

- [ ] **Status:** Unreviewed
- **Scope:** Non-admin views
- **Location:** `src/SiloPlayer/Views/WatchTogetherRoomPage.xaml:306-314`
- **CodeRabbit ID:** `e78dfe8f-a07d-4a98-924d-5e68edebc2c0`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**`VoteCount` uses a one-time binding.**

`{x:Bind VoteCount}` defaults to `OneTime`. The displayed count then stays at the value present when the item was realized. If the view model updates an existing `WatchTogetherSuggestion` instance instead of replacing it, the vote count never changes on screen. Use `Mode=OneWay` and make `WatchTogetherSuggestion` raise `PropertyChanged`, or confirm the view model always replaces the whole collection.




<details>
<summary>🐛 Proposed fix</summary>

```diff
-                                                        <TextBlock Text="{x:Bind VoteCount}" FontSize="11" />
+                                                        <TextBlock Text="{x:Bind VoteCount, Mode=OneWay}" FontSize="11" />
```
</details>

#### Requested remediation

In @src/SiloPlayer/Views/WatchTogetherRoomPage.xaml around lines 306 - 314, Update the VoteCount binding in SuggestionVoteButton_Click’s suggestion item template to use OneWay mode, and ensure WatchTogetherSuggestion raises PropertyChanged when VoteCount changes so existing realized items refresh without requiring collection replacement.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 129. Remove the embedded line-number prefixes from the imported document.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `AGENTS.md:1-8`
- **CodeRabbit ID:** `5d99e1d3-1ee0-45e2-9ed1-c980570fa66a`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Remove the embedded line-number prefixes from the imported document.**

Lines 1 through 497 contain literal line numbers and padding, for example `        1 # Continuum Desktop Player`. The text is the raw output of a numbered file dump. Markdown renders these prefixes as content. The leading whitespace also turns several lines into code blocks. Strip the prefixes so the section renders and diffs correctly.

#### Requested remediation

In @AGENTS.md around lines 1 - 8, Remove the imported line-number prefixes and their associated padding from the entire AGENTS.md document, covering all affected lines through line 497. Preserve the underlying Markdown content and structure, including headings, lists, and code blocks, without changing the document’s substantive text.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 130. Resolve the conflicting runtime app-data path.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `AGENTS.md:511-525`
- **CodeRabbit ID:** `001ff5c4-ae26-41ab-b2b3-f8e5caf2eb30`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Resolve the conflicting runtime app-data path.**

Line 511 states the runtime app data and logs directory is `%LOCALAPPDATA%\SiloPlayer`. Lines 525, 568, and 635 state `%LOCALAPPDATA%\ContinuumPlayer`. The 1.1.44 entry in `CHANGELOG.md` records diagnostics under `%LOCALAPPDATA%\SiloPlayer\navigation_errors.txt`. Use one directory in this file so log inspection and secret-redaction steps target the correct location.

#### Requested remediation

In @AGENTS.md around lines 511 - 525, Update the runtime app-data/logs references in AGENTS.md to use one consistent directory, resolving the conflict between SiloPlayer and ContinuumPlayer. Align the affected entries with the directory established by the 1.1.44 CHANGELOG diagnostics path, including log-inspection and secret-redaction guidance.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 131. Update the stale `ContinuumPlayer` file paths to the current `SiloPlayer` layout.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `AGENTS.md:549-671`
- **CodeRabbit ID:** `9eaa0a2c-1704-4fcf-a2aa-9acc9865b3c8`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Update the stale `ContinuumPlayer` file paths to the current `SiloPlayer` layout.**

The "Important Recent Changes" and "Working Tree Notes" sections reference paths such as `src/ContinuumPlayer/Views/LibraryPage.xaml.cs`, `src/ContinuumPlayer.Core/Services/EventChannelClient.cs`, and `libs/mpv/scripts/continuum-osc.lua`. The repository uses `src/SiloPlayer`, `src/SiloPlayer.Core`, `src/SiloPlayer.Player`, and `libs/mpv/scripts/silo-osc.lua`. An agent that follows this file will look for files that do not exist. Lines 616-617 and 623-624 also pin a `F:\ContinuumPlayer` workspace and a v1.0.205 installer, while the current release is 1.1.90.

As per coding guidelines: "Before each parity pass, fetch the current GitHub `main` branch directly and record the exact commit used."

#### Requested remediation

In @AGENTS.md around lines 549 - 671, Update the “Important Recent Changes” and “Working Tree Notes” sections to use the current SiloPlayer source, core, player, and mpv script layout instead of stale ContinuumPlayer paths. Replace the pinned F:\ContinuumPlayer workspace and v1.0.205 installer details with the current workspace and 1.1.90 release information. Add the requirement to fetch the current GitHub main branch and record its exact commit before each parity pass.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 132. Correct the duplicate `1.1.41` heading.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `CHANGELOG.md:391-405`
- **CodeRabbit ID:** `7bb07ed2-977e-4fe4-bf11-3904fe12a8f4`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Correct the duplicate `1.1.41` heading.**

Two consecutive sections carry the same version heading `## 1.1.41` with different content. The second section describes Autoscan, Scheduled Tasks, Subtitles, Marker History, Recommendations, Users, Playback History, and History Import, and reports 258 tests, while the first reports 268 tests. Assign the correct distinct version to one of the two sections so the release history stays unambiguous.

#### Requested remediation

In @CHANGELOG.md around lines 391 - 405, Correct the duplicate version heading in the changelog by assigning the appropriate distinct release version to either the first or second consecutive 1.1.41 section. Keep each section’s existing content unchanged, including its feature list and test count.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 133. Do not report a timestamped signature without confirming the timestamp.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `installer/build.ps1:224-232`
- **CodeRabbit ID:** `521fd802-51bb-4c9b-99bd-e845a5a4c57c`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Do not report a timestamped signature without confirming the timestamp.**

Lines 227-230 re-sign the installer when the signature is invalid or when `TimeStamperCertificate` is absent. `Set-LocalCodeSignature` throws only when the status is not `Valid`. It does not check `TimeStamperCertificate`. If the timestamp server is unreachable, the second signing attempt can still succeed without a timestamp, and line 231 prints "Verified timestamp-signed installer" for an untimestamped binary. `installer/sign-file.ps1` line 28 already enforces the timestamp for uninstaller signing. Apply the same check here.

<details>
<summary>🛡️ Proposed fix to re-verify after signing</summary>

```diff
     if ($SigningCertificate) {
         $setupSignature = Get-AuthenticodeSignature -LiteralPath $SetupExe.FullName
         if ($setupSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
             -not $setupSignature.TimeStamperCertificate) {
             Set-LocalCodeSignature -Path $SetupExe.FullName
+            $setupSignature = Get-AuthenticodeSignature -LiteralPath $SetupExe.FullName
+        }
+        if ($setupSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
+            -not $setupSignature.TimeStamperCertificate) {
+            throw "The installer is not validly timestamp-signed: $($setupSignature.Status)."
         }
         Write-Host "Verified timestamp-signed installer with $($SigningCertificate.Subject)."
     }
```
</details>

#### Requested remediation

In @installer/build.ps1 around lines 224 - 232, Update the signing flow around Set-LocalCodeSignature and $setupSignature so the installer signature is re-read after any re-signing, then require both Valid status and a non-null TimeStamperCertificate before printing the verified timestamp-signed message. Ensure an untimestamped result is rejected rather than reported as verified.

#### CodeRabbit suggestion

```text
if ($SetupExe) {
    if ($SigningCertificate) {
        $setupSignature = Get-AuthenticodeSignature -LiteralPath $SetupExe.FullName
        if ($setupSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
            -not $setupSignature.TimeStamperCertificate) {
            Set-LocalCodeSignature -Path $SetupExe.FullName
            $setupSignature = Get-AuthenticodeSignature -LiteralPath $SetupExe.FullName
        }
        if ($setupSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
            -not $setupSignature.TimeStamperCertificate) {
            throw "The installer is not validly timestamp-signed: $($setupSignature.Status)."
        }
        Write-Host "Verified timestamp-signed installer with $($SigningCertificate.Subject)."
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 134. `linecounts` refresh silently skips missing files and reports an inflated count.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `scripts/audit-bump.ps1:62-77`
- **CodeRabbit ID:** `862acb01-3a0e-49c8-9622-9f9c83318232`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**`line_counts` refresh silently skips missing files and reports an inflated count.**

Line 86 prints `$allSources.Count`, but the loop at Line 63 skips any source that fails `Test-Path` at Line 66. A renamed or deleted upstream file therefore keeps a stale count in the baseline while the summary claims it was refreshed.

Track the refreshed count separately and warn about missing sources.




<details>
<summary>♻️ Proposed fix</summary>

```diff
     $allSources = @($areaObj.web_sources) + @($areaObj.server_sources)
+    $refreshed = 0
+    $missing = @()
     foreach ($src in $allSources) {
         if (-not $src) { continue }
         $full = Join-Path $ServerPath ($src -replace '/', '\')
         if (Test-Path $full) {
             $lineCount = (Get-Content $full | Measure-Object -Line).Lines
+            $refreshed++
```

```diff
+        } else {
+            $missing += $src
         }
     }
```

```diff
-    Write-Host "    line counts refreshed for $($allSources.Count) source file(s)"
+    Write-Host "    line counts refreshed for $refreshed of $($allSources.Count) source file(s)"
+    foreach ($m in $missing) { Write-Warning "    missing source: $m" }
```
</details>

#### Requested remediation

In @scripts/audit-bump.ps1 around lines 62 - 77, Update the source-refresh loop to track a separate count of successfully processed files, incrementing it only after Test-Path succeeds; warn when a source is missing, and use this refreshed count instead of $allSources.Count in the summary so missing files cannot leave stale counts undisclosed.

#### CodeRabbit suggestion

```text
$allSources = @($areaObj.web_sources) + @($areaObj.server_sources)
    $refreshed = 0
    $missing = @()
    foreach ($src in $allSources) {
        if (-not $src) { continue }
        $full = Join-Path $ServerPath ($src -replace '/', '\')
        if (Test-Path $full) {
            $lineCount = (Get-Content $full | Measure-Object -Line).Lines
            $refreshed++
            if (-not $areaObj.line_counts) {
                $areaObj | Add-Member -NotePropertyName line_counts -NotePropertyValue ([pscustomobject]@{}) -Force
            }
            if ($areaObj.line_counts.PSObject.Properties[$src]) {
                $areaObj.line_counts.$src = $lineCount
            } else {
                $areaObj.line_counts | Add-Member -NotePropertyName $src -NotePropertyValue $lineCount -Force
            }
        } else {
            $missing += $src
        }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 135. `Pop-Location` is skipped when `git pull` fails.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `scripts/audit-delta.ps1:38-44`
- **CodeRabbit ID:** `97ab825a-7b22-4fc3-8944-73f00073eb49`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**`Pop-Location` is skipped when `git pull` fails.**

`$ErrorActionPreference = "Stop"` is set at Line 35. If `git pull --ff-only` throws, Line 42 never runs and the shell stays in `$ServerRepo`. Subsequent relative paths in the same session then resolve against the wrong directory.

Wrap each `Push-Location` block in `try`/`finally`, as `audit-scan.ps1` Line 51 already does. The same applies to Lines 54-57 and Lines 114-127.




<details>
<summary>♻️ Proposed fix</summary>

```diff
 if ($Pull) {
     Write-Host "Pulling latest continuum-server..." -ForegroundColor Cyan
     Push-Location $ServerRepo
-    git pull --ff-only
-    Pop-Location
+    try { git pull --ff-only }
+    finally { Pop-Location }
     Write-Host ""
 }
```
</details>

#### Requested remediation

In @scripts/audit-delta.ps1 around lines 38 - 44, Ensure every Push-Location block in the script, including the pull, lines 54-57, and lines 114-127 flows, uses try/finally so Pop-Location always executes when commands fail; preserve the existing commands and behavior while preventing the working directory from remaining changed.

#### CodeRabbit suggestion

```text
if ($Pull) {
    Write-Host "Pulling latest continuum-server..." -ForegroundColor Cyan
    Push-Location $ServerRepo
    try { git pull --ff-only }
    finally { Pop-Location }
    Write-Host ""
}
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 136. A baseline without `continuumservercommitmessage` crashes report generation.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `scripts/audit-scan.ps1:190`
- **CodeRabbit ID:** `8b25d503-5095-4503-9692-0ff161e58555`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**A baseline without `continuum_server_commit_message` crashes report generation.**

Line 44 validates only `continuum_server_sha`. If a bootstrapped or hand-edited baseline omits `continuum_server_commit_message`, `$baseline.continuum_server_commit_message` is `$null` and `.Substring(...)` throws. The failure happens after the diff and classification work completes, so the report is lost.

Resolve the message into a local variable with a fallback first.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
+$baselineMsg = if ($baseline.continuum_server_commit_message) { $baseline.continuum_server_commit_message } else { "(unknown)" }
 [void]$sb.AppendLine("| Generated | $(Get-Date -Format "yyyy-MM-dd HH:mm:ss") |")
-[void]$sb.AppendLine("| Baseline  | ``$($baseline.continuum_server_short_sha)`` ($(($baseline.continuum_server_commit_message).Substring(0, [Math]::Min(60, $baseline.continuum_server_commit_message.Length)))) |")
+[void]$sb.AppendLine("| Baseline  | ``$($baseline.continuum_server_short_sha)`` ($($baselineMsg.Substring(0, [Math]::Min(60, $baselineMsg.Length)))) |")
```
</details>

#### Requested remediation

In @scripts/audit-scan.ps1 at line 190, Update the baseline report generation around the baseline table row to first resolve continuum_server_commit_message into a local variable with a null-safe fallback, then use that variable for length calculation and Substring instead of dereferencing the baseline property directly. Preserve the existing truncation and formatting for present messages.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 137. Restore `ObservableCollection<T>` property notifications for bulk mutations.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer/Helpers/BulkObservableCollection.cs:17-29`
- **CodeRabbit ID:** `119c2310-d12e-4821-a47f-9f7986ec5dcb`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Restore `ObservableCollection<T>` property notifications for bulk mutations.**

`AddRange` and `RemoveFromFront` modify `Items` directly. They emit `CollectionChanged`, but they do not emit `PropertyChanged` for `Count` and `Item[]`. Bindings to `Count` or indexed items can retain stale values.

Emit both property notifications before the ranged collection notification.

<details>
<summary>Proposed fix</summary>

```diff
         foreach (T item in newItems)
             Items.Add(item); // Protected Items list — no per-item notification

+        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
+        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
         // Single ranged Add: ItemsRepeater adds new elements without touching existing ones
         OnCollectionChanged(new NotifyCollectionChangedEventArgs(
             NotifyCollectionChangedAction.Add, newItems, startIndex));
...
         for (int i = 0; i < count; i++)
         {
             removed.Add(Items[0]);
             Items.RemoveAt(0);
         }

+        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
+        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
         OnCollectionChanged(new NotifyCollectionChangedEventArgs(
             NotifyCollectionChangedAction.Remove, removed, 0));
```
</details>






Also applies to: 37-50

#### Requested remediation

In @src/SiloPlayer/Helpers/BulkObservableCollection.cs around lines 17 - 29, Update AddRange and RemoveFromFront to raise ObservableCollection property notifications for Count and Item[] after modifying Items but before emitting their ranged CollectionChanged notifications, preserving the existing bulk collection-change behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 138. Unsubscribe every handler that this window registers.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer/MainWindow.xaml.cs:426-453`
- **CodeRabbit ID:** `906bd575-31d5-4a54-b620-5c484874cf80`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Unsubscribe every handler that this window registers.**

`OnWindowClosed` removes three of the four `_navigationService.Navigated` handlers. `OnNavigated_AnimatePageEntrance` (registered at Line 131) stays attached. `_navigationService` is a DI service and can outlive this window, so the handler keeps animating content for a frame that is closing. `this.Activated` (Line 199) is also not removed.




<details>
<summary>🔧 Proposed fix</summary>

```diff
         this.SizeChanged -= OnWindowSizeChanged;
+        this.Activated -= OnWindowActivated;
         if (AppWindow != null) AppWindow.Changed -= OnAppWindowChanged;
         _navigationService.Navigated -= OnNavigated_UpdateWindowTitle;
         _navigationService.Navigated -= OnNavigated_ApplyAccessibility;
         _navigationService.Navigated -= OnNavigated_SynchronizeShellChrome;
+        _navigationService.Navigated -= OnNavigated_AnimatePageEntrance;
```
</details>

#### Requested remediation

In @src/SiloPlayer/MainWindow.xaml.cs around lines 426 - 453, Update OnWindowClosed to unsubscribe the remaining handlers registered by the window: remove OnNavigated_AnimatePageEntrance from _navigationService.Navigated and remove the this.Activated subscription using its corresponding handler. Keep the existing cleanup behavior unchanged.

#### CodeRabbit suggestion

```text
private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        CancelShellHydration();
        ReleaseNotificationSubscription();
        _eventChannel.SnapshotReceived -= OnShellEventSnapshot;
        _eventChannel.EventReceived -= OnShellEvent;
        _playerService.StateChanged -= OnPlayerStateChanged;
        _uiCustomizationService.Changed -= OnUICustomizationChanged;
        _authService.LoggedOut -= OnAuthLoggedOut;
        _authService.UserChanged -= OnAuthUserChanged;
        _authService.ProfileVerificationRequired -= OnProfileVerificationRequired;
        _authService.CredentialStoreFailed -= OnCredentialStoreFailed;
        _playerService.ShowPlayingNextRequested -= OnShowPlayingNextRequested;
        _playerService.PostRollReturnRequested -= OnPostRollReturnRequested;
        this.SizeChanged -= OnWindowSizeChanged;
        this.Activated -= OnWindowActivated;
        if (AppWindow != null) AppWindow.Changed -= OnAppWindowChanged;
        _navigationService.Navigated -= OnNavigated_UpdateWindowTitle;
        _navigationService.Navigated -= OnNavigated_ApplyAccessibility;
        _navigationService.Navigated -= OnNavigated_SynchronizeShellChrome;
        _navigationService.Navigated -= OnNavigated_AnimatePageEntrance;
        if (_paneOpenPropertyCallbackToken != 0)
        {
            NavView.UnregisterPropertyChangedCallback(
                NavigationView.IsPaneOpenProperty,
                _paneOpenPropertyCallbackToken);
            _paneOpenPropertyCallbackToken = 0;
        }
        StopPlayingNextCountdown();
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 139. Cache the avatar image data, not the presigned URL.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer/MainWindow.xaml.cs:2044-2072`
- **CodeRabbit ID:** `6da8dee5-69d9-4094-b558-d98a4e3d6877`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Cache the avatar image data, not the presigned URL.**

`ApplyProfileAvatar` stores the presigned URL in `BitmapImage.UriSource`. WinUI re-fetches from that URI whenever the image is re-decoded, and the request fails after the presigned URL expires. `LoadPlayingNextPosterAsync` already shows the correct pattern in this file: download the bytes once and call `SetSourceAsync`.




As per coding guidelines: "All images come as **presigned S3 URLs** in API responses. **URLs expire** — cache the image data, not the URL."

#### Requested remediation

In @src/SiloPlayer/MainWindow.xaml.cs around lines 2044 - 2072, Update ApplyProfileAvatar to download the presigned avatar URL once, cache the resulting image bytes, and initialize BitmapImage through SetSourceAsync instead of assigning avatarUri to UriSource. Follow the existing LoadPlayingNextPosterAsync pattern, while preserving the current invalid-URL handling, visibility updates, and ImageFailed behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 140. Do not throw from a XAML input handler.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer/MainWindow.xaml.cs:2361-2366`
- **CodeRabbit ID:** `8880ae8f-193f-479e-8d3a-d72b1c91b142`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Do not throw from a XAML input handler.**

`SidebarBrandHost_Tapped` and `SidebarBrandHost_KeyDown` call `NavigateToHome`. If the frame rejects the navigation, the thrown `InvalidOperationException` escapes the event handler and terminates the app. `NavView_ItemInvoked` already protects the same navigation with try/catch and a user-visible error. Apply the same treatment here.




<details>
<summary>🛡️ Proposed fix</summary>

```diff
     public void NavigateToHome()
     {
-        if (!_navigationService.Navigate<HomePage>())
-            throw new InvalidOperationException("The navigation frame rejected the Home page.");
-        NavView.SelectedItem = HomeNavItem;
+        if (!_navigationService.Navigate<HomePage>())
+        {
+            LogNavigationFailure(
+                "navigate_home",
+                new InvalidOperationException("The navigation frame rejected the Home page."));
+            ShowPlaybackError(
+                "Page failed to open",
+                "Silo could not open Home. The current page is still available.");
+            return;
+        }
+        NavView.SelectedItem = HomeNavItem;
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/MainWindow.xaml.cs around lines 2361 - 2366, Update NavigateToHome and its callers SidebarBrandHost_Tapped and SidebarBrandHost_KeyDown so rejected navigation is handled without allowing InvalidOperationException to escape a XAML input event; reuse the existing try/catch and user-visible error handling pattern from NavView_ItemInvoked, while preserving NavView.SelectedItem updates only after successful navigation.

#### CodeRabbit suggestion

```text
public void NavigateToHome()
    {
        if (!_navigationService.Navigate<HomePage>())
        {
            LogNavigationFailure(
                "navigate_home",
                new InvalidOperationException("The navigation frame rejected the Home page."));
            ShowPlaybackError(
                "Page failed to open",
                "Silo could not open Home. The current page is still available.");
            return;
        }
        NavView.SelectedItem = HomeNavItem;
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 141. Align the package version and replace the placeholder publisher identity.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer/Package.appxmanifest:10-13`
- **CodeRabbit ID:** `bc0a6a76-f8f8-46e0-9dc0-5734fdca0fbe`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Align the package version and replace the placeholder publisher identity.**

`Version="1.1.9.0"` does not match `SiloPlayer.csproj`, which sets `Version` 1.1.90 and `AssemblyVersion` 1.1.90.0. `1.1.9.0` sorts below `1.1.90.0`, so the MSIX version no longer tracks the shipped assembly version and upgrade ordering becomes wrong.

`Publisher="CN=User Name"` and `PublisherDisplayName` "User Name" are template placeholders. The publisher must match the signing certificate subject used by `installer/sign-file.ps1`, otherwise package installation fails.




<details>
<summary>🔧 Proposed fix</summary>

```diff
   <Identity
     Name="341d7bfc-3629-4cb4-b847-e890196eccbc"
-    Publisher="CN=User Name"
-    Version="1.1.9.0" />
+    Publisher="CN=<signing certificate subject>"
+    Version="1.1.90.0" />
```
</details>

#### Requested remediation

In @src/SiloPlayer/Package.appxmanifest around lines 10 - 13, Update the Identity Version in Package.appxmanifest to match the 1.1.90/1.1.90.0 version values from SiloPlayer.csproj, and replace the placeholder Publisher and PublisherDisplayName values with the signing certificate subject and display name used by installer/sign-file.ps1.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 142. Fix the channel order of `BadgeBackgroundColor`.

- [ ] **Status:** Unreviewed
- **Scope:** Remaining player/installer/scripts/root code
- **Location:** `src/SiloPlayer/Themes/DarkTheme.xaml:37`
- **CodeRabbit ID:** `8fcd752b-01f1-48f9-b58c-be0e1584abff`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Fix the channel order of `BadgeBackgroundColor`.**

XAML parses 8-digit color literals as `#AARRGGBB`. `#1F6FEB33` therefore resolves to alpha `0x1F` over green `#6FEB33`, not to the web token `#1F6FEB` at 20% alpha. Every badge renders a faint green wash instead of the translucent blue used by the WebUI. `CardOverlayBackgroundBrush` on Line 98 already uses the correct order.




<details>
<summary>🎨 Proposed fix</summary>

```diff
-    <Color x:Key="BadgeBackgroundColor">#1F6FEB33</Color>
+    <Color x:Key="BadgeBackgroundColor">#331F6FEB</Color>
```
</details>

#### Requested remediation

In @src/SiloPlayer/Themes/DarkTheme.xaml at line 37, Update the BadgeBackgroundColor value to use XAML’s #AARRGGBB ordering, preserving the intended WebUI blue #1F6FEB with 20% alpha and matching the ordering used by CardOverlayBackgroundBrush.

#### CodeRabbit suggestion

```text
<Color x:Key="BadgeBackgroundColor">#331F6FEB</Color>
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 143. Assert the Bearer scheme for plugin routes.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/AdminPluginsCurrentParitySourceTests.cs:65-67`
- **CodeRabbit ID:** `9163950d-1c74-4f0e-a7ec-cd856df0e94d`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Assert the Bearer scheme for plugin routes.**

Lines 65-67 only prove that the source contains `Authorization`. An unrelated header reference or another authentication scheme passes this test. Capture the outgoing request and require `Authorization.Scheme == "Bearer"` for the authenticated plugin route.

As per coding guidelines, all authenticated requests: `Authorization: Bearer <access_token>`.

#### Requested remediation

In @tests/SiloPlayer.Tests/AdminPluginsCurrentParitySourceTests.cs around lines 65 - 67, Update the plugin route parity test assertions around PluginRoutePage to verify the authenticated request uses an Authorization header with Scheme equal to “Bearer”, rather than only checking for the header name; retain the existing route and X-Profile-Id assertions.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 144. Assert the required public GitHub source.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/AdminSettingsCurrentParitySourceTests.cs:24-25`
- **CodeRabbit ID:** `056d2bcc-e3db-4557-88bc-ecdf57fbf10d`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Assert the required public GitHub source.**

These assertions accept any source that contains `Silo-Server/silo-themes`. A GitLab or another host can pass this test. Require the exact `https://github.com/Silo-Server/silo-themes` origin, or validate the parsed host and repository.

As per coding guidelines, the only authoritative Silo server/WebUI source is the public GitHub repository: `https://github.com/Silo-Server/silo-server`.

#### Requested remediation

In @tests/SiloPlayer.Tests/AdminSettingsCurrentParitySourceTests.cs around lines 24 - 25, Update the source assertions in AdminSettingsCurrentParitySourceTests to require the exact authoritative public GitHub Silo repository URL, https://github.com/Silo-Server/silo-server, rather than accepting any string containing a repository fragment. Preserve the assertion rejecting the Continuum source and validate the full URL or its parsed host and repository.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 145. Test current write keys, not only legacy fallback reads.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/AdminSettingsCurrentParitySourceTests.cs:111-120`
- **CodeRabbit ID:** `7ed8d045-5563-4f56-937e-b56519764c93`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Test current write keys, not only legacy fallback reads.**

These assertions verify legacy key names and `GetSettingValue`, but they do not verify any save target. A path that writes `subtitle_ai.*` keys can pass this test and leave current settings unchanged. Assert that save payloads use the current keys and exclude legacy keys except during fallback reads.

#### Requested remediation

In @tests/SiloPlayer.Tests/AdminSettingsCurrentParitySourceTests.cs around lines 111 - 120, Update AiSettingsReadLegacyValuesButAlwaysWriteCurrentKeys to verify save payloads target the current setting keys, while legacy keys appear only in fallback reads; add assertions covering current-key writes and rejecting legacy-key save targets.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 146. Move the guard before the second `IndexOf` call.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/AdminTasksParitySourceTests.cs:43-48`
- **CodeRabbit ID:** `345c6cb6-a850-40df-a7a6-9cde6296aa7c`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Move the guard before the second `IndexOf` call.**

If `_refreshMetrics = await adminApi.GetTaskMetricsAsync` is not found, `metricsAssignment` is `-1`. Line 44 then calls `string.IndexOf(string, int, StringComparison)` with `startIndex = -1`, which throws `ArgumentOutOfRangeException`. The assertion on line 46 never runs, so the intended message "Expected refresh metrics to be captured." is never reported. Order the guard first.

Line 47 is also weaker than the test name suggests. `rebuild > metricsAssignment` is true for any `RebuildTaskGroups();` occurrence after the assignment, including one inside an unrelated method. Consider bounding the search to the enclosing method or asserting on the awaited load call as well.




<details>
<summary>🐛 Proposed fix for the assertion order</summary>

```diff
         var metricsAssignment = CodeBehind.IndexOf("_refreshMetrics = await adminApi.GetTaskMetricsAsync", StringComparison.Ordinal);
-        var rebuild = CodeBehind.IndexOf("RebuildTaskGroups();", metricsAssignment, StringComparison.Ordinal);
-
-        Assert.True(metricsAssignment >= 0, "Expected refresh metrics to be captured.");
+        Assert.True(metricsAssignment >= 0, "Expected refresh metrics to be captured.");
+
+        var rebuild = CodeBehind.IndexOf("RebuildTaskGroups();", metricsAssignment, StringComparison.Ordinal);
         Assert.True(rebuild > metricsAssignment, "Expected task groups to rebuild after metrics are available.");
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/AdminTasksParitySourceTests.cs around lines 43 - 48, Move the metricsAssignment existence assertion before the rebuild IndexOf call so a missing refresh-metrics assignment reports the intended assertion message instead of passing -1 as the start index. In the AdminTasksParitySourceTests ordering check, also tighten the rebuild validation to the relevant enclosing method or verify the awaited load call so an unrelated RebuildTaskGroups occurrence cannot satisfy the test.

#### CodeRabbit suggestion

```text
var metricsAssignment = CodeBehind.IndexOf("_refreshMetrics = await adminApi.GetTaskMetricsAsync", StringComparison.Ordinal);
        Assert.True(metricsAssignment >= 0, "Expected refresh metrics to be captured.");

        var rebuild = CodeBehind.IndexOf("RebuildTaskGroups();", metricsAssignment, StringComparison.Ordinal);
        Assert.True(rebuild > metricsAssignment, "Expected task groups to rebuild after metrics are available.");
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 147. Remove the dependency on alignment whitespace.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/AdminUserDetailParitySourceTests.cs:34`
- **CodeRabbit ID:** `f1ed226c-3322-42f2-9414-cc577db08ed9`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Remove the dependency on alignment whitespace.**

`"Permissions              = permissions"` encodes an exact run of 14 spaces used for column alignment in the page source. Any reformatting of that object initializer, including a `dotnet format` run, breaks this test while the behavior stays the same. Normalize the whitespace before the check.




<details>
<summary>🐛 Proposed fix</summary>

```diff
-        Assert.Contains("Permissions              = permissions", Page, StringComparison.Ordinal);
+        Assert.Matches(@"Permissions\s*=\s*permissions", Page);
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/AdminUserDetailParitySourceTests.cs at line 34, Update the assertion in AdminUserDetailParitySourceTests to normalize or otherwise ignore whitespace alignment before checking for the Permissions assignment, while still verifying the same source content.

#### CodeRabbit suggestion

```text
Assert.Matches(@"Permissions\s*=\s*permissions", Page);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 148. The assertions do not prove the invariant in the test name.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/AdminUsersParitySourceTests.cs:74-81`
- **CodeRabbit ID:** `6d1dd9d1-9fcc-4a28-9adf-2c7e7a1b4c5b`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**The assertions do not prove the invariant in the test name.**

`InviteMutationsOnlyReportSuccessAfterApiSuccess` states an ordering guarantee. The four assertions only confirm that four strings exist somewhere in the code-behind. The test passes even if the code-behind shows the success toast before it inspects the result of `CreateInviteCodeAsync`, which is the exact regression the name targets. Assert on the relative position of the success call and the awaited result, or rename the test to describe what it checks.

#### Requested remediation

In @tests/SiloPlayer.Tests/AdminUsersParitySourceTests.cs around lines 74 - 81, Strengthen InviteMutationsOnlyReportSuccessAfterApiSuccess by asserting that the success notification statements occur after the awaited InviteCodesViewModel.CreateInviteCodeAsync result is captured and validated, rather than only checking that related strings exist. If the test cannot verify this ordering, rename it to reflect the narrower coverage.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 149. Make the negative assertion independent of line endings.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/ChapterThumbnailRealtimeParitySourceTests.cs:29`
- **CodeRabbit ID:** `95fdaa44-086f-43de-ad01-970371865ae2`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Make the negative assertion independent of line endings.**

The pattern embeds `\n` only. If the working copy uses CRLF, the substring can never match, so the assertion always passes and guards nothing. Normalize the source text before the check, or assert on a single-line marker.

<details>
<summary>♻️ Proposed fix</summary>

```diff
-        var source = File.ReadAllText(SourcePath("libs", "mpv", "scripts", "silo-osc.lua"));
+        var source = File.ReadAllText(SourcePath("libs", "mpv", "scripts", "silo-osc.lua"))
+            .Replace("\r\n", "\n");
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/ChapterThumbnailRealtimeParitySourceTests.cs at line 29, Update the negative assertion in ChapterThumbnailRealtimeParitySourceTests to avoid depending on a literal newline sequence: normalize source line endings before checking or assert against a single-line marker, while preserving the existing verification that the unwanted code is absent.

#### CodeRabbit suggestion

```text
var source = File.ReadAllText(SourcePath("libs", "mpv", "scripts", "silo-osc.lua"))
            .Replace("\r\n", "\n");

        Assert.DoesNotContain("osc-patch-chapter-thumbnail-url\", function(index, url)\n        state.chapter_thumbnails = {}", source);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 150. This negative assertion depends on line endings.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/CollectionsInteractionParitySourceTests.cs:14`
- **CodeRabbit ID:** `cdbbd120-d8e6-4425-bff2-9d9169bd4c87`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**This negative assertion depends on line endings.**

The expected pattern uses `\n` only. On a CRLF checkout the substring never matches, so the assertion cannot fail. Normalize the source text after reading, or assert on a single-line marker.

<details>
<summary>♻️ Proposed fix</summary>

```diff
-        var source = Read("src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs");
+        var source = Read("src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs")
+            .Replace("\r\n", "\n");
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/CollectionsInteractionParitySourceTests.cs at line 14, Update the source assertion in CollectionsInteractionParitySourceTests to avoid line-ending sensitivity by normalizing the loaded source text before checking it, or by asserting against a line-ending-independent single-line marker; preserve the existing negative assertion semantics for the card.Tapped handler.

#### CodeRabbit suggestion

```text
var source = Read("src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs")
            .Replace("\r\n", "\n");

        Assert.DoesNotContain("card.Tapped += (_, _) =>\n        {\n            ShowTemplateConfigInGallery", source);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 151. The ordering assertion passes when the first marker is absent.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/CurrentAdminParitySourceTests.cs:32-34`
- **CodeRabbit ID:** `ce7a973e-baba-4a7f-a56c-1ccabda42a09`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**The ordering assertion passes when the first marker is absent.**

`string.IndexOf` returns `-1` for a missing substring. If `var skippedRootsTask` is deleted, the comparison becomes `-1 < <index>`, which is true, so the test still passes. Assert that both markers exist before you compare their positions.

<details>
<summary>♻️ Proposed fix</summary>

```diff
-        Assert.True(
-            code.IndexOf("var skippedRootsTask", StringComparison.Ordinal) <
-            code.IndexOf("await ViewModel.LoadLibrariesAsync()", StringComparison.Ordinal));
+        var skippedRootsIndex = code.IndexOf("var skippedRootsTask", StringComparison.Ordinal);
+        var loadLibrariesIndex = code.IndexOf("await ViewModel.LoadLibrariesAsync()", StringComparison.Ordinal);
+        Assert.True(skippedRootsIndex >= 0);
+        Assert.True(loadLibrariesIndex >= 0);
+        Assert.True(skippedRootsIndex < loadLibrariesIndex);
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/CurrentAdminParitySourceTests.cs around lines 32 - 34, Update the ordering assertion in CurrentAdminParitySourceTests to first verify that both “var skippedRootsTask” and “await ViewModel.LoadLibrariesAsync()” markers are present, then compare their indices to enforce the required order.

#### CodeRabbit suggestion

```text
var skippedRootsIndex = code.IndexOf("var skippedRootsTask", StringComparison.Ordinal);
        var loadLibrariesIndex = code.IndexOf("await ViewModel.LoadLibrariesAsync()", StringComparison.Ordinal);
        Assert.True(skippedRootsIndex >= 0);
        Assert.True(loadLibrariesIndex >= 0);
        Assert.True(skippedRootsIndex < loadLibrariesIndex);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 152. These negative assertions depend on CRLF line endings.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/CurrentCatalogParitySourceTests.cs:136`
- **CodeRabbit ID:** `39f703a9-bd3a-4b88-b400-8c71107bc67c`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**These negative assertions depend on CRLF line endings.**

Both patterns embed `\r\n`. On a checkout with LF endings the substrings can never match, so the assertions always pass and guard nothing. Normalize the source text to `\n` after reading, and write the patterns with `\n`.

<details>
<summary>♻️ Proposed fix</summary>

```diff
-        var code = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
+        var code = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs")
+            .Replace("\r\n", "\n");
```

Then update the patterns to use `\n` only.
</details>





Also applies to: 144-144

#### Requested remediation

In @tests/SiloPlayer.Tests/CurrentCatalogParitySourceTests.cs at line 136, Normalize the source text read by CurrentCatalogParitySourceTests to use LF line endings before performing the negative assertions, then update both patterns in the affected assertions to use \n instead of \r\n. Preserve the existing assertion content and scope.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 153. Assert `initialize >= 0` before you use it as a start index.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/CurrentLibraryParitySourceTests.cs:9-13`
- **CodeRabbit ID:** `99754566-bb7d-4506-ba8d-12a6a69b5f2d`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Assert `initialize >= 0` before you use it as a start index.**

If `InitializeComponent();` is missing from the file, `initialize` is `-1`. Line 10 then calls `IndexOf(string, int, StringComparison)` with a negative start index and throws `ArgumentOutOfRangeException`. The test fails with an unclear message instead of the intended assertion.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         var initialize = source.IndexOf("this.InitializeComponent();", StringComparison.Ordinal);
-        var enableEvents = source.IndexOf("_suppressFilterEvents = false;", initialize, StringComparison.Ordinal);
+        Assert.True(initialize >= 0, "LibraryPage.xaml.cs no longer calls InitializeComponent().");
+        var enableEvents = source.IndexOf("_suppressFilterEvents = false;", initialize, StringComparison.Ordinal);
 
         Assert.Contains("private bool _suppressFilterEvents = true;", source, StringComparison.Ordinal);
-        Assert.True(initialize >= 0 && enableEvents > initialize);
+        Assert.True(enableEvents > initialize);
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/CurrentLibraryParitySourceTests.cs around lines 9 - 13, Update the CurrentLibraryParitySourceTests assertion flow to assert that initialize, the result of locating InitializeComponent, is nonnegative before passing it as the start index to the enableEvents IndexOf call; retain the existing ordering assertion afterward.

#### CodeRabbit suggestion

```text
var initialize = source.IndexOf("this.InitializeComponent();", StringComparison.Ordinal);
        Assert.True(initialize >= 0, "LibraryPage.xaml.cs no longer calls InitializeComponent().");
        var enableEvents = source.IndexOf("_suppressFilterEvents = false;", initialize, StringComparison.Ordinal);

        Assert.Contains("private bool _suppressFilterEvents = true;", source, StringComparison.Ordinal);
        Assert.True(enableEvents > initialize);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 154. Assert `loadStart >= 0` before you pass it as a start index.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/CurrentProfilesParitySourceTests.cs:66-69`
- **CodeRabbit ID:** `5489c39a-4398-438a-a44b-5bc8dceb03ba`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Assert `loadStart >= 0` before you pass it as a start index.**

If `LoadProfilesAsync` is renamed, `loadStart` is `-1`. Line 67 then throws `ArgumentOutOfRangeException` before the assertion at line 68 runs. Split the assertions so the failure message names the missing method.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         var loadStart = settings.IndexOf("private async Task LoadProfilesAsync", StringComparison.Ordinal);
-        var loadEnd = settings.IndexOf("private static Border ProfileBadge", loadStart, StringComparison.Ordinal);
-        Assert.True(loadStart >= 0 && loadEnd > loadStart);
+        Assert.True(loadStart >= 0, "SettingsPage.xaml.cs no longer defines LoadProfilesAsync.");
+        var loadEnd = settings.IndexOf("private static Border ProfileBadge", loadStart, StringComparison.Ordinal);
+        Assert.True(loadEnd > loadStart);
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/CurrentProfilesParitySourceTests.cs around lines 66 - 69, Split the combined assertion in the profile parity test so loadStart is asserted to be nonnegative before it is passed to the loadEnd IndexOf call. Then assert loadEnd is greater than loadStart before slicing the settings string, preserving a clear failure for a missing LoadProfilesAsync method.

#### CodeRabbit suggestion

```text
var loadStart = settings.IndexOf("private async Task LoadProfilesAsync", StringComparison.Ordinal);
        Assert.True(loadStart >= 0, "SettingsPage.xaml.cs no longer defines LoadProfilesAsync.");
        var loadEnd = settings.IndexOf("private static Border ProfileBadge", loadStart, StringComparison.Ordinal);
        Assert.True(loadEnd > loadStart);
        var load = settings[loadStart..loadEnd];
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 155. Guard the four marker offsets before you slice `window`.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/CurrentSettingsParitySourceTests.cs:225-230`
- **CodeRabbit ID:** `1863fb85-c883-4da6-9f3b-3e11e97792c3`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Guard the four marker offsets before you slice `window`.**

None of `transitionStart`, `transitionEnd`, `shellStart`, or `shellEnd` is checked. If `MainWindow.xaml.cs` renames any marker, the code throws `ArgumentOutOfRangeException` at line 226 or at the range expression on line 227. The test then reports a crash instead of the missing member.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         var transitionStart = window.IndexOf("public bool TryEnterAuthenticatedPage", StringComparison.Ordinal);
+        Assert.True(transitionStart >= 0, "MainWindow.xaml.cs no longer defines TryEnterAuthenticatedPage.");
         var transitionEnd = window.IndexOf("public void UpdateLibraryNavItems", transitionStart, StringComparison.Ordinal);
+        Assert.True(transitionEnd > transitionStart, "MainWindow.xaml.cs no longer defines UpdateLibraryNavItems after TryEnterAuthenticatedPage.");
         var transition = window[transitionStart..transitionEnd];
         var shellStart = window.IndexOf("public void ShowMainNavigation()", StringComparison.Ordinal);
+        Assert.True(shellStart >= 0, "MainWindow.xaml.cs no longer defines ShowMainNavigation.");
         var shellEnd = window.IndexOf("private async Task LoadShellNavigationAsync", shellStart, StringComparison.Ordinal);
+        Assert.True(shellEnd > shellStart, "MainWindow.xaml.cs no longer defines LoadShellNavigationAsync after ShowMainNavigation.");
         var shell = window[shellStart..shellEnd];
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/CurrentSettingsParitySourceTests.cs around lines 225 - 230, Validate transitionStart, transitionEnd, shellStart, and shellEnd in CurrentSettingsParitySourceTests before using either range expression, and report the missing marker through the test’s existing assertion mechanism instead of slicing window and throwing.

#### CodeRabbit suggestion

```text
var transitionStart = window.IndexOf("public bool TryEnterAuthenticatedPage", StringComparison.Ordinal);
        Assert.True(transitionStart >= 0, "MainWindow.xaml.cs no longer defines TryEnterAuthenticatedPage.");
        var transitionEnd = window.IndexOf("public void UpdateLibraryNavItems", transitionStart, StringComparison.Ordinal);
        Assert.True(transitionEnd > transitionStart, "MainWindow.xaml.cs no longer defines UpdateLibraryNavItems after TryEnterAuthenticatedPage.");
        var transition = window[transitionStart..transitionEnd];
        var shellStart = window.IndexOf("public void ShowMainNavigation()", StringComparison.Ordinal);
        Assert.True(shellStart >= 0, "MainWindow.xaml.cs no longer defines ShowMainNavigation.");
        var shellEnd = window.IndexOf("private async Task LoadShellNavigationAsync", shellStart, StringComparison.Ordinal);
        Assert.True(shellEnd > shellStart, "MainWindow.xaml.cs no longer defines LoadShellNavigationAsync after ShowMainNavigation.");
        var shell = window[shellStart..shellEnd];
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 156. The order assertion can pass when a section heading is deleted.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/CurrentSettingsParitySourceTests.cs:288-293`
- **CodeRabbit ID:** `1854cfd9-d939-40d9-b555-3985806c0640`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**The order assertion can pass when a section heading is deleted.**

`IndexOf` returns `-1` for a missing heading. A `-1` value is smaller than every real offset, so removing "New Episode Notifications" or "Browser Notifications" still satisfies line 293. Assert that each offset is non-negative first.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         var webhooks = control.IndexOf("Webhooks", StringComparison.Ordinal);
+        Assert.All(
+            new[] { episode, browser, email, discord, webhooks },
+            index => Assert.True(index >= 0, "A notification section heading is missing."));
         Assert.True(episode < browser && browser < email && email < discord && discord < webhooks);
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/CurrentSettingsParitySourceTests.cs around lines 288 - 293, Update the ordering assertion in CurrentSettingsParitySourceTests to first require that every IndexOf result for the notification headings is non-negative, then verify their relative order so missing sections cannot satisfy the comparison.

#### CodeRabbit suggestion

```text
var episode = control.IndexOf("New Episode Notifications", StringComparison.Ordinal);
        var browser = control.IndexOf("Browser Notifications", StringComparison.Ordinal);
        var email = control.IndexOf("Email Notifications", StringComparison.Ordinal);
        var discord = control.IndexOf("Discord Notifications", StringComparison.Ordinal);
        var webhooks = control.IndexOf("Webhooks", StringComparison.Ordinal);
        Assert.All(
            new[] { episode, browser, email, discord, webhooks },
            index => Assert.True(index >= 0, "A notification section heading is missing."));
        Assert.True(episode < browser && browser < email && email < discord && discord < webhooks);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 157. Move the native library acquisition inside the `try` block.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/MpvOscRuntimeTests.cs:18-25`
- **CodeRabbit ID:** `fdedac39-38e5-41ac-b4b2-ff43f95aceee`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Move the native library acquisition inside the `try` block.**

`NativeLibrary.Load` runs at Line 18, but the `try`/`finally` that calls `NativeLibrary.Free` starts at Line 27. Two failure paths leak the native module:

- `LoadDelegate` throws if any export is missing (Lines 19-23).
- `Assert.NotEqual(IntPtr.Zero, handle)` throws at Line 25 when `mpv_create` returns null.

In both cases the process keeps `libmpv-2.dll` loaded, and the file stays locked for the remainder of the test host.

<details>
<summary>♻️ Proposed fix to guard the native handle</summary>

```diff
         var library = NativeLibrary.Load(libraryPath);
-        var create = LoadDelegate<MpvCreate>(library, "mpv_create");
-        var setOption = LoadDelegate<MpvSetOptionString>(library, "mpv_set_option_string");
-        var initialize = LoadDelegate<MpvInitialize>(library, "mpv_initialize");
-        var commandString = LoadDelegate<MpvCommandString>(library, "mpv_command_string");
-        var terminate = LoadDelegate<MpvTerminateDestroy>(library, "mpv_terminate_destroy");
-        var handle = create();
-        Assert.NotEqual(IntPtr.Zero, handle);
-
+        var handle = IntPtr.Zero;
+        MpvTerminateDestroy? terminate = null;
         try
         {
+            var create = LoadDelegate<MpvCreate>(library, "mpv_create");
+            var setOption = LoadDelegate<MpvSetOptionString>(library, "mpv_set_option_string");
+            var initialize = LoadDelegate<MpvInitialize>(library, "mpv_initialize");
+            var commandString = LoadDelegate<MpvCommandString>(library, "mpv_command_string");
+            terminate = LoadDelegate<MpvTerminateDestroy>(library, "mpv_terminate_destroy");
+            handle = create();
+            Assert.NotEqual(IntPtr.Zero, handle);
+
             Set("vo", "null");
```

Then update the `finally` block:

```diff
         finally
         {
-            terminate(handle);
+            if (handle != IntPtr.Zero)
+                terminate?.Invoke(handle);
             NativeLibrary.Free(library);
         }
```

The local function `Set` must move inside the `try` block or capture `setOption` and `handle` from an outer declaration.
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/MpvOscRuntimeTests.cs around lines 18 - 25, Move NativeLibrary.Load and the related delegate initialization and handle creation into the existing try block so every failure path reaches cleanup. Ensure the finally block frees the library only when its handle was successfully acquired, and relocate the local Set function or required variable declarations so it still accesses setOption and handle.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 158. Assert `delayStart` before you use it as a search offset.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/MpvPlayerSourceTests.cs:161-166`
- **CodeRabbit ID:** `4b555b4f-4f02-42d2-b3fb-b88a4abc4ae1`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Assert `delayStart` before you use it as a search offset.**

Line 162 passes `delayStart` to `IndexOf` before Line 163 validates it. If `Task.Delay(200)` disappears from `MpvVideoWindow.cs`, `delayStart` is `-1`, and `string.IndexOf(string, int, StringComparison)` throws `ArgumentOutOfRangeException` at Line 162. The test then reports an unhandled exception instead of the intended assertion failure, which hides the real regression.

Move the guard above the second lookup.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         var delayStart = source.IndexOf("Task.Delay(200)", StringComparison.Ordinal);
-        var nextMember = source.IndexOf("        });", delayStart, StringComparison.Ordinal);
         Assert.True(delayStart >= 0);
+        var nextMember = source.IndexOf("        });", delayStart, StringComparison.Ordinal);
         Assert.True(nextMember > delayStart);
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/MpvPlayerSourceTests.cs around lines 161 - 166, Move the Assert.True check for delayStart immediately after its IndexOf call and before passing delayStart to the nextMember IndexOf lookup, preserving the existing assertions and delayedCorrection extraction in the surrounding test.

#### CodeRabbit suggestion

```text
var delayStart = source.IndexOf("Task.Delay(200)", StringComparison.Ordinal);
        Assert.True(delayStart >= 0);
        var nextMember = source.IndexOf("        });", delayStart, StringComparison.Ordinal);
        Assert.True(nextMember > delayStart);

        var delayedCorrection = source[delayStart..(nextMember + "        });".Length)];
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 159. Validate `start` before you pass it to the second `IndexOf`.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/NavigationResourceRegressionTests.cs:34-36`
- **CodeRabbit ID:** `a201eb1f-e9b0-4210-a82d-197903216e76`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Validate `start` before you pass it to the second `IndexOf`.**

Line 35 uses `start` as the search offset, but Line 36 validates it afterwards. If `NavView_ItemInvoked` is renamed or removed in `MainWindow.xaml.cs`, `start` is `-1` and Line 35 throws `ArgumentOutOfRangeException`. The test then fails with an unhandled exception rather than the intended assertion message.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         var start = source.IndexOf("private void NavView_ItemInvoked", StringComparison.Ordinal);
-        var end = source.IndexOf("private void SwitchProfile_Click", start, StringComparison.Ordinal);
-        Assert.True(start >= 0 && end > start);
+        Assert.True(start >= 0, "NavView_ItemInvoked was not found in MainWindow.xaml.cs.");
+        var end = source.IndexOf("private void SwitchProfile_Click", start, StringComparison.Ordinal);
+        Assert.True(end > start, "SwitchProfile_Click was not found after NavView_ItemInvoked.");
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/NavigationResourceRegressionTests.cs around lines 34 - 36, Validate that start, obtained from the NavView_ItemInvoked search, is nonnegative before calling IndexOf for SwitchProfile_Click; only perform the second search after that assertion, while preserving the existing assertion that both markers are found in the expected order.

#### CodeRabbit suggestion

```text
var start = source.IndexOf("private void NavView_ItemInvoked", StringComparison.Ordinal);
        Assert.True(start >= 0, "NavView_ItemInvoked was not found in MainWindow.xaml.cs.");
        var end = source.IndexOf("private void SwitchProfile_Click", start, StringComparison.Ordinal);
        Assert.True(end > start, "SwitchProfile_Click was not found after NavView_ItemInvoked.");
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 160. The multi-line assertion on Line 803 can pass vacuously on CRLF checkouts.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/PlayerServiceSourceTests.cs:799-804`
- **CodeRabbit ID:** `c643244c-530d-4d9d-ba7f-40562ed1a399`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**The multi-line assertion on Line 803 can pass vacuously on CRLF checkouts.**

The needle hardcodes `\n` between the lines. `PlayerService.cs` contains `\r\n` when the repository is checked out with CRLF line endings on Windows. The substring then never matches, and `Assert.DoesNotContain` succeeds regardless of the source content.

Assert on single-line fragments, or normalize line endings before the comparison.

<details>
<summary>♻️ Proposed fix</summary>

```diff
-        Assert.DoesNotContain("if (State != PlayerState.Idle)\n        {\n            _playbackManager?.Dispose();", source, StringComparison.Ordinal);
+        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
+        Assert.DoesNotContain("if (State != PlayerState.Idle)\n        {\n            _playbackManager?.Dispose();", normalized, StringComparison.Ordinal);
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/PlayerServiceSourceTests.cs around lines 799 - 804, Update the source assertion in PlayerServiceSourceTests so the DoesNotContain check is independent of CRLF versus LF line endings, either by normalizing source line endings before comparison or asserting the relevant single-line fragments separately. Preserve the intent of detecting the forbidden disposal block.

#### CodeRabbit suggestion

```text
Assert.Contains("else if (_postRollActive && !_postRollVideoEnded)", source, StringComparison.Ordinal);
        Assert.Contains("_videoWindow?.EnterPostRollPreview();", source, StringComparison.Ordinal);
        Assert.Contains("_closing = true;", source, StringComparison.Ordinal);
        Assert.Contains("_playbackManager.ProgressReportingFailed -= OnProgressReportingFailed;", source, StringComparison.Ordinal);
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain("if (State != PlayerState.Idle)\n        {\n            _playbackManager?.Dispose();", normalized, StringComparison.Ordinal);
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 161. This negative assertion cannot fail.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/RequestsCurrentParitySourceTests.cs:15`
- **CodeRabbit ID:** `7baf5054-fbe2-450b-888f-3d8a797baf6e`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**This negative assertion cannot fail.**

The needle embeds a bare `\n` and an exact 8-space indent. If the working copy uses CRLF line endings, the file text contains `\r\n`, so the substring never matches and the assertion passes for any content of `RequestsViewModel.cs`. The indent is also brittle. Normalize the line endings, or assert the two statements separately.

<details>
<summary>🔧 Proposed fix</summary>

```diff
-        Assert.DoesNotContain("StatusMessage = \"Searching...\";\n        SearchResults.Clear();", source);
+        var normalized = source.Replace("\r\n", "\n");
+        Assert.DoesNotContain("StatusMessage = \"Searching...\";\n        SearchResults.Clear();", normalized);
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/RequestsCurrentParitySourceTests.cs at line 15, Update the assertion in RequestsCurrentParitySourceTests so it reliably detects the prohibited StatusMessage assignment and SearchResults.Clear statements regardless of CRLF/LF endings or indentation; normalize the source before checking or assert the two statements independently, while preserving the negative assertion’s intent.

#### CodeRabbit suggestion

```text
var normalized = source.Replace("\r\n", "\n");
        Assert.DoesNotContain("StatusMessage = \"Searching...\";\n        SearchResults.Clear();", normalized);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 162. `Slice` throws an unclear exception when the start marker is missing.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/SearchRuntimeRegressionTests.cs:242-248`
- **CodeRabbit ID:** `ac284ab0-5f4f-4240-8945-4d45317bb09f`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**`Slice` throws an unclear exception when the start marker is missing.**

If `source` does not contain `start`, `startIndex` is `-1`. Line 245 then calls `IndexOf(end, -1)`, which throws `ArgumentOutOfRangeException` before the assertion on line 246 runs. Every caller of `Slice` then reports a confusing failure instead of the missing marker. Check `startIndex` first.

<details>
<summary>♻️ Proposed fix</summary>

```diff
     private static string Slice(string source, string start, string end)
     {
         var startIndex = source.IndexOf(start, StringComparison.Ordinal);
+        Assert.True(startIndex >= 0, $"Marker not found: {start}");
         var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
-        Assert.True(startIndex >= 0 && endIndex > startIndex);
+        Assert.True(endIndex > startIndex, $"Marker not found after start: {end}");
         return source[startIndex..endIndex];
     }
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/SearchRuntimeRegressionTests.cs around lines 242 - 248, Update the Slice helper to validate startIndex immediately after locating the start marker, before calling source.IndexOf for end. Preserve the assertion-based failure while ensuring a missing start marker produces the intended clear assertion failure instead of an ArgumentOutOfRangeException.

#### CodeRabbit suggestion

```text
private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Marker not found: {start}");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Marker not found after start: {end}");
        return source[startIndex..endIndex];
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 163. Handle a non-zero `git` exit code by falling back, and read the pipes without a deadlock risk.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/ServerContractSourceTests.cs:24-46`
- **CodeRabbit ID:** `31e76275-1e06-48ca-9097-ecb19d2da509`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Handle a non-zero `git` exit code by falling back, and read the pipes without a deadlock risk.**

Two problems exist in `TryListTrackedFiles`:

1. The method only catches `Win32Exception`, which covers a missing `git` executable. If `git` exists but the directory is not a work tree, for example in an exported source archive, `git ls-files` exits non-zero and line 39 fails the test. The `EnumerateProjectSourcePaths` fallback never runs, although that is its purpose.
2. The code reads stdout to the end, then stderr, then waits. If the child fills the stderr pipe buffer while the parent drains stdout, both processes block. Read both streams asynchronously and bound `WaitForExit`.

<details>
<summary>🔧 Proposed fix</summary>

```diff
             using var process = System.Diagnostics.Process.Start(startInfo)
                 ?? throw new InvalidOperationException("Failed to start git.");
-            var output = process.StandardOutput.ReadToEnd();
-            var error = process.StandardError.ReadToEnd();
-            process.WaitForExit();
-            Assert.True(process.ExitCode == 0, error);
-            return output;
+            var outputTask = process.StandardOutput.ReadToEndAsync();
+            var errorTask = process.StandardError.ReadToEndAsync();
+            if (!process.WaitForExit(30_000))
+            {
+                process.Kill(entireProcessTree: true);
+                return null;
+            }
+            var output = outputTask.GetAwaiter().GetResult();
+            _ = errorTask.GetAwaiter().GetResult();
+            return process.ExitCode == 0 ? output : null;
```
</details>

#### Requested remediation

In @tests/SiloPlayer.Tests/ServerContractSourceTests.cs around lines 24 - 46, Update TryListTrackedFiles to return null instead of asserting when git ls-files exits with a non-zero status, allowing EnumerateProjectSourcePaths to provide the fallback. Drain StandardOutput and StandardError concurrently and await process completion with a bounded timeout, preserving the existing missing-git fallback and avoiding pipe deadlocks.

#### CodeRabbit suggestion

```text
private static string? TryListTrackedFiles(string root)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git", "ls-files")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start git.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            var output = outputTask.GetAwaiter().GetResult();
            _ = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 164. Assert bearer authentication for the room request.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/WatchTogetherCurrentParityTests.cs:30-34`
- **CodeRabbit ID:** `9aa68112-e784-47cb-8a99-c48608f0efca`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Assert bearer authentication for the room request.**

The handler ignores the request headers. This test passes if `GetWatchTogetherRoomAsync` omits or misformats `Authorization`. Assert that the request contains `Bearer token` before returning the fixture response.

As per coding guidelines, "All authenticated requests: `Authorization: Bearer <access_token>`."

#### Requested remediation

In @tests/SiloPlayer.Tests/WatchTogetherCurrentParityTests.cs around lines 30 - 34, Update the request handler in the test around GetWatchTogetherRoomAsync to inspect the incoming request’s Authorization header and assert it equals Bearer token before returning the fixture response. Keep the existing room request and response assertions unchanged.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 165. Exercise bearer authentication in the connection test.

- [ ] **Status:** Unreviewed
- **Scope:** Tests
- **Location:** `tests/SiloPlayer.Tests/WebhookSyncTests.cs:11-17`
- **CodeRabbit ID:** `b1bd79a2-7cbf-43f9-b47e-b946ed7ddfda`
- **Fingerprint:** `phantom:medusa:tapir`
- **Category:** `SECURITY_AND_PRIVACY`

#### CodeRabbit explanation

**Exercise bearer authentication in the connection test.**

The test does not set an access token. `JsonHandler` does not inspect the request. The test passes if `GetConnectionsAsync` sends no `Authorization` header. Set a fixture token and assert `Bearer token` in `JsonHandler.SendAsync`.

As per coding guidelines, "All authenticated requests: `Authorization: Bearer <access_token>`."

#### Requested remediation

In @tests/SiloPlayer.Tests/WebhookSyncTests.cs around lines 11 - 17, Update the WebhookSyncApi connection test to configure a fixture access token on SiloApiClient and make JsonHandler.SendAsync validate that the request contains the Authorization header with the Bearer token value. Preserve the existing connection response and GetConnectionsAsync assertions.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 166. Add a re-entrancy guard for `DecideAsync`.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/ActivateDeviceViewModel.cs:302-310`
- **CodeRabbit ID:** `e67d200a-a02e-49dc-bde2-3f9aa24d2677`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Add a re-entrancy guard for `DecideAsync`.**

`DecideAsync` sets `IsActing` but never checks it. Two quick invocations (double activation of Approve, or Approve then Deny) send two decision calls for the same code. These endpoints are not idempotent; the second call can produce a confusing "already used" or "already denied" error after a successful first call.

<details>
<summary>🛡️ Proposed guard</summary>

```diff
 private async Task DecideAsync(bool approve)
 {
-    if (!HasActiveRequest) return;
+    if (!HasActiveRequest || IsActing) return;
 
     IsActing = true;
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/ActivateDeviceViewModel.cs around lines 302 - 310, Add an early re-entrancy check to DecideAsync so it returns when IsActing is already true, before setting IsActing or issuing any decision request. Preserve the existing HasActiveRequest check and normal behavior for the first invocation.

#### CodeRabbit suggestion

```text
private async Task DecideAsync(bool approve)
    {
        if (!HasActiveRequest || IsActing) return;

        IsActing = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 167. Raise change notification for `HasActiveFilters` and `ActiveFilterCount`.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminActivityViewModel.cs:38-43`
- **CodeRabbit ID:** `bab87aee-b47d-48d7-b69a-3b14410b3930`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Raise change notification for `HasActiveFilters` and `ActiveFilterCount`.**

Both properties derive from `MethodFilter`, `NodeFilter`, `TypeFilter`, and `SearchText`. No code calls `OnPropertyChanged` for them. Any binding to the filter badge or the "clear filters" affordance keeps the value from the first evaluation.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-    partial void OnSearchTextChanged(string value) => ApplyFilters();
-    partial void OnMethodFilterChanged(string? value) => ApplyFilters();
-    partial void OnNodeFilterChanged(string? value) => ApplyFilters();
-    partial void OnTypeFilterChanged(string? value) => ApplyFilters();
+    partial void OnSearchTextChanged(string value) { RaiseFilterFlags(); ApplyFilters(); }
+    partial void OnMethodFilterChanged(string? value) { RaiseFilterFlags(); ApplyFilters(); }
+    partial void OnNodeFilterChanged(string? value) { RaiseFilterFlags(); ApplyFilters(); }
+    partial void OnTypeFilterChanged(string? value) { RaiseFilterFlags(); ApplyFilters(); }
     partial void OnSortFieldChanged(string value) => ApplyFilters();
     partial void OnSortAscendingChanged(bool value) => ApplyFilters();
+
+    private void RaiseFilterFlags()
+    {
+        OnPropertyChanged(nameof(HasActiveFilters));
+        OnPropertyChanged(nameof(ActiveFilterCount));
+    }
```
</details>





Also applies to: 89-98

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminActivityViewModel.cs around lines 38 - 43, Update the filter-change handlers in AdminActivityViewModel—especially OnSearchTextChanged, OnMethodFilterChanged, OnNodeFilterChanged, and OnTypeFilterChanged—to raise property-change notifications for HasActiveFilters and ActiveFilterCount after applying filters, so bindings refresh whenever any contributing filter changes.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 168. Clear stale results and report failures in `LookupIPAsync`.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminActivityViewModel.cs:127-140`
- **CodeRabbit ID:** `391c4953-2123-4d14-aa17-43aa1b5aca46`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Clear stale results and report failures in `LookupIPAsync`.**

`catch { }` discards the error. `IPLookupResults` keeps the rows from the previous IP address. The user then sees results that belong to a different IP address and no failure indication.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         catch { }
+        catch (Exception ex)
+        {
+            IPLookupResults.Clear();
+            ErrorMessage = ex.Message;
+        }
         finally { IpLookupLoading = false; }
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminActivityViewModel.cs around lines 127 - 140, Update LookupIPAsync to clear IPLookupResults before starting the request, and replace the empty catch with the view model’s established error-reporting mechanism so lookup failures are visible to the user. Preserve the loading-state cleanup in finally and the existing successful result population.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 169. Avoid `TimeSpan.FromDays(int.MaxValue)` in the fallback branch.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminDevicesViewModel.cs:337-341`
- **CodeRabbit ID:** `64e3a6a6-e94b-4ad4-9a3b-a60c8b1a99fd`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Avoid `TimeSpan.FromDays(int.MaxValue)` in the fallback branch.**

`TimeSpan.FromDays` accepts a maximum of about 10,675,199 days. The `_ => int.MaxValue` fallback therefore throws `ArgumentOutOfRangeException` for any `RecencyFilter` value outside the three known strings. The fallback is intended to disable the filter, so skip the filter instead.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         if (RecencyFilter != "Any activity")
         {
-            var days = RecencyFilter switch { "Last 24 hours" => 1, "Last 7 days" => 7, "Last 30 days" => 30, _ => int.MaxValue };
-            query = query.Where(d => d.LastUpdated is { } when && DateTimeOffset.UtcNow - when < TimeSpan.FromDays(days));
+            int? days = RecencyFilter switch { "Last 24 hours" => 1, "Last 7 days" => 7, "Last 30 days" => 30, _ => null };
+            if (days is { } window)
+                query = query.Where(d => d.LastUpdated is { } when && DateTimeOffset.UtcNow - when < TimeSpan.FromDays(window));
         }
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminDevicesViewModel.cs around lines 337 - 341, Update the RecencyFilter handling in the AdminDevicesViewModel query flow so unknown filter values disable recency filtering instead of converting int.MaxValue with TimeSpan.FromDays. Apply the Where predicate only for the three recognized recency values, while preserving the existing filtering behavior for those values and the unfiltered behavior for “Any activity”.

#### CodeRabbit suggestion

```text
if (RecencyFilter != "Any activity")
        {
            int? days = RecencyFilter switch { "Last 24 hours" => 1, "Last 7 days" => 7, "Last 30 days" => 30, _ => null };
            if (days is { } window)
                query = query.Where(d => d.LastUpdated is { } when && DateTimeOffset.UtcNow - when < TimeSpan.FromDays(window));
        }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 170. `LoadAsync` clears every success message.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminInviteCodesViewModel.cs:27-47`
- **CodeRabbit ID:** `1af5d563-6ad3-46c8-88c4-f4d632b3a72e`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**`LoadAsync` clears every success message.**

Line 32 sets `StatusMessage = null`. `CreateInviteCodeAsync`, `TopUpInviteCodeAsync`, `ToggleInviteCodeAsync`, and `DeleteInviteCodeAsync` all set `StatusMessage` and then await `LoadAsync()`. The reload discards the message, so no confirmation reaches the user. Set the message after the reload.

<details>
<summary>🐛 Proposed fix (apply the same order to each mutating method)</summary>

```diff
             var code = await _adminApi.CreateInviteCodeAsync(request);
-            StatusMessage = $"Invite code \"{code.Code}\" created.";
             await LoadAsync();
+            StatusMessage = $"Invite code \"{code.Code}\" created.";
             return code;
```

```diff
             await _adminApi.UpdateInviteCodeAsync(code.Id, new UpdateInviteCodeRequest
             {
                 Enabled = !code.Enabled
             });
-            StatusMessage = code.Enabled ? "Invite code disabled." : "Invite code enabled.";
+            var disabled = code.Enabled;
             await LoadAsync();
+            StatusMessage = disabled ? "Invite code disabled." : "Invite code enabled.";
```
</details>





Also applies to: 71-151

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminInviteCodesViewModel.cs around lines 27 - 47, Update CreateInviteCodeAsync, TopUpInviteCodeAsync, ToggleInviteCodeAsync, and DeleteInviteCodeAsync so each calls LoadAsync before assigning its success StatusMessage; remove or avoid clearing StatusMessage during that reload so the confirmation remains visible, while preserving LoadAsync’s existing loading and error behavior.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 171. Set the status message after the reload.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminLibrariesViewModel.cs:300-312`
- **CodeRabbit ID:** `878cdcb8-240e-45d8-88d4-1dc11f2fd73f`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Set the status message after the reload.**

`LoadAsync` calls `LoadLibrariesAsync`, which sets `StatusMessage = null` at Line 77. Line 308 sets the message before Line 309 awaits the reload, so the confirmation is discarded. `CreateLibraryForEditorAsync` and `DeleteLibraryAsync` already use the correct order.

<details>
<summary>🐛 Proposed fix</summary>

```diff
             await _adminApi.ConfirmEmptyRootCleanupAsync(id);
-            StatusMessage = "Empty root cleanup confirmed.";
             await LoadAsync();
+            StatusMessage = "Empty root cleanup confirmed.";
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminLibrariesViewModel.cs around lines 300 - 312, Update ConfirmEmptyRootCleanupAsync so it awaits LoadAsync before assigning the success StatusMessage, preserving the existing reload behavior and ensuring the confirmation remains visible.

#### CodeRabbit suggestion

```text
[RelayCommand]
    private async Task ConfirmEmptyRootCleanupAsync(int id)
    {
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            await _adminApi.ConfirmEmptyRootCleanupAsync(id);
            await LoadAsync();
            StatusMessage = "Empty root cleanup confirmed.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 172. Fix the `SummaryLastSeen` boundary condition.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminLogsViewModel.cs:189-190`
- **CodeRabbit ID:** `ddced33a-a109-4225-94cf-56e0aeb675ae`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Fix the `SummaryLastSeen` boundary condition.**

Line 190 requires more than one timestamp. When the session has exactly one matching log entry, `SummaryFirstSeen` shows a time and `SummaryLastSeen` shows "-". Use the same `Count > 0` test as Line 189.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         SummaryFirstSeen = timestamps.Count > 0 ? FormatDateTime(timestamps[0]) : "-";
-        SummaryLastSeen = timestamps.Count > 1 ? FormatDateTime(timestamps[^1]) : "-";
+        SummaryLastSeen = timestamps.Count > 0 ? FormatDateTime(timestamps[^1]) : "-";
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminLogsViewModel.cs around lines 189 - 190, Update SummaryLastSeen to use a timestamps.Count > 0 condition, matching SummaryFirstSeen, so sessions with exactly one timestamp display that timestamp instead of "-".

#### CodeRabbit suggestion

```text
SummaryFirstSeen = timestamps.Count > 0 ? FormatDateTime(timestamps[0]) : "-";
        SummaryLastSeen = timestamps.Count > 0 ? FormatDateTime(timestamps[^1]) : "-";
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 173. Clear `ErrorMessage` when the export starts.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminMaintenanceViewModel.cs:116-132`
- **CodeRabbit ID:** `829e5ba5-1262-4d30-b11c-34daedca497d`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Clear `ErrorMessage` when the export starts.**

Line 121 resets `StatusMessage` only. A failure from a previous action stays visible. After a successful export start, the page then shows "Export job queued." together with the stale error. `SubmitImportAsync` at Line 138 clears both.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         IsStartingExport = true;
+        ErrorMessage = null;
         StatusMessage = null;
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminMaintenanceViewModel.cs around lines 116 - 132, Update StartExportAsync to clear ErrorMessage when a new export begins, alongside the existing StatusMessage reset, matching the behavior of SubmitImportAsync.

#### CodeRabbit suggestion

```text
[RelayCommand]
    public async Task StartExportAsync()
    {
        if (IsStartingExport) return;
        IsStartingExport = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            // All libraries (null library_ids) mirrors the webui's default "Start Export" button.
            await _adminApi.CreateExportJobAsync(new CatalogSeedExportRequest());
            StatusMessage = "Export job queued.";
            await RefreshExportJobsAsync();
            await RefreshAllJobsAsync();
        }
        catch (Exception ex) { ErrorMessage = $"Failed to start export: {ex.Message}"; }
        finally { IsStartingExport = false; }
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 174. Load profiles in the changed handler, or correct the comment.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminPlaybackHistoryViewModel.cs:32-37`
- **CodeRabbit ID:** `b43f6efb-b560-4638-a531-8d422ef0b780`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Load profiles in the changed handler, or correct the comment.**

The comment at Line 60 states that profiles load reactively in `OnSelectedUserIdChanged`. The handler only clears `SelectedProfileId` and `Profiles`. The profile list therefore stays empty until the next `LoadAsync` call, so the profile filter shows no options right after a user is selected.

<details>
<summary>♻️ Proposed fix</summary>

```diff
     partial void OnSelectedUserIdChanged(int? value)
     {
-        // When user changes, clear profile selection
         SelectedProfileId = null;
         Profiles.Clear();
+        if (value is int userId)
+            _ = LoadProfilesAsync(userId, CancellationToken.None);
     }
```
</details>





Also applies to: 60-63

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminPlaybackHistoryViewModel.cs around lines 32 - 37, Update OnSelectedUserIdChanged to reload profiles for the newly selected user after clearing the previous selection and Profiles, ensuring the profile filter is populated immediately; otherwise correct the nearby comment so it no longer claims reactive profile loading.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 175. Do not label the result set with an unrelated title.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminPlaybackHistoryViewModel.cs:87`
- **CodeRabbit ID:** `0da2af58-38bc-453b-bd38-810d470994b9`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Do not label the result set with an unrelated title.**

`ActiveMediaItemLabel` falls back to the first row title even when `MediaItemId` is empty. With no media filter applied, the label then shows the title of an arbitrary history row.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-            ActiveMediaItemLabel = Items.FirstOrDefault()?.MediaTitle ?? MediaItemId ?? "";
+            ActiveMediaItemLabel = string.IsNullOrWhiteSpace(MediaItemId)
+                ? ""
+                : Items.FirstOrDefault()?.MediaTitle ?? MediaItemId;
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminPlaybackHistoryViewModel.cs at line 87, Update the ActiveMediaItemLabel assignment in the AdminPlaybackHistoryViewModel so it uses MediaItemId only when a media filter is provided, and otherwise remains empty; remove the fallback to Items.FirstOrDefault()?.MediaTitle.

#### CodeRabbit suggestion

```text
ActiveMediaItemLabel = string.IsNullOrWhiteSpace(MediaItemId)
                ? ""
                : Items.FirstOrDefault()?.MediaTitle ?? MediaItemId;
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 176. Raise notifications for `EditorStep` and `ActivationTarget` at the end of `SelectDocumentAsync`.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminPolicyViewModel.cs:55-76`
- **CodeRabbit ID:** `f7d8451f-7930-4803-83e8-26b288bd875c`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Raise notifications for `EditorStep` and `ActivationTarget` at the end of `SelectDocumentAsync`.**

`OnSourceChanged` is the only place that notifies `EditorStep` and `ActivationTarget`. If the new document source equals the current `Source` value, the setter at Line 74 does not raise a change, so no notification occurs. `_seedIsActive` and `_activationTarget` still changed, and the editor keeps the previous step and activation target.

<details>
<summary>♻️ Proposed fix</summary>

```diff
         Source = _seedSource;
         StatusMessage = null;
+        OnPropertyChanged(nameof(EditorStep));
+        OnPropertyChanged(nameof(ActivationTarget));
     }
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminPolicyViewModel.cs around lines 55 - 76, At the end of SelectDocumentAsync, explicitly raise property-change notifications for EditorStep and ActivationTarget after assigning Source and updating _seedIsActive and _activationTarget. Preserve the existing OnSourceChanged behavior while ensuring these dependent properties refresh even when Source’s value does not change.

#### CodeRabbit suggestion

```text
public async Task SelectDocumentAsync(PolicyDocument document)
    {
        SelectedDocument = document;
        Replace(Versions, await api.GetPolicyVersionsAsync(document.Id));
        var seedSummary = document.ActiveVersionId is long activeId
            ? Versions.FirstOrDefault(version => version.Id == activeId)
            : Versions.FirstOrDefault();
        var seed = document.ActiveVersion?.Source is not null
            ? document.ActiveVersion
            : seedSummary is not null
                ? await api.GetPolicyVersionAsync(document.Id, seedSummary.Id)
                : null;
        _seedSource = seed?.Source ?? "";
        _seedIsActive = seed is not null && seed.Id == document.ActiveVersionId;
        _validatedSource = null;
        _savedSource = null;
        _activationTarget = !_seedIsActive && seedSummary?.CompiledOk == true ? seedSummary : null;
        CompileIssues.Clear();
        Comment = "";
        Source = _seedSource;
        StatusMessage = null;
        OnPropertyChanged(nameof(EditorStep));
        OnPropertyChanged(nameof(ActivationTarget));
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 177. Refresh the sensitive status after the page-level save, and keep dirty state accurate on partial failure.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminSettingsDetailViewModel.cs:241-263`
- **CodeRabbit ID:** `86edc63a-f22f-42a2-aacd-63dcdbfabe47`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Refresh the sensitive status after the page-level save, and keep dirty state accurate on partial failure.**

`SaveSettingsAsync` refreshes `_sensitiveConfigured` and `_managedByEnv` after a save at Lines 184-194. `SaveAsync` does not. After a page-level save of a sensitive key, `IsSensitiveConfigured` and `IsManagedByEnv` return stale values until the next load.

If one `UpdateAdminSettingAsync` call fails in the middle of the loop, the already-persisted keys stay in `_dirtySettings` and `_settings` keeps the old values, so the merged view from `GetEffectiveSettings` no longer matches the server.

<details>
<summary>♻️ Proposed fix</summary>

```diff
-            foreach (var (key, value) in _dirtySettings)
+            foreach (var (key, value) in _dirtySettings.ToArray())
             {
                 var response = await _adminApi.UpdateAdminSettingAsync(key, value);
                 LastSaveRequiresRestart |= response.RestartRequired;
+                _settings[key] = value;
+                _dirtySettings.Remove(key);
             }
@@
             // Reload to get fresh values
             _settings = await _adminApi.GetAdminSettingsAsync();
+            try
+            {
+                var (configured, managed) = await _adminApi.GetSensitiveStatusAsync();
+                _sensitiveConfigured = configured;
+                _managedByEnv = managed;
+            }
+            catch { }
             _dirtySettings.Clear();
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminSettingsDetailViewModel.cs around lines 241 - 263, Update SaveAsync to refresh _sensitiveConfigured and _managedByEnv after the page-level save, matching SaveSettingsAsync so IsSensitiveConfigured and IsManagedByEnv reflect persisted values. Also handle partial failures in the _dirtySettings update loop by reloading server settings and retaining only changes that were not successfully persisted, keeping _settings, dirty state, and GetEffectiveSettings consistent.

#### CodeRabbit suggestion

```text
try
        {
            foreach (var (key, value) in _dirtySettings.ToArray())
            {
                var response = await _adminApi.UpdateAdminSettingAsync(key, value);
                LastSaveRequiresRestart |= response.RestartRequired;
                _settings[key] = value;
                _dirtySettings.Remove(key);
            }

            if (DirtyRateLimitConfig != null)
            {
                var response = await _adminApi.UpdateRateLimitConfigAsync(DirtyRateLimitConfig);
                LastSaveRequiresRestart |= response.RestartRequired;
                RateLimitConfig = DirtyRateLimitConfig;
                DirtyRateLimitConfig = null;
            }

            // Reload to get fresh values
            _settings = await _adminApi.GetAdminSettingsAsync();
            try
            {
                var (configured, managed) = await _adminApi.GetSensitiveStatusAsync();
                _sensitiveConfigured = configured;
                _managedByEnv = managed;
            }
            catch { }
            _dirtySettings.Clear();
            HasDirtyChanges = false;
            DirtyCount = 0;
            StatusMessage = "Settings saved successfully.";
        }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 178. Avoid rebuilding `History` on every silent refresh, and guard against out-of-order responses.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/Admin/AdminTaskDetailViewModel.cs:34-65`
- **CodeRabbit ID:** `9ad8d071-5dc1-4da3-9425-aa56f5e32408`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `STABILITY_AND_AVAILABILITY`

#### CodeRabbit explanation

**Avoid rebuilding `History` on every silent refresh, and guard against out-of-order responses.**

`RefreshSilentAsync` is intended for polling. Each call clears `History` and re-adds every row. A bound list then loses selection and scroll position on each poll. The coding guidelines require a lightweight, stable UI where scrolling and navigation do not freeze.

`LoadInternalAsync` also has no cancellation or version guard. If a poll response arrives after a newer explicit load, it overwrites `TaskDetail` and `History` with older data. The sibling view models in this change set use a `CancellationTokenSource` swap or a `_loadVersion` counter for this purpose.

<details>
<summary>♻️ Proposed fix</summary>

```diff
+    private int _loadVersion;
+
     private async System.Threading.Tasks.Task LoadInternalAsync(string taskKey, bool showLoading)
     {
         if (string.IsNullOrWhiteSpace(taskKey)) return;
+        var version = Interlocked.Increment(ref _loadVersion);
@@
             await System.Threading.Tasks.Task.WhenAll(infoTask, historyTask);
+            if (version != _loadVersion) return;
 
             TaskDetail = infoTask.Result;
 
-            History.Clear();
-            foreach (var h in historyTask.Result) History.Add(h);
+            ReplaceHistory(historyTask.Result);
```

Implement `ReplaceHistory` so it updates only the rows that changed, keyed by execution id.
</details>
As per coding guidelines: "Lightweight, stable, optimized UI. Scrolling and navigation should not freeze."

#### Requested remediation

In @src/SiloPlayer/ViewModels/Admin/AdminTaskDetailViewModel.cs around lines 34 - 65, Update LoadInternalAsync to prevent stale responses from overwriting newer loads by adding a cancellation-token swap or load-version guard, and ensure polling requests are invalidated when a newer load begins. Replace the unconditional History.Clear/repopulate logic with a ReplaceHistory helper keyed by execution id that updates, inserts, and removes only changed rows while preserving existing item instances and ordering where possible.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 179. Report failed item additions on the create path.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:340-357`
- **CodeRabbit ID:** `4fd208ae-5797-4399-bcc4-666c997d07b3`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `DATA_INTEGRITY_AND_INTEGRATION`

#### CodeRabbit explanation

**Report failed item additions on the create path.**

The loop swallows every `AddCollectionItemAsync` failure. The following `ReorderCollectionItemsAsync` call then sends media item IDs that the server never stored, so the reorder request can fail as a whole. The user sees a saved collection with missing items and no message.

Track the IDs that were added, reorder only those, and set `ErrorMessage` when at least one add fails.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-                if (CollectionType == "manual" && ManualItems.Count > 0)
-                {
-                    foreach (var item in ManualItems)
-                    {
-                        try
-                        {
-                            await _collectionsApi.AddCollectionItemAsync(created.Id, item.MediaItemId);
-                        }
-                        catch
-                        {
-                            // Continue adding remaining items even if one fails
-                        }
-                    }
-
-                    await _collectionsApi.ReorderCollectionItemsAsync(
-                        created.Id,
-                        ManualItems.Select(item => item.MediaItemId).ToList());
-                }
+                if (CollectionType == "manual" && ManualItems.Count > 0)
+                {
+                    var addedIds = new List<string>(ManualItems.Count);
+                    var failed = 0;
+                    foreach (var item in ManualItems)
+                    {
+                        try
+                        {
+                            await _collectionsApi.AddCollectionItemAsync(created.Id, item.MediaItemId);
+                            addedIds.Add(item.MediaItemId);
+                        }
+                        catch
+                        {
+                            failed++;
+                        }
+                    }
+
+                    if (addedIds.Count > 0)
+                        await _collectionsApi.ReorderCollectionItemsAsync(created.Id, addedIds);
+                    if (failed > 0)
+                        ErrorMessage = $"{failed} item(s) could not be added to the collection.";
+                }
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs around lines 340 - 357, Update the manual-item creation flow around AddCollectionItemAsync to track successfully added media item IDs, record whether any addition fails, and set ErrorMessage when at least one failure occurs. Pass only the successfully added IDs to ReorderCollectionItemsAsync, preserving the existing behavior of continuing through remaining items.

#### CodeRabbit suggestion

```text
if (CollectionType == "manual" && ManualItems.Count > 0)
                {
                    var addedIds = new List<string>(ManualItems.Count);
                    var failed = 0;
                    foreach (var item in ManualItems)
                    {
                        try
                        {
                            await _collectionsApi.AddCollectionItemAsync(created.Id, item.MediaItemId);
                            addedIds.Add(item.MediaItemId);
                        }
                        catch
                        {
                            failed++;
                        }
                    }

                    if (addedIds.Count > 0)
                        await _collectionsApi.ReorderCollectionItemsAsync(created.Id, addedIds);
                    if (failed > 0)
                        ErrorMessage = $"{failed} item(s) could not be added to the collection.";
                }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 180. Clear the undo state when the dismiss request fails.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/HomeViewModel.cs:623-642`
- **CodeRabbit ID:** `bdf861a4-ad2f-4329-85b0-8822528ce5ba`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Clear the undo state when the dismiss request fails.**

The catch block calls `RestoreDismissedItem`, but it leaves `ShowUndoBanner` true and keeps `_lastDismissedItem` and `_lastDismissedSection` set. The banner therefore stays visible for an item that is already back in the row. If the user then presses Undo and `UndoDismissalAsync` succeeds, `RestoreDismissedItem` runs a second time and inserts the same `MediaItem` instance again, so the row shows a duplicate entry.

Hide the banner and clear the dismiss state in the failure path.

<details>
<summary>🐛 Proposed fix</summary>

```diff
         catch
         {
             // Restore on failure
             RestoreDismissedItem();
+            ShowUndoBanner = false;
+            ClearDismissState();
+            return;
         }
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/HomeViewModel.cs around lines 623 - 642, Update the catch block in the dismiss flow around RestoreDismissedItem to hide the undo banner and clear _lastDismissedItem and _lastDismissedSection after restoring the item, preventing a later undo from restoring it again.

#### CodeRabbit suggestion

```text
try
        {
            var body = surface == "continue_watching"
                ? new { progress_updated_at = request.Item.ProgressUpdatedAt }
                : (object)new { series_id = request.Item.SeriesId ?? request.Item.ContentId };

            if (surface == "continue_watching" && string.IsNullOrWhiteSpace(request.Item.ProgressUpdatedAt))
                throw new InvalidOperationException("Continue-watching item is missing its progress timestamp.");

            await _homeApi.DismissItemAsync(surface, request.Item.ContentId, body);
        }
        catch
        {
            // Restore on failure
            RestoreDismissedItem();
            ShowUndoBanner = false;
            ClearDismissState();
            return;
        }

        // Auto-hide after 5 seconds
        _ = AutoHideUndoBannerAsync();
    }
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 181. Do not use season number 0 as a sentinel.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/ItemDetailViewModel.cs:430-441`
- **CodeRabbit ID:** `145d2396-2ea2-4999-8880-e013b758e137`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Do not use season number 0 as a sentinel.**

`SelectSeasonAsync` returns when `seasonNumber == 0`. Season 0 is the standard number for a specials season. If the series exposes a specials season, the user cannot load its episodes, and the auto-select at Line 413 also does nothing when `Seasons[0].SeasonNumber` is 0. Use a nullable parameter or a separate "no selection" sentinel such as `-1`.

<details>
<summary>🐛 Proposed fix</summary>

```diff
-        if (Item == null || seasonNumber == 0) return;
+        if (Item == null || seasonNumber < 0) return;
```

Update `SelectedSeasonNumber` initialization at Line 176 to `-1` so the reset state stays distinct from season 0.
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/ItemDetailViewModel.cs around lines 430 - 441, Remove seasonNumber == 0 as a guard in SelectSeasonAsync so specials season 0 can load, and use a distinct no-selection sentinel instead. Initialize SelectedSeasonNumber to -1 and update the auto-selection logic to recognize -1 rather than treating season 0 as unselected.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 182. Fix the age calculation across leap years.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/PersonDetailViewModel.cs:137-143`
- **CodeRabbit ID:** `5bf1c7e5-e273-490c-a529-baa1d8b79583`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Fix the age calculation across leap years.**

`ComputeAge` compares `DayOfYear` values. `DayOfYear` shifts by one between leap and non-leap years for all dates after February. A person born on 1 March 1996 (leap year, `DayOfYear` 61) evaluated on 1 March 2025 (`DayOfYear` 60) gets one year subtracted, so the displayed age is one year too low. The same comparison appears at Line 77 in `AgeDisplay`.

Compare month and day instead.




<details>
<summary>🐛 Proposed fix</summary>

```diff
     private static int ComputeAge(string? birthDate, string? endDate)
     {
         if (!DateTime.TryParse(birthDate, out var birth) || !DateTime.TryParse(endDate, out var end)) return 0;
         var age = end.Year - birth.Year;
-        if (end.DayOfYear < birth.DayOfYear) age--;
+        if (end.Month < birth.Month || (end.Month == birth.Month && end.Day < birth.Day)) age--;
         return age;
     }
```

Apply the same change at Line 77:

```diff
-            if (endDate.DayOfYear < birth.DayOfYear) age--;
+            if (endDate.Month < birth.Month || (endDate.Month == birth.Month && endDate.Day < birth.Day)) age--;
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/PersonDetailViewModel.cs around lines 137 - 143, Update ComputeAge and the corresponding AgeDisplay age calculation to compare end.Month/end.Day against birth.Month/birth.Day instead of using DayOfYear, preserving the existing year subtraction behavior while avoiding leap-year errors.

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 183. Compare the URL scheme without case sensitivity.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/ServerSelectViewModel.cs:63-68`
- **CodeRabbit ID:** `8d17b546-9d93-4a64-85da-50c46820149c`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `FUNCTIONAL_CORRECTNESS`

#### CodeRabbit explanation

**Compare the URL scheme without case sensitivity.**

`StartsWith("http://")` and `StartsWith("https://")` use the culture-sensitive, case-sensitive overload. A user who types `HTTP://192.168.1.10:8096` passes neither check, so the code produces `https://HTTP://192.168.1.10:8096` and stores an invalid server URL. Use `StringComparison.OrdinalIgnoreCase`.




<details>
<summary>🐛 Proposed fix</summary>

```diff
         var url = NewServerUrl.Trim();
-        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
+        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
+            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
         {
             url = "https://" + url;
         }
```
</details>

#### Requested remediation

In @src/SiloPlayer/ViewModels/ServerSelectViewModel.cs around lines 63 - 68, Update the scheme checks in the server URL normalization flow to use StringComparison.OrdinalIgnoreCase for both HTTP and HTTPS prefixes, preserving correctly formed URLs regardless of scheme casing and avoiding an extra https:// prefix.

#### CodeRabbit suggestion

```text
var url = NewServerUrl.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }
        url = ServerUrlIdentity.Normalize(url);
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---

### 184. Remove the legacy repository reference from this comment.

- [ ] **Status:** Unreviewed
- **Scope:** View models
- **Location:** `src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs:13-15`
- **CodeRabbit ID:** `997874ee-abad-44a5-a1e3-df14cc7d2659`
- **Fingerprint:** `phantom:medusa:komodo`
- **Category:** `MAINTAINABILITY_AND_CODE_QUALITY`

#### CodeRabbit explanation

**Remove the legacy repository reference from this comment.**

The comment names `continuum-server` as the parity source. The authoritative parity source is the public Silo server repository. Point the comment at that repository instead.

<details>
<summary>📝 Proposed comment fix</summary>

```diff
-// Two ViewModels backing the two Watch Party pages. Kept in the same file so the
-// page pair ships as a single shadow of continuum-server's
-// WatchTogetherJoin.tsx + WatchTogetherRoomPage.tsx.
+// Two ViewModels backing the two Watch Party pages. Kept in the same file so the
+// page pair ships as a single shadow of the Silo server WebUI pages
+// WatchTogetherJoin.tsx + WatchTogetherRoomPage.tsx.
```
</details>

Based on learnings: "Never fetch, inspect, compare against, cite, or use the legacy private GitLab Continuum repository for Silo desktop parity work." The only authoritative source is the public GitHub repository `https://github.com/Silo-Server/silo-server`.

#### Requested remediation

In @src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs around lines 13 - 15, Update the comment above the WatchTogether view models to replace the legacy continuum-server parity reference with the authoritative public Silo server repository, using the repository URL https://github.com/Silo-Server/silo-server.

#### CodeRabbit suggestion

```text
// Two ViewModels backing the two Watch Party pages. Kept in the same file so the
// page pair ships as a single shadow of the Silo server WebUI pages
// WatchTogetherJoin.tsx + WatchTogetherRoomPage.tsx.
```

#### Codex disposition

- **Decision:** _Pending_
- **Evidence:** _Pending_
- **Validation:** _Pending_

---


