<#
.SYNOPSIS
Packs obm and obmd, as Publish-App.ps1 published them for macOS or Linux, into the tarball releases offer for them.

.DESCRIPTION
Writes OpenBackupManager-<version>-<runtime>.tar.gz, with obm, obmd and LICENSE at its top level, so the programs can be
unpacked straight into a folder on PATH. It writes the archive itself rather than running tar, whose options differ
between macOS and Linux, so both come out alike: owned by root, with nothing from the build machine. CI's package-macos
and package-linux jobs pack with it.

.EXAMPLE
./tools/Pack-Tarball.ps1 -Publish publish -Runtime linux-x64 -Version 0.1.0
#>
param(
    # The folder Publish-App.ps1 published to
    [Parameter(Mandatory)]
    [string]$Publish,

    [Parameter(Mandatory)]
    [string]$Runtime,

    [string]$Version = '0.0.0',

    [string]$Output = '.'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$program = [IO.UnixFileMode]'UserRead, UserWrite, UserExecute, GroupRead, GroupExecute, OtherRead, OtherExecute'
$text = [IO.UnixFileMode]'UserRead, UserWrite, GroupRead, OtherRead'
$entries = @(
    @{ Name = 'obm'; Path = Join-Path $Publish 'bin/obm'; Mode = $program }
    @{ Name = 'obmd'; Path = Join-Path $Publish 'obmd'; Mode = $program }
    @{ Name = 'LICENSE'; Path = Join-Path $root 'LICENSE'; Mode = $text }
)

$tarball = Join-Path $Output "OpenBackupManager-$Version-$Runtime.tar.gz"
$file = [IO.File]::Create($tarball)
$gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionLevel]::SmallestSize)
$writer = [Formats.Tar.TarWriter]::new($gzip, [Formats.Tar.TarEntryFormat]::Ustar)
try {
    foreach ($entry in $entries) {
        $tarEntry = [Formats.Tar.UstarTarEntry]::new([Formats.Tar.TarEntryType]::RegularFile, $entry.Name)
        $tarEntry.Mode = $entry.Mode
        $tarEntry.ModificationTime = (Get-Item $entry.Path).LastWriteTimeUtc
        $tarEntry.DataStream = [IO.File]::OpenRead($entry.Path)
        try {
            $writer.WriteEntry($tarEntry)
        }
        finally {
            $tarEntry.DataStream.Dispose()
        }
    }
}
finally {
    $writer.Dispose()
    $gzip.Dispose()
    $file.Dispose()
}

$tarball
