# AI disclaimer

Most of this repository was written by **Claude Opus 5** (Anthropic), driven by the maintainer
in a single interactive session. This file states plainly what was generated, what was
verified, and what was not — so that anyone reading the code knows what they are looking at.

The maintainer has read and validated the code.

## What was generated

Everything under `SWGLLauncher/`, `Tools/SWGLManifest/` and `server/`: the WinForms launcher,
the manifest generator, the ProFTPD configuration and the beta-provisioning script. Comments
are in French, the user interface is in English, both by request.

Not generated: the artwork, composed by **Devis** from the game's own images; `swgl-sp.ico`,
taken from the OpenJK-SWGL engine repository; and everything the maintainer decided —
specifications, update model, FTP layout, beta credentials scheme, cleanup rules, interface
layout.

## What was verified, and how

Claims below were checked by running code, not by inspection alone.

- **Update engine** — tested end to end against a throwaway FTP server written for the
  occasion, reproducing the real mount-point layout. Covered: initial download, removal of a
  file dropped from the manifest, repair of a corrupted `.pk3` detected by checksum, and
  resumption of an interrupted download (`REST 100000` in the server log, correct final
  checksum).
- **Steam shortcuts** — read and rewrote a copy of a real `shortcuts.vdf`: byte-identical
  round-trip, entry added without touching the existing one, second call updating in place
  rather than duplicating, artwork written under the names Steam expects.
- **Jedi Outcast import** — all four folder-resolution cases, three files copied with matching
  SHA-256, no leftover `.part`, re-import overwriting cleanly.
- **Cleanup** — planning verified on a synthetic install: stray folders and obsolete `.pk3`
  removed, stock game files, `base/`, saves, player configuration and launcher files kept.
- **Packaging** — published executable launched from a folder containing nothing but itself
  and its configuration: window, embedded background and embedded music all load.
- **Automatic recovery** — the test server was made to stall halfway through a transfer: the
  launcher hit the read timeout, reconnected, resumed at the right offset (`REST 100000`) and
  finished with a correct checksum.
- **Log panel** — rendered off-screen after a failed login, opened automatically with the
  error and its cause; beta passwords confirmed masked, including in verbose FTP logging.

## Verified in production by the maintainer

- **FTPS updates against the real server**, over the maintainer's own connection. Large
  `.pk3` transfers there lost their control connection mid-transfer; the automatic recovery
  took over, e.g. a 756.5 MB file resumed and verified four seconds after the interruption.
- **The ProFTPD configuration** was applied on the target Ubuntu 24.04 machine. Two directives
  failed with Ubuntu's packaging and were fixed afterwards: `mod_tls` ships in
  `proftpd-mod-crypto`, and `IdentLookups` needs a module that is not installed.

## What was not verified

- **`server/add-beta.sh`** has not been confirmed to run on the target machine.
- **Steam library rendering.** Artwork file names and the `shortcuts.vdf` format were verified,
  but no shortcut was ever written to a live Steam profile.
- **Interface rendering on other DPI settings.** The window was checked at 100 % only.

## Known caveats

- The launcher lives inside the folder it synchronises, so it cannot update itself: a
  `SWGLLauncher.exe` listed in a manifest would fail to be replaced while running.
- The cleanup deletes files. Its protection list (`sync.keep`) is a plain list of names and
  patterns — an install that does not match the expected layout should be checked with
  *Verify only* before the first update.

## Model

Claude Opus 5, then Claude Opus 5.5, via Claude Code. Commits carry a `Co-Authored-By`
trailer naming the model that wrote them.
