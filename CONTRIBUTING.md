# Feedback and contributions

Beta feedback is welcome. Please use this repository's Issues page for a reproducible bug or feature request.

## Bug reports

Include:

- GHMR version and Windows version.
- Game edition, store, executable build and scripting runtime version.
- Current mission, previous game, and Random order versus diagnostic/fixed order.
- What you expected, what happened, and any input/manual closure you used.
- Relevant `GHMR-controller.log` lines or a sanitized log attachment; add the affected runtime log if useful.
- Screenshot/clip with a timestamp at the failing boundary.

Use Options > Open Logs. Remove private paths/account information before publishing. Do not post credentials, personal saves, game binaries or complete game folders. Say if a trainer/mod menu was used so QA attempts are interpreted correctly.

## Code changes

Read [DEVELOPMENT.md](docs/DEVELOPMENT.md), keep changes focused, and describe the behavior and validation in your pull request. Game-build support needs native in-game evidence; simulated tests alone cannot establish compatibility.

Do not include runtime logs, crash dumps, saves, local settings, secrets, debug output or unrelated third-party binaries. GHMR-authored code uses the MIT licence; dependencies/media keep their own terms.
