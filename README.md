<div align="center">

<br>

<img src="docs/media/wordmark.svg" alt="DISPATCH" width="460">

### Everything you need. Nothing you have to figure out.

**A complete LSPDFR police setup for GTA V â€” installed in one unattended run,<br>then managed from the same app.**

<br>

[![Build](https://img.shields.io/github/actions/workflow/status/Jackmod/dispatch/ci.yml?branch=main&style=for-the-badge&labelColor=0B1220&color=E8B44A&label=BUILD)](../../actions)
[![Tests](https://img.shields.io/badge/TESTS-556%20passing-3ECF8E?style=for-the-badge&labelColor=0B1220)](../../actions)
[![Edition](https://img.shields.io/badge/GTA%20V-LEGACY%20ONLY-3ECF8E?style=for-the-badge&labelColor=0B1220)](#)
[![.NET](https://img.shields.io/badge/.NET-8.0-4C8DFF?style=for-the-badge&labelColor=0B1220)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/WINDOWS-10%2F11-93A6C4?style=for-the-badge&labelColor=0B1220)](#)

<br>

</div>

---

## The problem

Installing LSPDFR properly means **forty mods from thirty different sites**, files
dragged into precise folders, and **thirty config files hand-edited**. Any single
typo breaks it silently.

Nothing tells you which step went wrong. That's why people give up â€” not
difficulty, just forty small things failing quietly.

## What Dispatch does

<table>
<tr>
<td width="33%" valign="top">

### Collects
Every mod, from its **own author's server**, through your own session. Nothing
rehosted, nothing bundled, nothing stale.

</td>
<td width="33%" valign="top">

### Places
Every file where it belongs, staged and hash-verified **before** anything enters
your game folder.

</td>
<td width="33%" valign="top">

### Configures
Thirty config files written **in place** â€” comments, ordering and formatting
preserved. Never regenerated.

</td>
</tr>
</table>

Then it stays on as the launcher and control panel for the setup it built.

---

## Status

> [!IMPORTANT]
> **GTA V Legacy only.** LSPDFR, RagePluginHook and Script Hook V do not run on
> the 2025 Enhanced edition. Dispatch detects the edition from the executable and
> refuses to install onto Enhanced, so the whole flow is Legacy end to end.

> [!NOTE]
> The install is **real and wired**: acquisition feeds the placement engine, which
> backs up, journals, hash-verifies and can roll back to a byte-identical state.
> `--offline` performs a complete real install sourced entirely from the bundled
> pack with no network; `--demo` runs a write-nothing simulation. Every file
> placement in the pack is verified by running the real engine over each archive.
> The one thing only you can confirm is that the mods *load* — that needs a real
> game launch.

| Stage | | |
|---|---|---|
| Solution, DI, logging | ✅ | Four projects, Serilog, warnings-as-errors |
| Design system | ✅ | Palette, type scale, motion, 30 hand-drawn icons, gallery |
| Intro animation | ✅ | Siren-length, unskippable, plays every launch |
| Wizard | ✅ | Full-bleed screens, Legacy-aware, end to end |
| Install run | ✅ | Real placement, phases, live log, elapsed/remaining, report |
| Profiles + persistence | ✅ | Atomic writes, schema migration, corrupt-file quarantine |
| Key translation | ✅ | Per-mod dialects, both directions |
| Conflict detection | ✅ | Modifier layers, per-device, free-key suggestions |
| Config engine | ✅ | Comment-preserving ini, byte-exact round trips |
| Controls screen | ✅ | Keyboard map, capture, conflict resolution, staged diffs |
| Launcher | ✅ | Nav rail, dashboard, command palette, empty states |
| Game detection | ✅ | Steam VDF, Epic manifests, version + edition reader |
| Folder cleaner | ✅ | Install-record aware, staged modal, quarantine with byte-identical restore |
| Resilience layer | ✅ | Journal, staging, preflight, retry, error catalogue |
| Install engine | ✅ | Catalogue-driven placement, backup, reversible rollback |
| Acquisition | ✅ | Bundled pack, GitHub releases, direct HTTP; offline mode |
| Mod pack | ✅ | ~70 mods across three tiers, every placement verified by the real engine |
| OpenIV hand-off | ✅ | .oiv / DLC / map mods set aside to an import folder with a walkthrough |
| Audit + log translation | ✅ | Integrity, compatibility, plain-English log findings |
| In-game verified | ⬜ | Only a real launch confirms the mods load — that part is on you |

---

## Run it

```powershell
git clone https://github.com/Jackmod/dispatch.git
cd dispatch
./tools/dispatch.ps1 run
```

Requires the **.NET 8 or 10 SDK**. The helper script finds `dotnet` even when
your shell's PATH is stale, closes any running instance before building, and
takes `build`, `test`, `run`, `demo` (write-nothing simulation), `offline` (real
install from the bundled pack, no network), `watch` or `clean`.

> [!TIP]
> **On Command Prompt (`cmd.exe`) rather than PowerShell?** The `./tools/dispatch.ps1`
> line is PowerShell syntax — `cmd` reports `'.' is not recognized`. Run it through
> PowerShell instead:
>
> ```
> powershell -ExecutionPolicy Bypass -File tools\dispatch.ps1 run
> ```
>
> Or skip the helper script entirely — this works from **either** shell when the
> .NET SDK is on your PATH:
>
> ```
> dotnet run --project src/Dispatch.App/Dispatch.App.csproj
> ```

<details>
<summary><b>If <code>dotnet</code> isn't recognised</b></summary>

<br>

A terminal opened *before* the SDK was installed never re-reads its
environment. VS Code makes it worse â€” every integrated terminal inherits the
environment the editor launched with, so opening a new panel doesn't help.
**Restart the editor.** Or fix the shell you're in:

```powershell
$env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
            [Environment]::GetEnvironmentVariable("Path","User")
```

Or just use `./tools/dispatch.ps1`, which resolves `dotnet` by looking for it.

</details>

<details>
<summary><b>Keyboard shortcuts while developing</b></summary>

<br>

| Key | Does |
|---|---|
| `F12` | Toggle the design-system gallery |
| `Ctrl` `â†’` | Step the wizard forward, ignoring validation |
| `Ctrl` `â†` | Step backward |

Debug builds only. Nothing in the shipped navigation reaches them. The intro is
deliberately unskippable — it plays in full, timed to the siren, every launch.

</details>

---

## Design

Midnight navy, cool blue for structure, **warm gold for anything that demands
action**. The gold is the deliberate risk: it reads as badge metal against the
navy and makes every call to action unmistakable, which is what lets the whole
app avoid the red-and-blue lightbar clichÃ©.

That clichÃ© appears in exactly one place â€” the intro â€” because there it *is*
the point. Nothing says police faster; anywhere else it would be wallpaper
inside a day.

```
Ink       #060A12   Â·   Navy      #111B2E   Â·   Steel     #24365A
Blue      #4C8DFF   Â·   Gold      #E8B44A   Â·   Green     #3ECF8E
```

**Archivo Narrow** for display, **Inter** for body, **IBM Plex Mono** for paths
and versions â€” bundled, so it renders identically everywhere rather than
falling back to Segoe UI.

Every graphic is original vector: a seven-point badge, a lightbar that doubles
as the progress motif, an abstract Los Santos street grid. Thirty icons on a
20px grid at 1.5px stroke, drawn for this project so the set looks like one
hand made it.

<details>
<summary><b>Accessibility isn't a later pass</b></summary>

<br>

- Body and muted text are **asserted** at 4.5:1 against the card surface by a test
- Gold focus rings drawn *outside* the fill, so they survive on the gold button
- Reduced motion collapses every duration to zero **and** switches ambient loops
  off â€” an infinite animation at zero duration is a busy loop, not a still image
- Status is never carried by colour alone; every pill states its meaning in words

</details>

---

## Architecture

```
src/
â”œâ”€â”€ Dispatch.Core/              the domain â€” no UI, no Windows API, builds on macOS
â”œâ”€â”€ Dispatch.Platform.Windows/  everything P/Invoke, behind Core interfaces
â”œâ”€â”€ Dispatch.UI/                Avalonia: theme, controls, views, view models
â””â”€â”€ Dispatch.App/               composition root, the shipped executable
tests/
â”œâ”€â”€ Dispatch.Core.Tests/        unit
â”œâ”€â”€ Dispatch.UI.Tests/          headless Avalonia
â””â”€â”€ Dispatch.E2E.Tests/         against a fixture game folder
```

`Dispatch.Core` references no UI assembly and no Windows-only API. Anything
platform-specific is an interface in Core and an implementation in
`Dispatch.Platform.Windows`. ViewModels orchestrate Core services and never
touch the filesystem. Code-behind is control wiring, nothing else.

---

## Decisions worth not reversing by accident

<details>
<summary><b>Avalonia 11.3, not 12.x</b></summary>

`Avalonia.Svg.Skia` has no stable 12.x release and Avalonia doesn't keep binary
compatibility across majors. The SVG pipeline is a requirement, so the
framework follows the package that gates it.

</details>

<details>
<summary><b>FluentAssertions 7.2.2, not 8.x</b></summary>

Version 8 moved to a paid Xceed licence for commercial use. 7.2.2 is the last
Apache-2.0 release.

</details>

<details>
<summary><b>The real runner is the default; the simulation names itself</b></summary>

`RealInstallRunner` is now the registered `IInstallRunner`. The write-nothing
runner is `SimulatedInstallRunner` — never `InstallRunner`, because a class with
that name silently doing nothing would be a genuinely dangerous thing to leave
lying in this codebase. `--demo` swaps the simulation back in via one container
registration; `--offline` keeps the real runner but sources every mod from the
bundled pack instead of the network.

</details>

<details>
<summary><b>Three Avalonia traps, recorded so they aren't rediscovered</b></summary>

Each compiles fine and fails only at runtime:

1. **Keyframes cannot animate `RenderTransform`** â€” no animator is registered
   and the style throws as it attaches. Per-transform properties work;
   transitions are a separate path that handles it normally.
2. **A `ControlTheme` may not contain descendant selectors**, only `/template/`
   ones. Controls that build children in code are reachable by neither, so
   their styling belongs in an application-level Styles file.
3. **Fluent's `ListBoxItem` paints its own `ContentPresenter` background** on
   hover and selection. Setting `Background` on the item does nothing â€” the row
   fills solid with the accent and any glass beneath it vanishes.

</details>

---

## Loading screen images

`src/Dispatch.UI/Assets/Loading/` is compiled into the executable by an
`AvaloniaResource` wildcard, so whatever is there ships inside a release.

```powershell
./tools/Import-LoadingImages.ps1 -Source "$env:USERPROFILE\Downloads"
```

With the folder empty the install screen falls back to a vector scene and
nothing looks missing â€” images are an enhancement, not a dependency.

> [!WARNING]
> Screenshots of GTA V belong to Rockstar and screenshots of mods belong to
> their authors. A public release redistributes whatever is in that folder to
> everyone who downloads it, and non-commercial use is one factor in a fair-use
> test rather than an exemption. See the folder's README before adding anything
> you didn't capture yourself.

---

## Testing

```powershell
./tools/dispatch.ps1 test
```

556 tests. The ones worth knowing about:

- **Font resolution** â€” each type style is asserted to land on the *genuinely
  drawn* weight. Avalonia resolves an unmatched weight to the nearest one
  silently, so a missing file would otherwise render slightly wrong forever
- **Contrast** â€” WCAG ratios computed and asserted, not eyeballed
- **Reduced motion** â€” asserts durations actually reach zero. The first
  implementation looked correct and changed nothing; only the test caught it
- **Radio phrasing** â€” `10 ADAM 24` must speak as *one zero adam two four*, not
  the "twenty-four" a synthesiser produces on its own

---

## Licence

Code under [MIT](LICENSE).

Bundled fonts are SIL OFL 1.1 with their licences alongside them. No game or
mod artwork is bundled by this repository â€” see the note above if you add any.

Dispatch is an unofficial fan project. Not affiliated with Rockstar Games,
Take-Two, or the authors of any mod it installs.

<div align="center">
<br>
<sub><i>Left Shift starts a traffic stop.<br>Left Shift also starts most conversations you'll regret.</i></sub>
<br><br>
</div>
