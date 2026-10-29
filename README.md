# Slip & Skid Map Template

Make your own tracks for **Slip & Skid** by describing them in one JSON file: straights, banked turns, climbs,
half-pipes, loops, jumps, forks, tunnels, surfaces (asphalt, mud, ice, sand…) and a decor theme. The Map Kit in this
repo turns the file into a real map in Unity, and the **MapKitLoader** mod
([Thunderstore](https://thunderstore.io/c/slip-skid/p/LesAmisDeIronWings/MapKitLoader/)) plays it in the game with the
game's own start and finish gates, no-trap zones, surface cards, scenery and podium.

```json
{
  "map": { "id": "MyFirstMap", "name": "My First Map", "difficulty": 2, "host": "Snake" },
  "decor": { "theme": "NightCity" },
  "course": [
    { "straight": 8 },
    { "tiles": 6, "turn": 90, "bank": 10 },
    { "boost": true }, { "straight": 2 },
    { "jump": { "gap": 18, "drop": 4 } },
    { "boostRun": 3 }, { "straight": 2, "push": true },
    { "loop": { "tiles": 30, "tileLength": 4 } },
    { "straight": 10 },
    { "finish": true }
  ]
}
```

Two examples are included: [`maps/MyFirstMap`](maps/MyFirstMap/map.json), a short starter, and
[`maps/MegaStunt`](maps/MegaStunt/map.json), the published Mega Stunt map (2.1 km, two loops, two jumps, a fork).

## 1. What you need (Windows)

| | |
|---|---|
| **Slip & Skid** + a mod manager | [Thunderstore Mod Manager](https://www.overwolf.com/app/thunderstore-thunderstore_mod_manager) or r2modman, with a profile that has **MapKitLoader** (1.3.0 or newer). |
| **Unity 2022.3.62f3** | Exactly this version (the game's: a map built with another one, Unity 6 included, doesn't load). Install it from [Unity Hub](https://unity.com/download) → Installs → Install Editor → Archive, or `unityhub://2022.3.62f3`. Activate a free Personal licence in Unity Hub once. |
| **Python 3.10+** | [python.org](https://www.python.org/downloads/), then in this folder: `pip install -r tools/requirements.txt` |
| Git, VS Code (optional) | VS Code completes the map file's keys and flags mistakes (the file's `$schema`). |

The build script finds Unity in `C:\Program Files\Unity\Hub\Editor\2022.3.62f3` or `C:\Program Files\Unity 2022.3.62f3`;
anywhere else, set `UNITY_EXE` to its `Editor\Unity.exe`.

## 2. Get the template and build the example

Click **Use this template** on GitHub (or clone it), then in the repo folder:

```bash
pip install -r tools/requirements.txt
python tools/build_map.py maps/MyFirstMap/map.json --no-install
```

The first run imports Unity's packages and takes several minutes; later builds take one or two. A good build ends with:

```
[Slip & Skid] My First Map created from map.json: ... (... road tiles, ... scenery props, 1 loop(s), about 880 m long).
[Slip & Skid] Track clearance OK.
[Slip & Skid] Road seams OK.
[Slip & Skid] No flickering faces.
[Slip & Skid] Every renderer is drawn in game.
[Slip & Skid] Built to: ...\unity\MapKit\Builds\MyFirstMap ...
```

The map is in `unity/MapKit/Builds/MyFirstMap/`: `mapkit.json`, the bundle, `preview.png` (level-select picture),
`thumbnail.png` (outline), `preview.obj` (3D model) and `track.json` (the road's centre line).

Unity can't open a project twice: close the editor before building, or add `--copy` (builds from a copy).

## 3. Make your map

1. Copy `maps/MyFirstMap` to `maps/<YourMap>` (letters and digits only).
2. In `maps/<YourMap>/map.json` set `map.id` to the folder name, and a **unique** `name` (lobbies match levels by
   name), your `author`, `difficulty` (1–5), the `host` level (its lighting and camera: `Snake`, `Dermz city`, `Taiga`…)
   and the `decor` theme (`NightCity`, `DrownedCity`, `Seasons`, `None`).
3. Write the course: every step is listed in **[docs/JSON_MAPS.md](docs/JSON_MAPS.md)**; read
   **[docs/MAP_RULES.md](docs/MAP_RULES.md)** first (start, finish, loops, jumps, mud and ice).
4. Check the layout in seconds, without Unity:

   ```bash
   python tools/track_sim.py maps/<YourMap>/map.json
   ```

   It prints the length, the heights and `clearance OK` (or where the road comes too close to itself), and draws
   `maps/<YourMap>/map-layout.png`: top view and height profile.
5. Build it: `python tools/build_map.py maps/<YourMap>/map.json --no-install`. A mistake in the file stops the build
   and names the step, e.g. `course[12].loop.tiles: 90 is out of range (8 to 80)`; a road too close to itself names
   the place. Fix and build again.

In Unity instead: open `unity/MapKit` with Unity Hub (2022.3.62f3), menu **Slip & Skid > 1j. Create Map from JSON
file...**, then **Slip & Skid > 2. Build and Install Map**.

## 4. Test it in the game

1. Install the build into a mod manager profile that has MapKitLoader:

   ```bash
   python tools/build_map.py maps/<YourMap>/map.json --install-to="%APPDATA%\Thunderstore Mod Manager\DataFolder\SlipSkid\profiles\<profile>\BepInEx"
   ```

   (or copy `unity/MapKit/Builds/<YourMap>/` to that profile's `BepInEx/CustomMaps/<YourMap>/`). Without
   `--no-install` or `--install-to`, the build goes into **every** profile that has the loader.
2. Start the game from the mod manager with that profile, host a game, pick your map in the level select.
3. Open the profile's `BepInEx/LogOutput.log`. A healthy map logs `Loaded <Map>`, `Registered <Map> on host level
   '<host>'`, `Host road surface: …`, two `No-trap zone …` lines, `Trails camera …`, `Built <Map> on '<host>'`,
   `Scenery scaled …` and `Fog for …`.
4. Play it:
   - drive the whole track: every bend, the loops (enough speed?), the jumps (do you land on the road?);
   - place a surface card (ice, mud…) on the road: the road takes it;
   - try placing a trap at the start and the finish: refused (no-trap zones);
   - finish: the podium shows the winners;
   - play it with a friend: give them the same build (or publish it), everyone needs the same version.
5. Change the JSON, build, test again.

Don't keep the example **Mega Stunt** installed next to the published Mega Stunt package: it would load twice.

## 5. Publish it on Thunderstore

1. In `maps/<YourMap>/thunderstore/` (copied from MyFirstMap): `manifest.json` (`name` = your map's folder name,
   `version_number`, a `description` of up to 250 characters, the dependencies on the BepInEx pack and
   `LesAmisDeIronWings-MapKitLoader-<version>`), `README.md`, `CHANGELOG.md` and a 256×256 `icon.png`
   (`python tools/icons/make_map_icon.py "" maps/<YourMap>/thunderstore/icon.png WORD1 WORD2` draws one).
2. Build the map, then zip it: `python tools/pack_map.py <YourMap>` → `dist/<YourMap>-<version>.zip`.
3. Upload the zip on [thunderstore.io](https://thunderstore.io/c/slip-skid/) (Upload, community Slip & Skid). Bump
   `version_number` and add a CHANGELOG line for every new upload: Thunderstore refuses a version twice.

## What's in this repo

| Folder | Contents |
|---|---|
| `maps/<Map>/map.json` | Your maps; `maps/<Map>/thunderstore/` their Thunderstore files |
| `docs/` | `JSON_MAPS.md` (every step), `MAP_RULES.md`, `mapkit-map.schema.json` (the file's schema) |
| `unity/MapKit/` | The Unity 2022.3.62f3 project: `Assets/SlipSkid/SDK` (the markers the loader reads, the same as `SlipSkid.MapKit.SDK.dll`), `Editor` (TrackBuilder, JsonMapGenerator, decor, build), `Shaders`, `Rendering` (URP) |
| `tools/` | `build_map.py` (build in Unity), `track_sim.py` (check a layout without Unity), `plot_track.py` (draw a build's track.json), `pack_map.py` (Thunderstore zip), `icons/make_map_icon.py`, `editor-check/` (compile the editor scripts with `dotnet build tools/editor-check`, no Unity) |

The map file can't do everything the Map Kit can: portals, moving parts, wind, geysers, rising seas, trains and your
own scenery need C# in Unity (see the end of docs/JSON_MAPS.md).

Made by **LesAmisDeIronWings** (MapKitLoader, Mega Stunt, Twin Tunnels, Four Seasons and more on Thunderstore).
