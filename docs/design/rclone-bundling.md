# Bundled rclone

The build fetches rclone at a pinned version and checks it's genuine, so the app can ship one it's been tested with rather than use whatever is installed. The engine runs it as `rclone rcd`, one for each remote. Every package has it beside `obmd`: the Windows app, the tarball, the .deb and .rpm, and the Docker image, which is made from the tarball.

## Code map

- `rclone.version` pins the release. Renovate proposes updates, which wait for review.
- `tools/RcloneFetch` downloads the release for one runtime and checks it against rclone's signing key, `rclone-signing-key.asc` beside it.
- `OpenBackupManager.Rclone` runs the fetcher as it builds, and puts rclone next to anything that references it. `BundledRclone` says where it is and which version it is. `Rcd` starts and stops rcd, and `RcClient` calls it.
- The engine's `Remotes` runs an rcd for each `[[remotes]]` in `config.toml`, and keeps how each is doing for `obm status`. `RcloneConfig` keeps their config files and password.

## Shipping it

- **Never on PATH as `rclone`,** so it can't stand in for the user's own. The engine runs it by full path. The tarball goes in a folder of its own, such as `~/.local/share/openbackupmanager`, with only `obm` and `obmd` linked onto PATH, as the .deb and .rpm do from `/usr/lib/openbackupmanager`, and on Windows only `obm` is on PATH.
- **Named `obm-rclone`,** so it still can't if it's unpacked onto PATH, and shows as ours in process lists. rclone only minds names starting `mount`.
- **Fetched for the runtime being published,** as `tools/Publish-App.ps1` passes `RcloneRuntimeIdentifier`. The Rclone project is built for any runtime, so it would otherwise get the build machine's.

## Running it

- **One rcd for each remote,** not each folder, as each account has its own sign-in, throttling and limits, and a crash only takes its own folders. Each starts with the engine, at about 50 MB idle, or once it's added to `config.toml`, and stops once it's taken out. One taken out and put back while its rcd is still quitting starts again once it's gone, as starting stops one still answering on its socket.
- **Its own config, cache and log,** named from the remote's ID, as rclone's defaults are the user's own. The config is a file for each remote, as rclone rewrites the whole file when it refreshes a token, so two rcds sharing one could lose each other's.
- **None of the user's `RCLONE_` variables,** which rclone reads as settings, such as their own config's password.
- **Our own User-Agent,** `ISV|AmyJeanes|OpenBackupManager/<version>`, in the form Microsoft prioritises over clients it can't identify.
- **One password for every remote's config,** `rclone-config` in the secret store, given to each rcd with `config/unlock` once it answers, rather than as `RCLONE_CONFIG_PASS`, which anything rclone starts would get too. Setup makes it if there's none, and without it rclone writes a new config in plain text. It's never made anew while any config is encrypted, as a new one couldn't read them, which would hide that bringing the old store back would fix them.
- **No rcd for a remote that can't run,** rather than one failing every minute: no config yet, an encrypted one without its password or with the wrong one, or one rclone can't read. To the user they're the same: it needs setting up again, and its file is left. `obm status` says why, and `obm remote retry` tries it again, such as once its password is back. rclone only reads its config when first asked, so the engine lists its remotes once it starts to find a wrong password or a damaged file, and points to rclone's log for why, as rclone's message can quote a line of the config. rcd runs with `--ask-password=false`, so it never waits on a prompt. A secret store that can't be read is retried like a crash.
- **rc over a Unix socket with no password,** in the engine's `run` folder, which only the user can open, rather than localhost TCP, which any local user can reach. On Windows it's given an ACL of its own, as what it would inherit outside the user's profile, such as on another drive, often lets every user in. One another user made first is refused, as its owner can always let themselves back in. Windows has these since 10 1803. It's named from a hash of the ID, as the engine only checks `--home` has room for names of up to 16 bytes there. In Windows Sandbox, another standard user couldn't connect to it, in the default home or one outside the profile, while the owner could, and a `run` folder another user made first was refused (Nov 2026).
- **It goes when the engine does,** through .NET 11's `KillOnParentExit` on Windows and Linux. On Linux that's `PR_SET_PDEATHSIG`, which fires when the thread that started the child ends, but .NET starts those children from a thread that lasts as long as the process. Stopping the systemd service signals only the engine, with `KillMode=mixed`, so it quits each rcd itself rather than racing rclone's own exit, and anything left is killed once it has gone. On macOS, which has nothing like it, launchd ends a service's process group, a terminal signals its foreground group, and a starting engine stops an rcd still answering on its socket. On a macOS VM, killing a service's `obmd` took its rcd with it, and launchd started both again (Nov 2026).
- **Started again when it stops or can't start,** after a second, doubling up to a minute, and back to a second once one has stayed up a minute. A new rcd numbers its jobs from 1 again, so an old job ID names a different job. Only the failures each step can have are retried. Anything else is a bug, which stops that remote's rclone until it's tried again, and is logged as an error and shown by `obm status`.
- **Stopping sends `core/quit`, then kills it after 4 seconds.** rcd exits 1.5 seconds after answering, or about 3 with a job running, Windows gives the engine about 5 at sign-out, and killing rcd loses nothing a later run can't redo.

## Fetching

- **The version and signing key are pinned, not each zip's hash.** A release has six zips we use, and Renovate can bump a version but can't recalculate hashes. The signed `SHA256SUMS` gives the same guarantee, and an update stays a one-line change.
- **The signature is checked in .NET, with BouncyCastle, rather than with `gpg`.** GPG isn't on a stock Mac or on every Windows machine, and the build should work anywhere .NET does with nothing else installed.
- **The fetcher is a console project, not a single-file script,** so the same analyzers, formatting and CI apply to it as to the rest of the code.

## Accepted limits

- **rclone signs its releases with a 2001 DSA-1024 key, using SHA-1.** That's weak by today's standards, but it's the only signature rclone publishes, and it sits on top of HTTPS from GitHub. The key's fingerprint is `FBF7 37EC E9F8 AB18 604B D2AC 9393 5E02 FF3B 54FA`, which matched on [rclone.org](https://rclone.org/release_signing/), in rclone's GitHub repository and on keyserver.ubuntu.com. If rclone changes key, replace the key file by hand, after checking the new fingerprint the same way.
- **Only x64 and ARM64 builds of Windows, macOS and Linux are fetched.** Other runtimes fail the build with a clear message.
- **A config something holds open with no sharing as rcd starts reads as damaged,** so that remote needs setting up until it's tried again. Only Windows has such locks, nothing usual opens it that way, and the engine's own read of it just before is retried like a crash, so only the second or so while rcd starts is open to it.
- **rcds share a foreground engine's process group,** so closing its terminal ends them while the engine, which takes SIGHUP as a reload, carries on and starts them again a second later, and Ctrl+C can log their exit as a restart as the engine stops. A service has no terminal, and systemd's `KillMode=mixed` signals only the engine.
