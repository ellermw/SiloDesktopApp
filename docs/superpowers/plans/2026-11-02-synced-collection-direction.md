# Synced collection direction implementation plan

**Goal:** Make both sort directions editable without losing saved configuration.
**Spec:** ../specs/2026-11-02-synced-collection-direction.md
**Architecture:** Retain the ViewModel's existing field:order value and API serialization; separate native field and direction controls in the shared new/saved synced Order section.
**Constraints:** Work/build below D:/SiloPlayer; official source remains at established server reference. No version edits/main promotion/installed-player access. All .NET commands hold SiloOct18Build after pinned mpv verification and use fresh outputs/synthetic fixtures.
**Review focus:** Saved opposite direction, unknown untouched fields/configuration, explicit source order, save/read-only guards, compact keyboard-accessible layout.

- [x] Native regression against frozen exact544 Release reproduces a missing Direction control in the synced Order grid. Original broader fixture selection was corrected to query this specific grid and fails for the intended missing control.
- [x] Build a shared field/direction refresh with event suppression and default-on-field-change. Retain unknown saved fields without mutation and disable direction when it cannot be safely edited.
- [ ] Verify actual new/saved UI, direction contract, unknown config preservation, read-only/save guards and 1280/460px geometry. Run full unit suite, Release and native checks; fingerprint source/artifacts.
- [ ] Commit locally to pin source, independent Standards/Spec review, recheck remote head and open same-repository beta PR. Pipeline owns merge/stamps/package/release.
