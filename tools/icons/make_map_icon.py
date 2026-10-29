"""Draws a 256x256 Thunderstore icon for a map with a two-line title.

Styles: "loop" (default) – a loop on a night-sky gradient (MegaStunt);
        "twin" – a road forking into a tunnel lane and an open boost/mud lane, city at dusk (TwinTunnels).

Usage: python make_map_icon.py <PIL path or ""> <output.png> [LINE1] [LINE2] [--style loop|twin]
       (default title: MEGA / STUNT)
"""
import math
import random
import sys

args = sys.argv[1:]
STYLE = "loop"
if "--style" in args:
    i = args.index("--style")
    STYLE = args[i + 1]
    del args[i:i + 2]

if args[0]:
    sys.path.insert(0, args[0])
from PIL import Image, ImageDraw, ImageFont, ImageFilter

SIZE = 256
SS = 4                      # supersampling
W = SIZE * SS
OUT = args[1]
LINE1 = args[2] if len(args) > 2 else "MEGA"
LINE2 = args[3] if len(args) > 3 else "STUNT"
FONT = "C:/Windows/Fonts/seguibl.ttf"


def title(draw, top):
    """Two-line title: LINE1 in yellow, LINE2 in white, both with a dark outline, starting at y = top."""
    def fit(txt, size):
        # Shrink long titles so they stay inside the icon.
        font = ImageFont.truetype(FONT, size)
        while size > 20 and draw.textbbox((0, 0), txt, font=font)[2] > W * 0.92:
            size -= 4
            font = ImageFont.truetype(FONT, size)
        return size

    def centered(txt, y, size, fill):
        font = ImageFont.truetype(FONT, size)
        box = draw.textbbox((0, 0), txt, font=font, stroke_width=size // 9)
        x = (W - (box[2] - box[0])) / 2 - box[0]
        draw.text((x, y), txt, font=font, fill=fill, stroke_width=size // 9, stroke_fill=(20, 16, 44))

    centered(LINE1, top, fit(LINE1, int(W * 0.15)), (255, 205, 20))
    centered(LINE2, top + int(W * 0.14), fit(LINE2, int(W * 0.15)), (255, 255, 255))


def draw_twin():
    """Perspective view down a road that forks: left lane into a tunnel, right lane open with boost pads and mud."""
    HORIZON = W * 0.45          # screen y of the horizon
    CAM_H = 8.5                 # camera height above the road (m)
    Z_NEAR = 6.0                # road distance at the bottom edge (m)
    F = (W - HORIZON) * Z_NEAR / CAM_H
    SPREAD = 7.5                # lane centre offset after the fork
    FORK0, FORK1 = 9.0, 18.0   # fork starts / lanes fully apart
    PORTAL = 21.0               # tunnel entrances distance
    FAR = 160.0
    HALF, EDGE = 4.0, 1.0       # half asphalt width, concrete border
    SKY_TOP, SKY_LOW = (20, 32, 78), (255, 146, 92)
    ASPHALT, CONCRETE = (62, 64, 80), (196, 200, 212)
    MUD, MUD_RUT = (128, 86, 50), (96, 62, 36)
    BOOST_A, BOOST_B = (255, 205, 20), (255, 110, 0)

    def screen(x, y, z):
        return (W / 2 + x * F / z, HORIZON + (CAM_H - y) * F / z)

    def fogged(col, z):
        t = min(1.0, max(0.0, (z - 25) / (FAR - 25))) ** 0.8
        return tuple(int(c + (SKY_LOW[k] * 0.75 - c) * t) for k, c in enumerate(col))

    def lane_x(side, z):
        if z <= FORK0:
            return 0.0
        t = min(1.0, (z - FORK0) / (FORK1 - FORK0))
        return side * SPREAD * t * t * (3 - 2 * t)

    img = Image.new("RGB", (W, W))
    px = img.load()
    for y in range(W):
        t = min(1.0, y / HORIZON)
        row = tuple(int(SKY_TOP[k] + (SKY_LOW[k] - SKY_TOP[k]) * t ** 1.6) for k in range(3))
        if y >= HORIZON:
            g = (y - HORIZON) / (W - HORIZON)
            row = tuple(int(c) for c in (40 + 30 * (1 - g), 42 + 26 * (1 - g), 58 + 20 * (1 - g)))
        for x in range(W):
            px[x, y] = row
    draw = ImageDraw.Draw(img)

    # Sun and city skyline on the horizon.
    sun = Image.new("L", (W, W), 0)
    ImageDraw.Draw(sun).ellipse([W * 0.62, HORIZON - W * 0.16, W * 0.86, HORIZON + W * 0.08], fill=200)
    sun = sun.filter(ImageFilter.GaussianBlur(W * 0.02))
    img = Image.composite(Image.new("RGB", (W, W), (255, 214, 140)), img, sun)
    draw = ImageDraw.Draw(img)
    random.seed(3)
    for layer, (col, hmin, hmax) in enumerate([((120, 84, 120), 0.05, 0.12), ((58, 46, 86), 0.03, 0.10)]):
        x = -random.randrange(int(W * 0.05))
        while x < W:
            bw = random.randrange(int(W * 0.04), int(W * 0.09))
            bh = random.uniform(hmin, hmax) * W
            draw.rectangle([x, HORIZON - bh, x + bw, HORIZON + 2], fill=col)
            if layer == 1:
                for wy in range(int(HORIZON - bh + 10), int(HORIZON - 6), 18):
                    for wx in range(x + 8, x + bw - 8, 16):
                        if random.random() < 0.35:
                            draw.rectangle([wx, wy, wx + 6, wy + 8], fill=(255, 210, 120))
            x += bw + random.randrange(0, int(W * 0.02))

    # Road: borders first, then the surface, so the shared trunk has no inner borders.
    # Left lane: asphalt with boost pads; right lane: mud with tyre ruts. Both run into a tunnel at PORTAL.
    zs = [Z_NEAR * 0.9 + (PORTAL + 1 - Z_NEAR * 0.9) * (i / 300) ** 1.6 for i in range(301)]
    for layer in ("border", "top"):
        for side in (1, -1):                    # left lane last: its asphalt cuts a clean edge into the mud
            for z0, z1 in zip(zs, zs[1:]):
                c0, c1 = lane_x(side, z0), lane_x(side, z1)
                if layer == "border":
                    w, col = HALF + EDGE, CONCRETE
                else:
                    w, col = HALF, ASPHALT
                    if side < 0 and FORK1 - 4 < z0 < PORTAL - 1:
                        col = BOOST_A if int(z0 * 1.2) % 2 == 0 else BOOST_B
                    if side > 0 and z0 > FORK0 + 1:
                        col = MUD
                poly = [screen(c0 - w, 0, z0), screen(c0 + w, 0, z0), screen(c1 + w, 0, z1), screen(c1 - w, 0, z1)]
                draw.polygon(poly, fill=fogged(col, z0))
                if layer == "top" and col == MUD:       # tyre ruts in the mud
                    for r in (-1.7, 1.7):
                        rut = [screen(c0 + r - 0.3, 0, z0), screen(c0 + r + 0.3, 0, z0),
                               screen(c1 + r + 0.3, 0, z1), screen(c1 + r - 0.3, 0, z1)]
                        draw.polygon(rut, fill=fogged(MUD_RUT, z0))
    # Trunk: one wide asphalt road with a dashed centre line.
    for z0, z1 in zip(zs, zs[1:]):
        if z0 > FORK0:
            break
        draw.polygon([screen(-HALF - 0.5, 0, z0), screen(HALF + 0.5, 0, z0),
                      screen(HALF + 0.5, 0, z1), screen(-HALF - 0.5, 0, z1)], fill=ASPHALT)
    for z in (5.6, 7.6, 9.6):
        draw.polygon([screen(-0.2, 0, z), screen(0.2, 0, z), screen(0.2, 0, z + 1.0), screen(-0.2, 0, z + 1.0)],
                     fill=(235, 235, 240))

    # Twin tunnels: roof and inner side wall receding, then the portal with an arched opening.
    TH, TW = 7.0, HALF + EDGE + 0.8
    for side in (-1, 1):
        cx = side * SPREAD
        inner = cx - side * TW
        draw.polygon([screen(inner, 0, PORTAL), screen(inner, TH, PORTAL), screen(inner, TH, FAR), screen(inner, 0, FAR)],
                     fill=fogged((132, 138, 160), 50))
        draw.polygon([screen(cx - TW, TH, PORTAL), screen(cx + TW, TH, PORTAL), screen(cx + TW, TH, FAR),
                      screen(cx - TW, TH, FAR)], fill=fogged((176, 182, 200), 50))
        for z in range(int(PORTAL) + 5, 120, 8):         # light strips on the wall
            draw.line([screen(inner, TH * 0.5, z), screen(inner, TH * 0.5, z + 3)],
                      fill=fogged((255, 190, 90), z), width=max(3, int(F / z * 0.35)))
        draw.polygon([screen(cx - TW, 0, PORTAL), screen(cx + TW, 0, PORTAL), screen(cx + TW, TH, PORTAL),
                      screen(cx - TW, TH, PORTAL)], fill=(214, 218, 230))
        # hazard stripe along the top of the portal
        y_top, y_band = screen(0, TH, PORTAL)[1], screen(0, TH - 1.0, PORTAL)[1]
        xl, xr = screen(cx - TW, 0, PORTAL)[0], screen(cx + TW, 0, PORTAL)[0]
        step = (xr - xl) / 7
        for k in range(7):
            draw.polygon([(xl + k * step, y_band), (xl + (k + 0.5) * step, y_top), (xl + (k + 1) * step, y_top),
                          (xl + (k + 0.5) * step, y_band)], fill=BOOST_A)
        draw.line([(xl, y_band), (xr, y_band)], fill=(40, 40, 50), width=5)
        # arched opening: dark inside, light at the far end
        rpx = 3.3 * F / PORTAL                           # opening half width = arch radius
        ox, oy0 = screen(cx, 0, PORTAL)
        oy1 = oy0 - (TH - 1.5) * F / PORTAL + rpx        # arch centre: opening top 1.5 m under the portal top
        hole = Image.new("L", (W, W), 0)
        hd = ImageDraw.Draw(hole)
        hd.rectangle([ox - rpx, oy1, ox + rpx, oy0], fill=255)
        hd.ellipse([ox - rpx, oy1 - rpx, ox + rpx, oy1 + rpx], fill=255)
        hd.rectangle([0, oy0, W, W], fill=0)            # nothing below the road
        inside = Image.new("RGB", (W, W), (14, 14, 24))
        idr = ImageDraw.Draw(inside)
        fx, fy = ox, oy0 - rpx * 0.9
        for k in range(4):                              # ceiling lights receding to the exit
            t = 0.25 + 0.2 * k
            ly = (oy1 - rpx * 0.7) + (fy - (oy1 - rpx * 0.7)) * t
            lw = rpx * 0.5 * (1 - t) + 2
            idr.rectangle([fx - lw, ly - lw * 0.18, fx + lw, ly + lw * 0.18], fill=(255, 196, 110))
        idr.ellipse([fx - rpx * 0.14, fy - rpx * 0.1, fx + rpx * 0.14, fy + rpx * 0.1], fill=(255, 232, 180))
        lane = ASPHALT if side < 0 else MUD
        idr.polygon([(ox - rpx, oy0), (ox + rpx, oy0), (fx + rpx * 0.08, fy), (fx - rpx * 0.08, fy)],
                    fill=tuple(int(c * 0.6) for c in lane))
        img = Image.composite(inside, img, hole)
        draw = ImageDraw.Draw(img)

    title(draw, int(W * 0.03))
    img.resize((SIZE, SIZE), Image.LANCZOS).save(OUT)
    print("icon written:", OUT)


if STYLE == "twin":
    draw_twin()
    sys.exit(0)

ROAD = (58, 56, 92)
ROAD_UNDER = (34, 32, 58)
BORDER = (178, 190, 228)
BOOST_A = (255, 205, 20)
BOOST_B = (255, 110, 0)

# ---------------------------------------------------------------- track geometry (world units)
R = 20.0          # loop radius
HALF = 5.0        # half asphalt width
EDGE = 1.2        # concrete border width
SHIFT = 14.0      # sideways shift so the exit clears the entry


def path(t):
    """Centre line: approach straight, full loop, exit straight. Returns position and tangent."""
    if t < 0:                                   # approach, t in [-1, 0)
        z = 45.0 * t
        return (0.0, 0.0, z), (0.0, 0.0, 1.0)
    if t > 1:                                   # exit, t in (1, 2]
        z = 45.0 * (t - 1)
        return (SHIFT, 0.0, z), (0.0, 0.0, 1.0)
    a = 2 * math.pi * t
    p = (SHIFT * t, R * (1 - math.cos(a)), R * math.sin(a))
    d = (SHIFT / (2 * math.pi * R), math.sin(a), math.cos(a))
    n = math.sqrt(sum(c * c for c in d))
    return p, tuple(c / n for c in d)


def sub(a, b): return tuple(x - y for x, y in zip(a, b))
def add(a, b): return tuple(x + y for x, y in zip(a, b))
def mul(a, s): return tuple(x * s for x in a)
def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def norm(a):
    n = math.sqrt(dot(a, a)) or 1.0
    return mul(a, 1.0 / n)


# ---------------------------------------------------------------- camera (orthographic)
yaw, pitch = math.radians(-38), math.radians(14)
cam_right = (math.cos(yaw), 0.0, -math.sin(yaw))
cam_fwd = (math.sin(yaw) * math.cos(pitch), -math.sin(pitch), math.cos(yaw) * math.cos(pitch))
cam_up = cross(cam_fwd, cam_right)
LIGHT = norm((-0.4, 1.0, -0.6))

# Build quads: (depth, polygon points, colour)
quads = []
samples = [-1 + i / 12 for i in range(12)] + [i / 160 for i in range(161)] + [1 + i / 12 for i in range(1, 13)]
right = (1.0, 0.0, 0.0)
for i in range(len(samples) - 1):
    (p0, d0), (p1, d1) = path(samples[i]), path(samples[i + 1])
    up0, up1 = norm(cross(d0, right)), norm(cross(d1, right))
    # cross(d, right) points up on the approach; flip so "up" is the driving side.
    if up0[1] < 0 and samples[i] < 0: up0 = mul(up0, -1)
    up0, up1 = mul(up0, -1), mul(up1, -1)
    strips = [(-HALF - EDGE, -HALF, BORDER), (-HALF, HALF, ROAD), (HALF, HALF + EDGE, BORDER)]
    for lo, hi, col in strips:
        a = add(p0, mul(right, lo)); b = add(p0, mul(right, hi))
        c = add(p1, mul(right, hi)); d = add(p1, mul(right, lo))
        centre = mul(add(add(a, b), add(c, d)), 0.25)
        normal = norm(add(up0, up1))
        facing = dot(normal, cam_fwd) < 0          # driving surface faces the camera
        base = col if facing else (ROAD_UNDER if col == ROAD else (120, 128, 160))
        shade = 0.55 + 0.45 * abs(dot(normal, LIGHT))
        colour = tuple(int(min(255, ch * shade)) for ch in base)
        # Boost chevrons on the approach just before the loop.
        if col == ROAD and -0.35 < samples[i] < -0.05:
            colour = BOOST_A if int((samples[i] + 1) * 36) % 2 == 0 else BOOST_B
        quads.append((dot(centre, cam_fwd), [a, b, c, d], colour))

# Fit to the canvas.
def proj(p): return (dot(p, cam_right), -dot(p, cam_up))
pts = [proj(p) for _, poly, _ in quads for p in poly]
minx, maxx = min(p[0] for p in pts), max(p[0] for p in pts)
miny, maxy = min(p[1] for p in pts), max(p[1] for p in pts)
scale = min(W * 0.86 / (maxx - minx), W * 0.62 / (maxy - miny))
ox = W / 2 - (minx + maxx) / 2 * scale
oy = W * 0.42 - (miny + maxy) / 2 * scale
def screen(p):
    x, y = proj(p)
    return (x * scale + ox, y * scale + oy)

# ---------------------------------------------------------------- background
img = Image.new("RGB", (W, W))
top, bottom = (26, 22, 58), (92, 70, 150)
px = img.load()
for y in range(W):
    t = y / (W - 1)
    row = tuple(int(top[k] + (bottom[k] - top[k]) * t) for k in range(3))
    for x in range(W):
        px[x, y] = row
draw = ImageDraw.Draw(img)
# a few stars
import random
random.seed(7)
for _ in range(60):
    x, y = random.randrange(W), random.randrange(int(W * 0.55))
    r = random.choice([2, 3, 4])
    draw.ellipse([x - r, y - r, x + r, y + r], fill=(220, 225, 255))

# soft glow behind the loop
glow = Image.new("L", (W, W), 0)
gd = ImageDraw.Draw(glow)
cx, cy = screen((SHIFT / 2, R, 0))
gr = R * scale * 1.5
gd.ellipse([cx - gr, cy - gr, cx + gr, cy + gr], fill=110)
glow = glow.filter(ImageFilter.GaussianBlur(W * 0.06))
img = Image.composite(Image.new("RGB", (W, W), (150, 140, 255)), img, glow)
draw = ImageDraw.Draw(img)

# shadow on an imaginary ground
for _, poly, _ in quads:
    shadow = [screen((p[0] + 6, -2.0, p[2] + 4)) for p in poly]
    draw.polygon(shadow, fill=(30, 24, 60))

# track, far to near
for _, poly, colour in sorted(quads, key=lambda q: -q[0]):
    draw.polygon([screen(p) for p in poly], fill=colour)

# ---------------------------------------------------------------- title
def text_centered(txt, y, size, fill):
    font = ImageFont.truetype("C:/Windows/Fonts/seguibl.ttf", size)
    box = draw.textbbox((0, 0), txt, font=font, stroke_width=size // 9)
    x = (W - (box[2] - box[0])) / 2 - box[0]
    draw.text((x, y), txt, font=font, fill=fill, stroke_width=size // 9, stroke_fill=(20, 16, 44))

def fit(txt, size):
    # Shrink long titles so they stay inside the icon.
    font = ImageFont.truetype("C:/Windows/Fonts/seguibl.ttf", size)
    while size > 20 and draw.textbbox((0, 0), txt, font=font)[2] > W * 0.92:
        size -= 4
        font = ImageFont.truetype("C:/Windows/Fonts/seguibl.ttf", size)
    return size

text_centered(LINE1, int(W * 0.66), fit(LINE1, int(W * 0.15)), (255, 205, 20))
text_centered(LINE2, int(W * 0.80), fit(LINE2, int(W * 0.15)), (255, 255, 255))

img.resize((SIZE, SIZE), Image.LANCZOS).save(OUT)
print("icon written:", OUT)
