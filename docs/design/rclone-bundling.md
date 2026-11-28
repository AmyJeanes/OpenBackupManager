# Bundled rclone

The build fetches rclone at a pinned version and checks it's genuine, so the app ships one it's been tested with rather than whatever is installed. The engine runs it as `rclone rcd`, one for each remote. Every package has it beside `obmd`: the Windows app, the tarball, the .deb and .rpm, and the Docker image.

## Code map

- `rclone.version` pins the release. Renovate proposes updates, which wait for review.
- `tools/RcloneFetch` downloads a release and checks it against `rclone-signing-key.asc` beside it.
- `OpenBackupManager.Rclone` runs the fetcher as it builds and puts rclone next to anything that references it:
  - `BundledRclone` says where it is and which version.
  - `Rcd` starts and stops rcd, and `RcClient` calls it, each method named as rclone names it.
  - `RemoteSetup` steps through rclone's own setup questions.
- The engine:
  - `Remotes` runs an rcd for each `[[remotes]]` and keeps how each is doing for `obm status`.
  - `RcloneConfig` keeps their config files and password.
  - `Backends` lists the backends setup offers, for `listBackends`.
  - `Setups` runs a setup for a client, through `setupRemote`, `answerSetup`, `listFolders` and `cancelSetup`: rclone's questions, then our own, each an `OwnQuestion`, with `RemoteFolders` finding folders in the remote's own case.
  - `Preset` applies a backend's file in `Presets`, such as `onedrive.jsonc`, which answers the rclone questions we know the answers to and words the rest more plainly.

## Shipping it

- **Never on PATH as `rclone`,** so it can't stand in for the user's own. The engine runs it by full path, and only `obm` and `obmd` are linked onto PATH (only `obm` on Windows).
- **Named `obm-rclone`,** so it still can't if it's unpacked onto PATH, and shows as ours in process lists. rclone only minds names starting `mount`.
- **Fetched for the runtime being published,** as `tools/Publish-App.ps1` passes `RcloneRuntimeIdentifier`. Otherwise it would get the build machine's.

## Running it

- **One rcd for each remote,** not each folder, as each account has its own sign-in, throttling and limits, and a crash only takes its own folders. About 50 MB idle. Starting one stops any old one still answering on its socket, so a remote taken out and put back while its rcd quits comes back.
- **Its own config, cache and log,** as rclone's defaults are the user's own rclone's. A config file for each remote, as rclone rewrites the whole file when it refreshes a token, so two rcds sharing one could lose each other's.
- **None of the user's `RCLONE_` variables,** as rclone reads them as settings, such as their own config's password.
- **Our own User-Agent,** in the ISV form Microsoft prioritises over clients it can't identify.
- **rc over a Unix socket with no password,** in the engine's `run` folder, which only the user can open, rather than localhost TCP, which any local user can reach. Windows has them since 10 1803. On Windows the folder gets its own ACL, as what it would inherit outside the profile often lets every user in, and one another user made first is refused, as its owner could always let themselves back in. The socket is named from a hash of the ID, to fit the room the engine checks for there. Checked in Windows Sandbox with a second user (Nov 2026).
- **No rcd for a remote that can't run,** rather than one failing every minute. rclone only reads its config when first asked, so the engine lists remotes as soon as one starts, to catch a wrong password or a damaged file. It then points to rclone's log rather than quoting rclone, whose message can contain a line of the config.
- **A new rcd numbers its jobs from 1 again,** so a job ID from before a restart names a different job.
- **Only each step's expected failures are retried.** Anything else is a bug, so that remote stops until it's retried, rather than failing the same way every minute.
- **It goes when the engine does.** On Linux, `KillOnParentExit` is `PR_SET_PDEATHSIG`, which fires when the starting thread ends, but .NET starts children from a thread that lasts as long as the process. The systemd service uses `KillMode=mixed`, so the engine quits each rcd itself rather than racing rclone's own exit. macOS has nothing like it, but launchd ends a service's process group, a terminal signals its foreground group, and a starting engine stops any rcd left answering. Checked on a macOS VM (Nov 2026). That includes a setup's, the backend list's and a removed remote's, before the engine answers anything, and it deletes setup's copies of configs and rclone's temporary files and backups beside them, as they hold the same secrets.
- **Stopping kills rcd after 4 seconds.** rcd exits 1.5 seconds after `core/quit`, about 3 with a job running, and Windows gives the engine about 5 at sign-out. Killing it loses nothing a later run can't redo.

## The config password

- **One for every remote's config,** given to each rcd with `config/unlock` rather than `RCLONE_CONFIG_PASS`, which anything rclone starts would inherit. Without it, rclone writes a new config in plain text, so setup makes one if there's none.
- **Never made anew while another remote's config is encrypted, unless asked to start afresh,** as a new one can't read them and bringing the old store back would keep them, so it's the person's choice. Setup's refusal has its own error code, so clients can offer it. Starting afresh deletes every encrypted config, as nothing could read them. The remote being set up doesn't count, as it's replacing its own.
- **From the environment, it's never saved,** so a copy of a container's volume, where the store is a file beside the configs, can't open them. The variables end in `_SECRET` so none reads as a setting's variable, and rclone is started without them, as it is without `RCLONE_` ones, so nothing it starts gets them. Setting both a value and a file is refused rather than one winning unseen. A stored copy of the same value is removed, but a different one is kept, as it's the one that works.
- **A locked secret store is checked every 10 seconds rather than backing off,** so its remotes carry on soon after it's unlocked. It's never asked to unlock.

## Setting a remote up

- **The backends offered are left out only by rclone's own ratings, never by name,** so an rclone update changes the list by itself. Hiding only shapes the list: nothing is refused by type.
- **rclone's own questions use `all` as well as `nonInteractive`,** as without it rclone only asks the questions its backend's own setup code adds, such as signing in, and expects the options themselves as parameters.
- **The client opens the sign-in link, not rclone,** as the client is where the person is, while the engine may be a service or in a container with no browser, and a browser rclone started could be closed with it. rclone's `config/oauthstatus` gives the link, which the engine looks for every 100 ms while a step runs, as rclone doesn't say it's waiting. `config/oauthstop` ends a sign-in when its setup ends, so its port is free for the next.
- **The root comes last,** once rclone's questions are done, as only then can the account be reached, to list its folders or look at the one chosen. A remote set up again keeps its own.
- **The root is rclone's own path, with how it starts kept as given,** as what a path means is the backend's: empty is OneDrive's top but an SFTP user's home, where `/` is the server's root. `\` divides folders only for `local` on Windows, as elsewhere it can be in a name. `.` and `..` are refused, as rclone takes them as steps but the folders are looked at one by one. `local` needs a full path, as anything else is from the folder the engine started in, which isn't the same for a service, the app and a shell. Only `local` is checked, as `alias` reports itself local too but its paths are from its own target.
- **Where the remote ignores case, the root takes the real names of the folders that exist,** so `config.toml` matches what's on the remote and later comparisons don't trip over case. Names are compared in one Unicode form, as macOS and Windows can write the same accented name two ways.
- **A folder above the root that can't be listed is passed over,** as an S3 key can be kept to one bucket and an SFTP folder may only be passed through, so only the root itself has to list. A backup or case there goes unchecked, which the confirm says. local lists a folder it can't open as empty, so a folder a listing didn't show is looked at directly before it's called missing. Not inside a bucket, such as S3's, where one that isn't there lists as empty too. One that lists when the listing showed it only in another case takes that case, as the remote ignores case though rclone can't tell, such as SFTP to Windows.
- **What's at the root is confirmed before it's used,** with the default no only when someone else's files are there, so a typo or the wrong folder is caught. A folder that isn't there points to the nearest beside it a letter or two away, or differing in case where the remote minds case. A short name allows fewer, as two letters changed in one makes another name altogether.
- **A backup's folder, or one inside it, is refused,** as two backups never share files, until joining one is built.
- **Yes proves the root can hold files,** by writing a file, checking it's there at its full size and deleting it, which also makes the folder. Its name is random, so two devices checking at once don't meet, and isn't `.obm-check`, which marks a synced folder. A remote set up again has no root question, so a failed check ends its setup.
- **A no, a failed check, or a root that can't be looked at asks for the root again,** with rclone's reason, rather than ending the setup, as a wrong folder needs no new sign-in.
- **An rcd of its own, writing a copy of the config,** which replaces the remote's config only once it's set up. So a setup stopped part way leaves what was there, and the remote's own rcd can run beside it. A new remote is added to `config.toml` last, as that starts its rcd.
- **Owned by the connection that started it,** and ended when it closes, even in the middle of a sign-in, so nothing outlives a client that's gone, such as a sign-in holding the port rclone's others need, and there's nothing to resume or time out.

## Fetching

- **The version and signing key are pinned, not each zip's hash.** Renovate can bump a version but can't recalculate six hashes, and the signed `SHA256SUMS` gives the same guarantee.
- **The signature is checked in .NET with BouncyCastle, not `gpg`,** which isn't on a stock Mac or every Windows machine.
- **The fetcher is a console project, not a script,** so the same analyzers, formatting and CI apply.

## Accepted limits

- **rclone signs releases with a 2001 DSA-1024 key, using SHA-1.** Weak today, but it's the only signature rclone publishes, on top of HTTPS from GitHub. Its fingerprint, `FBF7 37EC E9F8 AB18 604B D2AC 9393 5E02 FF3B 54FA`, matched on [rclone.org](https://rclone.org/release_signing/), in rclone's GitHub repository and on keyserver.ubuntu.com. If rclone changes key, replace the file by hand after checking the new fingerprint the same way.
- **Only x64 and ARM64 builds of Windows, macOS and Linux are fetched.** Other runtimes fail the build with a clear message.
- **A config held open with no sharing as rcd starts reads as damaged,** so that remote needs setting up until it's retried. Only Windows has such locks, nothing usual takes one, and the window is the second or so while rcd starts.
- **A config rclone was stopped saving is lost,** as clearing what a crash left deletes its backup, then the only copy, with the rest. Telling which file holds it would depend on how rclone saves, and its window is between two renames. That remote needs setting up again.
- **Only a link is refused for the `rclone` folder,** not another user's folder or its mode, as a folder chosen as a home may be shared on purpose and the configs are encrypted. It's checked before clearing what a crash left and before starting afresh, not on every write, as whoever could make the link can already write in the state folder, and the next start refuses it.
- **rcds share a foreground engine's process group,** so closing its terminal ends them while the engine carries on (it takes SIGHUP as a reload) and restarts them a second later, and Ctrl+C can log their exit as a restart. A service has no terminal, and `KillMode=mixed` signals only the engine.
