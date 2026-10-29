# Map rules

Learned by taking the game apart and by testing maps in it. Follow them and your map plays like an official one.

## Must-haves

- **Start**: 8 or more `straight` pieces first, flat (start gate, railings, the no-trap zone around the grid).
- **Finish**: `{ "finish": true }` last, after a flat straight of 3+ pieces (10 is good). The gate stands 15 m before
  the end of the road. Put `{ "reachHeight": 2 }` before the last turn so the finish isn't sunk below the scenery.
- **No traps at the start and finish**: automatic, the zones follow the gates.
- **Podium** off the track (the default (0, 0, −80) facing back is fine while the course heads away from the start).
- **A unique name** (`map.name`): lobbies match levels by name – two maps called the same break the lobby. Don't
  reuse "Mega Stunt" when you start from the example.
- **Height**: roughly −20 to +40 m. The sea and the death plane lie `deathDepth` (30 m) below the lowest road.
- **Clearance**: the build fails when the road comes within 12 m sideways and 10 m vertically of itself, or within
  25 m of a loop. `track_sim.py` shows it before Unity does. Don't let long turns bring the road back over itself.
- **Size**: 1–2 km. Scenery and fog are scaled to it automatically.

## Shapes

- **Width**: 8 m like the game's road (the default). 16 m only for jump landings. Instead of extra width give relief:
  bank asphalt turns 8–12° (`bank`), a quarter-pipe wall outside mud bends (`"pipe": "Outer"`), a half-pipe on ice
  (`"pipe": "Both"`). No walls anywhere else (the game's roads have none).
- **Loops**: `{ "boostRun": 3 }`, `{ "straight": 2, "push": true }`, then the `loop`, on asphalt. Keep other road
  25 m away.
- **Jumps**: a `boost` a piece or two before; `jump` with a gap of 15–22 m and a drop of 3–7 m; the defaults shape the
  kicker and the 16 m landing (tilted down 12°, two flat pieces, levelled).
- **Forks**: the two lanes are mirror images (same steps, turns negated) so they end side by side. On dirt keep lane
  S-bends at 6° per piece at most.

## Surfaces

| Surface | Look (biome) | Driving |
|---|---|---|
| Asphalt | night city | baseline |
| Dirt | green island, falling leaves | loose: slides (drift ×0.3) |
| Sand | pyramids, sand wind | acceleration ×0.2, steering ×0.7 |
| Mud | drowned city, rain | grip 0.3, acceleration ×0.3, steering ×0.5 |
| Ice | mountains, snow | grip 0.05, acceleration ×0.5 |
| Magma | volcano | acceleration ×1.2, **kills after 3 s on it** |

- The look (sky, fog, scenery, weather) comes from the most used surface other than Asphalt (5 tiles or more).
- Loops and climbs on asphalt. Sand and mud downhill or boosted.
- **Never a boost in front of a bend on mud or ice**: they can't brake. Mud bends ≤ 8° per piece (`"tiles": 4,
  "turn": 32`), ice bends ≤ 4° per piece in a half-pipe, and ~50 m of calm asphalt after ice before boosts or a loop.
- Seasonal maps (`map.seasons`): each player's look follows the season under their car; switch `season` halfway
  through a `tunnel`.

## Mistakes already made

A boost in front of a mud bend (cars thrown off at 46 m/s) · 8 m fork lanes with 10° S-bends on dirt · a 240° ice
corkscrew bringing the road back over a tunnel, with a loop on the tunnel roof · fork lanes that only came back to
the same heading (they ended 71 m apart: an S, not a single bend) · a finish at road height (the gate sunk).
