# SHADOW CONTRACT

A 2D top-down pixel-art assassin / stealth game for PC, made in **Unity 6**.

Sneak through a lakeside villa, an underground research lab and a downtown office tower. Study your target's routine, then take them out with a silenced shot, a knife in the dark or a well-placed "accident". After that, get out. Get paid, buy better gear, take the next contract.

---

## How to play it (first time)

1. **Install Unity Hub** from <https://unity.com/download>.
2. In Unity Hub go to **Installs → Install Editor** and install **Unity 6 (6000.0 LTS)**. Add the **Windows Build Support** module if you want to make an `.exe`.
3. Download this repository (green **Code** button → **Download ZIP**, then unzip it). You can also `git clone` it.
4. In Unity Hub click **Add → Add project from disk** and pick the unzipped folder.
5. Open the project. The first import takes a few minutes. On first open the project also sets itself up automatically. If it ever doesn't, use the menu **Shadow Contract → Setup Project**.
6. Open **Assets/Scenes/Main.unity** (it's usually open already) and press **▶ Play**.

> If Unity asks to *enable the new Input System backends* and restart, click **Yes**. The game works with either input setting.

### Make a Windows .exe
Menu **Shadow Contract → Build Windows (.exe)**. The build lands in `Builds/Windows/ShadowContract.exe`, and you can copy that folder to any Windows PC.

---

## Controls (all can be rebound in Settings)

| Key | Action |
|---|---|
| **W A S D** | Move |
| **Mouse** | Aim |
| **Left click** | Attack / shoot / throw |
| **Right click** | Aim down sights (guns) · Silent subdue from behind (knife) |
| **R** | Reload |
| **E** | Interact: doors, closets, bodies, terminals, stairs, windows, pickups |
| **Shift** | Sprint (loud!) |
| **Ctrl** or **C** | Crouch (quiet, harder to see, hides behind low furniture) |
| **1 – 5** | Weapon slots: melee, sidearm, primary, throwing knives, coins |
| **Mouse wheel** | Cycle weapons |
| **H** | Use a medkit |
| **Tab** | Inventory, objectives and map |
| **Esc** | Pause |

---

## What's in the game

- **The full game loop:** Main Menu → Contracts → Loadout/Shop → Mission → Objectives → Extraction → Rewards → Shop → next contract. Progress is saved automatically.
- **3 maps and 6 contracts:**
  - *Villa Moretti (Luxury Mansion):* gardens, ballroom, study, bedrooms, kitchen, a security room, a wine cellar and vault level, and a secret passage behind a bookshelf.
  - *Helix Labs (Underground Facility):* loading dock, keycard checkpoint, ventilation shafts, labs, servers, barracks, a generator room with explosive barrels, security cameras and a containment lab.
  - *Kessler Tower (Downtown Office):* street and alley, lobby, security desk, a keycard elevator and stairwell, an open-plan office floor, meeting room, CEO office, server room, and a rooftop helipad.
- **Stealth:** Guards have vision cones and a suspicion meter that builds up rather than detecting you instantly. Light and darkness matter, and so do crouching, low cover, bushes, closets and vents. Noise matters too: footsteps, doors, gunshots, glass, explosions. Bodies can be dragged and hidden. Suppressors make shots quiet, and you can distract guards with coins, radios, pianos and vending machines. Fuse boxes cut the lights, and a security terminal shuts off the cameras.
- **Enemy AI:** idle, patrol, suspicious, investigate, search, chase, attack and return to patrol. Guards radio in the alarm (kill them before they finish the call), check closets, fix fuse boxes and switch off distractions. Civilians run to report you, and bodyguards follow their principal. Targets follow daily routines and **flee for the exit when the alarm goes up.**
- **Weapons:** Knife, Combat Knife, Throwing Knives, Pistol, Silverballer SD, Vektor SMG, Breacher 12G shotgun and KR-7 rifle. Each has its own damage, fire rate, magazine size, reload time, accuracy, range, recoil and noise. Upgrades: suppressors, extended magazines and stabilizers.
- **Shop and loadout:** weapons, ammo, armor, medkits and upgrades. Every item has a price and stat bars, and you can't buy what you can't afford. Stock unlocks as you complete contracts.
- **Rewards:** a mission-complete screen with time, kills, detection status and bonus challenges (Silent Assassin, Clean Hands, Professional, Ghost, Swift).
- **Presentation:** hand-tuned procedural pixel art, baked lighting with wall shadows, muzzle flashes, tracers, shell casings, sparks, glass, explosions and non-graphic hit effects. Adaptive music crossfades between calm, tension and combat. There are voice barks with subtitles and ambience for each map.
- **Settings:** volume controls, Easy/Normal/Hard difficulty, screen shake on/off, vision cones on/off, fullscreen, and key rebinding.

Save files live in Unity's persistent data folder (menu **Shadow Contract → Open Save Folder**). They are written atomically with a backup copy, so a crash can't corrupt your progress.

---

## Project layout

```
Assets/
  Scripts/Core/     Engine-independent game simulation (no UnityEngine references):
                    maps, collision, pathfinding, lighting, weapons, AI, stealth,
                    missions, shop, progression, saving
  Scripts/Game/     Unity layer: bootstrap & GameManager, input, rendering (WorldView,
                    CharacterView, lighting, vision cones), FX, audio, HUD and menus
  Scripts/Editor/   Project setup, Windows build menu, asset import settings
  Resources/        Maps (text), generated sprites, audio, fonts (OFL), shaders
  Tests/EditMode/   Smoke tests for Unity's Test Runner
tests/Core.Tests/   Full NUnit suite for the core (dotnet test)
tools/mapgen/       Map authoring scripts (Python) -> Assets/Resources/Maps/*.txt
tools/art/          Pixel-art generator -> Assets/Resources/Sprites
tools/audio/        Sound & music synthesiser -> Assets/Resources/Audio
tools/compile_check Compiles the Unity scripts against Unity reference assemblies
```

The game creates itself from code when Play starts (`GameBootstrap`), so the scene has no objects to set up and no references to break.

### For developers

```bash
# Core test suite: map validation, weapons, shop, saves, AI/stealth behaviour,
# scripted full-mission playthroughs and randomised stress simulations
dotnet test tests/Core.Tests

# Compile-check all Unity scripts (new input system, legacy input, editor)
dotnet build tools/compile_check -p:Variant=Player
dotnet build tools/compile_check -p:Variant=Legacy
dotnet build tools/compile_check -p:Variant=Editor

# Regenerate content (Python 3 + pillow, numpy, scipy)
python3 tools/mapgen/mansion.py && python3 tools/mapgen/facility.py && python3 tools/mapgen/office.py
python3 tools/art/gen_maps.py && python3 tools/art/gen_sprites.py
python3 tools/audio/gen_audio.py
```

Debug helpers in the editor: **Shadow Contract → Unlock Everything (debug)** and **Delete Save Data**.

## Browser version (`web/`)

`web/` is a TypeScript port of the same game that runs in a browser without Unity. `web/src/core` is a direct port of `Assets/Scripts/Core`, with the same names and numbers. `web/src/game` draws it with Canvas2D and plays sound through WebAudio, reusing the generated art, maps and audio. Progress and settings are saved in the browser's local storage.

```bash
cd web
npm install
npm test            # ported core tests: map reachability, stealth, weapons, shop, bots finishing all 6 contracts
npm run pack        # packs Assets/Resources into dist/ (sprites.json, maps.json, audio.json, sfx.wav, music.wav)
npm run build       # bundles the game and inlines it into dist/index.html
python3 -m http.server -d dist 8000   # then open http://localhost:8000
```

In the browser, crouch defaults to **C**, because Ctrl+W closes the tab.

## Credits

- Game design, code, art and audio: generated for this project.
- Fonts: *Silkscreen* by Jason Kottke and *VT323* by Peter Hull, both under the SIL Open Font License (see `Assets/Resources/Fonts/OFL-*.txt`).
