using System.Collections.Generic;
using System.IO;
using SlipSkid.MapKit.SDK;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using T = SlipSkid.MapKit.Editor.SlipSkidMapTools;

namespace SlipSkid.MapKit.Editor
{
    // Faces of two differently painted pieces that lie in the same plane and overlap flicker in game – z-fighting: the
    // depth buffer can't tell which one is in front, so it changes from pixel to pixel and frame to frame (Rush Hour's
    // level-crossing signs were a white and a red square on top of each other, 2026-10-26). TrackBuilder.FinishChecks
    // runs this over the whole map (road and decor); batch builds fail on any. Fix: move one face a few centimetres
    // (5 cm is enough at any distance the game draws), or make the two pieces one colour.
    public static class DecorCheck
    {
        private const float PlaneTolerance = 0.005f;   // metres between the two planes
        private const float NormalTolerance = 0.9995f; // cos of ~1.8°
        private const float MinOverlap = 0.0025f;      // m² (5 cm × 5 cm)
        private const float Cell = 2f;                 // metres, the 2D grid inside a plane

        private struct Tri
        {
            public Vector3 A, B, C, N;
            public float D;
            public int Owner;
        }

        // Renderers the game would not draw. Its URP renderer primes depth: an opaque material is coloured only where
        // its own DepthOnly/DepthNormals pass wrote depth, so one of our shaders (SlipSkid/...) in an opaque queue
        // without both passes shows as an empty outline (Glow and BoostGlow did: lasers, neon, lava and boost pads
        // were invisible in the 2026-10-27 release). KitRoads are skipped: the loader gives them a game material.
        public static List<string> Undrawn(Transform root, int max = 25)
        {
            var found = new List<string>();
            foreach (Renderer rd in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rd.GetComponent<KitRoad>() != null) continue;
                foreach (Material m in rd.sharedMaterials)
                {
                    string why = m == null ? "no material"
                        : m.shader == null || m.shader.name.Contains("InternalError") ? "no working shader"
                        : m.name.StartsWith("Default-Material") || m.shader.name == "Standard" ? "Unity's default material (magenta in game)"
                        : m.shader.name.StartsWith("SlipSkid/") && m.renderQueue < 2500 && (m.FindPass("DepthOnly") < 0 || m.FindPass("DepthNormals") < 0)
                            ? $"{m.shader.name} is opaque without DepthOnly/DepthNormals passes"
                        : null;
                    if (why == null) continue;
                    found.Add($"{PathOf(rd.transform, root)}: {why}");
                    if (found.Count >= max) return found;
                }
            }
            return found;
        }

        // "'a' and 'b' overlap in one plane near (x, y, z)" for each pair of pieces (at most `max`). Faces nobody sees
        // are left out: facing straight down (the bases things stand on), under the sea, and between two road pieces
        // (fork lanes meet side by side: their cut faces touch inside the road) or within one road tile and its own
        // borders (a cut-away border's sides lie on the road's cut face, hidden against the road beside it). Pieces
        // under different KitMovers are apart in game (the cars of one lane all wait at its start in the scene).
        public static List<string> CoplanarFaces(Transform root, int max = 25)
        {
            var owners = new List<(string path, string look, bool road, Transform mover)>();
            var tris = new List<Tri>();
            float sea = SeaLevel(root);
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null || IsEditorOnly(mf.transform)) continue;
                // Soft light (GlowSoft) adds to what is behind it and writes no depth: it can't z-fight.
                if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null && mr.sharedMaterial.shader.name == "SlipSkid/GlowSoft") continue;
                int owner = owners.Count;
                var kit = mf.GetComponent<KitRoad>();
                KitMover mover = mf.GetComponentInParent<KitMover>(true);
                owners.Add((PathOf(mf.transform, root), Look(mf.gameObject, mr), kit != null && (kit.kind == KitRoadKind.Road || kit.kind == KitRoadKind.Border),
                            mover != null ? mover.transform : null));
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                Vector3[] v = mesh.vertices;
                int[] t = mesh.triangles;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    float twice = n.magnitude;
                    if (twice < 2f * MinOverlap) continue;
                    n /= twice;
                    if (n.y < -0.9f || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < sea) continue;
                    tris.Add(new Tri { A = a, B = b, C = c, N = n, D = Vector3.Dot(n, a), Owner = owner });
                }
            }

            // Same plane = same rounded normal and plane distance (a neighbouring distance bin is checked too).
            var planes = new Dictionary<(int, int, int, int), List<int>>();
            for (int i = 0; i < tris.Count; i++)
            {
                var key = PlaneKey(tris[i]);
                if (!planes.TryGetValue(key, out List<int> list)) planes[key] = list = new List<int>();
                list.Add(i);
            }

            var found = new List<string>();
            var reported = new HashSet<(int, int)>();
            foreach (var plane in planes)
            {
                var (nx, ny, nz, d) = plane.Key;
                var members = new List<int>(plane.Value);
                if (planes.TryGetValue((nx, ny, nz, d + 1), out List<int> next)) members.AddRange(next);
                if (members.Count < 2) continue;
                // 2D grid in the plane, so a busy plane (every flat road tile) isn't compared all with all.
                Vector3 n0 = tris[members[0]].N;
                Vector3 u = Vector3.Cross(n0, Mathf.Abs(n0.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(n0, u);
                var grid = new Dictionary<(int, int), List<int>>();
                foreach (int i in members)
                {
                    Tri tr = tris[i];
                    Vector2 a = new Vector2(Vector3.Dot(tr.A, u), Vector3.Dot(tr.A, w)), b = new Vector2(Vector3.Dot(tr.B, u), Vector3.Dot(tr.B, w)),
                        c = new Vector2(Vector3.Dot(tr.C, u), Vector3.Dot(tr.C, w));
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / Cell);
                    int y0 = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) / Cell), y1 = Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) / Cell);
                    if ((x1 - x0 + 1) * (y1 - y0 + 1) > 4096) continue;   // a huge face (sea, ground): never decor-thin
                    for (int x = x0; x <= x1; x++)
                        for (int y = y0; y <= y1; y++)
                        {
                            if (!grid.TryGetValue((x, y), out List<int> cell)) grid[(x, y)] = cell = new List<int>();
                            cell.Add(i);
                        }
                }
                var tested = new HashSet<(int, int)>();
                foreach (List<int> cell in grid.Values)
                {
                    for (int p = 0; p < cell.Count; p++)
                        for (int q = p + 1; q < cell.Count; q++)
                        {
                            Tri s = tris[cell[p]], r = tris[cell[q]];
                            if (s.Owner == r.Owner || owners[s.Owner].look == owners[r.Owner].look || (owners[s.Owner].road && owners[r.Owner].road) ||
                                owners[s.Owner].mover != owners[r.Owner].mover || OwnBorder(owners[s.Owner].path, owners[r.Owner].path)) continue;
                            var pair = s.Owner < r.Owner ? (s.Owner, r.Owner) : (r.Owner, s.Owner);
                            if (reported.Contains(pair)) continue;
                            var triPair = cell[p] < cell[q] ? (cell[p], cell[q]) : (cell[q], cell[p]);
                            if (!tested.Add(triPair)) continue;
                            if (Vector3.Dot(s.N, r.N) < NormalTolerance || Mathf.Abs(s.D - Vector3.Dot(s.N, r.A)) > PlaneTolerance) continue;
                            float area = OverlapArea(s, r, u, w);
                            if (area < MinOverlap) continue;
                            reported.Add(pair);
                            Vector3 at = (s.A + s.B + s.C) / 3f;
                            found.Add($"'{owners[pair.Item1].path}' and '{owners[pair.Item2].path}' overlap in one plane near ({at.x:0.0}, {at.y:0.0}, {at.z:0.0}), " +
                                      $"facing ({s.N.x:0.00}, {s.N.y:0.00}, {s.N.z:0.00}), {area * 10000f:0} cm² [{owners[pair.Item1].look} | {owners[pair.Item2].look}]");
                            if (found.Count >= max) return found;
                        }
                }
            }
            return found;
        }

        // The sea the loader puts deathDepth under the lowest road (30 m when 0: the host's own is about that).
        private static float SeaLevel(Transform root)
        {
            float lowest = float.MaxValue;
            foreach (KitRoad r in root.GetComponentsInChildren<KitRoad>(true))
                if (r.kind == KitRoadKind.Road) lowest = Mathf.Min(lowest, r.transform.position.y);
            KitMap map = root.GetComponent<KitMap>();
            float depth = map != null && map.deathDepth > 0f ? map.deathDepth : 30f;
            return lowest == float.MaxValue ? float.NegativeInfinity : lowest - depth;
        }

        // "Road_012" and "Road_012/BorderL", "Decoy_001" and "Decoy_001/BorderR": a tile and its own border or wall.
        private static bool OwnBorder(string a, string b)
        {
            string tile = a.Length < b.Length ? a : b, part = a.Length < b.Length ? b : a;
            string name = tile.Substring(tile.LastIndexOf('/') + 1);
            return part.StartsWith(tile + "/") && part.IndexOf('/', tile.Length + 1) < 0 &&
                   (name.StartsWith("Road_") || name.StartsWith("Decoy_"));
        }

        private static (int, int, int, int) PlaneKey(Tri t) =>
            (Mathf.RoundToInt(t.N.x * 40f), Mathf.RoundToInt(t.N.y * 40f), Mathf.RoundToInt(t.N.z * 40f), Mathf.FloorToInt(t.D / (4f * PlaneTolerance)));

        // What it looks like in game: a KitRoad's kind, paint and tint (the loader paints it), else its material.
        private static string Look(GameObject go, MeshRenderer mr)
        {
            var kit = go.GetComponent<KitRoad>();
            if (kit != null)
            {
                Color c = kit.tint;
                return $"{kit.kind}/{kit.paint}/{kit.look}/{Mathf.RoundToInt(c.r * 50)},{Mathf.RoundToInt(c.g * 50)},{Mathf.RoundToInt(c.b * 50)},{Mathf.RoundToInt(c.a * 50)}";
            }
            return mr.sharedMaterial != null ? mr.sharedMaterial.name : "";
        }

        private static bool IsEditorOnly(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.CompareTag("EditorOnly") || t.name == "StockPreview") return true;
            return false;
        }

        private static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // Area shared by two triangles of one plane (Sutherland–Hodgman: `a` clipped by each edge of `b`).
        private static float OverlapArea(Tri a, Tri b, Vector3 u, Vector3 w)
        {
            Vector2 P(Vector3 p) => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w));
            var poly = new List<Vector2> { P(a.A), P(a.B), P(a.C) };
            Vector2[] clip = { P(b.A), P(b.B), P(b.C) };
            if (Cross(clip[1] - clip[0], clip[2] - clip[0]) < 0f) System.Array.Reverse(clip);
            for (int e = 0; e < 3 && poly.Count > 0; e++)
            {
                Vector2 c0 = clip[e], c1 = clip[(e + 1) % 3];
                var output = new List<Vector2>();
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 p = poly[i], q = poly[(i + 1) % poly.Count];
                    float sp = Cross(c1 - c0, p - c0), sq = Cross(c1 - c0, q - c0);
                    if (sp >= 0f) output.Add(p);
                    if ((sp >= 0f) != (sq >= 0f)) output.Add(p + (q - p) * (sp / (sp - sq)));
                }
                poly = output;
            }
            float area = 0f;
            for (int i = 0; i < poly.Count; i++) area += Cross(poly[i], poly[(i + 1) % poly.Count]);
            return Mathf.Abs(area) / 2f;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        // Command line: Unity.exe -batchmode -projectPath . -executeMethod SlipSkid.MapKit.Editor.DecorCheck.BatchReport -quit
        // Checks every generated map scene as it is (no rebuild) and logs what flickers.
        public static void BatchReport()
        {
            foreach (string scene in Directory.GetFiles(T.MapsFolder, "*.unity", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(scene);
                if (name.StartsWith("Official")) continue;
                EditorSceneManager.OpenScene(scene.Replace('\\', '/'), OpenSceneMode.Single);
                KitMap map = Object.FindObjectOfType<KitMap>();
                if (map == null) continue;
                List<string> faces = CoplanarFaces(map.transform, 200);
                Debug.Log($"[Slip & Skid] Flicker check {name}: {faces.Count} pair(s) of overlapping faces in one plane.");
                foreach (string f in faces) Debug.Log($"[Slip & Skid]   {name}: {f}");
            }
        }
    }
}
