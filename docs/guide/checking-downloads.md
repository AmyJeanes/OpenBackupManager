# Checking a download

Every download is attested by the project's build on GitHub: a signed record of which build made the file, and from which commit. To check a file came from there unchanged, install the [GitHub CLI](https://cli.github.com), sign in with `gh auth login`, and run `gh attestation verify` with the file. It names the commit and the build that made the file. If it fails, don't run the file.

=== "Windows"

    ```
    gh attestation verify OpenBackupManager.App-win-x64-Setup.exe --repo AmyJeanes/OpenBackupManager
    ```

    On an Arm PC, check `OpenBackupManager.App-win-arm64-Setup.exe` instead. The installed programs can be checked the same way, such as `obm.exe` in `%LocalAppData%\OpenBackupManager.App\current\bin`.

=== "macOS"

    ```
    gh attestation verify OpenBackupManager-<version>-osx-arm64.tar.gz --repo AmyJeanes/OpenBackupManager
    ```

    On an Intel Mac, check `OpenBackupManager-<version>-osx-x64.tar.gz` instead. The programs can be checked the same way once they're unpacked, such as `~/.local/bin/obm`.

=== "Linux"

    ```
    gh attestation verify OpenBackupManager-<version>-linux-x64.tar.gz --repo AmyJeanes/OpenBackupManager
    ```

    On an Arm computer, check `OpenBackupManager-<version>-linux-arm64.tar.gz` instead. The programs can be checked the same way once they're unpacked, such as `~/.local/bin/obm`.
