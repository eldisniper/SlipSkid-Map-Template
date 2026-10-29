using System.Collections.Generic;
using SlipSkid.MapKit.SDK;
using UnityEngine;
using Random = System.Random;

namespace SlipSkid.MapKit.Editor
{
    // Themed scenery for maps without seasons, instead of the kit's plain grey towers (AddDecorAlong):
    // - NightCity (Mega Stunt, The Heist, on the host's night-city look): dark skyscrapers in four silhouettes (setback
    //   tiers with a spire, a crowned box with neon corner strips, hexagonal and round towers), glowing bands of lit
    //   windows from the top down to 20 m under the lowest road (bright up high, sparser and dimmer lower), mullions,
    //   neon signs on some roofs, a red light on every top; giant billboards facing the road (dark boards, neon stripes,
    //   a glowing border and lettering or pictogram, on braced lattice legs); smooth glowing stunt hoops floating
    //   18–28 m up beside the road, turned away from its line;
    // - DrownedCity (Twin Tunnels, Rising Tide, mud: a drowned city in the rain): half-sunk, leaning towers in teal and
    //   plum, 32–60 m apart, of very different sizes, with a dark wet band at the waterline, a grid of dim windows partly
    //   lit, two neon bands, parapets round darker roof caps with water tanks, AC units and antennas with red lights
    //   (some on a setback tier), or broken tops (jagged slabs, rebar); clusters of glowing sky lanterns.
    // Every light is our Glow (unlit: the toon-painted neon was dull on shaded faces), parts of one colour merged into one
    // mesh per piece (MergeMesh, a few renderers each). AddFinish lights the finish (neon pylons, checkered banners, a
    // hoop, an end barrier, a light bar under the start edge); AddLoopRims puts neon rims on the loops.
    // Painted primitives (SeasonDecor.Prim: KitRoad Concrete + paint, MapKitLoader 1.1.0+), no colliders, every piece
    // kept clear of every part of the track (TrackBuilder.Path, jump flight paths included), of the ground between a
    // fork's lanes and of the podium. Under the map root's "City decor", one child per piece named "Night tower",
    // "Drowned tower", "Stunt hoop", "Billboard" or "Lanterns" (HeistDecor and RisingTideDecor find them by these names;
    // a tower's first child is its body, whose scale is its footprint and height).
    internal static class CityDecor
    {
        internal enum Theme { NightCity, DrownedCity }

        private static readonly Color[] Neon =
        {
            new Color(0.3f, 0.92f, 1f), new Color(1f, 0.35f, 0.8f), new Color(1f, 0.85f, 0.3f), new Color(0.55f, 1f, 0.5f),
        };

        private static readonly string[] SignWords = { "BAR", "CAFE", "HOTEL", "NEON", "TAXI", "DINER", "RAMEN", "MOTEL", "24H", "CLUB", "ARCADE" };
        private static readonly string[] BoardWords = { "MEGA|STUNT", "NITRO|BOOST", "BIG|AIR", "LOOP|IT!", "GO|GO GO" };

        // The glows and shapes one map's pieces share (one material per colour).
        private sealed class Kit
        {
            public Sculpt S;
            public Material[] Bright, Dim, Pulse;
            public Material Beacon, Window, Letters;
            public Mesh Hex, Round;
            public float Low, Sea;
        }

        private static Kit MakeKit(Transform root, TrackBuilder b, float low)
        {
            Sculpt s = Sculpt.For(b.Folder);
            KitMap map = root.GetComponent<KitMap>();
            var k = new Kit
            {
                S = s, Low = low, Sea = low - (map != null && map.deathDepth > 0f ? map.deathDepth : 30f),
                Bright = new Material[Neon.Length], Dim = new Material[Neon.Length], Pulse = new Material[Neon.Length],
            };
            for (int i = 0; i < Neon.Length; i++)
            {
                k.Bright[i] = s.Glow($"CityNeon{i}", Neon[i], Lighten(Neon[i], 0.35f));
                k.Dim[i] = s.Glow($"CityNeonDim{i}", Scale(Neon[i], 0.5f), Scale(Neon[i], 0.6f));
                k.Pulse[i] = s.Glow($"CityNeonPulse{i}", Neon[i], Lighten(Neon[i], 0.6f), pulse: 0.45f);
            }
            k.Beacon = s.Glow("CityBeacon", new Color(1f, 0.12f, 0.15f), new Color(1f, 0.55f, 0.5f), pulse: 0.5f);
            k.Window = s.Glow("CityWindow", new Color(0.92f, 0.66f, 0.32f), new Color(1f, 0.82f, 0.5f));
            k.Letters = s.Glow("CityLetters", new Color(1f, 0.97f, 0.9f), Color.white, pulse: 0.2f);
            k.Hex = UnitPrism(s, 6, "CityHexBody");
            k.Round = UnitPrism(s, 16, "CityRoundBody");
            return k;
        }

        // A prism of radius 0.5 and height 1 centred on the origin (a tower body scaled to its size: top = y + scale.y / 2).
        private static Mesh UnitPrism(Sculpt s, int sides, string name)
        {
            var m = new MergeMesh();
            m.Prism(Vector3.zero, 0.5f, 0.5f, 1f, sides);
            return m.Build(s, name);
        }

        public static int Add(Transform root, TrackBuilder b, Theme theme, int seed = 21)
        {
            var rng = new Random(seed);
            var gaps = new Random(seed * 7 + 3);
            Transform holder = new GameObject("City decor").transform;
            holder.SetParent(root, false);
            float low = float.MaxValue;
            foreach (var (p, _) in b.Path) low = Mathf.Min(low, p.y);
            var placed = new List<(Vector3 c, float r)>();
            var towers = new List<(Vector3 c, float r, float top)>();   // night towers, for the hoops' views
            var hoops = new List<Vector3>();
            Kit k = MakeKit(root, b, low);
            // The drowned city's sky lanterns glow (our Glow material, through the map's own Sculpt).
            Material lanternGlow = theme == Theme.DrownedCity
                ? k.S.Glow("CityLantern", new Color(1f, 0.72f, 0.32f), new Color(1f, 0.9f, 0.6f), bands: 1.2f, speed: 0.15f)
                : null;
            List<Vector2[]> forks = ForkAreas(b);
            List<(int from, int to)> loops = SeasonDecor.Loops(b);
            float sinceTower = 0f, sinceExtra = 0f, towerGap = theme == Theme.DrownedCity ? Mathf.Lerp(32f, 60f, (float)gaps.NextDouble()) : 32f;
            int side = 1, count = 0;
            float R(float a, float c) => Mathf.Lerp(a, c, (float)rng.NextDouble());

            for (int i = 1; i < b.Path.Count; i++)
            {
                float step = (b.Path[i].p - b.Path[i - 1].p).magnitude;
                sinceTower += step;
                sinceExtra += step;
                if (b.AirPoints.Contains(i)) continue;
                Vector3 p = b.Path[i].p, right = b.Path[i].right.normalized;

                if (sinceTower >= towerGap)
                {
                    float w = theme == Theme.NightCity ? R(9f, 18f) : R(8f, 22f), d = theme == Theme.NightCity ? R(9f, 18f) : R(8f, 22f);
                    float radius = Mathf.Sqrt(w * w + d * d) / 2f;
                    Vector3 at = p + right * side * (radius + R(16f, 55f));
                    if (Free(b, placed, at, radius + 12f, forks))
                    {
                        float top = p.y + (theme == Theme.NightCity ? R(6f, 55f) : R(2f, 45f));
                        bool ok = true;
                        if (theme == Theme.NightCity)
                        {
                            var me = new List<(Vector3 c, float r, float top)> { (at, radius, top + TowerCrown) };
                            foreach (Vector3 hoop in hoops) ok &= HoopClear(b, me, hoop);
                        }
                        else ok = LeanClear(b, at, w, d, k.Sea, top);
                        if (ok)
                        {
                            placed.Add((at, radius));
                            if (theme == Theme.NightCity)
                            {
                                towers.Add((at, radius, top + TowerCrown));
                                NightTower(holder, k, rng, at, w, d, low - 60f, top, p);
                            }
                            else DrownedTower(holder, k, at, w, d, low - 60f, top);
                            count++;
                            sinceTower = 0f;
                            if (theme == Theme.DrownedCity) towerGap = Mathf.Lerp(32f, 60f, (float)gaps.NextDouble());
                        }
                    }
                    side = -side;
                }

                // Not from a loop's points: a hoop placed from the top of loop 2 floated 52 m up.
                if (sinceExtra >= 140f && !InLoop(loops, i))
                {
                    Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;
                    Vector3 at = p - right * side * R(26f, 40f);
                    float radius = theme == Theme.NightCity ? 10f : 6f;
                    if (Free(b, placed, at, radius + 10f, forks))
                    {
                        bool made = true;
                        if (theme == Theme.DrownedCity) Lanterns(holder, rng, at + Vector3.up * R(4f, 16f), lanternGlow);
                        else if (rng.NextDouble() < 0.5)
                        {
                            Vector3 centre = at + Vector3.up * R(18f, 28f);
                            made = HoopClear(b, towers, centre);
                            if (made)
                            {
                                hoops.Add(centre);
                                Hoop(holder, k, rng, centre, forward);
                            }
                        }
                        else Billboard(holder, k, rng, at, p, low - 40f);
                        if (made)
                        {
                            placed.Add((at, radius));
                            count++;
                            sinceExtra = 0f;
                        }
                    }
                }
            }
            return count;
        }

        // Horizontally clear of the whole track, of the ground between a fork's lanes (a tower stood in the V like a
        // spike planted on the split), of the podium (0, 0, -80) and of what is already placed.
        private static bool Free(TrackBuilder b, List<(Vector3 c, float r)> placed, Vector3 at, float clear, List<Vector2[]> forks)
        {
            foreach (var (p, _) in b.Path)
                if (new Vector2(p.x - at.x, p.z - at.z).magnitude < clear) return false;
            foreach (Vector3 p in b.KeepClear)   // spurs and decoys (1.3.0)
                if (new Vector2(p.x - at.x, p.z - at.z).magnitude < clear) return false;
            if (new Vector2(at.x, at.z + 80f).magnitude < clear + 30f) return false;
            foreach (var (c, r) in placed)
                if (new Vector2(c.x - at.x, c.z - at.z).magnitude < r + clear) return false;
            var flat = new Vector2(at.x, at.z);
            foreach (Vector2[] area in forks)
                if (Inside(area, flat)) return false;
            return true;
        }

        // How far a night tower's crown (setback tier, drum, antenna) rises over its body, and a stunt hoop's outer radius
        // (its halo).
        private const float TowerCrown = 12f, HoopRadius = 10.12f;

        // Whether a stunt hoop centred here stands clear of these towers, in plan and in the view from the road
        // (2026-10-28: seen from the road, a hoop 12 m behind a tower's corner looked cut into it, and on The Heist one
        // just in front of a tower looked pressed flat against it): in plan every tower stays the hoop's radius + 2 m away
        // (Free keeps them further); and from every road point 25-160 m from it (3 m up), no tower – in front of it or up
        // to 60 m behind it, and reaching up into its view – overlaps its disc widened by 2 m.
        private static bool HoopClear(TrackBuilder b, List<(Vector3 c, float r, float top)> towers, Vector3 hoop)
        {
            float wide = HoopRadius + 2f;
            foreach (var (c, r, _) in towers)
                if (new Vector2(c.x - hoop.x, c.z - hoop.z).magnitude < r + wide) return false;
            for (int i = 0; i < b.Path.Count; i += 4)
            {
                if (b.AirPoints.Contains(i)) continue;
                Vector3 v = b.Path[i].p + Vector3.up * 3f;
                var toHoop = new Vector2(hoop.x - v.x, hoop.z - v.z);
                float dh = toHoop.magnitude;
                if (dh < 25f || dh > 160f) continue;
                float wh = Mathf.Asin(Mathf.Min(1f, wide / dh)), low = Mathf.Atan2(hoop.y - wide - v.y, dh);
                foreach (var (c, r, top) in towers)
                {
                    var toTower = new Vector2(c.x - v.x, c.z - v.z);
                    float dt = toTower.magnitude;
                    if (dt < 1f || dt - dh > 60f || Mathf.Atan2(top - v.y, dt) < low) continue;
                    if (Vector2.Angle(toHoop, toTower) * Mathf.Deg2Rad < wh + Mathf.Asin(Mathf.Min(1f, r / dt))) return false;
                }
            }
            return true;
        }

        // How a drowned tower at `at` leans (DrownedTower draws it first from the same sequence).
        private static Quaternion DrownedTilt(Vector3 at)
        {
            Random more = Detail(at);
            float R(float a, float c) => Mathf.Lerp(a, c, (float)more.NextDouble());
            float x = R(-7f, 7f), y = R(0f, 90f), z = R(-7f, 7f);
            return Quaternion.Euler(x, y, z);
        }

        // Whether a leaning drowned tower here stays clear of the road at every height (2026-10-28: one leaning 7° towards
        // the road came up flush against its border): its footprint, anywhere from the sea to its top, stays 13 m sideways
        // from every road point (8 m past an 8 m road's border, 4 m past a 16 m landing's). Free only kept its centre
        // 12 m + its half-diagonal from the road, which a 45 m lean of 7° eats 5.5 m into.
        private static bool LeanClear(TrackBuilder b, Vector3 at, float w, float d, float bottom, float top)
        {
            const float Clear = 13f;
            Quaternion q = DrownedTilt(at);
            Vector3 axis = q * Vector3.up, ex3 = q * Vector3.right, ez3 = q * Vector3.forward;
            Vector2 ex = new Vector2(ex3.x, ex3.z).normalized, ez = new Vector2(ez3.x, ez3.z).normalized;
            float reach = Mathf.Sqrt(w * w + d * d) / 2f + Mathf.Max(Mathf.Abs(bottom), Mathf.Abs(top)) * 0.13f + Clear;
            bool Near(Vector3 p)
            {
                if (new Vector2(p.x - at.x, p.z - at.z).magnitude > reach) return false;
                foreach (float y in new[] { bottom, Mathf.Clamp(p.y, bottom, top), top })
                {
                    Vector3 c = new Vector3(at.x, 0f, at.z) + axis * (y / axis.y);   // the tower's origin is at y 0
                    var dl = new Vector2(p.x - c.x, p.z - c.z);
                    float u = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(dl, ex)) - w / 2f), v = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(dl, ez)) - d / 2f);
                    if (new Vector2(u, v).magnitude < Clear) return true;
                }
                return false;
            }
            foreach (var (p, _) in b.Path)
                if (Near(p)) return false;
            foreach (Vector3 p in b.KeepClear)
                if (Near(p)) return false;
            return true;
        }

        // The ground between the two lanes of each Y-fork, as a polygon. Path order is approach, lane 1 (split…merge),
        // lane 2 (from the split again: a jump of more than 15 m back), exit; mirrored lanes have as many points.
        private static List<Vector2[]> ForkAreas(TrackBuilder b)
        {
            var areas = new List<Vector2[]>();
            for (int i = 1; i < b.Path.Count; i++)
            {
                if (b.AirPoints.Contains(i) || b.AirPoints.Contains(i - 1)) continue;
                if ((b.Path[i].p - b.Path[i - 1].p).magnitude < 15f) continue;
                Vector3 laneEnd = b.Path[i - 1].p;
                int end = -1;
                for (int k = i; k < b.Path.Count && end < 0; k++)
                    if ((b.Path[k].p - laneEnd).magnitude < 2f) end = k;
                int length = end - i + 1, start = i - length;
                if (end < 0 || start < 0) continue;
                var area = new List<Vector2>();
                for (int k = start; k < i; k++) area.Add(new Vector2(b.Path[k].p.x, b.Path[k].p.z));
                for (int k = end; k >= i; k--) area.Add(new Vector2(b.Path[k].p.x, b.Path[k].p.z));
                areas.Add(area.ToArray());
            }
            return areas;
        }

        private static bool Inside(Vector2[] polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], c = polygon[j];
                if ((a.y > p.y) != (c.y > p.y) && p.x < (c.x - a.x) * (p.y - a.y) / (c.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        private static bool InLoop(List<(int from, int to)> loops, int i)
        {
            foreach (var (from, to) in loops)
                if (i >= from && i <= to) return true;
            return false;
        }

        // A piece's own random sequence for the details of its look (from its position): the city's layout (where the
        // pieces go, drawn from the map's sequence) doesn't move when a piece's details change.
        private static Random Detail(Vector3 at) =>
            new Random(unchecked(Mathf.RoundToInt(at.x * 10f) * 73856093 ^ Mathf.RoundToInt(at.z * 10f) * 19349663));

        // A dark skyscraper (see the header): the body first, then trims, metal, bands and lights merged per kind.
        private static void NightTower(Transform holder, Kit k, Random rng, Vector3 at, float w, float d, float bottom, float top, Vector3 road)
        {
            Transform t = new GameObject("Night tower").transform;
            t.SetParent(holder, false);
            t.position = new Vector3(at.x, 0f, at.z);
            t.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 90f, 0f);
            Color[] bodies = { new Color(0.12f, 0.13f, 0.25f), new Color(0.18f, 0.15f, 0.3f), new Color(0.1f, 0.18f, 0.28f) };
            Color body = bodies[rng.Next(bodies.Length)];
            int lit = rng.Next(Neon.Length);
            Random more = Detail(at);
            int shape = more.Next(4);   // 0 setback tier + spire, 1 crowned box + corner strips, 2 hexagonal, 3 round
            if (shape >= 2) w = d = Mathf.Max(w, d) * 0.85f;
            float h = top - bottom;
            Sculpt s = k.S;
            if (shape >= 2) s.Painted(shape == 2 ? k.Hex : k.Round, t, new Vector3(0f, bottom + h / 2f, 0f), Quaternion.identity, new Vector3(w, h, d), body, "Body");
            else SeasonDecor.Prim(PrimitiveType.Cube, t, new Vector3(0f, bottom + h / 2f, 0f), new Vector3(w, h, d), body);
            var trim = new MergeMesh();
            var metal = new MergeMesh();
            var bright = new MergeMesh();
            var dim = new MergeMesh();
            var strips = new MergeMesh();
            var letters = new MergeMesh();
            var beacon = new MergeMesh();
            int sides = shape == 2 ? 6 : 16;
            // Bands 7.5 cm proud of every face (on a prism: its corners pushed out so the faces are).
            void Band(MergeMesh m, float y, float bw, float bd, float thick)
            {
                if (shape >= 2)
                {
                    float r = bw / 2f + 0.075f / Mathf.Cos(Mathf.PI / sides);
                    m.Prism(new Vector3(0f, y, 0f), r, r, thick, sides);
                }
                else m.Box(new Vector3(0f, y, 0f), new Vector3(bw + 0.15f, thick, bd + 0.15f));
            }
            float floor = Mathf.Max(bottom + 2f, k.Low - 20f);
            float y0 = top - 3f;
            for (; y0 > top - 46f && y0 > floor; y0 -= 3.5f) Band(bright, y0, w, d, 0.7f);
            for (y0 -= 2.5f; y0 > floor; y0 -= 6f) Band(dim, y0, w, d, 0.55f);
            switch (shape)
            {
                case 0:
                {
                    // Mullions up every face (5 cm proud, the bands 2.5 cm behind their faces; ending 5 cm under the roof,
                    // whose face they would share), a setback tier, a spire.
                    for (float x = -w / 2f + 1.6f; x < w / 2f - 1f; x += 3.2f)
                        foreach (float sz in new[] { -1f, 1f })
                            trim.Box(new Vector3(x, (floor + top - 0.05f) / 2f, sz * (d / 2f + 0.04f)), new Vector3(0.3f, top - 0.05f - floor, 0.1f));
                    for (float z = -d / 2f + 1.6f; z < d / 2f - 1f; z += 3.2f)
                        foreach (float sx in new[] { -1f, 1f })
                            trim.Box(new Vector3(sx * (w / 2f + 0.04f), (floor + top - 0.05f) / 2f, z), new Vector3(0.1f, top - 0.05f - floor, 0.3f));
                    float tw = w * 0.62f, td = d * 0.62f, th = Mathf.Lerp(8f, 16f, (float)more.NextDouble());
                    trim.Box(new Vector3(0f, top + th / 2f, 0f), new Vector3(tw, th, td));
                    for (float y = top + th - 2f; y > top + 1.5f; y -= 3.5f) bright.Box(new Vector3(0f, y, 0f), new Vector3(tw + 0.15f, 0.6f, td + 0.15f));
                    float sh = Mathf.Lerp(7f, 13f, (float)more.NextDouble()), roof = top + th;
                    metal.Prism(new Vector3(0f, roof + sh / 2f, 0f), Mathf.Min(tw, td) * 0.28f, 0.05f, sh, 8);
                    beacon.Ball(new Vector3(0f, roof + sh + 0.35f, 0f), Vector3.one * 0.6f);
                    break;
                }
                case 1:
                    // A crown, neon strips down the corners, an antenna, often a sign facing the road.
                    trim.Box(new Vector3(0f, top + 1f, 0f), new Vector3(w * 0.6f, 2f, d * 0.6f));
                    foreach (float sx in new[] { -1f, 1f })
                        foreach (float sz in new[] { -1f, 1f })
                            strips.Box(new Vector3(sx * w / 2f, (floor + top - 0.05f) / 2f, sz * d / 2f), new Vector3(0.32f, top - 0.05f - floor, 0.32f));
                    metal.Prism(new Vector3(0f, top + 4f, 0f), 0.18f, 0.12f, 4f, 6);
                    beacon.Ball(new Vector3(0f, top + 6.3f, 0f), Vector3.one * 0.65f);
                    if (more.NextDouble() < 0.6) RoofSign(t, more, metal, letters, w, d, top, road);
                    break;
                default:
                    // Hexagonal or round: a narrower drum on top, an antenna.
                    trim.Prism(new Vector3(0f, top + 1.5f, 0f), w * 0.36f, w * 0.3f, 3f, sides);
                    metal.Prism(new Vector3(0f, top + 5.5f, 0f), 0.16f, 0.1f, 5f, 6);
                    beacon.Ball(new Vector3(0f, top + 8.3f, 0f), Vector3.one * 0.6f);
                    break;
            }
            trim.Paint(s, t, "Trim", body * 1.35f);
            metal.Paint(s, t, "Metal", new Color(0.36f, 0.38f, 0.46f));
            bright.Light(s, t, "Bands", k.Bright[lit]);
            dim.Light(s, t, "Bands (lower)", k.Dim[lit]);
            strips.Light(s, t, "Corner strips", k.Bright[(lit + 1) % Neon.Length]);
            letters.Light(s, t, "Sign", k.Pulse[(lit + 2) % Neon.Length]);
            beacon.Light(s, t, "Beacon", k.Beacon);
        }

        // A neon sign on the roof's edge nearest the road: a board on two posts and a glowing word, over the middle 60 % of
        // the edge (HeistDecor's searchlights stand at 0.38 of the footprint, off the middle).
        private static void RoofSign(Transform t, Random more, MergeMesh metal, MergeMesh letters, float w, float d, float top, Vector3 road)
        {
            Vector3 toRoad = t.InverseTransformDirection(new Vector3(road.x - t.position.x, 0f, road.z - t.position.z));
            bool alongX = Mathf.Abs(toRoad.x) > Mathf.Abs(toRoad.z);
            Vector3 n = alongX ? new Vector3(Mathf.Sign(toRoad.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toRoad.z));
            Vector3 along = Vector3.Cross(n, Vector3.up);   // left to right for whoever faces the sign
            float span = (alongX ? d : w) * 0.6f;
            Vector3 c = n * ((alongX ? w : d) * 0.4f) + Vector3.up * (top + 3.2f);
            metal.Box(c, alongX ? new Vector3(0.3f, 2.6f, span) : new Vector3(span, 2.6f, 0.3f));
            foreach (float sx in new[] { -0.35f, 0.35f })
                metal.Box(new Vector3(c.x, top + 0.95f, c.z) + along * (span * sx), new Vector3(0.3f, 1.9f, 0.3f));
            string word = SignWords[more.Next(SignWords.Length)];
            float height = Mathf.Min(1.6f, (span - 0.6f) / (word.Length * 5.6f / 6f - 1.6f / 6f));
            letters.Text(word, c + n * 0.21f, along, Vector3.up, height, height * 0.15f, 0.1f);
        }

        // A half-sunk, leaning tower (see the header).
        private static void DrownedTower(Transform holder, Kit k, Vector3 at, float w, float d, float bottom, float top)
        {
            Random more = Detail(at);
            float R(float a, float c) => Mathf.Lerp(a, c, (float)more.NextDouble());
            Transform t = new GameObject("Drowned tower").transform;
            t.SetParent(holder, false);
            t.position = new Vector3(at.x, 0f, at.z);
            t.rotation = Quaternion.Euler(R(-7f, 7f), R(0f, 90f), R(-7f, 7f));
            Color[] bodies = { new Color(0.16f, 0.32f, 0.34f), new Color(0.3f, 0.2f, 0.3f), new Color(0.24f, 0.28f, 0.34f) };
            Color body = bodies[more.Next(bodies.Length)];
            float h = top - bottom;
            Sculpt s = k.S;
            SeasonDecor.Prim(PrimitiveType.Cube, t, new Vector3(0f, bottom + h / 2f, 0f), new Vector3(w, h, d), body);   // first: RisingTide coils round it
            var upper = new MergeMesh();
            var dark = new MergeMesh();
            var edge = new MergeMesh();
            var metal = new MergeMesh();
            var rebar = new MergeMesh();
            var bands = new MergeMesh();
            var windows = new MergeMesh();
            var beacon = new MergeMesh();
            float water = k.Sea;   // the tower's origin is at y 0 and it leans 7° at most: local y ≈ world y
            float wetTop = Mathf.Min(water + 3.5f, top - 1f);
            if (wetTop > water - 1.5f)
                dark.Box(new Vector3(0f, (water - 1.5f + wetTop) / 2f, 0f), new Vector3(w + 0.1f, wetTop - water + 1.5f, d + 0.1f));
            Windows(windows, more, w, d, water + 4.5f, top - 2f);
            int neon = more.Next(2);
            for (int n = 0; n < 2; n++)
            {
                float y = top - 4f - n * 9f;
                if (y > wetTop + 2f) bands.Box(new Vector3(0f, y, 0f), new Vector3(w + 0.15f, 0.8f, d + 0.15f));
            }
            bool broken = more.NextDouble() < 0.35;
            float roof = top, rw = w, rd = d;
            if (!broken && more.NextDouble() < 0.35)
            {
                // A setback tier with its own windows.
                rw = w * 0.6f;
                rd = d * 0.6f;
                float th = R(5f, 12f);
                upper.Box(new Vector3(0f, top + th / 2f, 0f), new Vector3(rw, th, rd));
                Windows(windows, more, rw, rd, top + 1f, top + th - 1.5f);
                dark.Box(new Vector3(0f, top + 0.15f, 0f), new Vector3(w - 1.2f, 0.3f, d - 1.2f));   // the terrace round it
                roof = top + th;
            }
            if (broken)
            {
                // The walls of the lost floors go on up round the edge as jagged stubs – a ragged skyline of wall slabs,
                // 0.6 m thick and set 10 cm inside the facade, with gaps – a couple of broken floor slabs leaning against
                // them, and rebar sticking up (2026-10-28: boxes at odd angles read as dark boxes stacked on the roof).
                BrokenWalls(edge, more, w, d, roof);
                int shards = 1 + more.Next(2);
                for (int n = 0; n < shards; n++)
                {
                    float a = R(0f, Mathf.PI * 2f), span = R(0.3f, 0.45f) * Mathf.Min(w, d);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 u = Vector3.Cross(Vector3.up, dir), v = Quaternion.AngleAxis(R(25f, 45f), u) * Vector3.up;
                    float tip = R(-0.3f, 0.3f) * span;
                    edge.Slab(new Vector3(0f, roof - 0.15f, 0f) + dir * (Mathf.Min(w, d) * R(0.05f, 0.18f)), u, v,
                        new[] { new Vector2(-span / 2f, 0f), new Vector2(span / 2f, 0f), new Vector2(span * 0.3f + tip, R(2.5f, 4.5f)), new Vector2(-span * 0.25f + tip, R(1.8f, 3.5f)) },
                        0.5f);
                }
                for (int n = 0; n < 6; n++)
                {
                    float len = R(1.2f, 2.6f);
                    Quaternion tilt = Quaternion.Euler(R(-15f, 15f), 0f, R(-15f, 15f));
                    rebar.Prism(new Vector3(R(-0.35f, 0.35f) * w, roof - 0.1f, R(-0.35f, 0.35f) * d) + tilt * Vector3.up * (len / 2f), 0.08f, 0.07f, len, 5, tilt);
                }
            }
            else
            {
                // A darker inset cap and a parapet break the roof up (the flat roofs flashed white), a tank, AC units, an
                // antenna with a red light.
                dark.Box(new Vector3(0f, roof + 0.175f, 0f), new Vector3(rw - 1.2f, 0.35f, rd - 1.2f));
                foreach (float sz in new[] { -1f, 1f })
                    edge.Box(new Vector3(0f, roof + 0.45f, sz * (rd / 2f - 0.2f)), new Vector3(rw - 0.1f, 0.9f, 0.3f));
                foreach (float sx in new[] { -1f, 1f })
                    edge.Box(new Vector3(sx * (rw / 2f - 0.2f), roof + 0.45f, 0f), new Vector3(0.3f, 0.9f, rd - 0.1f));
                if (rw >= 9f && rd >= 9f)
                {
                    var tank = new Vector3(rw * 0.2f, roof, -rd * 0.18f);
                    foreach (float lx in new[] { -0.75f, 0.75f })
                        foreach (float lz in new[] { -0.75f, 0.75f })
                            metal.Box(tank + new Vector3(lx, 0.7f, lz), new Vector3(0.2f, 1.4f, 0.2f));
                    metal.Prism(tank + Vector3.up * 2.4f, 1.1f, 1.1f, 2f, 12);
                    metal.Prism(tank + Vector3.up * 3.7f, 1.15f, 0f, 0.6f, 12);
                }
                metal.Box(new Vector3(-rw * 0.22f, roof + 0.5f, -rd * 0.2f), new Vector3(Mathf.Min(1.6f, rw * 0.2f), 1f, 1.2f));
                metal.Box(new Vector3(rw * 0.05f, roof + 0.45f, rd * 0.22f), new Vector3(Mathf.Min(1.4f, rw * 0.18f), 0.9f, 1.1f));
                var mast = new Vector3(-rw * 0.28f, roof, rd * 0.26f);
                metal.Prism(mast + Vector3.up * 3f, 0.09f, 0.05f, 6f, 6);
                beacon.Ball(mast + Vector3.up * 6.2f, Vector3.one * 0.35f);
            }
            upper.Paint(s, t, "Setback", body);
            dark.Paint(s, t, "Waterline and roof cap", body * 0.5f);
            edge.Paint(s, t, broken ? "Broken top" : "Parapet", broken ? body * 1.05f : body * 0.85f);   // broken walls: the facade's own
            metal.Paint(s, t, "Rooftop", new Color(0.32f, 0.34f, 0.38f));
            rebar.Paint(s, t, "Rebar", new Color(0.42f, 0.24f, 0.15f));
            bands.Light(s, t, "Neon bands", k.Bright[neon]);
            windows.Light(s, t, "Windows", k.Window);
            beacon.Light(s, t, "Beacon", k.Beacon);
        }

        // A broken top's wall stubs: along each edge of the w × d roof at `roof`, 0.6 m thick slabs whose outer face is 10 cm
        // inside the facade, their tops jagged (a new height every 1.2-2.6 m, 0.4-5 m tall), a gap now and then, their
        // feet 15 cm down in the roof.
        private static void BrokenWalls(MergeMesh m, Random rng, float w, float d, float roof)
        {
            float R(float a, float c) => Mathf.Lerp(a, c, (float)rng.NextDouble());
            const float Thick = 0.6f, Inset = 0.1f;
            var corners = new[] { new Vector3(-w / 2f, 0f, -d / 2f), new Vector3(w / 2f, 0f, -d / 2f), new Vector3(w / 2f, 0f, d / 2f), new Vector3(-w / 2f, 0f, d / 2f) };
            for (int e = 0; e < 4; e++)
            {
                Vector3 a = corners[e], z = corners[(e + 1) % 4];
                Vector3 along = (z - a).normalized, inward = Vector3.Cross(Vector3.up, along);   // the corners run anticlockwise seen from above
                if (Vector3.Dot(inward, -a) < 0f) inward = -inward;
                float length = (z - a).magnitude - Inset;   // the ends stay inside the facades beside too (no shared plane)
                Vector3 c = a + inward * (Inset + Thick / 2f) + Vector3.up * (roof - 0.15f);
                float x = Inset, h = R(0.4f, 4f);
                while (x < length - 0.3f)
                {
                    float x1 = Mathf.Min(length, x + R(1.2f, 2.6f)), h1 = R(0.4f, 5f);
                    if (rng.NextDouble() < 0.18)   // a gap: this stretch fell
                    {
                        x = x1;
                        h = R(0.4f, 4f);
                        continue;
                    }
                    m.Slab(c, along, Vector3.up, new[] { new Vector2(x, 0f), new Vector2(x1, 0f), new Vector2(x1, h1 + 0.15f), new Vector2(x, h + 0.15f) }, Thick);
                    x = x1;
                    h = h1;
                }
            }
        }

        // Window quads 5 cm proud of each face of a w × d block, rows every 3.2 m between `from` and `to`, about a third
        // of them lit (each floor its own share), the others left out (the dark wall shows).
        private static void Windows(MergeMesh m, Random rng, float w, float d, float from, float to)
        {
            var faces = new[]
            {
                (n: Vector3.right, u: Vector3.forward, half: d / 2f, depth: w / 2f), (n: Vector3.left, u: Vector3.forward, half: d / 2f, depth: w / 2f),
                (n: Vector3.forward, u: Vector3.right, half: w / 2f, depth: d / 2f), (n: Vector3.back, u: Vector3.right, half: w / 2f, depth: d / 2f),
            };
            for (float y = from; y + 1.5f < to; y += 3.2f)
            {
                float share = Mathf.Lerp(0.12f, 0.5f, (float)rng.NextDouble());
                foreach (var f in faces)
                {
                    for (float x = -f.half + 1.3f; x + 1.1f <= f.half - 0.8f; x += 2.6f)
                    {
                        if (rng.NextDouble() >= share) continue;
                        Vector3 c0 = f.n * (f.depth + 0.05f) + f.u * x + Vector3.up * y;
                        m.Quad(c0, c0 + f.u * 1.1f, c0 + f.u * 1.1f + Vector3.up * 1.5f, c0 + Vector3.up * 1.5f, f.n);
                    }
                }
            }
        }

        // A floating stunt hoop: one smooth glowing ring and a thinner halo of another colour 7 cm outside it (16
        // overlapping cubes before: a saw-tooth outline, ink seams, segments turning dark or white), turned 35–55° away
        // from the road's line (lined up with it, hoops read as gates to drive through).
        private static void Hoop(Transform holder, Kit k, Random rng, Vector3 centre, Vector3 forward)
        {
            Transform t = new GameObject("Stunt hoop").transform;
            t.SetParent(holder, false);
            int a = rng.Next(Neon.Length), c = rng.Next(Neon.Length);
            if (c == a) c = (a + 2) % Neon.Length;
            Random more = Detail(centre);
            float turn = (more.NextDouble() < 0.5 ? -1f : 1f) * Mathf.Lerp(35f, 55f, (float)more.NextDouble());
            t.position = centre;
            t.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, turn, 0f) * forward, Vector3.up);
            k.S.Glowing(k.S.Torus(0.078f, 48, 12), t, Vector3.zero, Quaternion.identity, Vector3.one * 9f, k.Bright[a], "Ring");
            k.S.Glowing(k.S.Torus(0.035f, 48, 8), t, Vector3.zero, Quaternion.identity, Vector3.one * 10.12f, k.Pulse[c], "Halo");
        }

        // A giant billboard facing the road (see the header). Local: its face towards the road is -z.
        private static void Billboard(Transform holder, Kit k, Random rng, Vector3 at, Vector3 road, float bottom)
        {
            Transform t = new GameObject("Billboard").transform;
            t.SetParent(holder, false);
            t.position = new Vector3(at.x, 0f, at.z);
            Vector3 face = new Vector3(road.x - at.x, 0f, road.z - at.z);
            t.rotation = Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? -face.normalized : Vector3.forward, Vector3.up);
            int offset = rng.Next(Neon.Length), variant = rng.Next(3), words = rng.Next(BoardWords.Length);
            float y = road.y + 10f;
            Sculpt s = k.S;
            var metal = new MergeMesh();
            var frame = new MergeMesh();
            var stripes = new[] { new MergeMesh(), new MergeMesh(), new MergeMesh() };
            var border = new MergeMesh();
            var backFrame = new MergeMesh();
            var content = new MergeMesh();
            // Two lattice legs: posts, braces every 10 m and X diagonals between them.
            foreach (float sx in new[] { -5f, 5f })
                metal.Box(new Vector3(sx, (bottom + y) / 2f, 0.9f), new Vector3(1.4f, y - bottom, 1.4f));
            for (float yy = y - 6f; yy > bottom + 2f; yy -= 10f)
            {
                metal.Box(new Vector3(0f, yy, 0.9f), new Vector3(8.6f, 0.5f, 0.5f));
                if (yy - 10f <= bottom + 2f) continue;
                metal.Beam(new Vector3(-4.3f, yy, 0.9f), new Vector3(4.3f, yy - 10f, 0.9f), 0.35f, 0.35f, Vector3.forward);
                metal.Beam(new Vector3(4.3f, yy, 0.9f), new Vector3(-4.3f, yy - 10f, 0.9f), 0.35f, 0.35f, Vector3.forward);
            }
            // A dark board all round (its white back glared at earlier stretches of the road), three stripes of different
            // neon colours, a border, and lettering or a pictogram in front of them.
            frame.Box(new Vector3(0f, y + 4f, 0f), new Vector3(17f, 9f, 0.6f));
            for (int n = 0; n < 3; n++)
                stripes[n].Box(new Vector3(0f, y + 1.5f + n * 2.5f, -0.37f), new Vector3(15.5f, 2f, 0.12f));
            var corners = new[] { new Vector3(-8.1f, y - 0.1f, -0.5f), new Vector3(8.1f, y - 0.1f, -0.5f), new Vector3(8.1f, y + 8.1f, -0.5f), new Vector3(-8.1f, y + 8.1f, -0.5f) };
            for (int n = 0; n < 4; n++) border.Beam(corners[n], corners[(n + 1) % 4], 0.3f, 0.1f, Vector3.forward);
            // Its back (seen from other stretches of the road: a blank black slab before): a neon frame round it, 6 cm off
            // the board, and a truss on it – three chords and X braces, 0.3 m thick, inside the frame.
            var back = new[] { new Vector3(-8.2f, y - 0.2f, 0.36f), new Vector3(8.2f, y - 0.2f, 0.36f), new Vector3(8.2f, y + 8.2f, 0.36f), new Vector3(-8.2f, y + 8.2f, 0.36f) };
            for (int n = 0; n < 4; n++) backFrame.Beam(back[n], back[(n + 1) % 4], 0.3f, 0.1f, Vector3.forward);
            float[] chords = { y + 0.6f, y + 4f, y + 7.4f };
            foreach (float cy in chords) metal.Box(new Vector3(0f, cy, 0.46f), new Vector3(15.6f, 0.3f, 0.3f));
            for (int n = 0; n + 1 < chords.Length; n++)
                for (float x0 = -7.8f; x0 < 7.7f; x0 += 3.9f)
                {
                    metal.Beam(new Vector3(x0, chords[n], 0.46f), new Vector3(x0 + 3.9f, chords[n + 1], 0.46f), 0.24f, 0.24f, Vector3.forward);
                    metal.Beam(new Vector3(x0 + 3.9f, chords[n], 0.46f), new Vector3(x0, chords[n + 1], 0.46f), 0.24f, 0.24f, Vector3.forward);
                }
            Vector3 mid = new Vector3(0f, y + 4f, -0.52f);
            switch (variant)
            {
                case 0:
                    string[] lines = BoardWords[words].Split('|');
                    content.Text(lines[0], mid + Vector3.up * 1.75f, Vector3.right, Vector3.up, 2.6f, 0.36f, 0.1f);
                    content.Text(lines[1], mid - Vector3.up * 1.75f, Vector3.right, Vector3.up, 2.6f, 0.36f, 0.1f);
                    break;
                case 1:
                    CarPictogram(content, mid);
                    break;
                default:
                    content.Text(">>>", mid, Vector3.right, Vector3.up, 5f, 0.6f, 0.1f);
                    break;
            }
            metal.Paint(s, t, "Legs", new Color(0.22f, 0.23f, 0.28f));
            frame.Paint(s, t, "Board", new Color(0.07f, 0.08f, 0.13f));
            for (int n = 0; n < 3; n++) stripes[n].Light(s, t, "Stripe", k.Dim[(n + offset) % Neon.Length]);
            border.Light(s, t, "Border", k.Bright[offset]);
            backFrame.Light(s, t, "Back frame", k.Bright[(offset + 1) % Neon.Length]);
            content.Light(s, t, variant == 0 ? "Lettering" : "Pictogram", k.Letters);
        }

        // A sports car in neon strokes, speeding to the right (its wheels octagons), on the board plane at `c`.
        private static void CarPictogram(MergeMesh m, Vector3 c)
        {
            void Line(params float[] xy)
            {
                for (int i = 2; i + 1 < xy.Length; i += 2)
                    m.Beam(c + new Vector3(xy[i - 2], xy[i - 1], 0f), c + new Vector3(xy[i], xy[i + 1], 0f), 0.3f, 0.1f, Vector3.forward);
            }
            Line(-5.2f, -1.4f, -5.2f, 0.2f, -3.4f, 0.6f, -1.8f, 2.2f, 2f, 2.2f, 3.6f, 0.6f, 5.2f, 0.2f, 5.2f, -1.4f, -5.2f, -1.4f);
            Line(-1.5f, 0.7f, -0.9f, 1.9f, 1.8f, 1.9f, 2.9f, 0.7f, -1.5f, 0.7f);
            foreach (float wx in new[] { -3f, 3f })
            {
                var ring = new float[18];
                for (int i = 0; i <= 8; i++)
                {
                    float a = (i % 8) * Mathf.PI / 4f + Mathf.PI / 8f;
                    ring[i * 2] = wx + Mathf.Cos(a) * 1.1f;
                    ring[i * 2 + 1] = -1.5f + Mathf.Sin(a) * 1.1f;
                }
                Line(ring);
            }
            Line(-7.6f, 1f, -6f, 1f);
            Line(-7.6f, 0f, -5.8f, 0f);
            Line(-7.6f, -1f, -6.2f, -1f);
        }

        // A cluster of warm sky lanterns: glowing paper bodies (a lit sphere, taller than wide) between a dark top cap
        // and a dark base ring, each cluster rising and sinking slowly. Toon-painted orange cubes before (2026-10-27:
        // "floating cardboard boxes").
        private static void Lanterns(Transform holder, Random rng, Vector3 centre, Material glow)
        {
            Transform t = new GameObject("Lanterns").transform;
            t.SetParent(holder, false);
            t.position = centre;
            int n = 4 + rng.Next(4);
            Color cap = new Color(0.32f, 0.14f, 0.1f);
            for (int k = 0; k < n; k++)
            {
                Vector3 o = new Vector3(R(rng, -5f, 5f), R(rng, -3f, 4f), R(rng, -5f, 5f));
                float size = R(rng, 0.85f, 1.2f);
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = "Lantern";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(t, false);
                body.transform.localPosition = o;
                body.transform.localScale = new Vector3(1f, 1.35f, 1f) * size;
                body.GetComponent<MeshRenderer>().sharedMaterial = glow;
                SeasonDecor.Prim(PrimitiveType.Cylinder, t, o + Vector3.up * 0.72f * size, new Vector3(0.62f, 0.08f, 0.62f) * size, cap);
                SeasonDecor.Prim(PrimitiveType.Cylinder, t, o - Vector3.up * 0.7f * size, new Vector3(0.5f, 0.06f, 0.5f) * size, cap);
            }
            var bob = t.gameObject.AddComponent<KitMover>();
            bob.mode = KitMoverMode.PingPong;
            bob.moveBy = new Vector3(0f, R(rng, 1f, 2f), 0f);
            bob.period = R(rng, 6f, 9f);
            bob.phase = (float)rng.NextDouble();
            bob.noTraps = false;
        }

        // ------------------------------------------------------------------ finish and loops

        // The finish, lit (Mega Stunt, Twin Tunnels: the stock arch nearly vanishes at night and the road ended in the void
        // 15 m later). Call right after b.Finish(), the builder still at the road's end. 3 m past the line, a neon pylon on
        // each side of the road with a glowing checkered banner, standing on its own feet beside the road down to a
        // footing at the sea (or, with road under it, ending flush with the deck's underside on a bracket from it); over
        // the road from one pylon's top to the other's, a neon half-hoop arch (2026-10-28: a full ring 1.5 m up ran through
        // the deck and hung below it, and the pylons ended in the air 4 m under the road); a chevron barrier just past the
        // road's end; a light bar under the start edge. Under "City decor" ("Finish lights", "Start edge light").
        // barrier: false leaves out the chevron barrier past the road's end (Twin Tunnels: the user wanted it gone).
        public static void AddFinish(Transform root, TrackBuilder b, bool barrier = true)
        {
            Sculpt s = Sculpt.For(b.Folder);
            Transform holder = root.Find("City decor") ?? root;
            Quaternion q = b.Heading;
            Transform t = new GameObject("Finish lights").transform;
            t.SetParent(holder, false);
            t.SetPositionAndRotation(b.Position - q * Vector3.forward * 15f, q);
            float half = b.RoadWidth / 2f + TrackBuilder.BorderWidth;
            const float Top = 9f;   // the pylons' tops, over the road
            float low = float.MaxValue;
            foreach (var (p, _) in b.Path) low = Mathf.Min(low, p.y);
            KitMap map = root.GetComponent<KitMap>();
            float sea = low - (map != null && map.deathDepth > 0f ? map.deathDepth : 30f) - t.position.y;   // in the finish's frame
            var dark = new MergeMesh();
            var left = new MergeMesh();
            var right = new MergeMesh();
            var white = new MergeMesh();
            var red = new MergeMesh();
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * (half + 2.4f), bx = x - sx * 1.5f;
                // Down to the sea unless some road passes under the pylon (then it stops at the deck's underside).
                Vector3 foot = t.TransformPoint(new Vector3(x, 0f, 3f));
                bool clear = true;
                for (int i = 0; i < b.Path.Count - 60 && clear; i++)
                {
                    Vector3 p = b.Path[i].p;
                    if (p.y < foot.y - 2f && new Vector2(p.x - foot.x, p.z - foot.z).magnitude < 9f) clear = false;
                }
                float bottom = clear ? sea - 1f : -1f;
                dark.Box(new Vector3(x, (bottom + Top) / 2f, 3f), new Vector3(1.2f, Top - bottom, 1.2f));
                if (clear) dark.Box(new Vector3(x, sea + 0.5f, 3f), new Vector3(2.6f, 3f, 2.6f));   // its footing at the waterline
                else dark.Box(new Vector3(sx * (half + 1.2f), -1.25f, 3f), new Vector3(3.6f, 0.5f, 1.2f));   // a bracket under the deck
                (sx < 0f ? left : right).Box(new Vector3(x, (0.3f + Top - 0.3f) / 2f, 2.38f), new Vector3(0.3f, Top - 0.6f, 0.12f));
                dark.Box(new Vector3(bx, 5.8f, 3f), new Vector3(1.8f, 6.4f, 0.2f));   // the banner, hung off the pylon
                for (int row = 0; row < 8; row++)
                    for (int col = 0; col < 2; col++)
                        if ((row + col) % 2 == 0)
                            white.Box(new Vector3(bx - 0.45f + col * 0.9f, 3f + row * 0.8f, 2.86f), new Vector3(0.78f, 0.78f, 0.08f));
            }
            // The barrier 0.6 m past the road's end: a dark board with red and white diagonal stripes, red lamps on it.
            if (barrier) dark.Box(new Vector3(0f, 1.3f, 15.75f), new Vector3(2f * half + 1f, 2.2f, 0.3f));
            for (int j = 0; barrier; j++)
            {
                float x0 = -half - 0.2f + j * 1.1f;
                if (x0 + 1.25f > half + 0.3f) break;
                (j % 2 == 0 ? red : white).Beam(new Vector3(x0, 0.5f, 15.54f), new Vector3(x0 + 1f, 2.1f, 15.54f), 0.45f, 0.08f, Vector3.forward);
            }
            if (barrier) foreach (float sx in new[] { -1f, 1f }) red.Ball(new Vector3(sx * (half + 0.1f), 2.75f, 15.75f), Vector3.one * 0.35f);
            dark.Paint(s, t, "Pylons and boards", new Color(0.07f, 0.08f, 0.13f));
            left.Light(s, t, "Neon (left)", s.Glow("CityFinishLeft", Neon[0], Lighten(Neon[0], 0.5f), pulse: 0.4f));
            right.Light(s, t, "Neon (right)", s.Glow("CityFinishRight", Neon[1], Lighten(Neon[1], 0.5f), pulse: 0.4f));
            white.Light(s, t, "Checks", s.Glow("CityFinishWhite", new Color(0.97f, 0.97f, 1f), Color.white));
            if (barrier) red.Light(s, t, "Barrier red", s.Glow("CityFinishRed", new Color(1f, 0.15f, 0.15f), new Color(1f, 0.5f, 0.4f), pulse: 0.8f));
            // The arch: an upper half circle centred over the road at the pylons' tops, its ends running 0.6 m down into them.
            var arch = new MergeMesh();
            float span = half + 2.4f;
            var spine = new List<Vector3> { new Vector3(-span, Top - 0.6f, 3f) };
            for (int k = 0; k <= 32; k++)
            {
                float a = Mathf.PI * k / 32f;
                spine.Add(new Vector3(-Mathf.Cos(a) * span, Top + Mathf.Sin(a) * span, 3f));
            }
            spine.Add(new Vector3(span, Top - 0.6f, 3f));
            var radii = new List<float>();
            foreach (Vector3 _ in spine) radii.Add(0.45f);
            arch.Tube(spine, radii, 12);
            arch.Light(s, t, "Finish hoop", s.Glow("CityFinishHoop", Neon[2], Lighten(Neon[2], 0.5f), pulse: 0.3f));

            // The road's first edge (it starts at the origin heading +z): a light bar 3 cm in front of its cut face.
            var bar = new MergeMesh();
            bar.Box(new Vector3(0f, -0.4f, -0.08f), new Vector3(TrackBuilder.DefaultWidth + 2f * TrackBuilder.BorderWidth, 0.3f, 0.1f));
            Transform start = new GameObject("Start edge light").transform;
            start.SetParent(holder, false);
            bar.Light(s, start, "Light bar", s.Glow("CityStartEdge", Neon[0], Lighten(Neon[0], 0.5f)));
        }

        // A foundation under the podium's stage (SlipSkidMapTools.AddPodium's 36 m slab, "PodiumStage": it floated in the
        // dark as a bare slab), so the stage is a building's roof: NightCity – a lit tower 1 m inside the stage's edge all
        // round, down into the sea, its window bands bright under the stage and dimmer lower, mullions, a neon band right
        // under the stage's edge; DrownedCity – a drowned building (a dark wet band at the waterline, a grid of dim windows,
        // two neon bands). Its top is inside the stage; the stage and the podium are left as they are, and nothing else is
        // added near them. Under "City decor" ("Podium foundation"). Call after Add.
        public static void AddPodiumFoundation(Transform root, TrackBuilder b, Theme theme)
        {
            Transform stage = root.Find("PodiumStage");
            if (stage == null) return;
            Transform holder = root.Find("City decor") ?? root;
            float low = float.MaxValue;
            foreach (var (p, _) in b.Path) low = Mathf.Min(low, p.y);
            Kit k = MakeKit(root, b, low);
            Sculpt s = k.S;
            Transform t = new GameObject("Podium foundation").transform;
            t.SetParent(holder, false);
            t.SetPositionAndRotation(new Vector3(stage.position.x, 0f, stage.position.z), stage.rotation);
            float w = stage.localScale.x - 2f, d = stage.localScale.z - 2f;
            float top = stage.position.y, under = stage.position.y - stage.localScale.y / 2f;   // its top inside the stage
            float bottom = k.Sea - 6f;
            var bands = new MergeMesh();
            var dim = new MergeMesh();
            var trim = new MergeMesh();
            var dark = new MergeMesh();
            var windows = new MergeMesh();
            Random more = Detail(stage.position);
            if (theme == Theme.NightCity)
            {
                Color body = new Color(0.12f, 0.13f, 0.25f);
                SeasonDecor.Prim(PrimitiveType.Cube, t, new Vector3(0f, (top + bottom) / 2f, 0f), new Vector3(w, top - bottom, d), body);
                trim.Box(new Vector3(0f, under - 0.55f, 0f), new Vector3(w + 0.15f, 0.7f, d + 0.15f));   // the neon band under the stage
                float y = under - 3.5f;
                for (; y > under - 20f && y > k.Sea + 1f; y -= 3.5f) bands.Box(new Vector3(0f, y, 0f), new Vector3(w + 0.15f, 0.6f, d + 0.15f));
                for (y -= 2.5f; y > k.Sea + 1f; y -= 6f) dim.Box(new Vector3(0f, y, 0f), new Vector3(w + 0.15f, 0.5f, d + 0.15f));
                var mullions = new MergeMesh();
                for (float x = -w / 2f + 1.6f; x < w / 2f - 1f; x += 3.2f)
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        mullions.Box(new Vector3(x, (k.Sea + under - 1.3f) / 2f, sz * (d / 2f + 0.04f)), new Vector3(0.3f, under - 1.3f - k.Sea, 0.1f));
                        mullions.Box(new Vector3(sz * (w / 2f + 0.04f), (k.Sea + under - 1.3f) / 2f, x), new Vector3(0.1f, under - 1.3f - k.Sea, 0.3f));
                    }
                mullions.Paint(s, t, "Mullions", body * 1.35f);
                trim.Light(s, t, "Neon band", k.Bright[0]);
                bands.Light(s, t, "Bands", k.Bright[1]);
                dim.Light(s, t, "Bands (lower)", k.Dim[1]);
            }
            else
            {
                Color body = new Color(0.24f, 0.28f, 0.34f);
                SeasonDecor.Prim(PrimitiveType.Cube, t, new Vector3(0f, (top + bottom) / 2f, 0f), new Vector3(w, top - bottom, d), body);
                float water = k.Sea;
                dark.Box(new Vector3(0f, water + 1f, 0f), new Vector3(w + 0.1f, 5f, d + 0.1f));   // the wet band, 1.5 m under to 3.5 m over the water
                Windows(windows, more, w, d, water + 4.5f, under - 3f);
                foreach (float y in new[] { under - 1.2f, under - 10f })
                    if (y > water + 5f) bands.Box(new Vector3(0f, y, 0f), new Vector3(w + 0.15f, 0.8f, d + 0.15f));
                dark.Paint(s, t, "Waterline", body * 0.5f);
                windows.Light(s, t, "Windows", k.Window);
                bands.Light(s, t, "Neon bands", k.Bright[1]);
            }
        }

        // Neon rims along both edges of every loop (on its side walls), lights chasing round them: cyan on the left,
        // pink on the right. Under "City decor".
        public static int AddLoopRims(Transform root, TrackBuilder b)
        {
            Sculpt s = Sculpt.For(b.Folder);
            Transform holder = root.Find("City decor") ?? root;
            var left = new[] { s.Glow("CityLoopLeft", Neon[0], Lighten(Neon[0], 0.7f), bands: 1f, speed: 1.2f) };
            var right = new[] { s.Glow("CityLoopRight", Neon[1], Lighten(Neon[1], 0.7f), bands: 1f, speed: 1.2f) };
            return SeasonDecor.AddLoopRims(holder, b, left, right);
        }

        private static Color Lighten(Color c, float amount) => Color.Lerp(c, Color.white, amount);

        private static Color Scale(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);

        private static float R(Random rng, float a, float c) => Mathf.Lerp(a, c, (float)rng.NextDouble());
    }
}
