using System;
using System.Collections.Generic;
using SlipSkid.MapKit.SDK;
using UnityEditor;
using UnityEngine;
using T = SlipSkid.MapKit.Editor.SlipSkidMapTools;

namespace SlipSkid.MapKit.Editor
{
    // Lays a track piece by piece from the origin (heading +Z, height 0): smooth curved road strips with
    // concrete borders, banked turns, quarter- and half-pipes, loops, jumps, Y-forks, tunnels, boost pads, pillars.
    // Sizes follow the game's own pieces (measured, CLAUDE.md "Road pieces"): 8 m of asphalt with a flat 1 m border
    // each side, straights cut into 10 m tiles (20 m on a road twice as wide), and the curve or slope of one Tiles()
    // call kept in one tile, up to 90° and 100 m – a surface card repaints every tile it touches, so a card covers a
    // whole bend, as on stock tracks.
    internal sealed class TrackBuilder
    {
        public const float DefaultWidth = 8f;       // asphalt (stock R001B101); borders add 1 m each side
        public const float WideWidth = 16f;         // a stock piece at x2 (Umbrellas, Ozone…): landings, wide lanes
        private const float WideRoad = 14f;         // from this width on, straights are 20 m tiles (stock x2 pieces)
        public const float DefaultLength = 10f;
        private const float Thickness = 1f;
        public const float BorderWidth = 1f;
        private const float WallHeight = 2.5f;      // loops only
        public const float WallThickness = 0.5f;
        public const float TunnelHeight = 7f;       // inside clearance; the roof is a tile so the camera fades it
        private const int SubSteps = 6;
        private const float DeathHeight = -60f;
        private const float MaxTileTurn = 90f;      // the stock turns are 90° pieces
        private const float MaxTileLength = 100f;   // the longest stock curve (R139, 70 m footprint)
        public const float PipeRadius = 5f;         // stock half-pipe arcs (R162-R167)
        private const int ArcSteps = 6;
        public const float SplitLength = 30f;       // a Y-fork's split, and its merge

        // A quarter-pipe wall on a side of the road (Outer = the outside of the turn), or both (a half-pipe).
        public enum Pipe { None, Left, Right, Both, Outer }

        public Transform Root;
        public Material BoostMat, ConcreteMat, AsphaltMat;
        public int TileCount, LoopCount;
        public readonly List<(Vector3 p, Vector3 right)> Path = new List<(Vector3 p, Vector3 right)>();
        // Road normal at each Path point (upside down in loops, tilted on banked turns) and the points flown over in
        // jump gaps.
        public readonly List<Vector3> PathUp = new List<Vector3>();
        // Surface name of the road at each Path point ("" = the host's road surface, or a jump gap).
        public readonly List<string> PathSurface = new List<string>();
        public readonly HashSet<int> AirPoints = new HashSet<int>();
        public float Distance;

        private readonly string _folder;
        // Path index ranges whose points may be close to each other on purpose (a fork's lanes, a loop and its
        // entry/exit), skipped by CheckClearance – only between points of the same range: any other road near them
        // is still a problem. Loops are also kept LoopClearance away from every other road.
        private readonly List<(int from, int to)> _closeOnPurpose = new List<(int, int)>();
        private readonly List<(int from, int to)> _loops = new List<(int, int)>();
        public const float LoopClearance = 25f;
        private Transform _section;
        private Vector3 _pos;
        private Quaternion _rot = Quaternion.identity;   // heading and slope; the bank is applied per slice
        // The road's shape where the last piece ended (the next piece eases from it); _width < 0 = start afresh.
        private float _width = -1f, _roll, _pipeL, _pipeR;
        private float _uvDistance;
        private bool _meshAssetReady;
        private int _call, _segments;
        private OpenTile _open;
        // The last slice laid: the next piece must start on it (null after a jump gap). Steps found there fail
        // batch builds (FinishChecks).
        private Slice _end;
        private readonly List<string> _seams = new List<string>();

        // Decoys (1.3.0): road built while Fake is on looks like road – dark asphalt, concrete borders – but is plain
        // decoration: no collision, not a tile, not on the driving line. Drive onto it and you fall through. A decoy
        // going on where the real road turns: DecoyFork (never a Fake Spur starting on the road itself – the two
        // overlapped and the decoy stood out of the bend as a wedge with a concrete edge, 2026-10-26).
        public bool Fake;
        // Road that isn't on the driving line (spurs, decoys): decoration keeps clear of these points too.
        public readonly List<Vector3> KeepClear = new List<Vector3>();
        private int _spurDepth, _decoys;
        private bool OffPath => Fake || _spurDepth > 0;

        // Where the road ends now: centre of its surface, flat heading, width.
        public Vector3 Position => _pos;
        public Quaternion Heading => Flat;
        public float RoadWidth => _width < 0f ? DefaultWidth : _width;

        public TrackBuilder(string folder) { _folder = folder; }

        // The map's folder (materials and meshes are saved there).
        public string Folder => _folder;

        public void Init()
        {
            // Editor preview only: the loader replaces border materials with the game's concrete.
            AsphaltMat = T.GetOrCreateMaterial($"{_folder}/AsphaltPreview.mat", new Color(0.22f, 0.22f, 0.25f));
            ConcreteMat = T.GetOrCreateMaterial($"{_folder}/ConcretePreview.mat", new Color(0.62f, 0.62f, 0.6f));
            BoostMat = T.GetOrCreateBoostMaterial($"{_folder}/BoostGlow.mat");
        }

        private Quaternion Flat => Quaternion.Euler(0f, _rot.eulerAngles.y, 0f);

        // Game surface for the road tiles built from now on ("Asphalt", "Mud"…; empty = host road surface).
        // The most used surface picks the biome (fog, sky, scenery).
        public string Surface = "";

        // Seasonal maps: the season (a surface name whose look it shows) of the road tiles built from now on
        // (KitRoad.season). The look switches where the season does, whatever the tiles' surface is at the time –
        // switch it in the middle of a tunnel. Empty = the look follows the surface under the car.
        public string Season = "";

        // Road tiles built from now on look like this surface while driving like `Surface` (KitRoad.look, MapKitLoader
        // 1.3.0+): Surface = "Ice", Look = "Asphalt" is black ice. Empty = they look like their surface.
        public string Look = "";

        // Every tunnel built (consecutive tunnel tiles), for decoration: each tile's road object, its frames
        // (centre line position, rotation, road width) and its season.
        public sealed class TunnelInfo
        {
            public readonly List<(GameObject road, List<(Vector3 p, Quaternion q, float w)> frames, string season)> Tiles =
                new List<(GameObject, List<(Vector3, Quaternion, float)>, string)>();
        }

        public readonly List<TunnelInfo> Tunnels = new List<TunnelInfo>();
        private bool _inTunnel;

        // One cross-section of the road: centre line point, heading + slope, bank (degrees about the road's
        // forward axis, + = right side up), asphalt width, quarter-pipe wall heights, and for a Y-fork's lanes where
        // the slice is cut (at the fork's centre line, in the slice's own x).
        private sealed class Slice
        {
            public Vector3 P;
            public Quaternion Rot;
            public float Roll, W, PipeL, PipeR;
            public float ClipL = float.NegativeInfinity, ClipR = float.PositiveInfinity;
            public Quaternion Q => Rot * Quaternion.Euler(0f, 0f, Roll);
        }

        // The tile being laid: slices of consecutive segments that become one road tile (see AddToTile).
        private sealed class OpenTile
        {
            public readonly List<Slice> Slices = new List<Slice>();
            public Transform Section;
            public int Call;
            public string Surface, Season;
            public bool Tunnel, Walls, Curved, Fake;
            public float Turn, Length;
        }

        public void Section(string name) => _section = T.Child(Root, name).transform;

        // `count` pieces of `length` m. yaw = degrees turned per piece (+ right), pitch = degrees added per piece
        // (negative climbs; the slope accumulates until reversed). The width, bank and pipe walls ease in over the
        // first piece from what the road had before. bank = degrees the road leans into the turn (inner side down);
        // pipe = quarter-pipe wall(s) `pipeHeight` m high (at most PipeRadius) on that side; Outer = outside of the
        // turn, Both = a half-pipe. Tunnel pieces stay flat (walls and roof).
        public void Tiles(int count, float yaw = 0f, float pitch = 0f, float width = DefaultWidth,
                          bool pillars = false, float length = DefaultLength, bool push = false, bool tunnel = false,
                          float bank = 0f, Pipe pipe = Pipe.None, float pipeHeight = 3f)
        {
            _call++;
            float roll = yaw == 0f ? 0f : -Mathf.Sign(yaw) * bank;
            Pipe side = pipe == Pipe.Outer ? (yaw > 0f ? Pipe.Left : yaw < 0f ? Pipe.Right : Pipe.None) : pipe;
            float h = Mathf.Clamp(pipeHeight, 0f, PipeRadius);
            float hl = side == Pipe.Left || side == Pipe.Both ? h : 0f;
            float hr = side == Pipe.Right || side == Pipe.Both ? h : 0f;
            if (tunnel)
            {
                roll = 0f;
                hl = hr = 0f;
            }
            for (int i = 0; i < count; i++)
            {
                Segment(length, yaw, pitch, width, 0f, Vector3.zero, pillars, push, tunnel: tunnel, roll: roll, pipeL: hl, pipeR: hr);
            }
            Flush();
        }

        // A Y-fork: the road splits into two lanes that move `spread` m either side of the centre line over
        // SplitLength (cut at the centre line, so they touch without overlapping: stock Y pieces R071-R089 don't
        // need a plaza either), run `left` and `right` from there, and merge back the same way. Build the two lanes
        // as mirror images (same pieces, yaw negated) so they end side by side, heading the fork's way.
        public void Fork(float spread, Action left, Action right)
        {
            Flush();
            Quaternion heading = Flat;
            Vector3 start = _pos;
            float width = _width < 0f ? DefaultWidth : _width;
            // Both lanes leave the road as it ends here (its bank and walls ease out over the split).
            float roll = _roll, pipeL = _pipeL, pipeR = _pipeR, startWidth = _width;
            Slice approach = _end;
            int first = Path.Count;
            Vector3 leftEnd = Lane(start, heading, -1, spread, width, left);
            _width = startWidth;
            _roll = roll;
            _pipeL = pipeL;
            _pipeR = pipeR;
            _end = approach;
            Vector3 rightEnd = Lane(start, heading, 1, spread, width, right);
            if ((leftEnd - rightEnd).magnitude > 1f)
            {
                Debug.LogWarning($"[Slip & Skid] Fork lanes don't meet ({(leftEnd - rightEnd).magnitude:0.0} m apart). Mirror the two lanes.");
            }
            _rot = heading;
            _pos = (leftEnd + rightEnd) * 0.5f;
            _width = width;
            _end = new Slice { P = _pos, Rot = heading, W = width };
            _closeOnPurpose.Add((first - 15, Path.Count + 15));
        }

        // One lane of a Y-fork: splits off the centre line, runs `lane`, merges back (see Fork).
        private Vector3 Lane(Vector3 start, Quaternion heading, int sign, float spread, float width, Action lane)
        {
            Vector3 side = heading * Vector3.right;
            Split(start, heading, sign, spread, width, outward: true);
            lane();
            Flush();
            if (Quaternion.Angle(_rot, heading) > 1f)
            {
                Debug.LogWarning("[Slip & Skid] A fork lane doesn't end heading the fork's way (flat). Mirror the two lanes and turn back straight.");
            }
            // The centre line is `spread` m from where the lane ended.
            Split(_pos - side * (sign * spread), heading, sign, spread, _width < 0f ? width : _width, outward: false);
            return _pos;
        }

        // A fork lane leaving the centre line for `sign * spread` m off it (outward) or coming back to it, over
        // SplitLength, eased; cut at the centre line. The bank and walls the road had where the split starts ease
        // out over its first half, before the lanes touch (lanes ending in a banked bend used to drop onto a flat
        // merge: a 0.5 m step on each lane's outer edge, FourSeasons, 2026-10-25).
        private void Split(Vector3 centre, Quaternion heading, int sign, float spread, float width, bool outward)
        {
            Flush();
            _call++;
            bool fresh = _width < 0f;
            float roll = fresh ? 0f : _roll, pipeL = fresh ? 0f : _pipeL, pipeR = fresh ? 0f : _pipeR;
            int n = Mathf.CeilToInt(SplitLength / (DefaultLength / SubSteps));
            Vector3 forward = heading * Vector3.forward, side = heading * Vector3.right;
            var slices = new List<Slice>();
            for (int k = 0; k <= n; k++)
            {
                float t = (float)k / n, e = t * t * (3f - 2f * t);
                float off = spread * (outward ? e : 1f - e);
                float h = Mathf.Clamp01(2f * t), fade = 1f - h * h * (3f - 2f * h);
                var s = new Slice
                {
                    P = centre + forward * (SplitLength * t) + side * (sign * off), Rot = heading, W = width,
                    Roll = roll * fade, PipeL = pipeL * fade, PipeR = pipeR * fade,
                };
                if (sign < 0) s.ClipR = off;
                else s.ClipL = -off;
                slices.Add(s);
                if (k == 0) continue;
                Path.Add((s.P, side));
                PathUp.Add(s.Q * Vector3.up);
                PathSurface.Add(Surface ?? "");
            }
            AddToTile(slices, curved: true, turn: 0f, length: SplitLength, tunnel: false, walls: false);
            Flush();
            _pos = slices[n].P;
            _rot = heading;
            _width = width;
            _roll = _pipeL = _pipeR = 0f;
            Distance += SplitLength;
        }

        // Leaves empty space: miss the jump and you fall to your death.
        public void Gap(float forward, float drop)
        {
            Flush();
            _rot = Flat;
            // Keep scenery out of the flight path too.
            Vector3 from = _pos, to = _pos + _rot * Vector3.forward * forward + Vector3.down * drop;
            for (int i = 1; i < 8; i++)
            {
                if (OffPath)
                {
                    KeepClear.Add(Vector3.Lerp(from, to, i / 8f));
                    continue;
                }
                AirPoints.Add(Path.Count);
                Path.Add((Vector3.Lerp(from, to, i / 8f), _rot * Vector3.right));
                PathUp.Add(Vector3.up);
                PathSurface.Add("");
            }
            _pos = to;
            _width = -1f;   // the landing starts at its own width, bank and walls: no easing
            _end = null;
            Distance += forward;
        }

        // A full vertical loop, shifted sideways so the exit clears the entry.
        public void Loop(int tiles, float tileLength, float width = DefaultWidth)
        {
            Flush();
            _call++;
            LoopCount++;
            int first = Path.Count;
            Quaternion entry = Flat;
            _rot = entry;
            Vector3 shiftDir = entry * Vector3.right;
            float step = 360f / tiles;
            float shift = (width + 4f) / tiles;
            var path = new List<Vector3>();
            Transform loop = T.Child(_section, $"Loop_{LoopCount}").transform;
            Transform prev = _section;
            _section = loop;
            for (int i = 0; i < tiles; i++)
            {
                path.Add(_pos);
                Segment(tileLength, 0f, -step, width, shift, shiftDir, pillars: false, push: true, walls: true);
            }
            Flush();
            _rot = entry;
            _pos.y = path[0].y;
            _section = prev;
            _loops.Add((first, Path.Count - 1));
            // The way down passes beside the loop's own run-up (and the exit beside the entry) for about a radius.
            float radiusAlong = tiles * tileLength / (2f * Mathf.PI) + 10f;
            int before = first;
            for (float d = 0f; before > 0 && d < radiusAlong; before--) d += (Path[before].p - Path[before - 1].p).magnitude;
            _closeOnPurpose.Add((before, Path.Count + 15));

            // Gravity pushing outwards from the loop's axis keeps cars glued upside down. The game's spheres pull
            // radially from one point (GravityShapeSimpleSphere), and the loop is shifted (width + 4) m sideways:
            // one sphere in the middle pulled ~31 m/s² sideways at both ends, into the walls, and cars left the loop
            // at its edge (drive tests, 2026-10-24). Instead: a row of weak spheres along the axis, one per metre,
            // running a sphere's reach past both ends, so every point of the loop has as many on each side – the
            // sideways pulls cancel and the outward pull adds up to LoopGravity. Gravity shapes add up in game.
            Vector3 c = Vector3.zero;
            foreach (Vector3 p in path) c += p;
            c /= path.Count;
            float radius = tiles * tileLength / (2f * Mathf.PI);
            float total = width + 4f;
            GravityRow(loop, "LoopGravity", c - shiftDir * (total / 2f), shiftDir, total, radius);
        }

        // Outward pull from an axis (loops, barrel rolls): a row of weak gravity spheres along it, one per metre,
        // running a sphere's reach past both ends, so the sideways pulls cancel everywhere along the road and the
        // outward pull adds up to LoopGravity.
        private static void GravityRow(Transform parent, string name, Vector3 from, Vector3 dir, float length, float radius)
        {
            float outer = radius + 1.5f, carRadius = radius - 0.6f;
            float reach = Mathf.Sqrt(outer * outer - carRadius * carRadius);   // along the axis, seen from a car
            int perSide = Mathf.CeilToInt(reach);
            float each = LoopGravity / (2f * perSide + 1f);
            Transform row = T.Child(parent, name).transform;
            for (float s = -reach; s <= length + reach + 0.01f; s += 1f)
            {
                GameObject sphere = T.Child(row, "GravitySphere");
                sphere.transform.position = from + dir * s;
                KitGravitySphere gs = sphere.AddComponent<KitGravitySphere>();
                gs.gravity = each;
                gs.innerRadius = 0f;
                gs.outerRadius = outer;
            }
        }

        // Outward pull in loops (m/s², negative = away from the axis); the game's gravity is 40 down.
        public const float LoopGravity = -90f;

        // One piece of road as SubSteps slices: the turn, slope and sideways shift are spread over them, the width
        // tapers and the bank and walls ease from where the last piece ended, and each piece starts exactly where
        // the previous one ended, so the track has no steps or seams. The slices join the tile being laid.
        private void Segment(float length, float yaw, float pitch, float width, float shift, Vector3 shiftDir,
                             bool pillars, bool push, bool walls = false, bool tunnel = false,
                             float roll = 0f, float pipeL = 0f, float pipeR = 0f)
        {
            bool fresh = _width < 0f;
            float w0 = fresh ? width : _width, r0 = fresh ? roll : _roll;
            float l0 = fresh ? pipeL : _pipeL, rr0 = fresh ? pipeR : _pipeR;
            var slices = new List<Slice> { new Slice { P = _pos, Rot = _rot, Roll = r0, W = w0, PipeL = l0, PipeR = rr0 } };
            for (int k = 1; k <= SubSteps; k++)
            {
                float t = (float)k / SubSteps, e = t * t * (3f - 2f * t);
                // Turn about the world's vertical axis, slope about the road's own sideways axis: a turn while
                // climbing stays a climb (a corkscrew) instead of wobbling up and down.
                _rot = Quaternion.Euler(0f, yaw / SubSteps, 0f) * _rot * Quaternion.Euler(pitch / SubSteps, 0f, 0f);
                _pos += _rot * Vector3.forward * (length / SubSteps) + shiftDir * (shift / SubSteps);
                var s = new Slice
                {
                    P = _pos, Rot = _rot, W = Mathf.Lerp(w0, width, t), Roll = Mathf.Lerp(r0, roll, e),
                    PipeL = Mathf.Lerp(l0, pipeL, e), PipeR = Mathf.Lerp(rr0, pipeR, e),
                };
                slices.Add(s);
                if (OffPath)
                {
                    KeepClear.Add(_pos);
                    continue;
                }
                Path.Add((_pos, Quaternion.Euler(0f, _rot.eulerAngles.y, 0f) * Vector3.right));
                PathUp.Add(s.Q * Vector3.up);
                PathSurface.Add(Surface ?? "");
            }
            _width = width;
            _roll = roll;
            _pipeL = pipeL;
            _pipeR = pipeR;
            bool curved = Mathf.Abs(yaw) > 0.01f || Mathf.Abs(pitch) > 0.01f || Mathf.Abs(shift) > 0.01f;
            AddToTile(slices, curved, Mathf.Abs(yaw) + Mathf.Abs(pitch), length, tunnel, walls);
            _segments++;

            Slice mid = slices[SubSteps / 2];
            if (pillars && _segments % 3 == 0) Pillar(mid.P);
            if (push) Accelerator(mid.P, mid.Q, Mathf.Max(w0, width), length + 0.5f);
            Distance += length;
        }

        // Tiles like the game's pieces: the curved or sloped pieces of one call join into one tile until it turns
        // MaxTileTurn or runs MaxTileLength; straights are one 10 m tile each (20 m on a wide road); tunnel and wall
        // pieces stay one tile each (tunnel walls are painted per tile, roofs fade per tile). A change of surface or
        // season always starts a new tile.
        private void AddToTile(List<Slice> slices, bool curved, float turn, float length, bool tunnel, bool walls)
        {
            CheckSeam(slices[0]);
            _end = slices[slices.Count - 1];
            float straightMax = slices[slices.Count - 1].W >= WideRoad ? 20f : DefaultLength;
            bool joins = _open != null && _open.Call == _call && !tunnel && !walls && !_open.Tunnel && !_open.Walls &&
                         _open.Surface == (Surface ?? "") && _open.Season == (Season ?? "") && _open.Curved == curved && _open.Fake == Fake &&
                         _open.Length + length <= (curved ? MaxTileLength : straightMax) + 0.01f &&
                         (!curved || _open.Turn + turn <= MaxTileTurn + 0.01f);
            if (!joins)
            {
                Flush();
                _open = new OpenTile
                {
                    Section = _section, Call = _call, Surface = Surface ?? "", Season = Season ?? "",
                    Tunnel = tunnel, Walls = walls, Curved = curved, Fake = Fake,
                };
                _open.Slices.Add(slices[0]);
            }
            for (int k = 1; k < slices.Count; k++) _open.Slices.Add(slices[k]);
            _open.Turn += turn;
            _open.Length += length;
        }

        // A piece must start with the cross-section the last one ended with: both border edges within 5 cm and the
        // same wall heights (a fork lane's cut aside: it is half of the same road).
        private void CheckSeam(Slice next)
        {
            if (_end == null) return;
            float worst = Mathf.Max(Mathf.Abs(_end.PipeL - next.PipeL), Mathf.Abs(_end.PipeR - next.PipeR));
            foreach (int side in new[] { -1, 1 })
            {
                worst = Mathf.Max(worst, (EdgePoint(_end, side) - EdgePoint(next, side)).magnitude);
            }
            if (worst > 0.05f) _seams.Add($"{worst:0.00} m step at {next.P} (section '{(_section != null ? _section.name : "")}')");
        }

        private static Vector3 EdgePoint(Slice s, int side) => s.P + s.Q * new Vector3(side * (s.W / 2f + BorderWidth), 0f, 0f);

        // Builds the tile being laid: the road (asphalt and its pipe walls), a flat 1 m concrete border on each side
        // at the road's edge (on top of a pipe wall), and in loops/tunnels the walls and roof. Same layout as the
        // game's own pieces (R###B101 with Border…I000 / I001).
        private void Flush()
        {
            if (_open == null) return;
            OpenTile tile = _open;
            _open = null;
            List<Slice> s = tile.Slices;
            bool arcs = s.Exists(x => x.PipeL > 0.01f || x.PipeR > 0.01f);

            bool solid = !tile.Fake;
            var go = new GameObject(solid ? $"Road_{TileCount:000}" : $"Decoy_{_decoys++:000}");
            go.transform.SetParent(tile.Section, false);
            go.transform.SetPositionAndRotation(s[0].P, s[0].Q);
            Mesh mesh = BuildSurface(s, go.transform, go.name, x => RoadTop(x, arcs), Thickness, advanceUv: true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = AsphaltMat;   // preview; the game applies its surface
            if (!solid)
            {
                // A decoy: the host's road asphalt (KitRoad Dark), concrete borders, nothing to stand on.
                T.AddRoad(go, KitRoadKind.Dark);
                Border(go, s, "BorderL", x => Lip(x, arcs, -1), Thickness, solid: false);
                Border(go, s, "BorderR", x => Lip(x, arcs, 1), Thickness, solid: false);
                return;
            }
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            KitRoad kitRoad = T.AddRoad(go, KitRoadKind.Road);
            kitRoad.surface = tile.Surface;
            kitRoad.season = tile.Season;
            kitRoad.look = Look ?? "";

            var frames = s.ConvertAll(x => (x.P, x.Q, x.W));
            if (tile.Tunnel)
            {
                // A new tunnel unless this tile carries straight on from the last tunnel tile (a fork lane jumps).
                var last = Tunnels.Count > 0 && Tunnels[Tunnels.Count - 1].Tiles.Count > 0
                    ? Tunnels[Tunnels.Count - 1].Tiles[Tunnels[Tunnels.Count - 1].Tiles.Count - 1].frames : null;
                bool continues = _inTunnel && last != null && (last[last.Count - 1].p - frames[0].Item1).magnitude < 0.5f;
                if (!continues) Tunnels.Add(new TunnelInfo());
                Tunnels[Tunnels.Count - 1].Tiles.Add((go, frames, tile.Season));
            }
            _inTunnel = tile.Tunnel;

            Border(go, s, "BorderL", x => Lip(x, arcs, -1), Thickness);
            Border(go, s, "BorderR", x => Lip(x, arcs, 1), Thickness);
            if (tile.Walls || tile.Tunnel)
            {
                float edge = BorderWidth + WallThickness;
                float height = tile.Tunnel ? TunnelHeight : WallHeight;
                Border(go, s, "WallL", x => Span(x, -x.W / 2f - edge, -x.W / 2f - BorderWidth, height), height + Thickness);
                Border(go, s, "WallR", x => Span(x, x.W / 2f + BorderWidth, x.W / 2f + edge, height), height + Thickness);
                if (tile.Tunnel)
                {
                    Border(go, s, "Roof", x => Span(x, -x.W / 2f - edge, x.W / 2f + edge, TunnelHeight + 0.8f), 0.8f);
                }
            }
            TileCount++;
        }

        // Concrete borders and walls: game road tiles that the loader paints with the host level's concrete. A strip
        // cut away entirely (the inner border where a fork's lanes still touch) isn't built.
        private void Border(GameObject road, List<Slice> slices, string name, Func<Slice, List<Vector2>> top, float thickness, bool solid = true)
        {
            float widest = 0f;
            foreach (Slice x in slices)
            {
                List<Vector2> p = top(x);
                widest = Mathf.Max(widest, p[p.Count - 1].x - p[0].x);
            }
            if (widest < 0.01f) return;
            var go = new GameObject(name);
            go.transform.SetParent(road.transform, false);
            Mesh mesh = BuildSurface(slices, go.transform, $"{road.name}_{name}", top, thickness);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ConcreteMat;
            if (!solid)
            {
                T.AddRoad(go, KitRoadKind.Concrete);   // a decoy's border: decoration
                return;
            }
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            T.AddRoad(go, KitRoadKind.Border);
        }

        // The top of the road across a slice, left to right, in the slice's frame (x right, y up): the flat asphalt,
        // with a quarter-pipe arc (PipeRadius) rising on a side that has a wall; `arcs` keeps the same number of
        // points on every slice of a tile (an arc 0 m high is a point).
        private static List<Vector2> RoadTop(Slice s, bool arcs)
        {
            var points = new List<Vector2>();
            float half = s.W / 2f;
            if (arcs)
            {
                for (int i = ArcSteps; i > 0; i--) points.Add(ArcPoint(-1, half, s.PipeL, i));
            }
            points.Add(new Vector2(-half, 0f));
            points.Add(new Vector2(half, 0f));
            if (arcs)
            {
                for (int i = 1; i <= ArcSteps; i++) points.Add(ArcPoint(1, half, s.PipeR, i));
            }
            return Clip(points, s);
        }

        private static Vector2 ArcPoint(int side, float half, float height, int i)
        {
            float max = Mathf.Acos(1f - Mathf.Clamp(height, 0f, PipeRadius) / PipeRadius);
            float a = max * i / ArcSteps;
            return new Vector2(side * (half + PipeRadius * Mathf.Sin(a)), PipeRadius * (1f - Mathf.Cos(a)));
        }

        // A border: 1 m flat, outward from the road's edge (the top of a pipe wall).
        private static List<Vector2> Lip(Slice s, bool arcs, int side)
        {
            float half = s.W / 2f;
            Vector2 edge = arcs ? ArcPoint(side, half, side < 0 ? s.PipeL : s.PipeR, ArcSteps) : new Vector2(side * half, 0f);
            var points = side < 0
                ? new List<Vector2> { new Vector2(edge.x - BorderWidth, edge.y), edge }
                : new List<Vector2> { edge, new Vector2(edge.x + BorderWidth, edge.y) };
            return Clip(points, s);
        }

        private static List<Vector2> Span(Slice s, float left, float right, float top) =>
            Clip(new List<Vector2> { new Vector2(left, top), new Vector2(right, top) }, s);

        private static List<Vector2> Clip(List<Vector2> points, Slice s)
        {
            for (int i = 0; i < points.Count; i++)
            {
                points[i] = new Vector2(Mathf.Clamp(points[i].x, s.ClipL, s.ClipR), points[i].y);
            }
            return points;
        }

        // A solid strip along the slices: the top cross-section `top` (left to right), the same `thickness` below it,
        // and the two sides. uv0 = across / along the road (10 m per unit), uv1 = across / along the tile.
        private Mesh BuildSurface(List<Slice> slices, Transform owner, string name, Func<Slice, List<Vector2>> top,
                                  float thickness, bool advanceUv = false)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var uvs2 = new List<Vector2>();
            var tris = new List<int>();
            List<List<Vector2>> tops = slices.ConvertAll(s => top(s));
            int cols = tops[0].Count, rows = slices.Count;
            var along = new float[rows];
            float dist = _uvDistance;
            for (int k = 0; k < rows; k++)
            {
                if (k > 0) dist += Vector3.Distance(slices[k - 1].P, slices[k].P);
                along[k] = dist;
            }
            Vector3 At(int k, Vector2 c) => owner.InverseTransformPoint(slices[k].P + slices[k].Q * new Vector3(c.x, c.y, 0f));
            void Add(Vector3 v, float u, int k)
            {
                verts.Add(v);
                uvs.Add(new Vector2(u, along[k] / 10f));
                uvs2.Add(new Vector2(u, rows == 1 ? 0f : (float)k / (rows - 1)));
            }

            // Top and bottom: one grid each (shared vertices, so pipe walls shade smoothly).
            foreach (bool upper in new[] { true, false })
            {
                int first = verts.Count;
                for (int k = 0; k < rows; k++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        Vector2 p = tops[k][c];
                        if (!upper) p.y -= thickness;
                        Add(At(k, p), cols == 1 ? 0f : (float)c / (cols - 1), k);
                    }
                }
                for (int k = 0; k < rows - 1; k++)
                {
                    for (int c = 0; c < cols - 1; c++)
                    {
                        int v0 = first + k * cols + c;
                        Vector2 d = tops[k][c + 1] - tops[k][c];
                        Vector3 n = d.sqrMagnitude > 1e-8f ? new Vector3(-d.y, d.x, 0f) : Vector3.up;
                        if (!upper) n = -n;
                        AddQuad(tris, verts, v0, v0 + 1, v0 + cols, v0 + cols + 1, owner.InverseTransformDirection(slices[k].Q * n));
                    }
                }
            }
            // The left and right sides, top to bottom.
            foreach (int c in new[] { 0, cols - 1 })
            {
                int first = verts.Count;
                for (int k = 0; k < rows; k++)
                {
                    Vector2 p = tops[k][c];
                    Add(At(k, p), 0f, k);
                    Add(At(k, p - new Vector2(0f, thickness)), 1f, k);
                }
                Vector3 n = c == 0 ? Vector3.left : Vector3.right;
                for (int k = 0; k < rows - 1; k++)
                {
                    int v0 = first + 2 * k;
                    AddQuad(tris, verts, v0, v0 + 1, v0 + 2, v0 + 3, owner.InverseTransformDirection(slices[k].Q * n));
                }
            }
            if (advanceUv) _uvDistance = along[rows - 1];

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            // Stock road meshes carry a second UV set and tangents. The game's road shader is triplanar
            // with strong rim lighting built from normal + tangent; without tangents the far road (seen at
            // grazing angles) glows pale green.
            mesh.SetUVs(1, uvs2);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            SaveMesh(mesh);
            return mesh;
        }

        // v0/v1 across one slice, v2/v3 the same on the next. Unity's front face is Cross(B - A, C - A): face outwards.
        private static void AddQuad(List<int> tris, List<Vector3> v, int v0, int v1, int v2, int v3, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(v[v2] - v[v0], v[v3] - v[v0]), outward) >= 0f)
                tris.AddRange(new[] { v0, v2, v3, v0, v3, v1 });
            else
                tris.AddRange(new[] { v0, v3, v2, v0, v1, v3 });
        }

        // All track meshes live in one asset next to the scene so the bundle can carry them.
        private void SaveMesh(Mesh mesh)
        {
            string path = $"{_folder}/TrackMeshes.asset";
            if (!_meshAssetReady)
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                _meshAssetReady = true;
            }
            else
            {
                AssetDatabase.AddObjectToAsset(mesh, path);
            }
        }

        // Writes <folder>/track.json: the road's centre line in map space (the KitMap root's local frame), one point
        // every ~1.7 m with the road's up vector and whether it is a jump gap. BuildAndInstall ships it next to the
        // bundle; the automated tests (mods/TestHarness) use it to check and drive the track.
        public void WriteTrackJson(string folder)
        {
            // Version 2: each point is [x, y, z, upX, upY, upZ, gap (0/1), surface index into "surfaces"].
            var names = new List<string>();
            foreach (string s in PathSurface) if (!names.Contains(s)) names.Add(s);
            var sb = new System.Text.StringBuilder("{\n  \"version\": 2,\n  \"surfaces\": [");
            sb.Append(string.Join(", ", names.ConvertAll(n => "\"" + n + "\"")));
            sb.Append("],\n  \"points\": [\n");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < Path.Count; i++)
            {
                Vector3 p = Root != null ? Root.InverseTransformPoint(Path[i].p) : Path[i].p;
                Vector3 up = i < PathUp.Count ? PathUp[i] : Vector3.up;
                if (Root != null) up = Root.InverseTransformDirection(up);
                int surface = i < PathSurface.Count ? names.IndexOf(PathSurface[i]) : 0;
                sb.Append(string.Format(inv, "    [{0:0.###}, {1:0.###}, {2:0.###}, {3:0.###}, {4:0.###}, {5:0.###}, {6}, {7}]",
                    p.x, p.y, p.z, up.x, up.y, up.z, AirPoints.Contains(i) ? 1 : 0, surface));
                sb.Append(i < Path.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "track.json"), sb.ToString());
        }

        // Warns about parts of the track that come close to each other by accident: less than 12 m apart
        // sideways and less than 10 m apart in height, more than ~100 m apart along the road.
        // Generators call this last: writes track.json and, in batch builds, refuses a track that is too close
        // to itself or has a step between two pieces (an interactive build only warns, like before).
        public void FinishChecks(string folder, bool interactive)
        {
            Flush();
            WriteTrackJson(folder);
            int problems = CheckClearance();
            foreach (string seam in _seams)
            {
                Debug.LogWarning($"[Slip & Skid] Road seam: {seam}. A piece must start where the last one ended (same bank, width and walls).");
            }
            if (_seams.Count == 0) Debug.Log("[Slip & Skid] Road seams OK.");
            // Faces of different colours in one plane flicker in game (DecorCheck): road, decor and gimmicks alike.
            List<string> flicker = Root != null ? DecorCheck.CoplanarFaces(Root) : new List<string>();
            foreach (string f in flicker)
            {
                Debug.LogWarning($"[Slip & Skid] Flickering faces: {f}. Move one of them 5 cm, or give them one colour.");
            }
            if (flicker.Count == 0) Debug.Log("[Slip & Skid] No flickering faces.");
            // Materials the game would never colour (DecorCheck.Undrawn): invisible in game, only their outline shows.
            List<string> undrawn = Root != null ? DecorCheck.Undrawn(Root) : new List<string>();
            foreach (string u in undrawn)
            {
                Debug.LogWarning($"[Slip & Skid] Not drawn in game: {u}.");
            }
            if (undrawn.Count == 0) Debug.Log("[Slip & Skid] Every renderer is drawn in game.");
            if ((problems > 0 || _seams.Count > 0 || flicker.Count > 0 || undrawn.Count > 0) && !interactive)
                throw new System.Exception($"[Slip & Skid] Track checks: {problems} place(s) too close to itself, {_seams.Count} road seam(s), " +
                                           $"{flicker.Count} pair(s) of flickering faces, {undrawn.Count} renderer(s) not drawn in game (see the warnings above).");
        }

        public int CheckClearance()
        {
            int problems = 0;
            for (int i = 0; i < Path.Count && problems < 10; i += 3)
            {
                for (int j = i + 60; j < Path.Count; j += 3)
                {
                    Vector3 a = Path[i].p, c = Path[j].p;
                    if (Mathf.Abs(a.y - c.y) >= 10f) continue;
                    if (new Vector2(a.x - c.x, a.z - c.z).magnitude >= 12f) continue;
                    if (_closeOnPurpose.Exists(r => i >= r.from && i <= r.to && j >= r.from && j <= r.to)) continue;
                    Debug.LogWarning($"[Slip & Skid] Track too close to itself near {a} (point {i}) and {c} (point {j}).");
                    problems++;
                    break;
                }
            }
            // A car leaving a loop by its side, or coming off at the top, must not land on other road (a tunnel
            // roof next to a loop's way down trapped cars: drive-fourseasons, 2026-10-24).
            foreach (var (from, to) in _loops)
            {
                float low = float.MaxValue, high = float.MinValue;
                for (int i = from; i <= to; i++)
                {
                    low = Mathf.Min(low, Path[i].p.y);
                    high = Mathf.Max(high, Path[i].p.y);
                }
                for (int j = 0; j < Path.Count && problems < 10; j += 2)
                {
                    if (j >= from - 60 && j <= to + 60) continue;
                    Vector3 c = Path[j].p;
                    if (c.y < low - 10f || c.y > high + 10f) continue;
                    for (int i = from; i <= to; i += 2)
                    {
                        Vector3 a = Path[i].p;
                        if (new Vector2(a.x - c.x, a.z - c.z).magnitude >= LoopClearance) continue;
                        Debug.LogWarning($"[Slip & Skid] Road {new Vector2(a.x - c.x, a.z - c.z).magnitude:0} m from a loop (< {LoopClearance} m) near {a} (point {i}) and {c} (point {j}).");
                        problems++;
                        j = Path.Count;
                        break;
                    }
                }
            }
            if (problems == 0) Debug.Log("[Slip & Skid] Track clearance OK.");
            return problems;
        }

        // ------------------------------------------------------------------ 1.3.0: tricks (MapKitLoader 1.3.0+)

        // Road off the driving line: a shortcut ramp behind a fake wall, a side road to a portal, a dead end. Built
        // from `start` heading `heading` (flat), then the builder is back where it was. Its points go to KeepClear
        // (decoration avoids them), not to track.json: the autopilot and CheckClearance ignore it.
        public void Spur(Vector3 start, Quaternion heading, Action build)
        {
            Flush();
            var saved = (_pos, _rot, _width, _roll, _pipeL, _pipeR, _end, Surface, Season, Look);
            _pos = start;
            _rot = heading;
            _width = -1f;   // the first piece starts at its own width
            _roll = _pipeL = _pipeR = 0f;
            _end = null;
            _spurDepth++;
            try
            {
                build();
                Flush();
            }
            finally
            {
                _spurDepth--;
                (_pos, _rot, _width, _roll, _pipeL, _pipeR, _end, Surface, Season, Look) = saved;
            }
        }

        // A decoy road straight on from here while the real road – built by `real`, flat (no bank) until they have
        // parted – turns away to the other side: the decoy is cut along the real road's outer border edge, so it
        // peels out beside it like the stock Y pieces (no overlap, no step), and is plain decoration (Fake). side = -1:
        // the decoy is on the left (the real road turns right), +1 on the right. Then the builder carries on from the
        // end of `real`.
        public void DecoyFork(int decoyTiles, int side, Action real)
        {
            Flush();
            if (Mathf.Abs(_roll) > 0.5f) Debug.LogWarning("[Slip & Skid] DecoyFork on a banked road: build a flat piece first.");
            Vector3 start = _pos;
            Quaternion heading = Flat;
            Vector3 forward = heading * Vector3.forward, across = heading * Vector3.right;
            float width = RoadWidth, outer = width / 2f + BorderWidth;
            int from = Path.Count;
            real();
            Flush();
            // The real road's outer border edge on the decoy's side, as (distance along the decoy, offset across it),
            // from the junction itself (the driving line's first point is a step on: the decoy's first 1.6 m were
            // left uncut and lay on the real road).
            var edge = new List<Vector2> { new Vector2(0f, side * outer) };
            for (int i = from; i < Path.Count; i++)
            {
                Vector3 e = Path[i].p + Path[i].right * (side * outer) - start;
                edge.Add(new Vector2(Vector3.Dot(e, forward), Vector3.Dot(e, across)));
            }
            var saved = (_pos, _rot, _width, _roll, _pipeL, _pipeR, _end, Surface, Season, Look);
            Fake = true;
            _spurDepth++;
            _end = null;
            try
            {
                int perTile = SubSteps * 2;
                var slices = new List<Slice>();
                for (int k = 0; k <= decoyTiles * perTile; k++)
                {
                    float z = DefaultLength * k / perTile;
                    // Where the real road's edge crosses this cross-section (none past its end: parted).
                    float cut = float.NaN;
                    for (int j = 1; j < edge.Count && float.IsNaN(cut); j++)
                    {
                        if ((edge[j - 1].x - z) * (edge[j].x - z) > 0f || Mathf.Abs(edge[j].x - edge[j - 1].x) < 1e-5f) continue;
                        cut = Mathf.Lerp(edge[j - 1].y, edge[j].y, (z - edge[j - 1].x) / (edge[j].x - edge[j - 1].x));
                    }
                    var s = new Slice { P = start + forward * z, Rot = heading, W = width };
                    if (!float.IsNaN(cut))
                    {
                        if (side * cut >= outer - 0.01f) continue;   // still all under the real road
                        cut += side * 0.02f;   // 2 cm short of the edge: coplanar overlapping faces flicker
                        if (side < 0) s.ClipR = cut; else s.ClipL = cut;
                    }
                    slices.Add(s);
                    KeepClear.Add(s.P);
                }
                // One decoration piece per 10 m, as a straight road would be.
                for (int a = 0; a + 1 < slices.Count; a += perTile)
                {
                    _call++;
                    List<Slice> part = slices.GetRange(a, Mathf.Min(perTile + 1, slices.Count - a));
                    AddToTile(part, curved: false, turn: 0f, length: (part[part.Count - 1].P - part[0].P).magnitude, tunnel: false, walls: false);
                    Flush();
                }
            }
            finally
            {
                Fake = false;
                _spurDepth--;
                (_pos, _rot, _width, _roll, _pipeL, _pipeR, _end, Surface, Season, Look) = saved;
            }
        }

        // Builds `build`'s road under a new object and returns it, for whatever must act on that stretch only: a
        // KitMover (see Moving), a KitRoundChange (an eruption that turns it to magma from race 3)... The object sits
        // at `pivot` (default: where the road is now), turned to the road's heading, so its x is the road's right.
        public GameObject Group(string name, Action build, Vector3? pivot = null)
        {
            Flush();
            GameObject g = T.Child(_section, name);
            g.transform.SetPositionAndRotation(pivot ?? _pos, Flat);
            Transform saved = _section;
            _section = g.transform;
            try
            {
                build();
                Flush();
            }
            finally
            {
                _section = saved;
            }
            return g;
        }

        // A stretch of road on a moving part: configure the returned KitMover (mode, moveBy/rotateBy in the road's
        // frame, period...). A drawbridge: pivot at the gap's near edge, PingPong rotateBy (-75, 0, 0).
        public KitMover Moving(string name, Action build, Vector3? pivot = null) => Group(name, build, pivot).AddComponent<KitMover>();

        // A barrel roll: the road turns a full 360° around the direction of travel over `length` m – a corkscrew you
        // drive along the wall, upside down at the top and back down. Its centre circles an axis `radius` m above the
        // road, facing it; a row of gravity spheres along that axis glues cars to it (like a loop) and accelerators keep
        // them going. Starts and ends flat, heading the same way; low walls on both sides.
        public void BarrelRoll(float length = 120f, float radius = 9f, bool clockwise = true)
        {
            Flush();
            Quaternion heading = Flat;
            Vector3 forward = heading * Vector3.forward;
            Vector3 axis = _pos + Vector3.up * radius;
            float width = RoadWidth;
            int n = Mathf.CeilToInt(length / (DefaultLength / SubSteps));
            n += (4 - n % 4) % 4;   // four quarters, one tile each
            float sign = clockwise ? 1f : -1f;
            var slices = new List<Slice>();
            int first = Path.Count;
            LoopCount++;
            Transform roll = T.Child(_section, $"BarrelRoll_{LoopCount}").transform;
            Transform prev = _section;
            _section = roll;
            // The centre line is a helix round the axis; each cross-section faces along the helix (up towards the
            // axis), so the road is straight in its own plane and a car needs no steering. Facing straight ahead
            // instead (before 2026-10-26) sheared the road up to 35° sideways under the car: it slid onto the low
            // wall, crawled over the top at 4 m/s and came out sliding sideways.
            Vector3 At(float t)
            {
                float e = t * t * (3f - 2f * t);
                return axis + forward * (length * t) + Quaternion.AngleAxis(sign * 360f * e, forward) * (Vector3.down * radius);
            }
            for (int k = 0; k <= n; k++)
            {
                float t = (float)k / n, e = t * t * (3f - 2f * t);
                float angle = sign * 360f * e;
                Quaternion turn = Quaternion.AngleAxis(angle, forward);
                Vector3 along = At(Mathf.Min(1f, t + 0.001f)) - At(Mathf.Max(0f, t - 0.001f));
                var s = new Slice { P = At(t), Rot = Quaternion.LookRotation(along, turn * Vector3.up), Roll = 0f, W = width };
                slices.Add(s);
                if (k == 0) continue;
                if (OffPath)
                {
                    KeepClear.Add(s.P);
                    continue;
                }
                Path.Add((s.P, s.Q * Vector3.right));
                PathUp.Add(s.Q * Vector3.up);
                PathSurface.Add(Surface ?? "");
            }
            int quarter = n / 4;
            for (int q = 0; q < 4; q++)
            {
                _call++;
                List<Slice> part = slices.GetRange(q * quarter, quarter + 1);
                AddToTile(part, curved: true, turn: 90f, length: length / 4f, tunnel: false, walls: true);
                Slice mid = part[quarter / 2];
                Accelerator(mid.P, mid.Q, width, length / 4f);
                Flush();
            }
            _section = prev;
            GravityRow(roll, "RollGravity", axis, forward, length, radius);
            _pos = slices[n].P;
            _rot = heading;
            _roll = _pipeL = _pipeR = 0f;
            _width = width;
            _end = new Slice { P = _pos, Rot = heading, W = width };   // 360° = flat again
            Distance += length;
            if (!OffPath)
            {
                _loops.Add((first, Path.Count - 1));
                _closeOnPurpose.Add((first - 10, Path.Count + 10));
            }
        }

        // A marker on the road here (on its surface, `ahead` m on along its slope, `lateral` m to the right): portal
        // exits, landmarks, the decor's spots. Level by default – heading only, upright – since the bank of the bend
        // just built is gone a piece later (rolled marks right after a banked bend tilted the Ice Factory pistons
        // half into the road); level: false = the road's own frame with slope and bank.
        public Transform Mark(string name, float ahead = 0f, float lateral = 0f, bool level = true)
        {
            Quaternion q = level ? Flat : _rot * Quaternion.Euler(0f, 0f, _roll);
            GameObject go = T.Child(_section, name);
            go.transform.SetPositionAndRotation(_pos + _rot * new Vector3(lateral, 0f, ahead), q);
            return go.transform;
        }

        // A portal standing on the road here (KitPortal): a trigger as wide as `width` (0 = the whole road, borders
        // included) centred `lateral` m to the right, and – with a Sculpt – a glowing ring around a swirling disc you
        // can't see through. Set its exit afterwards: portal.exit = b.Mark("...") where cars must come out.
        public KitPortal Portal(string name, Sculpt sculpt = null, Material ring = null, Material swirl = null, float width = 0f, float lateral = 0f)
        {
            float w = width > 0f ? width : RoadWidth + 2f * BorderWidth;
            float r = w * 0.5f + 0.5f;
            Quaternion q = Flat;
            GameObject go = T.Child(_section, name);
            go.transform.SetPositionAndRotation(_pos + q * new Vector3(lateral, 0f, 0f), q);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, r * 0.8f, 0f);
            box.size = new Vector3(w, r * 1.6f, 3f);   // 3 m deep: a car at 60 m/s moves 1.2 m per physics step
            KitPortal portal = go.AddComponent<KitPortal>();
            if (sculpt != null && ring != null && swirl != null)
            {
                sculpt.Glowing(sculpt.Torus(0.08f), go.transform, new Vector3(0f, r, 0f), Quaternion.identity, Vector3.one * r, ring, "Portal ring");
                sculpt.Glowing(sculpt.Torus(0.05f), go.transform, new Vector3(0f, r, -0.4f), Quaternion.identity, Vector3.one * (r * 1.12f), ring, "Portal halo");
                sculpt.Glowing(sculpt.Disc(), go.transform, new Vector3(0f, r, 0.2f), Quaternion.identity, Vector3.one * (r * 0.93f), swirl, "Portal swirl");
            }
            return portal;
        }

        // Wind over the next `length` m of road from here (KitForceZone): pushes to the right (side 1) or the left
        // (-1) at `acceleration` m/s², in gusts when gustPeriod > 0. Build the road it covers straight.
        public KitForceZone Wind(float length, int side, float acceleration, float gustPeriod = 0f, float gustOn = 0.5f, float phase = 0f, float height = 6f)
        {
            Quaternion q = Flat;
            GameObject go = T.Child(_section, side > 0 ? "Wind (to the right)" : "Wind (to the left)");
            go.transform.SetPositionAndRotation(_pos + q * new Vector3(0f, height / 2f, length / 2f), q * Quaternion.Euler(0f, side > 0 ? 90f : -90f, 0f));
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(length, height, RoadWidth + 2f * BorderWidth + 4f);
            KitForceZone z = go.AddComponent<KitForceZone>();
            z.acceleration = acceleration;
            z.gustPeriod = gustPeriod;
            z.gustOn = gustOn;
            z.phase = phase;
            return z;
        }

        // A geyser (KitForceZone pointing up) at `at`: cars driving over it are thrown up at launchSpeed – while it
        // erupts, when gustPeriod > 0.
        public KitForceZone Geyser(Vector3 at, float radius, float launchSpeed, float gustPeriod = 0f, float gustOn = 0.5f, float phase = 0f)
        {
            GameObject go = T.Child(_section, "Geyser");
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.up, Flat * Vector3.forward));
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0f, 1.5f);
            box.size = new Vector3(2f * radius, 2f * radius, 3f);
            KitForceZone z = go.AddComponent<KitForceZone>();
            z.acceleration = 0f;
            z.launchSpeed = launchSpeed;
            z.gustPeriod = gustPeriod;
            z.gustOn = gustOn;
            z.phase = phase;
            return z;
        }

        // A low-gravity field over the next `length` m from here (KitForceZone pushing up at `lift` m/s² against the
        // game's 40): a jump inside it floats – lift 26 leaves 14, about three times the hang time and distance.
        public KitForceZone LowGravity(float length, float width, float height, float lift = 26f)
        {
            Quaternion q = Flat;
            GameObject go = T.Child(_section, "Low-gravity field");
            go.transform.SetPositionAndRotation(_pos + q * new Vector3(0f, height / 2f - 8f, length / 2f), Quaternion.LookRotation(Vector3.up, q * Vector3.forward));
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width, length, height);   // local z is up, local y along the road
            KitForceZone z = go.AddComponent<KitForceZone>();
            z.acceleration = lift;
            return z;
        }

        // A fog zone over the next `length` m from here (KitFogZone): the fog closes in to `fogEnd` m inside it.
        public KitFogZone FogZone(float length, float width, float height, float fogEnd = 45f, float fogStart = 4f, float blend = 30f)
        {
            Quaternion q = Flat;
            GameObject go = T.Child(_section, "Fog zone");
            go.transform.SetPositionAndRotation(_pos + q * new Vector3(0f, height / 2f - 10f, length / 2f), q);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width, height, length);
            KitFogZone z = go.AddComponent<KitFogZone>();
            z.fogEnd = fogEnd;
            z.fogStart = fogStart;
            z.blend = blend;
            return z;
        }

        // A solid block (a vault door, a piston, a sweeping arm): a road tile of kind Border, so cars hit it like a wall
        // (box collider + the RoadCombined copy the build adds), painted with a game material (MapKitLoader 1.1.0+).
        public GameObject SolidBlock(Transform parent, string name, Vector3 centre, Quaternion rotation, Vector3 size, string paint = "", Color tint = default)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, rotation);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = ConcreteMat;
            KitRoad kit = T.AddRoad(go, KitRoadKind.Border);
            kit.paint = paint ?? "";
            kit.tint = tint;
            return go;
        }

        // A turntable: a round platform `diameter` m across, turning `degreesPerSecond` (KitMover Spin), that the road
        // crosses straight through its centre (round, so it always lines up). `arms` solid barriers stand on its rim and
        // sweep across the way in and out: cross between them. Returns the mover.
        public KitMover Turntable(float diameter, float degreesPerSecond, int arms = 0, string paint = "ToonMetal", Color armTint = default)
        {
            Flush();
            Quaternion heading = Flat;
            Vector3 forward = heading * Vector3.forward;
            float radius = diameter / 2f;
            // The road's square end pokes into the disc by 1 m so its corners don't leave gaps at the round edge.
            Vector3 centre = _pos + forward * (radius - 1f);
            GameObject g = T.Child(_section, "Turntable");
            g.transform.SetPositionAndRotation(centre, heading);
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = $"Road_{TileCount:000}_Turntable";
            UnityEngine.Object.DestroyImmediate(disc.GetComponent<Collider>());   // a capsule: not flat
            disc.transform.SetParent(g.transform, false);
            disc.transform.localPosition = new Vector3(0f, -Thickness / 2f - 0.02f, 0f);
            disc.transform.localScale = new Vector3(diameter, Thickness / 2f, diameter);
            disc.GetComponent<MeshRenderer>().sharedMaterial = AsphaltMat;
            disc.AddComponent<MeshCollider>().sharedMesh = disc.GetComponent<MeshFilter>().sharedMesh;
            KitRoad road = T.AddRoad(disc, KitRoadKind.Road);
            road.surface = Surface ?? "";
            road.season = Season ?? "";
            TileCount++;
            for (int a = 0; a < arms; a++)
            {
                float ang = a * 360f / arms;
                Quaternion q = heading * Quaternion.Euler(0f, ang, 0f);
                SolidBlock(g.transform, $"Arm_{a}", centre + q * Vector3.forward * (radius * 0.78f) + Vector3.up * 0.75f, q,
                    new Vector3(1.2f, 1.5f, radius * 0.44f), paint, armTint);
            }
            KitMover m = g.AddComponent<KitMover>();
            m.mode = KitMoverMode.Spin;
            m.rotateBy = new Vector3(0f, degreesPerSecond, 0f);
            float across = diameter - 2f;
            int steps = Mathf.CeilToInt(across / (DefaultLength / SubSteps));
            for (int k = 1; k <= steps; k++)
            {
                Vector3 p = _pos + forward * (across * k / steps);
                if (OffPath)
                {
                    KeepClear.Add(p);
                    continue;
                }
                Path.Add((p, heading * Vector3.right));
                PathUp.Add(Vector3.up);
                PathSurface.Add(Surface ?? "");
            }
            _pos += forward * across;
            _rot = heading;
            _roll = _pipeL = _pipeR = 0f;
            _width = RoadWidth;
            _end = null;   // a round edge: no seam to match
            Distance += across;
            return m;
        }

        // A decoy finish arch `ahead` m on (KitFakeFinish: the loader copies the real arch's look, nothing else).
        public void FakeFinish(float ahead = 0f)
        {
            T.Marker<KitFakeFinish>(_section, "Fake finish", _pos + Flat * Vector3.forward * ahead + Vector3.up * T.FinishLineHeight, Flat);
        }

        public void Boost()
        {
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "BoostPad";
            pad.transform.SetParent(_section, false);
            Quaternion q = _rot * Quaternion.Euler(0f, 0f, _roll);   // lies on a banked road too
            pad.transform.SetPositionAndRotation(_pos + q * Vector3.up * 0.05f, q);
            // As wide as the road here (always 8 m before: half of a 16 m landing), 5 cm in from its sides: coplanar faces flicker.
            float width = RoadWidth - 0.1f;
            pad.transform.localScale = new Vector3(width, 0.1f, 5f);
            pad.GetComponent<BoxCollider>().isTrigger = true;
            pad.GetComponent<MeshRenderer>().sharedMaterial = BoostMat;
            pad.AddComponent<KitBoostPad>().boostSpeed = 20f;
            // A dark rim round it, 4 cm thick and 2 cm higher, over its side faces: the chevrons squashed into the pad's
            // 10 cm sides read as a stack of paper. Decoration (painted, no collider), beside the pad (not under its scale).
            var rim = new GameObject("BoostPadRim");
            rim.transform.SetParent(_section, false);
            rim.transform.SetPositionAndRotation(_pos, q);
            float a = width / 2f;
            var bars = new[]
            {
                (new Vector3(-(a + 0.02f), 0.06f, 0f), new Vector3(0.04f, 0.12f, 5.08f)), (new Vector3(a + 0.02f, 0.06f, 0f), new Vector3(0.04f, 0.12f, 5.08f)),
                (new Vector3(0f, 0.06f, -2.52f), new Vector3(2f * a + 0.08f, 0.12f, 0.04f)), (new Vector3(0f, 0.06f, 2.52f), new Vector3(2f * a + 0.08f, 0.12f, 0.04f)),
            };
            foreach (var (centre, size) in bars)
            {
                GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "Rim";
                UnityEngine.Object.DestroyImmediate(bar.GetComponent<Collider>());
                bar.transform.SetParent(rim.transform, false);
                bar.transform.localPosition = centre;
                bar.transform.localScale = size;
                bar.GetComponent<MeshRenderer>().sharedMaterial = ConcreteMat;
                KitRoad kit = T.AddRoad(bar, KitRoadKind.Concrete);
                kit.paint = "ToonWood";
                kit.tint = new Color(0.12f, 0.12f, 0.15f);
            }
        }

        // Pushes cars along the track harder than gravity pulls them back, so loops work at any speed.
        // capAtBoostSpeed is what makes the loader honour continuousAcceleration.
        private void Accelerator(Vector3 surfaceCenter, Quaternion rot, float width, float length)
        {
            GameObject zone = T.Child(_section, "Accelerator");
            zone.transform.SetPositionAndRotation(surfaceCenter, rot);
            BoxCollider box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width, 4f, length);
            box.center = new Vector3(0f, 2f, 0f);
            KitBoostPad pad = zone.AddComponent<KitBoostPad>();
            pad.capAtBoostSpeed = true;
            pad.boostSpeed = 35f;
            pad.continuousAcceleration = 100f;
        }

        // Ramps up or down until the road is at the given height (within about a metre).
        public void ReachHeight(float targetY, bool pillars = false)
        {
            float dy = targetY - _pos.y;
            if (Mathf.Abs(dy) < 1.5f) return;
            float sign = dy > 0f ? -1f : 1f;   // negative pitch climbs
            Tiles(2, pitch: 5f * sign, pillars: pillars);
            float levelOut = DefaultLength * Mathf.Sin(5f * Mathf.Deg2Rad);
            float perTile = DefaultLength * Mathf.Sin(10f * Mathf.Deg2Rad);
            while (Mathf.Abs(targetY - _pos.y) > levelOut + perTile * 0.5f) Tiles(1, pillars: pillars);
            Tiles(2, pitch: -5f * sign, pillars: pillars);
        }

        // A row of boost pads: each one adds speed on top of the last.
        public void BoostRun(int pads)
        {
            for (int i = 0; i < pads; i++)
            {
                Boost();
                Tiles(1);
            }
        }

        public void Finish()
        {
            T.Marker<KitFinish>(_section, "Finish",
                _pos - Flat * Vector3.forward * 15f + Vector3.up * T.FinishLineHeight, Flat);
        }

        // Decoration only, no collider: you fall past them.
        private void Pillar(Vector3 roadCenter)
        {
            float top = roadCenter.y - 1f;
            float height = top - DeathHeight;
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            p.name = "Pillar";
            p.transform.SetParent(_section, false);
            p.transform.position = new Vector3(roadCenter.x, top - height * 0.5f, roadCenter.z);
            p.transform.localScale = new Vector3(3f, height * 0.5f, 3f);
            UnityEngine.Object.DestroyImmediate(p.GetComponent<Collider>());
            p.GetComponent<MeshRenderer>().sharedMaterial = ConcreteMat;
            T.AddRoad(p, KitRoadKind.Concrete);
        }
    }
}
