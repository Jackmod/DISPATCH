# Loading screen stills

Images placed in this folder become the install screen's backdrop. They
crossfade every nine seconds with a slow push, in the style loading screens in
this genre established, with the phase readout and a tip over the top.

## How it works

`Dispatch.UI.csproj` includes `Assets\**` as `AvaloniaResource`, so **anything
dropped here is compiled into `Dispatch.exe`**. A downloaded release therefore
already has them — there is no folder to ship alongside the binary and nothing
for a user to copy.

Supported: `.jpg`, `.jpeg`, `.png`, `.webp`. They are shown in filename order,
so a numeric prefix controls the sequence.

Suggested naming, matching the preset tiers:

| File | Suits |
|---|---|
| `01-patrol.jpg` | A single marked unit, daylight, no drama — the Standard Issue register |
| `02-tactical.jpg` | An officer in full kit beside a cruiser — the Full Duty register |
| `03-backup.jpg` | Several officers deploying — the Realism register |
| `04-uniform.jpg` | A dress or state-trooper uniform — the realism layer |
| `05-hero.jpg` | The most cinematic frame you have; it carries the sequence |

Landscape, 1600px wide or larger. They are drawn `UniformToFill`, so anything
important should sit away from the edges, and the lower third is covered by the
readout.

## If this folder is empty

That is the state the repository ships in. `LoadingSlideshow` reports
`HasImages = false`, the install screen falls back to its vector scene, and
nothing looks broken or missing. Adding images is an enhancement, not a
requirement.

## Before you commit anything here

Dispatch deliberately bundles no game or mod artwork. Every graphic the project
ships is original vector work, because screenshots of GTA V belong to Rockstar
and screenshots of mods belong to their authors, and a public release
redistributes whatever is in this folder to everyone who downloads it.

Being non-commercial does not change that — it is one factor in a fair-use
test, not an exemption, and Take-Two has acted against non-commercial GTA
projects before, including OpenIV and the re3 decompilations.

Material that is safe to put here:

- Frames you captured yourself in your own game
- Artwork you made, or hold a licence to redistribute
- Anything released under a licence that permits redistribution

If you are unsure about an image, leave it out. The vector fallback is a
complete design, not a placeholder.
