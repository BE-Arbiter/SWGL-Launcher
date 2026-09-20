# SWGL Launcher

Launcher and updater for **Star Wars: Galactic Legacy**, a *Jedi Knight: Jedi Academy* mod.

It keeps a player's `GameData` in sync with the mod's file server, over FTPS, on a public
channel or on a private beta channel, then starts the game. It ships as a single executable.

![The launcher downloading an update](docs/screenshot.png)

## Features

- **Borderless window** with a configurable background, looping music and a mute button.
- **Public and beta channels.** A tester enters a code; the launcher connects to the matching
  account and installs that build. Switching back to the public release is one click.
- **State-based updates.** A channel is described by a manifest listing every file with its
  size and SHA-256. The launcher makes the installation match it, so the same code path
  installs, repairs, updates, switches channel and rolls back.
- **Resumable downloads,** verified by checksum, written through a temporary file so an
  interrupted transfer never leaves a truncated `.pk3` behind.
- **Cleanup** of files that do not belong to the channel, with the stock game files, the
  `base` folder and the player's saves left alone.
- **Jedi Outcast asset import**, for the mod's JO-derived missions.
- **Steam integration**: adds the launcher to the Steam library as a non-Steam game, with
  library artwork.

## Requirements

Windows 10 or 11, 64-bit. Nothing else — the .NET runtime is bundled in the executable.

## Installation

Drop these two files into the game's `GameData` folder, next to `jasp.exe`:

```
GameData\
├── SWGLLauncher.exe
└── launcher.properties
```

Then run `SWGLLauncher.exe`. On the first update it will download the mod into place.

## Interface

The title bar carries a speaker button — it mutes the music and remembers the choice —
alongside minimise and close.

At the bottom left, **Options** opens upward:

| Entry | What it does |
| --- | --- |
| **Beta channel** | Pick *Public*, a saved beta, add a beta code, or forget one. |
| **Configure Jedi Outcast...** | Import `Assets0/1/2.pk3` from a Jedi Outcast install. |
| **Configure Steam launcher...** | Add the launcher to the Steam library, artwork included. |

At the bottom right, **Update** brings the installation in line with the channel; its chevron
opens *Verify only*, which re-checks every checksum and reports what differs without
downloading anything. **Start** launches the game. During an operation *Update* becomes
*Cancel* and *Start* is disabled.

## Configuration

`launcher.properties` sits next to the executable and is plain `key=value`. Paths are
relative to the executable or absolute; colours are `#RRGGBB` or `#AARRGGBB`.

| Key | Meaning |
| --- | --- |
| `window.title`, `window.width`, `window.height`, `window.corner.radius` | Window. |
| `music.enabled`, `music.loop`, `music.volume` | Music. `music.enabled` is rewritten by the speaker button. |
| `titlebar.*`, `ui.accent.color` | Colours. |
| `sync.enabled`, `sync.check.on.start` | Updates; the check on start downloads nothing. |
| `install.path` | Folder to synchronise. `.` means the launcher's own folder. |
| `state.file` | Local state written by the launcher. |
| `sync.remove.unknown`, `sync.keep` | Cleanup — see below. |
| `ftp.host`, `ftp.port`, `ftp.tls`, `ftp.accept.any.certificate`, `ftp.timeout.ms` | Connection. `explicit` means FTPS on port 21. |
| `ftp.public.user`, `ftp.public.password` | Public channel account. |
| `ftp.beta.user.prefix` | Beta account is `<prefix><code>`, password is the code. |
| `manifest.file` | Manifest path on the server, same for every channel. |
| `game.executable`, `game.arguments`, `game.close.launcher` | The **Start** button. |
| `jo.path`, `steam.*` | Written by the Options menu. |
| `beta.codes`, `channel.selected` | Written by the channel menu. |

The background, the music and the Steam artwork are embedded in the executable. To replace
one without rebuilding, drop a file next to the executable — `SWGL\background.png`,
`SWGL\music.mp3`, `SWGL\cover.png`, `SWGL\wide_cover.jpg`, `SWGL\logo.png`. A file on disk
always wins over the embedded copy, and the cleanup leaves it alone.

## How updating works

Each channel publishes a `manifest.json` at the root of its FTP account:

```json
{
  "channel": "public",
  "version": "21",
  "files": [
    { "path": "SWGL/SWGL_Menu.pk3", "from": "/base/SWGL/SWGL_Menu.pk3", "size": 190532653, "sha256": "..." }
  ]
}
```

`path` is the destination inside `install.path`, `from` is the path on the server. The
manifest describes the **expected end state**, not a sequence of operations — which is what
makes installing, repairing, switching beta and going back to public the same operation.

Checksums are only recomputed when a file's size or timestamp has changed; *Verify only*
forces a full pass.

### Cleanup

With `sync.remove.unknown=true`, anything inside `install.path` that the manifest does not
list is deleted — stale `.pk3` files, leftover folders, remains of an older install. Three
things are never touched:

- the patterns in `sync.keep`, which by default name the `base` folder and the stock Jedi
  Academy files;
- the launcher, its configuration and its state file;
- an override dropped next to the executable, if there is one.

In `sync.keep`, `*` stays inside a folder and `**` crosses folders; entries are separated by
commas. Empty folders are pruned afterwards. Setting `sync.remove.unknown=false` reverts to
the cautious behaviour: only files the launcher installed itself are removed.

## Publishing an update

`Tools/SWGLManifest` generates a manifest from a folder. It runs on Windows and on Linux, so
it can run directly on the file server rather than hashing gigabytes across the network.

```bash
# public channel
SWGLManifest --source /srv/swgl/base --source-prefix /base \
             --channel public --version 21 \
             --output /srv/swgl/manifest-public.json

# a beta: its own files win over the shared ones
SWGLManifest --source /srv/swgl/beta-099cw --source-prefix /beta-099cw \
             --base   /srv/swgl/base       --base-prefix   /base \
             --channel beta-099cw --version "Ep3 test 4" \
             --output /srv/swgl/beta-099cw/manifest.json
```

`SWGLManifest --help` lists the remaining options (`--notes`, `--exclude`, ...).

Server side, [`server/README.md`](server/README.md) documents the ProFTPD setup: one
read/write publishing account, one read-only public account, one account per beta seeing only
the shared folder and its own build. `server/add-beta.sh` provisions a beta in one command.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Visual Studio 2026 opens
`SWGLLauncher.slnx` directly.

```bash
dotnet build SWGLLauncher.slnx
dotnet publish SWGLLauncher/SWGLLauncher.csproj -c Release
```

The publish output is the executable plus `launcher.properties`, nothing else. Two settings
in the project file make that possible and should not be dropped:
`SatelliteResourceLanguages`, which stops dependencies from creating per-language
subfolders, and `ExcludeFromSingleFile` on `launcher.properties`, without which the bundler
swallows it into the executable and it can no longer be edited.

To build the manifest tool for the server:

```bash
dotnet publish Tools/SWGLManifest/SWGLManifest.csproj -c Release -r linux-x64 \
       --self-contained -p:PublishSingleFile=true -o out-linux
```

## Repository layout

```
SWGLLauncher/            the launcher (WinForms, .NET 10)
  SWGL/                  artwork and music, embedded at build time
Tools/SWGLManifest/      manifest generator
server/                  ProFTPD configuration and beta provisioning
```

## Known limitations

- A `.pk3` is a monolithic zip: changing one texture in an 800 MB file means downloading the
  800 MB again. FTP cannot do deltas. Splitting large `.pk3` files is the only real answer.
- The launcher lives in the folder it synchronises, so it cannot update itself — a
  `SWGLLauncher.exe` listed in a manifest would fail to be replaced while running.

## Maintainer

Arbiter — [@BE-Arbiter](https://github.com/BE-Arbiter)

## License

GNU General Public License, version 2 — the same licence as OpenJK, from which the mod's
engine derives. See [LICENSE.txt](LICENSE.txt).

## Credits

Artwork — background, library capsules and logo — composed by **Devis** from the game's own
images.

The launcher icon is `swgl-sp.ico`, taken from the OpenJK-SWGL engine. *Star Wars: Galactic
Legacy* belongs to its authors; *Star Wars* is a trademark of Lucasfilm Ltd.

## AI disclaimer

Most of this code was written by Claude Opus 5 under the maintainer's direction, and reviewed
by him. See [AI_DISCLAIMER.md](AI_DISCLAIMER.md) for what was generated, what was tested and
what was not.
