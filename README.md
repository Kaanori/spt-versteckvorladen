# VersteckVorladen (preload hideout)

Uses the time you spend on the post-raid results screen to load, in the
background, the data the hideout will need afterwards.

## Requirements

- SPT 4.1.6 (EFT 0.16.9). BepInEx ships with SPT.

## Installation

1. Download `VersteckVorladen-1.0.0.zip` from the [Releases](https://github.com/Kaanori/spt-versteckvorladen/releases) page.
2. Extract it **into your SPT game folder** (the folder that contains `EscapeFromTarkov.exe`).
   The ZIP already has the right layout; the mod ends up in `BepInEx/plugins/`.
   On Windows, make sure "Extract All" does not add an extra subfolder.
3. Restart the game.

## Uninstall

Delete the mod's files from `BepInEx/plugins/` (settings: `BepInEx/config/` for client mods) and restart the game.

## The problem

After the raid, the game proceeds like this:

1. `OnGameEnd` — clear away the raid map, clean up memory
2. `ShowSessionResult` — **the results screen you click through**
3. `ComebackToMainMenu` — load the menu scene
4. `PrepareGameJob` — **only here does the hideout start loading**

Step 4 therefore only starts once you have finished clicking in step 2.
All the clicking time lies idle. This mod claims exactly that time.

## What is preloaded — and what is not

The game assembles the hideout in three steps
(`TarkovApplication.HideoutController.method_2`):

| Step | What | Preloadable? |
|---|---|---|
| `method_1` | the hideout scenes (`bunker_2` and neighbours) | **no** |
| `method_5` | appearance, equipment, `BUNDLES_TO_PRELOAD` | yes |
| `method_4` | prefabs of all craftable items | yes |

**Why step 1 does not work:** `ComebackToMainMenu` loads the menu scene
with `LoadSceneMode.Single`. That clears every other scene. Any scene
loaded beforehand would be gone again seconds later.

**Why steps 2 and 3 work:** These are pure bundle loads. Bundles are not
tied to scenes but to a reference counter (`DependencyGraph`). Anyone who
holds a bundle with `Retain` keeps it loaded — across the scene change,
`Resources.UnloadUnusedAssets` and the forced garbage collection. When the
game later calls `Retain` itself, it finds everything ready.

So the time saving is **partial**: the bundles are there, but the scene
still has to be loaded.

## Release happens at raid start

At the next raid the game calls `UnloadHideout`. There the mod releases its
bundles — from then on the memory should belong to the map.

## Settings (F12 -> Versteck vorladen)

| Setting | Default | Meaning |
|---|---|---|
| **Eingeschaltet** (enabled) | on | Preloading at all |
| **Startverzoegerung (Sekunden)** (start delay, seconds) | 3 | Wait after the results screen appears |
| **Bundles gleichzeitig** (bundles at once) | 16 | Higher = finished sooner, but choppier |
| **Herstellungs-Rezepte mitladen** (also load crafting recipes) | on | The biggest chunk. Off = less memory, less gain |
| **Protokoll** (log) | off | Write count and duration to the BepInEx console |

## Measuring

Turn **Protokoll** on. After the raid the BepInEx console then shows e.g.:

    Versteck vorladen: 412 Bundles in 6,3 s vorgeladen.

You would otherwise have waited these 6.3 s after the last click. If the
number is small, there was little to fetch — then the scene is the
bottleneck, not the bundles.

## If it stutters

The results screen stutters while clicking through? Lower **Bundles
gleichzeitig** (e.g. 4) or raise **Startverzoegerung**.

## Note on the game setting

The game has its own setting `EnableHideoutPreload`. In this setup it is
**on** — but it only ensures preloading at game start and from the main
menu on, not during the results screen. This mod complements it, it does
not replace it.

## Building from source

Needs the .NET 10 SDK and an SPT 4.1.6 installation to build against.

```bash
./build.sh             # Linux: build only
./build.sh --install   # build and copy into the game folder
```
```powershell
.\build.ps1            # Windows
.\build.ps1 -Install
```

The game folder is set in `Directory.Build.props` (Windows default `C:/SPT`)
or via the environment variable `SPT_DIR`.

## Support

Bugs and questions: [GitHub issues](https://github.com/Kaanori/spt-versteckvorladen/issues).

## License

MIT — see [LICENSE](LICENSE).
