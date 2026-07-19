# Dispatch

**Everything you need. Nothing you have to figure out.**

A Windows desktop app that installs a complete LSPDFR police-mod setup into GTA V
Legacy in one unattended run, then stays on as the launcher and control panel for
that setup.

---

## Status

Early. The solution skeleton is up: four source projects, three test projects,
dependency injection, Serilog, and a window that opens. See
[Build order](#build-order) for what is done and what is next.

## Requirements

- **.NET SDK 10** (builds the `net8.0` target) — or the .NET 8 SDK
- **Windows 10 1809** or later to run; Core and UI build and test on macOS

```
dotnet build Dispatch.slnx
dotnet test  Dispatch.slnx
dotnet run   --project src/Dispatch.App
```

### If `dotnet` is not recognised

A terminal opened *before* the SDK was installed will not have it on PATH — the
process captured its environment at launch and never re-reads it. Editors make
this worse: VS Code hands every integrated terminal the environment it started
with, so opening a new terminal panel does not help. **Restart the editor
completely.**

To fix the terminal you already have:

```powershell
$env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
            [Environment]::GetEnvironmentVariable("Path","User")
```

Or skip the problem entirely — this resolves `dotnet` by looking for it rather
than relying on PATH, and works from any shell:

```powershell
./tools/dispatch.ps1          # build
./tools/dispatch.ps1 run
./tools/dispatch.ps1 test
./tools/dispatch.ps1 clean
```

### Loading screen images

`src/Dispatch.UI/Assets/Loading/` is empty in the repository and the install
screen falls back to a vector scene, so nothing is missing without it. To add
stills, put them in a folder and run:

```powershell
./tools/Import-LoadingImages.ps1 -Source "$env:USERPROFILE\Downloads"
```

They are numbered in order and copied in. Because `Assets/**` is an
`AvaloniaResource` wildcard they compile into `Dispatch.exe` on the next build,
so a release carries them with no companion files. Read
`src/Dispatch.UI/Assets/Loading/README.md` before committing anything there.

## Layout

| Project | Purpose |
|---|---|
| `src/Dispatch.Core` | The domain. No UI reference, no Windows API — builds on macOS |
| `src/Dispatch.Platform.Windows` | Everything P/Invoke or Windows-only, behind Core interfaces |
| `src/Dispatch.UI` | Avalonia: theme, controls, views, view models |
| `src/Dispatch.App` | Composition root and the shipped executable |
| `tests/Dispatch.Core.Tests` | Unit tests |
| `tests/Dispatch.UI.Tests` | Headless Avalonia tests |
| `tests/Dispatch.E2E.Tests` | Full install against a committed fixture game folder |

`Dispatch.Core` references no UI assembly and no Windows-only API. Anything
platform-specific is an interface in Core and an implementation in
`Dispatch.Platform.Windows`. ViewModels orchestrate Core services and never touch
the filesystem directly. Code-behind is for control wiring, nothing else.

## Decisions worth not reversing by accident

**Avalonia 11.3.18, not 12.x.** `Avalonia.Svg.Skia` has no stable 12.x release and
Avalonia does not keep binary compatibility across majors. The SVG illustration
pipeline is a requirement, so the framework follows the package that gates it.
Revisit when Svg.Skia ships a 12.x stable.

**FluentAssertions 7.2.2, not 8.x.** Version 8 moved to a paid Xceed licence for
commercial use. 7.2.2 is the last Apache-2.0 release.

**`Dispatch.App` targets `net8.0-windows10.0.17763.0`.** It is the Windows
executable and references the Windows platform project, so it is Windows-only by
construction. The portability requirement applies to `Dispatch.Core` and
`Dispatch.UI`, which target plain `net8.0` and are what build and test on macOS.
Windows 10 1809 is the floor because it is the oldest build the WebView2 evergreen
runtime supports.

**`Program.Main` lives in `Dispatch.App`, not `Dispatch.UI`.** The spec sketches
`Program.cs` under the UI project, but the composition root is the one place
allowed to know about Core, UI and the platform layer together, and that is the
executable. `App.axaml` stays in `Dispatch.UI` as specced.

**Solution file is `Dispatch.slnx`.** The .NET 10 SDK emits the XML solution
format by default. It is supported by the CLI, Visual Studio 17.13+ and Rider.

**Package versions are centralised** in `Directory.Packages.props`. Project files
reference packages without a `Version`.

## Build order

1. ✅ Solution skeleton — four projects, DI container, Serilog, a window that opens
2. ⬜ Theme — `Palette`, `Typography`, `Spacing`, `Motion`, a ControlTheme per control, and a gallery page kept in the solution as a dev tool
3. ⬜ Core model — `ModCatalogue`, `GameAction`, `KeyToken`, `ControlProfile`, `OfficerProfile`, `ProfileStore`, fully unit tested
4. ⬜ Intro and shell
5. ⬜ Wizard — all six screens against mock data
6. ⬜ `KeyboardMap`, `ControllerMap`, `ConflictDetector`, the controls screen
7. ⬜ `GameLocator`, `VersionReader`, `CompatibilityChecker`
8. ⬜ `FolderCleaner`, with its complete test suite before it is wired to any UI
9. ⬜ Resilience — `RunJournal`, `StagingArea`, `PreflightCheck`, `RetryPolicy`, `ErrorCatalogue`
10. ⬜ `IniDocument` and `IniWriter`, comment-preserving, with diffing
11. ⬜ Acquisition — GitHub releases, direct HTTP, then the WebView2 source
12. ⬜ `InstallRunner`, `FilePlacer`, `OpenIvBridge`, `DefenderService`
13. ⬜ Recovery screen and completion report
14. ⬜ Launcher — dashboard, mods, settings
15. ⬜ `GameLogReader`, `IntegrityAuditor`, `DependencyInstaller`
16. ⬜ E2E suite, Velopack packaging, self-update

## Where Dispatch writes

Everything lives under `%LOCALAPPDATA%\Dispatch`, resolved by `IAppPaths` and
never composed by hand elsewhere:

| Path | Contents |
|---|---|
| `profile.json` | Officer identity, control profiles, tuning, appearance |
| `install-record.json` | What was installed, when, against which build, with hashes |
| `runs/*.jsonl` | Run journals — the resume and rollback mechanism |
| `backups/` | Per-run backups of anything overwritten |
| `quarantine/` | Where the cleaner moves files instead of deleting them |
| `archives/` | Downloaded mod archives, kept so a repair needs no network |
| `logs/` | Rolling Serilog output, 10 days retained |

Staging is deliberately **not** under that root: extraction goes to
`%TEMP%\Dispatch\staging\<run-id>\<mod>\`, is validated there, and is only then
moved into the game folder file by file.

## Running it the first time

Dispatch is unsigned, so SmartScreen will show a full-screen warning on first
launch. Choose **More info → Run anyway**. This will be replaced with a signed
binary if a certificate becomes available.

## Licence

Fonts bundled under `src/Dispatch.UI/Assets/Fonts` are redistributed under the SIL
Open Font License; see the licence file alongside them. No game art, mod art or
other third-party artwork is bundled — every graphic in the app is original vector
work drawn for this project, and every mod is fetched from its own author's server
at install time rather than rehosted.
