# The map file (map.json)

A map is described in one JSON file: the course is a list of steps, one per call of the Map Kit's track builder
(`TrackBuilder`), and `JsonMapGenerator` builds it in Unity – road pieces, collisions, start and finish gates,
podium, decor, checks. Unity 2022.3.62f3 is needed (the game can't make collisions from code, so the road is baked
in Unity); no C#.

Examples: [`maps/MyFirstMap/map.json`](../maps/MyFirstMap/map.json) (a small starter) and
[`maps/MegaStunt/map.json`](../maps/MegaStunt/map.json) (the published Mega Stunt map, 2.1 km, two loops, two jumps,
a fork).

```
python tools/track_sim.py maps/<Map>/map.json          # seconds, no Unity: length, heights, clearance, a plot (maps/<Map>/map-layout.png)
python tools/build_map.py maps/<Map>/map.json --no-install   # builds it in Unity (add --copy if the editor is open)
```

In the editor: menu **Slip & Skid > 1j. Create Map from JSON file...**, then **2. Build and Install Map**.

Add `"$schema": "../../docs/mapkit-map.schema.json"` at the top: VS Code then completes keys and underlines mistakes
([schema](mapkit-map.schema.json)). Keys starting with `$` (`"$comment"`) are notes, allowed anywhere. The builder
also stops on any mistake, naming the step: `course[12].loop.tiles: 90 is out of range (8 to 80)`, `course[3].tunel:
unknown key here`.

## The file

```json
{
  "$schema": "../../docs/mapkit-map.schema.json",
  "map": { "id": "MyMap", "name": "My Map", "difficulty": 3, "host": "Snake" },
  "podium": { "position": [0, 0, -80], "yaw": 180 },
  "decor": { "theme": "NightCity" },
  "course": [
    { "straight": 8 },
    { "tiles": 6, "turn": 90, "bank": 10 },
    { "finish": true }
  ]
}
```

| Key | Meaning |
|---|---|
| `map.id` | Letters and digits: the folder (`maps/<id>/`), the bundle and the package. **Required.** |
| `map.name` | Level-select name (default: the id with spaces). Unique: lobbies match levels by name. |
| `map.author`, `map.difficulty` (1–5, default 3), `map.reversible` | As on `KitMap`. |
| `map.host` | Stock level the map runs on (lighting, camera, its biomes): `Snake` (default), `Dermz city`, `Taiga`… |
| `map.deathDepth` | The sea (and the death plane 3.5 m under it) this far below the lowest road. Default 30. |
| `map.seasons` | Seasonal biomes: each player's look follows the season under their car (use `season` steps). |
| `podium` | `position` [x, y, z] and `yaw` of the winners' podium, off the track. Default (0, 0, −80), 180. |
| `decor.theme` | `NightCity` (Mega Stunt), `DrownedCity` (Twin Tunnels), `Seasons` (Four Seasons' islands), `None`. City themes add the lit finish, neon loop rims and the podium's tower; Seasons adds islands, the podium's island and season loop rims. |
| `decor.tunnelArt` | Decorated tunnels: `Neon`, `Seasons` (default with the Seasons theme), `None`. |
| `decor.finishBarrier` | City themes: a barrier after the finish (default true). |
| `course` | The road from the start (origin, heading +Z, height 0), in order. Last step: `{ "finish": true }`. |

The start gate and its 4-car grid, the no-trap zones and the checks (`Track clearance OK`, `Road seams OK`, …) come
by themselves. The [map rules](MAP_RULES.md) still apply: 8+ straight pieces at the start, a flat straight at the finish,
`reachHeight` 2 before it, loops on asphalt with a boost run, and so on.

## Steps

One action per step, plus its options.

| Step | Does | TrackBuilder |
|---|---|---|
| `{ "section": "2 - Big turn" }` | Groups the following pieces (a folder in the scene). | `Section` |
| `{ "straight": 8 }` | 8 straight 10 m pieces. | `Tiles(8)` |
| `{ "tiles": 6, "turn": 90, "slope": 15 }` | 6 pieces turning 90° in all (+ = right) and tilting up 15° in all (− = down). The slope stays until changed: climb with `+15`, level out with `−15`. Turning while sloped = a corkscrew. | `Tiles(6, yaw: 15, pitch: −2.5)` |
| `{ "surface": "Mud" }` | Surface of the road from here: Asphalt, Dirt, Ice, Mud, Sand, Magma (`""` = the host's). | `Surface =` |
| `{ "season": "Ice" }` | Season tag from here (seasonal maps; switch inside a tunnel). | `Season =` |
| `{ "boost": true }` | A +20 boost pad here (no piece). | `Boost()` |
| `{ "boostRun": 3 }` | 3 boost pads, each followed by a piece (before a loop). | `BoostRun(3)` |
| `{ "loop": { "tiles": 32, "tileLength": 4 } }` or `{ "loop": true }` | A vertical loop (walls, accelerators, gravity). Before it: `boostRun` 3 + `{ "straight": 2, "push": true }`. | `Loop` |
| `{ "barrelRoll": { "length": 120, "radius": 9 } }` or `true` | A full roll about the direction of travel. | `BarrelRoll` |
| `{ "jump": { "gap": 20, "drop": 5 } }` | Kicker, gap and landing (put a boost before). Options: `kicker` (12° in all), `kickerTiles` (2), `kickerLength` (6 m), `landing` (12°), `landingFlat` (2 pieces), `landingWidth` (16 m), `landingLength` (12 m). | `Tiles` + `Gap` + `Tiles` |
| `{ "gap": { "forward": 20, "drop": 5 } }` | A bare gap, for a jump shaped by hand. | `Gap` |
| `{ "reachHeight": 2 }` | Ramps to that height (±1 m); `"pillars": true` under it. | `ReachHeight` |
| `{ "fork": { "spread": 9, "left": [...], "right": [...] } }` | A Y-fork; the lanes are step lists. Build them as mirror images (same pieces, turns negated) so they end side by side. | `Fork` |
| `{ "finish": true }` | The finish gate, 15 m before the end. Always last. | `Finish()` |

Options of `straight` and `tiles`: `width` (8 m; 16 for landings), `length` (10 m per piece), `bank` (degrees the
road leans into the turn), `pipe` (`Left`, `Right`, `Both` = half-pipe, `Outer` = outside of the turn), `pipeHeight`
(3, at most 5), `pillars`, `push` (accelerators), `tunnel` (walls and roof, always flat). Like `Tiles`, each step
starts from the default width unless it gives one.

## What the file can't do (yet)

The Map Kit's tricks (portals, moving parts, wind, geysers, a rising sea, fog banks, fake finishes, trains and
traffic: the `Kit*` markers listed in MapKitLoader's README) and scenery of your own need C# in Unity: build them on
top of the scene the JSON made (then un-ignore the map's folder in `.gitignore`, it is no longer rebuilt from the
JSON), or write an editor script that calls `TrackBuilder` directly.
