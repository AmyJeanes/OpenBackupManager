# Bundled rclone

The build fetches rclone at a pinned version and checks it's genuine, so the app can ship one it's been tested with rather than use whatever is installed. The engine runs it as `rclone rcd`, one for each remote. So far only the Windows app ships it, as its package takes everything published beside `obmd`.

## Code map

- `rclone.version` pins the release. Renovate proposes updates, which wait for review.
- `tools/RcloneFetch` downloads the release for one runtime and checks it against rclone's signing key, `rclone-signing-key.asc` beside it.
- `OpenBackupManager.Rclone` runs the fetcher as it builds, and puts rclone next to anything that references it. `BundledRclone` says where it is and which version it is. `Rcd` starts and stops rcd, and `RcClient` calls it.
- The engine's `Remotes` runs an rcd for each `[[remotes]]` in `config.toml`.

## Running it

- **One rcd for each remote,** never for each folder, as each account has its own sign-in, throttling and limits, and a crash only takes its own folders. Each starts with the engine, even while idle, at about 50 MB.
- **Only its own config, cache and log.** rclone's defaults are the user's own rclone's, so each rcd gets the remote's own config in the state folder, its cache, and a log beside the engine's, named from the remote's ID, which `config.toml` allows to be only lowercase letters, digits and hyphens. The config is a file for each remote, as rclone rewrites the whole file when it refreshes a token, and two rcds sharing one could lose each other's.
- **rc over a Unix socket with no password,** in the engine's private `run` folder, as for the engine's own socket, rather than localhost TCP with a password, which any local user can reach and try. Windows has had Unix sockets since Windows 10 1803. The socket is named from a hash of the ID, as a socket's path is limited to about 104 bytes on macOS and Linux.
- **It goes when the engine does.** .NET 11's `KillOnParentExit` ends it with the engine on Windows, through a job object, and Linux, through `PR_SET_PDEATHSIG`. It does nothing on macOS, where launchd ends a service's process group and a terminal signals its foreground group, and an engine starting stops an rcd still answering on its socket, which only an engine for the same home could have left.
- **Stopping sends `core/quit`, then kills it after 4 seconds.** rcd exits 1.5 seconds after answering, and about 3 with a job running. Windows gives the engine about 5 at sign-out, and killing rcd loses nothing a later run can't redo.

## Fetching

- **The version and signing key are pinned, not each zip's hash.** A release has six zips we use, and Renovate can bump a version but can't recalculate hashes. The signed `SHA256SUMS` gives the same guarantee, and an update stays a one-line change.
- **The signature is checked in .NET, with BouncyCastle, rather than with `gpg`.** GPG isn't on a stock Mac or on every Windows machine, and the build should work anywhere .NET does with nothing else installed.
- **The fetcher is a console project, not a single-file script,** so the same analyzers, formatting and CI apply to it as to the rest of the code.

## Accepted limits

- **rclone signs its releases with a 2001 DSA-1024 key, using SHA-1.** That's weak by today's standards, but it's the only signature rclone publishes, and it sits on top of HTTPS from GitHub. The key's fingerprint is `FBF7 37EC E9F8 AB18 604B D2AC 9393 5E02 FF3B 54FA`, which matched on [rclone.org](https://rclone.org/release_signing/), in rclone's GitHub repository and on keyserver.ubuntu.com. If rclone changes key, replace the key file by hand, after checking the new fingerprint the same way.
- **Only x64 and ARM64 builds of Windows, macOS and Linux are fetched.** Other runtimes fail the build with a clear message.
