<#
.SYNOPSIS
    Builds the thin DISPATCH installer and publishes it to a GitHub release WITH the
    Velopack update feed, so already-installed copies update themselves.

.DESCRIPTION
    Self-update only works when a release carries the whole Velopack feed (the
    RELEASES file and the full .nupkg), not just the Setup.exe. This does that:
    publish -> vpk pack -> vpk upload github. Existing installs then pick the new
    version up on their next launch; no one re-downloads the installer.

    Requires the .NET SDK, the vpk tool (dotnet tool install -g vpk), and the GitHub
    CLI signed in (gh auth login) so the release can be created.

.PARAMETER Version
    The new version, e.g. 1.1.0. Must be higher than the last published version.

.PARAMETER Repo
    owner/name of the public repo whose releases the app updates from.
    Defaults to Jackmod/DISPATCH (matches VelopackAppUpdater).

.EXAMPLE
    ./tools/Publish-App.ps1 -Version 1.1.0
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Repo = 'Jackmod/DISPATCH'
)

# Continue, not Stop: this is mostly native tools (dotnet, vpk, gh) whose stderr
# would otherwise be treated as terminating. Every step is gated on $LASTEXITCODE.
$ErrorActionPreference = 'Continue'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

function Write-Step($t) { Write-Host ''; Write-Host $t -ForegroundColor Cyan }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, got '$Version'" }

foreach ($tool in 'dotnet', 'vpk', 'gh') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is not on PATH. Install it and try again."
    }
}

gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "GitHub CLI is not signed in. Run 'gh auth login' first." }

$token = (gh auth token).Trim()
if (-not $token) { throw "Could not read a GitHub token from the CLI." }

# --- publish the thin app (no bundled pack; the pack is fetched on demand) --

Write-Step "Publishing thin build $Version"
Remove-Item -Recurse -Force "$repoRoot/publish-thin", "$repoRoot/releases" -ErrorAction SilentlyContinue
dotnet publish src/Dispatch.App/Dispatch.App.csproj -c Release -r win-x64 --self-contained true `
    -p:IncludeModPack=false -o publish-thin
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# --- package the Velopack release feed -------------------------------------

Write-Step "Packaging Velopack release $Version"
vpk pack -u Dispatch -v $Version -p publish-thin -e Dispatch.exe -o releases `
    --packTitle 'DISPATCH' --packAuthors 'DISPATCH'
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

# --- publish to GitHub with the full update feed ---------------------------

Write-Step "Uploading release v$Version to $Repo (with the update feed)"
vpk upload github --repoUrl "https://github.com/$Repo" --token $token --publish `
    --releaseName "DISPATCH v$Version" --tag "v$Version" -o releases
if ($LASTEXITCODE -ne 0) { throw "vpk upload github failed." }

Write-Host ''
Write-Host "Published DISPATCH v$Version to https://github.com/$Repo/releases/tag/v$Version" -ForegroundColor Green
Write-Host "Installed copies will update to it on their next launch." -ForegroundColor Gray
