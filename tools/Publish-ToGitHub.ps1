<#
.SYNOPSIS
    Creates the GitHub repository and pushes Dispatch to it.

.DESCRIPTION
    Uses the GitHub CLI when it is available and authenticated. When it is not,
    prints the exact manual steps instead of failing, because repository
    creation needs an interactive browser sign-in that cannot be automated.

.PARAMETER Name
    Repository name. Defaults to "dispatch".

.PARAMETER Private
    Create it private. Default is public.

.EXAMPLE
    ./tools/Publish-ToGitHub.ps1
    ./tools/Publish-ToGitHub.ps1 -Name dispatch-lspdfr -Private
#>

[CmdletBinding()]
param(
    [string]$Name = 'dispatch',
    [switch]$Private
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

function Write-Step($text) { Write-Host "`n$text" -ForegroundColor Cyan }

# --- sanity ---------------------------------------------------------------

$branch = git rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') {
    Write-Step "Renaming branch '$branch' to 'main'"
    git branch -M main
}

$dirty = git status --porcelain
if ($dirty) {
    Write-Warning "Working tree has uncommitted changes:"
    git status --short
    Write-Host "`nCommit or stash them first." -ForegroundColor Yellow
    return
}

$visibility = if ($Private) { 'private' } else { 'public' }

# --- automated path -------------------------------------------------------

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    $authed = $false
    try {
        gh auth status *> $null
        $authed = $LASTEXITCODE -eq 0
    }
    catch { $authed = $false }

    if (-not $authed) {
        Write-Step "GitHub CLI is installed but not signed in. Run this once:"
        Write-Host "  gh auth login" -ForegroundColor Green
        Write-Host "`nThen run this script again."
        return
    }

    Write-Step "Creating $visibility repository '$Name' and pushing"
    gh repo create $Name --$visibility --source . --remote origin --push

    Write-Step "Pushing tags"
    git push origin --tags

    $url = gh repo view --json url --jq .url
    Write-Host "`nDone: $url" -ForegroundColor Green
    Write-Host "The v0.1.0 tag will trigger the packaging job and attach a build to Releases."
    return
}

# --- manual path ----------------------------------------------------------

Write-Step "GitHub CLI is not installed, so the repository has to be created by hand."
Write-Host @"

Two options.

  A. Install the CLI, then re-run this script:

       winget install GitHub.cli
       gh auth login
       ./tools/Publish-ToGitHub.ps1

     The winget step opens a UAC prompt, which is why it cannot be done for you.

  B. Create the repository in a browser, then run three commands:

       1. Go to https://github.com/new
       2. Name it '$Name', set it $visibility
       3. Create it EMPTY - no README, no .gitignore, no licence.
          This repository already has all three, and letting GitHub add its
          own means the first push is rejected as a non-fast-forward.

     Then:

"@ -ForegroundColor Gray

$user = git config user.email
$handle = if ($user -match '\+?([A-Za-z0-9-]+)@users\.noreply\.github\.com') { $Matches[1] } else { '<your-username>' }

Write-Host "       git remote add origin https://github.com/$handle/$Name.git" -ForegroundColor Green
Write-Host "       git push -u origin main" -ForegroundColor Green
Write-Host "       git push origin --tags" -ForegroundColor Green
Write-Host @"

The tag push is what triggers the packaging job, which builds a self-contained
win-x64 executable and attaches it to a GitHub Release.
"@ -ForegroundColor Gray
