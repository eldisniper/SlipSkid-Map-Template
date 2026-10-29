"""Draws a map's track.json (written by the Map Kit build) as a PNG: top view and height profile, coloured by surface,
jump gaps dotted, start (green) and finish (checkered). Use it to check a generator's layout without the game.

    python tools/plot_track.py unity/MapKit/Builds/<Map>/track.json [out.png]
"""
import json
import os
import sys

from PIL import Image, ImageDraw

COLOURS = {"": (90, 100, 150), "Asphalt": (90, 100, 150), "Dirt": (120, 170, 70), "Sand": (230, 200, 90),
           "Mud": (120, 85, 55), "Ice": (150, 220, 240), "Magma": (230, 90, 40)}


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    path = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.splitext(path)[0] + ".png"
    data = json.load(open(path, encoding="utf-8"))
    names = data.get("surfaces", [""])
    pts = data["points"]
    xs, ys, zs = [p[0] for p in pts], [p[1] for p in pts], [p[2] for p in pts]
    W, H, M = 1400, 1000, 40
    sx = (W - 2 * M) / max(1e-3, max(xs) - min(xs))
    sz = (H - 2 * M) / max(1e-3, max(zs) - min(zs))
    s = min(sx, sz)

    def top(p):
        return M + (p[0] - min(xs)) * s, H - M - (p[2] - min(zs)) * s

    img = Image.new("RGB", (W, H + 320), (25, 28, 40))
    d = ImageDraw.Draw(img)
    for i in range(1, len(pts)):
        a, b = pts[i - 1], pts[i]
        if ((a[0] - b[0]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5 > 15:
            continue   # fork lanes / sections jump: don't join them
        name = names[int(b[7])] if len(b) > 7 else ""
        colour = COLOURS.get(name, (200, 200, 200))
        width = 2 if b[6] else 6
        d.line([top(a), top(b)], fill=colour if not b[6] else (255, 255, 255), width=width)
    d.ellipse([top(pts[0])[0] - 8, top(pts[0])[1] - 8, top(pts[0])[0] + 8, top(pts[0])[1] + 8], fill=(60, 220, 60))
    fx, fy = top(pts[-1])
    d.rectangle([fx - 8, fy - 8, fx + 8, fy + 8], fill=(255, 255, 255), outline=(0, 0, 0), width=3)

    # height profile along the track
    y0, ph = H + 20, 260
    lo, hi = min(ys), max(ys)
    for i in range(1, len(pts)):
        a, b = pts[i - 1], pts[i]
        name = names[int(b[7])] if len(b) > 7 else ""
        xa, xb = M + (i - 1) / len(pts) * (W - 2 * M), M + i / len(pts) * (W - 2 * M)
        ya = y0 + ph - (a[1] - lo) / max(1e-3, hi - lo) * ph
        yb = y0 + ph - (b[1] - lo) / max(1e-3, hi - lo) * ph
        d.line([(xa, ya), (xb, yb)], fill=COLOURS.get(name, (200, 200, 200)), width=3)
    d.text((M, y0 - 14), f"height {lo:.0f} .. {hi:.0f} m   points {len(pts)}   " +
           "   ".join(f"{n or 'host'}" for n in names), fill=(220, 220, 220))
    img.save(out)
    print("written:", out)


if __name__ == "__main__":
    main()
