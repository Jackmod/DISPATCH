# DISPATCH — Handoff Notes

Pick-up notes for continuing this project (for a future dev / Claude session). As of
**v1.1.0**, 2026-07-20.

---

## What this is

A one-click installer + launcher for a GTA V **LSPDFR** police-mod setup. It turns the
infamous "40 mods, 30 config files" manual install into: download one `Setup.exe`, pick a
preset, install, and launch through RagePluginHook — with every keybind/setting editable
in-app and written back to the real `.ini` files.

**Stack:** .NET 8 (targets `net8.0-windows`), Avalonia UI (MVVM, CommunityToolkit.Mvvm),
Velopack (packaging + self-update). Build with .NET 10 SDK is fine. `Directory.Build.props`
enforces **`TreatWarningsAsErrors` + code-style analyzers** — keep builds at **0 warnings**.

---

## Repos (IMPORTANT — two of them, kept in sync)

| Repo | Visibility | Role |
|---|---|---|
| **`Jackmod/DISPATCHPRIV`** | private | primary source repo (this folder came from here) |
| **`Jackmod/DISPATCH`** | public | source mirror **+** hosts all releases **+** hosts the mod pack |

Both `main` branches are kept identical. Workflow used all session:
1. branch off `main`, commit, push to `DISPATCHPRIV`, open PR, `gh pr merge --squash --delete-branch`.
2. `git reset --hard origin/main`, then `git push public main:main` (fast-forward) — where
   remote `public` = `https://github.com/Jackmod/DISPATCH.git`.

`git` identity + `gh` are already authenticated on the owner's machine (account `Jackmod`).

---

## The distribution model (the key design — read this)

**Thin installer.** The shipped `Setup.exe` does **not** bundle the ~2 GB of mods. It carries
a small `remote-pack.json` (mod → download-URL index) and downloads only the mods the user
ticks, on demand.

- Mods are hosted as assets on the **`modpack-v1`** release of `Jackmod/DISPATCH`
  (73 archives, ~1.5 GB), plus `remote-pack.json` itself as an asset.
- `tools/Publish-ModPack.ps1 -Repo Jackmod/DISPATCH` uploads/updates the archives **and** the
  manifest (resumable, clobbers, BOM-free). Re-run it whenever a mod changes.
- **Live manifest:** on startup the app fetches the hosted `remote-pack.json` (see
  `RemotePackRefresher`) into a cache the loader prefers over the bundled copy. So **adding or
  renaming a mod reaches installed copies automatically** on next launch — no reinstall.
- **App self-update:** `VelopackAppUpdater` (in `Dispatch.App`) checks `Jackmod/DISPATCH`
  releases on startup and stages a newer version to apply on next close. Requires releases to
  carry the Velopack feed (`RELEASES` + `releases.win.json` + full `.nupkg`), which
  `vpk upload github` produces.

Net effect: **users never re-download the installer** — mods and app versions both flow
automatically. A `fat` build (bundles the whole pack) still exists via
`-p:IncludeModPack=true` (the default) but the thin build is what ships.

---

## Build / test / release

```bash
# Build (must be 0 warnings)
dotnet build Dispatch.slnx -c Release

# Full test suite (583 tests as of v1.1.0)
dotnet test Dispatch.slnx -c Release

# Thin installer build by hand
dotnet publish src/Dispatch.App/Dispatch.App.csproj -c Release -r win-x64 \
  --self-contained true -p:IncludeModPack=false -o publish-thin
vpk pack -u Dispatch -v <version> -p publish-thin -e Dispatch.exe -o releases

# Cut a real release (build + pack + publish WITH self-update feed):
./tools/Publish-App.ps1 -Version 1.2.0        # needs dotnet, vpk, gh (authed)

# Update the hosted mod pack (after changing modpack/ archives):
./tools/Publish-ModPack.ps1 -Repo Jackmod/DISPATCH
```

Tooling: `.NET SDK`, `vpk` (`dotnet tool install -g vpk`, v1.2.0 used), `gh` CLI (signed in).
The `modpack/` archives are gitignored — a fresh clone has an **empty pack**; the master copy
lives in `dispatch-modpack-backup.zip` (owner's Downloads) and on the `modpack-v1` release.

---

## What was built recently (v1.1.0 session)

1. **Thin installer + on-demand mod downloads** (`RemotePackSource`, `Acquisition/*`).
2. **Full config editing:** the scanner classifies every `.ini` key as keyboard-bind /
   controller-bind / plain-setting (folding `<bind>Modifier` companions), so one file feeds
   the keyboard tab, controller tab, and Plugin Settings. Controls screen reads real `.ini`s
   on open; edits write back in place (`IniScanner`, `ControlsViewModel`, `ControlWriter`).
3. **Launch via RagePluginHook** with real feedback + double-launch guard
   (`IGameLauncher` → `LaunchOutcome`, `DashboardViewModel.GoOnDuty`).
4. **Live manifest** (`RemotePackRefresher`) and **app self-update** (`VelopackAppUpdater`).

Design docs of intent live in the XML-doc comments — the code is heavily commented on *why*.

---

## Open items / decisions for next time

- **Grammar Police keybind: F4 vs F7 (unresolved — owner's call).** The guide's auto-config
  (`ConfigCatalogue`) writes GP Interface = **F4**, which is the RagePluginHook console key
  ("never rebind"). The app's `ControlCatalogue.Suggested` scheme resolves it to **F7** and a
  test enforces "Suggested never takes F4". Options: (A) change the auto-config to F7
  (clash-free, deviates from guide), (B) add a Controls-screen warning that flags any bind on
  F4, (C) leave as guide-faithful. Not changed because writing a bad GP bind unattended was
  riskier than the known caveat.
- **Code signing:** `Setup.exe` is unsigned → SmartScreen "unknown publisher" on first run.
  A cert would remove it.
- **Reserved-key (F4) warning** in Controls, and an **Epic-vs-Steam launch hint** — minor
  polish, not done.
- **CI:** `.github/workflows/ci.yml` builds/tests, and its `package` job builds from a plain
  checkout — so a CI-built release would have an **empty pack**. Real releases are cut locally
  with `Publish-App.ps1`. Wiring CI to fetch the pack (or use the thin model) is a follow-up.

---

## Gotchas learned

- **PowerShell 5.1** (Windows): non-ASCII chars (em-dash) in a `.ps1` mis-parse as ANSI →
  keep scripts ASCII. `Set-Content -Encoding utf8` adds a **BOM** — write manifests with
  `[System.IO.File]::WriteAllText(..., UTF8Encoding($false))`; the app strips a leading BOM too.
  Native-command stderr under `$ErrorActionPreference='Stop'` becomes terminating — the scripts
  use `'Continue'` + explicit `$LASTEXITCODE` checks.
- **`gh release upload`** globs file args — filenames with `[ ]` (e.g. `[SP ONLY]`) fail;
  `Publish-ModPack.ps1` sanitizes those. The runtime matcher normalizes names anyway.
- **GitHub 100 MB/file limit** → the pack can't live in the repo tree; it lives on a Release.
- **Velopack self-update** only works if the release carries the feed files — a bare
  `Setup.exe` is not enough. Use `vpk upload github` (what `Publish-App.ps1` does).
