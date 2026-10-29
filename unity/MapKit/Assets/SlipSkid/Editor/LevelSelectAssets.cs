using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SlipSkid.MapKit.SDK;
using UnityEngine;

namespace SlipSkid.MapKit.Editor
{
    // Level-select assets for a map, drawn from its own road:
    //   preview.png   - the track seen from above at an angle (left half of the level card)
    //   thumbnail.png - top-down outline (right half of the level card)
    //   preview.obj   - the spinning 3D model, loaded by the SlipSkid.MapPreviews plugin
    // Rendered on the CPU so it also works in batch mode without a graphics device.
    public static class LevelSelectAssets
    {
        // Colours sampled from the stock level cards.
        private static readonly Color RoadColor = new Color(0.25f, 0.24f, 0.36f, 1f);
        private static readonly Color ShadowColor = new Color(0.55f, 0.58f, 0.72f, 0.22f);
        private static readonly Color OutlineColor = new Color(0.80f, 0.82f, 0.90f, 1f);

        // Level-select colours of the road surfaces (the shaded card image, shown untinted), close to the stock cards: the host road dark, the
        // season surfaces in their own colour so a map with several surfaces shows them (Four Seasons).
        internal static Color SurfaceColor(string surface)
        {
            switch ((surface ?? "").ToLowerInvariant())
            {
                case "dirt": return new Color(0.62f, 0.43f, 0.25f);
                case "sand": return new Color(0.96f, 0.80f, 0.36f);
                case "mud": return new Color(0.36f, 0.33f, 0.22f);
                case "ice": return new Color(0.50f, 0.86f, 1.00f);
                case "magma": return new Color(0.95f, 0.38f, 0.18f);
                case "concrete": return OutlineColor;
                default: return RoadColor;   // "" (the host's road surface) and Asphalt
            }
        }

        private struct Tri
        {
            public Vector3 A, B, C;
            public bool Border;
            public string Surface;   // the road's KitRoad.surface ("" = host road); borders take their road's
        }

        public static void Export(KitMap root, string outDir)
        {
            List<Tri> tris = CollectTriangles(root, out int tileCount);
            if (tris.Count == 0)
            {
                Debug.LogWarning("[Slip & Skid] No road geometry found; level-select images not generated.");
                return;
            }
            WriteObj(tris, tileCount, Path.Combine(outDir, "preview.obj"));
            WritePng(Render(tris, 512, 320, Quaternion.Euler(58f, 20f, 0f), shaded: true, grow: 2), Path.Combine(outDir, "preview.png"));
            WritePng(Render(tris, 256, 256, Quaternion.Euler(90f, 0f, 0f), shaded: false, grow: 1), Path.Combine(outDir, "thumbnail.png"));
        }

        private static List<Tri> CollectTriangles(KitMap root, out int tileCount)
        {
            var tris = new List<Tri>();
            tileCount = 0;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                KitRoad road = mf.GetComponent<KitRoad>();
                bool isRoad, isBorder;
                string surface;
                if (road == null && StockPieceLibrary.IsPreview(mf.transform))
                {
                    // A stock piece's preview (StockPieceLibrary): the piece is the preview root, its parts the borders.
                    KitStockPiece stock = mf.GetComponentInParent<KitStockPiece>(true);
                    isRoad = mf.name == StockPieceLibrary.PreviewName;
                    isBorder = !isRoad;
                    surface = stock != null ? stock.surface ?? "" : "";
                }
                else
                {
                    if (road == null || (road.kind != KitRoadKind.Road && road.kind != KitRoadKind.Border)) continue;
                    isRoad = road.kind == KitRoadKind.Road;
                    isBorder = road.kind == KitRoadKind.Border;
                    // Borders, walls and roofs are children of their road tile (TrackBuilder.Border).
                    KitRoad owner = isRoad ? road : mf.transform.parent != null ? mf.transform.parent.GetComponent<KitRoad>() : null;
                    surface = owner != null && owner.kind == KitRoadKind.Road ? owner.surface ?? "" : "";
                }
                if (isRoad) tileCount++;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                Vector3[] v = mf.sharedMesh.vertices;
                int[] t = mf.sharedMesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    tris.Add(new Tri
                    {
                        A = m.MultiplyPoint3x4(v[t[i]]),
                        B = m.MultiplyPoint3x4(v[t[i + 1]]),
                        C = m.MultiplyPoint3x4(v[t[i + 2]]),
                        Border = isBorder,
                        Surface = surface,
                    });
                }
            }
            return tris;
        }

        // Plain OBJ in Unity's own axes, centred on the origin (the level select spins it around 0,0,0).
        // Group "road" (tiles on the host's road surface) is submesh 0, "border" (borders, walls, roofs: concrete)
        // submesh 1, then "surface <Name>" per KitRoad.surface used (MapKitLoader 1.1.0 draws each with that
        // surface's level-select material; older loaders merge the extra groups).
        private static void WriteObj(List<Tri> tris, int tileCount, string path)
        {
            var b = new Bounds(tris[0].A, Vector3.zero);
            foreach (Tri t in tris) { b.Encapsulate(t.A); b.Encapsulate(t.B); b.Encapsulate(t.C); }
            Vector3 c = b.center;
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Slip & Skid map preview");
            sb.AppendLine($"# tiles {tileCount}");
            foreach (Tri t in tris)
            {
                foreach (Vector3 p in new[] { t.A, t.B, t.C })
                {
                    Vector3 q = p - c;
                    sb.Append("v ").Append(q.x.ToString("0.###", inv)).Append(' ')
                      .Append(q.y.ToString("0.###", inv)).Append(' ').Append(q.z.ToString("0.###", inv)).Append('\n');
                }
            }
            var groups = new List<(string name, System.Func<Tri, bool> member)>
            {
                ("road", t => !t.Border && string.IsNullOrEmpty(t.Surface)),
                ("border", t => t.Border),
            };
            var named = new SortedSet<string>();
            foreach (Tri t in tris) if (!t.Border && !string.IsNullOrEmpty(t.Surface)) named.Add(t.Surface);
            foreach (string n in named) groups.Add(("surface " + n, t => !t.Border && t.Surface == n));
            foreach (var (name, member) in groups)
            {
                sb.AppendLine("g " + name);
                for (int i = 0; i < tris.Count; i++)
                {
                    if (!member(tris[i])) continue;
                    int v0 = i * 3 + 1;
                    sb.Append("f ").Append(v0).Append(' ').Append(v0 + 1).Append(' ').Append(v0 + 2).Append('\n');
                }
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static Texture2D Render(List<Tri> tris, int width, int height, Quaternion camera, bool shaded, int grow)
        {
            const int ss = 3;   // supersampling for smooth edges
            int w = width * ss, h = height * ss;
            Quaternion toView = Quaternion.Inverse(camera);
            Vector3 light = new Vector3(-0.35f, 1f, -0.25f).normalized;

            float groundY = float.MaxValue;
            foreach (Tri t in tris) groundY = Mathf.Min(groundY, Mathf.Min(t.A.y, Mathf.Min(t.B.y, t.C.y)));
            groundY -= 4f;

            // Fit everything, shadow included, into the image.
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (Tri t in tris)
            {
                foreach (Vector3 p in new[] { t.A, t.B, t.C })
                {
                    foreach (Vector3 q in shaded ? new[] { p, new Vector3(p.x, groundY, p.z) } : new[] { p })
                    {
                        Vector3 v = toView * q;
                        min = Vector2.Min(min, v);
                        max = Vector2.Max(max, v);
                    }
                }
            }
            float margin = 0.06f;
            float scale = Mathf.Min(w * (1f - 2f * margin) / (max.x - min.x), h * (1f - 2f * margin) / (max.y - min.y));
            Vector2 offset = new Vector2(w, h) * 0.5f - (min + max) * 0.5f * scale;

            var color = new Color[w * h];
            var depth = new float[w * h];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.MaxValue;

            Vector3 Project(Vector3 p)
            {
                Vector3 v = toView * p;
                return new Vector3(v.x * scale + offset.x, v.y * scale + offset.y, v.z);
            }

            if (shaded)
            {
                foreach (Tri t in tris)
                {
                    Raster(Project(new Vector3(t.A.x, groundY, t.A.z)), Project(new Vector3(t.B.x, groundY, t.B.z)),
                           Project(new Vector3(t.C.x, groundY, t.C.z)), w, h, (i, z) => color[i] = ShadowColor);
                }
            }
            foreach (Tri t in tris)
            {
                Color c;
                if (shaded)
                {
                    Vector3 n = Vector3.Cross(t.B - t.A, t.C - t.A).normalized;
                    float lambert = Mathf.Abs(Vector3.Dot(n, light));
                    // Borders take their road's colour: at card size road and borders are a pixel or two, and two
                    // colours speckle.
                    c = SurfaceColor(t.Surface) * (0.55f + 0.45f * lambert);
                    c.a = 1f;
                }
                else
                {
                    // The outline stays light: the level select tints it with the theme of the map's look
                    // (LevelSelectElement.levelThumbnail.color = thumbnailColor), colours would mix.
                    c = OutlineColor;
                }
                Raster(Project(t.A), Project(t.B), Project(t.C), w, h, (i, z) =>
                {
                    if (z >= depth[i]) return;
                    depth[i] = z;
                    color[i] = c;
                });
            }

            // Downsample, averaging colour by coverage so edges fade into the transparent background.
            var outPixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float r = 0, g = 0, bl = 0, a = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            Color p = color[(y * ss + sy) * w + x * ss + sx];
                            r += p.r * p.a; g += p.g * p.a; bl += p.b * p.a; a += p.a;
                        }
                    }
                    outPixels[y * width + x] = a > 0f ? new Color(r / a, g / a, bl / a, a / (ss * ss)) : Color.clear;
                }
            }
            // A 1.8 km track is only a few pixels wide on a card: thicken it so it reads like the stock ones.
            for (int i = 0; i < grow; i++) outPixels = Grow(outPixels, width, height);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels(outPixels);
            tex.Apply();
            return tex;
        }

        // One-pixel dilation: each pixel takes the most opaque of itself and its four neighbours.
        private static Color[] Grow(Color[] src, int w, int h)
        {
            var dst = new Color[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color best = src[y * w + x];
                    if (x > 0 && src[y * w + x - 1].a > best.a) best = src[y * w + x - 1];
                    if (x < w - 1 && src[y * w + x + 1].a > best.a) best = src[y * w + x + 1];
                    if (y > 0 && src[(y - 1) * w + x].a > best.a) best = src[(y - 1) * w + x];
                    if (y < h - 1 && src[(y + 1) * w + x].a > best.a) best = src[(y + 1) * w + x];
                    dst[y * w + x] = best;
                }
            }
            return dst;
        }

        // Fills a screen-space triangle; plot gets the pixel index and interpolated depth.
        private static void Raster(Vector3 a, Vector3 b, Vector3 c, int w, int h, System.Action<int, float> plot)
        {
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-6f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            for (int y = y0; y <= y1; y++)
            {
                float py = y + 0.5f;
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f;
                    float w0 = ((b.x - px) * (c.y - py) - (b.y - py) * (c.x - px)) / area;
                    float w1 = ((c.x - px) * (a.y - py) - (c.y - py) * (a.x - px)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                    plot(y * w + x, w0 * a.z + w1 * b.z + w2 * c.z);
                }
            }
        }

        private static void WritePng(Texture2D tex, string path)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
