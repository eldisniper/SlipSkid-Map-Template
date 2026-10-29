using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlipSkid.MapKit.SDK;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using T = SlipSkid.MapKit.Editor.SlipSkidMapTools;

namespace SlipSkid.MapKit.Editor
{
    // Builds a map from a JSON file (#53): the course is a list of steps, one per TrackBuilder call, so a map needs no
    // C#. Format: docs/JSON_MAPS.md (schema docs/mapkit-map.schema.json, example maps/MegaStunt/map.json). Every
    // mistake (unknown key, wrong type, missing finish…) stops the build with the step's path, e.g. "course[12].loop".
    public static class JsonMapGenerator
    {
        // build_map.py passes the file here: the project may be a temporary copy (--copy).
        public const string EnvVar = "SLIPSKID_MAP_JSON";

        [MenuItem("Slip & Skid/1j. Create Map from JSON file...", priority = 3)]
        public static void CreateFromFile()
        {
            string path = EditorUtility.OpenFilePanel("Map JSON file", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Generate(path, interactive: true);
            }
            catch (MapJson.Error e)
            {
                EditorUtility.DisplayDialog("Map JSON", e.Message, "OK");
            }
        }

        // Command line: python tools/build_map.py maps/<Map>/map.json
        public static void BatchCreateAndBuild()
        {
            string path = Environment.GetEnvironmentVariable(EnvVar);
            try
            {
                if (string.IsNullOrEmpty(path)) throw new MapJson.Error($"{EnvVar} is not set (use tools/build_map.py <map.json>).");
                Generate(path, interactive: false);
            }
            catch (MapJson.Error e)
            {
                Debug.LogError($"[Slip & Skid] Map JSON error in {path}: {e.Message}");
                EditorApplication.Exit(1);
                return;
            }
            T.BuildAndInstall(interactive: false);
        }

        public static void Generate(string jsonPath, bool interactive)
        {
            if (!File.Exists(jsonPath)) throw new MapJson.Error($"file not found: {jsonPath}");
            if (!(MapJson.Parse(File.ReadAllText(jsonPath)) is MapJson.Obj top)) throw new MapJson.Error("the file must be a JSON object { ... }");
            var file = new Node(top, "");

            Node meta = file.Obj("map", required: true);
            string id = meta.Str("id", null) ?? throw meta.Missing("id");
            if (T.Sanitize(id) != id) throw meta.Bad("id", $"\"{id}\": letters and digits only (it names the folder, the bundle and the package)");
            string name = meta.Str("name", ObjectNames.NicifyVariableName(id));
            string author = meta.Str("author", "");
            int difficulty = meta.Int("difficulty", 3, 1, 5);
            string host = meta.Str("host", "");
            float deathDepth = meta.Float("deathDepth", 30f);
            bool seasons = meta.Bool("seasons", false);
            bool reversible = meta.Bool("reversible", false);
            meta.Done();

            Node podium = file.Obj("podium", required: false);
            Vector3 podiumPos = new Vector3(0f, 0f, -80f);
            float podiumYaw = 180f;
            if (podium != null)
            {
                podiumPos = podium.Vec3("position", podiumPos);
                podiumYaw = podium.Float("yaw", podiumYaw);
                podium.Done();
            }

            Node decor = file.Obj("decor", required: false);
            string theme = "None", tunnelArt = "None";
            bool finishBarrier = true;
            if (decor != null)
            {
                theme = decor.Choice("theme", "None", "NightCity", "DrownedCity", "Seasons", "None");
                tunnelArt = decor.Choice("tunnelArt", theme == "Seasons" ? "Seasons" : "None", "Neon", "Seasons", "None");
                finishBarrier = decor.Bool("finishBarrier", true);
                decor.Done();
            }

            List<object> course = file.List("course", required: true);
            file.Done();
            CheckCourse(course);

            // ---------------------------------------------------------- scene
            string folder = $"{T.MapsFolder}/{id}";
            string scenePath = $"{folder}/{id}.unity";
            if (interactive && File.Exists(scenePath) &&
                !EditorUtility.DisplayDialog("Map exists", $"{scenePath} already exists. Overwrite it?", "Overwrite", "Cancel"))
            {
                return;
            }
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(folder);

            URPSetup.Ensure();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var b = new TrackBuilder(folder);
            new Sculpt(folder);   // the map's own: the shared decor's meshes and glows go into it (Sculpt.For)

            // No camera, lights or world gravity: the host level brings its own.
            var rootGo = new GameObject(id);
            KitMap root = rootGo.AddComponent<KitMap>();
            root.mapName = name;
            root.author = author;
            root.difficulty = difficulty;
            if (host != "") root.hostLevel = host;
            root.deathDepth = deathDepth;
            root.seasonalBiomes = seasons;
            root.isReversible = reversible;
            b.Root = rootGo.transform;
            b.Init();

            Run(b, course, "course");

            // ---------------------------------------------------------- start, spawns, podium, decor
            T.AddStartGrid(rootGo.transform, T.StartRowZ);
            T.AddPodium(rootGo.transform, podiumPos, Quaternion.Euler(0f, podiumYaw, 0f), b.ConcreteMat);
            int props = 0;
            if (theme == "NightCity" || theme == "DrownedCity")
            {
                var city = theme == "NightCity" ? CityDecor.Theme.NightCity : CityDecor.Theme.DrownedCity;
                props = CityDecor.Add(rootGo.transform, b, city);   // needs MapKitLoader 1.1.0 (paint)
                CityDecor.AddFinish(rootGo.transform, b, barrier: finishBarrier);   // the builder is still at the road's end
                CityDecor.AddLoopRims(rootGo.transform, b);
                CityDecor.AddPodiumFoundation(rootGo.transform, b, city);
            }
            else if (theme == "Seasons")
            {
                props = SeasonDecor.AddFloatingIslands(rootGo.transform, b);
                SeasonDecor.AddPodiumIsland(rootGo.transform, b, podiumPos);
                SeasonDecor.AddSeasonLoopRims(rootGo.transform, b);
            }
            if (tunnelArt == "Neon") SeasonDecor.AddTunnelArt(rootGo.transform, b, SeasonDecor.Neon);
            else if (tunnelArt == "Seasons") SeasonDecor.AddTunnelArt(rootGo.transform, b);

            EditorSceneManager.SaveScene(scene, scenePath);
            T.WriteMapSettings(folder);
            b.FinishChecks(folder, interactive);
            AssetDatabase.Refresh();
            Selection.activeGameObject = rootGo;
            Debug.Log($"[Slip & Skid] {name} created from {Path.GetFileName(jsonPath)}: {scenePath} ({b.TileCount} road tiles, " +
                      $"{props} scenery props, {b.LoopCount} loop(s), about {Mathf.RoundToInt(b.Distance)} m long).");
        }

        // ------------------------------------------------------------------ the course

        private static readonly string[] Actions =
        {
            "section", "straight", "tiles", "surface", "season", "boost", "boostRun", "loop", "barrelRoll", "jump", "gap",
            "reachHeight", "fork", "finish",
        };

        private static readonly string[] PieceOptions = { "width", "length", "bank", "pipe", "pipeHeight", "pillars", "push", "tunnel" };

        // Before anything is built: one finish, as the last step (TrackBuilder.Finish puts the gate 15 m back).
        private static void CheckCourse(List<object> course)
        {
            if (course.Count == 0) throw new MapJson.Error("course: empty");
            for (int i = 0; i < course.Count; i++)
            {
                bool finish = course[i] is MapJson.Obj o && o.ContainsKey("finish");
                if (finish && i != course.Count - 1) throw new MapJson.Error($"course[{i}]: \"finish\" must be the last step");
                if (!finish && i == course.Count - 1) throw new MapJson.Error("course: the last step must be { \"finish\": true }");
            }
        }

        private static void Run(TrackBuilder b, List<object> steps, string path)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                string at = $"{path}[{i}]";
                if (!(steps[i] is MapJson.Obj o)) throw new MapJson.Error($"{at}: a step must be an object {{ ... }}");
                Step(b, new Node(o, at), at);
            }
        }

        private static void Step(TrackBuilder b, Node s, string at)
        {
            string[] found = Actions.Where(s.Has).ToArray();
            if (found.Length != 1)
            {
                throw new MapJson.Error(found.Length == 0
                    ? $"{at}: no action; a step is one of {string.Join(", ", Actions)}"
                    : $"{at}: one action per step, found {string.Join(" and ", found)}");
            }
            switch (found[0])
            {
                case "section":
                    b.Section(s.Str("section", ""));
                    break;
                case "surface":
                    b.Surface = s.Str("surface", "");
                    break;
                case "season":
                    b.Season = s.Str("season", "");
                    break;
                case "straight":
                case "tiles":
                    Pieces(b, s, found[0]);
                    break;
                case "boost":
                    if (s.Bool("boost", true)) b.Boost();
                    break;
                case "boostRun":
                    b.BoostRun(s.Int("boostRun", 3, 1, 20));
                    break;
                case "loop":
                {
                    Node l = s.Obj("loop", required: false, allowTrue: true);
                    int tiles = l?.Int("tiles", 32, 8, 80) ?? 32;
                    float tileLength = l?.Float("tileLength", 4f) ?? 4f;
                    float width = l?.Float("width", TrackBuilder.DefaultWidth) ?? TrackBuilder.DefaultWidth;
                    l?.Done();
                    b.Loop(tiles, tileLength, width);
                    break;
                }
                case "barrelRoll":
                {
                    Node r = s.Obj("barrelRoll", required: false, allowTrue: true);
                    float length = r?.Float("length", 120f) ?? 120f;
                    float radius = r?.Float("radius", 9f) ?? 9f;
                    bool clockwise = r?.Bool("clockwise", true) ?? true;
                    r?.Done();
                    b.BarrelRoll(length, radius, clockwise);
                    break;
                }
                case "jump":
                    Jump(b, s.Obj("jump", required: true));
                    break;
                case "gap":
                {
                    Node g = s.Obj("gap", required: true);
                    float forward = g.Float("forward", float.NaN);
                    float drop = g.Float("drop", 0f);
                    if (float.IsNaN(forward)) throw g.Missing("forward");
                    g.Done();
                    b.Gap(forward, drop);
                    break;
                }
                case "reachHeight":
                    b.ReachHeight(s.Float("reachHeight", 0f), s.Bool("pillars", false));
                    break;
                case "fork":
                {
                    Node f = s.Obj("fork", required: true);
                    float spread = f.Float("spread", 9f);
                    List<object> left = f.List("left", required: true), right = f.List("right", required: true);
                    f.Done();
                    foreach (var (lane, side) in new[] { (left, "left"), (right, "right") })
                    {
                        if (lane.Count == 0) throw new MapJson.Error($"{at}.fork.{side}: empty lane");
                        if (lane.Any(x => x is MapJson.Obj o && o.ContainsKey("finish")))
                            throw new MapJson.Error($"{at}.fork.{side}: no finish inside a fork");
                    }
                    b.Fork(spread, () => Run(b, left, at + ".fork.left"), () => Run(b, right, at + ".fork.right"));
                    break;
                }
                case "finish":
                    if (!s.Bool("finish", true)) throw s.Bad("finish", "must be true");
                    b.Finish();
                    break;
            }
            s.Done();
        }

        // { "straight": n } or { "tiles": n, "turn": total °, "slope": total ° } + piece options. turn > 0 = right;
        // slope > 0 = the road tilts up by that much over the pieces (TrackBuilder's pitch per piece, sign flipped).
        private static void Pieces(TrackBuilder b, Node s, string action)
        {
            int count = s.Int(action, 1, 1, 200);
            float turn = 0f, slope = 0f;
            if (action == "tiles")
            {
                turn = s.Float("turn", 0f);
                slope = s.Float("slope", 0f);
            }
            else if (s.Has("turn") || s.Has("slope"))
            {
                throw new MapJson.Error($"{s.Path}: \"straight\" has no turn or slope; use \"tiles\"");
            }
            float width = s.Float("width", TrackBuilder.DefaultWidth);
            float length = s.Float("length", TrackBuilder.DefaultLength);
            float bank = s.Float("bank", 0f);
            var pipe = (TrackBuilder.Pipe)Enum.Parse(typeof(TrackBuilder.Pipe),
                s.Choice("pipe", "None", "None", "Left", "Right", "Both", "Outer"));
            float pipeHeight = s.Float("pipeHeight", 3f);
            bool pillars = s.Bool("pillars", false), push = s.Bool("push", false), tunnel = s.Bool("tunnel", false);
            b.Tiles(count, yaw: turn / count, pitch: slope == 0f ? 0f : -slope / count, width: width, pillars: pillars, length: length,
                push: push, tunnel: tunnel, bank: bank, pipe: pipe, pipeHeight: pipeHeight);
        }

        // Kicker + gap + landing, shaped by the map rules for jumps: a kicker of `kickerTiles` pieces of
        // `kickerLength` m tilting up by `kicker`°, the gap, then a `landingWidth` m landing tilted down by `landing`°,
        // `landingFlat` flat pieces, and levelled again.
        private static void Jump(TrackBuilder b, Node j)
        {
            float gap = j.Float("gap", float.NaN), drop = j.Float("drop", float.NaN);
            if (float.IsNaN(gap)) throw j.Missing("gap");
            if (float.IsNaN(drop)) throw j.Missing("drop");
            float kicker = j.Float("kicker", 12f);
            int kickerTiles = j.Int("kickerTiles", 2, 1, 6);
            float kickerLength = j.Float("kickerLength", 6f);
            float landing = j.Float("landing", 12f);
            int landingFlat = j.Int("landingFlat", 2, 0, 10);
            float landingWidth = j.Float("landingWidth", TrackBuilder.WideWidth);
            float landingLength = j.Float("landingLength", 12f);
            j.Done();
            b.Tiles(kickerTiles, pitch: -kicker / kickerTiles, length: kickerLength);
            b.Gap(forward: gap, drop: drop);
            b.Tiles(1, pitch: landing, width: landingWidth, length: landingLength);
            if (landingFlat > 0) b.Tiles(landingFlat, width: landingWidth, length: landingLength);
            b.Tiles(1, pitch: -landing, width: landingWidth, length: landingLength);
        }

        // ------------------------------------------------------------------ reading with clear errors

        private sealed class Node
        {
            private readonly MapJson.Obj _o;
            private readonly HashSet<string> _used = new HashSet<string>();
            public readonly string Path;

            public Node(MapJson.Obj o, string path)
            {
                _o = o;
                Path = path;
            }

            private string At(string key) => Path == "" ? key : $"{Path}.{key}";

            public bool Has(string key) => _o.ContainsKey(key);

            public MapJson.Error Missing(string key) => new MapJson.Error($"{At(key)}: missing");

            public MapJson.Error Bad(string key, string why) => new MapJson.Error($"{At(key)}: {why}");

            private bool Get(string key, out object v)
            {
                _used.Add(key);
                return _o.TryGetValue(key, out v);
            }

            public string Str(string key, string def)
            {
                if (!Get(key, out object v)) return def;
                return v as string ?? throw Bad(key, "must be a \"string\"");
            }

            public float Float(string key, float def)
            {
                if (!Get(key, out object v)) return def;
                return v is double d ? (float)d : throw Bad(key, "must be a number");
            }

            public int Int(string key, int def, int min, int max)
            {
                if (!Get(key, out object v)) return def;
                if (!(v is double d) || d != Math.Floor(d)) throw Bad(key, "must be a whole number");
                if (d < min || d > max) throw Bad(key, $"{d} is out of range ({min} to {max})");
                return (int)d;
            }

            public bool Bool(string key, bool def)
            {
                if (!Get(key, out object v)) return def;
                return v is bool x ? x : throw Bad(key, "must be true or false");
            }

            public string Choice(string key, string def, params string[] allowed)
            {
                string s = Str(key, def);
                string match = allowed.FirstOrDefault(a => string.Equals(a, s, StringComparison.OrdinalIgnoreCase));
                return match ?? throw Bad(key, $"\"{s}\" is not one of {string.Join(", ", allowed)}");
            }

            public Vector3 Vec3(string key, Vector3 def)
            {
                if (!Get(key, out object v)) return def;
                if (!(v is List<object> a) || a.Count != 3 || !a.All(x => x is double)) throw Bad(key, "must be [x, y, z]");
                return new Vector3((float)(double)a[0], (float)(double)a[1], (float)(double)a[2]);
            }

            public List<object> List(string key, bool required)
            {
                if (!Get(key, out object v)) return required ? throw Missing(key) : null;
                return v as List<object> ?? throw Bad(key, "must be a list [ ... ]");
            }

            // allowTrue: { "loop": true } means "all defaults" (returns null).
            public Node Obj(string key, bool required, bool allowTrue = false)
            {
                if (!Get(key, out object v)) return required ? throw Missing(key) : null;
                if (allowTrue && v is bool t)
                {
                    if (!t) throw Bad(key, "use true or { ... }");
                    return null;
                }
                return v is MapJson.Obj o ? new Node(o, At(key)) : throw Bad(key, allowTrue ? "must be true or { ... }" : "must be { ... }");
            }

            // Any key not read is a typo or an option that doesn't apply here: say so rather than ignore it. Keys
            // starting with $ are for people and editors ("$schema", "$comment") and allowed anywhere.
            public void Done()
            {
                string unknown = _o.Order.FirstOrDefault(k => !_used.Contains(k) && !k.StartsWith("$"));
                if (unknown == null) return;
                throw new MapJson.Error($"{At(unknown)}: unknown key here");
            }
        }
    }
}
