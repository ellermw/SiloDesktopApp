# Viewer localization refresh

## Baseline and source

- **Desktop base:** Beta `bdd033c1c`. Version numbers are left to the pipeline.
- **Catalogs:** silo-translations release `2026.11.16-20af7f8`, tarball SHA256
  `e3be70437abe1190ba4daef6bd9774906201c6ab501c308e852eb9af097945e6`. They were
  imported with `tools/SiloTranslationsImport` from official Silo Server
  `17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8` (`web/src/locales`, keys SHA256
  `f22874bd…`), excluding `web.admin.`.
- **Later commits:** official main through `8f93594ce` makes no further
  locale changes.
- **Import input:** a Git checkout with `core.autocrlf=false`, so the file
  bytes match the pinned blobs. A CRLF working copy changes the hashed bytes.

## Changes

- The catalogs grow from 8 to 22 languages: nl, it, pl, tr, sv, da, nb, fi,
  cs, hr, ru, uk, ko and zh-Hant are added, with 6,895 messages each.
- **Plural rules:** CLDR cardinal rules cover the new languages, including
  Slavic fractions, Danish, Italian millions, Bokmål and Traditional Chinese.
- **Language matching:** `no` maps to `nb`, and zh-TW, zh-HK and zh-MO resolve to
  zh-Hant. Saved language tags and separate formatting preferences are kept.
- **OAuth sign-in buttons:** they use the localized "Sign in with {provider}"
  sentence and accessible name, read the current provider, and refresh on
  reload. Network-provider buttons are unchanged.
- **Audiobook text:** the listening hero uses the surviving `hero.ariaLabel`.
  The retired chapter-count and chapter-number keys have explicit readable
  fallbacks.
- **Catalog contents:** the catalogs include the 9 new viewer keys and the 32
  new notification keys, and drop the 6 removed audiobook keys. Adding the
  notification keys doesn't implement the notification inbox, which is a
  separate change.

## Verification

Local results on this source:

- **Unit tests:** all 8,578 pass on current Beta, including `ViewerLocalizationRefreshTests`,
  the exact release, language order, hash and key contracts, and the
  `SourceWalkingResolvesThisCheckoutNotPhysicalBuildRoot` proof.
- **`viewer-localization` native:** passes all 96 checks. They cover login,
  the retained hero, UIA names and the translated untitled chapter title.
- **`shell-localization` native:** completes with all checks passing.

## Reconciled with #182

Books #182 merged first and replaced the old audiobook chapter code in
`ItemDetailPage.xaml.cs`, including this change's three chapter fallback
hunks. The merge keeps #182's layout. Of the six retired keys, only
`web.audiobooks.player.chapterNumber` is still called
(`AudiobookTransportAppearance`), and it carries an explicit fallback. Untitled
chapter rows now use the surviving `web.audiobooks.chapter.fallbackTitle`. The
source guard checks every remaining retired-key call in `src` for an explicit
fallback. The native case checks that an untitled chapter row shows the
translated chapter title in English, Polish, Russian and Korean.

## Not certified

Narrator, physical input, font fallback for Korean and Traditional Chinese,
and the DPI matrix.
