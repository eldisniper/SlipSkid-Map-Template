using System;
using System.Collections.Generic;
using SlipSkid.MapKit.SDK;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlipSkid.MapKit.Editor
{
    // The decoration workshop of the 1.3.0 maps: shapes Unity's primitives don't have – rings, lathed silhouettes
    // (lighthouses, volcano cones, crystals, vases), tubes along a curve (sandworms, horns, pipes, lava falls), portal
    // discs, lumpy rocks – saved into the map's DecorMeshes.asset so the bundle carries them, and two ways to show them:
    //  - Painted: a game toon material with a tint (KitRoad Concrete + paint, like SeasonDecor.Prim) – the stock props'
    //    look, lit and shaded by the level;
    //  - Glowing: our unlit SlipSkid/Glow shader (neon, lava, portals, lasers; bands, swirl, pulse) – it ships in the
    //    bundle and draws in the transparent queue, out of the game's depth priming and ink outline (Glow.shader).
    // Decoration only: nothing gets a collider. Mesh sizes are about one unit: scale them when placing.
    internal sealed class Sculpt
    {
        private const string Toon = "ToonWood";   // matte toon material, recoloured by the tint (SeasonDecor)
        private readonly string _folder, _meshPath;
        private bool _assetReady;
        private readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Material> _glows = new Dictionary<string, Material>();
        private Material _preview;

        // The latest Sculpt made for each map folder: two would each start the folder's DecorMeshes.asset afresh and
        // wipe the other's meshes, so shared decor (CityDecor, SeasonDecor) borrows the map's own with For().
        private static readonly Dictionary<string, Sculpt> _byFolder = new Dictionary<string, Sculpt>();

        public Sculpt(string folder)
        {
            _folder = folder;
            _meshPath = $"{folder}/DecorMeshes.asset";
            _byFolder[folder] = this;
        }

        // The map's Sculpt (the one its generator made for this folder), or a new one if it has none.
        public static Sculpt For(string folder) => _byFolder.TryGetValue(folder, out Sculpt s) ? s : new Sculpt(folder);

        // ------------------------------------------------------------------ shapes

        // A ring in the XY plane around the Z axis (faces along Z, like a portal facing the road): ring radius 1.
        public Mesh Torus(float tube = 0.12f, int segments = 40, int sides = 10)
        {
            string key = $"torus_{tube:0.###}_{segments}_{sides}";
            if (_meshes.TryGetValue(key, out Mesh cached)) return cached;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float u = i * Mathf.PI * 2f / segments;
                var c = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                for (int j = 0; j <= sides; j++)
                {
                    float w = j * Mathf.PI * 2f / sides;
                    v.Add(c * (1f + tube * Mathf.Cos(w)) + Vector3.forward * (tube * Mathf.Sin(w)));
                    uv.Add(new Vector2((float)i / segments, (float)j / sides));
                }
            }
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j, b = a + 1, c = a + sides + 1, d = c + 1;
                    float u = (i + 0.5f) * Mathf.PI * 2f / segments;
                    Vector3 mid = (v[a] + v[d]) * 0.5f, axis = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                    Quad(t, v, a, b, c, d, mid - axis);
                }
            }
            return Save(key, v, uv, t);
        }

        // A solid of revolution about Y: profile points (radius, height) from bottom to top; uv.x around, uv.y up.
        public Mesh Lathe(string key, IList<Vector2> profile, int segments = 24)
        {
            key = "lathe_" + key + "_" + segments;
            if (_meshes.TryGetValue(key, out Mesh cached)) return cached;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            float total = 0f;
            var along = new float[profile.Count];
            for (int r = 1; r < profile.Count; r++)
            {
                total += (profile[r] - profile[r - 1]).magnitude;
                along[r] = total;
            }
            for (int r = 0; r < profile.Count; r++)
            {
                for (int s = 0; s <= segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    v.Add(new Vector3(Mathf.Cos(a) * profile[r].x, profile[r].y, Mathf.Sin(a) * profile[r].x));
                    uv.Add(new Vector2((float)s / segments, total > 0f ? along[r] / total : 0f));
                }
            }
            int cols = segments + 1;
            for (int r = 0; r < profile.Count - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * cols + s, b = a + 1, c = a + cols, d = c + 1;
                    float ang = (s + 0.5f) * Mathf.PI * 2f / segments;
                    Vector3 outward = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    // A flat cap ring (same height, radius shrinking) faces up or down.
                    float dr = profile[r + 1].x - profile[r].x, dh = profile[r + 1].y - profile[r].y;
                    if (Mathf.Abs(dh) < 1e-4f) outward = dr < 0f ? Vector3.up : Vector3.down;
                    Quad(t, v, a, b, c, d, outward);
                }
            }
            return Save(key, v, uv, t);
        }

        public Mesh Cone(int segments = 20) => Lathe("cone", new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) }, segments);

        // A faceted gem (crystals, ice shards, gems in a vault): 1 tall, about 0.45 wide.
        public Mesh Crystal(int facets = 6) => Lathe("crystal", new[]
        {
            new Vector2(0f, 0f), new Vector2(0.32f, 0.1f), new Vector2(0.45f, 0.72f), new Vector2(0f, 1f),
        }, facets);

        // A tube along a polyline with a radius per point (horns, worm bodies, pipes, ribs, lava falls, tentacles);
        // uv.x around, uv.y along (Glow bands scroll along it).
        public Mesh Tube(string key, IList<Vector3> spine, IList<float> radii, int sides = 10)
        {
            key = "tube_" + key + "_" + sides;
            if (_meshes.TryGetValue(key, out Mesh cached)) return cached;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            int n = spine.Count;
            Vector3 normal = Vector3.zero;
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 tangent = (spine[Mathf.Min(i + 1, n - 1)] - spine[Mathf.Max(i - 1, 0)]).normalized;
                if (i == 0)
                {
                    normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                }
                else
                {
                    total += (spine[i] - spine[i - 1]).magnitude;
                    normal = Vector3.Cross(Vector3.Cross(tangent, normal), tangent).normalized;   // parallel transport
                }
                Vector3 binormal = Vector3.Cross(tangent, normal);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    v.Add(spine[i] + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * radii[i]);
                    uv.Add(new Vector2((float)s / sides, total));
                }
            }
            for (int k = 0; k < uv.Count; k++) uv[k] = new Vector2(uv[k].x, total > 0f ? uv[k].y / total : 0f);
            int cols = sides + 1;
            for (int i = 0; i < n - 1; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = i * cols + s, b = a + 1, c = a + cols, d = c + 1;
                    Vector3 mid = (v[a] + v[b] + v[c] + v[d]) * 0.25f;
                    Quad(t, v, a, b, c, d, mid - (spine[i] + spine[i + 1]) * 0.5f);
                }
            }
            return Save(key, v, uv, t);
        }

        // A round disc of radius 1 in the XY plane, both faces; uv 0..1 across it (Glow swirl turns around its centre).
        public Mesh Disc(int segments = 40)
        {
            string key = "disc_" + segments;
            if (_meshes.TryGetValue(key, out Mesh cached)) return cached;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            foreach (float face in new[] { -1f, 1f })
            {
                int centre = v.Count;
                v.Add(Vector3.zero);
                uv.Add(new Vector2(0.5f, 0.5f));
                for (int s = 0; s <= segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
                    uv.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
                }
                for (int s = 0; s < segments; s++)
                {
                    int a = centre + 1 + s, b = a + 1;
                    if (Vector3.Dot(Vector3.Cross(v[a] - v[centre], v[b] - v[centre]), Vector3.forward * face) >= 0f)
                        t.AddRange(new[] { centre, a, b });
                    else
                        t.AddRange(new[] { centre, b, a });
                }
            }
            return Save(key, v, uv, t);
        }

        // A lumpy boulder of radius about 1 (rocks, asteroids, lava bombs); the same seed gives the same rock.
        public Mesh Rock(int seed, float roughness = 0.25f, int rings = 10, int segments = 16)
        {
            string key = $"rock_{seed}_{roughness:0.##}";
            if (_meshes.TryGetValue(key, out Mesh cached)) return cached;
            var rng = new System.Random(seed);
            var dirs = new Vector3[5];
            var phases = new float[5];
            for (int k = 0; k < dirs.Length; k++)
            {
                dirs[k] = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized * (2f + k * 1.3f);
                phases[k] = (float)rng.NextDouble() * 6.28f;
            }
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.PI * r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float lon = s * Mathf.PI * 2f / segments;
                    var dir = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
                    float bump = 0f;
                    for (int k = 0; k < dirs.Length; k++) bump += Mathf.Sin(Vector3.Dot(dir, dirs[k]) + phases[k]) / (k + 1f);
                    v.Add(dir * (1f + roughness * bump * 0.6f));
                    uv.Add(new Vector2((float)s / segments, (float)r / rings));
                }
            }
            int cols = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * cols + s, b = a + 1, c = a + cols, d = c + 1;
                    Quad(t, v, a, b, c, d, (v[a] + v[d]) * 0.5f);
                }
            }
            return Save(key, v, uv, t);
        }

        // ------------------------------------------------------------------ placing them

        // Painted with the game's matte toon material in this colour (lit and shaded like the stock props).
        public GameObject Painted(Mesh mesh, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 scale, Color tint, string name = null)
        {
            GameObject go = Place(mesh, parent, localPos, localRot, scale, name);
            go.GetComponent<MeshRenderer>().sharedMaterial = Preview();
            var kit = go.AddComponent<KitRoad>();
            kit.kind = KitRoadKind.Concrete;
            kit.paint = Toon;
            kit.tint = new Color(Mathf.Clamp01(tint.r), Mathf.Clamp01(tint.g), Mathf.Clamp01(tint.b), 1f);
            return go;
        }

        // Lit from inside: an unlit Glow material (see Glow()).
        public GameObject Glowing(Mesh mesh, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 scale, Material glow, string name = null)
        {
            GameObject go = Place(mesh, parent, localPos, localRot, scale, name);
            go.GetComponent<MeshRenderer>().sharedMaterial = glow;
            return go;
        }

        // A Unity primitive (cube, sphere, cylinder, capsule) lit from inside.
        public GameObject GlowPrim(PrimitiveType type, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 scale, Material glow)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = "Glow " + type;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = glow;
            return go;
        }

        // An unlit glowing material (one asset per name in the map folder): steady, banded (lava, scan lines), swirling
        // (portal) or pulsing (beacons). Falls back to a flat preview colour if the shader isn't imported yet.
        // fogAmount: how far it fades into the level's fog (Glow.shader: 0.6 by default; 0 for the sky's planets and moons).
        public Material Glow(string name, Color color, Color? second = null, float bands = 0f, float speed = 0.5f, float swirl = 0f, float pulse = 0f,
                             float fogAmount = 0.6f)
        {
            if (_glows.TryGetValue(name, out Material cached)) return cached;
            string path = $"{_folder}/Glow_{name}.mat";
            Shader shader = Shader.Find("SlipSkid/Glow");
            Material m;
            if (shader == null)
            {
                m = SlipSkidMapTools.GetOrCreateMaterial(path, color);
            }
            else
            {
                AssetDatabase.DeleteAsset(path);
                m = new Material(shader) { name = "Glow_" + name };
                m.SetColor("_Color", color);
                m.SetColor("_Color2", second ?? color);
                m.SetFloat("_Bands", bands);
                m.SetFloat("_Speed", speed);
                m.SetFloat("_Swirl", swirl);
                m.SetFloat("_Pulse", pulse);
                m.SetFloat("_FogAmount", fogAmount);
                AssetDatabase.CreateAsset(m, path);
            }
            _glows[name] = m;
            return m;
        }

        // Soft light (SlipSkid/GlowSoft, additive): beams, steam, spray, haze, force fields – what the opaque Glow turns
        // into solid shapes. It fades along the mesh's v (fadeAlong 1: gone at v = 1 – a lathed cone's v runs up its
        // profile, a Tube's along its spine, a Unity cylinder's round it, so use Sculpt shapes) and where faces turn
        // edge-on (edgeSoft; 0 = no edge fade). Show it with Glowing / GlowPrim like a Glow material.
        public Material SoftGlow(string name, Color color, Color? second = null, float strength = 0.8f, float fadeAlong = 1f,
                                 float edgeSoft = 1.5f, float bands = 0f, float speed = 0.5f, float pulse = 0f, float fogAmount = 0.6f)
        {
            string key = "soft_" + name;
            if (_glows.TryGetValue(key, out Material cached)) return cached;
            string path = $"{_folder}/GlowSoft_{name}.mat";
            Shader shader = Shader.Find("SlipSkid/GlowSoft");
            Material m;
            if (shader == null)
            {
                m = SlipSkidMapTools.GetOrCreateMaterial(path, color);
            }
            else
            {
                AssetDatabase.DeleteAsset(path);
                m = new Material(shader) { name = "GlowSoft_" + name };
                m.SetColor("_Color", color);
                m.SetColor("_Color2", second ?? color);
                m.SetFloat("_Strength", strength);
                m.SetFloat("_FadeAlong", fadeAlong);
                m.SetFloat("_EdgeSoft", edgeSoft);
                m.SetFloat("_Bands", bands);
                m.SetFloat("_Speed", speed);
                m.SetFloat("_Pulse", pulse);
                m.SetFloat("_FogAmount", fogAmount);
                AssetDatabase.CreateAsset(m, path);
            }
            _glows[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ helpers

        private GameObject Place(Mesh mesh, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 scale, string name)
        {
            var go = new GameObject(name ?? mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            return go;
        }

        private Material Preview() =>
            _preview != null ? _preview : _preview = SlipSkidMapTools.GetOrCreateMaterial($"{_folder}/DecorPreview.mat", new Color(0.7f, 0.68f, 0.64f));

        // v0/v1 across one row, v2/v3 the same on the next: two triangles facing `outward` (as TrackBuilder.AddQuad,
        // but judged on both triangles: at a cone's tip one of them has no area).
        private static void Quad(List<int> tris, List<Vector3> v, int v0, int v1, int v2, int v3, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(v[v2] - v[v0], v[v3] - v[v0]) + Vector3.Cross(v[v3] - v[v0], v[v1] - v[v0]);
            if (Vector3.Dot(n, outward) >= 0f)
                tris.AddRange(new[] { v0, v2, v3, v0, v3, v1 });
            else
                tris.AddRange(new[] { v0, v3, v2, v0, v1, v3 });
        }

        // Vertices that share a position (the uv seam of a ring, the poles of a rock) get one normal: with their own,
        // the game's ink outline drew a black line down the seam (drifting rocks, 2026-10-27). Corners meant to be
        // sharp (normals more than 70° apart) keep theirs.
        private static void SmoothSeams(Mesh mesh)
        {
            Vector3[] pos = mesh.vertices, nrm = mesh.normals;
            var groups = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < pos.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(pos[i].x * 1000f), Mathf.RoundToInt(pos[i].y * 1000f), Mathf.RoundToInt(pos[i].z * 1000f));
                if (!groups.TryGetValue(key, out List<int> list)) groups[key] = list = new List<int>();
                list.Add(i);
            }
            bool changed = false;
            foreach (List<int> list in groups.Values)
            {
                if (list.Count < 2) continue;
                Vector3 sum = Vector3.zero;
                foreach (int i in list) sum += nrm[i];
                if (sum.sqrMagnitude < 1e-8f) continue;
                Vector3 avg = sum.normalized;
                foreach (int i in list)
                {
                    if (Vector3.Angle(nrm[i], avg) > 70f) continue;
                    nrm[i] = avg;
                    changed = true;
                }
            }
            if (changed) mesh.normals = nrm;
        }

        private Mesh Save(string key, List<Vector3> v, List<Vector2> uv, List<int> t)
        {
            var mesh = new Mesh { name = key };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            SmoothSeams(mesh);
            mesh.RecalculateBounds();
            if (!_assetReady)
            {
                // An empty mesh is the asset's main object: CreateAsset renames its main object after the file, and
                // checks that know Sculpt meshes by name ("tube_", "lathe_"...) took the first real one for a plain box.
                AssetDatabase.DeleteAsset(_meshPath);
                AssetDatabase.CreateAsset(new Mesh { name = "DecorMeshes" }, _meshPath);
                _assetReady = true;
            }
            AssetDatabase.AddObjectToAsset(mesh, _meshPath);
            _meshes[key] = mesh;
            return mesh;
        }
    }
}
