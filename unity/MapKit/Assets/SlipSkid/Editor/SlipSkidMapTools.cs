using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SlipSkid.MapKit.SDK;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlipSkid.MapKit.Editor
{
    // Menu: "Slip & Skid" in the Unity menu bar.
    public static class SlipSkidMapTools
    {
        private const string BepInExPathPref = "SlipSkid.MapKit.BepInExPath";
        internal const string MapsFolder = "Assets/Maps";
        internal const string ManifestName = "mapkit.json";
        private const long LzmaAbove = 9_000_000;   // bytes: bundles heavier in LZ4 are built in LZMA
        private const string LoaderDll = "SlipSkid.MapKit.Loader.dll";

        // The game's layer for the collision cars physically drive on (stock levels: one combined mesh).
        internal const int RoadCombinedLayer = 16;

        // Stock road pieces: 8 m of asphalt between two 1 m concrete borders.
        private const float RoadWidth = 10f;
        private const float TileLength = 10f;

        // ---------------------------------------------------------------- create

        [MenuItem("Slip & Skid/1. Create Starter Track", priority = 1)]
        public static void CreateStarterTrack()
        {
            string mapName = EditorInputDialog.Show("New map", "Map name (letters and numbers):", "MyFirstMap");
            if (string.IsNullOrWhiteSpace(mapName)) return;
            mapName = Sanitize(mapName);

            string folder = $"{MapsFolder}/{mapName}";
            string scenePath = $"{folder}/{mapName}.unity";
            if (File.Exists(scenePath) &&
                !EditorUtility.DisplayDialog("Map exists", $"{scenePath} already exists. Overwrite it?", "Overwrite", "Cancel"))
            {
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(folder);
            URPSetup.Ensure();

            // No camera or lights: the host level brings its own.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material asphalt = GetOrCreateMaterial($"{folder}/AsphaltPreview.mat", new Color(0.22f, 0.22f, 0.25f));
            Material concrete = GetOrCreateMaterial($"{folder}/ConcretePreview.mat", new Color(0.62f, 0.62f, 0.6f));

            var rootGo = new GameObject(mapName);
            KitMap root = rootGo.AddComponent<KitMap>();
            root.mapName = ObjectNames.NicifyVariableName(mapName);
            root.difficulty = 2;

            Transform track = Child(rootGo.transform, "Track").transform;

            // The track is a list of steps: (turn per tile in degrees, slope per tile in degrees, tile count).
            var steps = new (float yaw, float pitch, int count)[]
            {
                (0, 0, 6),      // start straight
                (15, 0, 6),     // 90 deg right
                (0, 0, 3),
                (0, -5, 3),     // ramp up
                (0, 5, 3),      // level out on a raised section
                (0, 0, 2),
                (-15, 0, 6),    // 90 deg left
                (0, 0, 3),
                (-15, 0, 6),    // 90 deg left
                (0, 0, 6),      // finish straight
            };

            var pos = Vector3.zero;
            var rot = Quaternion.identity;
            int tileIndex = 0;
            var path = new List<(Vector3 p, Vector3 right)>();
            foreach (var (yaw, pitch, count) in steps)
            {
                for (int i = 0; i < count; i++)
                {
                    // Pivot the turn on the tile centre and overlap a bit so corners stay closed.
                    rot = rot * Quaternion.Euler(pitch, yaw, 0f);
                    Vector3 center = pos + rot * Vector3.forward * (TileLength * 0.5f);
                    float length = Mathf.Abs(yaw) > 0.01f ? TileLength * 1.35f : TileLength + 0.2f;

                    GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"Road_{tileIndex:000}";
                    tile.transform.SetParent(track, false);
                    tile.transform.SetPositionAndRotation(center + rot * Vector3.down * 0.5f, rot);
                    tile.transform.localScale = new Vector3(RoadWidth, 1f, length);
                    tile.GetComponent<MeshRenderer>().sharedMaterial = asphalt;
                    AddRoad(tile, KitRoadKind.Road);

                    foreach (int side in new[] { -1, 1 })
                    {
                        GameObject border = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        border.name = side < 0 ? "BorderL" : "BorderR";
                        border.transform.SetParent(track, false);
                        border.transform.SetPositionAndRotation(center + rot * new Vector3(side * (RoadWidth * 0.5f + 0.5f), -0.5f, 0f), rot);
                        border.transform.localScale = new Vector3(1f, 1f, length);
                        border.GetComponent<MeshRenderer>().sharedMaterial = concrete;
                        AddRoad(border, KitRoadKind.Border);
                    }

                    path.Add((center, rot * Vector3.right));
                    pos += rot * Vector3.forward * TileLength;
                    tileIndex++;
                }
            }

            AddStartGrid(rootGo.transform, StartRowZ);
            Quaternion endRot = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
            Vector3 finishPos = pos - endRot * Vector3.forward * (TileLength * 1.5f);
            Marker<KitFinish>(rootGo.transform, "Finish", finishPos + Vector3.up * FinishLineHeight, endRot);
            AddPodium(rootGo.transform, new Vector3(0f, 0f, -60f), Quaternion.Euler(0, 180, 0), concrete);
            AddDecorAlong(rootGo.transform, path, concrete, asphalt);

            EditorSceneManager.SaveScene(scene, scenePath);
            WriteMapSettings(folder);
            AssetDatabase.Refresh();
            Selection.activeGameObject = rootGo;
            Debug.Log($"[Slip & Skid] Starter track created: {scenePath}. Edit it, then use 'Slip & Skid > 2. Build and Install Map'.");
        }

        internal static KitRoad AddRoad(GameObject go, KitRoadKind kind)
        {
            KitRoad road = go.AddComponent<KitRoad>();
            road.kind = kind;
            road.tileGuid = Guid.NewGuid().ToString();
            return road;
        }

        // ---------------------------------------------------------------- build

        [MenuItem("Slip & Skid/2. Build and Install Map", priority = 3)]
        public static void BuildAndInstallMenu() => BuildAndInstall(interactive: true);

        public static void BuildAndInstall(bool interactive)
        {
            URPSetup.Ensure();
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Fail(interactive, "Save the scene first (it must live in Assets/Maps/<Name>/).");
                return;
            }
            KitMap root = UnityEngine.Object.FindObjectOfType<KitMap>();
            if (root == null)
            {
                Fail(interactive, "This scene has no KitMap. Use 'Create Starter Track' or 'Create Big Stunt Track'.");
                return;
            }
            string problem = Validate(root);
            if (problem != null) Debug.LogWarning("[Slip & Skid] Map check: " + problem.Replace("\n", " "));
            if (problem != null && !interactive)
            {
                // Batch builds (tools/build_map.py) never ship a map that fails the check.
                Fail(false, "Map check failed: " + problem.Replace("\n", " "));
                return;
            }
            if (problem != null && interactive && !EditorUtility.DisplayDialog("Map check", problem + "\n\nBuild anyway?", "Build", "Cancel"))
            {
                return;
            }
            EnsureTileGuids(root);
            int collisions = EnsureRoadCombinedCollision(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            string mapId = scene.name;
            string folder = Path.GetDirectoryName(scene.path).Replace('\\', '/');
            string bundleName = mapId.ToLowerInvariant() + ".bundle";
            string outDir = Path.GetFullPath(Path.Combine("Builds", mapId));
            string tmpDir = Path.GetFullPath(Path.Combine("Temp", "SlipSkidBundles"));
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(tmpDir);

            // Level-select images and 3D model, drawn from the track itself.
            LevelSelectAssets.Export(root, outDir);
            var info = new MapInfo
            {
                Id = mapId,
                Name = root.mapName,
                Author = root.author,
                Host = root.hostLevel,
                Difficulty = root.difficulty,
                Reversible = root.isReversible,
            };

            // The map ships as a prefab: the game's stripped engine can load prefabs from a bundle but the
            // loader has to instantiate them into the host level synchronously.
            string prefabPath = $"{folder}/{mapId}.prefab";
            // Stock piece previews (the game's meshes, StockPieceLibrary) never ship: save a copy without them.
            GameObject shipped = root.gameObject;
            if (root.GetComponentInChildren<KitStockPiece>(true) != null)
            {
                shipped = UnityEngine.Object.Instantiate(root.gameObject);
                shipped.name = root.gameObject.name;
                foreach (KitStockPiece m in shipped.GetComponentsInChildren<KitStockPiece>(true))
                {
                    Transform preview = m.transform.Find(StockPieceLibrary.PreviewName);
                    if (preview != null) UnityEngine.Object.DestroyImmediate(preview.gameObject);
                }
            }
            PrefabUtility.SaveAsPrefabAsset(shipped, prefabPath);
            if (shipped != root.gameObject) UnityEngine.Object.DestroyImmediate(shipped);
            var build = new AssetBundleBuild { assetBundleName = bundleName, assetNames = new[] { prefabPath } };
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                tmpDir, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null)
            {
                Fail(interactive, "Bundle build failed, see the Console.");
                return;
            }
            // LZ4 (chunk-based) loads fastest, but a heavy map's LZ4 bundle can top the 10 MB a browser upload takes
            // (Desert Storm's 390k decor vertices: 14.5 MB, 6.6 MB in LZMA). Those are built again in LZMA, which the game
            // loads too (decompressed into memory at load).
            if (new FileInfo(Path.Combine(tmpDir, bundleName)).Length > LzmaAbove)
            {
                manifest = BuildPipeline.BuildAssetBundles(
                    tmpDir, new[] { build }, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
                if (manifest == null)
                {
                    Fail(interactive, "Bundle build (LZMA) failed, see the Console.");
                    return;
                }
                Debug.Log($"[Slip & Skid] Bundle over {LzmaAbove / 1000000} MB in LZ4: built in LZMA ({new FileInfo(Path.Combine(tmpDir, bundleName)).Length / 1e6:0.0} MB).");
            }
            File.Copy(Path.Combine(tmpDir, bundleName), Path.Combine(outDir, bundleName), true);
            // The road's centre line, written by the generators (TrackBuilder.WriteTrackJson), for the automated tests.
            string track = Path.Combine(folder, "track.json");
            if (File.Exists(track)) File.Copy(track, Path.Combine(outDir, "track.json"), true);

            foreach (string img in new[] { "thumbnail.png", "preview.png" })
            {
                // Your own images in the map folder win over the generated ones.
                string src = Path.Combine(folder, img);
                if (File.Exists(src)) File.Copy(src, Path.Combine(outDir, img), true);
            }
            string oldManifest = Path.Combine(outDir, "map.json");
            if (File.Exists(oldManifest)) File.Delete(oldManifest);
            File.WriteAllText(Path.Combine(outDir, ManifestName), ManifestJson(info, bundleName, folder, outDir));

            var installedTo = new List<string>();
            foreach (string bepinex in GetInstallTargets())
            {
                string dir = Path.Combine(bepinex, "CustomMaps", mapId);
                Directory.CreateDirectory(dir);
                // An older build in the Coconut Mall loader's format would load twice.
                string stale = Path.Combine(dir, "map.json");
                if (File.Exists(stale)) File.Delete(stale);
                foreach (string f in Directory.GetFiles(outDir))
                {
                    File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
                }
                installedTo.Add(dir);
            }

            string msg = $"Built to:\n{outDir}\n({collisions} collision piece(s) added)";
            msg += installedTo.Count > 0
                ? $"\n\nInstalled to:\n{string.Join("\n", installedTo)}\n\nRestart the game and pick '{info.Name}' in level select."
                : Environment.GetEnvironmentVariable("SLIPSKID_INSTALL_TARGETS") != null
                    ? "\n\nNot installed (build_map.py --no-install / --install-to)."
                    : $"\n\nNot installed: no Thunderstore profile has {LoaderDll}. Set one with 'Slip & Skid > Set Thunderstore Profile Folder'.";
            Debug.Log("[Slip & Skid] " + msg.Replace("\n", " "));
            if (!interactive) return;
            EditorUtility.DisplayDialog("Slip & Skid", msg, "OK");
            EditorUtility.RevealInFinder(Path.Combine(outDir, ManifestName));
        }

        private sealed class MapInfo
        {
            public string Id, Name, Author, Host;
            public int Difficulty;
            public bool Reversible;
        }

        private static void Fail(bool interactive, string message)
        {
            if (interactive) EditorUtility.DisplayDialog("Slip & Skid", message, "OK");
            else throw new Exception("[Slip & Skid] " + message);
        }

        // Every solid road, border or wall gets a copy of its collider on the RoadCombined layer, the one cars
        // physically collide with. The game cannot do this at load time: its stripped engine has no way to give
        // a MeshCollider a mesh from code.
        internal static int EnsureRoadCombinedCollision(KitMap root)
        {
            int added = 0;
            foreach (KitRoad road in root.GetComponentsInChildren<KitRoad>(true))
            {
                if (road.kind != KitRoadKind.Road && road.kind != KitRoadKind.Border) continue;
                Collider source = road.GetComponent<Collider>();
                if (source == null || source.isTrigger) continue;
                Transform existing = road.transform.Find("RoadCombined");
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

                var go = new GameObject("RoadCombined") { layer = RoadCombinedLayer };
                go.transform.SetParent(road.transform, false);
                switch (source)
                {
                    case MeshCollider mc:
                        go.AddComponent<MeshCollider>().sharedMesh = mc.sharedMesh;
                        break;
                    case BoxCollider bc:
                        var box = go.AddComponent<BoxCollider>();
                        box.center = bc.center;
                        box.size = bc.size;
                        break;
                    default:
                        UnityEngine.Object.DestroyImmediate(go);
                        Debug.LogWarning($"[Slip & Skid] '{road.name}': only Mesh and Box colliders get road collision.");
                        continue;
                }
                added++;
            }
            return added;
        }

        // Objects still on Unity's built-in Default-Material show magenta under URP. The game replaces
        // road and border materials anyway, so these are editor-only preview colours.
        [MenuItem("Slip & Skid/Fix Pink Preview Materials", priority = 10)]
        public static void FixPreviewMaterials()
        {
            Scene scene = SceneManager.GetActiveScene();
            string folder = string.IsNullOrEmpty(scene.path) ? MapsFolder : Path.GetDirectoryName(scene.path).Replace('\\', '/');
            Material asphalt = GetOrCreateMaterial($"{folder}/AsphaltPreview.mat", new Color(0.22f, 0.22f, 0.25f));
            Material concrete = GetOrCreateMaterial($"{folder}/ConcretePreview.mat", new Color(0.62f, 0.62f, 0.6f));
            int fixedCount = 0;
            foreach (MeshRenderer r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true))
            {
                Material m = r.sharedMaterial;
                bool broken = m == null || m.shader == null || !m.shader.isSupported || m.shader.name == "Standard" || m.shader.name == "Hidden/InternalErrorShader";
                if (!broken) continue;
                KitRoad road = r.GetComponent<KitRoad>();
                Undo.RecordObject(r, "Fix preview material");
                r.sharedMaterial = road != null && road.kind == KitRoadKind.Road ? asphalt : concrete;
                fixedCount++;
            }
            if (fixedCount > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Slip & Skid] Fixed {fixedCount} pink object(s). Editor preview only; the game paints roads and walls itself.");
        }

        [MenuItem("Slip & Skid/Set Thunderstore Profile Folder...", priority = 20)]
        public static void SetProfileFolder()
        {
            string picked = EditorUtility.OpenFolderPanel("Pick the profile's BepInEx folder", DefaultProfilesDir(), "BepInEx");
            if (string.IsNullOrEmpty(picked)) return;
            if (!picked.EndsWith("BepInEx", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(picked, "BepInEx")))
            {
                picked = Path.Combine(picked, "BepInEx");
            }
            EditorPrefs.SetString(BepInExPathPref, picked);
            Debug.Log("[Slip & Skid] Maps will be installed to " + Path.Combine(picked, "CustomMaps"));
        }

        // ---------------------------------------------------------------- add markers

        [MenuItem("GameObject/Slip & Skid/Road Tile", false, 10)]
        private static void AddRoadTile(MenuCommand cmd)
        {
            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = "Road";
            tile.transform.localScale = new Vector3(RoadWidth, 1f, TileLength);
            AddRoad(tile, KitRoadKind.Road);
            Place(tile, cmd);
        }

        [MenuItem("GameObject/Slip & Skid/Boost Pad", false, 11)]
        private static void AddBoostPad(MenuCommand cmd)
        {
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "BoostPad";
            pad.transform.localScale = new Vector3(8f, 0.1f, 5f);
            pad.GetComponent<BoxCollider>().isTrigger = true;
            pad.AddComponent<KitBoostPad>();
            Place(pad, cmd);
        }

        private static void Place(GameObject go, MenuCommand cmd)
        {
            GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
            if (cmd.context == null)
            {
                KitMap root = UnityEngine.Object.FindObjectOfType<KitMap>();
                if (root != null) go.transform.SetParent(root.transform, true);
                if (SceneView.lastActiveSceneView != null) go.transform.position = SceneView.lastActiveSceneView.pivot;
            }
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        // ---------------------------------------------------------------- gizmos

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawFinish(KitFinish f, GizmoType type)
        {
            // The marker sits 1.5 m above the road; draw the gate from the road up.
            Gizmos.matrix = Matrix4x4.TRS(f.transform.position + Vector3.down * FinishLineHeight, f.transform.rotation, Vector3.one);
            Gizmos.color = new Color(1f, 1f, 1f, 0.45f);
            Gizmos.DrawCube(new Vector3(0, 3f, 0), new Vector3(RoadWidth + 2f, 6f, 0.2f));
            Gizmos.matrix = Matrix4x4.identity;
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawStart(KitStart s, GizmoType type)
        {
            Gizmos.color = new Color(0f, 1f, 0.3f, 0.8f);
            Gizmos.DrawWireSphere(s.transform.position, 0.6f);
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawSpawns(KitSpawns s, GizmoType type)
        {
            Gizmos.color = Color.cyan;
            foreach (Transform t in s.Resolve())
            {
                if (t == null) continue;
                Gizmos.DrawWireCube(t.position, new Vector3(1.6f, 1f, 2.6f));
                Gizmos.DrawLine(t.position, t.position + t.forward * 3f);
            }
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawDecor(KitDecor d, GizmoType type)
        {
            Gizmos.color = new Color(0.5f, 0.9f, 0.5f, 0.5f);
            Gizmos.DrawWireCube(d.transform.position + Vector3.up * d.size * 0.25f, new Vector3(d.size, d.size * 0.5f, d.size));
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawGravity(KitGravitySphere g, GizmoType type)
        {
            Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireSphere(g.transform.position, g.outerRadius);
        }

        // ---------------------------------------------------------------- helpers

        private static string Validate(KitMap root)
        {
            var issues = new List<string>();
            if (!root.GetComponentsInChildren<KitRoad>(true).Any(r => r.kind == KitRoadKind.Road) &&
                root.GetComponentInChildren<KitStockPiece>(true) == null) issues.Add("- No Road pieces.");
            if (root.GetComponentInChildren<KitStart>(true) == null) issues.Add("- No Start marker.");
            if (root.GetComponentInChildren<KitFinish>(true) == null) issues.Add("- No Finish marker.");
            if (root.GetComponentInChildren<KitPodium>(true) == null) issues.Add("- No Podium marker.");
            KitSpawns sp = root.GetComponentInChildren<KitSpawns>(true);
            if (sp == null || sp.Resolve().Count == 0) issues.Add("- No Spawn Points.");
            else if (sp.Resolve().Count < 4) issues.Add($"- Only {sp.Resolve().Count} spawn points (4 recommended, like stock levels).");
            else if (SyncPhysics() && !Physics.Raycast(sp.Resolve()[0].position + Vector3.up * 5f, Vector3.down, 200f)) issues.Add("- First spawn point is not above the road.");
            return issues.Count == 0 ? null : string.Join("\n", issues);
        }

        // Freshly created colliders are invisible to raycasts until transforms are synced.
        private static bool SyncPhysics()
        {
            Physics.SyncTransforms();
            return true;
        }

        // Duplicated tiles keep the same GUID, which desyncs surface swaps online.
        private static void EnsureTileGuids(KitMap root)
        {
            var seen = new HashSet<string>();
            foreach (KitRoad t in root.GetComponentsInChildren<KitRoad>(true))
            {
                if (!string.IsNullOrEmpty(t.tileGuid) && seen.Add(t.tileGuid)) continue;
                Undo.RecordObject(t, "Fix tile GUID");
                t.tileGuid = Guid.NewGuid().ToString();
                seen.Add(t.tileGuid);
                EditorUtility.SetDirty(t);
            }
            foreach (KitStockPiece t in root.GetComponentsInChildren<KitStockPiece>(true))
            {
                if (!string.IsNullOrEmpty(t.tileGuid) && seen.Add(t.tileGuid)) continue;
                Undo.RecordObject(t, "Fix tile GUID");
                t.tileGuid = Guid.NewGuid().ToString();
                seen.Add(t.tileGuid);
                EditorUtility.SetDirty(t);
            }
        }

        private static string ManifestJson(MapInfo info, string bundleName, string folder, string outDir)
        {
            var s = MapSettings.Load(folder);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"id\": \"mapkit.{info.Id.ToLowerInvariant()}\",");
            sb.AppendLine($"  \"name\": \"{Esc(info.Name)}\",");
            sb.AppendLine($"  \"author\": \"{Esc(info.Author)}\",");
            sb.AppendLine($"  \"version\": \"{Esc(s.version)}\",");
            sb.AppendLine($"  \"bundle\": \"{bundleName}\",");
            if (!string.IsNullOrEmpty(info.Host)) sb.AppendLine($"  \"host\": \"{Esc(info.Host)}\",");
            if (File.Exists(Path.Combine(outDir, "thumbnail.png"))) sb.AppendLine("  \"thumbnail\": \"thumbnail.png\",");
            if (File.Exists(Path.Combine(outDir, "preview.png"))) sb.AppendLine("  \"preview\": \"preview.png\",");
            if (File.Exists(Path.Combine(outDir, "preview.obj"))) sb.AppendLine("  \"previewModel\": \"preview.obj\",");
            sb.AppendLine($"  \"difficulty\": {Mathf.Clamp(info.Difficulty, 1, 5)},");
            sb.AppendLine($"  \"isReversible\": {(info.Reversible ? "true" : "false")}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        internal static void WriteMapSettings(string folder)
        {
            string path = Path.Combine(folder, "mapsettings.json");
            if (!File.Exists(path)) File.WriteAllText(path, JsonUtility.ToJson(new MapSettings(), true));
        }

        [Serializable]
        private class MapSettings
        {
            public string version = "1.0.0";

            public static MapSettings Load(string folder)
            {
                string path = Path.Combine(folder, "mapsettings.json");
                return File.Exists(path) ? JsonUtility.FromJson<MapSettings>(File.ReadAllText(path)) : new MapSettings();
            }
        }

        // SLIPSKID_INSTALL_TARGETS (set by tools/build_map.py: "none", or BepInEx folders separated by ';') wins;
        // then the folder picked with 'Set Thunderstore Profile Folder'; otherwise every profile that has the loader.
        private static List<string> GetInstallTargets()
        {
            string forced = Environment.GetEnvironmentVariable("SLIPSKID_INSTALL_TARGETS");
            if (forced != null)
            {
                if (forced.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return new List<string>();
                return forced.Split(';').Select(p => p.Trim()).Where(Directory.Exists).ToList();
            }
            string saved = EditorPrefs.GetString(BepInExPathPref, "");
            if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved)) return new List<string> { saved };
            var targets = new List<string>();
            if (!Directory.Exists(DefaultProfilesDir())) return targets;
            foreach (string profile in Directory.GetDirectories(DefaultProfilesDir()))
            {
                string bepinex = Path.Combine(profile, "BepInEx");
                string plugins = Path.Combine(bepinex, "plugins");
                if (Directory.Exists(plugins) && Directory.GetFiles(plugins, LoaderDll, SearchOption.AllDirectories).Length > 0)
                {
                    targets.Add(bepinex);
                }
            }
            return targets;
        }

        private static string DefaultProfilesDir() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Thunderstore Mod Manager", "DataFolder", "SlipSkid", "profiles");

        internal static Material GetOrCreateMaterial(string path, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader")
                ?? Shader.Find("Standard");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                // Made before URP was set up: it would render pink.
                mat.shader = shader;
            }
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Same layout as the game's own levels (measured in levels 6 and 11, relative to the grid):
        // - one row of 4 cars, 2 m apart, 0.65 m above the road (the game rotates players through the spawn
        //   list each round, so a second row would put some players 4 m further back);
        // - the start gate at road height, 6 m behind the row and 5 m to its right, rotated so its forward
        //   points straight up and its up points to the cars' left; its railings run from 6.8 m behind to
        //   4.1 m ahead of the row and the countdown lights hang 4 m ahead.
        // The loader moves the host's gate (and the protected zone around it) onto the Start marker.
        internal static void AddStartGrid(Transform root, float rowZ)
        {
            GameObject spawnsGo = Child(root, "SpawnPoints");
            KitSpawns spawns = spawnsGo.AddComponent<KitSpawns>();
            for (int i = 0; i < 4; i++)
            {
                GameObject p = Child(spawnsGo.transform, $"Spawn_{i + 1}");
                p.transform.position = new Vector3((i - 1.5f) * 2f, 0.65f, rowZ);
                spawns.points.Add(p.transform);
            }
            // Time trials start alone in the middle of the row.
            Child(root, "Spawn_TimeTrial").transform.position = new Vector3(0f, 0.65f, rowZ);
            Marker<KitStart>(root, "Start", new Vector3(5f, 0f, rowZ - 6f), Quaternion.LookRotation(Vector3.up, Vector3.left));
        }

        // Roadside scenery: towers and columns on both sides of the track, rising from well below the road
        // (under the sea the loader puts below the lowest road) to 5-50 m above it. The loader paints them with
        // the host level's concrete and asphalt. Each is kept clear of every part of the track. (The host's own
        // environment pieces use a background shader that doesn't draw up close, so they can't be reused here.)
        internal static int AddDecorAlong(Transform root, List<(Vector3 p, Vector3 right)> path, Material concrete,
                                          Material dark, float spacing = 45f, int seed = 7)
        {
            if (path.Count == 0) return 0;
            Transform parent = Child(root, "Scenery").transform;
            var rng = new System.Random(seed);
            var placed = new List<(Vector3 c, float r)>();
            float travelled = 0f;
            Vector3 last = path[0].p;
            float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            foreach (var (p, right) in path)
            {
                travelled += Vector3.Distance(last, p);
                last = p;
                if (travelled < spacing) continue;
                travelled = 0f;
                foreach (int side in new[] { -1, 1 })
                {
                    if (rng.NextDouble() < 0.25) continue;   // leave gaps so it doesn't read as a wall
                    bool column = rng.NextDouble() < 0.3;
                    float width = column ? R(6f, 12f) : R(10f, 26f);
                    float depth = column ? width : R(10f, 26f);
                    float above = R(5f, 50f);
                    float radius = Mathf.Sqrt(width * width + depth * depth) * 0.5f;
                    float offset = radius + R(14f, 45f);
                    Vector3 pos = p + right.normalized * side * offset;
                    if (!Clear(pos, radius + 8f, path) || placed.Any(d => Flat(d.c - pos).magnitude < d.r + radius + 4f)) continue;

                    float bottom = p.y - 90f;   // always below the sea
                    float height = p.y + above - bottom;
                    GameObject prop = GameObject.CreatePrimitive(column ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                    prop.name = column ? $"Column_{placed.Count:000}" : $"Tower_{placed.Count:000}";
                    prop.transform.SetParent(parent, false);
                    prop.transform.position = new Vector3(pos.x, bottom + height * 0.5f, pos.z);
                    prop.transform.rotation = Quaternion.Euler(0f, R(0f, 90f), 0f);
                    // A unit cylinder is 2 high, a unit cube 1.
                    prop.transform.localScale = new Vector3(width, column ? height * 0.5f : height, depth);
                    UnityEngine.Object.DestroyImmediate(prop.GetComponent<Collider>());
                    bool isDark = rng.NextDouble() < 0.5;
                    prop.GetComponent<MeshRenderer>().sharedMaterial = isDark ? dark : concrete;
                    AddRoad(prop, isDark ? KitRoadKind.Dark : KitRoadKind.Concrete);
                    placed.Add((pos, radius));
                }
            }
            return placed.Count;
        }

        private static bool Clear(Vector3 pos, float radius, List<(Vector3 p, Vector3 right)> path)
        {
            foreach (var (p, _) in path)
            {
                if (Flat(p - pos).magnitude < radius) return false;
            }
            return true;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // The end-of-race podium (the host's rank stands and winners' camera) on a concrete stage, so the
        // final shot isn't stands floating in the void. Stock podium stands sit 1 m below the marker.
        internal static void AddPodium(Transform root, Vector3 pos, Quaternion rot, Material concrete)
        {
            Marker<KitPodium>(root, "Podium", pos, rot);
            GameObject stage = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stage.name = "PodiumStage";
            stage.transform.SetParent(root, false);
            stage.transform.SetPositionAndRotation(pos + Vector3.down * (1.3f + 1f), rot);
            stage.transform.localScale = new Vector3(36f, 2f, 36f);
            stage.GetComponent<MeshRenderer>().sharedMaterial = concrete;
            AddRoad(stage, KitRoadKind.Concrete);
        }

        // Puts the grid so the start railings (6.8 m behind the row) begin just after the road does.
        internal const float StartRowZ = 8f;

        // The game's finish gate object sits 1.5 m above the road; its gate and floor line hang below it.
        internal const float FinishLineHeight = 1.5f;

        internal static Material GetOrCreateBoostMaterial(string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Shader shader = Shader.Find("SlipSkid/BoostGlow");
            if (shader == null) return GetOrCreateMaterial(path, new Color(1f, 0.85f, 0.1f));
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        internal static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        internal static T Marker<T>(Transform parent, string name, Vector3 pos, Quaternion rot) where T : Component
        {
            GameObject go = Child(parent, name);
            go.transform.SetPositionAndRotation(pos, rot);
            return go.AddComponent<T>();
        }

        internal static string Sanitize(string s)
        {
            string clean = new string(s.Where(char.IsLetterOrDigit).ToArray());
            return string.IsNullOrEmpty(clean) ? "MyMap" : clean;
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // Unity has no built-in text prompt.
    internal class EditorInputDialog : EditorWindow
    {
        private string _label, _value;
        private bool _ok, _focused;

        public static string Show(string title, string label, string value)
        {
            var w = CreateInstance<EditorInputDialog>();
            w.titleContent = new GUIContent(title);
            w._label = label;
            w._value = value;
            w.minSize = w.maxSize = new Vector2(340, 90);
            w.ShowModal();
            return w._ok ? w._value : null;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(_label);
            GUI.SetNextControlName("input");
            _value = EditorGUILayout.TextField(_value);
            if (!_focused) { EditorGUI.FocusTextInControl("input"); _focused = true; }
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(80))) Close();
                if (GUILayout.Button("OK", GUILayout.Width(80)) || enter) { _ok = true; Close(); }
            }
        }
    }
}
