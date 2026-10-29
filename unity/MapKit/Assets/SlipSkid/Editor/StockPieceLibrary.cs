using System;
using System.Collections.Generic;
using System.IO;
using SlipSkid.MapKit.SDK;
using UnityEditor;
using UnityEngine;
using T = SlipSkid.MapKit.Editor.SlipSkidMapTools;

namespace SlipSkid.MapKit.Editor
{
    // The game's own road pieces in the editor (KitStockPiece, issue #16): their meshes and the official tracks'
    // layouts are read from the game install by `python tools/stock_pieces.py` into Assets/StockPieces (git-ignored:
    // game data). A marker gets a "StockPreview" child with the piece's parts (road and borders, with colliders so the
    // map check and the level-select pictures work); BuildAndInstall leaves it out of the bundle – the loader clones
    // the real piece in game.
    internal static class StockPieceLibrary
    {
        public const string Folder = "Assets/StockPieces";
        public const string PreviewName = "StockPreview";
        private const float StockScale = 100f;
        private static readonly Quaternion StockTurn = Quaternion.Euler(270f, 0f, 0f);

        [Serializable]
        public sealed class Part
        {
            public string name, kind, mesh;
            public float[] pos, rot, scale, v;
            public int[] t;
        }

        [Serializable]
        public sealed class Piece
        {
            public string name;
            public Part[] parts;
        }

        private static readonly Dictionary<string, Piece> Cache = new Dictionary<string, Piece>(StringComparer.OrdinalIgnoreCase);

        public static bool Available => Directory.Exists(Path.Combine(Folder, "pieces"));

        public static Piece Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out Piece piece)) return piece;
            string path = Path.Combine(Folder, "pieces", name + ".json");
            piece = File.Exists(path) ? JsonUtility.FromJson<Piece>(File.ReadAllText(path)) : null;
            Cache[name] = piece;
            return piece;
        }

        public static IEnumerable<string> Names()
        {
            string dir = Path.Combine(Folder, "pieces");
            if (!Directory.Exists(dir)) yield break;
            foreach (string f in Directory.GetFiles(dir, "*.json")) yield return Path.GetFileNameWithoutExtension(f);
        }

        // The part's mesh as an asset next to the data (one per stock mesh, made once).
        private static Mesh MeshFor(Part part)
        {
            string dir = Path.Combine(Folder, "Meshes");
            string path = $"{Folder}/Meshes/{part.mesh}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            Directory.CreateDirectory(dir);
            var verts = new Vector3[part.v.Length / 3];
            for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(part.v[3 * i], part.v[3 * i + 1], part.v[3 * i + 2]);
            mesh = new Mesh { name = part.mesh, indexFormat = verts.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = verts;
            mesh.triangles = part.t;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // Rebuilds the marker's preview: the piece's parts (minus withoutParts) under a child in the piece's own frame.
        public static bool AddPreview(KitStockPiece marker)
        {
            Transform old = marker.transform.Find(PreviewName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            Piece piece = Load(marker.piece);
            if (piece == null) return false;
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in (marker.withoutParts ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) skip.Add(s.Trim());

            GameObject preview = T.Child(marker.transform, PreviewName);
            preview.tag = "EditorOnly";
            preview.transform.localRotation = StockTurn;
            preview.transform.localScale = Vector3.one * StockScale;
            foreach (Part part in piece.parts)
            {
                if (skip.Contains(part.name)) continue;
                bool road = part.kind == "road";
                GameObject go = road ? preview : T.Child(preview.transform, part.name);
                if (!road)
                {
                    go.transform.localPosition = new Vector3(part.pos[0], part.pos[1], part.pos[2]);
                    go.transform.localRotation = new Quaternion(part.rot[0], part.rot[1], part.rot[2], part.rot[3]);
                    go.transform.localScale = new Vector3(part.scale[0], part.scale[1], part.scale[2]);
                }
                Mesh mesh = MeshFor(part);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = PreviewMaterial(road ? marker.surface : marker.borderSurface, road);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            return true;
        }

        // Is this object part of a stock piece's preview (left out of the bundle)?
        public static bool IsPreview(Transform t)
        {
            for (Transform x = t; x != null; x = x.parent)
            {
                if (x.name == PreviewName && x.parent != null && x.parent.GetComponent<KitStockPiece>() != null) return true;
            }
            return false;
        }

        private static Material PreviewMaterial(string surface, bool road)
        {
            string name = string.IsNullOrEmpty(surface) ? road ? "Asphalt" : "Concrete" : surface;
            Directory.CreateDirectory(Path.Combine(Folder, "Materials"));
            return T.GetOrCreateMaterial($"{Folder}/Materials/{name}.mat", LevelSelectAssets.SurfaceColor(name));
        }

        // ------------------------------------------------------------------ editor menus

        [MenuItem("Slip & Skid/Stock pieces/Refresh previews in this scene", priority = 30)]
        public static void RefreshPreviews()
        {
            if (!Available)
            {
                EditorUtility.DisplayDialog("Slip & Skid", "Run  python tools/stock_pieces.py  first: it reads the stock pieces from your game install.", "OK");
                return;
            }
            int done = 0, missing = 0;
            foreach (KitStockPiece m in UnityEngine.Object.FindObjectsOfType<KitStockPiece>(true))
            {
                if (AddPreview(m)) done++;
                else missing++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Slip & Skid] Stock piece previews: {done} made, {missing} unknown piece name(s).");
        }

        [MenuItem("GameObject/Slip & Skid/Stock Piece (R001B101 straight)", false, 12)]
        private static void AddStockPiece(MenuCommand cmd)
        {
            GameObject go = new GameObject("R001B101");
            KitStockPiece m = go.AddComponent<KitStockPiece>();
            m.piece = "R001B101";
            KitMap root = UnityEngine.Object.FindObjectOfType<KitMap>();
            if (cmd.context is GameObject parent) go.transform.SetParent(parent.transform, false);
            else if (root != null) go.transform.SetParent(root.transform, false);
            if (SceneView.lastActiveSceneView != null) go.transform.position = SceneView.lastActiveSceneView.pivot;
            if (Available) AddPreview(m);
            Undo.RegisterCreatedObjectUndo(go, "Add stock piece");
            Selection.activeGameObject = go;
        }
    }
}
