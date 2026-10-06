# Beta publication checklist

## Completed development validation

- Controller v0.4.14's recorded Random order QA run reached Finished / 5/5 with zero failures on 6 October 2026.
- The SA → VC → V → IV → III route completed all four handoffs.
- Recorded transition-track audio stopped before each destination's main menu.
- The final game remained open as intended.

This is development QA evidence, not a claim of a counted clean/unassisted run, every permutation, or every platform. Documentation/package-only revisions do not require repeating the entire run unless executable behavior/assets or new failures change the evidence.

## Before attaching the public asset

1. Upload all prepared files, then the workflow last if using GitHub web uploads. Verify the exact commit's Actions run passes.
2. Extract the built inner beta ZIP and inspect its complete file list. `START-HERE.txt`, docs, adapters, config, media, licence, `release.json` and checksums must be present.
3. Check `release.json`: source commit must be the final packaging commit and controller version must be 0.4.14. Check the external ZIP hash.
4. Smoke-open the freshly extracted app on Windows. Confirm Options shows v0.4.14, setup sees all five packaged adapters, and the media toggle/settings still open. Reinstall bridges only if changing the packaged scripts or fixing an installation.
5. Confirm no personal saves, runtime logs, credentials, account paths, debug databases or third-party runtime binaries are included. Do not post the private QA recordings/logs as release assets.
6. The creator supplied the transition thumbnails/music and reports no required music attribution. Preserve the source/permission record if available; don't invent a verified licence or apply the code's MIT licence to media.
7. If a named antivirus detection exists for this exact artifact, resolve it with the vendor before distributing it. The beta is unsigned; disclose that without recommending disabled security.
8. Create tag `v0.4.14-beta.1` at the verified source commit. Mark the GitHub release **pre-release** and attach the built Windows ZIP plus its `.sha256` file. Use the prepared release notes.

## GitHub presentation

- Root README is the short player-facing beta guide; history belongs in Changelog/development notes.
- About description, channel link and relevant topics match the project.
- Issues and bug-report templates are available for community feedback.
- Keep source/build history. Do not delete old Actions runs or rewrite Git history just to clean the front page.
- Older public Releases can be described as superseded. Permanently removing assets/runs/history needs a deliberate decision, not cosmetic cleanup.
- The repository and release must be accessible to the intended audience; review visibility separately before publishing.

See [GITHUB-RELEASE.md](GITHUB-RELEASE.md) for the exact upload/build/publish steps.
