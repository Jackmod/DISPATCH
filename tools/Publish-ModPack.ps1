<#
.SYNOPSIS
    Hosts the mod pack on a GitHub Release and writes the remote-pack.json the
    thin installer downloads from.

.DESCRIPTION
    Uploads every archive in the local mod pack to a GitHub Release as an asset,
    then reads the assets back and writes remote-pack.json - the (file name to
    direct URL) index the app uses to fetch each SELECTED mod on demand. Run it
    once after building the pack, and again whenever you add or replace an archive.

    Requires the GitHub CLI, signed in (gh auth login). Re-running is safe: assets
    are clobbered, not duplicated, and the manifest is regenerated from scratch.

.PARAMETER Repo
    The target repository as "owner/name". Required.

.PARAMETER Tag
    The release tag that holds the pack assets. Default "modpack-v1".

.PARAMETER ModPackDir
    The local pack to upload. Defaults to the repo's modpack folder.

.PARAMETER ManifestOut
    Where to write the index. Defaults to src/Dispatch.App/remote-pack.json.

.EXAMPLE
    gh auth login
    ./tools/Publish-ModPack.ps1 -Repo Jackmod/DISPATCH
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Repo,
    [string]$Tag = 'modpack-v1',
    [string]$ModPackDir,
    [string]$ManifestOut
)

# Continue, not Stop: this script is mostly native gh calls, and in Windows
# PowerShell a native command writing to stderr under 'Stop' becomes a terminating
# error even when its exit code is 0. Every gh step is gated on $LASTEXITCODE
# instead, which is the reliable signal.
$ErrorActionPreference = 'Continue'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $ModPackDir)  { $ModPackDir  = Join-Path $repoRoot 'modpack' }
if (-not $ManifestOut) { $ManifestOut = Join-Path $repoRoot 'src/Dispatch.App/remote-pack.json' }

function Write-Step($t) { Write-Host ''; Write-Host $t -ForegroundColor Cyan }

# --- preflight ------------------------------------------------------------

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    Write-Host 'GitHub CLI (gh) is not installed. Install it, then re-run:' -ForegroundColor Yellow
    Write-Host '  winget install GitHub.cli' -ForegroundColor Green
    Write-Host '  gh auth login' -ForegroundColor Green
    return
}

gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'GitHub CLI is not signed in. Run "gh auth login", then re-run this script.' -ForegroundColor Yellow
    return
}

if (-not (Test-Path $ModPackDir)) { throw "Mod pack folder not found: $ModPackDir" }

# Unique archives by file name - the pack repeats a shared mod across preset
# folders, but a release can hold each asset name once.
$archives = Get-ChildItem -Path $ModPackDir -Recurse -File |
    Where-Object { $_.Extension -in '.zip', '.rar', '.7z' } |
    Group-Object Name | ForEach-Object { $_.Group[0] }

if (-not $archives) { throw "No .zip/.rar/.7z archives found under $ModPackDir" }
$totalMb = [math]::Round(($archives | Measure-Object Length -Sum).Sum / 1MB)
Write-Step ('Found {0} unique archive(s), {1:N0} MB total' -f $archives.Count, $totalMb)

# --- ensure the release exists -------------------------------------------

gh release view $Tag --repo $Repo *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Step "Creating release '$Tag' on $Repo"
    $notes = 'Archives hosted for the DISPATCH thin installer. Managed by tools/Publish-ModPack.ps1.'
    gh release create $Tag --repo $Repo --title "DISPATCH mod pack ($Tag)" --notes $notes
    if ($LASTEXITCODE -ne 0) { throw "Failed to create release '$Tag' on $Repo" }
}
else {
    Write-Step "Release '$Tag' already exists on $Repo - uploading into it"
}

# --- upload ---------------------------------------------------------------

# What is already hosted, so a resumed run skips files that are fully uploaded -
# uploads are slow and re-sending a 300 MB archive that is already there is waste.
$existing = @{}
$existingJson = gh release view $Tag --repo $Repo --json assets
if ($LASTEXITCODE -eq 0) {
    foreach ($asset in ($existingJson | ConvertFrom-Json).assets) { $existing[$asset.name] = $asset.size }
}

# Glob-safe staging: gh globs its file argument, so [ ] in a name break the upload
# ("no matches found"). Upload a copy under a safe name for those; the runtime
# matcher normalises names, so a renamed asset still maps to the right mod.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) 'dispatch-upload'
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$i = 0
foreach ($a in $archives) {
    $i++
    $safeName = $a.Name -replace '[\[\]]', '_'

    if ($existing.ContainsKey($safeName) -and $existing[$safeName] -eq $a.Length) {
        Write-Host ('  [{0}/{1}] {2} - already hosted, skipping' -f $i, $archives.Count, $safeName)
        continue
    }

    if ($safeName -ne $a.Name) {
        $uploadPath = Join-Path $stage $safeName
        Copy-Item -LiteralPath $a.FullName -Destination $uploadPath -Force
    }
    else {
        $uploadPath = $a.FullName
    }

    Write-Host ('  [{0}/{1}] {2}' -f $i, $archives.Count, $safeName)
    gh release upload $Tag $uploadPath --repo $Repo --clobber
    if ($LASTEXITCODE -ne 0) { throw "Upload failed for $($a.Name)" }
}

Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue

# --- regenerate the manifest from what is actually hosted -----------------

Write-Step 'Reading back hosted assets and writing the manifest'
$assetsJson = gh release view $Tag --repo $Repo --json assets
if ($LASTEXITCODE -ne 0) { throw "Could not read assets from release '$Tag'" }
$assets = ($assetsJson | ConvertFrom-Json).assets

$entries = @($assets |
    Where-Object { [System.IO.Path]::GetExtension($_.name) -in '.zip', '.rar', '.7z' } |
    ForEach-Object { [ordered]@{ file = $_.name; url = $_.url } })

# ConvertTo-Json unwraps a single-element array (and emits nothing for an empty
# one), so force a JSON array shape for those edge cases. The real run has many.
$json = $entries | ConvertTo-Json -Depth 4
if ($entries.Count -eq 0) { $json = '[]' }
elseif ($entries.Count -eq 1) { $json = '[' + $json + ']' }
# UTF-8 without a BOM: Set-Content -Encoding utf8 adds one in Windows PowerShell,
# and the leading U+FEFF trips a strict JSON reader when the app fetches it live.
[System.IO.File]::WriteAllText($ManifestOut, $json, (New-Object System.Text.UTF8Encoding($false)))

# Host the manifest alongside the mods, so an already-installed thin installer can
# fetch the current mod list (including mods added or renamed since it was built)
# without being rebuilt. This must be named remote-pack.json to match the app's
# AcquisitionOptions.DefaultManifestUrl.
gh release upload $Tag $ManifestOut --repo $Repo --clobber
if ($LASTEXITCODE -ne 0) { throw "Failed to upload the manifest asset to release '$Tag'" }

Write-Host ''
Write-Host "Wrote $($entries.Count) entrie(s) to $ManifestOut and hosted it on release '$Tag'" -ForegroundColor Green
Write-Host 'Now build the thin installer:' -ForegroundColor Gray
Write-Host '  dotnet publish src/Dispatch.App/Dispatch.App.csproj -c Release -r win-x64 --self-contained true -p:IncludeModPack=false -o publish-thin' -ForegroundColor Green
Write-Host '  vpk pack -u Dispatch -v 1.0.0 -p publish-thin -e Dispatch.exe -o releases' -ForegroundColor Green
