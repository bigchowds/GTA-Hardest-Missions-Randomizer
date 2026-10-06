# Changelog

## v0.4.14-beta.1 — first five-mission beta package

Controller and bridge versions remain those tested in v0.4.14. This revision prepares documentation, public repository presentation and release packaging; it does not change mission selection, event delivery, failure/retry or handoff logic.

- Five supported missions, one per game, with fresh random order and a Trilogy mission first.
- Confirmed completion advances between games; final success shows Finished / 5/5 and leaves the last game open.
- Derailed result recognition can advance without waiting for Story Mode cleanup; the verified completed GTA V process closes without its interactive quit confirmation.
- Readable diagnostic logs, fixed-order QA routes, transition thumbnails and optional music.
- Player installation/baseline/compatibility guides, release metadata and checksums added to the package.
- Clarified GTA IV's required pre–Three Leaf Clover save and the actual packaged VC bridge version.

Developer QA on 6 October 2026 completed SA → VC → V → IV → III with 5/5 and zero recorded failures. Other environments/permutations remain community-testing territory. The beta is unsigned.

Historical implementation notes remain under `docs/V*-UPDATE-NOTES.md` and the game-specific test documents. Those notes may describe older behavior; the current README and installation guide are authoritative for this beta.
