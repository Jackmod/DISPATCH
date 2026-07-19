<div align="center">

<br>

<img src="docs/media/wordmark.svg" alt="DISPATCH" width="460">

### Everything you need. Nothing you have to figure out.

**A complete LSPDFR police setup for GTA V — installed in one unattended run,<br>then managed from the same app.**

<br>

[![Build](https://img.shields.io/github/actions/workflow/status/Jackmod/dispatch/ci.yml?branch=main&style=for-the-badge&labelColor=0B1220&color=E8B44A&label=BUILD)](../../actions)
[![Tests](https://img.shields.io/badge/TESTS-90%20passing-3ECF8E?style=for-the-badge&labelColor=0B1220)](../../actions)
[![.NET](https://img.shields.io/badge/.NET-8.0-4C8DFF?style=for-the-badge&labelColor=0B1220)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/WINDOWS-10%2F11-93A6C4?style=for-the-badge&labelColor=0B1220)](#)

<br>

</div>

---

## The problem

Installing LSPDFR properly means **forty mods from thirty different sites**, files
dragged into precise folders, and **thirty config files hand-edited**. Any single
typo breaks it silently.

Nothing tells you which step went wrong. That's why people give up — not
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
Thirty config files written **in place** — comments, ordering and formatting
preserved. Never regenerated.

</td>
</tr>
</table>

Then it stays on as the launcher and control panel for the setup it built.

---

## Status

> [!IMPORTANT]
> **Dispatch does not write to your game folder yet.** The install runs as a
> full simulation — every phase, every progress update, the complete report —
> against a clock rather than a filesystem. That is deliberate: the spec puts
> the resilience layer (journal, staging, rollback, preflight) *before* any
> file-writing code, and that layer isn't built. It is safe to run today, and
> it will not touch anything.

| Stage | | |
|---|---|---|
| Solution, DI, logging | ✅ | Four projects, Serilog, warnings-as-errors |
| Design system | ✅ | Palette, type scale, motion, 30 hand-drawn icons, gallery |
| Intro animation | ✅ | Scan sweep, wordmark draw-on, patrol lights |
| Wizard | ✅ | Six full-bleed screens, end to end |
| Install simulation | ✅ | Seven phases, live log, completion report |
| Core model | 🚧 | Catalogue, profiles, persistence |
| Controls screen | ⬜ | Keyboard map, conflict detection |
| Folder cleaner | ⬜ | Allowlist, quarantine, dry-run |
| Resilience layer | ⬜ | Journal, staging, preflight, retry |
| Acquisition | ⬜ | GitHub, direct HTTP, embedded browser |
| Real installer | ⬜ | Placement, OpenIV, Defender |

---

## Run it

```powershell
git clone https://github.com/Jackmod/dispatch.git
cd dispatch
./tools/dispatch.ps1 run
```

Requires the **.NET 8 or 10 SDK**. The helper script finds `dotnet` even when
your shell's PATH is stale, closes any running instance before building, and
takes `build`, `test`, `run`, `watch` or `clean`.

<details>
<summary><b>If <code>dotnet</code> isn't recognised</b></summary>

<br>

A terminal opened *before* the SDK was installed never re-reads its
environment. VS Code makes it worse — every integrated terminal inherits the
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
| `Ctrl` `→` | Step the wizard forward, ignoring validation |
| `Ctrl` `←` | Step backward |
| any key | Skip the intro |

Debug builds only. Nothing in the shipped navigation reaches them.

</details>

---

## Design

Midnight navy, cool blue for structure, **warm gold for anything that demands
action**. The gold is the deliberate risk: it reads as badge metal against the
navy and makes every call to action unmistakable, which is what lets the whole
app avoid the red-and-blue lightbar cliché.

That cliché appears in exactly one place — the intro — because there it *is*
the point. Nothing says police faster; anywhere else it would be wallpaper
inside a day.

```
Ink       #060A12   ·   Navy      #111B2E   ·   Steel     #24365A
Blue      #4C8DFF   ·   Gold      #E8B44A   ·   Green     #3ECF8E
```

**Archivo Narrow** for display, **Inter** for body, **IBM Plex Mono** for paths
and versions — bundled, so it renders identically everywhere rather than
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
  off — an infinite animation at zero duration is a busy loop, not a still image
- Status is never carried by colour alone; every pill states its meaning in words

</details>

---

## Architecture

```
src/
├── Dispatch.Core/              the domain — no UI, no Windows API, builds on macOS
├── Dispatch.Platform.Windows/  everything P/Invoke, behind Core interfaces
├── Dispatch.UI/                Avalonia: theme, controls, views, view models
└── Dispatch.App/               composition root, the shipped executable
tests/
├── Dispatch.Core.Tests/        unit
├── Dispatch.UI.Tests/          headless Avalonia
└── Dispatch.E2E.Tests/         against a fixture game folder
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
<summary><b>The install is simulated, and the class says so</b></summary>

`SimulatedInstallRunner`, not `InstallRunner`. A class with the latter name
that silently did nothing would be a genuinely dangerous thing to leave lying
in this codebase. Swapping in the real implementation is a container
registration, not a UI change.

</details>

<details>
<summary><b>Three Avalonia traps, recorded so they aren't rediscovered</b></summary>

Each compiles fine and fails only at runtime:

1. **Keyframes cannot animate `RenderTransform`** — no animator is registered
   and the style throws as it attaches. Per-transform properties work;
   transitions are a separate path that handles it normally.
2. **A `ControlTheme` may not contain descendant selectors**, only `/template/`
   ones. Controls that build children in code are reachable by neither, so
   their styling belongs in an application-level Styles file.
3. **Fluent's `ListBoxItem` paints its own `ContentPresenter` background** on
   hover and selection. Setting `Background` on the item does nothing — the row
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
nothing looks missing — images are an enhancement, not a dependency.

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

90 tests. The ones worth knowing about:

- **Font resolution** — each type style is asserted to land on the *genuinely
  drawn* weight. Avalonia resolves an unmatched weight to the nearest one
  silently, so a missing file would otherwise render slightly wrong forever
- **Contrast** — WCAG ratios computed and asserted, not eyeballed
- **Reduced motion** — asserts durations actually reach zero. The first
  implementation looked correct and changed nothing; only the test caught it
- **Radio phrasing** — `10 ADAM 24` must speak as *one zero adam two four*, not
  the "twenty-four" a synthesiser produces on its own

---

## Licence

Code under [MIT](LICENSE).

Bundled fonts are SIL OFL 1.1 with their licences alongside them. No game or
mod artwork is bundled by this repository — see the note above if you add any.

Dispatch is an unofficial fan project. Not affiliated with Rockstar Games,
Take-Two, or the authors of any mod it installs.

<div align="center">
<br>
<sub><i>Left Shift starts a traffic stop.<br>Left Shift also starts most conversations you'll regret.</i></sub>
<br><br>
</div>
