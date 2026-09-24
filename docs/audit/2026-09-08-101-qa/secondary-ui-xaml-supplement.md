# Transferred account/onboarding XAML coverage

Client/backend reviewer completed this explicit transfer from secondary_ui: all 1,472 lines in ten full XAML files below, plus SettingsPage.xaml:1911–2233 (323 lines). secondary_ui already read SettingsPage.xaml:1–1910; only the combined review claims that file in full. No unreadable lines, source edits, app interaction, or tests run in this slice. Findings are source-derived; code-behind and host navigation integration belong to secondary_ui.

| File under D:/SiloPlayer/src/SiloPlayer/Views/ | Full file lines | Coverage here | Decision |
| --- | ---: | --- | --- |
| ActivateDevicePage.xaml | 277 | 1–277 | Keep explicit pending/approved/denied/expired states; share a scrollable auth shell. |
| DownloadsPage.xaml | 130 | 1–130 | Keep states; replace manually populated StackPanel with bounded/virtualized items presentation if results are not capped. |
| HouseholdSetupPage.xaml | 113 | 1–113 | Keep scrollable setup, named profile buttons and repeater; unify shared profile tile/presentation. |
| InviteClaimPage.xaml | 111 | 1–111 | Keep differentiated invalid-link/network/retry states; make form scrollable and explicitly label credential inputs. |
| LoginPage.xaml | 278 | 1–278 | Keep scrollable auth shell, fallback device code, loading/error and server branding; extract shared shell. |
| ProfileSelectPage.xaml | 76 | 1–76 | Keep named profile buttons and first-profile state; add bounded scrolling and share profile tile model. |
| ServerSelectPage.xaml | 127 | 1–127 | Keep virtualized bounded server list; make overall form scrollable and label icon/input controls. |
| ServerSetupRequiredPage.xaml | 61 | 1–61 | Keep correct user-only WebUI handoff; adaptive actions/layout for narrow windows. |
| SignupPage.xaml | 209 | 1–209 | Keep credential input types/error feedback; add scrolling and recovery navigation when signup closed. |
| TasteSeedPage.xaml | 90 | 1–90 | Keep repeater, accessible selection names, loading-more and empty state; bound image decode/request work through common artwork pipeline. |
| SettingsPage.xaml | 2233 | 1911–2233 only | Split settings into independently loaded panels and share form fields; previous reviewer covers remainder. |

## Findings and qualifications

1. **Short-window forms can clip required actions (high source confidence, no visual reproduction).** SignupPage.xaml:31 centers an unscrollable StackPanel containing five inputs, labels, spacing, error/status and actions. ActivateDevicePage.xaml:31, InviteClaimPage.xaml:43, ProfileSelectPage.xaml:30 and ServerSelectPage.xaml:18 likewise omit an outer ScrollViewer. At reduced window height or larger text these forms cannot offer scrolling to content outside the viewport. LoginPage.xaml:34 already has the appropriate scrollable shell; HouseholdSetupPage.xaml:27 does too. Reuse that shell and verify 125–200% text/display scaling and short windows. No claim that every default-size state clips.

2. **Closed signup hides its only page return link (high confidence in XAML, host navigation may mitigate).** SignupPage.xaml:45 shows signup-closed content, but :205 binds the BackToLogin button's visibility to ShowSignupForm. When signups close between navigation and server-status load, the closed state has no local sign-in action. Keep the return action independent of form eligibility. Ask the host reviewer whether window-level back navigation is available before describing this as a complete trap.

3. **Form accessibility is inconsistent (high confidence in declared markup; runtime naming may mitigate).** SettingsPage.xaml:1913, :1923, :1927, :1950 and :1952 and InviteClaimPage.xaml:75/:80 rely on adjacent text instead of explicit Header, LabeledBy or AutomationProperties.Name. Login's inputs have descriptive placeholders, while its provider ComboBox:144 has only an adjacent label. ServerSelectPage.xaml:70 has a tooltip-only icon button. Custom CSS at SettingsPage.xaml:2073 and profile/taste buttons have explicit names, demonstrating the better pattern already exists. A shared labeled field/control factory should give stable semantic names, and live validation/error updates should be announced. Confirm the code-behind's global accessibility helpers before treating missing XAML labels as observed unnamed controls.

4. **Large settings/download lists inherit eager construction costs (architectural observation, magnitude unmeasured).** DownloadsPage.xaml:95–97 places a manually populated StackPanel inside a ScrollViewer. Settings tail uses similar hosts for webhook events, providers, community themes, devices, connections, profiles and sessions. A rebuild should use typed items and bounded/virtualized presentation, with independently instantiated settings panels. This markup alone cannot establish result counts or a runtime freeze; the owning code-behind reviewer evaluates bounds and load behavior.

Shared positives: external server setup stays in the WebUI as required by user-only scope; image backgrounds are decorative/Raw and do not intercept clicks; password inputs use PasswordBox; destructive device denial is separate from approval; explicit empty/loading/error states and current-account device context are present. Layout parity was not visually verified in this source-only slice.
