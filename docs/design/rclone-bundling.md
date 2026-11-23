# Bundled rclone

The build fetches rclone at a pinned version and checks it's genuine, so the app can ship one it's been tested with rather than use whatever is installed. The engine runs it as `rclone rcd`, one for each remote. Every package has it beside `obmd`: the Windows app, the tarball, the .deb and .rpm, and the Docker image, which is made from the tarball.

## Code map

- `rclone.version` pins the release. Renovate proposes updates, which wait for review.
- `tools/RcloneFetch` downloads the release for one runtime and checks it against rclone's signing key, `rclone-signing-key.asc` beside it.
- `OpenBackupManager.Rclone` runs the fetcher as it builds, and puts rclone next to anything that references it. `BundledRclone` says where it is and which version it is. `Rcd` starts and stops rcd, and `RcClient` calls it.
- The engine's `Remotes` runs an rcd for each `[[remotes]]` in `config.toml`, and `RcloneConfig` keeps their config files and password.

## Shipping it

- **Never on PATH as `rclone`,** so it can't stand in for the user's own. The engine runs it by full path. The tarball goes in a folder of its own, such as `~/.local/share/openbackupmanager`, with only `obm` and `obmd` linked onto PATH, as the .deb and .rpm do from `/usr/lib/openbackupmanager`, and on Windows only `obm` is on PATH.
- **Named `obm-rclone`,** so it still can't if it's unpacked onto PATH, and shows as ours in process lists. rclone only minds names starting `mount`.
- **Fetched for the runtime being published,** as `tools/Publish-App.ps1` passes `RcloneRuntimeIdentifier`. The Rclone project is built for any runtime, so it would otherwise get the build machine's.

## Running it

- **One rcd for each remote,** not each folder, as each account has its own sign-in, throttling and limits, and a crash only takes its own folders. Each starts with the engine, at about 50 MB idle.
- **Its own config, cache and log,** named from the remote's ID, as rclone's defaults are the user's own. The config is a file for each remote, as rclone rewrites the whole file when it refreshes a token, so two rcds sharing one could lose each other's.
- **None of the user's `RCLONE_` variables,** which rclone reads as settings, such as their own config's password.
- **One password for every remote's config,** `rclone-config` in the secret store, passed as `RCLONE_CONFIG_PASS`. Setup makes it if there's none and gives it with `config/unlock`, without which rclone writes a new config in plain text. It's never made anew while any config is encrypted, as a new one couldn't read them, which would hide that bringing the old store back would fix them.
- **No rcd for a remote that can't run,** rather than one failing every minute: no config yet, or an encrypted one without its password, or with the wrong one. To the user they're the same: it needs setting up again, and its file is left. rclone only reads its config when first asked, so the engine lists its remotes once it starts to find a wrong password, with `--ask-password=false` so rclone never waits on a prompt. A secret store that can't be read is retried like a crash.
- **rc over a Unix socket with no password,** in the engine's private `run` folder, rather than localhost TCP, which any local user can reach. Windows has these since 10 1803. It's named from a hash of the ID, as the engine only checks `--home` has room for names of up to 16 bytes there.
- **It goes when the engine does,** through .NET 11's `KillOnParentExit` on Windows and Linux. On macOS, which has nothing like it, launchd ends a service's process group, a terminal signals its foreground group, and a starting engine stops an rcd still answering on its socket.
- **Started again when it stops or can't start,** after a second, doubling up to a minute, and back to a second once one has stayed up a minute. A new rcd numbers its jobs from 1 again, so an old job ID names a different job.
- **Stopping sends `core/quit`, then kills it after 4 seconds.** rcd exits 1.5 seconds after answering, or about 3 with a job running, Windows gives the engine about 5 at sign-out, and killing rcd loses nothing a later run can't redo.

## Fetching

- **The version and signing key are pinned, not each zip's hash.** A release has six zips we use, and Renovate can bump a version but can't recalculate hashes. The signed `SHA256SUMS` gives the same guarantee, and an update stays a one-line change.
- **The signature is checked in .NET, with BouncyCastle, rather than with `gpg`.** GPG isn't on a stock Mac or on every Windows machine, and the build should work anywhere .NET does with nothing else installed.
- **The fetcher is a console project, not a single-file script,** so the same analyzers, formatting and CI apply to it as to the rest of the code.

## Accepted limits

- **rclone signs its releases with a 2001 DSA-1024 key, using SHA-1.** That's weak by today's standards, but it's the only signature rclone publishes, and it sits on top of HTTPS from GitHub. The key's fingerprint is `FBF7 37EC E9F8 AB18 604B D2AC 9393 5E02 FF3B 54FA`, which matched on [rclone.org](https://rclone.org/release_signing/), in rclone's GitHub repository and on keyserver.ubuntu.com. If rclone changes key, replace the key file by hand, after checking the new fingerprint the same way.
- **Only x64 and ARM64 builds of Windows, macOS and Linux are fetched.** Other runtimes fail the build with a clear message.
