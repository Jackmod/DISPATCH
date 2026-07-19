<#
.SYNOPSIS
    Build, test and run Dispatch without depending on the shell's PATH.

.DESCRIPTION
    A terminal opened before the .NET SDK was installed will not have dotnet on
    its PATH, because the process inherited its environment at launch and never
    re-reads it. Editors make this worse: VS Code hands every integrated
    terminal the environment it started with, so opening a new terminal panel
    does not help - the editor itself has to be restarted.

    This resolves dotnet by looking, rather than by hoping, and works from any
    shell regardless of when it was opened.

.PARAMETER Task
    build (default), test, run, watch, or clean.

.PARAMETER Configuration
    Debug (default) or Release.

.EXAMPLE
    ./tools/dispatch.ps1
    ./tools/dispatch.ps1 run
    ./tools/dispatch.ps1 test
#>

[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'run', 'watch', 'clean')]
    [string]$Task = 'build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

function Resolve-Dotnet {
    # Already on PATH: nothing to do.
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    # Refresh from the registry, which is where the installer actually wrote it.
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"

    $refreshed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($refreshed) { return $refreshed.Source }

    # Fall back to the standard install locations.
    $candidates = @(
        "$env:ProgramFiles\dotnet\dotnet.exe",
        "${env:ProgramFiles(x86)}\dotnet\dotnet.exe",
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $env:Path = "$(Split-Path $candidate);$env:Path"
            return $candidate
        }
    }

    throw @"
Could not find dotnet.

Install the SDK, then either restart your editor completely or run:
  winget install Microsoft.DotNet.SDK.10
"@
}

$dotnet = Resolve-Dotnet
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $root 'Dispatch.slnx'
$app = Join-Path $root 'src\Dispatch.App\Dispatch.App.csproj'

Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray
Write-Host "task:   $Task ($Configuration)" -ForegroundColor Cyan
Write-Host ""

switch ($Task) {
    'build' { & $dotnet build $solution -c $Configuration }
    'test'  { & $dotnet test  $solution -c $Configuration }
    'run'   { & $dotnet run --project $app -c $Configuration }
    'watch' { & $dotnet watch --project $app run }
    'clean' {
        & $dotnet clean $solution -c $Configuration
        Get-ChildItem $root -Include bin, obj -Recurse -Directory |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Removed all bin and obj folders." -ForegroundColor Green
    }
}

exit $LASTEXITCODE
