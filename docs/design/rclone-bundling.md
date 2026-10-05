# Bundled rclone

The app ships its own rclone, at a version we've tested, instead of using whatever is installed.

## How it works

- `rclone.version` pins the release. Renovate proposes updates, but rclone updates aren't merged automatically, because its behaviour can change between releases.
- Building `OpenBackupManager.Rclone` runs `tools/RcloneFetch` for the build's runtime, or the one in the `RcloneRuntimeIdentifier` property. It downloads `SHA256SUMS` and the release zip from rclone's GitHub release, checks the signature on `SHA256SUMS` against `tools/RcloneFetch/rclone-signing-key.asc`, checks the zip's hash against it, and extracts rclone to `obj/rclone/<version>/<runtime>/`.
- That file is copied next to the app, and next to anything that references the project such as the tests, along with `THIRD-PARTY-NOTICES.md`. It's reused until `rclone.version` or the key changes, so only the first build needs internet access.
- Code finds it through `BundledRclone.FilePath`, and `BundledRclone.Version` is the pinned version.

## Why

- **The version and signing key are pinned, not each zip's hash.** A release has six zips we use, and Renovate can bump a version but can't recalculate hashes. The signed `SHA256SUMS` gives the same guarantee, and an update stays a one-line change.
- **The signature is checked in .NET, with BouncyCastle, rather than with `gpg`.** GPG isn't on a stock Mac or on every Windows machine, and the build should work anywhere .NET does with nothing else installed.
- **The fetcher is a console project, not a single-file script,** so the same analyzers, formatting and CI apply to it as to the rest of the code.

## Accepted limits

- **rclone signs its releases with a 2001 DSA-1024 key, using SHA-1.** That's weak by today's standards, but it's the only signature rclone publishes, and it sits on top of HTTPS from GitHub. The key's fingerprint is `FBF7 37EC E9F8 AB18 604B D2AC 9393 5E02 FF3B 54FA`, which matched on rclone.org, in rclone's GitHub repository and on keyserver.ubuntu.com. If rclone changes key, replace the key file by hand, after checking the new fingerprint the same way.
- **Only x64 and ARM64 builds of Windows, macOS and Linux are fetched.** Other runtimes fail the build with a clear message.
