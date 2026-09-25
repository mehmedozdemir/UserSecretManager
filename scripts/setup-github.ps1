<#
.SYNOPSIS
    Creates the GitHub repository (private by default), pushes main and tags, and applies the
    repository rules described in CONTRIBUTING.md.

.DESCRIPTION
    - Merge options: squash (feature/fix) and merge commit (release/hotfix); rebase merge disabled;
      head branches deleted after merge; squash commit title = PR title.
    - main protection: pull request required, required CI checks must pass and be up to date,
      no force-push, no deletion, conversations resolved.

    Requires the GitHub CLI and a login: gh auth login

.EXAMPLE
    ./scripts/setup-github.ps1 -Repository UserSecretManager
    ./scripts/setup-github.ps1 -Repository my-org/UserSecretManager -Public
    ./scripts/setup-github.ps1 -Repository UserSecretManager -SkipCreate   # only (re)apply rules
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Repository,

    [switch] $Public,

    [switch] $SkipCreate
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$root = Split-Path -Parent $PSScriptRoot

function Invoke-Native {
    param([string] $Command, [string[]] $Arguments, [string] $Stdin)
    if ($Stdin) { $Stdin | & $Command @Arguments } else { & $Command @Arguments }
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') başarısız oldu (çıkış kodu $LASTEXITCODE)."
    }
}

gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI oturumu yok. Önce: gh auth login'
}

if (-not $SkipCreate) {
    $visibility = if ($Public) { '--public' } else { '--private' }
    Invoke-Native gh @('repo', 'create', $Repository, $visibility, '--source', $root, '--remote', 'origin',
        '--description', 'Move appsettings secrets to dotnet user-secrets — Avalonia desktop app')
    Invoke-Native git @('-C', $root, 'push', '-u', 'origin', 'main')
    Invoke-Native git @('-C', $root, 'push', 'origin', '--tags')
}

$fullName = (gh repo view $Repository --json nameWithOwner --jq '.nameWithOwner')
if ($LASTEXITCODE -ne 0) { throw "Repo bulunamadı: $Repository" }

Invoke-Native gh @('api', '--method', 'PATCH', "repos/$fullName",
    '-F', 'allow_squash_merge=true',
    '-F', 'allow_merge_commit=true',
    '-F', 'allow_rebase_merge=false',
    '-F', 'delete_branch_on_merge=true',
    '-f', 'squash_merge_commit_title=PR_TITLE',
    '-f', 'squash_merge_commit_message=PR_BODY') | Out-Null

$protection = @{
    required_status_checks        = @{
        strict   = $true
        contexts = @(
            'build-test (windows-latest)',
            'build-test (ubuntu-latest)',
            'build-test (macos-latest)',
            'conventions'
        )
    }
    enforce_admins                = $false
    required_pull_request_reviews = @{ required_approving_review_count = 0; dismiss_stale_reviews = $true }
    restrictions                  = $null
    allow_force_pushes            = $false
    allow_deletions               = $false
    required_conversation_resolution = $true
} | ConvertTo-Json -Depth 5

Invoke-Native gh @('api', '--method', 'PUT', "repos/$fullName/branches/main/protection", '--input', '-') -Stdin $protection | Out-Null

Write-Host "Hazır: https://github.com/$fullName" -ForegroundColor Green
Write-Host '  - main korumalı: PR + yeşil CI zorunlu, force-push ve silme kapalı'
Write-Host '  - Merge: squash (feature/fix) ve merge commit (release/hotfix); dallar otomatik silinir'
Write-Host '  - Not: tek kişilik projede onay sayısı 0; ekip büyürse required_approving_review_count artırılmalı'
