<#
.SYNOPSIS
    Prepares a release: creates release/vX.Y.Z, bumps the version and moves [Unreleased] in CHANGELOG.md.

.EXAMPLE
    ./scripts/release.ps1 -Version 0.2.0
    ./scripts/release.ps1 -Version 0.2.1 -NoBranch   # on an existing hotfix branch
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [switch] $NoBranch,

    [string] $Date = (Get-Date -Format 'yyyy-MM-dd')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $root 'Directory.Build.props'
$changelogPath = Join-Path $root 'CHANGELOG.md'

if (git -C $root status --porcelain) {
    throw 'Çalışma alanında commit edilmemiş değişiklik var.'
}

if (git -C $root tag --list "v$Version") {
    throw "v$Version etiketi zaten var."
}

$changelog = [IO.File]::ReadAllText($changelogPath)
$unreleased = [regex]::Match($changelog, '(?s)## \[Unreleased\]\s*(?<body>.*?)(?=\n## \[|\z)')
if (-not $unreleased.Success -or [string]::IsNullOrWhiteSpace($unreleased.Groups['body'].Value)) {
    throw 'CHANGELOG.md [Unreleased] bölümü boş; yayınlanacak değişiklik yok.'
}

if (-not $NoBranch) {
    git -C $root switch -c "release/v$Version"
}

$props = [IO.File]::ReadAllText($propsPath)
$props = [regex]::Replace($props, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($propsPath, $props)

$body = $unreleased.Groups['body'].Value.TrimEnd()
$replacement = "## [Unreleased]`n`n## [$Version] - $Date`n`n$body`n"
$changelog = $changelog.Substring(0, $unreleased.Index) + $replacement + $changelog.Substring($unreleased.Index + $unreleased.Length)
[IO.File]::WriteAllText($changelogPath, $changelog)

git -C $root add $propsPath $changelogPath
git -C $root commit -m "chore(release): prepare v$Version"

Write-Host ""
Write-Host "Hazır: v$Version. Sonraki adımlar:" -ForegroundColor Green
Write-Host "  1. PR açın ve CI yeşil olunca main'e merge commit ile birleştirin"
Write-Host "  2. git switch main; git pull"
Write-Host "  3. git tag -a v$Version -m 'Release v$Version'; git push origin v$Version"
