<#
.SYNOPSIS
    Builds the distributable packages of the current version.

.DESCRIPTION
    Windows : Velopack installer (per-user, no admin) + portable zip + update feed (nupkg, releases.win.json).
    Linux   : self-contained single-file executable (tar.gz).
    macOS   : self-contained single-file executable for Apple Silicon (tar.gz).
    All     : SHA256SUMS.txt.

    With -Upload the files are attached to the GitHub release vX.Y.Z (it must exist), which also serves as the
    update source of installed copies.

.EXAMPLE
    ./scripts/package.ps1
    ./scripts/package.ps1 -Upload
#>
[CmdletBinding()]
param(
    [string] $Version,

    [switch] $Upload,

    [string] $Repository = 'mehmedozdemir/UserSecretManager'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src/UserSecretManager.App'
$icon = Join-Path $app 'Assets/app.ico'

function Invoke-Native {
    param([string] $Command, [string[]] $Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') başarısız oldu (çıkış kodu $LASTEXITCODE)."
    }
}

if (-not $Version) {
    $props = [IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props'))
    $Version = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
}

$out = Join-Path $root "artifacts/$Version"
$publish = Join-Path $out 'publish'
$velopack = Join-Path $out 'velopack'
$dist = Join-Path $out 'dist'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $publish, $velopack, $dist | Out-Null

Invoke-Native dotnet @('tool', 'restore', '--tool-manifest', (Join-Path $root '.config/dotnet-tools.json'))

# Windows: Velopack packs a folder, so publish without single-file.
$winDir = Join-Path $publish 'win-x64'
Invoke-Native dotnet @('publish', $app, '-c', 'Release', '-r', 'win-x64', '--self-contained', '-o', $winDir, "-p:Version=$Version")

if ($Upload) {
    # Previous release enables delta updates; the first Velopack release has none, which is fine.
    & dotnet vpk download github --repoUrl "https://github.com/$Repository" --outputDir $velopack
}

Invoke-Native dotnet @('vpk', 'pack',
    # Velopack installs to %LocalAppData%\<packId> and deletes that folder on uninstall. The app keeps its data in
    # %LocalAppData%\UserSecretManager, so the package id must differ or uninstalling would wipe profiles and backups.
    '--packId', 'UserSecretManager.Desktop',
    '--packVersion', $Version,
    '--packDir', $winDir,
    '--mainExe', 'UserSecretManager.exe',
    '--packTitle', 'User Secret Manager',
    '--packAuthors', 'Mehmet Özdemir',
    '--icon', $icon,
    '--outputDir', $velopack)

# Only this version's packages and the feed files; skip full packages downloaded from earlier releases.
Get-ChildItem $velopack -File |
    Where-Object { $_.Name -like "*$Version*" -or $_.Name -like 'releases.*.json' -or $_.Name -like 'assets.*.json' -or
                   $_.Name -like 'RELEASES*' -or $_.Name -like '*-Setup.exe' -or $_.Name -like '*-Portable.zip' } |
    ForEach-Object { Copy-Item $_.FullName $dist }

# GNU tar (bundled with Git) can store the executable bit; Windows' bsdtar cannot.
$gnuTar = @("$env:ProgramFiles\Git\usr\bin\tar.exe", "${env:ProgramFiles(x86)}\Git\usr\bin\tar.exe") |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $gnuTar -and $env:OS -eq 'Windows_NT') {
    Write-Warning 'GNU tar bulunamadı; Linux/macOS arşivlerinde çalıştırma izni olmayacak (kullanıcı chmod +x yapmalı).'
}

# Linux and macOS: portable single-file executables.
foreach ($rid in 'linux-x64', 'osx-arm64') {
    $dir = Join-Path $publish $rid
    Invoke-Native dotnet @('publish', $app, '-c', 'Release', '-r', $rid, '--self-contained', '-o', $dir,
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', "-p:Version=$Version")
    $archive = Join-Path $dist "UserSecretManager-v$Version-$rid.tar.gz"
    if ($gnuTar) {
        Invoke-Native $gnuTar @('--force-local', '--mode=755', '-czf', $archive, '-C', $dir, 'UserSecretManager')
    }
    else {
        Invoke-Native tar @('-czf', $archive, '-C', $dir, 'UserSecretManager')
    }
}

$sums = Get-ChildItem $dist -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $sums)

Write-Host ''
Write-Host "Paketler hazır: $dist" -ForegroundColor Green
Get-ChildItem $dist -File | ForEach-Object { Write-Host ("  {0,-55} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }

if ($Upload) {
    $files = Get-ChildItem $dist -File | ForEach-Object { $_.FullName }
    Invoke-Native gh (@('release', 'upload', "v$Version", '--repo', $Repository, '--clobber') + $files)
    Write-Host "v$Version sürümüne yüklendi." -ForegroundColor Green
}
