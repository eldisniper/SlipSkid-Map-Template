using System;
using System.Collections.Generic;
using SlipSkid.MapKit.SDK;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace SlipSkid.MapKit.Editor
{
    // Floating islands (Avatar's Hallelujah Mountains style) around a track, themed by the surface of the road they
    // stand next to: spring (Dirt) cherry trees, flowering bushes and big flowers; summer (Sand) cacti, palms and
    // sandstone boulders; autumn (Mud) maples, pumpkins and leaf piles; winter (Ice) snowy pines (tiers of cones),
    // snowmen and ice crystals – plus one landmark island per season raised 15–25 m above the road with its own
    // centrepiece (a giant cherry tree, a palm grove round an oasis, a giant maple, a giant pine beside an ice spire).
    // Each island: a grass/sand/leaf/snow cap with a rounded lip (its first child, scale x = 2 × radius: Gravity Islands
    // reads it), a jagged faceted rock body whose top stays 12 cm under the cap's top (the old sphere dome poked through
    // the grass: fried eggs from above) and that narrows to a spire exactly 2.25 radii under the cap's centre (Gravity
    // Islands hangs its chains there), leaning and bulging differently on every island, with strata, stalactites and the
    // season's details (roots, sand falls, icicles; red sandstone in summer); props stand on the cap, never closer to
    // each other than their footprints (two snowmen fused into a pine before), and islands keep 6 m apart sideways
    // (they clustered into one blob on screen). Also: the podium's island (AddPodiumIsland), the season tunnels
    // (AddTunnelArt) and the glowing loop rims (AddLoopRims / AddSeasonLoopRims).
    // Painted with the game's own toon materials (KitRoad kind Concrete + paint/tint, MapKitLoader 1.1.0+: materials in
    // a bundle don't render) or our Glow (Sculpt). Parts of one colour are merged into one mesh per piece (MergeMesh, saved
    // in the map's Sculpt asset DecorMeshes.asset) so a detailed island costs about ten renderers. Decoration only: no
    // colliders, kept clear of every part of the track (and of jump flight paths) so nothing looks like ground you could
    // land on.
    internal static class SeasonDecor
    {
        private const string Toon = "ToonWood";   // matte toon material (smoothness 0.08), recoloured by the tint

        // Roots dangling under spring and autumn caps were dropped (2026-10-28: thin and in the cap's shadow, they read as
        // black hooks at the lip).
        private enum Detail { None, SandFalls, Icicles }

        private static readonly Dictionary<string, Season> Seasons = new Dictionary<string, Season>
        {
            // Summer's sand saturated (the pale sand blew out to cream and white under the toon highlight).
            ["Dirt"] = new Season("Spring", new Color(0.45f, 0.78f, 0.33f), new Color(0.5f, 0.44f, 0.37f), new Color(0.37f, 0.26f, 0.18f), 4,
                Detail.None, SpringProp, new[] { 2.3f, 2.3f, 1.6f, 1.8f }, CherryTree, GiantCherry, 9f),
            ["Sand"] = new Season("Summer", new Color(0.93f, 0.74f, 0.42f), new Color(0.6f, 0.29f, 0.19f), new Color(0.78f, 0.46f, 0.28f), 2,
                Detail.SandFalls, SummerProp, new[] { 1.4f, 1.4f, 3f, 1.6f }, PalmTree, PalmGrove, 9.5f),
            ["Mud"] = new Season("Autumn", new Color(0.83f, 0.47f, 0.17f), new Color(0.42f, 0.34f, 0.29f), new Color(0.3f, 0.2f, 0.14f), 4,
                Detail.None, AutumnProp, new[] { 2.4f, 2.4f, 3.6f, 1.6f }, MapleTree, GiantMaple, 10.5f),
            ["Ice"] = new Season("Winter", new Color(0.95f, 0.97f, 1f), new Color(0.45f, 0.51f, 0.61f), new Color(0.82f, 0.88f, 0.95f), 3,
                Detail.Icicles, WinterProp, new[] { 1.9f, 1.9f, 1.1f, 2.3f }, PineTree, GiantPine, 11f),
        };

        private sealed class Season
        {
            public readonly string Name;
            public readonly Color Ground, RockA, RockB;   // cap; rock and its strata (the band under the cap is always B)
            public readonly int Strata;                   // every Strata-th band of the rock is B
            public readonly Detail Details;
            public readonly Action<PaintPalette, Random, int> Prop;      // kind 0-3 (0 and 1: the season's tree), at the palette's At
            public readonly float[] Footprint;                       // radius each kind takes on the ground, at scale 1
            public readonly Action<PaintPalette, Random, float> Tree;     // the season's signature tree, `size` × its usual size
            public readonly Action<PaintPalette, Random> Landmark;        // the landmark island's centrepiece
            public readonly float LandmarkFootprint;

            public Season(string name, Color ground, Color rockA, Color rockB, int strata, Detail details, Action<PaintPalette, Random, int> prop,
                float[] footprint, Action<PaintPalette, Random, float> tree, Action<PaintPalette, Random> landmark, float landmarkFootprint)
            {
                Name = name;
                Ground = ground;
                RockA = rockA;
                RockB = rockB;
                Strata = strata;
                Details = details;
                Prop = prop;
                Footprint = footprint;
                Tree = tree;
                Landmark = landmark;
                LandmarkFootprint = landmarkFootprint;
            }
        }

        // An island placed so far: its centre (the cap's top), radius, and how far it reaches up (props) and down (rock).
        private readonly struct Spot
        {
            public readonly Vector3 C;
            public readonly float R, Top, Bottom;
            public readonly bool Landmark;

            public Spot(Vector3 c, float r, bool landmark)
            {
                C = c;
                R = r;
                Landmark = landmark;
                Top = c.y + (landmark ? 22f : 6f);
                Bottom = c.y - 2.25f * r;
            }
        }

        // Returns the number of islands. Call after the course is built, with the track's Path/PathSurface.
        public static int AddFloatingIslands(Transform root, TrackBuilder b, int seed = 4)
        {
            var rng = new Random(seed);
            Sculpt s = Sculpt.For(b.Folder);
            Transform holder = Holder(root);
            var placed = new List<Spot>();
            int count = 0;

            // The landmarks first – one big island per season, some way into its stretch (tried again further on if it
            // has no room), from their own sequence – so the regular islands can keep out of the view between them and
            // the road (a regular island in front of the autumn landmark read as one double island).
            var landmarkRng = new Random(seed * 31 + 7);
            var landmarkDone = new HashSet<string>();
            var runLength = new Dictionary<string, int>();
            int side = 1;
            for (int i = 1; i < b.Path.Count; i++)
            {
                string surface = i < b.PathSurface.Count ? b.PathSurface[i] : "";
                if (!Seasons.TryGetValue(surface, out Season season) || b.AirPoints.Contains(i))
                {
                    runLength.Clear();
                    continue;
                }
                runLength.TryGetValue(surface, out int run);
                runLength[surface] = run + 1;
                if (landmarkDone.Contains(surface) || run < 60 || run % 4 != 0) continue;
                if (TryIsland(holder, s, b, landmarkRng, placed, season, i, side, 70f, 18f, landmark: true))
                {
                    landmarkDone.Add(surface);
                    side = -side;
                    count++;
                }
            }

            float sinceLast = 0f;
            side = 1;
            for (int i = 1; i < b.Path.Count; i++)
            {
                sinceLast += (b.Path[i].p - b.Path[i - 1].p).magnitude;
                string surface = i < b.PathSurface.Count ? b.PathSurface[i] : "";
                if (!Seasons.TryGetValue(surface, out Season season) || b.AirPoints.Contains(i)) continue;
                if (sinceLast < 34f) continue;
                float radius = 5f + (float)rng.NextDouble() * 5f;
                float distance = 24f + radius + (float)rng.NextDouble() * 22f;
                if (TryIsland(holder, s, b, rng, placed, season, i, side, distance, radius, landmark: false))
                {
                    count++;
                    sinceLast = 0f;
                }
                side = -side;
            }
            return count;
        }

        // Whether an island here would read as one with a placed island from the road (2026-10-28: two islands one behind
        // the other read as one cap with two spires; a regular island in front of a landmark as one double island): seen
        // from any road point (4 m up) within 160 m of both, their silhouettes overlap (with a 2° margin), and either they
        // are about as far away (the farther less than 1.7 × the nearer: the same size on screen) or the nearer one is a
        // regular island in front of a landmark.
        private static bool Merges(TrackBuilder b, List<Spot> placed, Spot me)
        {
            float Elevation(float y, Vector3 v, float d) => Mathf.Atan2(y - v.y, d);
            for (int k = 0; k < b.Path.Count; k += 4)
            {
                if (b.AirPoints.Contains(k)) continue;
                Vector3 v = b.Path[k].p + Vector3.up * 4f;
                var toMe = new Vector2(me.C.x - v.x, me.C.z - v.z);
                float dMe = toMe.magnitude;
                if (dMe > 160f || dMe < 1f) continue;
                foreach (Spot o in placed)
                {
                    var toO = new Vector2(o.C.x - v.x, o.C.z - v.z);
                    float dO = toO.magnitude;
                    if (dO > 160f || dO < 1f) continue;
                    bool meFront = dMe < dO;
                    float near = meFront ? dMe : dO, far = meFront ? dO : dMe;
                    bool sameSize = far < near * 1.7f;
                    bool hidesLandmark = meFront ? o.Landmark && !me.Landmark : me.Landmark && !o.Landmark;
                    if (!sameSize && !hidesLandmark) continue;
                    float apart = Vector2.Angle(toMe, toO) * Mathf.Deg2Rad;
                    if (apart > Mathf.Atan(me.R / dMe) + Mathf.Atan(o.R / dO) + 2f * Mathf.Deg2Rad) continue;
                    if (Elevation(me.Top, v, dMe) < Elevation(o.Bottom, v, dO) || Elevation(o.Top, v, dO) < Elevation(me.Bottom, v, dMe)) continue;
                    return true;
                }
            }
            return false;
        }

        private static Transform Holder(Transform root)
        {
            Transform holder = root.Find("Season decor");
            if (holder != null) return holder;
            holder = new GameObject("Season decor").transform;
            holder.SetParent(root, false);
            return holder;
        }

        private static bool TryIsland(Transform holder, Sculpt s, TrackBuilder b, Random rng, List<Spot> placed,
            Season season, int i, int side, float distance, float radius, bool landmark)
        {
            Vector3 right = b.Path[i].right.normalized;
            // Landmarks float well above the road (at +4 m they sat at eye level like a flat hill).
            float height = landmark ? 15f + (float)rng.NextDouble() * 10f : -9f + (float)rng.NextDouble() * 16f;
            Vector3 centre = b.Path[i].p + right * side * distance + Vector3.up * height;

            // Clear of the whole track (and jump flight paths, which are in Path too), the podium and other islands.
            float clear = radius + 14f;
            for (int k = 0; k < b.Path.Count; k += 2)
            {
                Vector3 p = b.Path[k].p;
                if (new Vector2(p.x - centre.x, p.z - centre.z).magnitude < clear && Mathf.Abs(p.y - centre.y) < radius * 2.2f + 10f)
                    return false;
            }
            foreach (Vector3 p in b.KeepClear)   // spurs and decoys (1.3.0), and the ground maps put under their lay-bys
            {
                if (new Vector2(p.x - centre.x, p.z - centre.z).magnitude < clear + 10f && Mathf.Abs(p.y - centre.y) < radius * 2.2f + 10f)
                    return false;
            }
            if (new Vector2(centre.x, centre.z + 80f).magnitude < radius + 40f) return false;   // the podium (and its island)
            // Apart sideways by both radii + 6 m, whatever their heights (so their centres are at least that far apart in
            // 3D too): islands one above the other merged into one blob on screen. And apart in the view from the road.
            foreach (Spot o in placed)
                if (new Vector2(o.C.x - centre.x, o.C.z - centre.z).magnitude < o.R + radius + 6f) return false;
            var spot = new Spot(centre, radius, landmark);
            if (Merges(b, placed, spot)) return false;
            placed.Add(spot);
            // The island's own sequence for its looks: changing how islands are built doesn't move the others (Gravity
            // Islands fits its own pieces round them).
            rng = new Random(rng.Next());

            Transform island = new GameObject($"{season.Name} island{(landmark ? " (landmark)" : "")}").transform;
            island.SetParent(holder, false);
            island.position = centre;
            island.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            Cap(s, island, season.Ground, radius);   // first child: Gravity Islands reads the radius from it
            IslandBody(s, island, rng, season, radius, 2.25f, landmark);

            var props = new PaintPalette();
            var taken = new List<(Vector2 at, float r)>();
            if (landmark)
            {
                const float Grand = 1.3f;   // the centrepiece towers over the island (20-25 m)
                props.At = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), Vector3.one * Grand);
                season.Landmark(props, rng);
                taken.Add((Vector2.zero, season.LandmarkFootprint * Grand));
            }
            int wanted = landmark ? 6 : 2 + Mathf.RoundToInt(radius / 3f);
            PlaceProps(props, rng, season, radius, wanted, taken, kind => kind, 0f);
            props.Paint(s, island, "Props");
            if (props.HasLit) props.Light(s, island, "Pumpkin faces", PumpkinGlow(s));
            return true;
        }

        // The jack-o'-lanterns' faces: warm candlelight, flickering a little.
        private static Material PumpkinGlow(Sculpt s) =>
            s.Glow("PumpkinFace", new Color(1f, 0.72f, 0.18f), new Color(1f, 0.9f, 0.5f), pulse: 0.7f);

        // `wanted` props on a cap of this radius, none closer to another (or to what `taken` holds) than their two
        // footprints; `pick` maps the drawn kind (0-3) to the one built; `minRing` keeps them that far from the centre.
        private static void PlaceProps(PaintPalette props, Random rng, Season season, float radius, int wanted, List<(Vector2 at, float r)> taken,
            Func<int, int> pick, float minRing, Func<Vector2, bool> allowed = null)
        {
            for (int made = 0, tries = 0; made < wanted && tries < wanted * 8; tries++)
            {
                int kind = pick(rng.Next(4));
                float scale = 0.8f + (float)rng.NextDouble() * 0.5f;
                float foot = season.Footprint[kind] * scale;
                float far = radius - foot - 0.3f, near = Mathf.Min(minRing, far);
                float a = R(rng, 0f, Mathf.PI * 2f);
                float d = Mathf.Lerp(near, far, Mathf.Sqrt((float)rng.NextDouble()));
                if (far <= 0f) continue;
                var at = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
                if (allowed != null && !allowed(at)) continue;
                bool free = true;
                foreach ((Vector2 o, float r) in taken)
                    if ((o - at).magnitude < Mathf.Max(r + foot, 2.5f * scale) * 0.92f) free = false;
                if (!free) continue;
                taken.Add((at, foot));
                made++;
                props.At = Matrix4x4.TRS(new Vector3(at.x, 0f, at.y), Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), Vector3.one * scale);
                season.Prop(props, rng, kind);
            }
            props.At = Matrix4x4.identity;
        }

        // The end-of-race podium's concrete stage (SlipSkidMapTools.AddPodium: a 36 m block whose top is 1.3 m under the
        // podium marker; it floated on the sea as a bare grey slab) set into a big spring island: the cap 25 cm under the
        // stage's top all round it, a jagged rock body under it, low bushes and flowers beside the stage (nothing tall: the
        // winners' camera looks at the stands). The podium itself is left as it is. Under "Season decor".
        public static void AddPodiumIsland(Transform root, TrackBuilder b, Vector3 podium, string surface = "Dirt", int seed = 5)
        {
            if (!Seasons.TryGetValue(surface, out Season season)) return;
            var rng = new Random(seed);
            Sculpt s = Sculpt.For(b.Folder);
            const float Radius = 27f, Stage = 18f;   // the stage is 36 m square (corners 25.5 m out)
            Transform island = new GameObject("Podium island").transform;
            island.SetParent(Holder(root), false);
            island.position = podium + Vector3.down * 1.55f;
            Cap(s, island, season.Ground, Radius);
            IslandBody(s, island, rng, season, Radius, 1.15f, true);
            var props = new PaintPalette();
            // Bushes and flower patches only (kinds 2 and 3), in the strips between the stage and the edge.
            PlaceProps(props, rng, season, Radius, 10, new List<(Vector2 at, float r)>(), kind => 2 + kind % 2, Stage + 1.5f,
                at => Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y)) > Stage + 2.2f);
            props.Paint(s, island, "Props");
            if (props.HasLit) props.Light(s, island, "Pumpkin faces", PumpkinGlow(s));
        }

        // The cap: a disc with a rounded lip, flat on top (props stand at y = 0), 0.75 m thick; one mesh per map scaled
        // to the island (scale x = 2 × radius: Gravity Islands reads the radius from it).
        private static readonly Dictionary<Sculpt, Mesh> CapMeshes = new Dictionary<Sculpt, Mesh>();

        private static void Cap(Sculpt s, Transform island, Color ground, float radius)
        {
            if (!CapMeshes.TryGetValue(s, out Mesh mesh) || mesh == null)
            {
                var m = new MergeMesh();
                m.Lathe(new[]
                {
                    new Vector2(0f, -0.75f), new Vector2(0.47f, -0.75f), new Vector2(0.5f, -0.45f), new Vector2(0.5f, -0.12f),
                    new Vector2(0.485f, 0f), new Vector2(0f, 0f),
                }, 28, Vector3.zero, Quaternion.identity, Vector3.one);
                CapMeshes[s] = mesh = m.Build(s, "IslandCap");
            }
            s.Painted(mesh, island, Vector3.zero, Quaternion.identity, new Vector3(radius * 2f, 1f, radius * 2f), ground, "Ground");
        }

        // The rock under an island's cap: a jagged inverted mountain, faceted (matte facets: the smooth stacked spheres
        // read as glossy pots and drops), its top ring 0.12 m under the cap's top and inside its lip, narrowing to a spire
        // `depth` × radius down exactly under the centre, leaning and bulging differently on every island; the band
        // under the cap and every season.Strata-th band in the strata colour; stalactites; the season's details.
        private static void IslandBody(Sculpt s, Transform island, Random rng, Season season, float radius, float depth, bool big)
        {
            var rock = new MergeMesh();
            var strata = new MergeMesh();
            var extra = new MergeMesh();
            int sides = 9 + rng.Next(5);
            float[] f = { 0f, 0.06f, 0.17f, 0.31f, 0.46f, 0.61f, 0.75f, 0.88f, 1f };
            float[][] shapes =
            {
                new[] { 0.96f, 0.97f, 0.86f, 0.68f, 0.5f, 0.34f, 0.2f, 0.09f, 0f },    // a steady taper
                new[] { 0.95f, 0.97f, 0.93f, 0.74f, 0.44f, 0.25f, 0.14f, 0.06f, 0f },  // a bulging top, a sharp spire
                new[] { 0.95f, 0.9f, 0.7f, 0.52f, 0.37f, 0.26f, 0.16f, 0.07f, 0f },    // slim
            };
            float[] g = shapes[rng.Next(shapes.Length)];
            float top = -0.12f, bottom = -depth * radius;
            float lean = R(rng, 0f, 0.22f) * radius, la = R(rng, 0f, Mathf.PI * 2f);
            var leanDir = new Vector3(Mathf.Cos(la), 0f, Mathf.Sin(la));
            int n = f.Length;
            var centres = new Vector3[n];
            var rings = new Vector3[n - 1][];
            float twist = 0f;
            for (int k = 0; k < n; k++)
            {
                float y = Mathf.Lerp(top, bottom, f[k]);
                if (k > 1 && k < n - 1) y += R(rng, -0.025f, 0.025f) * (top - bottom);
                centres[k] = new Vector3(0f, y, 0f) + leanDir * (lean * Mathf.Sin(Mathf.PI * f[k]));
                if (k == n - 1) break;
                rings[k] = new Vector3[sides];
                twist += R(rng, -0.4f, 0.4f) * Mathf.PI / sides;
                for (int j = 0; j < sides; j++)
                {
                    float a = twist + (j + R(rng, -0.25f, 0.25f) * (k > 0 ? 1f : 0.3f)) * Mathf.PI * 2f / sides;
                    float noise = k == 0 ? R(rng, -0.03f, 0.01f) : k == 1 ? R(rng, -0.07f, 0.03f) : R(rng, -0.17f, 0.15f);
                    float rr = radius * g[k] * (1f + noise);
                    if (k <= 1) rr = Mathf.Min(rr, radius * 0.97f);   // inside the cap's lip: nothing pokes through the grass
                    float dy = k >= 2 ? R(rng, -0.04f, 0.04f) * radius : 0f;
                    rings[k][j] = centres[k] + new Vector3(Mathf.Cos(a) * rr, dy, Mathf.Sin(a) * rr);
                }
            }
            centres[n - 1] = new Vector3(0f, bottom, 0f);   // the spire's tip, right under the centre
            for (int j = 0; j < sides; j++)   // the top, hidden under the cap
                strata.Tri(centres[0], rings[0][j], rings[0][(j + 1) % sides], Vector3.up);
            for (int k = 0; k < n - 1; k++)
            {
                bool layer = k == 0 || (season.Strata > 0 && k % season.Strata == season.Strata - 1);
                MergeMesh m = layer ? strata : rock;
                for (int j = 0; j < sides; j++)
                {
                    int j1 = (j + 1) % sides;
                    Vector3 a0 = rings[k][j], a1 = rings[k][j1];
                    if (k == n - 2)
                    {
                        Vector3 tip = centres[n - 1];
                        m.Tri(a0, a1, tip, (a0 + a1 + tip) / 3f - (centres[k] + tip) / 2f);
                        continue;
                    }
                    Vector3 b0 = rings[k + 1][j], b1 = rings[k + 1][j1];
                    Vector3 axis = (centres[k] + centres[k + 1]) / 2f;
                    m.Tri(a0, a1, b1, (a0 + a1 + b1) / 3f - axis);
                    m.Tri(a0, b1, b0, (a0 + b1 + b0) / 3f - axis);
                }
            }
            // Stalactites hanging from the underside, each a cone whose base is set on a corner of one of the rock's rings
            // (half inside it), so it comes out of the face at its full width – at least 0.9 m across (2026-10-28: cones
            // rooted deep inside the body only showed their thin ends: black slivers).
            int count = (big ? 4 : 2) + rng.Next(3);
            for (int q = 0; q < count; q++)
            {
                int k = 2 + rng.Next(3);
                Vector3 corner = rings[k][rng.Next(sides)];
                Vector3 outward = new Vector3(corner.x - centres[k].x, 0f, corner.z - centres[k].z).normalized;
                float rb = Mathf.Max(0.45f, radius * R(rng, 0.06f, 0.1f));
                float length = Mathf.Min(R(rng, 4f, 7f) * Mathf.Sqrt(radius / 8f), (corner.y - bottom) * 0.8f);
                Vector3 root = corner - outward * (rb * 0.75f);
                rock.Prism(root + Vector3.down * (length / 2f), 0f, rb, length, 6, null, R(rng, 0f, 1f));
            }
            var dust = new MergeMesh();
            switch (season.Details)
            {
                case Detail.SandFalls:
                    SandFalls(extra, dust, rng, rings, centres, radius, (big ? 2 : 1) + rng.Next(2));
                    break;
                case Detail.Icicles:
                    Icicles(extra, rng, radius);
                    break;
            }
            rock.Paint(s, island, "Rock", season.RockA);
            strata.Paint(s, island, "Rock strata", season.RockB);
            extra.Paint(s, island, season.Details == Detail.SandFalls ? "Sand falls" : "Icicles",
                season.Details == Detail.SandFalls ? SandFall : Ice2);
            if (!dust.Empty)
                dust.Light(s, island, "Sand dust", s.SoftGlow("SandDust", new Color(0.95f, 0.78f, 0.48f), new Color(1f, 0.88f, 0.62f),
                    strength: 0.75f, fadeAlong: 1f, edgeSoft: 1.2f));
        }

        private static readonly Color SandFall = new Color(0.9f, 0.68f, 0.36f);

        // How far the rock's ring reaches from its centre in direction `dir` (on the ground plane): where a ray from the
        // centre leaves the ring's polygon.
        private static float RingReach(Vector3[] ring, Vector3 centre, Vector3 dir)
        {
            var o = new Vector2(centre.x, centre.z);
            var d = new Vector2(dir.x, dir.z);
            float best = 0f;
            for (int j = 0; j < ring.Length; j++)
            {
                Vector2 a = new Vector2(ring[j].x, ring[j].z) - o, e = new Vector2(ring[(j + 1) % ring.Length].x, ring[(j + 1) % ring.Length].z) - o;
                Vector2 ab = e - a;
                float den = d.x * ab.y - d.y * ab.x;
                if (Mathf.Abs(den) < 1e-6f) continue;
                float t = (a.x * ab.y - a.y * ab.x) / den, u = (a.x * d.y - a.y * d.x) / den;
                if (t > 0f && u >= -0.001f && u <= 1.001f) best = Mathf.Max(best, t);
            }
            return best;
        }

        // Sand pouring off the cap's edge (summer): a band 0.8-1.2 m wide over the lip and down the rock face, 35 cm off it
        // (it follows the jagged rings), and below its end a soft column of dust (SoftGlow) falling on and fading out
        // (2026-10-28: round streams thinning as they fell read as ropes and tusks ending in a black point).
        private static void SandFalls(MergeMesh m, MergeMesh dust, Random rng, Vector3[][] rings, Vector3[] centres, float radius, int count)
        {
            var used = new List<float>();
            for (int q = 0, tries = 0; q < count && tries < 20; tries++)
            {
                float a = R(rng, 0f, Mathf.PI * 2f);
                bool free = true;
                foreach (float o in used)
                    if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, o * Mathf.Rad2Deg)) < 50f) free = false;
                if (!free) continue;
                used.Add(a);
                q++;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float width = R(rng, 0.8f, 1.2f);
                var spine = new List<Vector3>
                {
                    dir * (radius * 0.86f) + Vector3.up * 0.05f,       // on the cap's top
                    dir * (radius + 0.25f) + Vector3.down * 0.1f,       // over the lip
                    dir * (radius + 0.25f) + Vector3.down * 0.75f,      // down the cap's side
                };
                int last = 2 + rng.Next(2);   // down the face to ring 3 or 4
                for (int k = 1; k <= last; k++)
                {
                    float reach = RingReach(rings[k], centres[k], dir);
                    if (reach <= 0f) break;
                    Vector3 p = centres[k] + dir * (reach + 0.35f);
                    if (p.y > spine[spine.Count - 1].y - 0.3f) continue;
                    spine.Add(p);
                }
                var widths = new List<float>();
                for (int i = 0; i < spine.Count; i++) widths.Add(Mathf.Lerp(width, width * 0.8f, (float)i / (spine.Count - 1)));
                m.Strip(spine, widths, Vector3.Cross(Vector3.up, dir), 0.25f);

                // The dust: opening from a point 1 m up inside the band's end (no hard rim, and it hides the band's cut),
                // then straight down, widening and drifting out, fading to nothing.
                Vector3 end = spine[spine.Count - 1];
                float fall = R(rng, 6f, 10f) * Mathf.Sqrt(radius / 8f);
                var col = new List<Vector3> { end + Vector3.up * 1f, end + Vector3.up * 0.3f + dir * 0.15f };
                var radii = new List<float> { 0.05f, width * 0.55f };
                for (int i = 1; i <= 4; i++)
                {
                    float u = i / 4f;
                    col.Add(end + dir * (0.15f + 0.6f * u) + Vector3.down * (fall * u));
                    radii.Add(Mathf.Lerp(width * 0.6f, width * 1.4f, u));
                }
                dust.Tube(col, radii, 10, 0f, 1f, caps: false);
            }
        }

        // Icicles all round the cap's lip (winter).
        private static void Icicles(MergeMesh m, Random rng, float radius)
        {
            int count = Mathf.RoundToInt(radius * 1.6f);
            for (int q = 0; q < count; q++)
            {
                float a = (q + R(rng, -0.3f, 0.3f)) * Mathf.PI * 2f / count;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float length = R(rng, 0.6f, 2.2f) * Mathf.Sqrt(radius / 8f), rb = R(rng, 0.12f, 0.24f);
                Vector3 at = dir * (radius * 0.975f) + Vector3.down * 0.5f;   // its root inside the lip
                m.Prism(at + Vector3.down * (length / 2f), 0f, rb, length, 5, null, R(rng, 0f, 1f));
            }
        }

        // ------------------------------------------------------------------ loops

        // Every loop of the track: runs of Path points whose road leans (more than ~1°) with one of them upside down, from
        // the point before the loop's first piece to its last point.
        internal static List<(int from, int to)> Loops(TrackBuilder b)
        {
            var loops = new List<(int from, int to)>();
            int start = -1;
            bool flipped = false;
            for (int i = 0; i <= b.PathUp.Count; i++)
            {
                bool tilted = i < b.PathUp.Count && b.PathUp[i].y < 0.9998f && !b.AirPoints.Contains(i);
                if (tilted)
                {
                    if (start < 0)
                    {
                        start = i;
                        flipped = false;
                    }
                    if (b.PathUp[i].y < -0.5f) flipped = true;
                }
                else if (start >= 0)
                {
                    if (flipped) loops.Add((Mathf.Max(0, start - 1), Mathf.Min(i, b.PathUp.Count - 1)));
                    start = -1;
                }
            }
            return loops;
        }

        // Glowing rims along both edges of every loop, lying on top of its side walls (clear of the road surface: the walls
        // stand outside the borders), each edge in `left.Length` / `right.Length` equal parts with their own glow; uv.y in
        // metres / 6 along them, so a Glow with bands sends a light every 6 m round the loop (chasing lights). They start
        // after the loop's first piece, where its walls have risen to full height (they ease in over that piece: the rims
        // started 2.8 m over the bare road edge and stuck out as flat blades beside the exit), run to its end and finish
        // in a glowing ball on each end (an open tube's end seen at an angle is a flat sliver).
        internal static int AddLoopRims(Transform parent, TrackBuilder b, Material[] left, Material[] right, float radius = 0.3f)
        {
            const float WallTop = 2.5f;          // TrackBuilder's loop walls (WallHeight)
            const int PointsPerPiece = 6;        // Path points per loop piece (TrackBuilder's SubSteps)
            Sculpt s = Sculpt.For(b.Folder);
            float edge = TrackBuilder.DefaultWidth / 2f + TrackBuilder.BorderWidth + TrackBuilder.WallThickness / 2f;
            int made = 0;
            foreach ((int loopFrom, int to) in Loops(b))
            {
                int from = Mathf.Min(loopFrom + PointsPerPiece + 1, to - 2);
                // The loop's own sideways axis: Path's `right` comes from the heading's Euler angle, which flips round
                // past the top (the rims crossed the road there).
                Vector3 across = b.Path[loopFrom].right.normalized;
                Transform t = new GameObject($"Loop {made + 1} rims").transform;
                t.SetParent(parent, false);
                foreach (float side in new[] { -1f, 1f })
                {
                    Material[] glows = side < 0f ? left : right;
                    Vector3 At(int i) => b.Path[i].p + across * (side * edge) + b.PathUp[i] * (WallTop + radius - 0.03f);
                    for (int k = 0; k < glows.Length; k++)
                    {
                        int i0 = from + (to - from) * k / glows.Length, i1 = from + (to - from) * (k + 1) / glows.Length;
                        var spine = new List<Vector3>();
                        var radii = new List<float>();
                        float length = 0f;
                        for (int i = i0; i <= i1; i++)
                        {
                            spine.Add(At(i));
                            radii.Add(radius);
                            if (spine.Count > 1) length += (spine[spine.Count - 1] - spine[spine.Count - 2]).magnitude;
                        }
                        var m = new MergeMesh { FixedU = 0f };
                        m.Tube(spine, radii, 8, 0f, length / 6f, caps: false);
                        if (k == 0) m.Ball(At(from), Vector3.one * (radius * 1.45f), null, 6, 10);
                        if (k == glows.Length - 1) m.Ball(At(to), Vector3.one * (radius * 1.45f), null, 6, 10);
                        m.Light(s, t, $"Rim {(side < 0f ? "left" : "right")} {k + 1}", glows[k]);
                    }
                }
                made++;
            }
            return made;
        }

        // Four Seasons' loop: both rims in four glowing bands, one per season (spring, summer, autumn, winter), lights
        // chasing round them. Under "Season decor".
        public static int AddSeasonLoopRims(Transform root, TrackBuilder b)
        {
            Sculpt s = Sculpt.For(b.Folder);
            var glows = new[]
            {
                s.Glow("LoopSpring", new Color(1f, 0.5f, 0.76f), new Color(0.6f, 1f, 0.5f), bands: 1f, speed: 0.5f),
                s.Glow("LoopSummer", new Color(1f, 0.78f, 0.2f), new Color(0.35f, 0.95f, 0.95f), bands: 1f, speed: 0.5f),
                s.Glow("LoopAutumn", new Color(1f, 0.45f, 0.1f), new Color(0.9f, 0.15f, 0.1f), bands: 1f, speed: 0.5f),
                s.Glow("LoopWinter", new Color(0.55f, 0.88f, 1f), new Color(0.95f, 0.98f, 1f), bands: 1f, speed: 0.5f),
            };
            return AddLoopRims(Holder(root), b, glows, glows);
        }

        // ------------------------------------------------------------------ season-change tunnels

        // Tunnel colours per season: walls (the season's main colour) and accents (arches, light strips, portal band).
        private static readonly Dictionary<string, (Color wall, Color accent)> TunnelColours = new Dictionary<string, (Color, Color)>
        {
            ["Dirt"] = (new Color(0.98f, 0.62f, 0.78f), new Color(0.55f, 0.88f, 0.42f)),   // blossom pink, fresh green
            ["Sand"] = (new Color(1f, 0.78f, 0.32f), new Color(0.2f, 0.78f, 0.82f)),        // gold, turquoise
            ["Mud"] = (new Color(0.95f, 0.45f, 0.14f), new Color(0.62f, 0.12f, 0.1f)),      // pumpkin orange, deep red
            ["Ice"] = (new Color(0.62f, 0.88f, 1f), Color.white),                            // icy blue, snow white
        };

        private static (Color wall, Color accent) TunnelLook(string season) =>
            TunnelColours.TryGetValue(season ?? "", out var look) ? look : (new Color(0.7f, 0.72f, 0.78f), Color.white);

        // The seasons' light strips, saturated (2026-10-28: the accents lightened read as pale pink on the pale walls of the
        // tunnel to winter); they meet through the HSV middle, never through white.
        private static readonly Dictionary<string, Color> StripColours = new Dictionary<string, Color>
        {
            ["Dirt"] = new Color(0.3f, 1f, 0.35f),     // fresh green
            ["Sand"] = new Color(0.1f, 0.9f, 0.95f),   // turquoise
            ["Mud"] = new Color(1f, 0.32f, 0.06f),     // ember orange
            ["Ice"] = new Color(0.2f, 0.62f, 1f),      // icy blue
        };

        // A fixed look for the tunnels of a map without seasons: wall and accent colours at the tunnel's two ends
        // (gradient in between); the portals get glowing caps, lamps and strips instead of season trees, and a neon sign
        // over each mouth ("<Sign> <tunnel number>") when Sign is set.
        public sealed class TunnelStyle
        {
            public Color WallFrom, WallTo, AccentFrom, AccentTo;
            public string Sign;
        }

        // Neon city (TwinTunnels' drowned city): dark navy to plum walls, cyan to magenta arches and strips; the tunnels
        // are drowned metro lines.
        public static readonly TunnelStyle Neon = new TunnelStyle
        {
            WallFrom = new Color(0.16f, 0.19f, 0.36f), WallTo = new Color(0.26f, 0.14f, 0.34f),
            AccentFrom = new Color(0.2f, 0.92f, 1f), AccentTo = new Color(1f, 0.32f, 0.85f),
            Sign = "METRO",
        };

        // Every tunnel of the track (TrackBuilder.Tunnels): the walls painted with a gradient between the tunnel's two
        // ends (through HSV, or through a pale tint when the hues are far apart: straight RGB mixing turned greige
        // mid-tunnel) and the roofs too, darker (0.6 ×: bare concrete lids from outside before); an arch every 5 m; a
        // continuous glowing light strip along each wall (one Glow tube per wall, coloured from one end's accent to the
        // other's: opaque cream bars with a notch at every joint before); a monumental portal at each end, its pillars
        // rising above the lintel with their caps and lamps on top (they were buried in it). On a seasonal map (no style)
        // the colours are the seasons' – from the season left behind to the one ahead (tiles tagged with KitRoad.season) –
        // and each portal is crowned with its season's signature tree (big in the middle, two smaller beside) on a strip
        // of its ground; with a style (night city), its colours, glowing strips on the arches, neon trims along the roof's
        // edges, a ceiling light, rooftop clutter (vents, AC units, antennas with red lights: drowned buildings), and
        // portals in neon (band, caps, lamps, pillar strips, a sign). Decoration against the walls and on the roof: no
        // colliders (the roof stays a tile so the game can still fade it when it blocks the camera). Under the map
        // root's "Tunnel decor".
        public static void AddTunnelArt(Transform root, TrackBuilder b, TunnelStyle style = null, int seed = 11)
        {
            var rng = new Random(seed);
            Sculpt s = Sculpt.For(b.Folder);
            Transform holder = new GameObject("Tunnel decor").transform;
            holder.SetParent(root, false);
            int index = 0;
            float h = TrackBuilder.TunnelHeight;
            foreach (TrackBuilder.TunnelInfo tunnel in b.Tunnels)
            {
                var tiles = tunnel.Tiles;
                if (tiles.Count == 0) continue;
                index++;
                string from = style != null ? "" : tiles[0].season, to = style != null ? "" : tiles[tiles.Count - 1].season;
                (Color wall, Color accent) a = style != null ? (style.WallFrom, style.AccentFrom) : TunnelLook(from);
                (Color wall, Color accent) z = style != null ? (style.WallTo, style.AccentTo) : TunnelLook(to);
                Transform t = new GameObject(style != null ? $"Tunnel {index}" : $"Tunnel {index} ({from} to {to})").transform;
                t.SetParent(holder, false);

                // The walls' colour u of the way through the tunnel (by length), and the roofs' (darker).
                Color WallAt(float u) => Opaque(Blend(a.wall, z.wall, u, whiteAbove: 100f) * 0.92f);
                float total = 0f;
                var tileFrom = new float[tiles.Count];
                var tileLength = new float[tiles.Count];
                for (int i = 0; i < tiles.Count; i++)
                {
                    tileFrom[i] = total;
                    tileLength[i] = PolyLength(tiles[i].frames);
                    total += tileLength[i];
                }
                // Each wall and roof tile is one colour (the game's tile: it can't be split), so a big change of colour
                // along the tunnel came out in hard 10 m steps (yellow, orange, salmon, red, pink): such a tunnel gets
                // GradientSlices slices per tile instead, all but one of them a painted skin 3 cm proud of the tile's
                // faces (the tile shows through as the remaining slice). Each skin is a child of the wall or roof it
                // covers, so it fades with it when the game fades that tile out of the camera's way.
                bool smooth = false;
                for (int i = 1; i < tiles.Count; i++)
                {
                    Color c0 = WallAt((tileFrom[i - 1] + tileLength[i - 1] / 2f) / total), c1 = WallAt((tileFrom[i] + tileLength[i] / 2f) / total);
                    if (Mathf.Max(Mathf.Abs(c0.r - c1.r), Mathf.Abs(c0.g - c1.g), Mathf.Abs(c0.b - c1.b)) > 0.035f) smooth = true;
                }
                for (int i = 0; i < tiles.Count; i++)
                {
                    int keep = smooth ? GradientSlices / 2 : 0, slices = smooth ? GradientSlices : 1;
                    float U(int j, float at) => (tileFrom[i] + tileLength[i] * (j + at) / slices) / Mathf.Max(total, 0.01f);
                    Color wall = WallAt(U(keep, 0.5f));
                    foreach (KitRoad part in tiles[i].road.GetComponentsInChildren<KitRoad>(true))
                    {
                        if (part.kind != KitRoadKind.Border) continue;
                        bool isWall = part.gameObject.name.StartsWith("Wall"), isRoof = part.gameObject.name.StartsWith("Roof");
                        if (!isWall && !isRoof) continue;
                        part.paint = Toon;
                        part.tint = isWall ? wall : Opaque(wall * 0.6f);
                        if (!smooth) continue;
                        float sx = part.gameObject.name.EndsWith("L") ? -1f : 1f;
                        for (int j = 0; j < slices; j++)
                        {
                            if (j == keep) continue;
                            var skin = new MergeMesh { At = part.transform.worldToLocalMatrix * root.localToWorldMatrix };   // frames: map space
                            List<(Vector3 p, Quaternion q, float w)> run = SubRun(tiles[i].frames, (float)j / slices, (float)(j + 1) / slices);
                            if (isWall) WallSkin(skin, run, sx);
                            else RoofSkin(skin, run);
                            Color c = WallAt(U(j, 0.5f));
                            skin.Paint(s, part.transform, $"Gradient {j + 1}", isWall ? c : Opaque(c * 0.6f));
                        }
                    }
                }

                // The centre line through the whole tunnel (each tile's first frame repeats the previous one's last).
                var frames = new List<(Vector3 p, Quaternion q, float w)>();
                foreach (var tile in tiles)
                    for (int k = frames.Count == 0 ? 0 : 1; k < tile.frames.Count; k++) frames.Add(tile.frames[k]);
                for (int f = 0; f < frames.Count; f += 3)
                {
                    float u = frames.Count == 1 ? 0.5f : (float)f / (frames.Count - 1);
                    Arch(s, t, frames[f], Blend(a.accent, z.accent, u, whiteAbove: 150f));
                }
                // Light strips 15 cm off the walls (the gradient skins are 3 cm proud of them); a seasonal tunnel's in the
                // seasons' saturated strip colours.
                Color stripFrom = style != null ? Lighten(a.accent, 0.25f) : StripColours.TryGetValue(from ?? "", out Color sa) ? sa : Lighten(a.accent, 0.25f);
                Color stripTo = style != null ? Lighten(z.accent, 0.25f) : StripColours.TryGetValue(to ?? "", out Color sz) ? sz : Lighten(z.accent, 0.25f);
                GlowRun(s, t, $"Tunnel{index}Strips", "Light strips", frames, new Func<(Vector3 p, Quaternion q, float w), Vector3>[]
                {
                    fr => fr.p + fr.q * new Vector3(InnerHalf(fr.w) - 0.15f, 1.3f, 0f),
                    fr => fr.p + fr.q * new Vector3(-(InnerHalf(fr.w) - 0.15f), 1.3f, 0f),
                }, 0.09f, stripFrom, stripTo, vivid: style == null);
                if (style != null)
                {
                    ArchStrips(s, t, index, frames, a.accent, z.accent);
                    GlowRun(s, t, $"Tunnel{index}Trim", "Roof trim", frames, new Func<(Vector3 p, Quaternion q, float w), Vector3>[]
                    {
                        fr => fr.p + fr.q * new Vector3(InnerHalf(fr.w) + TrackBuilder.WallThickness, h + 0.8f, 0f),
                        fr => fr.p + fr.q * new Vector3(-(InnerHalf(fr.w) + TrackBuilder.WallThickness), h + 0.8f, 0f),
                    }, 0.13f, a.accent, z.accent);
                    GlowRun(s, t, $"Tunnel{index}Ceiling", "Ceiling light", frames, new Func<(Vector3 p, Quaternion q, float w), Vector3>[]
                    {
                        fr => fr.p + fr.q * new Vector3(0f, h - 0.15f, 0f),
                    }, 0.1f, Lighten(a.accent, 0.55f), Lighten(z.accent, 0.55f));
                    RoofClutter(s, t, frames, rng, index, z.accent);
                }
                Portal(s, t, frames[0], from, a, z.accent, rng, -1f, style, index);
                Portal(s, t, frames[frames.Count - 1], to, z, a.accent, rng, 1f, style, index);
            }
        }

        private static float InnerHalf(float roadWidth) => roadWidth / 2f + TrackBuilder.BorderWidth;

        // Slices per wall/roof tile in a tunnel whose colour changes a lot along it (see AddTunnelArt).
        private const int GradientSlices = 4;
        private const float Skin = 0.03f;   // how far the gradient skins stand proud of the tile's faces

        private static float PolyLength(List<(Vector3 p, Quaternion q, float w)> frames)
        {
            float length = 0f;
            for (int k = 1; k < frames.Count; k++) length += (frames[k].p - frames[k - 1].p).magnitude;
            return length;
        }

        // The frames of a tile from `t0` to `t1` of its length (0..1), the ends interpolated between its own frames.
        private static List<(Vector3 p, Quaternion q, float w)> SubRun(List<(Vector3 p, Quaternion q, float w)> frames, float t0, float t1)
        {
            float total = PolyLength(frames);
            var run = new List<(Vector3 p, Quaternion q, float w)>();
            (Vector3 p, Quaternion q, float w) At(float d)
            {
                float walked = 0f;
                for (int k = 1; k < frames.Count; k++)
                {
                    float step = (frames[k].p - frames[k - 1].p).magnitude;
                    if (walked + step >= d || k == frames.Count - 1)
                    {
                        float u = step > 1e-5f ? Mathf.Clamp01((d - walked) / step) : 0f;
                        return (Vector3.Lerp(frames[k - 1].p, frames[k].p, u), Quaternion.Slerp(frames[k - 1].q, frames[k].q, u),
                            Mathf.Lerp(frames[k - 1].w, frames[k].w, u));
                    }
                    walked += step;
                }
                return frames[frames.Count - 1];
            }
            float d0 = t0 * total, d1 = t1 * total, along = 0f;
            run.Add(At(d0));
            for (int k = 1; k < frames.Count - 1; k++)
            {
                along += (frames[k].p - frames[k - 1].p).magnitude;
                if (along > d0 + 0.01f && along < d1 - 0.01f) run.Add(frames[k]);
            }
            run.Add(At(d1));
            return run;
        }

        // A wall's gradient skin along `run` (side -1 left, +1 right): its outer face (from the deck's underside to the
        // roof's top, covering the roof's edge too) and its inner face (from the border to the roof), 3 cm proud.
        private static void WallSkin(MergeMesh m, List<(Vector3 p, Quaternion q, float w)> run, float side)
        {
            float h = TrackBuilder.TunnelHeight;
            for (int k = 1; k < run.Count; k++)
            {
                var a = run[k - 1];
                var b = run[k];
                float oa = a.w / 2f + TrackBuilder.BorderWidth + TrackBuilder.WallThickness + Skin, ob = b.w / 2f + TrackBuilder.BorderWidth + TrackBuilder.WallThickness + Skin;
                float ia = InnerHalf(a.w) - Skin, ib = InnerHalf(b.w) - Skin;
                Vector3 P((Vector3 p, Quaternion q, float w) f, float x, float y) => f.p + f.q * new Vector3(side * x, y, 0f);
                m.Quad(P(a, oa, -1f + Skin), P(b, ob, -1f + Skin), P(b, ob, h + 0.8f + Skin), P(a, oa, h + 0.8f + Skin), a.q * Vector3.right * side);
                m.Quad(P(a, ia, Skin), P(b, ib, Skin), P(b, ib, h - Skin), P(a, ia, h - Skin), a.q * Vector3.right * -side);
            }
        }

        // A roof's gradient skin along `run`: its top (out to the walls' skins) and its underside (between the walls), 3 cm
        // proud.
        private static void RoofSkin(MergeMesh m, List<(Vector3 p, Quaternion q, float w)> run)
        {
            float h = TrackBuilder.TunnelHeight;
            for (int k = 1; k < run.Count; k++)
            {
                var a = run[k - 1];
                var b = run[k];
                float oa = a.w / 2f + TrackBuilder.BorderWidth + TrackBuilder.WallThickness + Skin, ob = b.w / 2f + TrackBuilder.BorderWidth + TrackBuilder.WallThickness + Skin;
                float ia = InnerHalf(a.w) - Skin, ib = InnerHalf(b.w) - Skin;
                Vector3 P((Vector3 p, Quaternion q, float w) f, float x, float y) => f.p + f.q * new Vector3(x, y, 0f);
                m.Quad(P(a, -oa, h + 0.8f + Skin), P(a, oa, h + 0.8f + Skin), P(b, ob, h + 0.8f + Skin), P(b, -ob, h + 0.8f + Skin), a.q * Vector3.up);
                m.Quad(P(a, -ia, h - Skin), P(a, ia, h - Skin), P(b, ib, h - Skin), P(b, -ib, h - Skin), a.q * Vector3.down);
            }
        }

        // Two posts against the walls and a beam under the roof, merged into one mesh.
        private static void Arch(Sculpt s, Transform t, (Vector3 p, Quaternion q, float w) frame, Color colour)
        {
            float h = TrackBuilder.TunnelHeight, half = InnerHalf(frame.w);
            var m = new MergeMesh { At = Matrix4x4.TRS(frame.p, frame.q, Vector3.one) };
            m.Box(new Vector3(half - 0.2f, h / 2f, 0f), new Vector3(0.4f, h, 0.6f));
            m.Box(new Vector3(-(half - 0.2f), h / 2f, 0f), new Vector3(0.4f, h, 0.6f));
            m.Box(new Vector3(0f, h - 0.3f, 0f), new Vector3(half * 2f - 0.1f, 0.5f, 0.6f));   // ends 5 cm inside the posts
            m.Paint(s, t, "Arch", colour);
        }

        // Night style: a glowing strip along each arch's inner edge (half into the posts and the beam, 5 cm proud), all
        // arches of a tunnel in one mesh: each arch's uv.y places it on the tunnel's accent gradient (Glow with two colours).
        private static void ArchStrips(Sculpt s, Transform t, int index, List<(Vector3 p, Quaternion q, float w)> frames, Color from, Color to)
        {
            float h = TrackBuilder.TunnelHeight;
            var m = new MergeMesh { FixedU = 0f };
            for (int f = 0; f < frames.Count; f += 3)
            {
                var fr = frames[f];
                float half = InnerHalf(fr.w), u = frames.Count == 1 ? 0.5f : (float)f / (frames.Count - 1);
                m.FixedV = u - 0.5f;
                m.At = Matrix4x4.TRS(fr.p, fr.q, Vector3.one);
                foreach (float side in new[] { -1f, 1f })
                    m.Box(new Vector3(side * (half - 0.4f), (h - 0.5f) / 2f, 0f), new Vector3(0.1f, h - 0.6f, 0.16f));
                m.Box(new Vector3(0f, h - 0.55f, 0f), new Vector3(2f * (half - 0.4f) + 0.1f, 0.1f, 0.16f));
            }
            m.Light(s, t, "Arch neon", s.Glow($"Tunnel{index}Arches", Lighten(from, 0.3f), Lighten(to, 0.3f), bands: 0.5f, speed: 0f));
        }

        // Glow tubes along the tunnel (one per function giving a point at each frame), coloured from `from` to `to` along
        // their length: the Glow's two colours with uv.y from -0.5 to 0.5 (bands 0.5, no scrolling) – in two halves through
        // the HSV middle when the hues are far apart (the shader mixes in RGB: turquoise to red went grey); `vivid`: never
        // through a pale middle.
        private static void GlowRun(Sculpt s, Transform t, string material, string name, List<(Vector3 p, Quaternion q, float w)> frames,
            Func<(Vector3 p, Quaternion q, float w), Vector3>[] at, float radius, Color from, Color to, bool vivid = false)
        {
            int n = frames.Count;
            if (n < 2) return;
            bool split = FarHues(from, to);
            Color mid = Blend(from, to, 0.5f, whiteAbove: vivid ? 360f : 150f);
            var parts = split
                ? new[] { (0, n / 2, from, mid, "A"), (n / 2, n - 1, mid, to, "B") }
                : new[] { (0, n - 1, from, to, "") };
            foreach ((int i0, int i1, Color ca, Color cz, string tag) in parts)
            {
                var m = new MergeMesh { FixedU = 0f };
                foreach (var f in at)
                {
                    var spine = new List<Vector3>();
                    var radii = new List<float>();
                    for (int i = i0; i <= i1; i++)
                    {
                        spine.Add(f(frames[i]));
                        radii.Add(radius);
                    }
                    m.Tube(spine, radii, 8, -0.5f, 0.5f);
                }
                m.Light(s, t, name, s.Glow(material + tag, ca, cz, bands: 0.5f, speed: 0f));
            }
        }

        private static readonly string[] RoofSigns = { "HOTEL", "DINER", "CLUB", "BAR", "MOTEL" };

        // Night style: the roof dressed as a drowned building's (2026-10-28: a few tiny grey boxes and a lamp): water tanks
        // on legs with a conical lid, big AC units with their fans, vent stacks with hoods, antennas with red lights, and in
        // the middle a neon roof sign on two posts (a dark board, glowing letters on both faces, a neon frame). Every part is
        // at least 0.2 m thick (thinner reads as ink lines), all set on the roof's top.
        private static void RoofClutter(Sculpt s, Transform t, List<(Vector3 p, Quaternion q, float w)> frames, Random rng, int index, Color accent)
        {
            float top = TrackBuilder.TunnelHeight + 0.8f;   // the roof's top
            var metal = new MergeMesh();
            var tanks = new MergeMesh();
            var lights = new MergeMesh();
            var board = new MergeMesh();
            var letters = new MergeMesh();
            var frame = new MergeMesh();
            int signAt = frames.Count / 2;
            bool signed = false;
            int lastKind = -1;
            for (int f = 4; f + 4 < frames.Count; f += 6 + rng.Next(4))
            {
                var fr = frames[f];
                Matrix4x4 at = Matrix4x4.TRS(fr.p, fr.q, Vector3.one);
                metal.At = tanks.At = lights.At = board.At = letters.At = frame.At = at;
                float room = InnerHalf(fr.w) + TrackBuilder.WallThickness;
                if (!signed && f >= signAt - 3)
                {
                    // The roof sign across the middle of the roof, its faces to the sides (seen from the rest of the track).
                    signed = true;
                    float y = top + 3.4f, length = Mathf.Min(7f, 2f * room - 1.4f);
                    foreach (float z in new[] { -length * 0.32f, length * 0.32f })
                        metal.Box(new Vector3(0f, top + 1.2f, z), new Vector3(0.24f, 2.4f, 0.24f));   // thinner than the board: their faces would flicker
                    board.Box(new Vector3(0f, y, 0f), new Vector3(0.3f, 2.1f, length));
                    string word = RoofSigns[rng.Next(RoofSigns.Length)];
                    float height = Mathf.Min(1.3f, (length - 0.8f) / (word.Length * 5.6f / 6f - 1.6f / 6f));
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        Vector3 n = Vector3.right * sx, along = Vector3.forward * sx;   // reads left to right from that side
                        letters.Text(word, new Vector3(sx * 0.21f, y, 0f), along, Vector3.up, height, height * 0.16f, 0.1f);
                        float hz = length / 2f - 0.1f;
                        var corners = new[] { new Vector3(0f, y - 0.95f, -hz), new Vector3(0f, y - 0.95f, hz), new Vector3(0f, y + 0.95f, hz), new Vector3(0f, y + 0.95f, -hz) };
                        for (int k = 0; k < 4; k++)
                            frame.Beam(corners[k] + n * 0.21f, corners[(k + 1) % 4] + n * 0.21f, 0.16f, 0.1f, n);
                    }
                    continue;
                }
                int kind = rng.Next(4);
                if (kind == lastKind) kind = (kind + 1) % 4;
                lastKind = kind;
                float x = R(rng, -1f, 1f) * (room - 2f);
                switch (kind)
                {
                    case 0:   // a water tank on four legs, with its lid and a ladder rail
                        foreach (float lx in new[] { -0.9f, 0.9f })
                            foreach (float lz in new[] { -0.9f, 0.9f })
                                metal.Box(new Vector3(x + lx, top + 0.8f, lz), new Vector3(0.25f, 1.6f, 0.25f));
                        metal.Box(new Vector3(x, top + 1.65f, 0f), new Vector3(2.3f, 0.25f, 2.3f));
                        tanks.Prism(new Vector3(x, top + 3f, 0f), 1.3f, 1.3f, 2.5f, 14);
                        tanks.Prism(new Vector3(x, top + 4.6f, 0f), 1.38f, 0.15f, 0.7f, 14);
                        metal.Prism(new Vector3(x, top + 2.4f, 0f), 1.36f, 1.36f, 0.22f, 14);   // a hoop round it
                        metal.Prism(new Vector3(x, top + 3.6f, 0f), 1.36f, 1.36f, 0.22f, 14);
                        break;
                    case 1:   // a big AC unit with two fans
                        metal.Box(new Vector3(x, top + 0.7f, 0f), new Vector3(2.4f, 1.4f, 2.8f));
                        foreach (float fz in new[] { -0.7f, 0.7f })
                        {
                            metal.Prism(new Vector3(x, top + 1.46f, fz), 0.6f, 0.6f, 0.12f, 12);
                            lights.Box(new Vector3(x + 1.25f, top + 0.7f, fz), new Vector3(0.1f, 0.35f, 0.35f));   // a status light
                        }
                        break;
                    case 2:   // two vent stacks with hoods
                        foreach (float vz in new[] { -0.8f, 0.8f })
                        {
                            float vh = R(rng, 2f, 3f);
                            metal.Box(new Vector3(x, top + 0.2f, vz), new Vector3(1.2f, 0.4f, 1.2f));
                            metal.Prism(new Vector3(x, top + vh / 2f, vz), 0.42f, 0.42f, vh, 10);
                            metal.Prism(new Vector3(x, top + vh + 0.2f, vz), 0.8f, 0.2f, 0.45f, 10);
                        }
                        break;
                    default:  // an antenna mast with a red light
                        metal.Box(new Vector3(x, top + 0.25f, 0f), new Vector3(1.2f, 0.5f, 1.2f));
                        metal.Prism(new Vector3(x, top + 3.5f, 0f), 0.2f, 0.12f, 6.5f, 8);
                        lights.Ball(new Vector3(x, top + 6.95f, 0f), Vector3.one * 0.4f);
                        break;
                }
            }
            metal.Paint(s, t, "Rooftop", new Color(0.3f, 0.32f, 0.38f));
            tanks.Paint(s, t, "Water tanks", new Color(0.42f, 0.27f, 0.2f));
            board.Paint(s, t, "Roof sign", new Color(0.06f, 0.07f, 0.12f));
            lights.Light(s, t, "Rooftop lights", s.Glow("TunnelBeacon", new Color(1f, 0.12f, 0.15f), new Color(1f, 0.55f, 0.5f), pulse: 0.6f));
            letters.Light(s, t, "Roof sign letters", s.Glow($"Tunnel{index}RoofSign", Lighten(accent, 0.35f), Color.white, pulse: 0.25f));
            frame.Light(s, t, "Roof sign frame", s.Glow($"Tunnel{index}RoofSignFrame", accent, Lighten(accent, 0.4f)));
        }

        // A big frame around a tunnel mouth (side = -1 entrance, +1 exit), in the season's colours or the style's neon.
        // Local frame: x right, y up, z forward; its outer face at z = side × 1.3. Pillars from 1 m under the road to
        // h + 3.6 (above the lintel, h + 0.9…h + 3.1), caps and lamps on top of them (buried in the lintel before).
        private static void Portal(Sculpt s, Transform t, (Vector3 p, Quaternion q, float w) frame, string season, (Color wall, Color accent) look,
            Color otherAccent, Random rng, float side, TunnelStyle style, int index)
        {
            float h = TrackBuilder.TunnelHeight;
            float outer = frame.w / 2f + TrackBuilder.BorderWidth + TrackBuilder.WallThickness;
            Vector3 forward = frame.q * Vector3.forward;
            bool neon = style != null;
            string end = side < 0f ? "In" : "Out";
            Transform portal = new GameObject(neon ? $"Portal ({(side < 0f ? "entrance" : "exit")})" : "Portal " + season).transform;
            portal.SetParent(t, false);
            portal.SetPositionAndRotation(frame.p + forward * side * 1.3f, frame.q);
            var toon = new PaintPalette();
            var glow = new MergeMesh();     // neon: band and caps
            var lamps = new MergeMesh();    // neon: the balls on the pillars
            var strips = new MergeMesh();   // neon: strips up the pillars
            Color light = Color.Lerp(look.accent, Color.white, 0.5f);
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * (outer + 1f);
                toon[look.wall].Box(new Vector3(x, (h + 2.6f) / 2f, 0f), new Vector3(2f, h + 4.6f, 2.6f));
                (neon ? glow : toon[look.accent]).Box(new Vector3(x, h + 3.85f, 0f), new Vector3(2.5f, 0.5f, 3.1f));
                (neon ? lamps : toon[light]).Ball(new Vector3(x, h + 4.8f, 0f), Vector3.one * 0.7f, null, 7, 12);
                if (!neon) continue;
                foreach (float dx in new[] { -0.6f, 0.6f })   // up the outer face, stopping under the lintel
                    strips.Box(new Vector3(x + dx, (h + 0.2f) / 2f, side * 1.3f), new Vector3(0.22f, h + 1.4f, 0.12f));
            }
            // The lintel 5 cm proud of the pillars' sides and faces, the band 7 cm proud of the lintel (coplanar faces flicker).
            toon[look.wall * 0.95f].Box(new Vector3(0f, h + 2f, 0f), new Vector3(2f * (outer + 2f) + 0.1f, 2.2f, 2.7f));
            (neon ? glow : toon[look.accent]).Box(new Vector3(0f, h + 2f, side * 1.36f), new Vector3(2f * (outer + 2f) - 0.8f, 0.55f, 0.12f));

            if (Seasons.TryGetValue(season ?? "", out Season theme))
            {
                // A strip of the season's ground on the lintel between the pillars, and its signature tree: a big one in
                // the middle, two smaller beside (random leaf piles and pumpkins read as black lumps before).
                toon[theme.Ground].Box(new Vector3(0f, h + 3.175f, 0f), new Vector3(2f * outer - 0.1f, 0.15f, 2.6f));
                for (int k = -1; k <= 1; k++)
                {
                    toon.At = Matrix4x4.TRS(new Vector3(k * outer * 0.62f, h + 3.25f, 0f), Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), Vector3.one);
                    theme.Tree(toon, rng, k == 0 ? 1.5f : 0.85f);
                }
                toon.At = Matrix4x4.identity;
            }
            else if (neon && !string.IsNullOrEmpty(style.Sign))
            {
                // A neon sign on the lintel: a dark board, glowing letters and a frame in the other end's colour.
                float width = 2f * outer - 0.6f, y = h + 4.1f;
                toon[new Color(0.06f, 0.07f, 0.12f)].Box(new Vector3(0f, y, side * 1.15f), new Vector3(width, 2f, 0.3f));
                var text = new MergeMesh();
                var edge = new MergeMesh();
                Vector3 right = Vector3.right * -side;   // reads left to right for whoever faces this side
                text.Text($"{style.Sign} {index}", new Vector3(0f, y, side * 1.36f), right, Vector3.up, 1.1f, 0.17f, 0.1f);
                Vector3 n = Vector3.forward * side;
                float hw = width / 2f - 0.15f;
                var corners = new[] { new Vector3(-hw, y - 0.85f, 0f), new Vector3(hw, y - 0.85f, 0f), new Vector3(hw, y + 0.85f, 0f), new Vector3(-hw, y + 0.85f, 0f) };
                for (int k = 0; k < 4; k++)
                    edge.Beam(corners[k] + n * 1.36f, corners[(k + 1) % 4] + n * 1.36f, 0.14f, 0.1f, n);
                text.Light(s, portal, "Sign letters", s.Glow($"Tunnel{index}Sign{end}", Lighten(look.accent, 0.3f), Color.white, pulse: 0.25f));
                edge.Light(s, portal, "Sign frame", s.Glow($"Tunnel{index}SignFrame{end}", otherAccent, Lighten(otherAccent, 0.4f)));
            }
            else if (neon)
            {
                // No sign: a row of lamps along the lintel.
                for (int k = -2; k <= 2; k++)
                    lamps.Ball(new Vector3(k * (outer + 1f) / 2f, h + 3.4f, 0f), Vector3.one * 0.45f, null, 6, 10);
            }
            toon.Paint(s, portal, "Portal");
            if (!neon) return;
            glow.Light(s, portal, "Portal neon", s.Glow($"Tunnel{index}Portal{end}", look.accent, Lighten(look.accent, 0.4f)));
            lamps.Light(s, portal, "Portal lamps", s.Glow($"Tunnel{index}Lamp{end}", Lighten(look.accent, 0.55f), Color.white, pulse: 0.35f));
            strips.Light(s, portal, "Pillar strips", s.Glow($"Tunnel{index}PillarStrip{end}", Lighten(look.accent, 0.25f), Lighten(otherAccent, 0.25f),
                bands: 1.5f, speed: 0.4f));
        }

        // ------------------------------------------------------------------ colours

        private static Color Opaque(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);

        private static Color Lighten(Color c, float amount) => Opaque(Color.Lerp(c, Color.white, amount));

        // Both clearly coloured and more than 90° apart on the hue circle.
        private static bool FarHues(Color a, Color z)
        {
            Color.RGBToHSV(a, out float ha, out float sa, out _);
            Color.RGBToHSV(z, out float hz, out float sz, out _);
            return sa > 0.15f && sz > 0.15f && Mathf.Abs(Mathf.DeltaAngle(ha * 360f, hz * 360f)) > 90f;
        }

        // From a to z (u 0..1) through HSV, the short way round the hue circle (an unsaturated end takes the other's hue);
        // colours more than `whiteAbove` degrees apart meet through a pale tint instead (orange to icy blue through cream
        // rather than through green; turquoise to deep red through white rather than purple).
        internal static Color Blend(Color a, Color z, float u, float whiteAbove = 360f)
        {
            Color.RGBToHSV(a, out float ha, out float sa, out float va);
            Color.RGBToHSV(z, out float hz, out float sz, out float vz);
            if (sa < 0.12f) ha = hz;
            if (sz < 0.12f) hz = ha;
            float dh = Mathf.DeltaAngle(ha * 360f, hz * 360f);
            if (Mathf.Abs(dh) > whiteAbove && sa >= 0.12f && sz >= 0.12f)
            {
                Color mid = Color.Lerp(Color.Lerp(a, z, 0.5f), Color.white, 0.6f);
                return u < 0.5f ? Blend(a, mid, u * 2f) : Blend(mid, z, u * 2f - 1f);
            }
            Color c = Color.HSVToRGB(Mathf.Repeat(ha + dh / 360f * u, 1f), Mathf.Lerp(sa, sz, u), Mathf.Lerp(va, vz, u));
            c.a = 1f;
            return c;
        }

        internal static void PrimAt(PrimitiveType type, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Color tint)
        {
            Prim(type, parent, Vector3.zero, scale, tint);
            Transform made = parent.GetChild(parent.childCount - 1);
            made.position = position;
            made.rotation = rotation;
        }

        // ------------------------------------------------------------------ props (local units: metres, y up, at the palette's At)

        private static readonly Color Bark = new Color(0.45f, 0.31f, 0.21f), Leaf = new Color(0.3f, 0.6f, 0.27f), Leaf2 = new Color(0.38f, 0.69f, 0.3f),
            Blossom = new Color(1f, 0.6f, 0.79f), Blossom2 = new Color(0.94f, 0.46f, 0.7f), Pollen = new Color(1f, 0.78f, 0.15f),
            Stalk = new Color(0.3f, 0.58f, 0.25f), CactusGreen = new Color(0.3f, 0.58f, 0.3f), PalmLeaf = new Color(0.28f, 0.6f, 0.24f),
            PalmBark = new Color(0.6f, 0.45f, 0.28f), Coconut = new Color(0.4f, 0.27f, 0.14f), Sandstone = new Color(0.82f, 0.55f, 0.34f),
            Sandstone2 = new Color(0.68f, 0.38f, 0.24f), Water = new Color(0.2f, 0.55f, 0.82f),
            MapleOrange = new Color(0.95f, 0.45f, 0.08f), MapleRed = new Color(0.85f, 0.18f, 0.08f), MapleGold = new Color(0.98f, 0.72f, 0.15f),
            Pumpkin = new Color(1f, 0.62f, 0.2f), PumpkinStem = new Color(0.36f, 0.5f, 0.17f),
            PineGreen = new Color(0.13f, 0.38f, 0.25f), Snow = new Color(0.95f, 0.97f, 1f), Coal = new Color(0.1f, 0.1f, 0.12f),
            Carrot = new Color(1f, 0.5f, 0.1f), Scarf = new Color(0.85f, 0.15f, 0.15f), Ice = new Color(0.5f, 0.8f, 1f), Ice2 = new Color(0.7f, 0.9f, 1f);

        // Bright, saturated (white petals blew out to flat white): sunflower yellow, hot pink, poppy orange, violet.
        private static readonly Color[] FlowerColours =
        {
            new Color(1f, 0.8f, 0.1f), new Color(1f, 0.3f, 0.56f), new Color(1f, 0.42f, 0.14f), new Color(0.62f, 0.36f, 1f),
        };

        private static readonly Color[] MapleColours = { MapleOrange, MapleRed, MapleGold };

        private static void SpringProp(PaintPalette p, Random rng, int kind)
        {
            switch (kind)
            {
                case 0:
                case 1:
                    CherryTree(p, rng, 1f);
                    break;
                case 2:   // a flowering bush: two big blossoms nestled in its top, no stems
                {
                    p[Leaf].Boulder(rng, new Vector3(0f, 0.55f, 0f), new Vector3(1.25f, 0.95f, 1.15f), 0.12f);
                    p[Leaf2].Boulder(rng, new Vector3(0.75f, 0.42f, 0.25f), new Vector3(0.75f, 0.62f, 0.75f), 0.12f);
                    p[Leaf2].Boulder(rng, new Vector3(-0.7f, 0.4f, -0.3f), new Vector3(0.7f, 0.58f, 0.7f), 0.12f);
                    float a = R(rng, 0f, Mathf.PI);
                    int c0 = rng.Next(FlowerColours.Length);
                    for (int k = 0; k < 2; k++)
                    {
                        float sx = k == 0 ? 1f : -1f;
                        Flower(p, new Vector3(Mathf.Cos(a) * 0.62f * sx, 1.15f + k * 0.2f, Mathf.Sin(a) * 0.62f * sx), 0f,
                            FlowerColours[(c0 + k) % FlowerColours.Length], 0.95f);
                    }
                    break;
                }
                default:  // a patch of three big flowers on thick stems, their heads at three heights
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (k * 120f + R(rng, -15f, 15f)) * Mathf.Deg2Rad;
                        var at = new Vector3(Mathf.Cos(a) * 0.85f, 0f, Mathf.Sin(a) * 0.85f);
                        Flower(p, at, 0.9f + k * 0.4f + R(rng, -0.1f, 0.1f), FlowerColours[(k + rng.Next(FlowerColours.Length)) % FlowerColours.Length],
                            R(rng, 0.95f, 1.1f));
                    }
                    break;
            }
        }

        private static void SummerProp(PaintPalette p, Random rng, int kind)
        {
            switch (kind)
            {
                case 0:
                case 1:   // saguaro cactus: ribbed (an 8-sided prism), two arms, a flower on top
                    p[CactusGreen].Prism(new Vector3(0f, 2f, 0f), 0.45f, 0.42f, 4f, 8);
                    p[CactusGreen].Ball(new Vector3(0f, 4f, 0f), new Vector3(0.42f, 0.36f, 0.42f), null, 5, 8);
                    p[CactusGreen].Prism(new Vector3(0.7f, 2f, 0f), 0.26f, 0.26f, 0.9f, 8, Quaternion.Euler(0f, 0f, 90f));
                    p[CactusGreen].Ball(new Vector3(1.15f, 2f, 0f), Vector3.one * 0.26f, null, 5, 8);
                    p[CactusGreen].Prism(new Vector3(1.15f, 2.75f, 0f), 0.26f, 0.24f, 1.5f, 8);
                    p[CactusGreen].Ball(new Vector3(1.15f, 3.5f, 0f), new Vector3(0.24f, 0.2f, 0.24f), null, 5, 8);
                    p[CactusGreen].Prism(new Vector3(-0.65f, 2.8f, 0f), 0.24f, 0.24f, 0.75f, 8, Quaternion.Euler(0f, 0f, 90f));
                    p[CactusGreen].Ball(new Vector3(-1f, 2.8f, 0f), Vector3.one * 0.24f, null, 5, 8);
                    p[CactusGreen].Prism(new Vector3(-1f, 3.4f, 0f), 0.24f, 0.22f, 1.2f, 8);
                    p[CactusGreen].Ball(new Vector3(-1f, 4f, 0f), new Vector3(0.22f, 0.18f, 0.22f), null, 5, 8);
                    p[FlowerColours[1]].Ball(new Vector3(0f, 4.38f, 0f), new Vector3(0.22f, 0.1f, 0.22f), null, 5, 8);
                    break;
                case 2:
                    PalmTree(p, rng, 1f);
                    break;
                default:  // sandstone boulders
                    p[Sandstone].Boulder(rng, new Vector3(0f, 0.6f, 0f), new Vector3(1.2f, 0.85f, 1f), 0.2f);
                    p[Sandstone2].Boulder(rng, new Vector3(1.25f, 0.35f, 0.45f), new Vector3(0.65f, 0.48f, 0.6f), 0.2f);
                    break;
            }
        }

        private static void AutumnProp(PaintPalette p, Random rng, int kind)
        {
            switch (kind)
            {
                case 0:
                case 1:
                    MapleTree(p, rng, 1f);
                    break;
                case 2:   // pumpkins
                    PumpkinPatch(p, rng);
                    break;
                default:  // a leaf pile and loose leaves
                    p[MapleOrange].Boulder(rng, new Vector3(0f, 0.15f, 0f), new Vector3(1.3f, 0.45f, 1f), 0.18f);
                    p[MapleRed].Boulder(rng, new Vector3(0.85f, 0.12f, 0.35f), new Vector3(0.7f, 0.35f, 0.6f), 0.18f);
                    for (int k = 0; k < 5; k++)
                    {
                        float a = R(rng, 0f, Mathf.PI * 2f), d = R(rng, 1.2f, 1.9f);
                        p[k % 2 == 0 ? MapleGold : MapleRed].Box(new Vector3(Mathf.Cos(a) * d, 0.035f + k * 0.01f, Mathf.Sin(a) * d),
                            new Vector3(0.36f, 0.03f, 0.24f), Quaternion.Euler(0f, R(rng, 0f, 360f), 0f));
                    }
                    break;
            }
        }

        private static void WinterProp(PaintPalette p, Random rng, int kind)
        {
            switch (kind)
            {
                case 0:
                case 1:
                    PineTree(p, rng, 1f);
                    break;
                case 2:   // snowman
                    p[Snow].Ball(new Vector3(0f, 0.85f, 0f), Vector3.one * 0.9f);
                    p[Snow].Ball(new Vector3(0f, 2.15f, 0f), Vector3.one * 0.65f);
                    p[Snow].Ball(new Vector3(0f, 3.1f, 0f), Vector3.one * 0.45f);
                    p[Carrot].Prism(new Vector3(0f, 3.1f, 0.62f), 0.1f, 0f, 0.5f, 6, Quaternion.Euler(90f, 0f, 0f));
                    p[Coal].Ball(new Vector3(0.17f, 3.25f, 0.38f), Vector3.one * 0.07f, null, 4, 6);
                    p[Coal].Ball(new Vector3(-0.17f, 3.25f, 0.38f), Vector3.one * 0.07f, null, 4, 6);
                    for (int k = 0; k < 3; k++)
                        p[Coal].Ball(new Vector3(0f, 2.45f - k * 0.3f, 0.62f - Mathf.Abs(k - 1) * 0.04f), Vector3.one * 0.07f, null, 4, 6);
                    p[Coal].Prism(new Vector3(0f, 3.5f, 0f), 0.62f, 0.62f, 0.08f, 12);
                    p[Coal].Prism(new Vector3(0f, 3.8f, 0f), 0.38f, 0.36f, 0.55f, 12);
                    p[Scarf].Prism(new Vector3(0f, 2.72f, 0f), 0.5f, 0.46f, 0.18f, 12);
                    p[Scarf].Box(new Vector3(0.28f, 2.45f, 0.38f), new Vector3(0.2f, 0.5f, 0.08f), Quaternion.Euler(10f, -35f, 8f));
                    foreach (float sx in new[] { -1f, 1f })
                        p[Bark].Beam(new Vector3(sx * 0.55f, 2.25f, 0f), new Vector3(sx * 1.35f, 2.8f, 0f), 0.08f, 0.08f, Vector3.forward);
                    break;
                default:  // ice crystals: faceted hexagonal gems (rotated boxes read as milk cartons), 4-5 m tall (2.4-3 m read small)
                    for (int k = 0; k < 4; k++)
                    {
                        float a = (k * 90f + R(rng, -25f, 25f)) * Mathf.Deg2Rad, d = k == 0 ? 0f : R(rng, 1f, 1.5f);
                        float height = k == 0 ? R(rng, 4.1f, 5.1f) : R(rng, 1.9f, 3.2f);
                        Quaternion tilt = Quaternion.Euler(R(rng, -22f, 22f) * (k == 0 ? 0.3f : 1f), R(rng, 0f, 360f), R(rng, -22f, 22f) * (k == 0 ? 0.3f : 1f));
                        Crystal(p[k % 2 == 0 ? Ice : Ice2], new Vector3(Mathf.Cos(a) * d, -0.25f, Mathf.Sin(a) * d), height, tilt);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ trees and other pieces

        // A trunk from just under the ground to `height`, slightly bent; returns its top.
        private static Vector3 Trunk(PaintPalette p, Random rng, float height, float r0, float r1, Color colour)
        {
            var spine = new List<Vector3>();
            var radii = new List<float>();
            float a = R(rng, 0f, Mathf.PI * 2f);
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            for (int i = 0; i <= 5; i++)
            {
                float u = i / 5f;
                spine.Add(new Vector3(0f, -0.1f + (height + 0.1f) * u, 0f) + dir * (0.05f * height * Mathf.Sin(u * Mathf.PI * 1.2f)));
                radii.Add(Mathf.Lerp(r0, r1, u));
            }
            p[colour].Tube(spine, radii, 7);
            return spine[spine.Count - 1];
        }

        // Spring's tree: a cherry in blossom, its crown in faceted clumps of two pinks.
        private static void CherryTree(PaintPalette p, Random rng, float size)
        {
            Vector3 top = Trunk(p, rng, 3.1f * size, 0.3f * size, 0.17f * size, Bark);
            Color main = rng.Next(2) == 0 ? Blossom : Blossom2, other = main == Blossom ? Blossom2 : Blossom;
            p[main].Boulder(rng, top + new Vector3(0f, 0.75f, 0f) * size, new Vector3(1.7f, 1.3f, 1.7f) * size, 0.12f);
            float a0 = R(rng, 0f, 360f);
            for (int k = 0; k < 4; k++)
            {
                float a = (a0 + k * 90f + R(rng, -20f, 20f)) * Mathf.Deg2Rad;
                Vector3 at = top + new Vector3(Mathf.Cos(a) * 1.25f, R(rng, -0.35f, 0.35f), Mathf.Sin(a) * 1.25f) * size;
                p[k % 2 == 0 ? other : main].Boulder(rng, at, new Vector3(1.05f, 0.85f, 1.05f) * size * R(rng, 0.85f, 1.1f), 0.12f);
            }
        }

        // Summer's tree: a palm with a ringed, bending trunk, drooping fronds and coconuts.
        private static void PalmTree(PaintPalette p, Random rng, float size)
        {
            float h = 6.4f * size, bend = R(rng, 0.9f, 1.6f) * size;
            var spine = new List<Vector3>();
            var radii = new List<float>();
            for (int i = 0; i <= 8; i++)
            {
                float u = i / 8f;
                spine.Add(new Vector3(bend * u * u, -0.1f + (h + 0.1f) * u, 0f));
                radii.Add(Mathf.Lerp(0.34f, 0.2f, u) * size * (i % 2 == 0 ? 1f : 0.88f));
            }
            p[PalmBark].Tube(spine, radii, 7);
            Vector3 crown = spine[spine.Count - 1];
            float a0 = R(rng, 0f, 360f);
            for (int k = 0; k < 7; k++)
            {
                float a = (a0 + k * 360f / 7f + R(rng, -12f, 12f)) * Mathf.Deg2Rad;
                Frond(p[PalmLeaf], crown, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), R(rng, 2.6f, 3.2f) * size, 0.95f * size);
            }
            for (int k = 0; k < 3; k++)
            {
                float a = (a0 + 40f + k * 120f) * Mathf.Deg2Rad;
                p[Coconut].Ball(crown + new Vector3(Mathf.Cos(a) * 0.3f, -0.32f, Mathf.Sin(a) * 0.3f) * size, Vector3.one * 0.24f * size, null, 4, 6);
            }
        }

        // A palm frond: a folded leaf (a V in section) rising then drooping, both faces.
        private static void Frond(MergeMesh m, Vector3 root, Vector3 dir, float length, float width)
        {
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            const int N = 6;
            var l = new Vector3[N + 1];
            var c = new Vector3[N + 1];
            var r = new Vector3[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float u = (float)i / N;
                Vector3 pos = root + dir * (length * u) + Vector3.up * (length * (0.32f * u - 0.62f * u * u));
                float w = width * 0.5f * Mathf.Sin(Mathf.PI * (0.12f + 0.88f * u));
                l[i] = pos - side * w;
                r[i] = pos + side * w;
                c[i] = pos + Vector3.up * (w * 0.35f);
            }
            for (int i = 0; i < N; i++)
            {
                m.Quad(l[i], c[i], c[i + 1], l[i + 1], Vector3.up);
                m.Quad(c[i], r[i], r[i + 1], c[i + 1], Vector3.up);
                m.Quad(l[i], c[i], c[i + 1], l[i + 1], Vector3.down);
                m.Quad(c[i], r[i], r[i + 1], c[i + 1], Vector3.down);
            }
        }

        // Autumn's tree: a maple, its crown in clumps of orange, red and gold.
        private static void MapleTree(PaintPalette p, Random rng, float size)
        {
            Vector3 top = Trunk(p, rng, 3f * size, 0.34f * size, 0.2f * size, Bark);
            Color main = MapleColours[rng.Next(3)];
            p[main].Boulder(rng, top + new Vector3(0f, 0.9f, 0f) * size, new Vector3(1.8f, 1.5f, 1.8f) * size, 0.14f);
            float a0 = R(rng, 0f, 360f);
            for (int k = 0; k < 4; k++)
            {
                float a = (a0 + k * 90f + R(rng, -20f, 20f)) * Mathf.Deg2Rad;
                Vector3 at = top + new Vector3(Mathf.Cos(a) * 1.35f, R(rng, -0.4f, 0.4f), Mathf.Sin(a) * 1.35f) * size;
                p[MapleColours[rng.Next(3)]].Boulder(rng, at, new Vector3(1.1f, 1f, 1.1f) * size * R(rng, 0.85f, 1.1f), 0.14f);
            }
        }

        // Winter's tree: a pine of four tiers of faceted cones, each with a snow cap of the same slope 7 cm proud of it (the
        // stacked green balls read as topiary).
        private static void PineTree(PaintPalette p, Random rng, float size) => Pine(p, rng, size, 4);

        private static void Pine(PaintPalette p, Random rng, float size, int tiers)
        {
            p[Bark].Prism(new Vector3(0f, 0.7f * size, 0f), 0.28f * size, 0.22f * size, 1.6f * size, 7);
            float step = 4.6f / tiers;
            for (int k = 0; k < tiers; k++)
            {
                float u = tiers == 1 ? 0f : (float)k / (tiers - 1), angle = R(rng, 0f, 1f);
                float r = Mathf.Lerp(1.8f, 0.7f, u) * size, h = Mathf.Lerp(2.5f, 1.6f, u) * size, y = (1f + k * step) * size;
                p[PineGreen].Prism(new Vector3(0f, y + h / 2f, 0f), r, 0f, h, 9, null, angle);
                p[Snow].Prism(new Vector3(0f, y + h * 0.75f + 0.12f * size, 0f), r * 0.5f, 0f, h * 0.5f, 9, null, angle);
            }
        }

        private static readonly Vector2[] CrystalProfile = { new Vector2(0f, 0f), new Vector2(0.32f, 0.1f), new Vector2(0.45f, 0.72f), new Vector2(0f, 1f) };

        private static void Crystal(MergeMesh m, Vector3 at, float height, Quaternion rot) =>
            m.Lathe(CrystalProfile, 6, at, rot, new Vector3(height * 0.55f, height, height * 0.55f), smooth: false);

        // A big flower, a cup about 1.6 m across (× size): six thick petals (0.24 m) tilted up 35° round a golden heart, on a
        // green stem 0.28 m thick with two leaves, `stem` m above `at` (0: no stem, the head sits at `at` – blossoms on a
        // bush). 2026-10-28: 7 cm petals and 6 cm stems were all ink outline – black picnic tables and umbrellas.
        private static void Flower(PaintPalette p, Vector3 at, float stem, Color petals, float size)
        {
            if (stem > 0f)
            {
                p[Stalk].Prism(at + Vector3.up * (stem / 2f), 0.14f * size, 0.12f * size, stem + 0.05f, 7);
                for (int k = 0; k < 2; k++)
                {
                    Quaternion turn = Quaternion.Euler(0f, k * 180f + 40f, 0f);
                    p[Leaf].Ball(at + Vector3.up * (stem * (0.3f + 0.25f * k)) + turn * Vector3.right * (0.4f * size),
                        new Vector3(0.42f, 0.1f, 0.2f) * size, turn * Quaternion.Euler(0f, 0f, 25f), 4, 8);
                }
            }
            Vector3 heart = at + Vector3.up * (stem + 0.1f * size);
            for (int k = 0; k < 6; k++)
            {
                Quaternion tilt = Quaternion.Euler(0f, -k * 60f, 0f) * Quaternion.Euler(0f, 0f, 35f);   // x tipped 35° up
                p[petals].Ball(heart + tilt * Vector3.right * (0.48f * size), new Vector3(0.5f, 0.12f, 0.28f) * size, tilt, 4, 8);
            }
            p[Pollen].Ball(heart + Vector3.up * (0.1f * size), new Vector3(0.27f, 0.17f, 0.27f) * size, null, 4, 8);
        }

        // Three pumpkins side by side, 1.8 × the size they had (with the lighter orange: black lumps under the autumn
        // biome's dark rain before), the two biggest carved into jack-o'-lanterns, one facing each way.
        private static void PumpkinPatch(PaintPalette p, Random rng)
        {
            for (int k = 0; k < 3; k++)
                Pumpkin3(p, rng, new Vector3(k * 2.3f - 2.3f, 0f, (k % 2) * 1.4f), (1.2f - k * 0.25f) * 1.8f,
                    k == 0 ? Vector3.back : k == 1 ? Vector3.forward : (Vector3?)null);
        }

        // A pumpkin of six lobes and a stem; `face`: a jack-o'-lantern's glowing face (two eyes and a grin, in the palette's
        // Lit parts) on the lobe facing that way.
        private static void Pumpkin3(PaintPalette p, Random rng, Vector3 at, float s, Vector3? face = null)
        {
            float a0 = face.HasValue ? Mathf.Atan2(face.Value.z, face.Value.x) : 0f;
            for (int k = 0; k < 6; k++)
            {
                float a = a0 + k * 60f * Mathf.Deg2Rad;
                p[Pumpkin].Ball(at + new Vector3(Mathf.Cos(a) * 0.28f * s, 0.42f * s, Mathf.Sin(a) * 0.28f * s), new Vector3(0.32f, 0.44f, 0.32f) * s, null, 5, 7);
            }
            p[PumpkinStem].Prism(at + new Vector3(0f, 0.9f * s, 0f), 0.08f * s, 0.06f * s, 0.3f * s, 6, Quaternion.Euler(R(rng, -15f, 15f), 0f, R(rng, -15f, 15f)));
            if (!face.HasValue) return;
            Vector3 f = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), side = Vector3.Cross(Vector3.up, f);
            Quaternion facing = Quaternion.LookRotation(f, Vector3.up);
            MergeMesh lit = p.Lit;
            // The lobe's front is 0.5-0.58 s out where the features sit; each is a slab 0.12 s deep, its face 0.6 s out.
            Vector3 P(float x, float y) => at + side * (x * s) + Vector3.up * (y * s) + f * (0.54f * s);
            foreach (float ex in new[] { -0.13f, 0.13f })   // eyes: triangles pointing up
                lit.Prism(P(ex, 0.57f), 0.09f * s, 0.09f * s, 0.12f * s, 3, facing * Quaternion.Euler(90f, 0f, 0f), -Mathf.PI / 2f);
            lit.Box(P(0f, 0.3f), new Vector3(0.3f, 0.07f, 0.12f) * s, facing);   // the grin, its ends turned up
            lit.Box(P(-0.17f, 0.34f), new Vector3(0.1f, 0.07f, 0.12f) * s, facing * Quaternion.Euler(0f, 0f, -35f));
            lit.Box(P(0.17f, 0.34f), new Vector3(0.1f, 0.07f, 0.12f) * s, facing * Quaternion.Euler(0f, 0f, 35f));
        }

        // ------------------------------------------------------------------ landmark centrepieces

        // Spring: a giant cherry tree – a gnarled trunk on flaring roots, five branches, blossom clumps at their ends, a
        // carpet of fallen petals.
        private static void GiantCherry(PaintPalette p, Random rng)
        {
            BigTree(p, rng, new[] { Blossom, Blossom2 }, 8.5f);
            for (int k = 0; k < 10; k++)
            {
                float a = R(rng, 0f, Mathf.PI * 2f), d = R(rng, 2.6f, 7.5f);
                p[Blossom2].Prism(new Vector3(Mathf.Cos(a) * d, 0.04f, Mathf.Sin(a) * d), R(rng, 0.5f, 0.9f), R(rng, 0.45f, 0.85f), 0.05f, 7, null, R(rng, 0f, 1f));
            }
        }

        // Autumn: a giant maple, pumpkins at its foot and a carpet of fallen leaves.
        private static void GiantMaple(PaintPalette p, Random rng)
        {
            BigTree(p, rng, MapleColours, 7.5f);
            for (int k = 0; k < 10; k++)
            {
                float a = R(rng, 0f, Mathf.PI * 2f), d = R(rng, 2.8f, 7.5f);
                p[MapleOrange].Prism(new Vector3(Mathf.Cos(a) * d, 0.04f, Mathf.Sin(a) * d), R(rng, 0.5f, 0.9f), R(rng, 0.45f, 0.85f), 0.05f, 7, null, R(rng, 0f, 1f));
            }
            Matrix4x4 at = p.At;
            float b = R(rng, 0f, 360f);   // the patch runs outwards from 4.5 m (clear of the trunk and its roots) to 9.9 m
            p.At = at * Matrix4x4.TRS(new Vector3(Mathf.Cos(b * Mathf.Deg2Rad) * 6.8f, 0.05f, Mathf.Sin(b * Mathf.Deg2Rad) * 6.8f), Quaternion.Euler(0f, -b, 0f), Vector3.one);
            PumpkinPatch(p, rng);
            p.At = at;
        }

        // A tree three times the usual size: trunk, roots, branches, a crown of clumps in these colours.
        private static void BigTree(PaintPalette p, Random rng, Color[] crown, float height)
        {
            var spine = new List<Vector3>();
            var radii = new List<float>();
            for (int i = 0; i <= 6; i++)
            {
                float u = i / 6f;
                spine.Add(new Vector3(Mathf.Sin(u * 5f) * 0.35f, -0.3f + (height + 0.3f) * u, Mathf.Cos(u * 4f) * 0.3f - 0.3f));
                radii.Add(Mathf.Lerp(1.5f, 0.7f, u));
            }
            p[Bark].Tube(spine, radii, 10);
            float a0 = R(rng, 0f, 360f);
            for (int k = 0; k < 4; k++)   // roots flaring into the ground
            {
                float a = (a0 + k * 90f + R(rng, -15f, 15f)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                p[Bark].Tube(new[] { dir * 0.6f + Vector3.up * 1.2f, dir * 1.6f + Vector3.up * 0.3f, dir * 2.6f - Vector3.up * 0.25f },
                    new[] { 0.65f, 0.45f, 0.22f }, 8);
            }
            for (int k = 0; k < 5; k++)   // branches, each ending in a clump of blossom or leaves
            {
                float a = (a0 + 36f + k * 72f + R(rng, -12f, 12f)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float from = height * R(rng, 0.7f, 0.9f), reach = R(rng, 4f, 5.5f);
                Vector3 start = new Vector3(spine[5].x, from, spine[5].z), endAt = start + dir * reach + Vector3.up * R(rng, 2f, 3.5f);
                p[Bark].Tube(new[] { start, start + dir * reach * 0.5f + Vector3.up * 1.6f, endAt }, new[] { 0.55f, 0.38f, 0.22f }, 7);
                for (int c = 0; c < 3; c++)
                {
                    Vector3 o = new Vector3(R(rng, -1.4f, 1.4f), R(rng, -0.5f, 1.2f), R(rng, -1.4f, 1.4f));
                    float r = R(rng, 1.8f, 2.6f);
                    p[crown[(k + c) % crown.Length]].Boulder(rng, endAt + o, new Vector3(r, r * 0.8f, r), 0.12f, 5, 8);
                }
            }
            p[crown[0]].Boulder(rng, spine[6] + Vector3.up * 2.4f, new Vector3(3.2f, 2.6f, 3.2f), 0.12f, 5, 9);
            p[crown[1 % crown.Length]].Boulder(rng, spine[6] + new Vector3(1.6f, 1.2f, -1.2f), new Vector3(2.4f, 2f, 2.4f), 0.12f, 5, 8);
        }

        // Summer: a palm grove round an oasis – a pond rimmed with sandstone, four tall palms leaning outwards.
        private static void PalmGrove(PaintPalette p, Random rng)
        {
            p[Water].Prism(new Vector3(0f, 0.04f, 0f), 3.2f, 3.2f, 0.08f, 14);
            for (int k = 0; k < 7; k++)
            {
                float a = (k * 360f / 7f + R(rng, -15f, 15f)) * Mathf.Deg2Rad;
                p[k % 2 == 0 ? Sandstone : Sandstone2].Boulder(rng, new Vector3(Mathf.Cos(a) * 3.5f, 0.25f, Mathf.Sin(a) * 3.5f),
                    new Vector3(R(rng, 0.6f, 1f), R(rng, 0.4f, 0.65f), R(rng, 0.6f, 1f)), 0.2f);
            }
            Matrix4x4 at = p.At;
            float a0 = R(rng, 0f, 360f);
            for (int k = 0; k < 4; k++)
            {
                float a = a0 + k * 90f + R(rng, -20f, 20f), d = R(rng, 5f, 6.2f);
                var pos = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * d, 0f, Mathf.Sin(a * Mathf.Deg2Rad) * d);
                p.At = at * Matrix4x4.TRS(pos, Quaternion.Euler(0f, -a, 0f), Vector3.one);   // the palm bends along its +x: outwards
                PalmTree(p, rng, R(rng, 1.7f, 2.3f));
            }
            p.At = at;
        }

        // Winter: a giant snowy pine and, beside it, a spire of tall ice crystals (1.45 × the size they had: they read small
        // beside the pine).
        private static void GiantPine(PaintPalette p, Random rng)
        {
            Pine(p, rng, 2.8f, 6);
            var spire = new Vector3(6.6f, -0.5f, 2.9f);
            Crystal(p[Ice], spire, 15.2f, Quaternion.Euler(4f, R(rng, 0f, 60f), -5f));
            Crystal(p[Ice2], spire + new Vector3(2.1f, 0f, -1.6f), 10.2f, Quaternion.Euler(-10f, R(rng, 0f, 60f), -16f));
            Crystal(p[Ice2], spire + new Vector3(-1.6f, 0f, 1.8f), 8f, Quaternion.Euler(14f, R(rng, 0f, 60f), 10f));
            Crystal(p[Ice], spire + new Vector3(0.8f, 0f, 2.5f), 5.2f, Quaternion.Euler(18f, R(rng, 0f, 60f), -22f));
        }

        private static float R(Random rng, float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

        // ------------------------------------------------------------------ painted primitives (every map uses these)

        internal static void Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color tint) =>
            Prim(type, parent, localPos, scale, tint, Quaternion.identity);

        internal static void Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color tint, Quaternion rot)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = type.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var kit = go.AddComponent<KitRoad>();
            kit.kind = KitRoadKind.Concrete;
            kit.paint = Toon;
            kit.tint = new Color(Mathf.Clamp01(tint.r), Mathf.Clamp01(tint.g), Mathf.Clamp01(tint.b), 1f);
        }
    }

    // Parts of one colour (or one glow) merged into one mesh, so a detailed set piece costs a few renderers (as
    // RushHourDecor.Batch): flat-shaded quads, boxes, beams, prisms, cones and lathes, faceted boulders, smooth balls and
    // tubes, stroke-font lettering, copies of other meshes. Positions are in the space of the object the mesh is shown
    // under, through At (set it before adding parts); FixedU / FixedV give every vertex added that uv (a Glow gradient
    // or band position per part). Paint / Light save the mesh into the map's Sculpt asset (DecorMeshes.asset, which
    // ships in the bundle) and show it with toon paint or a Glow material.
    internal sealed class MergeMesh
    {
        private readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _t = new List<int>();
        private Matrix4x4 _at = Matrix4x4.identity, _normals = Matrix4x4.identity;
        private static int _made;
        public float? FixedU, FixedV;

        public Matrix4x4 At
        {
            get => _at;
            set
            {
                _at = value;
                _normals = value.inverse.transpose;
            }
        }

        public bool Empty => _t.Count == 0;

        private int Vertex(Vector3 p, Vector3 n, Vector2 uv)
        {
            _v.Add(_at.MultiplyPoint3x4(p));
            Vector3 wn = _normals.MultiplyVector(n);
            _n.Add(wn.sqrMagnitude > 1e-12f ? wn.normalized : Vector3.up);
            _uv.Add(new Vector2(FixedU ?? uv.x, FixedV ?? uv.y));
            return _v.Count - 1;
        }

        // A flat quad a-b-c-d (in order round it) facing `outward`.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(c - a, d - b);
            if (Vector3.Dot(n, outward) < 0f)
            {
                Vector3 x = b;
                b = d;
                d = x;
                n = -n;
            }
            n = n.sqrMagnitude > 1e-12f ? n.normalized : outward.normalized;
            int i = Vertex(a, n, new Vector2(0f, 0f));
            Vertex(b, n, new Vector2(1f, 0f));
            Vertex(c, n, new Vector2(1f, 1f));
            Vertex(d, n, new Vector2(0f, 1f));
            _t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        // A flat triangle facing `outward` (Unity's front face: (A, B, C) faces Cross(B - A, C - A)).
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0f)
            {
                Vector3 x = b;
                b = c;
                c = x;
                n = -n;
            }
            n = n.sqrMagnitude > 1e-12f ? n.normalized : outward.normalized;
            int i = Vertex(a, n, new Vector2(0.5f, 1f));
            Vertex(b, n, new Vector2(0f, 0f));
            Vertex(c, n, new Vector2(1f, 0f));
            _t.AddRange(new[] { i, i + 1, i + 2 });
        }

        // A box centred on c.
        public void Box(Vector3 c, Vector3 size, Quaternion? rot = null)
        {
            Quaternion r = rot ?? Quaternion.identity;
            Vector3 x = r * Vector3.right * (size.x / 2f), y = r * Vector3.up * (size.y / 2f), z = r * Vector3.forward * (size.z / 2f);
            Quad(c + x - y - z, c + x + y - z, c + x + y + z, c + x - y + z, x);
            Quad(c - x - y - z, c - x - y + z, c - x + y + z, c - x + y - z, -x);
            Quad(c - x + y - z, c - x + y + z, c + x + y + z, c + x + y - z, y);
            Quad(c - x - y - z, c + x - y - z, c + x - y + z, c - x - y + z, -y);
            Quad(c - x - y + z, c + x - y + z, c + x + y + z, c - x + y + z, z);
            Quad(c - x - y - z, c - x + y - z, c + x + y - z, c + x - y - z, -z);
        }

        // A box from a to b, `width` across and `depth` along `normal` (strokes of lettering lie across the normal); its
        // ends reach half its width past a and b, so the corners of a polyline close.
        public void Beam(Vector3 a, Vector3 b, float width, float depth, Vector3 normal)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-8f) return;
            Vector3 along = d.normalized;
            Vector3 up = Vector3.ProjectOnPlane(normal, along);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.Cross(along, Mathf.Abs(along.y) < 0.9f ? Vector3.up : Vector3.right);
            Box((a + b) / 2f, new Vector3(width, depth, d.magnitude + width), Quaternion.LookRotation(along, up));
        }

        // A flat band along `spine` (sand falls): widths[i] across – along `across` squared to the spine – and `thickness`
        // deep, both faces, its sides and its two ends closed.
        public void Strip(IList<Vector3> spine, IList<float> widths, Vector3 across, float thickness)
        {
            int n = spine.Count;
            if (n < 2) return;
            var fl = new Vector3[n];
            var fr = new Vector3[n];
            var bl = new Vector3[n];
            var br = new Vector3[n];
            var sides = new Vector3[n];
            var normals = new Vector3[n];
            var tangents = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (spine[Mathf.Min(i + 1, n - 1)] - spine[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.ProjectOnPlane(across, t).normalized;
                Vector3 normal = Vector3.Cross(t, side).normalized;
                Vector3 w = side * (widths[i] / 2f), d = normal * (thickness / 2f);
                fl[i] = spine[i] - w + d;
                fr[i] = spine[i] + w + d;
                bl[i] = spine[i] - w - d;
                br[i] = spine[i] + w - d;
                sides[i] = side;
                normals[i] = normal;
                tangents[i] = t;
            }
            for (int i = 0; i + 1 < n; i++)
            {
                Vector3 nn = normals[i] + normals[i + 1], ss = sides[i] + sides[i + 1];
                Quad(fl[i], fr[i], fr[i + 1], fl[i + 1], nn);
                Quad(bl[i], br[i], br[i + 1], bl[i + 1], -nn);
                Quad(fl[i], bl[i], bl[i + 1], fl[i + 1], -ss);
                Quad(fr[i], br[i], br[i + 1], fr[i + 1], ss);
            }
            Quad(fl[0], fr[0], br[0], bl[0], -tangents[0]);
            Quad(fl[n - 1], fr[n - 1], br[n - 1], bl[n - 1], tangents[n - 1]);
        }

        // A flat slab: the convex polygon `outline` (in the plane of `u` and `v`, around c), `thickness` deep along their
        // normal, both faces and its sides (broken walls, shards).
        public void Slab(Vector3 c, Vector3 u, Vector3 v, IList<Vector2> outline, float thickness)
        {
            int k = outline.Count;
            if (k < 3) return;
            Vector3 n = Vector3.Cross(u, v).normalized * (thickness / 2f);
            var front = new Vector3[k];
            var back = new Vector3[k];
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < k; i++)
            {
                Vector3 p = c + u * outline[i].x + v * outline[i].y;
                front[i] = p + n;
                back[i] = p - n;
                mid += p;
            }
            mid /= k;
            for (int i = 1; i + 1 < k; i++)
            {
                Tri(front[0], front[i], front[i + 1], n);
                Tri(back[0], back[i], back[i + 1], -n);
            }
            for (int i = 0; i < k; i++)
            {
                int j = (i + 1) % k;
                Quad(front[i], front[j], back[j], back[i], (front[i] + back[j]) / 2f - mid);
            }
        }

        // An upright prism or frustum (a cone when a radius is 0) centred on c, h tall, flat-shaded, capped; `angle`
        // (radians) turns its corners.
        public void Prism(Vector3 c, float rBottom, float rTop, float h, int sides, Quaternion? rot = null, float angle = 0f)
        {
            Quaternion r = rot ?? Quaternion.identity;
            Vector3 up = r * Vector3.up * (h / 2f);
            for (int k = 0; k < sides; k++)
            {
                float a0 = angle + k * Mathf.PI * 2f / sides, a1 = angle + (k + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = r * new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = r * new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 side = (d0 + d1).normalized;
                if (rBottom > 0f && rTop > 0f) Quad(c - up + d0 * rBottom, c - up + d1 * rBottom, c + up + d1 * rTop, c + up + d0 * rTop, side);
                else if (rTop > 0f) Tri(c - up, c + up + d1 * rTop, c + up + d0 * rTop, side);
                else if (rBottom > 0f) Tri(c + up, c - up + d0 * rBottom, c - up + d1 * rBottom, side);
                if (rBottom > 0f) Tri(c - up, c - up + d1 * rBottom, c - up + d0 * rBottom, -up);
                if (rTop > 0f) Tri(c + up, c + up + d0 * rTop, c + up + d1 * rTop, up);
            }
        }

        // A smooth ellipsoid centred on c.
        public void Ball(Vector3 c, Vector3 radii, Quaternion? rot = null, int rings = 6, int segments = 9)
        {
            Quaternion r = rot ?? Quaternion.identity;
            int first = _v.Count, cols = segments + 1;
            for (int i = 0; i <= rings; i++)
            {
                float lat = Mathf.PI * i / rings;
                for (int j = 0; j <= segments; j++)
                {
                    float lon = j * Mathf.PI * 2f / segments;
                    var dir = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
                    Vertex(c + r * Vector3.Scale(dir, radii), r * new Vector3(dir.x / radii.x, dir.y / radii.y, dir.z / radii.z),
                        new Vector2((float)j / segments, 1f - (float)i / rings));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a = first + i * cols + j;
                    Face(a, a + 1, a + cols + 1, a + cols);
                }
        }

        // A faceted, lumpy ball (boulders, leafy crowns, leaf piles): flat-shaded triangles, each vertex pushed in or out
        // by up to `rough`.
        public void Boulder(Random rng, Vector3 c, Vector3 radii, float rough = 0.15f, int rings = 4, int segments = 7, Quaternion? rot = null)
        {
            Quaternion r = rot ?? Quaternion.identity;
            var grid = new Vector3[rings + 1, segments];
            float twist = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i <= rings; i++)
            {
                float lat = Mathf.PI * i / rings;
                float poleNoise = 1f + rough * (float)(rng.NextDouble() * 2.0 - 1.0);
                for (int j = 0; j < segments; j++)
                {
                    float lon = twist + (j + (i % 2) * 0.5f) * Mathf.PI * 2f / segments;
                    var dir = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
                    float k = i == 0 || i == rings ? poleNoise : 1f + rough * (float)(rng.NextDouble() * 2.0 - 1.0);
                    grid[i, j] = c + r * Vector3.Scale(dir * k, radii);
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segments; j++)
                {
                    int j1 = (j + 1) % segments;
                    Vector3 a = grid[i, j], b = grid[i, j1], d = grid[i + 1, j], e = grid[i + 1, j1];
                    if (i > 0) Tri(a, b, e, (a + b + e) / 3f - c);
                    if (i < rings - 1 || i == 0) Tri(a, e, d, (a + e + d) / 3f - c);
                }
        }

        // A smooth tube along `spine` (a radius per point): uv.x round it, uv.y from v0 to v1 along it; flat end caps unless
        // `caps` is false.
        public void Tube(IList<Vector3> spine, IList<float> radii, int sides = 8, float v0 = 0f, float v1 = 1f, bool caps = true)
        {
            int n = spine.Count;
            if (n < 2) return;
            var along = new float[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + (spine[i] - spine[i - 1]).magnitude;
            float total = along[n - 1];
            int first = _v.Count, cols = sides + 1;
            Vector3 normal = Vector3.zero;
            var ends = new List<Vector3>[2] { new List<Vector3>(), new List<Vector3>() };
            Vector3[] tangents = new Vector3[2];
            for (int i = 0; i < n; i++)
            {
                Vector3 tangent = (spine[Mathf.Min(i + 1, n - 1)] - spine[Mathf.Max(i - 1, 0)]).normalized;
                if (i == 0) normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                else normal = Vector3.Cross(Vector3.Cross(tangent, normal), tangent).normalized;   // parallel transport
                Vector3 binormal = Vector3.Cross(tangent, normal);
                float v = total > 0f ? Mathf.Lerp(v0, v1, along[i] / total) : v0;
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    Vector3 dir = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    Vector3 p = spine[i] + dir * radii[i];
                    Vertex(p, dir, new Vector2((float)s / sides, v));
                    if (s < sides && (i == 0 || i == n - 1)) ends[i == 0 ? 0 : 1].Add(p);
                }
                if (i == 0) tangents[0] = -tangent;
                if (i == n - 1) tangents[1] = tangent;
            }
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = first + i * cols + s;
                    Face(a, a + 1, a + cols + 1, a + cols);
                }
            if (!caps) return;
            for (int e = 0; e < 2; e++)
            {
                Vector3 centre = spine[e == 0 ? 0 : n - 1];
                for (int s = 0; s < sides; s++) Tri(centre, ends[e][s], ends[e][(s + 1) % sides], tangents[e]);
            }
        }

        // A solid of revolution about y: profile points (radius, height) from bottom to top, flat along the profile (sharp
        // edges between its segments), smooth or faceted round; placed at c, turned and scaled.
        public void Lathe(IList<Vector2> profile, int sides, Vector3 c, Quaternion rot, Vector3 scale, bool smooth = true, float angle = 0f)
        {
            for (int k = 0; k + 1 < profile.Count; k++)
            {
                Vector2 p0 = profile[k], p1 = profile[k + 1], t = p1 - p0;
                if (t.sqrMagnitude < 1e-10f) continue;
                Vector2 n2 = new Vector2(t.y, -t.x).normalized;   // outward in (radius, height)
                for (int s = 0; s < sides; s++)
                {
                    float a0 = angle + s * Mathf.PI * 2f / sides, a1 = angle + (s + 1) * Mathf.PI * 2f / sides, am = (a0 + a1) / 2f;
                    Vector3 P(Vector2 p, float a) => c + rot * Vector3.Scale(new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x), scale);
                    Vector3 N(float a) => rot * new Vector3(Mathf.Cos(a) * n2.x / scale.x, n2.y / scale.y, Mathf.Sin(a) * n2.x / scale.z);
                    int i = Vertex(P(p0, a0), N(smooth ? a0 : am), new Vector2((float)s / sides, k));
                    Vertex(P(p0, a1), N(smooth ? a1 : am), new Vector2((float)(s + 1) / sides, k));
                    Vertex(P(p1, a1), N(smooth ? a1 : am), new Vector2((float)(s + 1) / sides, k + 1));
                    Vertex(P(p1, a0), N(smooth ? a0 : am), new Vector2((float)s / sides, k + 1));
                    Face(i, i + 1, i + 2, i + 3);
                }
            }
        }

        // Another mesh (a Sculpt shape), placed.
        public void Add(Mesh mesh, Vector3 c, Quaternion rot, Vector3 scale)
        {
            Vector3[] v = mesh.vertices, n = mesh.normals;
            Vector2[] uv = mesh.uv;
            int[] t = mesh.triangles;
            Matrix4x4 m = Matrix4x4.TRS(c, rot, scale), mn = m.inverse.transpose;
            int o = _v.Count;
            for (int i = 0; i < v.Length; i++)
                Vertex(m.MultiplyPoint3x4(v[i]), i < n.Length ? mn.MultiplyVector(n[i]) : Vector3.up, i < uv.Length ? uv[i] : Vector2.zero);
            foreach (int i in t) _t.Add(o + i);
        }

        // Lettering in a stroke font (capitals, digits, a few signs), centred on c, `height` tall, reading along `right`.
        public void Text(string text, Vector3 c, Vector3 right, Vector3 up, float height, float stroke, float depth)
        {
            float unit = height / 6f, advance = 5.6f * unit;
            float width = text.Length * advance - 1.6f * unit;
            Vector3 normal = Vector3.Cross(right, up).normalized;
            Vector3 origin = c - right * (width / 2f) - up * (height / 2f);
            for (int i = 0; i < text.Length; i++)
            {
                if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[i]), out string glyph)) continue;
                Vector3 cell = origin + right * (i * advance);
                foreach (string line in glyph.Split('|'))
                {
                    string[] points = line.Split(' ');
                    Vector3 last = Vector3.zero;
                    for (int k = 0; k < points.Length; k++)
                    {
                        string[] xy = points[k].Split(',');
                        Vector3 p = cell + right * (int.Parse(xy[0]) * unit) + up * (int.Parse(xy[1]) * unit);
                        if (k > 0) Beam(last, p, stroke, depth, normal);
                        last = p;
                    }
                }
            }
        }

        // Polylines on a 4 × 6 grid (x, y), strokes separated by '|'.
        private static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['A'] = "0,0 0,4 2,6 4,4 4,0|0,3 4,3",
            ['B'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3|3,3 4,2 4,1 3,0 0,0",
            ['C'] = "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1",
            ['D'] = "0,0 0,6 2,6 4,4 4,2 2,0 0,0",
            ['E'] = "4,6 0,6 0,0 4,0|0,3 3,3",
            ['F'] = "4,6 0,6 0,0|0,3 3,3",
            ['G'] = "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1 4,3 2,3",
            ['H'] = "0,0 0,6|4,0 4,6|0,3 4,3",
            ['I'] = "1,6 3,6|2,6 2,0|1,0 3,0",
            ['J'] = "4,6 4,1 3,0 1,0 0,1",
            ['K'] = "0,0 0,6|4,6 0,2|1,3 4,0",
            ['L'] = "0,6 0,0 4,0",
            ['M'] = "0,0 0,6 2,3 4,6 4,0",
            ['N'] = "0,0 0,6 4,0 4,6",
            ['O'] = "1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0",
            ['P'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3",
            ['Q'] = "1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0|2,2 4,0",
            ['R'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3|2,3 4,0",
            ['S'] = "4,5 3,6 1,6 0,5 0,4 1,3 3,3 4,2 4,1 3,0 1,0 0,1",
            ['T'] = "0,6 4,6|2,6 2,0",
            ['U'] = "0,6 0,1 1,0 3,0 4,1 4,6",
            ['V'] = "0,6 2,0 4,6",
            ['W'] = "0,6 1,0 2,4 3,0 4,6",
            ['X'] = "0,0 4,6|0,6 4,0",
            ['Y'] = "0,6 2,3 4,6|2,3 2,0",
            ['Z'] = "0,6 4,6 0,0 4,0",
            ['0'] = "1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0|1,1 3,5",
            ['1'] = "1,5 2,6 2,0|1,0 3,0",
            ['2'] = "0,5 1,6 3,6 4,5 4,4 0,0 4,0",
            ['3'] = "0,5 1,6 3,6 4,5 4,4 3,3 4,2 4,1 3,0 1,0 0,1|1,3 3,3",
            ['4'] = "3,0 3,6 0,2 4,2",
            ['5'] = "4,6 0,6 0,3 3,3 4,2 4,1 3,0 0,0",
            ['6'] = "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1 4,2 3,3 0,3",
            ['7'] = "0,6 4,6 1,0",
            ['8'] = "1,3 0,4 0,5 1,6 3,6 4,5 4,4 3,3 1,3 0,2 0,1 1,0 3,0 4,1 4,2 3,3",
            ['9'] = "4,3 1,3 0,4 0,5 1,6 3,6 4,5 4,1 3,0 1,0 0,1",
            ['>'] = "0,6 4,3 0,0",
            ['<'] = "4,6 0,3 4,0",
            ['!'] = "2,6 2,2|2,1 2,0",
            ['-'] = "0,3 4,3",
        };

        // Two triangles over a, b, c, d (round a quad of the grid) facing the way the vertices' normals point.
        private void Face(int a, int b, int c, int d)
        {
            Vector3 n = Vector3.Cross(_v[c] - _v[a], _v[d] - _v[b]);
            Vector3 outward = _n[a] + _n[b] + _n[c] + _n[d];
            if (Vector3.Dot(n, outward) >= 0f) _t.AddRange(new[] { a, b, c, a, c, d });
            else _t.AddRange(new[] { a, c, b, a, d, c });
        }

        // The mesh, saved into the map's Sculpt asset: positions, normals as built and uv0 only, like Sculpt's own shapes
        // (toon paint and Glow need nothing more; the bundle carries every vertex: with tangents and uv1 Four Seasons'
        // bundle was 12 MB).
        public Mesh Build(Sculpt s, string name)
        {
            var mesh = new Mesh { name = $"sd_{++_made}_{name}" };
            if (_v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateBounds();
            string asset = AssetDatabase.GetAssetPath(s.Disc());   // any Sculpt shape: its asset then exists
            if (!string.IsNullOrEmpty(asset)) AssetDatabase.AddObjectToAsset(mesh, asset);
            else Debug.LogWarning($"[Slip & Skid] Decor: DecorMeshes.asset not found; '{name}' stays in the scene only.");
            return mesh;
        }

        public GameObject Paint(Sculpt s, Transform parent, string name, Color tint) =>
            Empty ? null : s.Painted(Build(s, name), parent, Vector3.zero, Quaternion.identity, Vector3.one, tint, name);

        public GameObject Light(Sculpt s, Transform parent, string name, Material glow) =>
            Empty ? null : s.Glowing(Build(s, name), parent, Vector3.zero, Quaternion.identity, Vector3.one, glow, name);
    }

    // One MergeMesh per colour (a set piece's toon parts: one renderer per colour); At applies to all of them. Lit: parts
    // lit from inside (one Glow for the whole palette, e.g. jack-o'-lanterns' faces), shown by Light.
    internal sealed class PaintPalette
    {
        private readonly Dictionary<Color, MergeMesh> _by = new Dictionary<Color, MergeMesh>();
        private readonly List<Color> _order = new List<Color>();
        private Matrix4x4 _at = Matrix4x4.identity;
        private MergeMesh _lit;

        public Matrix4x4 At
        {
            get => _at;
            set
            {
                _at = value;
                foreach (MergeMesh m in _by.Values) m.At = value;
                if (_lit != null) _lit.At = value;
            }
        }

        public MergeMesh Lit => _lit ?? (_lit = new MergeMesh { At = _at });

        public bool HasLit => _lit != null && !_lit.Empty;

        public GameObject Light(Sculpt s, Transform parent, string name, Material glow) => HasLit ? _lit.Light(s, parent, name, glow) : null;

        public MergeMesh this[Color c]
        {
            get
            {
                c.a = 1f;
                if (!_by.TryGetValue(c, out MergeMesh m))
                {
                    _by[c] = m = new MergeMesh { At = _at };
                    _order.Add(c);
                }
                return m;
            }
        }

        // Paints each colour's mesh under `parent` ("name", or "name 1", "name 2"…); returns how many were made.
        public int Paint(Sculpt s, Transform parent, string name)
        {
            int made = 0;
            foreach (Color c in _order)
                if (_by[c].Paint(s, parent, _order.Count == 1 ? name : $"{name} {made + 1}", c) != null) made++;
            return made;
        }
    }
}
