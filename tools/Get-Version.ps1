<#
.SYNOPSIS
Works out the version CI builds from the git tags.

.DESCRIPTION
For a tag such as v0.1.0 or v0.1.0-beta.1, that version, once it's checked as safe to release. Otherwise an edge build,
numbered as MinVer numbers untagged builds. See docs/design/windows-app.md#versions.

.EXAMPLE
./tools/Get-Version.ps1 -Tag v0.1.0
#>
param(
    # The tag being released. Left out, an edge build
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
# A lowercase label with its number after a dot, so they sort as people expect: beta.10 after beta.9, and Beta before alpha
$pattern = '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[a-z]+\.(0|[1-9]\d*))?$'

function Versions($tags) {
    $tags | Where-Object { $_ -cmatch $pattern } | ForEach-Object { [semver]$_.Substring(1) } | Sort-Object
}

# Otherwise the commit counts below only count what was fetched
if ((git rev-parse --is-shallow-repository) -eq 'true') {
    throw 'This needs the whole history, such as from actions/checkout with fetch-depth: 0'
}

if ($Tag) {
    if ($Tag -cnotmatch $pattern) {
        throw "$Tag isn't a version tag such as v0.1.0 or v0.1.0-beta.1"
    }

    # Installed copies only update to a higher version
    $version = [semver]$Tag.Substring(1)
    $highest = Versions (git tag --list 'v*') | Where-Object { $_ -ne $version } | Select-Object -Last 1
    if ($highest -and $version -le $highest) {
        throw "$Tag must be higher than v$highest"
    }

    git merge-base --is-ancestor HEAD origin/main
    if ($LASTEXITCODE) {
        throw "$Tag isn't on main"
    }

    return "$version"
}

# After the release it follows and before any that can come next, and in commit order with other edge builds
$last = Versions (git tag --merged HEAD --list 'v*') | Select-Object -Last 1
if (!$last) {
    return "0.0.1-alpha.0.$(git rev-list --count HEAD)"
}

$height = git rev-list --count "v$last..HEAD"
if ($last.PreReleaseLabel) {
    return "$last.$height"
}

return "$($last.Major).$($last.Minor).$($last.Patch + 1)-alpha.0.$height"
