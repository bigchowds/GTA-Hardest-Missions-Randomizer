# v0.4.4 withdrawn

Do not use or release v0.4.4.

Live testing found that its synchronous transition-audio cleanup could hold the
serialized bridge-event path at **Preparing**. The mission could appear in-game
without GHMR receiving later lifecycle updates, while the audio cleanup itself
could also fail and leave music playing.

v0.4.5 replaces this experiment with non-blocking UI-thread cleanup and is
rebuilt from the proven v0.4.3 source.
