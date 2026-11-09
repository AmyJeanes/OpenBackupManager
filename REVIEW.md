# Code review of OpenBackupManager

Commit reviewed: `f22a7523dd337b016eeb87330e11fd5429f8c859`

## Summary

Phase 1 at this commit has 97 problems that clear the bar: 1 high, 24 medium and 72 low. 85 are confirmed; the other 12 are plausible, as they're Windows- or macOS-only and couldn't be run. By main area: command line 19, tests 16, build, release and supply chain 12, Windows app 11, crash reports and diagnostics 10, engine 10, core 9, user guide 9 and platform layers 1. The high one (finding 1) is that obm and obmd start systemctl, launchctl, journalctl, xdg-open and open by bare name, so a program of that name in the current folder runs as the user, or as root; in the Docker image that folder is always the mounted /config. The most serious of the rest: obm secrets migrate deletes the only copy of secrets kept in a keyring other than the default (finding 8; it's medium only because nothing saves a secret yet); where the umask allows, other local users can change obm update's download after its hash check, and obm update overwrites other programs' files beside obm (findings 14 and 15); two CI jobs hand signing, image-push or release-publishing rights to third-party code, on Renovate branches and in an unpinned Component Detection download (findings 22 and 23); the crash window copies the unredacted report under a caption saying it's been redacted (finding 7); obm data remove with a home deletes the user's own files in its logs, cache, crashes and run folders (finding 13); and the engine crashes or stays down after an unreadable config.toml, a full disk, running out of inotify instances, or an update that restarts it into a config error (findings 3, 6, 10 and 12). A separate agent tried to disprove each finding, two for those first rated high; 6 more were rejected that way, and the 117 that survived are merged by root cause into these 97. Windows- and macOS-only code was read against documentation and decompiled sources rather than run, and systemd user services, the Docker image and package builds and GitHub releases (there are none yet) couldn't be run for real, so 18 suspicions that depend on the OS are under To check. Every test passed here except the 13 Sandbox ServiceTests, which need a systemd user manager this container doesn't have, and five isolated reruns found no flaky tests.

## Findings

### 1. obm and obmd run a systemctl, journalctl or xdg-open planted in the current folder

- **Rating:** high
- **Area:** Command line (obm); Crash reports and diagnostics; Build, release and supply chain
- **Where:** `src/OpenBackupManager.Cli/EngineService.cs`: 80, 84, 88, 94; `src/OpenBackupManager.Cli/SystemdService.cs`: 16, 37, 40, 66, 67, 69, 78, 84, 95, 97; `src/OpenBackupManager.Cli/LaunchAgent.cs`: 42, 86, 88; `src/OpenBackupManager.Cli/Status.cs`: 38; `src/OpenBackupManager.Cli/Diagnostics.cs`: 135, 137, 155; `src/OpenBackupManager.Cli/Service.cs`: 24, 65, 73, 126; `src/OpenBackupManager.Cli/Data.cs`: 131, 133; `src/OpenBackupManager.Cli/Browser.cs`: 21; `src/OpenBackupManager.Cli/Crashes.cs`: 76; `src/OpenBackupManager.Core/Journal.cs`: 27; `src/OpenBackupManager.Core/SystemRecords.cs`: 55; `src/OpenBackupManager.Core/CrashReports.cs`: 88; `src/OpenBackupManager.Engine/CrashCapture.cs`: 88; `Dockerfile`: 22, 25; `docs/guide/docker.md`: 51
- **Status:** CONFIRMED
- **What's wrong:** EngineService.TryRun (EngineService.cs:80), Browser.TryOpen and Journal.Run start systemctl, launchctl, xdg-open, open and journalctl by bare name, so .NET on Unix tries the current folder before PATH, and obm crashes if that folder was deleted. The Dockerfile's last WORKDIR makes the current folder in the image always /config, the data folder.
- **Failure scenario:** Another local account leaves an executable systemctl in /tmp, or a cloned repository carries one (git keeps the executable bit), and the user runs obm status there with no engine running: the published obm runs ./systemctl --user show-environment as them, or as root for a root login. obm diagnostics export, service install and uninstall, data remove and obm crashes report reach planted programs the same way. In the Docker image, anyone who can write the mounted /config can plant a journalctl there, and every `docker exec ... obm diagnostics export`, the guide's own command, runs it as the engine's user, who can read /config/secrets.json.
- **Suggested fix:** Start each external program by absolute path, which also ends the deleted-folder crash: on Linux, find it in PATH's absolute entries only, and on macOS use /bin/launchctl and /usr/bin/open. End the Dockerfile with a WORKDIR the engine's user can't write, such as /app.

### 2. Without a final newline in config.toml, OBM_ env vars block startup or silently vanish

- **Rating:** medium
- **Area:** Core
- **Where:** `src/OpenBackupManager.Core/Configuration/ConfigFile.cs`: 80, 117, 118, 157, 162, 163, 316; `src/OpenBackupManager.Engine/Secrets.cs`: 81, 98, 139, 182; `Dockerfile`: 19
- **Status:** CONFIRMED
- **What's wrong:** ConfigFile.Edit, used for OBM_ env vars and obm secrets migrate, inserts new settings at LineEnd of the last top-level key or section item. If that line ends the file without a newline, the setting is glued onto it: a TOML error, or silently part of a trailing comment.
- **Failure scenario:** In the Docker image, which always sets OBM_SECRET_STORE=file, config.toml is `version = 1` with no final newline, as VS Code and Notepad save by default. obmd exits 1 with a config.toml error on line 1 ('Expected end-of-line after value but was `secret-store`'), so a `restart: unless-stopped` container loops. Yet `obm config validate` without --env finds no errors.
- **Suggested fix:** In Edit, put newLine first when appending to a text without a final newline, like line 166. In Secrets.MigrateAsync, check after SetStore that Chosen is the new store, and if not, fail rather than report success or remove anything from the old store.

### 3. A config.toml the running engine can't read crashes it, or reload says it's applied

- **Rating:** medium
- **Area:** Core; Engine (obmd)
- **Where:** `src/OpenBackupManager.Core/Configuration/ConfigWatcher.cs`: 41, 63, 83, 85; `src/OpenBackupManager.Core/Configuration/ConfigFile.cs`: 52; `src/OpenBackupManager.Engine/EngineService.cs`: 22, 26; `src/OpenBackupManager.Engine/Connection.cs`: 100, 102, 103; `src/OpenBackupManager.Engine/Listener.cs`: 59, 180; `src/OpenBackupManager.Cli/Config.cs`: 26; `src/OpenBackupManager.Cli/SystemdService.cs`: 117
- **Status:** CONFIRMED
- **What's wrong:** ConfigWatcher.Reload (ConfigWatcher.cs:83-88) catches only IOException, ignoring it as a save in progress. An UnauthorizedAccessException from an unreadable config.toml or its folder escapes the settle timer and SIGHUP handler and aborts obmd, while an IOException such as EIO makes obm config reload say "config.toml's settings are applied" and exit 0.
- **Failure scenario:** obmd runs as a systemd user service, and root runs `chown root:root config.toml; chmod 600 config.toml`. About 250 ms later obmd logs '[FTL] Closing because of an error nothing caught' and aborts with exit 134 and a crash report, though config.md says the last good settings stay in use. Restart=on-failure then restarts it into a crash at start until systemd gives up, so it stays down after the file is fixed.
- **Suggested fix:** In Reload, catch any read failure, log it, keep the last good settings and report it in Result, so reloadConfig answers RpcError.Failed and obm config reload exits 1. Only the watcher's timer should still ignore an IOException, which may be a save in progress.

### 4. obm secrets migrate replaces a linked config.toml, so dotfiles edits are silently ignored

- **Rating:** medium
- **Area:** Core
- **Where:** `src/OpenBackupManager.Core/Configuration/ConfigFile.cs`: 123, 325, 326, 339; `src/OpenBackupManager.Core/Configuration/ConfigWatcher.cs`: 62; `src/OpenBackupManager.Engine/Secrets.cs`: 81, 139, 182
- **Status:** CONFIRMED
- **What's wrong:** ConfigFile.Write renames config.toml.tmp over config.toml (ConfigFile.cs:325-339), so a config.toml that is a symbolic link to a file becomes a plain file, and the linked copy stops being read. The only writer is Secrets.SetStore (Secrets.cs:182), so `obm secrets migrate <store>` does this, including the first pick of a store on Linux.
- **Failure scenario:** On Linux, `~/.config/openbackupmanager/config.toml` is a Stow link into `~/dotfiles`, and the user runs `obm secrets migrate file`, as the guide advises for servers. It exits 0, but config.toml is now a regular file holding secret-store, and the dotfiles copy is unchanged. From then on, edits to the dotfiles copy are silently ignored, even after restarts: an error there such as prereleases = "yes" isn't listed by obm status, and `obm config validate` says there are no errors.
- **Suggested fix:** Resolve the final target (File.ResolveLinkTarget(Path, returnFinalTarget: true)), then write the temporary file and .bak beside it and rename over it, keeping its mode. Fail instead of replacing the link if that folder can't be written, and add a ConfigFileTests case writing through a file link.

### 5. Local-time crash report names in the culture's calendar hide crashes and can abort obmd

- **Rating:** medium
- **Area:** Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.Core/CrashReports.cs`: 58, 64, 70, 71, 121, 126, 130, 149, 164, 173, 181, 228, 230, 280; `src/OpenBackupManager.Engine/CrashCapture.cs`: 40, 82, 113; `src/OpenBackupManager.App.Windows/CrashCapture.cs`: 35, 86
- **Status:** CONFIRMED
- **What's wrong:** CrashReports.cs:228 names reports by local time in the culture's calendar, yet listing, the 'shown' marker, pruning and RepeatOf take them as time-ordered Gregorian dates, which breaks after the clock goes back or in other calendars. A name that isn't a Gregorian date makes RepeatOf throw a FormatException CrashCapture.Start doesn't catch.
- **Failure scenario:** With LANG=fa_IR.UTF-8, a report written on 22 Sep 2026 is named crash-14050631-..., which isn't a Gregorian date. While it's the newest, an engine that crashes within a minute of a start writes no report and leaves engine.running, and every start within a minute after that aborts with a FormatException from RepeatOf. All 7 restarts tried at launchd's 10 s cadence aborted, and systemd reaches its start limit and gives up.
- **Suggested fix:** Name reports by UTC time formatted with CultureInfo.InvariantCulture, keeping the local time and offset, formatted invariantly, in the heading. Have RepeatOf parse names as UTC with DateTime.TryParseExact and treat one it can't read as no repeat, so a bad name can't stop a start.

### 6. A full disk makes obmd abort at every start, leaving an empty crash report

- **Rating:** medium
- **Area:** Crash reports and diagnostics; Engine (obmd)
- **Where:** `src/OpenBackupManager.Core/CrashReports.cs`: 48, 166, 171, 226, 229; `src/OpenBackupManager.Core/BugReport.cs`: 42; `src/OpenBackupManager.Daemon/Program.cs`: 67, 75, 105, 109; `src/OpenBackupManager.Engine/CrashCapture.cs`: 26, 36, 49, 51, 88, 115, 118; `src/OpenBackupManager.App.Windows/Program.cs`: 127; `src/OpenBackupManager.App.Windows/CrashCapture.cs`: 27, 44, 46, 88; `src/OpenBackupManager.Engine/EngineHost.cs`: 39
- **Status:** CONFIRMED
- **What's wrong:** crashes.Start() (Daemon/Program.cs:105) writes engine.running outside the lock's guard, and the lock still opens on a full disk or a state folder later made read-only, so obmd aborts with exit 134 instead of refusing in one line. Non-atomic report writes then leave a 0-byte `crash-*.txt` shown as a new crash.
- **Failure scenario:** A Linux user who ran obm service install signs in with their home disk or quota full. obmd aborts with exit 134 in under 0.1 s, so Restart=on-failure hits systemd's limit of 5 starts in 10 s and leaves the unit failed, with the engine still down after space is freed. obm status lists 'New crash reports:' with a blank heading, and obm crashes report -n offers an issue titled ' closed unexpectedly' with an empty report.
- **Suggested fix:** Catch IOException and UnauthorizedAccessException around reports.Start, WriteEnded and WriteRecorded in both CrashCaptures, logging a warning, or at least run crashes.Start() inside obmd's guard. Write reports to a temporary name, then rename them into place, and keep the host's disposal from throwing Serilog's flush error.

### 7. Copy report gives the unredacted report beside a caption saying sensitive details are redacted

- **Rating:** medium
- **Area:** Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.App.Windows/CrashWindow.xaml.cs`: 26; `src/OpenBackupManager.ViewModels/CrashViewModel.cs`: 8, 14; `src/OpenBackupManager.App.Windows/CrashWindow.xaml`: 47, 56, 65; `src/OpenBackupManager.ViewModels/Strings.resx`: 201
- **Status:** CONFIRMED
- **What's wrong:** CrashWindow.OnCopy copies ViewModel.Report, the report as written, and the read-only text box shows it too; only Report on GitHub uses the redacted _shared. Yet the Crash_Check caption beside both buttons says 'Sensitive details have been redacted where possible, but check the report before you submit'.
- **Failure scenario:** After a crash, the window's report holds the run's log, starting 'App: Started version 0.1.0 with its home in C:\Users\Alex Kim\AppData\Local\OpenBackupManager'. It's too long for the link, so Report on GitHub puts the redacted text on the clipboard, but the user copies a GitHub sign-in code before pasting. They go back, click Copy report and, trusting the caption, paste into the public issue, which now shows 'C:\Users\Alex Kim\...', their Windows user name and often their name.
- **Suggested fix:** Bind the text box and OnCopy to the redacted _shared, so what's seen, copied and sent are the same, as obm crashes report prints exactly what it shares. Add a view-model test that the text Copy report uses has no home folder.

### 8. obm secrets migrate deletes the only copy of secrets in an unlocked non-default keyring

- **Rating:** medium
- **Area:** Engine (obmd); Platform layers
- **Where:** `src/OpenBackupManager.Engine/Secrets.cs`: 91, 93, 98, 114, 123, 126, 131, 140, 166; `src/OpenBackupManager.Platform.Linux/SecretService.cs`: 17, 18, 28, 33, 41, 43, 51, 57, 59, 193, 194, 202; `src/OpenBackupManager.Cli/Migrate.cs`: 48
- **Status:** CONFIRMED
- **What's wrong:** SecretService.ReadAsync (SecretService.cs:17-18) looks only in the default keyring, while ListAsync and RemoveAsync cover every keyring. So Secrets.CopyAsync skips a listed name that reads as missing but still returns it (Secrets.cs:126), and MigrateAsync deletes it from every keyring, breaking platform.md's promise that migrate copies everything before removing anything.
- **Failure scenario:** On Linux with secret-store = "secret-service", this home's rclone-config secret is in the login keyring, but another unlocked keyring is now the default (Seahorse's Set as default). `obm secrets migrate file` prints "There were no secrets to move. They'll be kept in `<home>/secrets.json`" and exits 0, but writes no secrets.json and deletes the secret from every keyring. Today only items saved by hand are at risk, but once the engine saves its rclone config password, this deletes its only copy.
- **Suggested fix:** Have SecretService.ReadAsync search every keyring as ListAsync does, preferring the default keyring's item. Have MigrateAsync remove from the old store only the names it copied and read back, and fail, or name as left behind, any listed name it couldn't read.

### 9. Uncaught request exceptions get no reply or log and make obmd's next stop abort

- **Rating:** medium
- **Area:** Engine (obmd); Core
- **Where:** `src/OpenBackupManager.Engine/Listener.cs`: 59, 136, 154, 177, 180; `src/OpenBackupManager.Engine/Connection.cs`: 94, 102; `src/OpenBackupManager.Engine/Secrets.cs`: 70, 72; `src/OpenBackupManager.Daemon/Program.cs`: 111; `src/OpenBackupManager.Core/Platform/FileStore.cs`: 100, 104, 109
- **Status:** CONFIRMED
- **What's wrong:** Listener.ServeAsync and Connection catch only EngineException and IOException, so any other exception, such as FileStore's InvalidDataException that MigrateAsync's pre-check (Secrets.cs:72) lets through, faults the connection's task with no reply or log. If the engine stops before another client connects, Listener.StopAsync rethrows it and Program.cs doesn't catch it.
- **Failure scenario:** On Linux, secrets.json was hand-edited with a trailing comma, and `obm secrets migrate file`, as the guide tells server users, prints only 'obm: The engine closed the connection' and exits 1. The engine logs nothing, so FileStore's 'isn't valid JSON' message reaches no one. If nothing else connects first, the next systemctl --user stop makes obmd exit 134 instead of 0, leave the systemd unit failed and write a crash report.
- **Suggested fix:** Catch every other exception per request in ServeAsync or Connection.AnswerAsync, log it and answer RpcError.Failed with its message, so no request can fault a task StopAsync awaits. Add InvalidDataException to the filter at Secrets.cs:72 so obm prints FileStore's own message.

### 10. Config folder, config.toml or watcher errors crash obmd and the Windows app at start

- **Rating:** medium
- **Area:** Engine (obmd); Core; Windows app
- **Where:** `src/OpenBackupManager.Daemon/Program.cs`: 94, 96; `src/OpenBackupManager.Engine/EngineHost.cs`: 26, 53, 55; `src/OpenBackupManager.Core/Configuration/ConfigWatcher.cs`: 29, 30, 42, 44, 46, 133, 148, 150, 159; `src/OpenBackupManager.Core/Configuration/ConfigFile.cs`: 52; `src/OpenBackupManager.App.Windows/Program.cs`: 98, 116, 118, 148, 217
- **Status:** CONFIRMED
- **What's wrong:** ConfigWatcher's constructor makes the config folder, reads config.toml and starts its watchers with no error handling. EngineHost.Build, obmd's Program.cs and the app's Main, which also takes its home's lock with no handler, catch only ConfigException, so an IOException, UnauthorizedAccessException or ArgumentException aborts obmd and silently ends the app.
- **Failure scenario:** On a NAS running other containers as the guide's 1000:1000, that uid's 128 inotify instances (the kernel default) are all in use. Every obmd start then prints an unhandled IOException, 'The configured user limit (128) on the number of inotify instances has been reached', with 21-26 lines of stack, and aborts with 134. Docker's restart: unless-stopped loops it forever, and systemd gives up after 5 tries and doesn't try again once instances free up.
- **Suggested fix:** Make ConfigWatcher's watchers best effort, logging that edits need obm config reload, and give a one-line refusal naming the path when the config folder or config.toml can't be made or read. The app's Main should show the home and reason when its lock fails.

### 11. Another local user's flock on the engine's world-readable files can keep it from starting

- **Rating:** medium
- **Area:** Engine (obmd); Core
- **Where:** `src/OpenBackupManager.Daemon/Program.cs`: 72, 73, 85; `src/OpenBackupManager.Daemon/SecretsRemoval.cs`: 15; `src/OpenBackupManager.Core/ProcessLock.cs`: 17, 21, 24; `src/OpenBackupManager.Core/Configuration/ConfigWatcher.cs`: 29, 30; `src/OpenBackupManager.Core/Configuration/ConfigFile.cs`: 326; `src/OpenBackupManager.Core/CrashReports.cs`: 44, 48
- **Status:** CONFIRMED
- **What's wrong:** obmd creates engine.lock (ProcessLock.cs:24), config.toml and engine.running 0644 in 0755 state and config folders, so another local user who can reach them can flock them. A held engine.lock passes for a running engine, and an exclusive flock on config.toml or a crash-left engine.running aborts obmd's start with an unhandled IOException.
- **Failure scenario:** On Linux, with the victim's home 0755 or 0711 (as for accounts made before Ubuntu 21.04 or Debian 12), another user runs `flock -s ~victim/.local/state/openbackupmanager/engine.lock sleep infinity`. It waits out the running engine and takes the lock at its next stop, such as an update restart, a crash or a reboot. From then on obmd exits 1 with 'the engine is already running' while obm status says it isn't, and the systemd service gives up.
- **Suggested fix:** Create the state and config folders, and any parents obmd makes, as 0700, as the XDG spec asks. Create engine.lock, engine.running and config.toml as 0600 with UnixCreateMode, as FileStore does for secrets.json, and chmod existing ones, so a --home others can reach is covered.

### 12. An update restarts a service's engine despite a config.toml error, leaving no engine running

- **Rating:** medium
- **Area:** Engine (obmd)
- **Where:** `src/OpenBackupManager.Engine/UpdateWatcher.cs`: 73, 76; `src/OpenBackupManager.Engine/EngineHost.cs`: 53; `src/OpenBackupManager.Core/Configuration/ConfigWatcher.cs`: 34; `src/OpenBackupManager.Daemon/Program.cs`: 96, 98; `src/OpenBackupManager.Cli/SystemdService.cs`: 69, 120, 122; `src/OpenBackupManager.Cli/LaunchAgent.cs`: 37, 71; `src/OpenBackupManager.Cli/Update.cs`: 72, 254; `src/OpenBackupManager.Cli/Service.cs`: 23, 24, 26, 96, 102
- **Status:** CONFIRMED
- **What's wrong:** For a service-started engine, UpdateWatcher exits 75 (UpdateWatcher.cs:73) once an update replaces obmd, without checking a new engine can load config.toml. So an engine keeping its last good settings through a config.toml error restarts into one that refuses at ConfigWatcher's first read, and obm service install does the same.
- **Failure scenario:** On Linux, the user sets secret-store = "File" in config.toml while the service's engine runs, so it keeps its last good settings. After sudo apt install of a newer .deb, the engine exits 75, and the next 5 starts exit 1 with 'The engine won't start until it's fixed', so systemd marks the unit failed (start-limit-hit). Fixing config.toml doesn't bring it back until systemctl --user restart, obm service install or the user manager's next start.
- **Suggested fix:** Before exiting 75 at UpdateWatcher.cs:73, check the new engine can load config.toml (at least ConfigWatcher.Result.HasErrors); if not, keep running and restart on the next clean reload. obm update and obm service install, which already have the engine's config problems, can warn or refuse first.

### 13. obm data remove deletes user files in a home's logs/, cache/, crashes/ and run/

- **Rating:** medium
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Data.cs`: 11, 15, 40, 95, 109, 112, 114
- **Status:** CONFIRMED
- **What's wrong:** With a home, RemoveFromHome (Data.cs:109-114) recursively deletes each MadeInAHome folder, including logs, cache, crashes and run (Data.cs:15), though Data.cs:11 says a home may hold the user's own files. The programs only write a few named files there, and the prompt only says "What Open Backup Manager keeps in `<home>`".
- **Failure scenario:** The home, set by --home or OBM_HOME, is a folder also used for other things, such as a project folder, and already has its own logs/, cache/, crashes/ or run/. obm data remove --home `<it>` --yes deletes the user's logs/build.log, cache/big.dat, crashes/core.txt and run/start.sh along with the engine's files, prints "Removed all of Open Backup Manager's data" and exits 0, with no undo. Only top-level entries it doesn't name, such as src/, are kept.
- **Suggested fix:** Inside logs, crashes and run, delete only the programs' files (`engine-*.log`, `app-*.log`, obmd-stderr.log, `crash-*.txt`, shown, engine.sock), leave cache alone until something writes to it, and remove each folder only if that leaves it empty. Have DataTests put a user file in each folder.

### 14. When the umask allows, other users can change obm update's download before it's installed

- **Rating:** medium
- **Area:** Command line (obm); Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.Cli/Update.cs`: 73, 77, 83, 165, 181; `src/OpenBackupManager.Cli/Releases.cs`: 74, 80; `src/OpenBackupManager.Cli/Crashes.cs`: 68, 69
- **Status:** CONFIRMED
- **What's wrong:** obm writes temporary files into shared /tmp with the umask's mode (Update.cs:73, Crashes.cs:68-69). obm update hashes its download from the network bytes but unpacks whatever the file then holds over obm and obmd, and obm crashes report saves a long report under a predictable name, following links and catching nothing.
- **Failure scenario:** On a Linux machine where accounts share a primary group with umask 002, a user with a tarball install in `~/.local/bin` runs obm update, and another account in that group writes into the 0664 download before Replace reads it. obm still says the SHA-256 hash matches, prints 'Updated obm and obmd' and exits 0. The changed obmd then runs as the user, also from their service, as reproduced with throwaway accounts.
- **Suggested fix:** Create both files in a private Directory.CreateTempSubdirectory() folder (0700 on Unix) with FileMode.CreateNew and UnixCreateMode 0600. obm update should also hash the bytes as Replace reads them and install only on a match, and obm crashes report should catch I/O errors and print a message.

### 15. obm update overwrites other programs' files and skips files a later release adds

- **Rating:** medium
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Update.cs`: 171, 175
- **Status:** CONFIRMED
- **What's wrong:** Apart from obm and obmd, Replace (Update.cs:175) installs only tarball files that already exist beside obm. In `~/.local/bin`, where the guide unpacks only obm and obmd, such a file is another program's and is silently replaced, while one a later release adds, like rclone, is never installed.
- **Failure scenario:** A user's `~/.local/bin` holds obm and obmd, unpacked as the guide says, plus k9s, unpacked with its LICENSE. obm update exits 0 saying "Updated obm and obmd in ...", and k9s's LICENSE now holds Open Backup Manager's MIT licence.
- **Suggested fix:** Install from a list the release carries, and replace files other than obm and obmd only if obm recorded them as its own. Keep licences and later helpers like rclone out of a shared PATH folder, for example in their own folder beside obmd.

### 16. Under a restrictive umask, obm update keeps other users from running a shared install

- **Rating:** medium
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Update.cs`: 181, 193
- **Status:** CONFIRMED
- **What's wrong:** Replace unpacks each file with TarEntry.ExtractToFile (Update.cs:181), which applies the process umask to the entry's mode (0755), then renames it over the old file (Update.cs:193) without restoring the old mode. Under a restrictive umask, obm and obmd lose their group and other permissions, yet obm update reports success.
- **Failure scenario:** A tarball install in /usr/local/bin, shared by several users, was unpacked by root with the guide's tar command (0755), and root runs obm update under CIS's umask 027. obm and obmd become 0750, yet obm prints 'Updated obm and obmd' and exits 0, and other users then get 'Permission denied' running obm. At their next restart, their systemd user service can't exec obmd, so their engine stays down until an admin fixes the modes.
- **Suggested fix:** Before each rename, set the unpacked file's mode with File.SetUnixFileMode (a chmod, which the umask doesn't filter) to File.GetUnixFileMode of the file it replaces, or to the entry's mode when there's no such file.

### 17. obm service install stops a running engine before learning it can't install the service

- **Rating:** medium
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Service.cs`: 23, 24, 56, 87, 101, 102; `src/OpenBackupManager.Cli/SystemdService.cs`: 56, 58, 59, 61, 64, 65, 66, 67; `src/OpenBackupManager.Cli/LaunchAgent.cs`: 38, 40, 41, 42
- **Status:** CONFIRMED
- **What's wrong:** InstallAsync (Service.cs:23) stops any engine its service didn't start before service.Install() runs any step that can fail, so when installing fails, the user's engine is gone. Those steps include systemd's reachability check (SystemdService.cs:59), though its comment says it's checked first so nothing is left behind, and launchctl bootstrap (LaunchAgent.cs:42).
- **Failure scenario:** obmd was started by hand on Linux where systemctl --user can't reach a user manager, such as WSL with systemd off. obm service install prints 'Stopping the engine that's running, so the service can start it', then 'obm: systemd isn't present or can't be reached for this user, so the engine can't be installed as a service', and exits 1. obm status then says 'the engine isn't running'.
- **Suggested fix:** Do every step that can fail before stopping a hand-started engine. For systemd, check it's reachable, write the unit, daemon-reload and enable before taking over and restarting; for launchd, check the `gui/<uid>` domain exists and write the plist first, removing it if bootstrap fails.

### 18. Uninstalling the service says the engine stopped without checking, and it can keep running

- **Rating:** medium
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/SystemdService.cs`: 72, 75, 78, 81; `src/OpenBackupManager.Cli/Service.cs`: 73, 81; `src/OpenBackupManager.Cli/Data.cs`: 72, 131, 133, 134, 135; `src/OpenBackupManager.Cli/LaunchAgent.cs`: 47
- **Status:** CONFIRMED
- **What's wrong:** obm service uninstall and obm data remove print "The engine has stopped" once service.Uninstall() returns (Service.cs:81, Data.cs:134), without checking that no engine still answers. SystemdService.Uninstall (SystemdService.cs:75-81) skips systemctl --user disable --now whenever show-environment fails, as in a shell with no XDG_RUNTIME_DIR while systemd runs, yet still deletes the unit.
- **Failure scenario:** On a server with lingering on, the service was installed over SSH, and an admin runs obm service uninstall in a sudo su - backupuser shell, where systemctl --user fails with "No medium found". obm prints "The engine has stopped, and no longer starts when you sign in" and exits 0, but systemd keeps the unit enabled and running until the computer restarts. Every later uninstall, from any shell, says "The engine isn't installed as a service".
- **Suggested fix:** After service.Uninstall(), stop any engine still answering on the socket, as Data.StopAsync does, and only then print "has stopped". When the unit file exists but systemd can't be reached, have SystemdService.Uninstall refuse as Install does, suggesting machinectl shell, instead of deleting the unit.

### 19. Uncaught update-check errors stop 6-hourly checks until restart and close the app from About

- **Rating:** medium
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/Updater.cs`: 55, 72, 162, 164, 173, 178, 213; `src/OpenBackupManager.ViewModels/AboutViewModel.cs`: 48, 60, 61, 62
- **Status:** CONFIRMED
- **What's wrong:** Updater.Check (Updater.cs:213) catches only HttpRequestException and IOException, but Velopack 1.2.161 also throws ChecksumFailedException, AcquireLockFailedException, JsonException and, on its 30-minute timeout, TaskCanceledException. These end CheckRegularly's loop without logging 'Couldn't check for updates', and from About the AsyncRelayCommand rethrows them on UiSynchronizationContext, which reports a crash and closes the app.
- **Failure scenario:** A work laptop's TLS-inspecting web filter answers the package download or api.github.com with its own block page and a 200 status, so the startup check fails with ChecksumFailedException ('Size doesn't match') or JsonException. The regular-check loop ends, so that copy never checks again, even at home once a newer release is out, until the app restarts. Each click on About > Check for updates there closes the app, and the next start shows the Closed unexpectedly window.
- **Suggested fix:** In Check, catch every exception except a cancellation from _stop, for example with catch (Exception e) when (!_stop.IsCancellationRequested), log it as 'Couldn't check for updates' and return Failed. In CheckRegularly, leave the loop only when _stop is cancelled.

### 20. If obmd.exe can't be started, the app says Starting forever and crashes on Quit

- **Rating:** medium
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/EngineConnection.cs`: 35, 41, 108, 110, 122, 124, 126, 164, 165, 193; `src/OpenBackupManager.App.Windows/AppActions.cs`: 52, 63, 79, 90
- **Status:** CONFIRMED
- **What's wrong:** KeepConnectedAsync catches only EngineException (EngineConnection.cs:122-130), so when Process.Start throws Win32Exception for a missing or blocked obmd.exe, the connection task dies after showing Starting and never restarts. Awaiting that task, StopConnectingAsync catches only OperationCanceledException, so the exception escapes the async void Quit and RestartToUpdate as a crash that ends the app.
- **Failure scenario:** Antivirus quarantines the unsigned obmd.exe; at the next start the app logs 'Starting the engine, `<path>`' once, and the tooltip and flyout then say Starting for good, never Can't reach the engine. When the user chooses Quit, the app logs 'Closing because of an error nothing caught', writes `crash-<time>-app.txt` and ends before installing a waiting update. The next start shows 'Open Backup Manager closed unexpectedly', and this repeats at every start and Quit until obmd.exe is back.
- **Suggested fix:** Catch Process.Start's failure in ConnectOrStartAsync, log it with the path and return null, so the loop shows Can't reach the engine and retries. Have StopConnectingAsync log any exception from _running and carry on, so Quit and Restart to update always finish.

### 21. The app's engine never starts when its home ends in a backslash, like E:\

- **Rating:** medium
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/EngineConnection.cs`: 193; `src/OpenBackupManager.App.Windows/Program.cs`: 83, 128, 157, 159, 253
- **Status:** CONFIRMED
- **What's wrong:** Program.Home is Path.GetFullPath of --home or OBM_HOME, which keeps a trailing separator (a drive root always has one). EngineConnection.StartEngine and other command lines write it as --home "{home}" without escaping, so by Windows' rules the last backslash escapes the closing quote and the home swallows every later argument.
- **Failure scenario:** With OBM_HOME=D:\OBM\, the app starts obmd with --home "D:\OBM\" --started-by app, which obmd reads as the single home 'D:\OBM" --started-by app'. Windows won't create a folder with a quote in its name, so obmd prints 'the engine can't keep its state' to its hidden console and exits 1. The app logs 'The engine exited with code 1 during startup', shows 'Can't reach the engine' with no reason and keeps retrying, backing off to once a minute.
- **Suggested fix:** Quote these arguments as .NET's PasteArguments does, doubling the backslashes before a closing quote, or give obmd its arguments through ProcessStartInfo.ArgumentList, which ShellExecute uses too. Trimming the trailing separator when choosing the home isn't enough, as a drive root such as E:\ keeps it.

### 22. Package jobs give every step signing rights, and package-docker push rights, on Renovate branches

- **Rating:** medium
- **Area:** Build, release and supply chain
- **Where:** `.github/workflows/ci.yml`: 7, 10, 28, 254, 255, 263, 268, 290, 321, 356, 357, 368, 384, 402, 523, 524, 583, 622, 623, 624, 631, 633, 651, 655, 668, 686; `docs/guide/checking-downloads.md`: 8, 16, 24, 29
- **Status:** CONFIRMED
- **What's wrong:** package-windows, package-macos and check-linux grant id-token and attestations write, and package-docker packages write, job-wide (ci.yml:254-255, 622-624), though only SHIP/PUSH-gated steps use them. Any step's third-party code on `renovate/*` or same-repo PR runs can attest any file, passing the guide's `gh attestation verify --repo`, and in package-docker push images.
- **Failure scenario:** A hijacked maintainer account publishes a malicious Microsoft.Windows.CsWin32 1.0.0, and Renovate opens a branch and PR, whose CI runs at once though majors never automerge. In package-windows' Publish, its build task, with the OIDC request variables and GH_TOKEN in its environment, gets a Sigstore certificate for the Renovate branch, signs SLSA provenance for an unrelated evil.exe and POSTs it to the repository's attestations. `gh attestation verify evil.exe --repo AmyJeanes/OpenBackupManager` then passes, even after the PR is closed.
- **Suggested fix:** Move the Attest steps and the image push into separate jobs gated on SHIP/PUSH that alone hold the write permissions. Set GH_TOKEN only where needed and persist-credentials: false on checkouts that don't push, and add --signer-workflow and --source-ref to the guide's verify commands.

### 23. The dependency-graph job runs Component Detection's latest release unchecked, with a release-publishing token

- **Rating:** medium
- **Area:** Build, release and supply chain
- **Where:** `.github/workflows/dependency-graph.yml`: 18, 20, 31
- **Status:** CONFIRMED
- **What's wrong:** The submission action pinned at dependency-graph.yml:31 (v0.2.0) downloads and runs microsoft/component-detection's latest release on every run, with no version pin, hash or signature check, in a job whose token has contents: write. The binary inherits that token as INPUT_TOKEN, and checkout's persisted credentials hold it too.
- **Failure scenario:** A malicious or hijacked microsoft/component-detection release runs at the next push to main with this repository's contents: write token. It publishes a release tagged 9.9.9, outside the Version tags ruleset, with a crafted `OpenBackupManager-9.9.9-<runtime>.tar.gz` and a releases.win-x64.json with its package. obm update takes the highest non-draft release whatever its tag and checks only GitHub's digest of the upload, so it installs the crafted obm and obmd, and installed Windows apps install it within 6 hours by default.
- **Suggested fix:** Scan in a job with only contents: read, using a fixed, SHA-256-checked Component Detection release, and submit the snapshot artifact from a separate contents: write job running only gh api. Or delete the workflow until built-in NuGet scanning works, as its comment plans.

### 24. On Windows, CI's Sandbox step silently skips Cli.Tests' Credential Manager and obmd crash tests

- **Rating:** medium
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Cli.Tests/ServiceTests.cs`: 11, 12; `tests/OpenBackupManager.Cli.Tests/DataSecretsTests.cs`: 18; `tests/OpenBackupManager.Cli.Tests/DaemonTests.cs`: 291, 318; `.github/workflows/ci.yml`: 83
- **Status:** CONFIRMED
- **What's wrong:** NUnit3TestAdapter's default Strict mode adds 'not Explicit' to a category filter unless every selected fixture is explicit, and on Windows either of ServiceTests' platform attributes (ServiceTests.cs:11-12) makes it Skipped, not Explicit. So '--filter TestCategory=Sandbox' (ci.yml:83) also skips DataSecretsTests and DaemonTests' two crash tests, and Cli.Tests runs 0 tests there.
- **Failure scenario:** A change stops obmd's 'data remove' taking secrets out of Credential Manager, yet CI's 'Test the Sandbox category' step on windows-latest and windows-11-arm still passes with 'None ... Total: 0 - OpenBackupManager.Cli.Tests.dll', as in run 42541203860 for f22a7523. Remove_TakesTheSecretsFromTheOSsStore and both crash tests never run on Windows, though CONTRIBUTING says CI runs the Sandbox category. The crash tests ran there (Passed: 2) until ServiceTests was added in 270c5ae; DataSecretsTests never has.
- **Suggested fix:** Wrap ServiceTests.cs in #if !WINDOWS, so every Sandbox test left in Cli.Tests on Windows is explicit, or set NUnit's ExplicitMode to Relaxed in a runsettings file. Also have CI fail a Sandbox step in which a project with Sandbox tests ran none.

### 25. The advised 'docker compose down -v' leaves all data in the guide's bind-mounted /config

- **Rating:** medium
- **Area:** User guide
- **Where:** `docs/guide/files.md`: 136; `src/OpenBackupManager.Cli/Data.cs`: 22
- **Status:** CONFIRMED
- **What's wrong:** files.md:136 tells Docker users to remove the /config volume 'such as with docker compose down -v', and obm data remove refuses in Docker with the same advice (Data.cs:22). The guide's compose file bind-mounts './config:/config' and the image declares no VOLUME, so 'down -v' leaves every file.
- **Failure scenario:** A Docker user with the guide's compose.yaml runs 'docker exec ... obm data remove -y', which exits 1 and tells them to use 'docker compose down -v'. Running it exits 0, but the host's ./config still holds config.toml, logs, crash reports and diagnostics zips, and will hold secrets.json in plain text once secrets are saved. The user thinks it's all removed, and a later 'docker compose up -d' there carries on with the old settings and logs.
- **Suggested fix:** For the guide's compose file, say to run 'docker compose down', then delete the config folder ('rm -rf config', with sudo if another user owns it), and keep 'down -v' only for a named volume. Change Data.cs:22's message and DataTests.cs:128's pinned text to match.

### 26. With dotted or inline-table updates, an env var silently drops the other updates key

- **Rating:** low
- **Area:** Core
- **Where:** `src/OpenBackupManager.Core/Configuration/ConfigFile.cs`: 80, 85, 129, 130, 166, 168
- **Status:** CONFIRMED
- **What's wrong:** ConfigFile.Edit finds a section only by its [table] header, so with updates written as dotted keys or an inline table, an env var for the other updates key appends a second [updates] table. Parse deserializes that without SyntaxParser's redefinition check, and the new table silently replaces the file's keys.
- **Failure scenario:** On Windows, config.toml has `updates.automatic = false` (or `updates = { automatic = false }`) and the user's environment has OBM_UPDATES_PRERELEASES=true. Updates.Automatic then reads true, and obm config validate --env says 'no errors or warnings'. So the app checks GitHub at start and every 6 hours, downloads updates and notifies, and installs a downloaded update when it quits, though the file turns automatic updates off.
- **Suggested fix:** Make Edit treat dotted keys and an inline table as the section, or apply env vars to the deserialized AppConfig instead. Either way, also run SyntaxParser's checks on the merged text, so a clash can't pass silently.

### 27. Env vars for [updates] are silently dropped when an empty [updates] follows another line

- **Rating:** low
- **Area:** Core
- **Where:** `src/OpenBackupManager.Core/Configuration/ConfigFile.cs`: 70, 80, 85, 162, 163, 316, 318; `src/OpenBackupManager.Cli/Update.cs`: 110; `src/OpenBackupManager.App.Windows/Updater.cs`: 41, 49, 116, 134, 171
- **Status:** CONFIRMED
- **What's wrong:** For a section with no keys, Edit (ConfigFile.cs:162) inserts at LineEnd(section.Span.Start.Offset), which searches from the newline before the '[', putting the env var's line above the header. There it joins the table above and is ignored as an unknown key; Check, seeing only the file's own text, reports nothing.
- **Failure scenario:** config.toml is the default file with '[updates]' uncommented but both keys under it still commented. With OBM_UPDATES_PRERELEASES=true, 'obm update --check' still skips a newer prerelease, exactly as with it unset, and with OBM_UPDATES_AUTOMATIC=false the Windows app still checks and downloads every 6 hours and installs on quit. 'obm config validate --env' says 'config.toml has no errors or warnings', though the guide says an env var wins over the file.
- **Suggested fix:** For a section with no keys, insert after its header line, such as at LineEnd(text, section.Span.Start.Offset + 1), rather than searching from the character before the '['. Add an Edit test and a Parse env-var test with a line above an empty [updates].

### 28. Disk errors like a read-only mount are reported as 'the engine is already running'

- **Rating:** low
- **Area:** Core; Engine (obmd)
- **Where:** `src/OpenBackupManager.Core/ProcessLock.cs`: 24, 26, 28; `src/OpenBackupManager.Daemon/Program.cs`: 75, 83; `src/OpenBackupManager.Daemon/SecretsRemoval.cs`: 15, 16; `src/OpenBackupManager.App.Windows/Program.cs`: 217; `src/OpenBackupManager.Cli/Data.cs`: 26
- **Status:** CONFIRMED
- **What's wrong:** ProcessLock.TryTake's catch (IOException) at ProcessLock.cs:26 returns null, meaning a held lock, for EROFS, EIO, ENOSPC and the like too. Every caller then reports a running engine or app, and obmd's 'can't keep its state' message (Program.cs:75) is never reached once the state folder exists.
- **Failure scenario:** Docker's ./config:/config is mounted :ro by mistake. obmd then prints 'obmd: the engine is already running' and exits 1 on every restart, while obm status says 'obm: the engine isn't running'. So the user looks for a second engine instead of at the disk.
- **Suggested fix:** In TryTake, return null only for a lock conflict: HResult EWOULDBLOCK (11 on Linux, 35 on macOS), or ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION on Windows. Let other errors reach Program.cs's 'can't keep its state' message, and catch IOException and UnauthorizedAccessException in SecretsRemoval.RunAsync, TakeLockOrHandOver and Data.RemoveAsync.

### 29. obm hangs with no output when the engine accepts the connection but doesn't answer

- **Rating:** low
- **Area:** Core
- **Where:** `src/OpenBackupManager.Core/Protocol/EngineClient.cs`: 83, 170; `src/OpenBackupManager.Cli/Status.cs`: 33, 43; `src/OpenBackupManager.Cli/Diagnostics.cs`: 126; `src/OpenBackupManager.Cli/Data.cs`: 138; `src/OpenBackupManager.Cli/Service.cs`: 89; `src/OpenBackupManager.Cli/Config.cs`: 15; `src/OpenBackupManager.Cli/Migrate.cs`: 15; `src/OpenBackupManager.Cli/Update.cs`: 212
- **Status:** CONFIRMED
- **What's wrong:** ConnectAsync's timeout covers only connecting: the hello (EngineClient.cs:83) and later calls wait in CallAsync (EngineClient.cs:170) on the caller's token alone, which in obm only Ctrl+C or SIGTERM cancels. When nothing answers, obm status, diagnostics export, data remove, config reload, secrets migrate, service install and update hang with no message.
- **Failure scenario:** On Linux or macOS, obmd runs in a terminal, as the guide suggests, and the user presses Ctrl+Z; the kernel still accepts connections to its socket. `obm status` prints nothing and never returns: `timeout` killed it after 15 seconds (exit 124), and Ctrl+C ends it with exit 1 and no message. `obm diagnostics export`, the command for when the engine seems stuck, hangs before writing anything, so no zip is made.
- **Suggested fix:** Give the hello and short calls, such as status, reload and stop, a limit of a few seconds linked to the caller's token, and on timeout throw an EngineException saying the engine accepted the connection but didn't answer. Leave secrets migrate's own call unlimited.

### 30. Edits are missed when config.toml links within its folder or is re-pointed while running

- **Rating:** low
- **Area:** Core
- **Where:** `src/OpenBackupManager.Core/Configuration/ConfigWatcher.cs`: 44, 70, 150
- **Status:** CONFIRMED
- **What's wrong:** ConfigWatcher.cs:44 adds no watch for a link target in config.toml's folder, whose watcher filters on config.toml, but inotify reports the target's name even for writes through the link, so those edits are never seen. It resolves the link only at start, so a re-pointed link's new target goes unwatched.
- **Failure scenario:** With `~/.config/openbackupmanager/config.toml` -> config-laptop.toml and obmd running, the user saves config-laptop.toml, or saves config.toml with any editor that writes in place or through the link. Nothing is applied or logged, and obm status shows no warning or error from the edit until obm config reload, SIGHUP or a restart. Yet settings.md says changes apply as soon as you save, and config.md says linked files are followed.
- **Suggested fix:** Resolve the link again on every reload, and keep one watcher on the current target whenever its full path differs from config.toml's, skipping a target whose folder doesn't exist, as FileSystemWatcher throws for one. Add tests for a sibling target and a re-pointed link.

### 31. A power cut soon after config.toml is written can leave it empty or NUL-filled

- **Rating:** low
- **Area:** Core
- **Where:** `src/OpenBackupManager.Core/Configuration/ConfigFile.cs`: 326, 329, 339
- **Status:** CONFIRMED
- **What's wrong:** ConfigFile.Write renames config.toml.tmp over config.toml without flushing it to disk, and doesn't flush the .bak copy either, unlike FileStore, which flushes before its rename. On file systems that can commit a rename before the data, a power cut in that window leaves config.toml empty or full of NULs.
- **Failure scenario:** On Linux with XFS, 'obm secrets migrate file' rewrites a hand-edited config.toml and prints success. If the power goes after XFS commits the rename but before the data is written back, up to about 60 s after the write when idle, config.toml comes back as 0 bytes, as a simulated power cut showed. obmd then starts on the defaults with no warning, so status says 'Secrets: no store chosen yet', and the old settings survive only in config.toml.bak.
- **Suggested fix:** Write the temporary file through a FileStream and call Flush(flushToDisk: true) before File.Move, as FileStore.WriteAsync does, so the rename can't reach the disk before the data. Write and flush the .bak copy the same way.

### 32. Two unhandled errors on different threads at once leave no crash report

- **Rating:** low
- **Area:** Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.Engine/CrashCapture.cs`: 94, 104, 115, 118; `src/OpenBackupManager.App.Windows/CrashCapture.cs`: 66, 88, 91; `src/OpenBackupManager.Core/CrashReports.cs`: 142
- **Status:** CONFIRMED
- **What's wrong:** In obmd and the app, Capture returns at once for later errors, so when a second thread's handler returns before the first finishes, the runtime ends the process, cutting off the first's report. CrashReports.Ending recognises only 'Process terminated.', so on Linux the next start can't recover the error either.
- **Failure scenario:** Two engine threads, such as the config settle timer and the SIGHUP handler, can throw together: with the DEBUG crash request sent on two connections at once, obmd exited 134 with no `crash-*.txt` and engine.running left in 6 of 6 JIT and 6 of 8 NativeAOT runs. The next start on Linux writes a report saying 'It didn't say why. It may have crashed, or been closed by the system or another program.', with no exception.
- **Suggested fix:** When another thread is capturing, wait, for a bounded time such as 10 s, until it has written the report and removed the running file, but return immediately on the same thread, for the app's WinUI-then-.NET double report. Let CrashReports.Ending accept 'Unhandled exception.' too.

### 33. On Windows, obm diagnostics export includes other accounts' crash and hang records, unredacted

- **Rating:** low
- **Area:** Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.Core/SystemRecords.cs`: 20, 25; `src/OpenBackupManager.Cli/Diagnostics.cs`: 54, 144; `src/OpenBackupManager.Core/Redaction.cs`: 14
- **Status:** PLAUSIBLE
- **What's wrong:** SystemRecords.WindowsEvents reads the machine-wide Application log and keeps 7 days of Application Error, Application Hang and .NET Runtime entries naming the app, obmd or obm, from any account. RedactAll removes only this user's profile folder, user name and computer name, so other accounts' paths and exception messages reach windows-events.txt unredacted.
- **Failure scenario:** Alice and Bob have their own accounts on one Windows PC, each with the app installed, and Bob's obmd.exe crashed this week. Alice runs obm diagnostics export for a public issue, as the guide suggests, and windows-events.txt holds Bob's Application Error 1000 and .NET Runtime 1026 entries as written, such as 'Faulting application path: C:\Users\Bob Smith\AppData\Local\OpenBackupManager.App\current\obmd.exe'. Her own entries are redacted, so redaction looks done, but Bob's name and file paths go public without his knowledge.
- **Suggested fix:** Keep only Application Error and Hang entries whose application path is under this user's profile or this install's folder, and .NET Runtime entries whose process ID matches one of those or this home's crash reports. For the rest, say only how many were left out.

### 34. On Windows 10, hangs and crashes .NET never records get no report

- **Rating:** low
- **Area:** Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.Core/CrashEvents.cs`: 34, 82, 83
- **Status:** PLAUSIBLE
- **What's wrong:** On Windows 10 (and, for hangs, Windows 11 21H2), Application Hang 1002 and Application Error 1000 are classic events, whose EventRecord.Properties values are strings, with the process ID in hex without 0x, such as "a94" or "1208". CrashEvents.IsProcessId parses only strings starting with 0x, so WindowsEntry never matches them.
- **Failure scenario:** On Windows 10 22H2, the app's UI thread hangs, and the user closes a window and chooses Close the program; WER logs Application Hang 1002 with the process ID "1a2c" and restarts the app. On restart, WindowsEntry's IsProcessId("1a2c", 0x1a2c) is false, and .NET writes no entry for a hang. So no report is written and no Closed unexpectedly window appears, only the log line "closed unexpectedly without saying why".
- **Suggested fix:** Read the process ID from its fixed position, Application Error's ninth value and Application Hang's third, and accept an int, a uint or a hex string with or without 0x. Don't scan all values for bare hex, which would also hit timestamps and offsets.

### 35. Copy report, and usually Report on GitHub, close the app when the clipboard's busy

- **Rating:** low
- **Area:** Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.App.Windows/CrashWindow.xaml.cs`: 26, 32, 42
- **Status:** PLAUSIBLE
- **What's wrong:** CrashWindow.Copy calls Clipboard.SetContent with no try/catch, and Microsoft documents that SetContent throws when it can't set the clipboard, as when another process has it open (COMException 0x800401D0, CLIPBRD_E_CANT_OPEN). The exception leaves the click handler, App.xaml.cs:22 hands it to CrashCapture without marking it handled, and the app closes.
- **Failure scenario:** A clipboard manager reading a recent copy has the clipboard open as the user clicks Copy report, or Report on GitHub for a report over about a dozen log lines (most reports). The app closes without copying or opening the issue. A crash report about the COMException is written, and the next start shows it instead of the original, unless the app had started under a minute before, within a minute of an earlier crash.
- **Suggested fix:** Use Clipboard.SetContentWithOptions, which returns false instead of throwing, or catch the exception and retry briefly, as WPF and WinForms do (10 x 100 ms). If it still fails, say so in the window so the user can copy the report from its text box.

### 36. Config warnings logged before CrashCapture.Start land in the last run's crash report, misdating it

- **Rating:** low
- **Area:** Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.Daemon/Program.cs`: 94, 105; `src/OpenBackupManager.Engine/EngineHost.cs`: 53; `src/OpenBackupManager.Core/Configuration/ConfigWatcher.cs`: 40; `src/OpenBackupManager.Engine/CrashCapture.cs`: 17; `src/OpenBackupManager.Core/CrashReports.cs`: 45, 123, 290; `src/OpenBackupManager.App.Windows/Program.cs`: 116, 127; `src/OpenBackupManager.App.Windows/CrashCapture.cs`: 21
- **Status:** CONFIRMED
- **What's wrong:** obmd's EngineHost.Build and the app's Program.Main resolve ConfigWatcher, whose constructor logs every config.toml warning, before CrashCapture.Start, which must run before anything logs. So RecentLog reads the new run's warnings as the dead run's last lines, and WriteEnded takes the heading's time from the log's last write, now the restart's.
- **Failure scenario:** config.toml has a warning, such as an unknown setting, and obmd is killed on Linux and started again. The report's heading says 'obmd closed unexpectedly after 2026-11-09 04:40:10 +00:00, when it last wrote to its log', the restart's time, not its last write (04:39:52), and Recent log ends with the new run's warning. If the dead run last logged on an earlier day, as is usual for an idle Phase 1 engine, Recent log holds none of its lines.
- **Suggested fix:** Start crash capture before ConfigWatcher is resolved in both programs, and call Stopped() when a ConfigException stops the run. Or have the last run's report read only up to where this run's log began (Current's LogFile and LogLength), and take its time from there.

### 37. An interrupted obm diagnostics export leaves a corrupt zip under its final name

- **Rating:** low
- **Area:** Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.Cli/Diagnostics.cs`: 27, 35, 45, 57, 67, 69; `src/OpenBackupManager.Cli/Program.cs`: 168
- **Status:** CONFIRMED
- **What's wrong:** ExportAsync (Diagnostics.cs:35) writes the zip straight to its final name and deletes it only on IOException or UnauthorizedAccessException. Only SummaryAsync gets the cancellation token, so Ctrl+C or SIGTERM while zipping ends obm after System.CommandLine's 2-second termination timeout, and SIGHUP kills it outright, both leaving a zip with no central directory.
- **Failure scenario:** With 70 MB of logs, as from an engine restarting in a loop, the export took 18.9 s, printing nothing. Ctrl+C 6 s in gave exit 130 two seconds later, no message, and an invalid 811 KB `obm-diagnostics-<time>.zip`; SIGTERM gave 143, and SIGHUP (terminal closed, SSH dropped) killed it instantly, both leaving the same. With -o for-the-issue.zip, a rerun fails with 'obm: there's already a file at ...', which suggests it was saved, so the corrupt file gets attached.
- **Suggested fix:** Write the zip under a temporary name such as `<name>.partial` and rename it once the ZipArchive and stream are disposed, deleting it on cancellation or I/O error. Passing the token in isn't enough: one 10 MB log outlasts the 2-second timeout, and SIGHUP isn't handled.

### 38. A crash report in the Docker image suggests coredumpctl info 1, which finds nothing

- **Rating:** low
- **Area:** Crash reports and diagnostics
- **Where:** `src/OpenBackupManager.Core/CrashReports.cs`: 100, 258, 267
- **Status:** CONFIRMED
- **What's wrong:** CrashReports.WhereToLook puts the dead run's PID into `coredumpctl info <pid>` whenever core_pattern pipes to systemd-coredump. In the image obmd is PID 1 of its own PID namespace, but systemd-coredump, via the host's core_pattern, records the host's PID, so the command can't find the crash, and the image lacks coredumpctl.
- **Failure scenario:** On a Fedora host, whose core_pattern pipes to systemd-coredump, obmd in the image fails fast, and the host's systemd-coredump records the crash under obmd's host PID. After the guide's restart: unless-stopped starts it again, the new run's report says 'If it crashed, coredumpctl info 1 may say more.', and never mentions `docker logs`, which holds the FailFast's message and stack. On the host, `coredumpctl info 1` finds no coredumps, which reads as 'it didn't crash'.
- **Suggested fix:** In a container (installed-by is docker, or the PID is 1), name the program instead, for example 'coredumpctl info obmd, run on the host', and say `docker logs` has what it printed. Or match by name and start time everywhere: `coredumpctl info --since=<started> obmd`.

### 39. Engine deletes its service and stops when obmd is unreadable twice in a row

- **Rating:** low
- **Area:** Engine (obmd)
- **Where:** `src/OpenBackupManager.Engine/UpdateWatcher.cs`: 55, 60, 63, 97, 100, 149
- **Status:** CONFIRMED
- **What's wrong:** UpdateWatcher.Identity treats obmd as gone whenever FileInfo.Exists is false, which .NET returns for any failed stat (EACCES, EIO, EHOSTDOWN), not only ENOENT. At two such checks 5 seconds apart, the engine runs systemctl --user disable, deletes its unit or LaunchAgent plist and exits 0, though obmd is still there.
- **Failure scenario:** obm service install was run with obm and obmd in `~/.local/bin`, and later obmd can't be stat'ed for 10 s or more because its folder's permissions are changed, then put back. The engine logs '`<home>/.local/bin/obmd` has been removed, so the engine is stopping', deletes `~/.config/systemd/user/openbackupmanager.service` and exits 0, which Restart=on-failure doesn't restart. obm status then prints only 'the engine isn't running', and the engine won't start at the next sign-in until obm service install is run again.
- **Suggested fix:** In Identity, count only a not-found result as absent, such as File.GetAttributes throwing FileNotFoundException or DirectoryNotFoundException. On any other error, keep the last identity so obmd is checked again, instead of treating an unreadable obmd as removed.

### 40. obm data remove says everything's removed and exits 0 while the secrets remain stored

- **Rating:** low
- **Area:** Engine (obmd); Command line (obm)
- **Where:** `src/OpenBackupManager.Daemon/SecretsRemoval.cs`: 43, 46, 50, 56; `src/OpenBackupManager.Cli/Data.cs`: 70, 95; `src/OpenBackupManager.Platform.Linux/SecretService.cs`: 91
- **Status:** CONFIRMED
- **What's wrong:** obmd --remove-secrets warns but returns 0 when a store is locked or fails (SecretsRemoval.cs:46, :50), and silently when obm's shell lacks DBUS_SESSION_BUS_ADDRESS, as SecretService.cs:91 never tries $XDG_RUNTIME_DIR/bus. Data.RemoveAsync checks only that code (Data.cs:70), so it deletes the data, prints 'Removed all of Open Backup Manager's data' and exits 0.
- **Failure scenario:** On Linux over SSH, GNOME Keyring holds this home's secret but is locked. `obm data remove --yes` prints 'obmd: Secret Service is locked, so any secrets kept there are still in it', then 'Removed all of Open Backup Manager's data', and exits 0, though the guide says a command that couldn't do what it was asked exits 1. An uninstall script sees success and removes the package, leaving the secrets with no obm to remove them.
- **Suggested fix:** Have obmd --remove-secrets exit non-zero when a store that may hold this home's secrets was locked, failed, or was unreachable though config.toml names it, after trying $XDG_RUNTIME_DIR/bus and `/run/user/<uid>/bus`. Then have obm data remove exit 1 with that warning instead of 'Removed all'.

### 41. On Windows, a client closing its pipe early stops the engine listening for good

- **Rating:** low
- **Area:** Engine (obmd)
- **Where:** `src/OpenBackupManager.Engine/Listener.cs`: 122, 124, 133, 144, 146, 150
- **Status:** PLAUSIBLE
- **What's wrong:** AcceptAsync survives only UnauthorizedAccessException from WaitForConnectionAsync (Listener.cs:124); anything else reaches the outer catch (:144), which logs 'Stopped listening, so nothing more can connect' and never listens again, while obmd keeps its lock. On Windows, a client closing the next instance (:133) before ConnectNamedPipe runs makes the wait throw IOException.
- **Failure scenario:** On Windows, a cancelled obm or the quitting tray app opens and closes the next pipe instance in the microseconds before the engine waits on it. From then on, obm status prints 'obm: the engine isn't running' and exits 1, and a new obmd exits with 'the engine is already running'. If it was the tray app, it couldn't stop the engine, and its next launch can't reach it until sign-out or until obmd is ended in Task Manager.
- **Suggested fix:** Catch IOException from WaitForConnectionAsync when not stopping, as Kestrel's named-pipe listener does: make the replacement with Create(first: false) before disposing the failed instance, so the name never lapses, then log it and keep waiting. Keep the outer catch for anything else.

### 42. obmd blames another listener when its home can't hold a socket, as on exFAT

- **Rating:** low
- **Area:** Engine (obmd)
- **Where:** `src/OpenBackupManager.Engine/Listener.cs`: 81, 102, 108, 110
- **Status:** CONFIRMED
- **What's wrong:** On macOS and Linux, the first server's FirstPipeInstance flag (Listener.cs:102) makes .NET 10 turn any bind failure, as on FAT, exFAT or with no free inodes, into an UnauthorizedAccessException, reported as 'Something else is already listening'. A chmod failure other than EPERM, as on fusefat, isn't caught and crashes obmd.
- **Failure scenario:** On Linux, with a portable home (a use config.md names) on an exFAT USB stick, 'obmd --home /media/usb/obm' prints 'obmd: Something else is already listening at /media/usb/obm/run/engine.sock, where the engine listens' and exits 1 every time, so the user looks for a process that isn't there. This was reproduced with exfat-fuse. On a FAT image mounted with fusefat, obmd crashes with exit 134 on an unhandled 'System.IO.IOException: Function not implemented'.
- **Suggested fix:** On macOS and Linux, drop FirstPipeInstance (the engine holds the lock and has just emptied its folder) and catch the SocketException to say the socket can't be made there, as on FAT or exFAT, with the OS's reason. Also catch IOException from the chmod.

### 43. On Linux and macOS, every refused listener start logs a false FATAL blaming UpdateWatcher

- **Rating:** low
- **Area:** Engine (obmd)
- **Where:** `src/OpenBackupManager.Engine/UpdateWatcher.cs`: 45; `src/OpenBackupManager.Engine/EngineHost.cs`: 33; `src/OpenBackupManager.Daemon/Program.cs`: 109; `src/OpenBackupManager.Engine/EngineService.cs`: 17
- **Status:** CONFIRMED
- **What's wrong:** UpdateWatcher is registered before EngineService, so it's already running when Listener.StartAsync throws. host.RunAsync then disposes the host without calling StopAsync, so BackgroundService.Dispose cancels the token WaitForNextTickAsync waits on (UpdateWatcher.cs:45), and the host logs the OperationCanceledException as a failed BackgroundService, at Error and at Critical with the StopHost advice.
- **Failure scenario:** On Linux, obmd given a --home over 91 bytes, the documented limit, exits 1 as intended. After '[ERR] Hosting failed to start' with the real reason, it logs '[ERR] BackgroundService failed' and an [FTL] entry saying 'A BackgroundService has thrown an unhandled exception, and the IHost instance is stopping', each with a trace ending in UpdateWatcher.ExecuteAsync, to the console and `engine-*.log`. So the refusal looks like a crash, pointing log readers and bug reports at the update watcher.
- **Suggested fix:** In UpdateWatcher.ExecuteAsync, end quietly on cancellation: return on an OperationCanceledException once stoppingToken is cancelled, or have stoppingToken dispose the PeriodicTimer and wait with CancellationToken.None (tested: only the real error is printed). Keep the registration order, which EngineHost.cs:31 uses so the engine stops answering first.

### 44. Usual-folders service inherits XDG_STATE_HOME or OBM_HOME the shell lacks; obm never finds its engine

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/EngineService.cs`: 65, 66; `src/OpenBackupManager.Cli/SystemdService.cs`: 119; `src/OpenBackupManager.Cli/LaunchAgent.cs`: 55, 65; `src/OpenBackupManager.Daemon/Program.cs`: 42; `src/OpenBackupManager.Cli/Service.cs`: 25, 26; `src/OpenBackupManager.Cli/Status.cs`: 38; `src/OpenBackupManager.Cli/Data.cs`: 80, 95
- **Status:** CONFIRMED
- **What's wrong:** For the usual folders, Variables (EngineService.cs:65-66) sets Environment= only for XDG variables the installing shell has, never OBM_HOME, so the service inherits any the shell lacks from the user manager's environment. obmd then keeps its lock, socket, logs and crash reports where obm in that shell doesn't look.
- **Failure scenario:** The user runs obm service install over SSH, whose shell lacks the XDG_STATE_HOME set in `~/.config/environment.d/*.conf`, and it waits 32 s, then says 'the service didn't start the engine. systemctl --user status openbackupmanager.service might say why' (exit 1), as shown against a real systemd 255. Yet systemd has started the engine with `XDG_STATE_HOME=~/.var/state`; obm status says 'the engine isn't running, though it's installed as a service' (exit 1), and reinstalling fails the same way.
- **Suggested fix:** For the usual folders, pin each folder variable, OBM_HOME and the XDG ones, in the unit: Environment=NAME=value if the shell sets it, else UnsetEnvironment=NAME. Do the same in the LaunchAgent's EnvironmentVariables, with empty strings for unset ones, and test a variable only systemd has.

### 45. Protocol-changing updates make obm update exit 1, wrongly saying the engine didn't come back

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Update.cs`: 239, 246, 254
- **Status:** CONFIRMED
- **What's wrong:** The obm running the update is the old version, so the restarted engine refuses its hellos with -32000, which EngineClient.cs:174 turns into an EngineException with Problem AnotherVersion. RestartedAsync's wait loop treats every EngineException as 'not back yet' (Update.cs:246), so it waits out 30 seconds and throws 'didn't come back'.
- **Failure scenario:** After obm service install on Linux or macOS, a user runs obm update to a release whose EngineProtocol.Version went up, as engine.md plans once releases are in use. The service restarts the engine into the new version within about 10 seconds, yet obm prints 'obm: the engine didn't come back within 30 seconds.' and exits 1, pointing at a healthy service. Since the old obm is what misreports, a later fix can't help anyone updating from this version.
- **Suggested fix:** In the wait loop, catch EngineException { Problem: EngineProblem.AnotherVersion } first: only the restarted engine can refuse this obm, so report it running the version just installed and return 0. A ServiceTests case with another protocol's build in the tarball would cover it.

### 46. A cut-off download or full temp folder makes obm update print an unhandled exception

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Releases.cs`: 71, 74, 78, 81, 86; `src/OpenBackupManager.Cli/Update.cs`: 87, 93
- **Status:** CONFIRMED
- **What's wrong:** DownloadAsync converts only HttpRequestException to UpdateException, so an IOException from reading the body, writing the temp file or File.Create escapes RunAsync's catch, and System.CommandLine prints 'Unhandled exception' and a stack trace. If TMPDIR doesn't exist, the finally's File.Delete throws DirectoryNotFoundException, which would replace the message even once DownloadAsync is fixed.
- **Failure scenario:** Wi-Fi or a VPN drops partway through the 16 MB tarball. After 'Updating from 0.2.0 to 0.3.0', obm prints 'Unhandled exception: System.Net.Http.HttpIOException: The response ended prematurely, with at least 10698314 additional bytes expected' and a stack trace, and exits 1, instead of 'obm: couldn't download OpenBackupManager-0.3.0-linux-x64.tar.gz, so nothing was changed'. obm and obmd are untouched, but it reads as obm crashing partway through replacing itself.
- **Suggested fix:** In DownloadAsync, catch IOException and UnauthorizedAccessException too, as the rest of Update.cs does, and throw the same 'couldn't download ..., so nothing was changed' UpdateException. Stop the finally's File.Delete (Update.cs:87) from throwing, such as by deleting only if the file exists.

### 47. A download whose connection dies silently hangs obm update and the app's update checks

- **Rating:** low
- **Area:** Command line (obm); Windows app
- **Where:** `src/OpenBackupManager.Cli/Releases.cs`: 20, 21, 71, 78; `src/OpenBackupManager.App.Windows/Updater.cs`: 173, 186, 198; `src/OpenBackupManager.ViewModels/AboutViewModel.cs`: 60
- **Status:** CONFIRMED
- **What's wrong:** obm update's Releases.DownloadAsync and the app's Updater.Check, holding _checking, wait on download reads with only the caller's token or _stop, no timeout and no keepalive, so a half-open connection whose reset was lost never returns. obm update hangs, and the app's 6-hourly, About and config-change checks queue on _checking.
- **Failure scenario:** The app starts downloading an update at a 6-hourly check, and the laptop's lid closes mid-download, so the server gives up and its reset never arrives. After waking, the read waits until the app is quit or restarted: this copy never checks again, About says Checking… with its button greyed out, and Quit installs nothing.
- **Suggested fix:** Give both downloads an idle timeout: a token, linked to the caller's or _stop, cancelled after about a minute without bytes. Report it as failed: 'couldn't download ..., so nothing was changed' in obm, and a failed check releasing _checking in the app.

### 48. Unwritable unit or plist folders make obm service install, uninstall and data remove throw

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/SystemdService.cs`: 64, 65, 81; `src/OpenBackupManager.Cli/LaunchAgent.cs`: 38, 40, 41, 48; `src/OpenBackupManager.Cli/Service.cs`: 56, 75; `src/OpenBackupManager.Cli/Data.cs`: 65
- **Status:** CONFIRMED
- **What's wrong:** SystemdService's and LaunchAgent's Install and Uninstall call Directory.CreateDirectory, File.WriteAllText and File.Delete unguarded, and their callers, Service.InstallAsync, Service.Uninstall and Data.RemoveAsync, don't catch IOException or UnauthorizedAccessException either. So those reach System.CommandLine, which prints 'Unhandled exception:' with a stack trace and exits 1, where obm elsewhere gives a one-line reason.
- **Failure scenario:** On Linux, `~/.config` belongs to root because a tool run with sudo -E created it. obm service install first stops an engine started by hand (finding 17), then prints "Unhandled exception: System.UnauthorizedAccessException: Access to the path '/home/u/.config/systemd' is denied." with a stack trace and exits 1, leaving no engine running.
- **Suggested fix:** In SystemdService's and LaunchAgent's Install and Uninstall, catch IOException and UnauthorizedAccessException, like UpdateWatcher.RemoveService, and rethrow a ServiceException naming the path and saying to check its owner, so obm prints one line. Checking the folder is writable before TakeOverAsync keeps a hand-started engine running (finding 17).

### 49. obm data remove exits 1 whenever the emptied home folder itself can't be removed

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Data.cs`: 90, 92, 122, 124
- **Status:** CONFIRMED
- **What's wrong:** RemoveFromHome ends by deleting the home once it's empty (Data.cs:122-124), and Data.cs:90-92 reports any IOException or UnauthorizedAccessException from that as "not everything could be removed" with exit 1, though the secrets and everything the programs made are gone. Every rerun fails the same way, so the command can't succeed.
- **Failure scenario:** On a Linux server the admin runs mkdir /srv/obm && chown backup: /srv/obm, and the backup user, with OBM_HOME=/srv/obm, runs obm data remove --yes, which stops the engine and removes the secrets and every file. It then prints "obm: not everything could be removed: Access to the path '/srv/obm' is denied." and exits 1, the same on every rerun, so a script that runs it before uninstalling stops there.
- **Suggested fix:** Treat removing the emptied home as best effort: catch IOException and UnauthorizedAccessException around the final Directory.Delete(home), and still report success, perhaps noting that the empty folder was left.

### 50. obm update hides why TLS failed, such as a wrong clock or untrusted CA

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Releases.cs`: 55, 88
- **Status:** CONFIRMED
- **What's wrong:** Both catch blocks (Releases.cs:55 and :88) append HttpRequestException.Message, which for any TLS failure is .NET's "The SSL connection could not be established, see inner exception." The inner AuthenticationException naming the reason (such as NotTimeValid or UntrustedRoot) is dropped, and obm has no log or verbose option to show it.
- **Failure scenario:** On a Raspberry Pi whose clock is months behind (it has no real-time clock and NTP is blocked), or on a host behind a TLS-inspecting proxy whose CA isn't in its trust store, obm update and obm update --check print "obm: couldn't reach GitHub to check for updates. The SSL connection could not be established, see inner exception." and exit 1. Nothing obm shows names the clock or the proxy's CA, so the user is left checking connectivity.
- **Suggested fix:** When the HttpRequestException's HttpRequestError is SecureConnectionError, append its InnerException's message, such as "...errors in the certificate chain: NotTimeValid", instead of e.Message. Don't use e.GetBaseException().Message for every error, as that would drop the host and port that refused-connection and DNS messages show today.

### 51. On Linux, obm status says to set OBM_SECRET_STORE, which never reaches the service's engine

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Status.cs`: 59, 96; `src/OpenBackupManager.Cli/SystemdService.cs`: 119; `src/OpenBackupManager.Cli/EngineService.cs`: 66; `src/OpenBackupManager.Cli/Service.cs`: 23
- **Status:** CONFIRMED
- **What's wrong:** On Linux with no store chosen, Status.Secrets (Status.cs:96) says 'Choose one with secret-store in config.toml or OBM_SECRET_STORE', even when the service started the engine. But obm service install's unit passes only the XDG_ folders, so a variable exported in the user's shell never reaches that engine, and nothing says why.
- **Failure scenario:** After obm service install and loginctl enable-linger on Linux, as command-line.md says, obm status prints 'Started by: its service, from obm service install' and 'Secrets: no store chosen yet. Choose one with secret-store in config.toml or OBM_SECRET_STORE: secret-service or file'. With no config.toml, the user adds export OBM_SECRET_STORE=file to `~/.bashrc`, but after systemctl --user restart and reinstalling, obm status still says no store is chosen, while obm config validate --env reports no problems, because the user manager never reads `~/.bashrc`.
- **Suggested fix:** Have Status.Secrets suggest obm secrets migrate `<store>`, which writes secret-store into config.toml and works however the engine started. Or say OBM_SECRET_STORE must be set where the engine runs, and have obm status and service install warn when obm's environment has one the engine doesn't reflect.

### 52. obm data remove only unlinks a linked folder, leaving its contents, yet reports success

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Data.cs`: 40, 82, 95, 103, 114; `src/OpenBackupManager.Daemon/SecretsRemoval.cs`: 25
- **Status:** CONFIRMED
- **What's wrong:** Without --home, Data.RemoveAsync calls Directory.Delete(folder, recursive: true) on each usual folder, including one that's a symlink or junction, but .NET then removes only the link itself. So the target keeps everything, and would keep secrets.json too, as SecretsRemoval leaves it to this delete, while obm reports success.
- **Failure scenario:** On Linux, `~/.local/state/openbackupmanager` is a symlink to a folder on another disk, and the engine has run through it. obm data remove --yes removes only the link, prints 'Removed all of Open Backup Manager's data' and exits 0, but the target still holds `logs/engine-*.log`, crashes, engine.lock and run. Nothing saves a secret yet, but once secrets are saved with secret-store = "file", their plain-text secrets.json will stay there too, where backups of that disk keep copying it.
- **Suggested fix:** Delete what the app made in a linked folder, then the link, as RemoveFromHome does for a linked --home, but leave a Linux or macOS config folder's target alone, for dotfiles. If anything listed stays, name it and exit non-zero instead of 'Removed all'.

### 53. macOS home paths differing in case get separate LaunchAgents, undoing uninstall and data remove

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/EngineService.cs`: 38; `src/OpenBackupManager.Cli/LaunchAgent.cs`: 16, 71; `src/OpenBackupManager.Core/AppHome.cs`: 55; `src/OpenBackupManager.Cli/Service.cs`: 96, 132; `src/OpenBackupManager.Cli/Data.cs`: 131; `tests/OpenBackupManager.Cli.Tests/ServiceDefinitionTests.cs`: 130
- **Status:** PLAUSIBLE
- **What's wrong:** EngineService.HomeKey (EngineService.cs:38) hashes the home exactly as typed, though on macOS everything else about a home ignores case, and AppHome.InstanceKey upper-cases it on purpose. So two spellings of one folder get two LaunchAgents, and install, uninstall and data remove each act only on the one for the spelling given.
- **Failure scenario:** On macOS, a user runs `obm service install --home ~/Backups/obm`, and later the same with `~/backups/obm`, which adds a second LaunchAgent whose obmd exits 1 ('obmd: the engine is already running') every 10 s forever, though obm exits 0. Later, `obm service uninstall --home ~/Backups/obm` says the engine has stopped, but the other agent restarts it within 10 s. `obm data remove --home ~/Backups/obm --yes` reports 'Removed all of Open Backup Manager's data', then the other agent re-creates the home.
- **Suggested fix:** Hash HomeKey from AppHome's normalised path (upper-cased on macOS and Windows, unchanged on Linux), such as by exposing AppHome.Normalize to EngineService, and make EachHome_HasItsOwn expect /srv/a and /srv/A to match on macOS, checking LaunchAgent.Label. Nothing has been released, so the label change needs no migration.

### 54. Uninstalling a failed service leaves it listed as failed and the user's systemd degraded

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/SystemdService.cs`: 73, 78, 84
- **Status:** CONFIRMED
- **What's wrong:** SystemdService.Uninstall (SystemdService.cs:73), used by obm service uninstall and obm data remove, runs `systemctl --user disable --now`, deletes the unit file and reloads, but never runs `systemctl --user reset-failed`. Stopping a failed unit is a no-op, and systemd doesn't unload it by itself, so it stays loaded as 'not-found failed'.
- **Failure scenario:** config.toml has an error, so obmd exits 1 at each start, systemd gives up after 5 tries, and `obm service install` says 'the service didn't start the engine'. The user then runs `obm service uninstall`, which exits 0 and says the engine has stopped and no longer starts, but `systemctl --user --failed` still lists `openbackupmanager-<key>.service` as 'not-found failed', and `systemctl --user is-system-running` prints degraded and exits 1. That lasts until `systemctl --user reset-failed` or the user manager restarts.
- **Suggested fix:** In Uninstall, after `disable --now`, run `TryRun("systemctl", "--user", "reset-failed", Unit)`, ignoring its exit code (1 for a unit that isn't loaded). Running it before Install's restart too would stop systemd's start limit refusing an install retried within 10 seconds of the first failed start.

### 55. obm update and the Windows app update from any tag, not just admin-only `v*`

- **Rating:** low
- **Area:** Command line (obm)
- **Where:** `src/OpenBackupManager.Cli/Releases.cs`: 60, 63; `src/OpenBackupManager.Cli/Update.cs`: 30, 32; `src/OpenBackupManager.App.Windows/Updater.cs`: 118, 190, 198
- **Status:** CONFIRMED
- **What's wrong:** Releases.NewestAsync (Releases.cs:60) takes the highest non-draft release whose tag parses as a version, even a bare 9.9.9; Velopack's GithubSource (Updater.cs:118) ignores tags. Each checks only a hash from that release, so anything that can create a release under a `non-v*` tag can ship updates to every tarball and Windows install.
- **Failure scenario:** A hijacked microsoft/component-detection release in the dependency-graph job (finding 23) uses that job's contents: write token to publish crafted tarballs, Windows feeds and nupkgs under tag 99.0.0, outside the `v*` ruleset. On tarball installs, obm update takes 99.0.0, passes the hash check and replaces obm and obmd, as reproduced with a fake API. Each Windows app's next automatic check downloads the nupkg, writes its Update.exe over the real one and installs it on Restart now, Quit or the next start.
- **Suggested fix:** Limit Releases.NewestAsync to tags in the vX.Y.Z[-label.N] form Get-Version.ps1 enforces. For the Windows app, as Velopack's GithubRelease has no tag, widen the Version tags ruleset so only admins can create any tag, covering both updaters, or give Updater a GitBase source filtering on tag_name.

### 56. With two homes per exe, notification clicks reach or start the other home's copy

- **Rating:** low
- **Area:** Platform layers
- **Where:** `src/OpenBackupManager.Platform.Windows/WindowsNotifier.cs`: 17, 18, 21, 31, 33, 84, 88, 91, 92; `src/OpenBackupManager.Platform.Windows/NotificationActivator.cs`: 19; `src/OpenBackupManager.App.Windows/Program.cs`: 157; `src/OpenBackupManager.ViewModels/AppNotifications.cs`: 51
- **Status:** PLAUSIBLE
- **What's wrong:** WindowsNotifier's AUMID and activator CLSID (WindowsNotifier.cs:17-21) ignore the home, so one exe's homes share one COM class, and each start rewrites LocalServer32 with its --home. Toasts carry only the action, so a click goes to whichever copy COM picks or, with none running, starts the last-started home's copy.
- **Failure scenario:** The installed app runs for its default home (A) and, via a shortcut, for D:\Work (B), which started last, so LocalServer32 ends with --home "D:\Work". A bad edit to A's config.toml shows 'Settings file has a problem' with Show file. If A has been quit when that's clicked in the notification centre, B gets the click and Explorer selects D:\Work\config.toml, not the broken file; with neither running, Windows starts B and its engine instead.
- **Suggested fix:** Give each non-default home its own AUMID (such as AppId plus AppHome.InstanceKey(home)), activator CLSID and LocalServer32 command, and have Unregister remove all of this exe's registrations. Or put the home's key in each toast and forward other homes' clicks to their copy.

### 57. A failed or cancelled download of a newer version silently deletes the waiting update

- **Rating:** low
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/Updater.cs`: 75, 82, 96, 198; `src/OpenBackupManager.App.Windows/AppActions.cs`: 64, 75, 82, 91, 119
- **Status:** CONFIRMED
- **What's wrong:** Updater.Check (Updater.cs:198) downloads a newer release even while one is waiting, and Velopack 1.2.161's DownloadUpdatesAsync runs CleanPackagesExcept(target) in a finally. So a download that fails, or that Updater.Dispose cancels on Quit, Restart to update or sign-out, deletes the waiting .nupkg: that update isn't installed and nothing says so.
- **Failure scenario:** With 0.0.2 downloaded and its Update available notification showing, the next 6-hourly check starts downloading 0.0.3 and the connection drops part way. Check logs 'Couldn't check for updates', but 0.0.2's package is gone. Restart now on the 0.0.2 notification returns at AppActions.cs:82 and does nothing, Quit starts no Update.exe and the next start has nothing to apply, though a later check that succeeds downloads 0.0.3 and notifies again.
- **Suggested fix:** Skip Updater.Check's download while UpdatePendingRestart isn't null, so the waiting update installs first and the next start finds the newer one. Or subclass UpdateManager so CleanPackagesExcept does nothing unless the target package exists, which kept 0.0.2 in every case tried; report it to Velopack.

### 58. If the downloaded package is unreadable, each start says Update available but nothing installs

- **Rating:** low
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/Updater.cs`: 198, 207, 211; `src/OpenBackupManager.ViewModels/AboutViewModel.cs`: 64, 79; `src/OpenBackupManager.App.Windows/AppActions.cs`: 82
- **Status:** CONFIRMED
- **What's wrong:** Updater.Check (Updater.cs:198-211) counts an update as ready whenever DownloadUpdatesAsync returns, but Velopack returns without checking anything when the target's .nupkg already exists. UpdatePendingRestart, which UpdateReady, ReadyVersion, Restart now, Quit and the install at start all rely on, skips a package it can't open, so the announced update never installs.
- **Failure scenario:** A few seconds after a download finishes, Windows crashes or the power goes; Velopack had renamed the download to .nupkg without flushing it, so NTFS keeps its name and size but its end (the zip's central directory) reads as zeros. Until a newer release downloads, each start then shows 'Update available - Version X is ready to install', but Restart now, Quit and the next start install nothing. About shows a blank version: 'Version  is ready to install'.
- **Suggested fix:** After DownloadUpdatesAsync, check that manager.UpdatePendingRestart is the target version; if not, delete the target's file (VelopackLocator.Current.PackagesDir plus TargetFullRelease.FileName) and return Failed without notifying, so the next check downloads it again. Upgrading Velopack doesn't help, as 1.3.39 has the same early return.

### 59. Uninstalling while Windows' notification platform is unavailable leaves start-at-login, PATH and notification entries behind

- **Rating:** low
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/Program.cs`: 62, 64, 65, 66; `src/OpenBackupManager.Platform.Windows/WindowsNotifier.cs`: 51
- **Status:** PLAUSIBLE
- **What's wrong:** OnBeforeUninstallFastCallback (Program.cs:62) runs WindowsNotifier.Unregister, RunKey.RemoveFor and UserPath.Remove in one lambda with no try, and Unregister calls History.Clear before deleting its registry keys, so a throw skips every later step. Velopack 1.2.161 catches it and exits -1, and Update.exe's uninstall ignores that, deletes the install folder and reports success.
- **Failure scenario:** The user uninstalls from Settings > Apps while Windows' notification platform is unavailable, a state seen in other apps' crash reports from History.Clear, said to last until a reboot or a WpnUserService reset. History.Clear(AppId) then throws 'The notification platform is unavailable.' (0x803E0105), yet Update.exe deletes %LocalAppData%\OpenBackupManager.App, shows uninstall complete and exits 0. Left behind: the HKCU Run value, still in Startup apps, pointing at a deleted exe; ...\OpenBackupManager.App\current\bin in the user's PATH; and the AppUserModelId and CLSID keys.
- **Suggested fix:** Run each clean-up step in its own try/catch and carry on, as the Windows Community Toolkit's ToastNotificationManagerCompat.Uninstall does, putting RunKey.RemoveFor and UserPath.Remove first, since they matter most once the folder is gone. Have WindowsNotifier.Unregister delete its registry keys whether or not History.Clear succeeds.

### 60. Quitting while the app is still starting the engine leaves that engine running

- **Rating:** low
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/EngineConnection.cs`: 42, 108, 135, 165, 169; `src/OpenBackupManager.App.Windows/AppActions.cs`: 63
- **Status:** CONFIRMED
- **What's wrong:** QuitAsync only stops _engine, which is set once a connection is made. A Quit after ConnectOrStartAsync launches obmd but before it answers cancels the start loop, StopConnectingAsync swallows that, 'using var started' only closes the Process handle, and obmd, started with --started-by app, keeps running after the app exits.
- **Failure scenario:** The tray app has just started obmd with --started-by app and shows Starting, as at launch or a cold sign-in. With no update waiting, the user picks Quit from the tray menu, and the app exits within milliseconds. obmd keeps running, and 'obm status' says 'Started by: the app, which stops it when you quit', until a later copy of the app connects and quits, or the session ends.
- **Suggested fix:** Keep the Process that StartEngine returns where QuitAsync can reach it. When Quit finds no connection but a start in progress, finish connecting to that engine with a short timeout and stop it as usual, or end the process if it never answers.

### 61. Opening the app just after sign-in can do nothing; the starting copy drops it

- **Rating:** low
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/Program.cs`: 110; `src/OpenBackupManager.App.Windows/App.xaml.cs`: 61, 68
- **Status:** PLAUSIBLE
- **What's wrong:** The running copy registers its AppInstance key at Program.cs:110 but attaches its Activated handler only at the end of OnLaunched (App.xaml.cs:68). In its Windows App SDK (2.6.5), the watcher, armed from registration, raises Activated to whatever handlers exist, without replay, so a launch redirected in between is lost.
- **Failure scenario:** With start-at-login on, the app starts at sign-in with --at-login, skipping the flyout, and registers its key before building its host and starting WinUI. If the user opens the app from the Start menu meanwhile, the running copy's watcher consumes the request with no handler attached, and the new copy exits 0 with no message. The running copy stays in the tray without opening its flyout, so the launch seems to do nothing until the user launches again.
- **Suggested fix:** Attach the Activated handler in Main straight after FindOrRegisterForKey. Until OnLaunched has the tray, the handler sets a flag under a lock, which OnLaunched checks to open the flyout even for an --at-login start; after that, the handler posts ShowFlyout to the UI context.

### 62. A bad OBM_ environment variable stops the Windows app, blaming an error-free config.toml

- **Rating:** low
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/Program.cs`: 120; `src/OpenBackupManager.ViewModels/Strings.resx`: 172
- **Status:** CONFIRMED
- **What's wrong:** By design, ConfigWatcher refuses to start on a bad value in a setting's environment variable as well as on an error in config.toml, and the ConfigException's Problem.EnvVar says which. Program.cs:118-121 ignores that and always shows Launch_ConfigMistake, which says the settings file must be fixed and gives its path.
- **Failure scenario:** A Windows user sets OBM_UPDATES_AUTOMATIC=no in their account's environment. Launching the app shows 'Open Backup Manager won't start until its settings file is fixed.', then 'Environment variable OBM_UPDATES_AUTOMATIC: updates.automatic should be true or false', then %LocalAppData%\OpenBackupManager\config.toml, the untouched default with no errors. Writing automatic = false into the file, as the headline suggests, still gives the same message at every launch, because the variable wins over the file; only fixing or removing it lets the app start.
- **Suggested fix:** When e.Problem.EnvVar is set, show a separate string saying the app won't start until that environment variable is fixed or removed, without the settings file's path, as obmd already does.

### 63. An update replaces the user's Task Scheduler engine with the app's own, or none

- **Rating:** low
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/EngineConnection.cs`: 51, 79, 193; `src/OpenBackupManager.App.Windows/AppActions.cs`: 63, 64, 90; `src/OpenBackupManager.App.Windows/Updater.cs`: 75, 82; `src/OpenBackupManager.App.Windows/Program.cs`: 61, 68
- **Status:** PLAUSIBLE
- **What's wrong:** QuitAsync(updating: true) and the before-update hook's StopFor stop the engine however it was started, as windows-app.md:23 decided. But nothing records it wasn't the app's, so the updated app starts its own obmd --started-by app, which Quit stops, and a Quit-install starts none, though command-line.md:44 says a user-started engine keeps running.
- **Failure scenario:** A user's Task Scheduler task runs obmd.exe at log on, as command-line.md:46 suggests (obm status: 'Started by: you or another program'), and the app connects to it and downloads an update. They choose Restart now: QuitAsync(updating: true) stops their engine, the new version starts obmd --started-by app, and their later Quit stops that one. obm status then says the engine isn't running until they next sign in; had they quit with the update waiting, none would run until then.
- **Suggested fix:** When QuitAsync or StopFor stops an engine the app didn't start, note it in the state folder, so the updated app starts obmd --started-by other and deletes the note. For a Quit-install, which Velopack's after-update hook can't handle, restart a copy that only does this.

### 64. A crash on another thread can mark its report seen, so it's never shown

- **Rating:** low
- **Area:** Windows app
- **Where:** `src/OpenBackupManager.App.Windows/App.xaml.cs`: 44, 47, 49; `src/OpenBackupManager.App.Windows/EngineConnection.cs`: 138; `src/OpenBackupManager.Core/CrashReports.cs`: 147, 154; `src/OpenBackupManager.App.Windows/CrashCapture.cs`: 88; `tests/OpenBackupManager.App.Windows.UITests/CrashTests.cs`: 20
- **Status:** CONFIRMED
- **What's wrong:** CrashReports.TakeNew marks the newest unseen report seen, whoever wrote it, and its callers in App.xaml.cs don't check whether this copy is crashing. While CrashCapture.Capture handles an error on another thread, the UI thread keeps running, so a queued Connected callback can mark the report just written as seen.
- **Failure scenario:** On CI (run 42337559135), the test's Crash > On another thread threw 11 ms after 'Connected to the engine', and the dying copy's UI thread ran the queued Connected callback, writing its own report's name to crashes/shown. The next copy opened the flyout instead of the Closed unexpectedly window, so ACrash_IsReportedAtTheNextStartOnly("On another thread") timed out at CrashTests.cs:20; it passed on re-run. Release builds hide such crashes the same way, leaving the report only in obm crashes list, without (new).
- **Suggested fix:** Have CrashCapture.Capture set a volatile crashing flag on CrashReports before it writes, and have TakeNew check it after listing and return null, marking nothing, when it's set; that covers both App.xaml.cs callers. Leave the UI test's timing alone, since it's what exposes the race.

### 65. Renovate merges new dependency releases into main itself, unreviewed, with no minimum release age

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `.github/renovate.json5`: 4, 5, 6
- **Status:** CONFIRMED
- **What's wrong:** renovate.json5 enables :automergeMinor (minor, patch and pin, 0.x included) and :automergeDigest with no minimumReleaseAge, which config:best-practices sets only for npm, so these updates to NuGet, vpk, the .NET SDK, images and actions merge once CI passes on Renovate's branch. Main refuses branch automerge, so Renovate merges its own PR, unreviewed.
- **Failure scenario:** A hijacked Velopack patch release that still passes the tests is published (Renovate groups Velopack with vpk, which builds Setup.exe). Renovate's branch CI runs the package jobs with finding 22's signing and push rights, then Renovate merges its own PR unreviewed and main's run pushes the attested :edge image the guide offers. The benign Tomlyn 2.11.0 took this path on 2026-11-08: on NuGet at 08:17Z, merged by renovate[bot] in [pull request 52](https://github.com/AmyJeanes/OpenBackupManager/pull/52) at 18:09Z, in the attested edge image at 18:23Z.
- **Suggested fix:** Add a packageRule with a 3-day minimumReleaseAge for the nuget and dotnet-version datasources and Docker Hub images (not MCR or Red Hat images: no timestamps, so they'd wait forever). Automerge action digests only for `actions/*` and `github/*`, as no release age catches a force-pushed tag.

### 66. The release image is built and pushed by an unpinned, unverified BuildKit image

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `.github/workflows/ci.yml`: 654, 655, 674, 679, 683, 685
- **Status:** CONFIRMED
- **What's wrong:** setup-buildx-action (ci.yml:654) gets no driver-opts, so the release image's privileged builder is moby/buildkit:buildx-stable-1, a floating tag, and buildx v0.38.1 skips its signature check, as BUILDX_DEFAULT_POLICY is unset. That builder gets the raw ghcr.io GITHUB_TOKEN and reports the digest actions/attest signs, while CI's other images are pinned by digest.
- **Failure scenario:** Someone with push access to moby/buildkit on Docker Hub repoints buildx-stable-1 at a tampered build. The next main or tag run pulls it unchecked, and the builder swaps /app/obmd in the image's layers, or pushes extra tags with the token it was handed. buildx writes the tampered digest to metadata.json, the Attest step signs it with the workflow's identity, and `gh attestation verify oci://ghcr.io/amyjeanes/openbackupmanager:<version> --repo AmyJeanes/OpenBackupManager` passes, which checking-downloads.md tells users means the image is safe.
- **Suggested fix:** Pin the builder with `with: driver-opts: image=moby/buildkit:vX.Y.Z@sha256:<digest>` on setup-buildx-action. Renovate's existing regex manager already matches that form, so it stays pinned and updated like the other images.

### 67. Dependabot can't alert on the tray app's Windows-only packages, which the dependency graph misses

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `.github/workflows/dependency-graph.yml`: 16, 27, 28, 30; `Directory.Solution.targets`: 4, 6
- **Status:** CONFIRMED
- **What's wrong:** The submit job restores on ubuntu-latest (dependency-graph.yml:16), where Directory.Solution.targets drops the .Windows projects, so they get no project.assets.json and Component Detection submits none of their packages. GitHub's graph has the direct ones only as unversioned '>= 0' entries, which can't raise alerts, and transitive ones like Microsoft.Web.WebView2 not at all.
- **Failure scenario:** An advisory is published for Microsoft.Web.WebView2 1.0.3719.77, whose WebView2Loader.dll is in the tray app's win-x64 output, and GitHub raises no Dependabot alert, so Renovate, whose security pull requests come only from those alerts, opens none. Renovate's routine updates never move this transitive package, and the only sign is an NU190x warning in the Windows CI logs, which Directory.Build.props keeps from failing the build. Releases keep shipping the vulnerable version until Microsoft.WindowsAppSDK.WinUI happens to move to a fixed one.
- **Suggested fix:** Run the submit job on windows-latest, as the format and CodeQL jobs do, or also restore the .Windows projects with -p:EnableWindowsTargeting=true, which was checked to add all 27 missing packages. Before going back to GitHub's built-in submission, note it runs on Linux or macOS.

### 68. The release job can't be re-run once its first run uploads to the draft

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `.github/workflows/ci.yml`: 732, 735, 742, 747
- **Status:** CONFIRMED
- **What's wrong:** On a re-run, `vpk upload github --merge` (ci.yml:735) stops if the draft has `releases.<channel>.json`, else fails on the first file already there, as does `gh release upload` without --clobber. So re-runs fail until someone deletes the draft or its assets, though windows-app.md:126 says the job can be re-run before publishing.
- **Failure scenario:** For tag v0.1.0, the first run's win-x64 upload creates the draft with its nupkg, Setup.exe and releases.win-x64.json, then the win-arm64 upload gets a 502 on all 3 tries and the job fails. 'Re-run failed jobs' runs the whole job again, and on every re-run its Windows step exits 255 at once with "There is already a remote asset named 'releases.win-x64.json', and merging release files on GitHub is not supported."
- **Suggested fix:** Before the Windows upload, delete the tag's release only if it's still a draft, using `gh release delete "$TAG" --yes` without --cleanup-tag so the tag stays; any notes in it are lost. Otherwise, document in windows-app.md that a re-run needs the draft deleted by hand.

### 69. A failed lookup of the latest release silently leaves the image's latest tag behind

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `.github/workflows/release-published.yml`: 24
- **Status:** CONFIRMED
- **What's wrong:** release-published.yml:24 compares what `gh api .../releases/latest` prints with the tag in an `if` condition, so bash -e never sees gh fail. On any HTTP or network error, gh prints its raw JSON error body or nothing, so the step logs 'isn't the latest release' and exits 0 without moving latest.
- **Failure scenario:** Stable v0.2.0 is published by hand, and the one releases/latest call gets a 5xx or network error. The job logs `gh: Server Error (HTTP 500)`, then 'v0.2.0 isn't the latest release, so the image's latest tag stays where it is', and shows green, with latest still on v0.1.0. A NAS using the guide's compose file (`:latest`, updated with `docker compose pull`) stays on v0.1.0 until the next stable release's run succeeds, and nothing prompts the maintainer to re-run it.
- **Suggested fix:** Exit early for a prerelease, passing `github.event.release.prerelease` through env, as releases/latest is a 404 until a stable release exists. Set `latest=$(gh api "repos/$GITHUB_REPOSITORY/releases/latest" --jq .tag_name)` on its own line, so a failed call fails the step and can be re-run, and compare `$latest` with `$TAG`.

### 70. Slow or stalled rclone downloads fail first builds after 100 seconds without saying why

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `tools/RcloneFetch/Program.cs`: 48, 49, 52, 63, 70; `src/OpenBackupManager.Rclone/OpenBackupManager.Rclone.csproj`: 35
- **Status:** CONFIRMED
- **What's wrong:** RcloneFetch (Program.cs:48-52) downloads SHA256SUMS and the `~31.6` MB rclone zip with a plain `new HttpClient()`, whose default 100-second Timeout covers each whole body. The resulting TaskCanceledException isn't in the catch filter (Program.cs:63), and System.CommandLine prints nothing for it, so RcloneFetch exits 1 silently and the build shows only MSB3073.
- **Failure scenario:** A contributor runs their first build on a link to GitHub's release downloads slower than about 2.5 Mbit/s, or one that stalls. After about 100 s the build fails with only `OpenBackupManager.Rclone.csproj(35,5): error MSB3073: The command "...RcloneFetch.dll ... --runtime linux-x64 --output "obj/rclone/v1.75.2/linux-x64/rclone"" exited with code 1.`, and no logger or verbosity says it was a timeout. Each retry downloads the whole zip again and fails the same way, so on a slow link they can never build.
- **Suggested fix:** Set the HttpClient's Timeout to Timeout.InfiniteTimeSpan and stream the body with a timeout that fires only when no data arrives for a while. When the command's own token wasn't cancelled, catch the timeout's OperationCanceledException and print `RcloneFetch: downloading <asset> timed out`.

### 71. Offline, restores that aren't no-ops fail with NU1900, though every package is cached

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `Directory.Build.props`: 5, 6
- **Status:** CONFIRMED
- **What's wrong:** Directory.Build.props:5-6 sets TreatWarningsAsErrors and exempts only NU1901-NU1904, so NuGet Audit's NU1900 (it couldn't get vulnerability data) becomes an error. Offline, once NuGet's cached service index is over 30 minutes old, every restore that isn't a no-op fails with it, though every package is in `~/.nuget/packages` and rclone is cached.
- **Failure scenario:** A contributor builds online, runs tools/Publish-App.ps1 -Configuration Debug as CONTRIBUTING suggests, goes offline, and over 30 minutes after their last online restore runs dotnet build or dotnet test. The projects the publish restored for linux-x64 restore again, and the run stops with 'error NU1900: Warning As Error: Error occurred while getting package vulnerability data: Unable to load the service index for source https://api.nuget.org/v3/index.json' and exit 1. Yet CONTRIBUTING and rclone-bundling.md say only the first build needs internet access.
- **Suggested fix:** Add NU1900 to WarningsNotAsErrors beside NU1901-NU1904, as Microsoft's NuGet Audit guidance does for local builds. Audit findings are already warnings, so CI loses no check.

### 72. The pre-commit hook leaves a by-path commit's unformatted file staged, blocking the next commit

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `.githooks/pre-commit`: 10, 18, 19, 26, 34, 38
- **Status:** CONFIRMED
- **What's wrong:** For `git commit <paths>`, git stages the files as they are, then runs .githooks/pre-commit against a temporary index, so its formatting, DocsGen and `git add` reach the commit and working tree but not the real index. The real index keeps the pre-hook text, a staged change undoing the hook's work.
- **Failure scenario:** With the hooks on, `git commit -m msg -- src/OpenBackupManager.Core/RunningAs.cs` commits a misformatted file formatted, but `git status` then shows MM and `git diff --cached` undoes the formatting. The next ordinary commit of an unrelated file is refused with 'these files have unstaged changes, so they can't be formatted safely: src/OpenBackupManager.Core/RunningAs.cs', naming a file already committed. A --no-verify commit, or one from a tool that skips hooks, silently puts the unformatted code back.
- **Suggested fix:** When GIT_INDEX_FILE isn't the real index or its .lock (a commit by path uses `next-index-<pid>.lock`), don't rewrite or re-stage anything. Only check, with `dotnet format ... --verify-no-changes` and DocsGen, and if a file would change, refuse, saying to `git add` it and commit without paths.

### 73. The commit-msg hook turns human co-authors at Anthropic, OpenAI or Cursor into Assisted-by trailers

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `.githooks/commit-msg`: 12, 14, 16
- **Status:** CONFIRMED
- **What's wrong:** The hook (.githooks/commit-msg:12-16) identifies AI tools by whole email domain, so it silently turns any Co-authored-by trailer with an `@anthropic.com`, `@openai.com` or `@cursor.com` address into an Assisted-by trailer. The person loses their GitHub co-author credit, and the commit falsely shows AI help in the project's record of AI use.
- **Failure scenario:** With the hooks enabled, as CONTRIBUTING.md:36 asks, a commit with `Co-authored-by: Jane Doe <jane.doe@anthropic.com>` and `Co-authored-by: Sam Lee <sam@openai.com>` is saved with `Assisted-by: Claude Code (Jane Doe)` and `Assisted-by: Codex` instead, so GitHub credits neither. A plain `git commit --amend --no-edit` does the same to such a trailer already on a commit, for example one from a review suggestion accepted on GitHub. The hook prints nothing, so none of this is visible at commit time.
- **Suggested fix:** Match the tools' own addresses instead of whole company domains: noreply@anthropic.com (Claude Code), noreply@openai.com (Codex) and cursoragent@cursor.com (Cursor). Leave every other Co-authored-by trailer unchanged.

### 74. The docs job's hash-locked install builds PyYAML with unpinned, unhashed setuptools and Cython

- **Rating:** low
- **Area:** Build, release and supply chain
- **Where:** `.github/workflows/ci.yml`: 200, 201, 205, 237, 241, 776; `docs/requirements.txt`: 190, 256
- **Status:** CONFIRMED
- **What's wrong:** The docs job runs Python 3.15 (ci.yml:200), for which PyYAML 6.0.3 has no wheel, so `pip install --require-hashes` builds it from source after installing unversioned `setuptools` and `Cython>=3.0`, build dependencies pip never hash-checks. Later runs reinstall the cached build after checking only the source archive's hash, not the wheel.
- **Failure scenario:** When the runner's Python 3.15 patch version changes, a docs run on main misses its pip cache and builds PyYAML with whatever setuptools and Cython PyPI then has. A malicious release can plant a .pth file that rewrites docs/site inside `zensical build`, after the git diff check, and deploy-docs publishes that as the guide users follow for downloads and `gh attestation verify`. A merely broken one fails the docs job, and so all-checks on every pull request.
- **Suggested fix:** Set python-version to '3.14', where the lock has wheels for all eleven packages, and add `--only-binary :all:` to the pip install, so a missing wheel fails the job instead of being built from source. Return to 3.15 once the lock has PyYAML's cp315 wheels.

### 75. ServiceTests.RemovingObmd_RemovesTheService fails now and then when a poll connects as the engine stops

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Cli.Tests/ServiceTests.cs`: 108, 150, 328, 329
- **Status:** CONFIRMED
- **What's wrong:** The loop at ServiceTests.cs:150 polls with the StatusAsync helper, which returns null only when connecting times out. If a poll connects as the engine stops after removing its service, the engine closes the connection unanswered, and EngineClient's EngineException escapes the loop, failing the test, though the removal worked.
- **Failure scenario:** With a reachable systemd user manager and OBM_TEST_PUBLISHED set, as CONTRIBUTING describes, someone runs RemovingObmd_RemovesTheService on its own. As the engine stops after deleting its unit, the test's first EngineClient call, slow in a fresh testhost, connects, and the test fails at 12 s with 'EngineException : The engine closed the connection' (7 of 10 runs). In CI's full Sandbox run, EngineClient is already warmed up, so it fails only if a poll lands within milliseconds of the stop.
- **Suggested fix:** In the loops at lines 108 and 150, not the helper, catch EngineException and IOException, as Update.RestartedAsync does, and keep polling. Or take the engine's ProcessId before deleting obmd, then wait for the unit file to go and that process to exit, without connecting.

### 76. Stale entries fail later DaemonTests and UpdateTests in TearDown, leaving engines and folders behind

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Cli.Tests/DaemonTests.cs`: 20, 25, 29, 217, 237, 255, 276, 412; `tests/OpenBackupManager.Cli.Tests/Tool.cs`: 159, 166; `tests/OpenBackupManager.Cli.Tests/UpdateTests.cs`: 17, 41, 43, 82
- **Status:** CONFIRMED
- **What's wrong:** DaemonTests' TearDown clears its lists only after both loops, so one failed Directory.Delete leaves stale entries, and UpdateTests never removes the obmd Tool Update_WithTheEngineStartedByHand_SaysToRestartIt adds. With one NUnit instance per fixture, later TearDowns throw on the stale home or disposed Tool (Tool.cs:159) before their own clean-up.
- **Failure scenario:** On a Linux machine whose temp folder is mounted noexec, the four CopyOfObmd tests can't start their copied obmd, so their reserved homes are never created and TearDown throws DirectoryNotFoundException. Every later DaemonTests test then fails in TearDown with 'No process is associated with this object', so 4 real failures are reported as 14. After dotnet test exits, 6 obmd engines (about 58 MB each, with PPID 1) and 15 temp folders are left.
- **Suggested fix:** Make Tool.Dispose safe to call twice. In both TearDowns, dispose each Tool and delete each existing home in its own try, and in a finally clear the lists (UpdateTests' _running too) and run UpdateTests' FakeGitHub and folder clean-up.

### 77. Inside Assert.EnterMultipleScope, a program that doesn't exit hangs the run instead of failing it

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Cli.Tests/Tool.cs`: 128, 145, 147, 149, 153; `tests/OpenBackupManager.Cli.Tests/CrashesTests.cs`: 86, 102, 134, 203, 211, 245; `tests/OpenBackupManager.Cli.Tests/DaemonTests.cs`: 124, 226, 245, 263, 284; `tests/OpenBackupManager.Cli.Tests/ServiceTests.cs`: 44; `tests/OpenBackupManager.Cli.Tests/StatusTests.cs`: 71; `tests/OpenBackupManager.Cli.Tests/UpdateTests.cs`: 74; `.github/workflows/ci.yml`: 69, 82, 92
- **Status:** CONFIRMED
- **What's wrong:** Tool.WaitForExit (Tool.cs:147-153) relies on Assert.Fail throwing after its 30-second wait, but inside a multiple-assert scope NUnit 5's Assert.Fail only records the failure and returns, so the untimed _process.WaitForExit() follows. For the 14 calls inside Assert.EnterMultipleScope, a program that never exits blocks the test forever, and its TearDown never runs.
- **Failure scenario:** A regression stops UpdateWatcher noticing obmd being removed, and DaemonTests.RemovingObmd_StopsTheEngine, which exists to catch that, hangs dotnet test instead of failing after 30 seconds. Reproduced with UpdateWatcher checking hourly, the unmodified test ran until --blame-hang-timeout killed the testhost, leaving the copied obmd running. On CI, with no timeout-minutes or --blame-hang-timeout, the Test step runs to GitHub's 360-minute job limit, and the cancelled job skips 'Upload the test results' (if: failure()), so nothing names the hung test.
- **Suggested fix:** In WaitForExit, call Kill() before Assert.Fail so the untimed wait returns at once (the regression then failed in 30 s), and add a return or throw after WaitFor's Assert.Fail. Add timeout-minutes to the test job, or --blame-hang-timeout to dotnet test, to name a hung test.

### 78. Default Cli test runs SIGKILL obmd, leaving .NET debugger FIFOs and sockets in TMPDIR

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Cli.Tests/Tool.cs`: 157, 161, 166; `tests/OpenBackupManager.Cli.Tests/DaemonTests.cs`: 20, 103; `tests/OpenBackupManager.Cli.Tests/UpdateTests.cs`: 43; `tests/OpenBackupManager.Cli.Tests/StatusTests.cs`: 30, 49, 80, 104, 168, 179; `tests/OpenBackupManager.Cli.Tests/ConfigTests.cs`: 26, 45, 61; `tests/OpenBackupManager.Cli.Tests/CrashesTests.cs`: 219; `tests/OpenBackupManager.Cli.Tests/MigrateTests.cs`: 31; `tests/OpenBackupManager.Cli.Tests/ServiceTests.cs`: 74
- **Status:** CONFIRMED
- **What's wrong:** Tool.Dispose() calls Tool.Kill(), which ends obmd with Process.Kill() (Tool.cs:161), SIGKILL on Linux and macOS, so the default framework-dependent obmd never removes the `clr-debug-pipe-<pid>-<start>-in/-out` FIFOs and `dotnet-diagnostic-<pid>-<start>-socket` the .NET runtime made in TMPDIR, and nothing else does. Every engine killed this way leaves one set; CI's NativeAOT programs make none.
- **Failure scenario:** A developer on Linux runs `dotnet test` without OBM_TEST_PUBLISHED, as CONTRIBUTING describes, and the tests end 23 obmd processes with Tool.Dispose() or Kill(). Each run adds 69 zero-byte FIFOs and sockets to /tmp (reproduced: exactly 69 after 86 passing tests), which pile up until a reboot or tmp cleaning clears TMPDIR. CI doesn't see it, because it sets OBM_TEST_PUBLISHED and tests the NativeAOT programs.
- **Suggested fix:** Set DOTNET_EnableDiagnostics=0 in Tool.Start's start.Environment, so no program a test starts makes the pipes or socket, deliberate kills included (checked: the six classes still pass 86/86 and leave nothing). Alternatively, have Dispose stop obmd with SIGTERM or the stop request before falling back to Kill.

### 79. The rclone test creates rclone's config folder in the developer's real home

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Rclone.Tests/BundledRcloneTests.cs`: 10, 14
- **Status:** CONFIRMED
- **What's wrong:** FilePath_RunsThePinnedVersion (BundledRcloneTests.cs:10-14) runs the bundled `rclone version` with the inherited environment and no config of its own. When no config file exists and neither RCLONE_CONFIG nor --config is given, rclone's init() creates its default config folder for any command, so `dotnet test` leaves it in the developer's real config location.
- **Failure scenario:** A developer with no rclone config runs `dotnet test OpenBackupManager.slnx`, which CONTRIBUTING says is safe on their own computer, and is left with an empty folder: `~/.config/rclone` on Linux and macOS ($XDG_CONFIG_HOME/rclone if set), or %APPDATA%\rclone on Windows. In the review's container, /root/.config/rclone was created during setup's run of this test, and running just this test with an empty temp HOME left .config/rclone there. Developers with an rclone config are unaffected: rclone neither reads nor changes it for `version`.
- **Suggested fix:** Give rclone a config of its own: add `--config` with a path in a temp folder to the arguments, or set `start.Environment["RCLONE_CONFIG"]` to such a path. rclone then skips creating its folder, and `rclone version` neither reads nor creates that file.

### 80. OBM_ settings exported in a developer's shell make tests fail on correct code

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Cli.Tests/Tool.cs`: 83; `tests/OpenBackupManager.Cli.Tests/StatusTests.cs`: 164; `tests/OpenBackupManager.Cli.Tests/MigrateTests.cs`: 27; `tests/OpenBackupManager.Cli.Tests/UpdateTests.cs`: 126, 268; `tests/OpenBackupManager.Core.Tests/ConfigFileTests.cs`: 258, 277, 301, 336; `tests/OpenBackupManager.Core.Tests/ConfigWatcherTests.cs`: 19, 170; `tests/OpenBackupManager.App.Windows.Tests/StartAtLoginSettingTests.cs`: 21
- **Status:** CONFIRMED
- **What's wrong:** Tool.Start removes only OBM_HOME from obm's and obmd's environment (Tool.cs:83), and ConfigFileTests, ConfigWatcherTests and StartAtLoginSettingTests build ConfigFile with no environment, so it reads the process environment (ConfigFile.cs:31). Any exported `OBM_*` setting then wins over the file the test wrote, though the tests mean to see only what they set.
- **Failure scenario:** A Linux contributor exports OBM_SECRET_STORE=file, as obm status suggests since Linux has no default store, and runs dotnet test. Three of Core's config tests fail, and so does Cli's ToTheStoreInUse_SaysSo. CI sets none of these variables, so it stays green while the contributor hunts a bug that isn't there.
- **Suggested fix:** In Tool.Start, remove every inherited variable whose name starts with OBM_ (ignoring case) before applying the test's own. Wherever ConfigFileTests, ConfigWatcherTests and StartAtLoginSettingTests build a ConfigFile, pass an environment that answers nothing (EnvVars() or `_ => null`).

### 81. The fake-GitHub update tests fail behind an HTTP proxy unless no_proxy lists 127.0.0.1 itself

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Cli.Tests/FakeGitHub.cs`: 23, 38; `tests/OpenBackupManager.Cli.Tests/Tool.cs`: 76, 83; `tests/OpenBackupManager.Cli.Tests/UpdateTests.cs`: 285; `tests/OpenBackupManager.Cli.Tests/ServiceTests.cs`: 127
- **Status:** CONFIRMED
- **What's wrong:** FakeGitHub serves on `http://127.0.0.1:<port>` and sets only OBM_UPDATE_SOURCE; Tool.Start passes obm the rest of the run's environment. obm's HttpClient follows HttpClient.DefaultProxy, which sends that address through http_proxy, HTTP_PROXY or all_proxy unless no_proxy names 127.0.0.1 itself, as localhost, 127.0.0.0/8 or `*` don't cover it, so obm never reaches the fake.
- **Failure scenario:** A Linux contributor sets a manual proxy in GNOME Settings, so GNOME Terminal exports http_proxy=http://proxy.corp:8080/ and GNOME's default no_proxy=localhost,127.0.0.0/8,::1, then runs dotnet test as CONTRIBUTING invites. 17 of the 19 UpdateTests (16 of 18 as root) fail with 'obm: couldn't reach GitHub to check for updates. Response status code does not indicate success: 403 (Forbidden).', as does ServiceTests.ObmUpdate_WaitsForTheServiceToRestartTheEngine in a Sandbox run, though nothing is wrong with obm update.
- **Suggested fix:** Have Tool.Start set no_proxy and NO_PROXY to 127.0.0.1 for every program it starts (setting them in FakeGitHub.Environment fixes the 17 too), or remove http_proxy, HTTP_PROXY, all_proxy and ALL_PROXY. The entry must be 127.0.0.1 itself, as .NET doesn't match an address range or `*`.

### 82. Two tests use fixed paths in the shared temp folder, so parallel runs collide

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Cli.Tests/CrashesTests.cs`: 16, 163, 167, 175, 184; `src/OpenBackupManager.Cli/Crashes.cs`: 68; `tests/OpenBackupManager.Engine.Tests/ListenerTests.cs`: 33, 297
- **Status:** CONFIRMED
- **What's wrong:** In CrashesTests.Report_TooLongForTheLink_HasTheWholeReportInAFile, the fake clock (CrashesTests.cs:16) always names the report crash-20261104-091203-000-engine.txt, so obm saves its long copy to the same temp path every run (Crashes.cs:68), which the test reads then deletes. ListenerTests.AHomeTooLongForASocket_SaysWhy always uses &lt;temp>/obm-engine-<120 x> as its home (ListenerTests.cs:297), which its engine writes into and TearDown deletes.
- **Failure scenario:** When two worktrees or agents share a temp folder and run these tests at once, the crash report test failed 5 of 80 runs and the Listener test up to 8 of 80. On a shared Linux machine, another user's leftover copy makes them fail every time for everyone else. Where fs.protected_symlinks=0, another user's link made a passing Listener run delete a live engine's socket, after which obm status said 'the engine isn't running' while obmd ran.
- **Suggested fix:** Give the crash test's obm its own temp folder inside _home (TMPDIR, TMP and TEMP through Tool.Run's environment), read the copy there and drop the finally. Build the Listener test's long home from SetUp's GUID-named one (_home.State plus 120 x's), as DaemonTests does.

### 83. When InstallTests' offline check fails, the 0.0.1 feed stays in %TEMP% and CleanUp errors

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.App.Windows.UITests/InstallTests.cs`: 64, 129, 130, 131, 132
- **Status:** CONFIRMED
- **What's wrong:** To fake being offline, UpdatesAndUninstalls renames the feed to `<feed>-away` and back two lines later (InstallTests.cs:130-132), with no try/finally, and CleanUp's finally deletes only _feed. So any failure in the offline check leaves the renamed feed behind, and CleanUp throws DirectoryNotFoundException instead of cleaning up.
- **Failure scenario:** The offline check fails, for example after someone changes About's strings, and Assert.That in CheckForUpdates (line 288) throws before line 132, leaving %TEMP%\obm-feed-XXXX-away holding 0.0.1's Setup.exe and full package. Then CleanUp's Directory.Delete(_feed) (line 64) throws DirectoryNotFoundException, reported as "TearDown failed for test fixture ...InstallTests" next to the real failure, and the -away folder is never deleted. On CI's throwaway runners only the extra error matters, but on a developer's machine each failure leaves another copy in %TEMP%.
- **Suggested fix:** In CleanUp's finally, delete _feed and _feed + "-away" only if they exist, and skip an empty _feed, since CleanUp also runs when Install fails before creating it. Or move the feed back in a finally around lines 130-131.

### 84. Update_ReplacesThemWithTheNewest never checks that obm was replaced

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Cli.Tests/UpdateTests.cs`: 55, 72, 74; `src/OpenBackupManager.Cli/Update.cs`: 191
- **Status:** CONFIRMED
- **What's wrong:** The test is meant to show obm update replaces obm and obmd, but checks only obmd's modified time (UpdateTests.cs:55, :72). For obm it only runs obm --version (line 74), which the old obm passes too, as FakeGitHub.NewVersionOf ships obm with the same bytes, only dated a minute later.
- **Failure scenario:** Suppose a change to Update.Replace, such as a filter on the move loop at Update.cs:191, unpacks obm but never moves it over the old one: obm still prints 'Updated obm and obmd in ...', and the finally block deletes the unmoved .obm.update, so Leftovers() is empty too. All 18 UpdateTests that run still pass. Users would keep the old obm, which the new engine refuses whenever a release changes EngineProtocol.Version, telling them to restart the engine, which doesn't help.
- **Suggested fix:** Record obm's modified time before the update and assert it moved on a minute, as line 72 does for obmd; the Within(1 s) tolerance covers Ustar's whole seconds. That check passed on the current code and failed on the change.

### 85. CreateIfMissing_WritesTheStartingFileOnce passes even when CreateIfMissing writes nothing

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Core.Tests/ConfigFileTests.cs`: 305, 309, 313
- **Status:** CONFIRMED
- **What's wrong:** After the first CreateIfMissing, the test overwrites config.toml itself (ConfigFileTests.cs:309), and its only assertion (line 313) checks that the second call left that text alone. Neither it nor any other test checks that the first call wrote the starting file, so that half of its name is never tested.
- **Failure scenario:** A change makes CreateIfMissing a no-op, and all of Core.Tests (236), this test included, still pass, while no Windows test reads config.toml after the app starts. The Windows app's only call (Program.cs:148, run at every start) then writes no starting file, and nothing else does in normal use. A user who deletes config.toml to get the defaults back, as docs/guide/settings.md:29 says ('The app writes a fresh one the next time it starts'), restarts the app and finds no file.
- **Suggested fix:** Between the first CreateIfMissing and the File.WriteAllText, assert that `File.ReadAllText(FilePath)` equals `ConfigFile.DefaultText.ReplaceLineEndings("\n")`. Checked: this passes on the current code and fails for a no-op, a wrong path and an empty file.

### 86. Crash report test's "from the run alone" check can't fail; nothing else tests it

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Core.Tests/CrashReportsTests.cs`: 31, 33, 45, 47
- **Status:** CONFIRMED
- **What's wrong:** Write_HasTheErrorVersionOSAndTheEndOfTheRunsLog checks "from the run alone" with Does.Not.Contain("An earlier run") (CrashReportsTests.cs:47), but the run then appends 151 lines, so the 100-line cap drops "An earlier run" whether or not RecentLog seeks to run.LogLength (CrashReports.cs:297-300). No other test checks that a report's log leaves out an earlier run's lines.
- **Failure scenario:** If RecentLog stops seeking to run.LogLength, or seeks to the wrong byte, every test still passes; this was shown for three such changes. A run that crashes before logging 100 lines, on a day the engine already ran, then gets a report whose "Recent log:" starts with the previous run's lines, such as "started, process &lt;another pid>" and "Engine stopped". That sends whoever reads it, or the GitHub issue obm crashes report fills in, to the wrong run.
- **Suggested fix:** Add a test with a run under 100 lines after an earlier run's line in one file, and assert the report's log section exactly, as Contain and Not.Contain miss a seek landing inside that line. Keep the 150-line run for the cap only.

### 87. RepeatOf_ARunThatReportedTheLastOne claims to test a report written after the run started, but doesn't

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Core.Tests/CrashReportsTests.cs`: 327, 329, 331, 332, 335, 447
- **Status:** CONFIRMED
- **What's wrong:** The test's comment (CrashReportsTests.cs:327) says it covers a report written after the run started, but it writes the report before moving the clock, at exactly the run's start. So a RepeatOf (CrashReports.cs:71) that ignores reports written after the run's start passes all 35 CrashReportsTests, and no other test reaches RepeatOf.
- **Failure scenario:** Someone tightens CrashReports.cs:71 to also require 'written <= run.Started', and all 35 tests still pass, though in real use the report on the previous run is always written after the new run started. An obmd in a crash loop outside .NET then writes a report every restart: 13 SIGKILLed starts wrote 12 reports, deleting the first two, where the real code wrote one. Under launchd's KeepAlive, the first report, which says why, is pushed out within 10 restarts.
- **Suggested fix:** Move the fake clock on between RunFrom(TimeSpan.Zero) and the WriteEnded call, for example with _time.Advance(TimeSpan.FromSeconds(2)), so the report is written after the run started, as in real use. With that line, the test fails against the mutation and passes against the current code.

### 88. WaitForWindow(AppName) also matches the flyout, so two main-window tests pass when it opens instead

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.App.Windows.UITests/AppTests.cs`: 167, 333; `tests/OpenBackupManager.App.Windows.UITests/NotificationTests.cs`: 28; `tests/OpenBackupManager.App.Windows.UITests/TrayTests.cs`: 40
- **Status:** PLAUSIBLE
- **What's wrong:** WaitForWindow(AppName) (AppTests.cs:167) returns the first uncloaked app window titled AppName, and the status flyout, titled the same (StatusFlyout.xaml.cs:38), is on top, so it's listed first. NotificationTests.ViewDetails_OpensTheMainWindow and TrayTests.Open_OpensTheMainWindow check nothing else, unlike DoubleClick and ClickingAfterQuit, which assert IsFlyoutShown() is false, so both pass when the flyout opens instead.
- **Failure scenario:** Suppose a change leaves the running copy without a registered notification activator, such as one registered only when started with -Embedding. Clicking View details then makes COM start a copy with -Embedding, which redirects to the running copy (Program.cs:241) and exits, so the click is lost and the running copy only opens its flyout. ViewDetails_OpensTheMainWindow still passes, because WaitForWindow(AppName) at NotificationTests.cs:28 finds the flyout, titled "Open Backup Manager (dev)", and no other test clicks a notification while the app runs.
- **Suggested fix:** Add a WaitForMainWindow helper that matches w.Title == AppName && !IsFlyout(w), and use it in both tests and wherever a test means the main window. Otherwise, assert IsFlyoutShown() is false afterwards, as DoubleClick_OpensTheMainWindow does, closing the launch flyout first in TrayTests.Open.

### 89. OverlappingCalls_EachGetTheirOwnAnswer passes when replies go to the wrong call

- **Rating:** low
- **Area:** Tests
- **Where:** `tests/OpenBackupManager.Engine.Tests/ListenerTests.cs`: 103, 108, 110
- **Status:** CONFIRMED
- **What's wrong:** The test (ListenerTests.cs:103-110) makes 20 overlapping status calls to the real engine, which gives identical answers in the order sent (Listener.ServeAsync handles one line at a time), so it can't tell whether EngineClient.ReadAsync gives each reply to the call with its id. No other test checks this.
- **Failure scenario:** Change ReadAsync to give each reply to the newest, the oldest or every waiting call, ignoring the id, and this test and every other ListenerTest stay green. This was run: with 'newest', all 20 status replies went to the wrong call. Once a client overlaps calls on one connection, such as the app asking for status while its reloadConfig is still waiting, that regression would give the status call the reloadConfig's reply, which fails to read as a status.
- **Suggested fix:** Test against a fake engine, as RefuseAsync does, that reads all the overlapping calls, then answers them in neither the order sent nor its reverse with processId = id, and check each call got its own id. Such a test failed with all three changes.

### 90. The guide's Docker commands use the container name openbackupmanager, which its compose.yaml never creates

- **Rating:** low
- **Area:** User guide
- **Where:** `docs/guide/docker.md`: 15, 29, 42, 45, 51; `docs/guide/checking-downloads.md`: 29
- **Status:** CONFIRMED
- **What's wrong:** The compose.yaml at docker.md:15 sets no container_name, so Compose names the container `<folder>-openbackupmanager-1`. Yet the guide's docker logs, docker exec and docker cp commands (docker.md:29, 42, 45, 51, checking-downloads.md:29) pass the bare name openbackupmanager, and since plain docker takes only an exact name or ID, all fail.
- **Failure scenario:** A user runs docker compose up -d with the guide's compose.yaml in `~/obm`, creating obm-openbackupmanager-1, and the engine can't start because the root daemon made ./config as root (finding 92). The guide's way to see why, docker logs openbackupmanager, fails with 'Error response from daemon: No such container: openbackupmanager' (exit 1). After they chown the folder and the engine is healthy, docker exec openbackupmanager obm status, obm diagnostics export and docker cp still fail the same way.
- **Suggested fix:** Add 'container_name: openbackupmanager' to the service in docker.md's compose.yaml, which makes every command work as written. Or switch the commands to compose forms run from that folder, such as docker compose logs and docker compose exec openbackupmanager obm status (both fixes checked).

### 91. Guide says macOS and Linux have no settings file; obmd and obm read `~/.config/openbackupmanager/config.toml`

- **Rating:** low
- **Area:** User guide
- **Where:** `docs/guide/settings.md`: 15, 19; `docs/guide/getting-started.md`: 97, 101; `docs/guide/files.md`: 25, 36
- **Status:** CONFIRMED
- **What's wrong:** settings.md:15-19 and getting-started.md:97-101 say macOS and Linux have no settings file, or no settings, though settings.md's table marks secret-store and updates.prereleases as working on All. On both, obmd applies config.toml from AppPaths.Config, obm update reads it, obm secrets migrate writes it, and no page says where it is.
- **Failure scenario:** A Linux user runs obm status and is told 'Secrets: no store chosen yet. Choose one with secret-store in config.toml or OBM_SECRET_STORE', but the Settings page says 'There's no settings file on Linux yet', with no path, and OBM_SECRET_STORE in their shell doesn't reach an engine started by obm service install. If they guess the engine's folder from files.md, `~/.local/state/openbackupmanager/config.toml`, obm config validate says 'config.toml has no errors or warnings' and the setting is ignored.
- **Suggested fix:** In the macOS and Linux tabs of settings.md and getting-started.md, give the path ($XDG_CONFIG_HOME/openbackupmanager/config.toml, by default `~/.config/openbackupmanager/config.toml`, or the --home folder) and say that secret-store and updates.prereleases apply there. Add a Settings row for that folder to files.md's macOS and Linux tables.

### 92. docker.md skips making ./config, so Docker creates it as root and every start fails

- **Rating:** low
- **Area:** User guide
- **Where:** `docs/guide/docker.md`: 11, 21, 26, 29
- **Status:** CONFIRMED
- **What's wrong:** docker.md:11 says to run docker compose up -d in a new folder, with ./config bind-mounted and the engine running as the user's IDs, but never to create ./config first. Rootful Docker then creates it as root:root 755, so the engine can't create /config/engine.lock and exits 1 on every start.
- **Failure scenario:** With rootful Docker on Linux, a user saves the page's compose.yaml in a new folder, with user set to their IDs, and runs docker compose up -d, which exits 0, but Docker made ./config as root:root 755. Each start then exits 1, so restart: unless-stopped leaves docker compose ps at Restarting (1) until they find the page's chown step, and each one logs "obmd: the engine can't keep its state in /config. Access to the path '/config/engine.lock' is denied."
- **Suggested fix:** Before docker compose up -d, add a step creating the folder as the user whose IDs go in user (mkdir config), and say Docker otherwise creates it as root. With config made first, the engine was healthy within 5 seconds, with no restarts.

### 93. The Docker guide's :latest examples fail with 'manifest unknown' until the first full release

- **Rating:** low
- **Area:** User guide
- **Where:** `docs/guide/docker.md`: 16; `docs/guide/checking-downloads.md`: 29
- **Status:** CONFIRMED
- **What's wrong:** The compose and attestation examples (docker.md:16, checking-downloads.md:29) name ghcr.io/amyjeanes/openbackupmanager:latest, but a tag build pushes only its version tag (ci.yml:675-676), and release-published.yml:24 moves latest only to GitHub's latest release, never a prerelease. No latest exists from the first prerelease, when testers are invited to try it, until the first full release.
- **Failure scenario:** v0.1.0-alpha.1 is published as a prerelease, so release-published.yml gets a 404 from releases/latest and exits 0 without tagging. A tester who copies docker.md's compose.yaml and runs docker compose up -d gets 'manifest unknown' from the pull, and gh attestation verify oci://ghcr.io/amyjeanes/openbackupmanager:latest fails with 'failed to fetch remote image ... MANIFEST_UNKNOWN: manifest unknown', as it already does today. Only the tag table at the bottom of docker.md hints that a version tag such as 0.1.0-alpha.1 is needed.
- **Suggested fix:** In docker.md, say beside the compose example that there's no latest until the first full release, so use a version tag such as 0.1.0-alpha.1 (or edge). In checking-downloads.md, name the tag being run, such as `oci://ghcr.io/amyjeanes/openbackupmanager:<tag>`, as the tarball examples already use `<version>`.

### 94. SECURITY.md says the app updates itself, but on macOS, Linux and Docker nothing does

- **Rating:** low
- **Area:** User guide
- **Where:** `SECURITY.md`: 5
- **Status:** CONFIRMED
- **What's wrong:** SECURITY.md:5 says only the latest release gets fixes and 'The app updates itself, so staying on it needs nothing from you'. But only the Windows tray app updates by itself; on macOS, Linux and Docker, someone must run obm update, install the newer .deb/.rpm or pull the newer image.
- **Failure scenario:** A security fix ships as a new release, and a user on the macOS or Linux tarball, a .deb or .rpm, or the Docker image reads SECURITY.md, believes they'll be kept current, and never runs obm update, installs the newer package or runs docker compose pull. Nothing tells them otherwise: obm status doesn't mention newer versions, obmd never checks, and the package adds no apt or dnf repository. So they keep running the unfixed version indefinitely.
- **Suggested fix:** Scope the sentence to Windows ('The Windows app updates itself'). Then say that on macOS and Linux you run obm update or install the newer .deb/.rpm, and in Docker you pull the newer image, linking the guide's Updates page.

### 95. Guide's Task Scheduler task won't run the engine on battery or past 3 days

- **Rating:** low
- **Area:** User guide
- **Where:** `docs/guide/command-line.md`: 46; `src/OpenBackupManager.Cli/Program.cs`: 191
- **Status:** PLAUSIBLE
- **What's wrong:** The guide's only Windows way to run obmd without the app is Task Scheduler with an At log on trigger (command-line.md:46); obm service install points there too. Such a task keeps Task Scheduler's defaults: not started on battery, stopped when unplugged or after 72 hours, and nothing restarts it.
- **Failure scenario:** A laptop user turns off start-at-login and, as the guide says, makes a 'When I log on' task that starts %LocalAppData%\OpenBackupManager.App\current\obmd.exe. If they sign in unplugged, Task Scheduler doesn't start it; on mains power, it starts but is ended when the charger comes out, or 72 hours after sign-in. Until the next sign-in, obm status says only 'obm: the engine isn't running' (exit 1), and once backing up is built, backups would silently stop.
- **Suggested fix:** Have the guide say to clear 'Stop if the computer switches to battery power', then 'Start the task only if the computer is on AC power', and 'Stop the task if it runs longer than'. Or show Register-ScheduledTask with New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0.

### 96. On Windows, the guide's obm config reload step doesn't reach the app's own settings

- **Rating:** low
- **Area:** User guide
- **Where:** `docs/guide/settings.md`: 25; `src/OpenBackupManager.Cli/Config.cs`: 26
- **Status:** CONFIRMED
- **What's wrong:** settings.md:25 says obm config reload applies edits the app can't see, but it only has obmd re-read its ConfigWatcher. On Windows, start-at-login and [updates] live in the app's own ConfigWatcher, which only its FileSystemWatcher reloads, so they wait for an app restart, though obm prints "config.toml's settings are applied" (Config.cs:26).
- **Failure scenario:** A Windows user runs the installed app with --home on a network share whose change notifications don't reach the PC, adds '[updates] automatic = false' and runs 'obm --home &lt;same folder> config reload', as the guide says. obm prints "config.toml's settings are applied" and exits 0, but the app's Updater still checks every 6 hours, downloads the update and notifies, and Quit installs it. It's rare, since it needs a home on a network drive.
- **Suggested fix:** In settings.md, say that on Windows obm config reload only applies the engine's secret-store, while start-at-login and [updates] need the app restarted, and fix docs/design/config.md:45 too. Or have the app re-read config.toml when asked, and have obm say the engine applied the settings.

### 97. The Windows install steps don't mention the SmartScreen warning an unsigned Setup.exe gets

- **Rating:** low
- **Area:** User guide
- **Where:** `docs/guide/getting-started.md`: 9, 10; `.github/workflows/ci.yml`: 303
- **Status:** PLAUSIBLE
- **What's wrong:** CI's package-windows job (ci.yml:303) packs Setup.exe unsigned, so a browser-downloaded Setup.exe has no file or certificate reputation, and Windows warns or blocks before it runs. The guide's Windows steps don't mention this, though its macOS steps explain macOS's equivalent, and docs/design records only that one as an accepted limit.
- **Failure scenario:** A Windows user downloads OpenBackupManager.App-win-x64-Setup.exe in a browser and runs it, as step 2 says. SmartScreen shows 'Windows protected your PC' with Run anyway hidden behind More info, and since the guide doesn't say this is expected, they give up or take it for malware. On Windows 11 with Smart App Control on, Microsoft says an unsigned app it can't vouch for is blocked with no way past it for that one app, so Setup can't run at all.
- **Suggested fix:** Add a Windows warning beside the macOS one: check Setup.exe with gh attestation verify, then choose More info and Run anyway; Smart App Control must be off. Record unsigned Windows builds as an accepted limit; only signing (vpk's --azureTrustedSignFile or --signParams) removes the prompt.

## To check on a real machine

These depend on how Windows, macOS, a real service manager or real hardware behaves, so they couldn't be settled in the review's Linux container.

1. **Suspicion:** With the notification platform unavailable, as when debloating or managed images disable WpnUserService, CreateToastNotifier in WindowsNotifier's constructor throws COMException 0x803E0105 while OnLaunched resolves Updater (App.xaml.cs:37), closing the app at every start. ToastNotifier.Show failing after a config.toml error would close it from ConfigWatcher's timer thread (ConfigNotices.cs:26).
   - **File:** `src/OpenBackupManager.Platform.Windows/WindowsNotifier.cs:32`
   - **Steps:** 1. On a Windows 11 VM with the app installed and started once, run in an elevated terminal: sc.exe config WpnUserService start= disabled, then sc.exe config WpnService start= disabled, and restart. 2. Sign in, start Open Backup Manager from the Start menu and wait 10 seconds. 3. If the tray icon is there, choose Show test notification from its menu. 4. If it's still running, add start-at-login = "yes" to %LocalAppData%\OpenBackupManager\config.toml, save and wait 2 seconds. 5. Look in %LocalAppData%\OpenBackupManager\crashes and Event Viewer > Windows Logs > Application. 6. Set both services back with start= auto and restart.
   - **Look for:** The app closing (at start, on Show test notification or after the save), with a crash report or Application Error event naming COMException 0x803E0105 from CreateToastNotifier or ToastNotifier.Show. Still running, just without notifications, clears it.

2. **Suspicion:** Show file on config-error notifications starts explorer.exe by bare name with UseShellExecute off, so CreateProcess looks for it in the app's current folder before C:\Windows; if any launch leaves that somewhere writable, such as Downloads, where Setup.exe runs, a planted explorer.exe runs as the user.
   - **File:** `src/OpenBackupManager.App.Windows/AppActions.cs:96`
   - **Steps:** 1. On a Windows 11 VM, download the release's Setup.exe into Downloads and run it from Explorer. 2. When the app starts, open its properties in Process Explorer and read Image > Current directory. 3. Repeat for each other way the app starts: Restart to update (tools/Publish-TestUpdate.ps1), the Start menu shortcut, sign-in through the Run key, a notification click while it isn't running, and a crash restart (a Debug build's tray Crash menu once it has run 60 seconds). 4. For any launch whose current directory is user-writable and isn't %LocalAppData%\OpenBackupManager.App\current, put a harmless program named explorer.exe there, such as a console program that writes a marker file. 5. Put version = "x" in that home's config.toml so 'Settings file has a problem' appears, and click Show file.
   - **Look for:** A current directory of Downloads, or another folder outside %LocalAppData%\OpenBackupManager.App and C:\Windows\System32, and the planted program running instead of Explorer opening with config.toml selected.

3. **Suspicion:** obmd's console is hidden only by ShellExecute's Hidden window style, so where Windows Terminal is the default terminal, as on new Windows 11 installs, conhost may hand it to a visible Terminal window, and closing that ends the engine; CI's conhost runners can't show it.
   - **File:** `src/OpenBackupManager.App.Windows/EngineConnection.cs:193`
   - **Steps:** 1. On Windows 11 22H2 or later with Windows Terminal installed, set Terminal to Windows Terminal under Settings > System > For developers (Privacy & security > For developers on older builds). 2. With no obmd.exe running (check Task Manager), start the installed app from the Start menu and watch the screen and taskbar for 10 seconds. 3. With start at login on, sign out and back in and watch again. 4. If a window for obmd.exe appears, close it and watch the flyout's status.
   - **Look for:** A Windows Terminal window or tab for obmd.exe, and the status going to Starting or Can't reach the engine once it's closed. No window, with obmd.exe running in Task Manager, clears it.

4. **Suspicion:** Report on GitHub and obm crashes report (Browser.cs:20) hand ShellExecute 2,100 to 6,500-character links, sized by GitHub's sign-in redirect (BugReport.LongestLink), past Windows' documented limit of about 2,083, so every fitting report may arrive cut short, or a refusal's Win32Exception, uncaught in OnReport, closes the app.
   - **File:** `src/OpenBackupManager.App.Windows/CrashWindow.xaml.cs:35`
   - **Steps:** 1. On Windows 11, and Windows 10 1809 or later, make Edge, then Chrome, then Firefox the default browser. 2. Run a Debug build until it has logged about 100 lines, crash it with the tray's Crash > On another thread, and start it again. 3. In the crash window, check the report fits (the issue won't ask you to paste it) and click Report on GitHub. 4. Compare the issue body with the redacted report: the closing ``` fence, `</details>` and the '### AI assistance (if any)' heading should all be there. 5. Repeat with a report whose redacted text only just fits, so its link is near 6,500 characters. 6. Run obm crashes report for the same report and compare again.
   - **Look for:** A body cut off near 2,083 characters, without the report's end, closing fence and last headings; nothing opening or a broken page; or the app closing, its new crash report naming Win32Exception from Process.Start.

5. **Suspicion:** The engine's pipe name derives from the home, whose default follows from the profile path, so another signed-in user can take it first; FirstPipeInstance then stops the engine starting while they hold it: Windows' counterpart of finding 11, not an accepted limit.
   - **File:** `src/OpenBackupManager.Engine/Listener.cs:102`
   - **Steps:** 1. On Windows with two standard users signed in at once (fast user switching or RDP), as user B run in PowerShell 7: $h = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('C:\USERS\\&lt;A>\APPDATA\LOCAL\OPENBACKUPMANAGER'))).Substring(0,16); $p = [System.IO.Pipes.NamedPipeServerStream]::new("OpenBackupManager-$h-engine", 'InOut', 1), and leave it open. 2. As A, start the tray app or run obmd in a terminal. 3. As A, run obm status.
   - **Look for:** While B holds the pipe: obmd exiting with 'Something else is already listening at OpenBackupManager-...-engine', the app showing the engine as started by another user or as administrator, and obm status saying it isn't running.

6. **Suspicion:** The status flyout, one reused window, has no Closing or Closed handler, so Alt+F4 on it, or taskkill without /f, may destroy it; then opening it, theme and status updates and Quit touch a closed window, and SessionEnd (Program.cs:170), listening on it, misses sign-out.
   - **File:** `src/OpenBackupManager.App.Windows/StatusFlyout.xaml.cs:18`
   - **Steps:** 1. On Windows, click the tray icon to open the flyout and press Alt+F4 while it has focus. 2. Click the tray icon again. 3. Switch Windows between light and dark mode in Settings > Personalization > Colors. 4. Choose Quit from the tray menu. 5. In a second run, after step 1 sign out and back in, then read the app's log in %LocalAppData%\OpenBackupManager\logs. 6. In a third run, with the app idle in the tray, run taskkill /im OpenBackupManager.App.Windows.exe without /f, then click the tray icon.
   - **Look for:** The flyout not reopening, a crash report naming RO_E_CLOSED or a COMException from StatusFlyout or Tray, Quit crashing, or the next start after sign-out logging 'The last run closed unexpectedly without saying why'.

7. **Suspicion:** obm update renames freshly unpacked obm and obmd (Update.cs:181) into place without flushing, so a power cut soon after can leave obmd or obm zero-length; once obm reports the new version, obm update won't replace it, and the service can't start the engine.
   - **File:** `src/OpenBackupManager.Cli/Update.cs:193`
   - **Steps:** 1. In a macOS VM on APFS, unpack a release's obm and obmd into `~/.local/bin`, and serve a newer release-shaped tarball as the Cli tests' FakeGitHub does, with OBM_UPDATE_SOURCE pointing at it. 2. Run obm update and force the VM off 1 to 2 seconds after it prints 'Updated obm and obmd'. 3. Boot and compare the size and SHA-256 of obm and obmd with the tarball's. 4. Run obmd --version, then obm update again. 5. Repeat several times, also waiting 30 to 60 seconds before the power-off. 6. On Linux, repeat on an XFS file system: after the update, fsync any other file on it and shut it down without flushing (xfs_io -x -c shutdown `<mountpoint>`), as finding 31's verifier did for config.toml.
   - **Look for:** A zero-length or mismatched obm or obmd after boot, obmd failing to run, and obm update answering 'This is the newest version' without replacing it.

8. **Suspicion:** The macOS install steps run tar -xzf on `~/Downloads/OpenBackupManager-<version>-osx-arm64.tar.gz`, but Safari's default 'Open safe files after downloading' may already have gunzipped or expanded it, so the guide's exact command fails for anyone who downloaded it with Safari.
   - **File:** `docs/guide/getting-started.md:26`
   - **Steps:** 1. On a Mac with Safari's default settings, click a release's `OpenBackupManager-<version>-osx-arm64.tar.gz` asset (or an edge tarball) in Safari. 2. List `~/Downloads`. 3. Run the guide's tar command exactly as written.
   - **Look for:** The .tar.gz gone, replaced by a .tar or an expanded folder, and tar failing with 'Error opening archive: Failed to open ...tar.gz'.

9. **Suspicion:** .NET's FileStream on Unix ignores flock errors other than EWOULDBLOCK, so where an NFS home's locking fails with ENOLCK (as with NFSv3 and rpc.statd stopped), ProcessLock succeeds for every obmd; the second deletes the first's run/engine.sock (Listener.cs:89), and nothing can reach or stop the first.
   - **File:** `src/OpenBackupManager.Core/ProcessLock.cs:24`
   - **Steps:** 1. On a Linux client, mount at /mnt/h an NFSv3 export whose locking doesn't work (rpc.statd stopped on the client, or the server's lockd blocked). 2. Check that flock -n /mnt/h/x true fails with 'No locks available'. 3. Start obmd --home /mnt/h/obm in two terminals. 4. Run obm status --home /mnt/h/obm and pgrep -a obmd.
   - **Look for:** Both obmd starting, obm status reaching only the second, and the first still running with run/engine.sock gone, so nothing through obm can stop it.

10. **Suspicion:** With obmd switched off in Login Items on macOS 13 or later, launchd may refuse the bootstrap that obm service install does after booting the agent out (LaunchAgent.cs:37), giving a raw launchctl error and no engine running instead of Service.cs:29-31's 'switched off' message.
   - **File:** `src/OpenBackupManager.Cli/LaunchAgent.cs:42`
   - **Steps:** 1. On macOS 13 or later, run obm service install. 2. Switch obmd off in System Settings > General > Login Items & Extensions. 3. Run obm status, then obm service install again, then obm status. 4. Repeat with obmd started by hand in a terminal before step 3. 5. Clean up with obm service uninstall.
   - **Look for:** 'obm: launchctl bootstrap `gui/<uid>` ... failed: Bootstrap failed: 5: Input/output error', exit 1 and no engine running, rather than 'The engine is running, but won't start when you sign in...'.

11. **Suspicion:** obm service install writes the unit under the XDG_CONFIG_HOME from systemctl --user show-environment, the environment systemd gives its units, but systemd finds units by its own process environment, so once environment.d, import-environment or dbus-update-activation-environment sets XDG_CONFIG_HOME, enabling it fails after stopping the running engine.
   - **File:** `src/OpenBackupManager.Cli/SystemdService.cs:28`
   - **Steps:** 1. On Linux in a systemd user session, run systemctl --user set-environment XDG_CONFIG_HOME=$HOME/altcfg. 2. Run systemctl --user show -p UnitPath and note whether $HOME/altcfg/systemd/user is in it. 3. Start obmd --home /tmp/obm-x in another terminal. 4. Run obm service install --home /tmp/obm-x, then obm status --home /tmp/obm-x. 5. Clean up with systemctl --user unset-environment XDG_CONFIG_HOME, obm service uninstall --home /tmp/obm-x and rm -r $HOME/altcfg.
   - **Look for:** UnitPath without $HOME/altcfg, and obm writing $HOME/altcfg/systemd/user/openbackupmanager-&lt;key>.service, then failing with 'systemctl --user enable ... failed: ... Unit file ... does not exist' after stopping the hand-started engine. A successful install clears it.

12. **Suspicion:** A Quit or sign-out during About's Check for updates, even one waiting behind the background check, disposes Updater's semaphore and token source under it, so _checking.Release() (Updater.cs:221) throws ObjectDisposedException, which AsyncRelayCommand rethrows as a UI-thread crash; finding 19's fix, passing cancellation through, wouldn't stop it.
   - **File:** `src/OpenBackupManager.App.Windows/Updater.cs:98`
   - **Steps:** 1. Install a build that uses a local feed with tools/Publish-TestUpdate.ps1 -Install, and add a newer version large enough to take several seconds to download, or throttle the network. 2. Open About and click Check for updates. 3. While it says Downloading update... N%, choose Quit from the tray menu. 4. Start the app again. 5. Repeat a few times, also signing out instead of quitting, and clicking Check for updates while the start-up check is still downloading.
   - **Look for:** A `crash-<time>-app.txt` in the home's crashes folder naming ObjectDisposedException (SemaphoreSlim or CancellationTokenSource) or TaskCanceledException after a clean Quit, and the Closed unexpectedly window at the next start.

13. **Suspicion:** RunKey writes a Run entry of any length and reports it as on, but Microsoft documents a 260-character limit on Run entries, and longer ones reportedly don't run, so with a home path over about 130 characters, start at login may silently fail.
   - **File:** `src/OpenBackupManager.Platform.Windows/RunKey.cs:41`
   - **Steps:** 1. On Windows 11 with the installed app, make a folder whose full path is about 150 characters. 2. Run OpenBackupManager.App.Windows.exe --home "&lt;that folder>" and put start-at-login = true in that home's config.toml. 3. Check with reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run that its `OpenBackupManager-<key>` value is there and longer than 260 characters. 4. Quit, sign out and sign in. 5. Repeat with a home under 100 characters as a control.
   - **Look for:** No tray icon for the long home after signing in, and no new 'Started at login' line in its `logs\app-*.log`, while the short home's copy starts.

14. **Suspicion:** obm --json writes non-ASCII letters unescaped (UnsafeRelaxedJsonEscaping) and nothing sets Console.OutputEncoding, so on Windows they're encoded in the console's code page, and one it lacks, such as ő on an English install, reaches scripts as ? or a look-alike, giving them a wrong path.
   - **File:** `src/OpenBackupManager.Cli/Json.cs:18`
   - **Steps:** 1. On an English (US) Windows install, run obmd --home C:\obm-ő in one terminal. 2. In PowerShell, run (obm status --home C:\obm-ő --json | ConvertFrom-Json).config. 3. Run obm status --home C:\obm-ő --json > out.json and look at its bytes with Format-Hex out.json. 4. Repeat in conhost and in Windows Terminal.
   - **Look for:** C:\obm-? or C:\obm-o instead of C:\obm-ő. JSON holding \u0151, or ő intact, clears it.

15. **Suspicion:** The guide says desktop distros have the OpenSSL 3 the tarball needs, but Fedora 45's openssl-libs is OpenSSL 4, OpenSSL 3 being openssl3-libs (nfpm.yaml:49-51), so a default Workstation may lack it, aborting obm update and obmd, which hashes the home with SHA-256 at start (Platform.Linux/ServiceCollectionExtensions.cs:16).
   - **File:** `docs/guide/getting-started.md:77`
   - **Steps:** 1. On a fresh Fedora 45 Workstation install, run rpm -q openssl3-libs. 2. Unpack the linux-x64 tarball into `~/.local/bin` as the guide says. 3. Run obmd in one terminal and obm status in another. 4. Run obm update --check.
   - **Look for:** openssl3-libs not installed, and obmd or obm aborting with 'No usable version of libssl was found' or a cryptography TypeInitializationException.

16. **Suspicion:** StartedAtLogin_StaysInTheTray polls IsFlyoutShown with Retry.WhileFalse without ignoreException, unlike every other waiting caller, while AppTests.Windows() (AppTests.cs:341) reads every desktop window and throws when one closes as they're listed, so any window closing during its two 3-second watches fails the test.
   - **File:** `tests/OpenBackupManager.App.Windows.UITests/StartAtLoginTests.cs:46`
   - **Steps:** 1. On a Windows runner or in Windows Sandbox, start a PowerShell loop that opens and closes a window every 200 ms, such as while ($true) { $p = Start-Process cmd -PassThru; Start-Sleep -Milliseconds 100; Stop-Process $p }. 2. Run dotnet test tests/OpenBackupManager.App.Windows.UITests --filter `FullyQualifiedName~StartedAtLogin_StaysInTheTray` 30 times. 3. Read each failure's message and stack.
   - **Look for:** A failure at StartAtLoginTests.cs:46 or :54 with ElementNotAvailableException or COMException thrown from AppTests.Windows() or NativeMethods.TopLevelWindow, rather than an assertion failure.

17. **Suspicion:** InstallTests' last check, 'The app's notifications should be cleared', only looks for no 'Update installed' item right after uninstalling has deleted the app's AppUserModelId and CLSID keys, so it may pass whether or not WindowsNotifier.Unregister's History.Clear ran.
   - **File:** `tests/OpenBackupManager.App.Windows.UITests/InstallTests.cs:212`
   - **Steps:** 1. On Windows, delete the line ToastNotificationManager.History.Clear(AppId); from WindowsNotifier.Unregister. 2. Pack 0.0.1 to 0.0.4 with tools/Publish-TestUpdate.ps1 as CI's install job does, and set OBM_TEST_RELEASES. 3. Run dotnet test tests/OpenBackupManager.App.Windows.UITests --filter Category=Install. 4. Open the notification centre by hand afterwards. 5. Put the line back and run it again.
   - **Look for:** UpdatesAndUninstalls passing without History.Clear, whether or not 'Update installed' still shows in the notification centre after the uninstall.

18. **Suspicion:** RcloneFetch downloads rclone with a plain new HttpClient(), which uses the system proxy but sends it no credentials, so the first build fails with 407 behind a proxy needing Windows sign-in (NTLM or Kerberos), though NuGet restore, which sends the user's credentials, gets through.
   - **File:** `tools/RcloneFetch/Program.cs:48`
   - **Steps:** 1. On a Windows machine behind a proxy that needs Windows sign-in, delete src/OpenBackupManager.Rclone/obj/rclone. 2. Run dotnet build OpenBackupManager.slnx.
   - **Look for:** Restore succeeding, then the build failing with 'RcloneFetch: Response status code does not indicate success: 407 (Proxy Authentication Required).'

## Risky and untested

Code that deletes things, applies updates or handles secrets, where no test covers the path:

- Data.RemoveAsync (obm data remove): the y/N question (:54), aborting when obmd --remove-secrets fails (:70), and the deletes without --home (:78) (`src/OpenBackupManager.Cli/Data.cs:54`): A regression would delete config.toml, the state folder, logs and every secret without consent or while the engine runs, or folders that aren't the app's and another home's OS-store secrets.
- Secrets.MigrateAsync (obm secrets migrate): removing every listed name, unread ones too (:93), setting secret-store (:139) and reporting leftovers (Migrate.cs:29) (`src/OpenBackupManager.Engine/Secrets.cs:93`): Deletes secrets kept only in a non-default keyring (finding 8). Can strand the engine on the emptied store (finding 2) or break dotfiles links (finding 4). Broken reporting would hide plain-text leftovers.
- FileStore.WriteAsync's failure path: the catch that deletes secrets.json.tmp and rethrows (`src/OpenBackupManager.Core/Platform/FileStore.cs:137`): If it deleted the wrong file, a failed save, as on a full disk, would lose every secret in the file store. A mutation deleting secrets.json passed every test.
- ci.yml's Velopack pack ID, OpenBackupManager.App (copied in tools/Publish-TestUpdate.ps1:42), which names the install folder Velopack deletes on uninstall and reinstall (`.github/workflows/ci.yml:306`): If ci.yml changed it to the data folder's name, an uninstall or reinstall would delete %LocalAppData%\OpenBackupManager with config.toml, secrets.json and logs. InstallTests use the script's copy, so that ships unnoticed.
- obm update: the version check (:32), Replace's entry checks (:174), unpacking after hashing (:181), and a failure between the renames (`src/OpenBackupManager.Cli/Update.cs:174`): Could install an older release, a swapped tarball (finding 14), a link or a file outside the folder, or only half the update. Umask 077 locks out other users (finding 16).
- Release flow: Get-Version.ps1's tag checks, the release job's uploads (ci.yml:735), and release-published.yml:24 moving the image's latest tag (`.github/workflows/ci.yml:735`): A wrong or missing feed or tarball stops updates. Partial runs can't be re-run (finding 68). The latest tag can lag (finding 69). Broken tag checks could draft lower or off-main releases.
- obmd --remove-secrets (SecretsRemoval): returning 0 for locked, failing or unavailable stores (:46), and the Windows Credential Manager key (:30) (`src/OpenBackupManager.Daemon/SecretsRemoval.cs:46`): obm data remove says it removed everything and exits 0 while secrets stay stored (finding 40). On Windows, a wrong key or prefix would remove another home's secrets.
- The crash window's Copy report, which skips redaction (:26), and obm crashes report's too-long copy in the temp folder (Crashes.cs:68) (`src/OpenBackupManager.App.Windows/CrashWindow.xaml.cs:26`): Home paths, user names and web-address passwords reach public issues while the caption says they're redacted (finding 7). Other users can read the /tmp copy (finding 14); no test checks it's redacted.
- Windows app Updater: Check's uncaught errors (:213), failed downloads and unreadable packages (:198), and reading last-version at start (:152) (`src/OpenBackupManager.App.Windows/Updater.cs:213`): Check for updates closes the app and 6-hourly checks stop (findings 19, 47); a waiting update is deleted (finding 57) or never installs (finding 58); an unreadable last-version closes the app every start.
- tools/RcloneFetch's rejection paths: VerifyClearSigned, ReadClearTextLines, FindHash and the zip's hash comparison (`tools/RcloneFetch/Program.cs:99`): A regression or BouncyCastle update accepting an unsigned or tampered SHA256SUMS, or a mismatched zip, would still build. BundledRcloneTests would then run that rclone on developer machines and CI.
- tools/Publish-TestUpdate.ps1 -Reset removing whatever -Feed resolves to (`tools/Publish-TestUpdate.ps1:34`): An empty or unset -Feed resolves to the current folder (checked with pwsh 7.6), so -Reset deletes it, perhaps a repository, recursively with -Force. A mistyped -Feed is deleted too.
- Service removal: UpdateWatcher taking a failed stat as obmd gone (:149), and SystemdService.Uninstall when systemctl --user can't be reached (SystemdService.cs:73) (`src/OpenBackupManager.Engine/UpdateWatcher.cs:149`): Two failed stats delete the unit or plist and stop the engine; nothing restarts it (finding 39). Uninstall deletes the unit; the engine runs on, yet obm says it stopped (finding 18).

## Tests that fail here

- **13 of the 14 Cli.Tests ServiceTests (Explicit, Sandbox category): all but WithoutSystemd_SaysSo_AndLeavesNothingBehind.**
  - **What set-up recorded:** Both Sandbox runs (build and published) and all five isolated reruns failed the same way. obm service install exits 1: 'systemd isn't present or can't be reached for this user'. No product frame in the stack. Nothing else failed.
  - **Verdict:** this container. There's no systemd user manager here, so obm refuses as designed, and WithoutSystemd_SaysSo_AndLeavesNothingBehind passes. Each passes alone against a private systemd 255 and leaves nothing behind (RemovingObmd's race aside); CI's Sandbox step passed them at this commit (run 42541203860). Install_TakesOverAnEngineAlreadyRunning shows a product bug, not why it fails (finding 17).
- **ServiceTests.RemovingObmd_RemovesTheService, once a systemd user manager is there**
  - **What set-up recorded:** Set-up saw only the systemd refusal. Under a private systemd 255 it passed 9 of 9 with the build, but alone with OBM_TEST_PUBLISHED failed 7 of 12 times: 'EngineException: The engine closed the connection' from its StatusAsync helper (ServiceTests.cs:328-329).
  - **Verdict:** real problem in the test. Its helper treats only connect timeouts as 'no engine'. A poll connecting as the engine stops, after it correctly removed its service, gets EngineException, which escapes the loop; obm update's wait catches this. Warmed up, as in CI, it passed 8 of 8 (finding 75, low). AnUpdate_RestartsTheEngine (:108) shares the flaw.

## Looked at closely and fine

- Other users can't reach or impersonate the engine. Unix: run/ is 0700 and each end checks the other's uid (ran). Windows: the pipe's DACL and owner checks (read only). At most they can deny service.
- Secrets stay out of logs, command lines, child environments, crash reports and diagnostics: Secret prints `***`, no log template takes Reveal(), the Keychain gets them on stdin, and diagnostics omit secrets.json (finding 9's one character aside).
- FileStore keeps secrets.json private and whole: it writes a new 0600 file (protected ACL on Windows), flushes and renames it over, replacing any planted link, and refuses a broken one. Tested, except failure clean-up.
- Secret removal is scoped to one home: each store names items by the state folder's AppHome.InstanceKey, so neither obmd --remove-secrets nor migrate can touch another home's or another program's items.
- Migrate survives being stopped: it copies, reads back and switches the setting before removing. Ctrl+C in obm doesn't interrupt it, and a stop before the switch puts the target back (tested). Finding 8 aside.
- obm data remove deletes only what it should: without --home, only app-named folders, never one inside another, and symlinked folders are unlinked, not followed (ran on Linux). Findings 13 and 52 aside.
- obm data remove's guards hold: Enter, n or EOF removes nothing; with engine.lock held, which obmd takes first, nothing is deleted; it refuses without --yes outside a terminal. The first two ran but are untested.
- On Windows, data (%LocalAppData%\OpenBackupManager) and the app (%LocalAppData%\OpenBackupManager.App) are separate folders, so removing one never removes the other; uninstall removes only its own Run key, PATH and notification entries (checked by InstallTests on CI).
- obm update installs only a download whose SHA-256 matches GitHub's digest (tested; finding 14 aside), and only top-level regular files, each unpacked beside its target and renamed over it, so nothing lands outside its folder (ran).
- Neither updater goes backwards or is fooled into 'up to date': obm refuses anything not newer, Velopack forbids downgrades, and short of breaking TLS network attackers can only fail checks. Who can publish is finding 55.
- UpdateWatcher won't take an upgrade for a removal: it needs two matching checks 5 s apart, dpkg, rpm and obm update replace obmd by rename, and it deletes only the --service-file path (finding 39 aside).
- Release pipeline: no pull_request_target or workflow_run, forks get a read-only token and no secrets, tags reach shells only via env, actions and images are pinned (findings 23, 66 aside), and Get-Version.ps1 refuses lower or off-main tags.
- RcloneFetch's checks hold: all 13 tampered variants of rclone's v1.75.2 SHA256SUMS (changed hash, added line, unsigned, other key) were refused, the zip is hashed before opening, and one entry goes to a fixed path.
- Docker image: obmd runs as PID 1 under non-root user 1654, /app stays root-owned so the engine can't replace its own programs, and installed-by makes obm update and obm data remove refuse.
- Crash reports: only the header (version, OS, .NET), with nothing personal, stays unredacted; the issue link escapes everything, so report text can't redirect it or inject Markdown or HTML. Copy report skipping redaction is finding 7.
