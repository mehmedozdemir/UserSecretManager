<#
.SYNOPSIS
    Prepares a release: creates release/vX.Y.Z, bumps the version and moves [Unreleased] in CHANGELOG.md.

.EXAMPLE
    ./scripts/release.ps1 -Version 0.2.0
    ./scripts/release.ps1 -Version 0.2.1 -NoBranch   # on an existing hotfix branch
    ./scripts/release.ps1 -Version 0.2.0 -DryRun     # show what would change, touch nothing
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [switch] $NoBranch,

    [switch] $DryRun,

    [string] $Date = (Get-Date -Format 'yyyy-MM-dd')
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$root = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $root 'Directory.Build.props'
$changelogPath = Join-Path $root 'CHANGELOG.md'

function Invoke-Git {
    git -C $root @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $($args -join ' ') başarısız oldu (çıkış kodu $LASTEXITCODE)."
    }
}

if (git -C $root status --porcelain) {
    throw 'Çalışma alanında commit edilmemiş değişiklik var.'
}

if (git -C $root tag --list "v$Version") {
    throw "v$Version etiketi zaten var."
}

$props = [IO.File]::ReadAllText($propsPath)
if ($props -match "<Version>$([regex]::Escape($Version))</Version>") {
    throw "Directory.Build.props zaten $Version sürümünde."
}

$changelog = [IO.File]::ReadAllText($changelogPath)
$unreleased = [regex]::Match($changelog, '(?ms)^## \[Unreleased\][ \t]*\r?\n(?<body>.*?)(?=^## \[|\z)')
$body = $unreleased.Groups['body'].Value.Trim()
if (-not $unreleased.Success -or [string]::IsNullOrWhiteSpace($body)) {
    throw 'CHANGELOG.md [Unreleased] bölümü boş; yayınlanacak değişiklik yok.'
}

$branch = if ($NoBranch) { '(mevcut dal)' } else { "release/v$Version" }
if ($DryRun) {
    Write-Host "Sürüm : $Version ($Date)"
    Write-Host "Dal   : $branch"
    Write-Host "Taşınacak CHANGELOG kayıtları:`n$body"
    return
}

if (-not $NoBranch) {
    Invoke-Git switch -c "release/v$Version"
}

$props = [regex]::Replace($props, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($propsPath, $props)

$replacement = "## [Unreleased]`n`n## [$Version] - $Date`n`n$body`n`n"
$changelog = $changelog.Substring(0, $unreleased.Index) + $replacement + $changelog.Substring($unreleased.Index + $unreleased.Length)
[IO.File]::WriteAllText($changelogPath, $changelog.TrimEnd() + "`n")

Invoke-Git add $propsPath $changelogPath
Invoke-Git commit -m "chore(release): prepare v$Version"

Write-Host ''
Write-Host "Hazır: v$Version. Sonraki adımlar:" -ForegroundColor Green
Write-Host '  1. PR açın ve CI yeşil olunca main''e merge commit ile birleştirin'
Write-Host '  2. git switch main; git pull'
Write-Host "  3. git tag -a v$Version -m 'Release v$Version'; git push origin v$Version"
