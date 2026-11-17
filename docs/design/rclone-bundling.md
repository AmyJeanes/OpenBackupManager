# Bundled rclone

The build fetches rclone at a pinned version and checks it's genuine, so the app can ship one it's been tested with rather than use whatever is installed. So far only the tests use it: nothing ships it yet.

## Code map

- `rclone.version` pins the release. Renovate proposes updates, which wait for review.
- `tools/RcloneFetch` downloads the release for one runtime and checks it against rclone's signing key, `rclone-signing-key.asc` beside it.
- `OpenBackupManager.Rclone` runs the fetcher as it builds, and puts rclone next to anything that references it. `BundledRclone` says where it is and which version it is.

## Fetching

- **The version and signing key are pinned, not each zip's hash.** A release has six zips we use, and Renovate can bump a version but can't recalculate hashes. The signed `SHA256SUMS` gives the same guarantee, and an update stays a one-line change.
- **The signature is checked in .NET, with BouncyCastle, rather than with `gpg`.** GPG isn't on a stock Mac or on every Windows machine, and the build should work anywhere .NET does with nothing else installed.
- **The fetcher is a console project, not a single-file script,** so the same analyzers, formatting and CI apply to it as to the rest of the code.

## Accepted limits

- **rclone signs its releases with a 2001 DSA-1024 key, using SHA-1.** That's weak by today's standards, but it's the only signature rclone publishes, and it sits on top of HTTPS from GitHub. The key's fingerprint is `FBF7 37EC E9F8 AB18 604B D2AC 9393 5E02 FF3B 54FA`, which matched on [rclone.org](https://rclone.org/release_signing/), in rclone's GitHub repository and on keyserver.ubuntu.com. If rclone changes key, replace the key file by hand, after checking the new fingerprint the same way.
- **Only x64 and ARM64 builds of Windows, macOS and Linux are fetched.** Other runtimes fail the build with a clear message.
