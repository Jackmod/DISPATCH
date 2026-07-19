<#
.SYNOPSIS
    Copies loading-screen stills into Assets/Loading with the naming the
    slideshow expects.

.DESCRIPTION
    LoadingSlideshow shows images in filename order, so this renames whatever
    you point it at to 01..NN with a slug, drops them in the right folder, and
    reports what the build will pick up.

    Assets/** is an AvaloniaResource wildcard, so anything imported here is
    compiled into Dispatch.exe on the next build and ships inside a release.

.PARAMETER Source
    Folder holding the images to import. Defaults to your Downloads folder.

.PARAMETER Names
    Optional slugs, in order, used to label the copies. Extra files past the
    end of this list are numbered without a slug.

.EXAMPLE
    ./tools/Import-LoadingImages.ps1
    Imports every image in Downloads, in name order.

.EXAMPLE
    ./tools/Import-LoadingImages.ps1 -Source "$env:USERPROFILE\Pictures\gta"
#>

[CmdletBinding()]
param(
    [string]$Source = (Join-Path $env:USERPROFILE 'Downloads'),

    [string[]]$Names = @('patrol', 'tactical', 'backup', 'uniform', 'hero'),

    [switch]$Clear
)

$ErrorActionPreference = 'Stop'

$target = Join-Path $PSScriptRoot '..\src\Dispatch.UI\Assets\Loading'
$target = [System.IO.Path]::GetFullPath($target)

if (-not (Test-Path $Source)) {
    throw "Source folder not found: $Source"
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

if ($Clear) {
    Get-ChildItem $target -File |
        Where-Object { $_.Extension -match '^\.(jpg|jpeg|png|webp)$' } |
        Remove-Item -Force
    Write-Host "Cleared existing stills from Assets/Loading" -ForegroundColor Yellow
}

$images = Get-ChildItem $Source -File |
    Where-Object { $_.Extension -match '^\.(jpg|jpeg|png|webp)$' } |
    Sort-Object Name

if ($images.Count -eq 0) {
    Write-Warning "No .jpg, .jpeg, .png or .webp files found in $Source"
    Write-Host "Save the images there first, then run this again." -ForegroundColor Cyan
    return
}

Write-Host "Importing $($images.Count) image(s) into Assets/Loading" -ForegroundColor Cyan

$index = 1
foreach ($image in $images) {
    $slug = if ($index -le $Names.Count) { "-$($Names[$index - 1])" } else { '' }
    $name = '{0:00}{1}{2}' -f $index, $slug, $image.Extension.ToLowerInvariant()

    Copy-Item $image.FullName (Join-Path $target $name) -Force
    Write-Host ("  {0,-24} <- {1}" -f $name, $image.Name)
    $index++
}

Write-Host ""
Write-Host "Assets/Loading now contains:" -ForegroundColor Cyan
Get-ChildItem $target -File |
    Where-Object { $_.Extension -match '^\.(jpg|jpeg|png|webp)$' } |
    Sort-Object Name |
    ForEach-Object { "  {0,-24} {1,6} KB" -f $_.Name, [math]::Round($_.Length / 1KB) }

Write-Host ""
Write-Host "Rebuild to compile them into Dispatch.exe:" -ForegroundColor Green
Write-Host "  dotnet build Dispatch.slnx"
