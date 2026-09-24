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
- **Cleanup** — planning verified on a synthetic install: only obsolete files matching
  `sync.deletable` removed (case-insensitive, `*` not crossing folders); stock game files, other
  `base/` files, other mods, saves, `.part` files and launcher files kept. Forced
  deletions from the manifest's `delete` list applied, except to a file the channel still
  publishes or to a protected launcher file; old manifests without the key still load. Files
  recorded in `installed.json` (installed by the launcher, e.g. for a beta) removed once the
  channel drops them, even outside the patterns; a tracked launcher file kept.
- **Packaging** — published executable launched from a folder containing nothing but itself
  and `SWGL\launcher_music.mp3`, without any configuration file: window, embedded background and
  music all load.
- **Automatic recovery** — the test server was made to stall halfway through a transfer: the
  launcher hit the read timeout, reconnected, resumed at the right offset (`REST 100000`) and
  finished with a correct checksum.
- **Patch notes** — end to end against the test FTP server: notes read after the manifest,
  link shown, two clicks open a single window, Markdown rendered (headings, lists, code,
  quote, table, link), a `<script>` in the notes shown as text and never run.
- **Self-update** — a 1.0.0 build pointed at a local stand-in for the GitHub API found a 9.9.9
  release, downloaded it, verified its SHA-256, handed over to it and exited cleanly; 9.9.9 copied
  itself in place, restarted, found itself up to date and removed the `.new`. With a wrong
  checksum, the download was refused and 1.0.0 kept running untouched. The first design renamed
  the running executable instead: it passed that same local test, but failed against the real
  `v0.3.0-beta` release (the renamed single-file executable could no longer load its own
  assemblies and crashed before restarting), which led to the current design.
- **Log panel** — rendered off-screen after a failed login, opened automatically with the
  error and its cause; beta passwords confirmed masked, including in verbose FTP logging.

## Verified in production by the maintainer

- **FTPS updates against the real server**, over the maintainer's own connection. Large
  `.pk3` transfers there lost their control connection mid-transfer; the automatic recovery
  took over, e.g. a 756.5 MB file resumed and verified four seconds after the interruption.
- **The ProFTPD configuration** was applied on the target Ubuntu 24.04 machine. Two directives
  failed with Ubuntu's packaging and were fixed afterwards: `mod_tls` ships in
  `proftpd-mod-crypto`, and `IdentLookups` needs a module that is not installed.
- **Release workflow**: the `v0.2.0-alpha` tag built and published the first release, with
  `SWGLLauncher.exe`, its SHA-256 digest, and `SWGLLauncher.zip`.
- **SSH access for `swgl-dev`** (`swgl-sync-ssh`, sudoers, sshd block): applied on the server,
  the `swgl-sync>` prompt works and no shell is reachable. The prompt was since rewritten in
  Python for history and completion: word splitting, completion candidates and the command
  loop were checked locally (`$(...)`, `;` and `*` reach `swgl-sync` as plain text; completion of options and their values), but the
  line editing itself (arrow keys, Tab) only runs on the server and was not tried there yet.

## What was not verified

- **`server/swgl-sync`**: `update`, `rename`, `exclude`, `restore`, `force-delete` and `cancel-delete`, with the named options, were tested against the real manifest tool on a fake repository (option errors, `--notify` against a stand-in Discord webhook: one message listing the changed branches, roles from `--notify-ping` (a name from the roles file, case-insensitive, or a raw id) mentioned, the roles file created when missing, the message text never parsed for mentions, `changes.txt` attached, nothing sent without changes; labels kept or changed, beta built from the public manifest with exclusions and inherited deletions, quotes stripped from paths, checksums reused for unchanged files and recomputed for modified or new ones), and the `swgl-sync>` word splitting on sample lines; `create` and `remove` need `useradd` and ProFTPD and have not been run on the target machine.
- **Patch notes on the real server**: the `VRootAlias` to `/patchnotes.md` and its backfill in
  `swgl-sync update` were checked on a copy of the configuration, not against ProFTPD.
- **Discord announcements against the real Discord API**: the request was checked against a
  local stand-in only; the webhook, role mentions and attachment preview are untested on Discord.
- **Steam library rendering.** Artwork file names and the `shortcuts.vdf` format were verified,
  but no shortcut was ever written to a live Steam profile.
- **Interface rendering on other DPI settings.** The window was checked at 100 % only.

## Known caveats

- The launcher updates itself from GitHub releases only; a `SWGLLauncher.exe` listed in a
  manifest would fail to be replaced while running. Releases are built by the
  `Release` workflow; its first run published `v0.2.0-alpha`.
- The cleanup deletes files matching `sync.deletable`. A file the mod ships outside those
  patterns is only cleaned up if the channel forces it with `swgl-sync force-delete`; a broad
  forced pattern deletes everything it covers.

## Model

Claude Opus 5, then Claude Opus 5.5, via Claude Code. Commits carry a `Co-Authored-By`
trailer naming the model that wrote them.
