"""A generator's layout without Unity: the same centre-line maths as TrackBuilder (Segment, Fork/Split, Loop,
BarrelRoll, Turntable, Gap, ReachHeight, Spur), its CheckClearance rules (12 m sideways / 10 m vertically, forks and
loops close on purpose, 25 m around loops) and a top view + height profile PNG. Validated against Four Seasons: its
mirror gives the real track.json point for point (1663 points). Use it to try a course before writing the generator:

    from track_sim import Sim                 # run from tools/, or put tools/ on sys.path
    b = Sim("MyMap")
    b.tiles(10); b.tiles(6, yaw=12)           # same calls and arguments as TrackBuilder (snake_case)
    b.fork(9, lambda: (b.tiles(4, yaw=-6), b.tiles(4, yaw=6)), lambda: (b.tiles(4, yaw=6), b.tiles(4, yaw=-6)))
    b.mark("vault")                           # labelled on the plot
    b.report("mymap.png")                     # prints length, heights, clearance problems; writes the plot

A JSON map (docs/JSON_MAPS.md) runs as it is, before Unity:

    python tools/track_sim.py maps/MegaStunt/map.json [plot.png]

Not modelled: widths and banks (no effect on the centre line), pipes, tunnels (plain tiles here), decoration.
Keep it in step with TrackBuilder when the maths there changes.
"""
import math
from PIL import Image, ImageDraw

SUB = 6
SPLIT = 30.0


def qmul(a, b):
    aw, ax, ay, az = a
    bw, bx, by, bz = b
    return (aw * bw - ax * bx - ay * by - az * bz,
            aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw)


def qaxis(axis, deg):
    h = math.radians(deg) / 2
    s = math.sin(h)
    n = math.sqrt(sum(c * c for c in axis)) or 1
    return (math.cos(h), axis[0] / n * s, axis[1] / n * s, axis[2] / n * s)


def qrot(q, v):
    p = (0.0, v[0], v[1], v[2])
    qc = (q[0], -q[1], -q[2], -q[3])
    r = qmul(qmul(q, p), qc)
    return (r[1], r[2], r[3])


def euler(x, y, z):
    return qmul(qmul(qaxis((0, 1, 0), y), qaxis((1, 0, 0), x)), qaxis((0, 0, 1), z))


def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


class Sim:
    def __init__(self, name):
        self.name = name
        self.pos = (0.0, 0.0, 0.0)
        self.rot = (1.0, 0.0, 0.0, 0.0)
        self.width = 8.0
        self.path = []        # (pos, tag)
        self.air = set()
        self.close = []
        self.loops = []
        self.spurs = []       # off-line points
        self.off = 0
        self.dist = 0.0
        self.marks = {}

    # heading helpers
    def yaw(self):
        f = qrot(self.rot, (0, 0, 1))
        return math.degrees(math.atan2(f[0], f[2]))

    def flat(self):
        return euler(0, self.yaw(), 0)

    def addp(self, p, tag=""):
        if self.off:
            self.spurs.append(p)
        else:
            self.path.append((p, tag))

    def tiles(self, count, yaw=0.0, pitch=0.0, width=8.0, length=10.0, tag=""):
        for _ in range(count):
            for _k in range(SUB):
                self.rot = qmul(qmul(euler(0, yaw / SUB, 0), self.rot), euler(pitch / SUB, 0, 0))
                self.pos = add(self.pos, mul(qrot(self.rot, (0, 0, 1)), length / SUB))
                self.addp(self.pos, tag)
            if not self.off:
                self.dist += length
        self.width = width

    def gap(self, forward, drop):
        self.rot = self.flat()
        frm = self.pos
        to = add(add(frm, mul(qrot(self.rot, (0, 0, 1)), forward)), (0, -drop, 0))
        for i in range(1, 8):
            p = tuple(frm[k] + (to[k] - frm[k]) * i / 8 for k in range(3))
            if self.off:
                self.spurs.append(p)
            else:
                self.air.add(len(self.path))
                self.path.append((p, "air"))
        self.pos = to
        self.dist += forward

    def _split(self, centre, heading, sign, spread, outward):
        n = math.ceil(SPLIT / (10 / SUB))
        fwd = qrot(heading, (0, 0, 1))
        side = qrot(heading, (1, 0, 0))
        last = None
        for k in range(n + 1):
            t = k / n
            e = t * t * (3 - 2 * t)
            off = spread * (e if outward else 1 - e)
            p = add(add(centre, mul(fwd, SPLIT * t)), mul(side, sign * off))
            last = p
            if k:
                self.addp(p, "fork")
        self.pos = last
        self.rot = heading
        self.dist += SPLIT

    def fork(self, spread, left, right):
        heading = self.flat()
        start = self.pos
        first = len(self.path)
        ends = []
        for sign, lane in ((-1, left), (1, right)):
            self.pos, self.rot = start, heading
            self._split(start, heading, sign, spread, True)
            lane()
            side = qrot(heading, (1, 0, 0))
            self._split(sub(self.pos, mul(side, sign * spread)), heading, sign, spread, False)
            ends.append(self.pos)
        d = math.dist(ends[0], ends[1])
        if d > 1:
            print(f"  WARNING fork lanes end {d:.1f} m apart")
        self.rot = heading
        self.pos = mul(add(ends[0], ends[1]), 0.5)
        self.close.append((first - 15, len(self.path) + 15))

    def loop(self, tiles, tile_length, width=8.0):
        first = len(self.path)
        entry = self.flat()
        self.rot = entry
        shift_dir = qrot(entry, (1, 0, 0))
        step = 360.0 / tiles
        shift = (width + 4) / tiles
        y0 = self.pos[1]
        for _ in range(tiles):
            for _k in range(SUB):
                self.rot = qmul(self.rot, euler(-step / SUB, 0, 0))
                self.pos = add(add(self.pos, mul(qrot(self.rot, (0, 0, 1)), tile_length / SUB)), mul(shift_dir, shift / SUB))
                self.addp(self.pos, "loop")
        self.rot = entry
        self.pos = (self.pos[0], y0, self.pos[2])
        self.loops.append((first, len(self.path) - 1))
        radius_along = tiles * tile_length / (2 * math.pi) + 10
        before, d = first, 0
        while before > 0 and d < radius_along:
            d += math.dist(self.path[before][0], self.path[before - 1][0])
            before -= 1
        self.close.append((before, len(self.path) + 15))
        self.dist += tiles * tile_length

    def barrel_roll(self, length=120.0, radius=9.0):
        first = len(self.path)
        heading = self.flat()
        fwd = qrot(heading, (0, 0, 1))
        axis = add(self.pos, (0, radius, 0))
        n = math.ceil(length / (10 / SUB))
        n += (4 - n % 4) % 4
        for k in range(1, n + 1):
            t = k / n
            e = t * t * (3 - 2 * t)
            turn = qaxis(fwd, 360 * e)
            p = add(add(axis, mul(fwd, length * t)), qrot(turn, (0, -radius, 0)))
            self.addp(p, "roll")
        self.pos = add(self.pos, mul(fwd, length))
        self.rot = heading
        self.loops.append((first, len(self.path) - 1))
        self.close.append((first - 10, len(self.path) + 10))
        self.dist += length

    def turntable(self, diameter):
        heading = self.flat()
        fwd = qrot(heading, (0, 0, 1))
        across = diameter - 2
        steps = math.ceil(across / (10 / SUB))
        for k in range(1, steps + 1):
            self.addp(add(self.pos, mul(fwd, across * k / steps)), "table")
        self.pos = add(self.pos, mul(fwd, across))
        self.rot = heading
        self.dist += across

    def reach_height(self, y):
        dy = y - self.pos[1]
        if abs(dy) < 1.5:
            return
        sign = -1 if dy > 0 else 1
        self.tiles(2, pitch=5 * sign)
        level_out = 10 * math.sin(math.radians(5))
        per_tile = 10 * math.sin(math.radians(10))
        while abs(y - self.pos[1]) > level_out + per_tile * 0.5:
            self.tiles(1)
        self.tiles(2, pitch=-5 * sign)

    def spur(self, start, heading_deg, build):
        saved = (self.pos, self.rot)
        self.pos, self.rot = start, euler(0, heading_deg, 0)
        self.off += 1
        build()
        self.off -= 1
        self.pos, self.rot = saved

    def mark(self, name, ahead=0.0, lateral=0.0):
        q = self.rot
        self.marks[name] = add(self.pos, qrot(q, (lateral, 0, ahead)))
        return self.marks[name]

    def side_point(self, lateral, ahead=0.0):
        return add(self.pos, qrot(self.flat(), (lateral, 0, ahead)))

    # ------------------------------------------------------------------ checks
    def clearance(self):
        pts = [p for p, _ in self.path]
        problems = 0
        for i in range(0, len(pts), 3):
            if problems >= 10:
                break
            for j in range(i + 60, len(pts), 3):
                a, c = pts[i], pts[j]
                if abs(a[1] - c[1]) >= 10:
                    continue
                if math.hypot(a[0] - c[0], a[2] - c[2]) >= 12:
                    continue
                if any(f <= i <= t and f <= j <= t for f, t in self.close):
                    continue
                print(f"  TOO CLOSE: point {i} {fmt(a)} and {j} {fmt(c)}")
                problems += 1
                break
        for f, t in self.loops:
            low = min(pts[i][1] for i in range(f, t + 1))
            high = max(pts[i][1] for i in range(f, t + 1))
            for j in range(0, len(pts), 2):
                if f - 60 <= j <= t + 60:
                    continue
                c = pts[j]
                if c[1] < low - 10 or c[1] > high + 10:
                    continue
                for i in range(f, t + 1, 2):
                    a = pts[i]
                    if math.hypot(a[0] - c[0], a[2] - c[2]) < 25:
                        print(f"  NEAR LOOP: point {j} {fmt(c)} within 25 m of loop point {i}")
                        problems += 1
                        break
                else:
                    continue
                break
        # spurs vs the line (sideways 12 m, unless the spur starts beside it on purpose: first 3 points)
        return problems

    def report(self, out):
        pts = [p for p, _ in self.path]
        ys = [p[1] for p in pts]
        print(f"{self.name}: {len(pts)} points, ~{self.dist:.0f} m, height {min(ys):.1f}..{max(ys):.1f}, end {fmt(self.pos)}")
        n = self.clearance()
        print("  clearance OK" if n == 0 else f"  {n} clearance problem(s)")
        self.plot(out)

    def plot(self, out):
        pts = [p for p, _ in self.path] + self.spurs + list(self.marks.values()) + [(0, 0, -80)]
        xs = [p[0] for p in pts]
        zs = [p[2] for p in pts]
        x0, x1, z0, z1 = min(xs) - 30, max(xs) + 30, min(zs) - 30, max(zs) + 30
        scale = 900 / max(x1 - x0, z1 - z0)
        W, H = int((x1 - x0) * scale) + 1, int((z1 - z0) * scale) + 1
        img = Image.new("RGB", (W, H + 220), (250, 250, 250))
        d = ImageDraw.Draw(img)
        def P(p): return ((p[0] - x0) * scale, H - (p[2] - z0) * scale)
        colours = {"": (60, 70, 140), "fork": (40, 150, 60), "loop": (200, 40, 40), "roll": (200, 40, 200),
                   "air": (180, 180, 180), "table": (240, 150, 0)}
        for i in range(1, len(self.path)):
            a, b = self.path[i - 1], self.path[i]
            if math.dist(a[0], b[0]) > 15:
                continue
            d.line([P(a[0]), P(b[0])], fill=colours.get(b[1], (60, 70, 140)), width=3)
        for p in self.spurs:
            x, y = P(p)
            d.ellipse([x - 1.5, y - 1.5, x + 1.5, y + 1.5], fill=(255, 120, 0))
        for name, p in self.marks.items():
            x, y = P(p)
            d.ellipse([x - 4, y - 4, x + 4, y + 4], outline=(0, 0, 0))
            d.text((x + 5, y - 5), name, fill=(0, 0, 0))
        x, y = P(self.path[0][0])
        d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=(0, 200, 0))
        x, y = P((0, 0, -80))
        d.rectangle([x - 4, y - 4, x + 4, y + 4], fill=(120, 0, 120))
        # height profile
        ys = [p[1] for p, _ in self.path]
        lo, hi = min(ys) - 5, max(ys) + 5
        for i, (p, _) in enumerate(self.path):
            px = i / len(self.path) * W
            py = H + 210 - (p[1] - lo) / (hi - lo) * 200
            d.point((px, py), fill=(0, 0, 0))
        d.text((5, H + 5), f"{self.name}  y {lo:.0f}..{hi:.0f}", fill=(0, 0, 0))
        img.save(out)


def fmt(p): return f"({p[0]:.0f}, {p[1]:.1f}, {p[2]:.0f})"


# ---------------------------------------------------------------------- JSON maps (JsonMapGenerator's steps)

def run_json(b, steps):
    """Runs a JSON map's course (or a fork lane) on a Sim: the same step meanings as JsonMapGenerator.cs."""
    for st in steps:
        if "straight" in st:
            b.tiles(st["straight"], width=st.get("width", 8.0), length=st.get("length", 10.0))
        elif "tiles" in st:
            n = st["tiles"]
            slope = st.get("slope", 0.0)
            b.tiles(n, yaw=st.get("turn", 0.0) / n, pitch=-slope / n if slope else 0.0,
                    width=st.get("width", 8.0), length=st.get("length", 10.0))
        elif "boostRun" in st:
            b.tiles(st["boostRun"])
        elif "loop" in st:
            lp = st["loop"] if isinstance(st["loop"], dict) else {}
            b.loop(lp.get("tiles", 32), lp.get("tileLength", 4.0), lp.get("width", 8.0))
        elif "barrelRoll" in st:
            r = st["barrelRoll"] if isinstance(st["barrelRoll"], dict) else {}
            b.barrel_roll(r.get("length", 120.0), r.get("radius", 9.0))
        elif "jump" in st:
            j = st["jump"]
            kt, lw, ll, land = j.get("kickerTiles", 2), j.get("landingWidth", 16.0), j.get("landingLength", 12.0), j.get("landing", 12.0)
            b.tiles(kt, pitch=-j.get("kicker", 12.0) / kt, length=j.get("kickerLength", 6.0))
            b.gap(j["gap"], j["drop"])
            b.tiles(1, pitch=land, width=lw, length=ll)
            if j.get("landingFlat", 2):
                b.tiles(j.get("landingFlat", 2), width=lw, length=ll)
            b.tiles(1, pitch=-land, width=lw, length=ll)
        elif "gap" in st:
            b.gap(st["gap"]["forward"], st["gap"].get("drop", 0.0))
        elif "reachHeight" in st:
            b.reach_height(st["reachHeight"])
        elif "fork" in st:
            f = st["fork"]
            b.fork(f.get("spread", 9.0), lambda: run_json(b, f["left"]), lambda: run_json(b, f["right"]))
        # section, surface, season, boost, finish: nothing on the centre line


def main(argv):
    import json
    if len(argv) < 2 or not argv[1].lower().endswith(".json"):
        sys.exit("python tools/track_sim.py <map.json> [plot.png]")
    data = json.load(open(argv[1], encoding="utf-8"))
    b = Sim(data["map"]["id"])
    run_json(b, data["course"])
    b.report(argv[2] if len(argv) > 2 else os.path.splitext(argv[1])[0] + "-layout.png")


if __name__ == "__main__":
    import os
    import sys
    main(sys.argv)
