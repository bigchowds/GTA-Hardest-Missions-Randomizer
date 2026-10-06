# Controller Support

GHMR's controller app supports keyboard and XInput-compatible gamepads.

## Menu controls

| Input | Action |
|---|---|
| D-pad or left stick | Move between enabled buttons |
| A / Cross through Steam Input | Select |
| B / Circle through Steam Input | Focus **Stop Run**; A confirms |
| Tab / Shift+Tab | Keyboard navigation |
| Enter / Space | Keyboard selection |

Gamepad navigation is active only while the GHMR controller window has focus.
When GTA has focus, GHMR does not react to gameplay button presses. Mission
progression is automatic after the designated save reaches free roam. If a
Trilogy title stops at its own landing or save-selection screen, select the
clean baseline or **Resume** once; gameplay timing has not started at that
point. GHMR deliberately does not send a blind Enter pulse into those menus.

GTA V Enhanced has a separate guarded confirmation for its blocking restore-point Alert. Its game
script pauses normal bridge ticks, so bridge v0.1.13 emits one main-keyboard
Enter down/up pair after requesting Derailed. The bridge first verifies that
the foreground window belongs to the current GTA process, stops after one
complete pair and does not navigate menus or send gameplay controls.

Vice City additionally has an experimental Auto Resume option: it recognizes the English landing menu locally and tries one targeted Resume action. If it is not recognized or accepted, choose Resume manually. It never blindly navigates to New Game.

The Definitive Edition trilogy has a separate Rockstar startup workaround that
does not emulate a keyboard. For two minutes after GHMR starts SA DE, GTA III
DE or Vice City DE, it looks for the complete known **Connecting to Social
Club** continue/cancel dialog and posts a click directly to that dialog's native
OK button. It requires the exact English message and both OK and Cancel; other
Rockstar or Social Club warnings remain untouched.

## Recommended Steam shortcut

GHMR reads XInput directly. To let Steam Input translate a controller that Steam recognises, add the released `GHMR.Controller.exe` as a non-Steam game:

1. In Steam, choose **Games → Add a Non-Steam Game to My Library**.
2. Browse to and add `GHMR.Controller.exe`.
3. Open that shortcut's controller settings and enable the appropriate Steam Input layout.
4. Launch GHMR from the Steam shortcut.

This is expected to cover Xbox, PlayStation and other controllers for which Steam provides a working layout. It does not promise compatibility with literally every controller.

## Rockstar Launcher fallback

Steam Input does not always propagate from one application to every launcher-spawned child process. If a GTA game does not receive the translated controller, use the setup already confirmed during development:

1. In Steam, choose **Games → Add a Non-Steam Game to My Library**.
2. Add Rockstar Games Launcher.
3. Open the shortcut's controller settings and enable the appropriate Steam Input layout.
4. Start Rockstar Games Launcher from that Steam shortcut, then launch the required GTA game.

This fallback is for the game itself. GHMR does not install controller drivers, remap buttons globally or modify Steam configuration. Keyboard and direct XInput support remain available without Steam.

## Safety rule

No required game-bridge action is assigned to a gameplay button. A game bridge observes mission state and responds to controller-app commands without intercepting movement, aiming, shooting, pause or retry inputs.
