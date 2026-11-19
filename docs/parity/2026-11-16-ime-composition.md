# IME composition guard

## Baseline and scope

- Desktop base: Beta `212333915` (1.2.1168-beta.1). Version numbers are left
  to the pipeline.
- Upstream: official Silo Server #4287 (`9eb3df6f3`, "ignore Enter and arrow
  keys while an input method is composing"), read from official main
  `5b3dcaa2f`. Upstream adds `web/src/lib/imeComposition.ts`
  (`isComposing || keyCode === 229`) and returns early from custom text-field
  key handlers such as `GlobalSearch`, `TagInput`, `ManualContentsPanel` and
  the searchable selects. Its admin-only sites are outside this user client.

## Behavior

Japanese and Chinese typists press Enter to confirm a conversion, arrows to
pick a candidate, Escape to cancel and Backspace to edit the reading. While a
composition is active those keys belong to the IME. They never submit,
navigate, move a selection, close a dialog, commit a tag or remove the last
tag.

- `ImeCompositionState` (Core) is the shared state machine. A key is left to
  the IME while `TextCompositionStarted` has not been followed by
  `TextCompositionEnded`, or when Windows reports VK_PROCESSKEY (229) in `Key`
  or `OriginalKey`. This is the native counterpart of upstream's
  `isComposing || keyCode === 229`.
- WinUI can end the composition before the confirming key reaches a handler,
  as Safari does in the browser. The first Enter, Escape, Backspace or arrow key
  within 200 ms of the end is therefore still treated as the IME's. Any key,
  or the end of that window, uses up this allowance. The next ordinary Enter
  acts exactly once, even when the IME swallowed the confirming key and no
  key event reached the handler.
- `ImeCompositionGuard.Attach(TextBox)` binds a TextBox's composition events
  once (repeat calls return the same state). It resets on `LostFocus` so a
  missed end event can never leave Enter blocked. Every site attaches in its
  constructor, before a composition can start, and checks the guard first in
  its handler. The TextBox and IME have already processed a guarded key before
  these handlers run. Other guarded keys continue routing unhandled. A guarded
  Enter or Escape is marked handled, so a parent ContentDialog cannot use it
  for its default button or close action. This covers Global Search and Edit
  Metadata.
- Tags: `MetadataTagsInput` ignores guarded keys in its KeyDown handler. Its
  TextChanged comma commit waits while a composition is active. Enter, blur and
  `CommitPending` still split on commas, so confirmed text containing a comma
  becomes tags on the next commit.

## Files changed

- `src/SiloPlayer.Core/Services/ImeCompositionState.cs` (new): the state
  machine.
- `src/SiloPlayer/Helpers/ImeCompositionGuard.cs` (new): binds it to a
  TextBox and adds a `KeyRoutedEventArgs` overload.
- `src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs`: the handled-events-too
  `SearchBox_KeyDown` returns first for IME keys before Ctrl+K, arrows,
  Enter or Escape.
- `src/SiloPlayer/Views/SearchPage.xaml.cs`: the Enter search is skipped for
  the confirming key.
- `src/SiloPlayer/Views/CollectionEditorPage.xaml.cs`: manual-title search
  Enter, Up, Down and Escape are left to the IME.
- `src/SiloPlayer/Controls/MetadataTagsInput.cs`: Enter, Backspace and the
  TextChanged comma commit are guarded. The edit stays inside the constructor's
  key/text handlers, so it merges with PR #190's `SetValues` insertion.
- Tests: `tests/SiloPlayer.Tests/ImeCompositionStateTests.cs`,
  `tests/SiloPlayer.Tests/ImeCompositionParitySourceTests.cs`, and one state
  check added to `ConditionalEditorsNativeFixture.TagsAsync`.

## Verification

Local results on this source:

- **Unit tests:** all 7,547 pass, including `ImeCompositionStateTests` and
  `ImeCompositionParitySourceTests`.
- **Native, passing:** `conditional-editors` (19 checks, including the tag
  field's composition comma case), `boxset-search` (33) and
  `boxset-search-quick` (21).
- **Already failing on Beta:** `collections-presentation`,
  `collections-localization`, `browse-parity` and `browse-acceptance` fail
  identically on clean Beta `212333915`, with the same pass counts and
  failure lines. This change does not cause them.

Still needs a manual check with a real Japanese IME and Chinese Pinyin in all
four places:

- Enter confirms a candidate, and the next Enter acts once.
- Candidate arrows don't move the result highlight.
- Escape cancels only the composition.
- Backspace and comma behave during composition.
- The 200 ms confirming-key window holds up on real hardware.

## Limitations and follow-ups

- The 200 ms confirm allowance is a heuristic. A second Enter pressed within
  200 ms of a composition ending will be ignored once, but only when the IME
  swallowed the confirming key. Manual IME testing should check the timing on
  real hardware.
- Other custom Enter handlers on free-text fields are not covered by this pass.
  They include the collection limit fields, Requests, Watch Together join, login
  and signup, match dialog and allowed titles. They can attach the same guard
  when audited. Numeric limit boxes do not usually host an IME.
