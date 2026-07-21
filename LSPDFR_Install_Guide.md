# LSPDFR Complete Installation Guide

**For GTA V Legacy (the original PC version) — Steam and Epic.**

Every file goes into your **main game directory** unless the step says otherwise. Where the tutorial backtracked and fixed something later, the correction is already applied here — you won't have to redo anything.

**Before you start:** set aside two to three hours. Roughly forty mods, thirty config files. Nothing here is difficult, but a single typo in an ini file breaks things silently, so work carefully rather than quickly.

---

# PHASE 1 — Preparation

You need **two clean copies of GTA V.** One to mod, one untouched as a fallback. If your only copy is already modded, uninstall and reinstall before going further.

### Find your game directory

**Steam:** File Explorer → This PC → your drive → `Program Files (x86)` → `Steam` → `steamapps` → `common` → **Grand Theft Auto V**

**Epic:** File Explorer → This PC → your drive → `Epic Games` → **GTA V**

### Make the backup

1. Go back so you can see the **folder itself**, not its contents
2. Right-click it → **Copy**
3. Left-click empty space below → right-click → **Paste**

This takes a while. Let it finish.

### Pin it

Right-click your main (to-be-modded) game folder → **Pin to Quick Access**.

From now on you can right-click File Explorer in the taskbar and open it in one click. You'll be going back to it constantly.

---

# PHASE 2 — Base mod downloads

Put everything on your Desktop so it's easy to find.

| Mod | Notes |
|---|---|
| **LSPDFR** | Download → Agree and Download → **Manual Install only.** Never the auto setup |
| **Simple Trainer** | |
| **Resource Adjuster** | Reduces texture loss |
| **RAGENativeUI** | Download `RAGENativeUI.zip` |
| **Script Hook V** | Must match your GTA build |
| **Script Hook V .NET** | |
| **WinRAR** | Pick your language and **64-bit** |

> **The auto setup is the single most common way people end up starting over.** It doesn't lay the directory out the way the rest of this guide expects. Manual install, every time.

Install WinRAR before continuing.

---

# PHASE 3 — Base mod installation

Open your main game directory. Keep it open for the rest of the guide.

### LSPDFR
Open the archive. Select **everything except**:
- the License file
- the RPH readme

Drag the rest into the **game root**.

This includes an up-to-date RagePluginHook, so you don't download that separately.

### Simple Trainer
`TrainerV.asi` + `TrainerV.ini` → **root**

### Resource Adjuster
Open the folder inside. `ResourceAdjuster.asi` + `ResourceAdjuster.ini` → **root**

### RAGENativeUI
`RAGENativeUI.dll` only → **root**

### Script Hook V
Open the `bin` folder. `dinput8.dll` + `ScriptHookV.dll` → **root**

### Script Hook V .NET
Three files, no others:
- `ScriptHookVDotNet.asi`
- `ScriptHookVDotNet2.dll`
- `ScriptHookVDotNet3.dll`

→ **root**

> Refresh the directory (F5) after each drop. It re-sorts the folder so you can see what landed.

---

# PHASE 4 — LSPDFR key configuration

Open `lspdfr` → `Keys` → the config file.

Highlight the word `None` somewhere in the file and press **Ctrl+C**. You'll paste it repeatedly.

| Setting | Change to |
|---|---|
| Pursuit Menu **Controller** Key | `None` |
| Crime Report **Controller** Key | `None` |
| Stop Peds Key | `I` |
| Perform Arrest Key | `I` |
| Chase Abort Join Key | `None` |
| Chase Abort Join **Controller** Key | `None` |
| Traffic Stop Start **Controller** Key | `None` |
| Traffic Stop Interact Key | `I` |
| Traffic Stop Interact **Controller** Key | `None` |
| Toggle Police Computer **Controller** Key | `None` |
| Backup Menu Key | `None` |
| Backup Menu **Controller** Key | `None` |

**File → Save.**

**Why these specifically:**
- **Leave Traffic Stop Start on Left Shift.** It's the community standard and everything else assumes it.
- **Chase Abort Join** gives up `G` because Stop The Ped needs it.
- **Police Computer on controller** shares `X` with the in-vehicle weapon switch — leave it bound and your computer opens every time you change weapons.
- **Backup Menu on `B`** collides with Simple HUD later. Clearing it now saves a trip back.

---

# PHASE 5 — LSPDFR settings

Open `lspdfr` → **LSPDFR Configuration Setting**.

| Setting | Change to | Why |
|---|---|---|
| Main Preload Models | `false` | Major texture-loss fix |
| Ambient Disable Escape Suspect Counter | `true` | |
| Chase Disable Camera Focus | `true` | Stops the camera yanking toward a passing suspect mid-chase |
| Ambient Disabled Player Flashlight Override | `true` | Hands the flashlight to Stop The Ped |

**File → Save.**

---

# PHASE 6 — RagePluginHook setup

1. In the game root, find `RagePluginHook.exe` → right-click → **Pin to taskbar**
   *(Windows 11: right-click → Show more options → Pin to taskbar)*
2. Close the directory and launch it from the taskbar
3. **Accept**
4. **Plugin Timeout Threshold** → `60000`
5. **Never change the Console Key.** It stays `F4`.
6. **Plugins** → **Load These Plugins on Startup** → check **LSPD First Response**
7. Back to **Load All Plugins on Startup**
8. **Game Settings** → **Backup Game Version** → **Backup Current Version**
9. **Save and Launch**

> Step 8 is worth doing. When Rockstar patches the game and Script Hook V hasn't caught up, that backup lets you roll the game back and keep playing.

**Epic Games users** launch differently: exit RagePluginHook, start GTA V from the Epic launcher, and click RagePluginHook from the taskbar as the loading screen comes up. Too early and the game crashes; too late and it won't hook. If it opens windowed, press **Alt+Enter**.

At the platform screen, choose **Story Mode with RagePluginHook**.

---

# PHASE 7 — First launch and character creation

**If LSPDFR isn't in the pause menu:** press **F4**, type `reloadall`, press **Tab**, press **Enter**.

1. Pause → **LSPDFR** → **Character** → **Nearest Police Station**
2. Walk to the door → **E** (or D-pad right)
3. **Go On Duty** → **Yes** → **OK**

### Mugshot

- **Heritage** — pick a mother and father. Resemblance fully toward one parent reads better than a blend; skin tone around halfway.
- **Features** — set all eleven to **Standard**. Faster and it looks fine.
- **Appearance** — hair, eyebrows, facial hair, eye colour, blemishes.
- **Backspace** → **Save and Continue** → type your officer's name → **Enter** → click your name to select it.

### Police Locker

Agency **LSPD** → **Outfit** → press right once.

Sunglasses: **Advanced Customization** → **Props** → **Prop 1**.

**Backspace** three times → **Confirm**.

### Police Garage

> **This part matters for ELS.** The vapid police cruiser is the only vanilla car ELS can drive, and only with both extras removed.

1. Agency → **LSPD** (not LSSD)
2. Select the **Police Cruiser** (vapid)
3. **Components** → turn **both extras OFF**
4. **Backspace** → **Modifications** → **Apply All**
5. **Backspace** → **Select and Continue**

You're on duty.

---

# PHASE 8 — Simple Trainer setup

**F3** to open. Controller: right bumper, then **X**.

**Navigation — Num Lock must be on or nothing works:**
`8` up · `2` down · `4` left · `6` right · `5` select · `0` back

### Options
- **Infinite Stamina** → enable
- Press `6` for the right column → **Show Fort Zancudo on Map** → enable
- **Reveal Full Map** → enable
- **Save Settings to TrainerV.ini** *(select it twice to confirm)*

Also in here for later: **Clean Player Clothes**, **Max Health**, **Max Armor**.

### Menu colours
**Set Menu Colors/Font** → `6`/`4` cycles colours.
Menu colour **cyan**, highlight colour **lawn green** — high contrast, easy to read against the game.

Back → **Save Settings**.

### Vehicle options
- **Vehicle God Mode** → enable
- **Vehicle God Mode Settings** → **Auto Clean** → enable
- Back, then `6` for the right column:
  - **Vehicle Boost** → enable
  - **Infinite Boost** → enable
  - **Engine Power Multiplier** → enable
  - **Set Engine Power Multiplier** → **70**

Don't enable Nitro — it behaves oddly.

Back → Options → **Save Settings**.

### Weather
**Extra Sunny** is the practical default. Snow doesn't work on this version regardless of what the menu offers.

### Weapons

> **Never select "Get All Weapons."** It crashes the game most of the time.

1. **Remove All Weapons** first — start clean
2. **Unlimited Ammo** → enable
3. Add, one at a time:
   - Parachute
   - Night Stick
   - Flashlight
   - Pistol *(service)*
   - Combat Pistol *(backup)*
   - **Stun Gun** — the plain one, **not** the MP version. The MP one recharges far more slowly.
   - Press `6` for the right column:
   - Pump Shotgun
   - Carbine Rifle
4. **Weapons Attachments and Tint Menu** → for each weapon in your wheel, enable the **Flashlight**
5. **Weapons Load Save Menu** → **Save All Weapons to Slot 1**
6. Options → **Save Settings**

> **Every session from now on:** F3 → Weapons → Weapons Load Save Menu → **Load All Weapons Slot 1**. Your loadout does not persist on its own.

---

# PHASE 9 — TrainerV.ini

Back to the desktop. Open `TrainerV.ini` in the game root.

| Setting | Change to |
|---|---|
| Spawn A Driver Key | `0` |
| Add Waypoint Key | `0` |

**File → Save.**

Both default to keys you'll be using constantly, and one of them spawns hostile vehicles behind you.

---

# PHASE 10 — Plugin downloads, batch 1

> **JoJo's sites** — Speed Radar Lite, Compulite, Stop The Ped, Ultimate Backup — open redirect pages when you click download. Close the popup, click the arrow again, repeat. It takes several tries. It's ad-gating, not malware.

1. Skin Control
2. Speed Radar Lite *(JoJo)*
3. Automatic Siren Cutout *(Rich)* — main file
4. ELS — main file
5. Better ELS Reflections *(Matthewski)* — main file
6. Callout Interface *(Opus49)* — main file
7. MDT Textures for Callout Interface
8. Clear The Way V
9. Compulite *(JoJo)*
10. Realistic Usable Charges and Citations
11. Custom Environmental Lighting for ELS
12. Custom Pullover — main file
13. Deadly Weapons
14. Fast Draw — main file
15. RPH Delete Vehicle — main file
16. **OpenIV**
17. LIAR radar gun *(Opus49)* — main file
18. Clipboard and Notepad *(Echo)* — main file
19. LemonUI

Always take the **main file**, never an optional or beta variant.

---

# PHASE 11 — OpenIV setup

### Install

Run the installer → Continue → accept terms → Continue.

You **cannot change the install path** — it goes on C: regardless.

- ✅ Create Desktop Icon
- ❌ Run OpenIV After Installation *(uncheck this)*

Continue → Yes → Close.

Drag the desktop shortcut to your **taskbar**, then delete the desktop copy.

### Point it at your game

1. Open OpenIV → **Windows for Grand Theft Auto V**
2. **Browse** → navigate to your game folder → click it **once** → **Select Folder**
3. You should get a green confirmation that it found `gta5.exe` → **Continue**

### Configure

1. **Tools** → **Options** → **General** → Default Work Mode: `Read Only` → **`Edit`** → Close
   *Now you're in edit mode automatically every time.*
2. Click **Edit Mode** (blue banner appears)
3. **ASI Manager** → **OpenIV.ASI** → **Install** *(both boxes checked)* → Yes
4. Also install **OpenCamera** → Close

A `mods` folder now exists, currently empty.

### Build the mods folder

1. Close OpenIV, open your game directory
2. Hold **Ctrl** and select both the **`update`** folder and the **`x64`** folder
3. Right-click → **Copy**
4. Open the **`mods`** folder → right-click → **Paste**

**This takes several minutes.** Let it run. It gives OpenIV a safe layer to edit so you're never touching original game archives.

---

# PHASE 12 — Plugin installation, batch 1

Keep the game root open. Refresh after each drop.

### Skin Control
`.ini` + `.asi` → **root**

### Speed Radar Lite
Open the folder → `plugins` folder → **root**

### Automatic Siren Cutout
GTA5 folder → `plugins` folder + `InputManager.dll` → **root**

### ELS
`Installation Files` → `Grand Theft Auto 5` → **everything** → **root**

### Better ELS Reflections
Folder → folder → **Brighter** → **"Brighter with Higher Range and Brighter Takedowns"** *(the middle option)* → `els.ini` → **root**, replace.

### Callout Interface
GTA5 folder → select everything → **hold Ctrl and deselect `RAGENativeUI.dll`** → **root**

*You already have RAGENativeUI in the root. The one in here is older.*

### MDT Textures
Folder → GTA5 folder → `plugins` → **root**, replace files.

### Clear The Way V
This one goes **inside** the plugins folder, not the root.

Open `plugins` in your game directory → drag `ClearTheWayV.dll` in there.

### Compulite
Compulite folder → `plugins` → **root**

### Realistic Usable Charges and Citations
Navigate to `plugins` → `lspdfr` → **`Compulite`** folder.

Drop in the **citations** file and replace. The charges file is optional — take it if you want a different charge list than stock.

### Custom Environmental Lighting
**First create the scripts folder.** In the game root, right-click empty space → **New** → **Folder** → name it exactly **`scripts`** (lowercase).

Then `CustomEnvironmentalLighting.dll` → **scripts**

### Custom Pullover
`plugins` → **root**

### Deadly Weapons
Deadly Weapons folder → everything **except the readme** → **root**

### Fast Draw
Fast Draw folder → GTA5 folder → drag the whole `scripts` folder → **root**. It merges with the one you just made.

### RPH Delete Vehicle
`plugins` → **root**

### LIAR radar gun
LIAR folder → GTA5 folder → select everything → **deselect `RAGENativeUI.dll`** → drag `IPCommon.dll` and the `plugins` folder → **root**, replace.

**Then the textures, via OpenIV:**

1. Open OpenIV, edit mode on
2. Navigate: `mods` → `update` → `x64` → `dlcpacks` → `patchday8ng` → `dlc.rpf` → `x64` → `models` → `cdimages` → **`weapons.rpf`**
3. In the archive, highlight one of the Vintage Pistol filenames → click again to select the text → right-click → Copy
4. Paste into OpenIV's **search box** to jump straight to it
5. Drag the **Vintage Pistol files** from the archive into OpenIV — not the readme
6. **Clear the search box**
7. **File** → **Close All Archives** → close OpenIV

### Clipboard and Notepad
Clipboard folder → `plugins` → **root**

### LemonUI
`SHVDN3` folder → **`LemonUI.SHVDN3.dll`** only → **scripts** folder

*If one's already there, another mod supplied it. Fine either way.*

---

# PHASE 13 — Config files, batch 1

### `SkinControl.ini` — game root

Hotkey reads `119`. Put your cursor after the `9` and backspace until only `9` remains. That binds it to **Tab** and frees F8.

**File → Save.**

### RPH Delete Vehicle — `plugins`

| Setting | Change to |
|---|---|
| Delete Key | `D` |

Leave the Shift modifier. Result: **Left Shift + D**.

### Clipboard — `plugins/lspdfr`

| Setting | Change to |
|---|---|
| Clipboard Key | `T` |
| Notepad Key | `Y` |
| Clipboard Modifier Key | `Left Control` |

Notepad's modifier is already Left Control. Result: **Left Control + T** clipboard, **Left Control + Y** notepad.

### Compulite — `plugins/lspdfr`

| Setting | Change to |
|---|---|
| Open Computer Key | `X` |
| Give Citation Key | `X` |
| Give Citation Modifier Key | `Left Shift` |
| Open Computer **Controller** Button | `None` |
| Court case waiting duration | `24` |
| Pause game when opening | `No` |

Result: **hold X** for the computer, **Left Shift + X** to issue a citation. Not pausing keeps the world moving while you work.

### LIAR radar gun — `plugins/lspdfr`

| Setting | Change to |
|---|---|
| LIAR Key | `1` *(numpad)* |
| Position X | `612` |
| Position Y | `755` |
| Scale | `71` |
| Volume | `5` |
| HUD Colour | `1` |

Positions the readout clear of your postal code and status text. `1` is a red HUD, `0` is green.

### Callout Interface — `plugins/lspdfr`

The longest one. Work down carefully.

| Setting | Change to |
|---|---|
| Callout Menu Key | `F10` |
| Toggle Terminal Key | `NumPad7` |
| Toggle ALPR Key | `D` |
| Hold Interval | `300` |
| **MDT Call Sign** | your callsign, **ALL CAPS** |
| MDT Position X | `1353` |
| MDT Position Y | `698` |
| MDT Scale | `91` |
| MDT Timeout | `15` |
| MDT Sound Display | `4` |
| *(line below it)* | `5` |
| Postal Code Enabled | `True` |
| Postal Code Set | `virus_City` |
| Postal Code Position X | `318` |
| Postal Code Position Y | `17` |
| Postal Code Scale | `47` |
| Plate Enabled | `True` |
| Plate Position X | `495` |
| Plate Position Y | `907` |
| Plate Scale | `60` |
| All Auto Tab entries | `True` |
| Auto Blip | `True` |
| Blip Enabled | `True` |

**Callsign format:** first number **1–10**, name from the phonetic set, second number **1–24**. Capital T on every `True`.

> Phonetic names: ADAM, BOY, CHARLES, DAVID, EDWARD, FRANK, GEORGE, HENRY, IDA, JOHN, KING, LINCOLN, MARY, NORA, OCEAN, PAUL, QUEEN, ROBERT, SAM, TOM, UNION, VICTOR, WILLIAM, XRAY, YOUNG, ZEBRA
>
> So `1 ADAM 7`, `3 LINCOLN 22`, and so on. **Use the same callsign in Grammar Police later** — they have to match.

### Speed Radar Lite — `plugins/lspdfr`

| Setting | Change to |
|---|---|
| Increase Threshold Key | `I` |
| Decrease Threshold Key | `O` |
| Threshold Modifier Key | `Left Shift` |
| Initial Speed Threshold | `55` |

### Fast Draw — `scripts`

| Setting | Change to |
|---|---|
| Menu Key | `NONE` |

Fast Draw has nothing worth configuring in-game, and Stop The Ped needs F9.

---

# PHASE 14 — Launching with standalone plugins

Anything installed into the `plugins` folder is a **standalone plugin**, and RagePluginHook needs telling about each one.

**From now on, launch like this:**

1. Click **RagePluginHook** on the taskbar
2. **Immediately hold Left Shift** and keep holding until the settings window appears
3. **Plugins** → **Load These Plugins on Startup** → **Check All**
4. **Load All Plugins on Startup**
5. **Save and Launch**

---

# PHASE 15 — Plugin downloads, batch 2

1. Immersive Effects *(Faya)* — main file
2. Turn Off That Engine *(SuperPowerManiac)*
3. Riskier Traffic Stops *(ashopburgers)* — main file
4. Spotlight *(alexguirre)*
5. Keep The Door Open *(Coro)*
6. Ambient Effects *(Dilapidated)*
7. Open All Interiors
8. Restrain The Deceased *(Faya)* — main file
9. Bait Car — main file
10. In-Game Screenshot
11. Dash Cam V *(PedroGamer)*
12. Grammar Police *(Opus49)* — main file
13. Heli Assistance *(OG does it)* — main file
14. Simple HUD — take **`SimpleHUD.zip`** specifically
15. Radio Realism Alpha *(Buddha Rocks)*
16. Radio Realism First Response *(Officer Porky)*
17. Sticky Wheels *(Coro)*
18. Stop The Ped *(JoJo)*
19. Search Items Reborn *(Officer034)*
20. Ultimate Backup *(JoJo)*

**Open All Interiors** isn't optional in practice — most modern callout packs require it.

---

# PHASE 16 — Plugin installation, batch 2

### Immersive Effects
`files` folder → `plugins` only → **root**

### Turn Off That Engine
Folder → `plugins` → **root**

### Riskier Traffic Stops
`plugins` → **root**

### Spotlight
Goes **inside** the plugins folder.

Open `plugins` in your game directory → drag in both `spotlight_resources` (folder) and `spotlight.dll`.

### Keep The Door Open
GTA5 folder → `plugins` → **root**

### Ambient Effects
`AmbientEffects.dll` → **scripts** folder

### Open All Interiors
Both files, **not** the readme → **root**

### Restrain The Deceased
`files` → `plugins` → **root**

### Bait Car
GTA5 folder → `plugins` → **root**

### In-Game Screenshot
Both files → **scripts** folder

### Dash Cam V
Open the **"lur friendly"** version.

`dashcamV.dll` + `dashcamV.ini` → **plugins** folder

### Grammar Police

> **Critical:** when prompted, do **not** replace `CalloutInterface.ApplicationExtension.dll` or `IPTCommon.dll`. Overwriting either silently breaks Callout Interface, and nothing tells you why.

GTA5 folder → drag the `lspdfr` folder **and** the `plugins` folder → **root**

**Then the textures, via OpenIV:**

1. Open OpenIV, edit mode on
2. Navigate to `mods` → look for **`x64c.rpf`**
3. **If it isn't there:** find `x64c.rpf` in the left-hand column, right-click → **Copy to mods folder**, wait a moment
4. Then: `x64c.rpf` → `levels` → `gta5` → `props` → `lev_des` → **`v_minigame.rpf`**
5. Drag in both texture files from the archive — not the readme
6. Clear the search box → **File** → **Close All Archives** → close OpenIV

This is the CB radio prop.

**Set up your microphone — Grammar Police won't work without this:**

1. Windows **Settings** → search `Sound` → **Sound Settings**
2. Under **Input**, select the **exact microphone you'll be speaking into**
3. Close

If you skip this, dispatch will not understand a word you say. Running Windows' voice recognition setup once improves accuracy further.

### Heli Assistance
Its three files, **not** the readme → `plugins` → `lspdfr` folder

### Simple HUD
Open your **scripts** folder in the game directory.

In the archive: GTA5 folder → `scripts` folder → select **everything** → drag into your **scripts** folder.

*This is what LemonUI was for.*

**Then the texture, via OpenIV:**

1. Navigate: `mods` → `update` → `update.rpf` → `x64` → `textures` → **`script_txds.rpf`**
2. Drag in **`simpleMenu.ytd`** from the archive's textures folder
3. Clear search → **File** → **Close All Archives** → close

### Radio Realism Alpha

> **The one drop where placement genuinely matters.**

Navigate in your game directory to: `lspdfr` → `audio` → `scanner`

In the archive, follow the matching path: GTA5 → `lspdfr` → `audio` → `scanner` → find the **`resident`** folder.

Drag `resident` into your `scanner` folder — **drop it in empty space, not on top of an existing folder.** If you drop it onto something, it nests wrongly and the audio breaks.

Replace **18 files** when prompted.

### Radio Realism First Response
`plugins` folder + `lspdfr` folder → **root**

### Sticky Wheels
GTA5 folder → `plugins` → **root**

### Stop The Ped
Stop The Ped folder → `plugins` → **root**

### Search Items Reborn
Folder → GTA5 folder → `plugins` → **root**, replace 2 files

### Ultimate Backup
Folder → `plugins` → **root**, replace 2 files

> Search Items Reborn and Ultimate Backup deliberately overwrite Stop The Ped files. **Install them in this order, after Stop The Ped**, or you'll undo them.

---

# PHASE 17 — Config files, batch 2

## In `scripts`

### In-Game Screenshot

| Setting | Change to |
|---|---|
| Screenshot Key | `K` |

### Simple HUD

| Setting | Change to |
|---|---|
| Direction Position X | `292` |
| Direction Scale | `51` |
| Road Position X | `312` |
| Road Scale | delete the trailing `5` |
| Postal Enabled | `false` |
| Time Position Y | `912` |
| Time Format | `12` |
| Time Enabled | `true` |
| Toggle Key | `B` |
| Modifier Key | `NONE` |
| Menu Enabled | `true` |

Postal is disabled here because Callout Interface's version is better positioned. If you'd rather use this one, leave it enabled and disable the Callout Interface one instead — don't run both.

**Menu Enabled must be `true`** or the menu won't open at all.

---

## In `plugins`

### Spotlight — `plugins/spotlight_resources` → General config

| Setting | Change to |
|---|---|
| Editor Key | `F6` |
| Keyboard toggle key | `S` |
| Controller modifier key | `NONE` |
| Controller key below it | `NONE` |
| Mouse toggle key | `S` |

Leave the Left Control modifier. Result: **Left Control + S**.

*The mod author distributes preset offset and visual settings files — if you have them, paste them over the defaults with Ctrl+A, Backspace, Ctrl+V. Otherwise the defaults are workable and F6 lets you adjust in-game.*

### Turn Off That Engine

**Keyboard:** leave as-is or set any capital letter.

**Controller:** find the line containing `Right Thumb`, copy that value, and paste it into `Turn Off Engine = None`, replacing `None`. Right stick click now kills the engine.

### Immersive Effects

| Setting | Change to |
|---|---|
| Menu Key | `F2` |

Leave the Left Shift modifier. Result: **Left Shift + F2**.

### Dash Cam V

| Setting | Change to |
|---|---|
| Measurement system | `1` *(imperial / mph)* |
| Date format | your preference — `0`/`1`/`2` reorder day, month, year |
| Unit Name | your officer name |
| State | `San Andreas` |
| Remote Toggle Key | `I` |
| Remote View **Gamepad** Toggle | `NONE` *(both entries)* |
| Department *(every entry)* | your department name |
| Black and white filter | `false` for colour |

There are several Department fields. Type it once, **Ctrl+C**, then highlight and **Ctrl+V** into each. Tedious but quick.

### Restrain The Deceased

| Setting | Change to |
|---|---|
| Restrain Key | `E` |

Leave the controller on D-pad right. No modifier needed.

---

## In `plugins/lspdfr`

### Grammar Police

**First, make your own config so updates don't wipe it:**

1. Open the `GrammarPolice` folder
2. Right-click the **`default`** folder → Copy → Paste
3. Click the copy once to select the name → rename to **`custom`** → Enter → **F5**
4. Open `custom` and delete the placeholder file at the top — nothing else

**Now edit the config inside `custom`:**

| Setting | Change to |
|---|---|
| Call Sign | **the same one you used in Callout Interface** |
| Agency | `IMMERSIVE` — all caps, **keep the quotes** |
| Dispatch Key | `0` *(number row, not numpad)* |
| Interface Key | `F8` |
| Settings Key | `F7` |
| Radio Key | `O` |
| Radio Modifier Key | `Left Control` |
| All Hot Keys | `None` |
| All Controller Buttons | `None` |
| Show Notifications | `True` |
| Player Status | `True` |
| Show Target Plate | `True` |
| Status Text Position X | `489` |
| Status Text Position Y | `980` |
| Status Text Scale | `47` |
| Radial Position X | `625` |
| Radio Position Y | `669` |
| Radio Scale | `43` |
| PTT Hold To Talk | `True` |
| Preface Response | `2` |
| Enable Traffic Stop | `True` |
| Attempt To Initiate Pursuit | `True` |
| Use Generic Response | `True` |
| Officer Backup Air | `True` |
| **Every `Use Natives` flag** | `False` |

The Use Natives block is easy to skim past — set them all to `False`.

*Preface Response 2 is dispatch answering "go ahead" after your callsign. 1 is "this is dispatch", 3 repeats your callsign back.*

### Bait Car

| Setting | Change to |
|---|---|
| Main Menu Key | `F11` |

### Heli Assistance

| Setting | Change to |
|---|---|
| Player Name | your officer name |
| Unit Name | your air unit callsign |

> Heli Assistance **does not respond to Grammar Police.** You call it with **Left Shift + H** and dismiss it the same way. Left Control + H opens its menu. Everything else about it is worth having for the searchlight alone.

### Officer Porky plugin

| Setting | Change to |
|---|---|
| Display Street Detection Notification | `True` |
| Enable Auto Ped ID Check | `True` |
| Key To Play Backup Animation | `NONE` |
| Controller buttons | `None` |

Leave the under-60mph notification `False` — you want alerts for speeders, not everyone. Leave the radio animation off; it gets in the way.

### Riskier Traffic Stops

| Setting | Change to |
|---|---|
| Chance value | `30` |

Scale is 0–100. Thirty means roughly one stop in three goes wrong. Higher gets exhausting fast.

### Stop The Ped

The longest config in the set.

| Setting | Change to |
|---|---|
| Shortcut key to pat down | `F9` |
| Key to call transport | `D9` |
| Controller context menu buttons | `None` *(both)* |
| Controller quick grab — `B` | `None` |
| Controller quick grab — D-Pad Left | `None` |
| Button to tackle | `X` |
| Button to boost player speed | `A` |
| Take control of all peds arrested by LSPDFR | `NO` |
| Force search result full screen | `NO` |
| Glowing stick / parking wand | `NO` |
| Realistic weapon system | `NO` |
| Prisoner transport backup enabled | `YES` |
| Use nearest cop as prisoner transport | `YES` |
| All prisoner transport siren / light / sound | `NO` |

**Keep the keyboard quick-grab bindings** — only the controller ones get cleared.

`D9` means the `9` on the number row. Anything written `D6`–`D9` follows the same convention.

**Why these:**
- *Take control of arrested peds = NO* — otherwise other officers leave every arrest standing next to you instead of transporting them
- *Full screen search = NO* — results appear above the minimap instead of freezing the game
- *Nearest cop as transport = YES* — with a backup unit on scene, they take the prisoner instead of you waiting for a van
- *Realistic weapon system = NO* — it's a toggle you enable in-game when you want it

### Ultimate Backup

| Setting | Change to |
|---|---|
| Toggle Menu Key | `U` |
| Perimeters code 2 siren lights on | `NO` |

---

# PHASE 18 — Final launch

1. **Empty your Recycle Bin**
2. **Restart your PC** — you've just installed forty plugins and edited thirty config files
3. Click **RagePluginHook** on the taskbar
4. **Immediately hold Left Shift** until the settings window appears
5. **Plugins** → **Load These Plugins on Startup** → **Check All**
6. **Load All Plugins on Startup**
7. **Save and Launch**

**If you spawn as a civilian, in an apartment, or in the shower:**
**F4** → type `forceduty` → **Tab** → **Enter**

**If LSPDFR is missing from the pause menu:**
**F4** → type `reloadall` → **Tab** → **Enter**

**Then load your weapons:** F3 → Weapons → Weapons Load Save Menu → **Load All Weapons Slot 1**

---

# One-time in-game setup

Two things that aren't in any config file and are easy to miss.

### ELS coronas

Out of the box, stage 1 and stage 2 lighting look completely dead — you'd reasonably assume ELS was broken.

Get in your ELS vehicle, turn the lights **off**, then hold **Left Alt** and tap **1**, **2**, **3**, **4**, **5** repeatedly until **each counter reaches 30**. Thirty is the maximum; `6` does nothing.

Stage 1 and 2 now have visible lights. You only ever do this once.

### Add the radar gun to your weapon wheel

1. **Load your weapons first** — F3 → Weapons → Weapons Load Save Menu → Load All Weapons Slot 1
2. F3 → Weapons → scroll to **Vintage Pistol** → add it
3. Weapons Load Save Menu → **Save All Weapons to Slot 1**

Load before saving, or you'll overwrite the slot with an empty loadout.

---

# Verifying it worked

Launch and check each of these. If one fails, the fix is almost always in that mod's own config.

| Check | How |
|---|---|
| LSPDFR loaded | LSPDFR tab in the pause menu |
| Plugins loaded | No errors in the RagePluginHook console (`F4`) |
| Weapons | F3 → load slot 1 → weapon wheel is populated, flashlights attached |
| ELS | Right Control + P, then Backspace — box appears. `J` cycles three light stages |
| Callout Interface | `F10` opens. Postal code visible near the minimap |
| Simple HUD | `B` opens. Street, zone, county and clock on screen |
| Grammar Police | Hold `0`, say *"Dispatch, show me 10-41"* — dispatch replies |
| Compulite | Hold `X` during a traffic stop |
| Stop The Ped | `E` at a stopped vehicle opens the interaction menu |
| Ultimate Backup | `U` opens the backup menu |
| Spotlight | Left Control + S |
| Speed radar | Left Control + I |
| LIAR | Numpad 1 |

---

# If something's wrong

**Nothing loads at all.** Script Hook V doesn't match your GTA build. This is behind most "LSPDFR is broken" posts — check the version and get the matching one. Nothing else will work until this does.

**Callout Interface stopped working.** Something overwrote `CalloutInterface.ApplicationExtension.dll` or `IPTCommon.dll`. Reinstall Callout Interface and redo its config, then never replace those two files again.

**Grammar Police doesn't understand you.** Windows Settings → Sound → Input isn't set to your actual microphone.

**Two things fire on one key.** Compare against the tables above — the whole point of these specific values is that nothing collides.

**Numpad binds do nothing.** Num Lock is off.

**Textures flickering or missing.** Confirm `Main Preload Models = false` in `LSPDFR.ini`, and that Resource Adjuster is in your game root.

**Game crashed and you want to know why.** Read `RagePluginHook.log` in your game directory. It names the plugin that failed and usually why.

**A mod is misbehaving badly.** Delete its `.dll` from `plugins`, launch, confirm the problem's gone, then reinstall it fresh.

---

# Complete keybind reference

### Core

| Key | Action |
|---|---|
| `F3` | Simple Trainer |
| `F4` | RagePluginHook console — **never rebind** |
| `Left Shift` | Start traffic stop |
| `B` *(hold)* | Initiate pursuit |
| `N` | Pursuit menu |
| `Y` | Accept callout |
| `E` | Interact / Stop The Ped menu |
| `E` ×2 | Detain — suspect kneels |
| `E` *(hold)* | Takedown — suspect prone |
| `I` | Arrest / stop ped / traffic stop interact |
| `Q` | Frisk *(vanilla)* |
| `G` | Vehicle & equipment menu |
| `F9` | Quick search |
| `Delete` | Teleport to waypoint |

### Vehicle & lighting

| Key | Action |
|---|---|
| `J` ×1/2/3 | ELS light stages |
| `1`–`5` | Sirens · `1` then `6` = dual |
| `R` / `T` | Manual siren |
| `E` / `Y` | Horn |
| `Left Alt + 1–5` | Corona setup — repeat to 30 |
| `Right Control + P` | ELS box |
| `C` | Engine off |
| `V` | Cycle camera → dash cam |
| `Left Control + I` | Remote dash cam view |
| `Left Control + S` | Spotlight |
| `F6` | Spotlight editor |
| `Left Control + W` | Custom Pullover |
| `Left Control + R` | Pullover mimic |
| `Left Control + T` | Pullover follow / quick grab |
| `Left Shift + D` | Delete vehicle |
| `Period` *(hold)* | Window down |

### Radio & HUD

| Key | Action |
|---|---|
| `0` *(number row)* | Grammar Police talk |
| `F8` | Grammar Police interface |
| `F7` | Grammar Police settings |
| `Left Control + F7` | Display settings |
| `Left Control + F8` | Ten codes |
| `Left Control + O` | Radio |
| `M` | Interaction menu — radio position, walk style |
| `F10` | Callout Interface |
| `NumPad 7` | MDT · hold for status arrow |
| `D` | ALPR toggle |
| `B` | Simple HUD menu |
| `X` *(hold)* | Compulite |
| `Left Shift + X` | Give citation |

### Tools

| Key | Action |
|---|---|
| `NumPad 1` | LIAR radar gun · hold for menu |
| `Left Control + I` | Speed radar |
| `Left Shift + I` / `O` | Radar threshold up / down |
| `Left Control + Home` | Radar direction |
| `U` | Ultimate Backup |
| `Left Shift + H` | Call / dismiss helicopter |
| `Left Control + H` | Helicopter menu |
| `F11` | Bait Car |
| `Insert` | Bait car kill switch |
| `Left Shift + F2` | Immersive Effects |
| `K` | Screenshot camera |
| `F7` | Open All Interiors blips |
| `Tab` | Skin Control |
| `Left Control + T` / `Y` | Clipboard / notepad |
| `Left Shift + E` | Restrain downed suspect |
| `Enter` *(hold)* | Dismiss all units |
| `Backspace` *(hold)* | Speed up backup arrival |

### Controller

| Input | Action |
|---|---|
| Left stick click | Lights / horn |
| D-pad left ×3 | ELS stages |
| D-pad down, then `B` | Sirens |
| D-pad right | Interact |
| D-pad right ×2 | Detain |
| D-pad right *(hold)* | Takedown |
| Up on D-pad *(aimed)* | Mark pursuit target |
| D-pad left | Frisk / K9 search |
| Right stick click | Engine off |
| `X` | Tackle · crawl behind cover |
| `A` | Sprint boost |
| `T` / D-pad up + `X` | Partner into vehicle |
| `U` *(hold)* | Dismiss partner |
| Select / Back | Cycle camera |

---

For how to actually use all of this in game — traffic stops, searches, evidence testing, backup, K9, jail — see the **Operations Manual**.
