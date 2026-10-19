# Checking a download

Every download is attested by the project's build on GitHub: a signed record of which build made the file, and from which commit. To check a file came from there unchanged, install the [GitHub CLI](https://cli.github.com), sign in with `gh auth login`, and run:

```
gh attestation verify OpenBackupManager.App-win-x64-Setup.exe --repo AmyJeanes/OpenBackupManager
```

It names the commit and the build that made the file. If it fails, don't run the file.

The installed programs can be checked the same way, such as `obm.exe` in `%LocalAppData%\OpenBackupManager.App\current\bin`.
