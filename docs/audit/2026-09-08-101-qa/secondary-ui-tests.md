# Secondary UI test-source reconciliation

All 13 requested secondary test files have now been read in full. Five were already read during the initial audit and were not reread; the remaining eight (380 lines) were read during reconciliation. EbookReaderWebPolicyTests was separately read earlier, making 14 focused test files in the main coverage ledger. No tests were executed.

The requested inventory is in the `files` array of `secondary-ui-tests-coverage.json`; the main implementation ledger retains its 62 implementation files and lists all 14 tests separately under `tests`.

| Test | Lines | Review phase |
|---|---:|---|
| CurrentAuthSetupParitySourceTests.cs | 47 | test_inventory_reconciliation |
| CurrentProfilesParitySourceTests.cs | 92 | initial_audit |
| CurrentSettingsParitySourceTests.cs | 385 | initial_audit |
| DateTimePreferenceSourceTests.cs | 46 | test_inventory_reconciliation |
| DownloadsCurrentParityTests.cs | 77 | initial_audit |
| EbookReaderCurrentParityTests.cs | 93 | initial_audit |
| NotificationsCurrentParitySourceTests.cs | 43 | test_inventory_reconciliation |
| NotificationsInteractionParitySourceTests.cs | 29 | test_inventory_reconciliation |
| RequestBrowseCurrentParitySourceTests.cs | 54 | test_inventory_reconciliation |
| RequestDetailCurrentParitySourceTests.cs | 42 | test_inventory_reconciliation |
| RequestsCurrentParitySourceTests.cs | 51 | test_inventory_reconciliation |
| WatchProvidersParitySourceTests.cs | 68 | test_inventory_reconciliation |
| WatchTogetherCurrentParityTests.cs | 87 | initial_audit |

The eight newly read files are entirely source-text assertions plus repository-location helpers. Keep these as inexpensive architecture/contract guards, and describe their scope precisely:

- Auth setup checks the presence of signup state and latest-load ownership tokens. It does not execute cancellation, token completion, or stale navigation.
- Date/time checks two exact bypass spellings and the fallback format string. It does not establish all formatting paths, preference updates, time zones, or culture output.
- Notification tests check separate optional load paths, snapshots/version tokens and focus/automation declarations. They do not exercise response ordering, server write ordering, actual focus visibility or screen-reader announcements.
- Request browse/detail tests check retry labels, toast calls, ownership tokens and markup states. They do not distinguish successful mutation followed by failed refresh, prove active reachability of actions, or execute keyboard navigation.
- Requests tests check search cancellation tokens and card action declarations. They do not test owned-request pagination, cancel action reachability or eager rebuild cost.
- Watch provider tests check route/control/schema/auth-method strings. They do not exercise API-key/device-code workflows, cancellation, busy-time lost writes or failed-save rollback.

These observations reinforce the behavioral-test recommendations in secondary-ui.md; they are not new claims that the test suite fails or passes. The five previously reviewed tests and the ebook origin-policy behavior tests retain the review notes already recorded.
