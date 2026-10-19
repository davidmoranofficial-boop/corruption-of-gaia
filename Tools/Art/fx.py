"""Effect sprites: little animations drawn over the world and over battles, plus the soft lights and shadows.

    python3 Tools/Art/fx.py         (also run by build.py; writes Sprites/Fx/)

Every effect is a sheet: its frames side by side in one row, all the same size, so the game cuts it up by
frame count. Like the rest of the art they use only palette colours and no blending: fading out is done by
dithering (dropping pixels in an ordered 4 x 4 pattern), never by making pixels see-through. The exceptions
are the soft lights and shadows at the bottom of this file (glows, cloud shadow, vignette, bird shadow):
those hold a few fixed alpha steps, with dithered rings between the steps.

World effects are drawn at tile scale (16 pixels a tile). Battle effects are drawn over 64-ish pixel beasts.
"""
import math, os, random
from PIL import Image
from palette import PAL

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'Assets', 'Game', 'Resources', 'Sprites', 'Fx')

BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def dith(x, y, d):
    """True for about `d` (0..1) of pixels, in an ordered dither pattern."""
    return BAYER[int(y) & 3][int(x) & 3] + 0.5 < d * 16


def rgb(k):
    return tuple(int(PAL[k][i:i + 2], 16) for i in (1, 3, 5))


class Frame:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.k = [[None] * w for _ in range(h)]
        self.a = [[255] * w for _ in range(h)]

    def put(self, x, y, k, a=255):
        x, y = int(math.floor(x)), int(math.floor(y))
        if 0 <= x < self.w and 0 <= y < self.h:
            self.k[y][x], self.a[y][x] = k, a

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h: return self.k[y][x]
        return None

    def grid(self, rows, ox=0, oy=0):
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch != '.': self.put(ox + x, oy + y, ch)
        return self

    def image(self):
        img = Image.new('RGBA', (self.w, self.h), (0, 0, 0, 0))
        px = img.load()
        for y in range(self.h):
            for x in range(self.w):
                if self.k[y][x] and self.a[y][x]: px[x, y] = rgb(self.k[y][x]) + (self.a[y][x],)
        return img


def sheet(frames):
    w, h = frames[0].w, frames[0].h
    img = Image.new('RGBA', (w * len(frames), h), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        img.paste(f.image(), (i * w, 0))
    return img


def line(f, x0, y0, x1, y1, k, d=1.0):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for i in range(n + 1):
        t = i / n
        x, y = round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t)
        if d >= 1 or dith(x, y, d): f.put(x, y, k)


def noise2(seed):
    """A cheap smooth 2D noise function (sum of a few rotated sine waves), about -1..1."""
    rnd = random.Random(seed)
    waves = [(rnd.uniform(0, 6.28), rnd.uniform(0.25, 0.6), rnd.uniform(0, 6.28)) for _ in range(5)]

    def n(x, y):
        s = 0
        for ang, fr, ph in waves:
            s += math.sin((x * math.cos(ang) + y * math.sin(ang)) * fr + ph)
        return s / 2.6
    return n


# ============================================================================================== world effects

def puffs(w, h, steps, light, mid, dark):
    """Round puffs (dust, smoke): each step is (list of (cx, cy, r), density). Lit from the top left."""
    frames = []
    for blobs, d in steps:
        f = Frame(w, h)
        inside = lambda x, y: any((x + .5 - cx) ** 2 + (y + .5 - cy) ** 2 <= r * r for cx, cy, r in blobs)
        for y in range(h):
            for x in range(w):
                if not inside(x, y) or not dith(x, y, d): continue
                if not inside(x - 1, y - 1): k = light
                elif not inside(x + 1, y + 1): k = dark
                else: k = mid
                f.put(x, y, k)
        frames.append(f)
    return frames


def fx_dust():
    # small enough that every pixel is placed by hand: z lit top, d body, D shade
    return [Frame(8, 8).grid(g) for g in (
        ["........", "........", "........", "........", "........", "...zd...", "..zddD..", "...DD...",],
        ["........", "........", "........", "..zz.z..", ".zddzdd.", ".dddddD.", "..DDdDD.", "........",],
        ["........", "..z..z..", ".zdz.dz.", "zd.dd.dD", ".dD.d.D.", "..D..D..", "........", "........",],
        [".z....z.", "........", "z..d.d..", "...D...d", ".d....D.", "........", "........", "........",],
    )]


def fx_smoke():
    return [Frame(8, 8).grid(g) for g in (
        ["........", "........", "........", "........", "...WW...", "..WYYy..", "..YYyy..", "...yy...",],
        ["........", "........", "...WW...", "..WYYW..", ".WYYYYy.", ".YYyYyy.", "..yyyy..", "........",],
        ["..W.W...", ".WY.YW..", "W.YY.Yy.", ".Y.Y.y..", "..y.y...", "........", "........", "........",],
        [".W...W..", "...Y....", ".Y...y..", "....y...", "........", "........", "........", "........",],
    )]


def fx_splash():
    return [Frame(12, 8).grid(g) for g in (
        ["............", "............", "............", "............",
         ".....WW.....", ".....cW.....", "....cWWc....", "...C.cc.C...",],
        ["............", "...W....W...", "...c.WW.c...", "....c..c....",
         "...Wc..cW...", "..cc....cc..", ".C..cccc..C.", "............",],
        ["............", ".W........W.", ".c........c.", "..W......W..",
         "....W..W....", "....c..c....", "W..........W", "cC.cccccc.Cc",],
        ["............", "............", "............", "W..........W",
         "c..........c", "............", "............", ".C.c.c.c.c.C",],
    )]


def fx_leaf():
    return [Frame(4, 4).grid(g) for g in (
        ["...A", ".fiF", "fiF.", "iF..",],
        ["....", "Aff.", ".iFF", "....",],
        ["F...", "Fi..", ".iiA", "..F.",],
        [".A..", ".f..", ".i..", ".F..",],
    )]


def fx_firefly():
    return [Frame(3, 3).grid([".a.", "aWa", ".a."]), Frame(3, 3).grid(["...", ".a.", "..."])]


def fx_ember():
    return [Frame(2, 2).grid(g) for g in (["aa", "rr"], ["a.", ".r"], ["..", "R."])]


def fx_ripple():
    frames = []
    for t, (rx, ry) in enumerate(((3.2, 1.3), (5.4, 2.3), (7.4, 3.3))):
        f = Frame(16, 8)
        for i in range(96):
            a = i / 96 * 6.283
            x, y = 7.5 + rx * math.cos(a), 3.5 + ry * math.sin(a)
            front = math.sin(a) > 0
            if t == 2 and (int(x) + int(y)) % 2: continue
            f.put(x + .5, y + .5, ('W' if t == 0 and front and abs(math.cos(a)) < .5 else 'c'))
        frames.append(f)
    return frames


def fx_glint():
    frames = []
    for arms in (1, 3, 2, 0):
        f = Frame(7, 7)
        cols = {1: ['W', 'c'], 2: ['W', 'Y', 'c'], 3: ['W', 'W', 'Y', 'c']}.get(arms, [])
        f.put(3, 3, 'W' if arms else 'c')
        for d, k in enumerate(cols[1:], 1):
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                f.put(3 + dx * d, 3 + dy * d, k)
        if arms >= 3:
            for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1)): f.put(3 + dx, 3 + dy, 'c')
        frames.append(f)
    return frames


def fx_bird():
    # a gull from above, head to the top: wings level and wide, then lifted (they look shorter from above)
    return [Frame(9, 5).grid(g) for g in (
        ["....W....",
         "gYWWWWWYg",
         "..YYWYY..",
         "....W....",
         "....Y....",],
        ["....W....",
         "...WWW...",
         ".gYWWWYg.",
         "....W....",
         "....Y....",],
    )]


# ============================================================================================== soft lights and shadows

def fx_shadow_bird():
    f = Frame(9, 4)
    for y in range(4):
        for x in range(9):
            d = ((x + .5 - 4.5) / 4.5) ** 2 + ((y + .5 - 2) / 2) ** 2
            if d <= .45: f.put(x, y, 'e', 110)
            elif d <= 1.0 and dith(x, y, .5 if d > .75 else .99): f.put(x, y, 'e', 70)
    return [f]


def glow(colours, alphas, size=48):
    """Soft round light: rings of alpha steps from the middle out, dithered where one step meets the next."""
    f = Frame(size, size)
    c = size / 2
    n = len(alphas)
    for y in range(size):
        for x in range(size):
            r = math.hypot(x + .5 - c, y + .5 - c) / c
            if r >= 1: continue
            lv = (1 - r) * n                 # 0 at the edge .. n in the middle
            i = int(lv)
            if dith(x, y, lv - i): i += 1
            i = min(i, n)
            if i == 0: continue
            f.put(x, y, colours[i - 1], alphas[i - 1])
    return [f]


def glow_warm():
    return glow(['r', 'A', 'a', 'a', 'a'], [22, 50, 82, 112, 140])


def glow_cool():
    return glow(['C', 'c', 'c', 'c', 'c'], [22, 50, 82, 112, 140])


def cloud_shadow():
    w, h = 96, 64
    f = Frame(w, h)
    rnd = random.Random(11)
    blobs = [(48, 32, 30, 18)] + [(rnd.uniform(22, 74), rnd.uniform(20, 44), rnd.uniform(10, 18), rnd.uniform(8, 13)) for _ in range(7)]
    nz = noise2(5)
    for y in range(h):
        for x in range(w):
            v = max(1 - ((x - bx) / rx) ** 2 - ((y - by) / ry) ** 2 for bx, by, rx, ry in blobs)
            v += 0.16 * nz(x * 0.6, y * 0.6)
            if v > 0.28: f.put(x, y, 'e', 56)
            elif v > 0.12 and dith(x, y, (v - 0.12) / 0.16): f.put(x, y, 'e', 56)
            elif v > 0.0 and dith(x, y, .2): f.put(x, y, 'e', 32)
    return [f]


def vignette():
    w, h = 160, 90
    f = Frame(w, h)
    alphas = [40, 80, 125, 170]
    for y in range(h):
        for x in range(w):
            dx, dy = (x + .5 - w / 2) / (w / 2), (y + .5 - h / 2) / (h / 2)
            r = math.sqrt(dx * dx * 0.8 + dy * dy * 0.75)
            lv = (r - 0.55) / 0.25 * len(alphas) / 2.2
            if lv <= 0: continue
            i = int(lv)
            if dith(x, y, lv - i): i += 1
            i = min(i, len(alphas))
            if i: f.put(x, y, 'e', alphas[i - 1])
    return [f]


# ============================================================================================== battle effects

def crescent(f, cx, cy, R, a0, a1, head, tail, thick, cols, fade=1.0):
    """A slash: a crescent along the circle (cx, cy, R) from angle a0 to a1 (radians), drawn only between
    tail and head (0..1 along it). Sharp outer edge, thickest in the middle. cols: outer edge to inner edge."""
    for y in range(f.h):
        for x in range(f.w):
            dx, dy = x + .5 - cx, y + .5 - cy
            r = math.hypot(dx, dy)
            a = math.atan2(dy, dx)
            u = (a - a0) / (a1 - a0)
            if not (tail <= u <= head): continue
            span = max(head - tail, 1e-3)
            # thickness grows from the tail to just behind the head, then a sharp point
            p = (u - tail) / span
            th = thick * (math.sin(min(p, 0.999) * math.pi) ** 0.6) * (0.6 + 0.4 * min(1, p * 2))
            depth = R - r
            if depth < 0 or depth > th: continue
            q = depth / max(th, 1e-3)
            d = fade * (0.35 + 0.65 * min(1, p * 2.5))       # the tail end is dithered away
            if not dith(x, y, d): continue
            k = cols[min(len(cols) - 1, int(q * len(cols)))]
            f.put(x, y, k)


def fx_slash():
    frames = []
    s, e = math.radians(-8), math.radians(98)
    for head, tail, fade in ((0.45, 0.0, 1), (1.0, 0.05, 1), (1.0, 0.45, .85), (1.0, 0.75, .4)):
        f = Frame(48, 48)
        crescent(f, 5, 5, 40, s, e, head, tail, 7 if head < 1 else 9, ['W', 'W', 'Y', 'Y', 'y'], fade)
        if tail == 0.05:
            # a few sparks thrown off the leading edge
            for x, y in ((40, 8), (42, 12), (38, 4)): f.put(x, y, 'W')
        frames.append(f)
    return frames


def sparkle(f, x, y, size, cols=('W', 'Y', 'a')):
    f.put(x, y, cols[0])
    for d in range(1, size + 1):
        k = cols[min(len(cols) - 1, d)]
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)): f.put(x + dx * d, y + dy * d, k)


def fx_slash_crit():
    frames = []
    s1, e1 = math.radians(-8), math.radians(98)       # top right -> bottom left
    s2, e2 = math.radians(172), math.radians(82)      # top left -> bottom right, about the other corner
    plan = [((0.5, 0, 1), None, 0),
            ((1, 0.05, 1), (0.5, 0, 1), 0),
            ((1, 0.4, .9), (1, 0.05, 1), 4),
            ((1, 0.7, .55), (1, 0.4, .85), 6),
            ((1, 0.9, .25), (1, 0.75, .35), 2)]
    cols = ['W', 'W', 'Y', 'a', 'a', 'A']
    for one, two, spark in plan:
        f = Frame(64, 64)
        crescent(f, 6, 6, 54, s1, e1, one[0], one[1], 11, cols, one[2])
        if two:
            crescent(f, 58, 6, 54, s2, e2, two[0], two[1], 11, cols, two[2])
        if spark:
            sparkle(f, 32, 44, spark, ('W', 'W', 'Y', 'a', 'a', 'A', 'A'))
            if spark >= 4:
                for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
                    for d in range(1, spark // 2 + 1): f.put(32 + dx * d, 44 + dy * d, 'a' if d < 2 else 'A')
        frames.append(f)
    return frames


def burst(f, cx, cy, spikes, r_in, r_out, cols, rot=0.0, d=1.0, hollow=0.0):
    """A star: a core of radius r_in with `spikes` points reaching r_out. cols from the middle out."""
    for y in range(f.h):
        for x in range(f.w):
            dx, dy = x + .5 - cx, y + .5 - cy
            r = math.hypot(dx, dy)
            a = math.atan2(dy, dx) + rot
            spike = abs(math.cos(a * spikes / 2)) ** 6
            edge = r_in + (r_out - r_in) * spike
            if r > edge or r < hollow * edge: continue
            if d < 1 and not dith(x, y, d): continue
            q = r / max(edge, 1e-3)
            f.put(x, y, cols[min(len(cols) - 1, int(q * len(cols)))])


def fx_hit():
    frames = []
    f = Frame(32, 32); burst(f, 16, 16, 4, 3, 7, ['W', 'W', 'Y']); frames.append(f)
    f = Frame(32, 32); burst(f, 16, 16, 8, 6, 15, ['W', 'W', 'Y', 'a'], rot=0.0); burst(f, 16, 16, 4, 2, 6, ['W']); frames.append(f)
    f = Frame(32, 32); burst(f, 16, 16, 8, 8, 15, ['Y', 'a', 'a', 'A'], rot=0.39, hollow=.55); frames.append(f)
    f = Frame(32, 32)
    for i in range(8):
        a = i / 8 * 6.283 + 0.39
        for rr in (12, 13):
            f.put(16 + math.cos(a) * rr, 16 + math.sin(a) * rr, 'a' if rr == 12 else 'A')
    frames.append(f)
    return frames


def claw(f, x0, y0, x1, y1, head, width, d=1.0, drip=0):
    """One claw rake: a slightly bent streak from (x0, y0) to (x1, y1), drawn up to `head` (0..1)."""
    n = 60
    for i in range(int(n * head) + 1):
        t = i / n
        bend = math.sin(t * math.pi) * 2.2
        x = x0 + (x1 - x0) * t + bend
        y = y0 + (y1 - y0) * t - bend * 0.4
        th = width * math.sin(min(t / max(head, .01), 1) * math.pi * 0.95 + 0.05)
        for w in range(-2, 3):
            if abs(w) > th: continue
            k = 'r' if abs(w) < th * 0.4 else 'R'
            if i == int(n * head) and head < 1: k = 'W'
            px, py = x + w * 0.7, y - w * 0.7
            if d >= 1 or dith(px, py, d): f.put(px, py, k)
    for j in range(drip):
        f.put(x0 + (x1 - x0) * 0.55 + 2, y0 + (y1 - y0) * 0.55 + j + 1, 'm' if j else 'R')


def fx_bite():
    frames = []
    rakes = [(9, 3, 25, 26), (14, 2, 29, 23), (4, 7, 20, 29)]
    for head, d, drip in ((0.45, 1, 0), (1, 1, 0), (1, 0.8, 2), (1, 0.35, 3)):
        f = Frame(32, 32)
        for (x0, y0, x1, y1) in rakes:
            claw(f, x0, y0, x1, y1, head, 2.2 if head == 1 else 1.6, d, drip)
        frames.append(f)
    return frames


def flame(f, cx, cy, R, t, cols, seed, lift=0.6, d=1.0, hollow=0.0):
    """A ball of fire: a round heat field stretched upwards, roughened by moving noise, cut into colour rings."""
    nz = noise2(seed)
    for y in range(f.h):
        for x in range(f.w):
            dx, dy = x + .5 - cx, y + .5 - cy
            if dy < 0: dy *= (1 - lift)                    # flames reach further up than down
            r = math.hypot(dx, dy) / R
            r += 0.2 * nz(x * 0.9, y * 0.9 + t * 3.0) * min(1, r + 0.3)
            if y + .5 < cy:                                # tongues licking upwards
                r += 0.32 * nz(x * 1.7 + 40, y * 0.35 + t * 2.0) * min(1, (cy - y) / R * 1.5)
            e = min(x, y, f.w - 1 - x, f.h - 1 - y)
            if e < 5: r += (5 - e) * 0.12                  # keep clear of the frame's edge
            r = max(r, 0.0)
            if r >= 1 or r < hollow: continue
            if d < 1 and not dith(x, y, d): continue
            q = (r - hollow) / (1 - hollow) if hollow else r
            f.put(x, y, cols[min(len(cols) - 1, int(q * len(cols)))])


def smoke_puffs(f, blobs, d, light='y', mid='G', dark='g'):
    inside = lambda x, y: any((x + .5 - bx) ** 2 + (y + .5 - by) ** 2 <= r * r for bx, by, r in blobs)
    for y in range(f.h):
        for x in range(f.w):
            if not inside(x, y) or not dith(x, y, d): continue
            if not inside(x - 1, y - 2): k = light
            elif not inside(x + 1, y + 2): k = dark
            else: k = mid
            f.put(x, y, k)


FIRE = ['W', 'W', 'a', 'a', 'A', 'r', 'R']


def fx_fire():
    frames = []
    f = Frame(48, 48); sparkle(f, 24, 32, 3, ('W', 'W', 'a', 'r')); frames.append(f)
    f = Frame(48, 48); flame(f, 24, 32, 9, 1, FIRE, 3); frames.append(f)
    f = Frame(48, 48); flame(f, 24, 30, 14, 2, FIRE, 3, lift=0.55); frames.append(f)
    f = Frame(48, 48); flame(f, 24, 29, 16, 3, ['W', 'a', 'a', 'A', 'r', 'r', 'R'], 4, lift=0.6); frames.append(f)
    f = Frame(48, 48)
    smoke_puffs(f, [(20, 14, 6), (28, 12, 5), (24, 9, 5)], 0.6)
    flame(f, 24, 32, 12, 4, ['a', 'A', 'r', 'r', 'R', 'R'], 5, lift=0.5, d=0.85)
    frames.append(f)
    f = Frame(48, 48)
    smoke_puffs(f, [(19, 9, 7), (29, 7, 6), (24, 4, 5)], 0.4)
    for x, y in ((18, 26), (29, 22), (24, 18), (33, 29)): f.put(x, y, 'a')
    for x, y in ((21, 30), (27, 27)): f.put(x, y, 'r')
    frames.append(f)
    return frames


def fx_flare():
    frames = []
    f = Frame(64, 64); burst(f, 32, 34, 4, 3, 10, ['W', 'W', 'a']); burst(f, 32, 34, 4, 1, 6, ['W'], rot=0.785); frames.append(f)
    f = Frame(64, 64)
    burst(f, 32, 34, 8, 8, 26, ['a', 'a', 'A', 'r'], rot=0.2, d=0.7)
    flame(f, 32, 34, 15, 1, FIRE, 7, lift=0.3)
    frames.append(f)
    f = Frame(64, 64); flame(f, 32, 36, 22, 2, FIRE, 7, lift=0.45); frames.append(f)
    f = Frame(64, 64)
    flame(f, 32, 36, 24, 3, ['a', 'a', 'A', 'r', 'r', 'R'], 8, lift=0.45, hollow=0.42)
    flame(f, 32, 36, 11, 3, ['r', 'R', 'R'], 9, lift=0.3, d=0.6)
    frames.append(f)
    f = Frame(64, 64)
    smoke_puffs(f, [(22, 22, 9), (40, 20, 9), (31, 13, 10), (31, 30, 8)], 0.75)
    flame(f, 32, 38, 18, 4, ['a', 'A', 'r', 'R'], 9, lift=0.3, hollow=0.55, d=0.7)
    frames.append(f)
    f = Frame(64, 64)
    smoke_puffs(f, [(22, 16, 10), (41, 14, 9), (31, 7, 9)], 0.35)
    for x, y in ((14, 34), (50, 30), (24, 42), (42, 44), (32, 26), (9, 24), (55, 40)): f.put(x, y, 'a')
    for x, y in ((18, 40), (46, 38), (30, 48)): f.put(x, y, 'r')
    frames.append(f)
    return frames


def swirl(f, cx, cy, R, phase, arms, width, cols, d=1.0, squash=1.0):
    """Water whirling round: spiral arms that get thinner towards the middle."""
    for y in range(f.h):
        for x in range(f.w):
            dx, dy = x + .5 - cx, (y + .5 - cy) / squash
            r = math.hypot(dx, dy)
            if r > R or r < 2: continue
            a = math.atan2(dy, dx)
            s = (a * arms + r / R * 7.0 - phase) % (2 * math.pi)
            band = width * (0.4 + 0.6 * r / R)
            if s > band: continue
            if d < 1 and not dith(x, y, d): continue
            q = s / band
            f.put(x, y, cols[min(len(cols) - 1, int(q * len(cols)))])


def fx_water():
    frames = []
    cols = ['W', 'c', 'c', 'C', 'C', 'w']
    f = Frame(48, 48)
    smoke_puffs(f, [(24, 27, 4.5)], 1, 'W', 'c', 'C')
    for x, y in ((17, 22), (31, 20), (22, 14), (28, 31)): f.put(x, y, 'c'); f.put(x, y + 1, 'C')
    frames.append(f)
    f = Frame(48, 48); swirl(f, 24, 28, 15, 0.0, 2, 2.6, cols, squash=0.8)
    smoke_puffs(f, [(24, 28, 3)], 1, 'W', 'c', 'C'); frames.append(f)
    f = Frame(48, 48); swirl(f, 24, 27, 21, 2.2, 3, 2.3, cols, squash=0.8); frames.append(f)
    # the surge: a column of water bursting upwards out of the whirl, lit from the left
    f = Frame(48, 48)
    swirl(f, 24, 38, 21, 4.0, 3, 1.8, cols, d=0.8, squash=0.4)
    for y in range(7, 40):
        wid = 2.5 + (y - 7) * 0.17
        for x in range(int(24 - wid), int(24 + wid) + 1):
            e = (x + .5 - 24) / wid
            if abs(e) > 1: continue
            k = 'W' if -0.55 < e < -0.1 else ('c' if e < 0.45 else 'C')
            if (y * 3 + x) % 11 == 0 and k == 'c': k = 'W'        # foam flecks
            f.put(x, y, k)
    for x in range(21, 28): f.put(x, 6 if 22 < x < 26 else 7, 'W')
    for x, y in ((18, 6), (30, 4), (15, 12), (33, 11), (21, 2), (27, 1), (12, 18), (36, 17)):
        f.put(x, y, 'W'); f.put(x, y + 1, 'c')
    frames.append(f)
    f = Frame(48, 48)
    for i in range(12):
        a = i / 12 * 6.283
        x, y = 24 + math.cos(a) * (7 + i % 3 * 5), 26 + math.sin(a) * 8 + (i % 2) * 4
        f.put(x, y, 'W'); f.put(x, y + 1, 'c'); f.put(x, y + 2, 'C')
    for i in range(120):
        a = i / 120 * 6.283
        for rr, k in ((19, 'c'), (17, 'C')):
            x, y = 24 + math.cos(a) * rr, 40 + math.sin(a) * rr * 0.27
            if dith(x, y, 0.9): f.put(x, y, k if math.sin(a) > 0 else 'C')
    frames.append(f)
    f = Frame(48, 48)
    for i in range(120):
        a = i / 120 * 6.283
        x, y = 24 + math.cos(a) * 22, 41 + math.sin(a) * 6
        if dith(x, y, 0.45): f.put(x, y, 'c')
    for x, y in ((12, 35), (35, 34), (20, 38), (30, 38), (25, 31)): f.put(x, y, 'W'); f.put(x, y + 1, 'C')
    frames.append(f)
    return frames


def fx_heal():
    # each mote: x, starting y, the frame it appears on
    motes = [(9, 27, 0), (22, 29, 0), (15, 22, 1), (26, 24, 1), (6, 20, 1), (19, 27, 2), (12, 18, 2), (24, 17, 3), (17, 14, 3)]
    frames = []
    for t in range(5):
        f = Frame(32, 32)
        if t < 4:
            for i in range(90):
                a = i / 90 * 6.283
                x, y = 16 + math.cos(a) * (9 + t * 2.5), 28 + math.sin(a) * (2.4 + t * 0.6)
                if dith(x, y, 1 - t * 0.25): f.put(x, y, 'f' if math.sin(a) > 0 else 'F')
        for x, y0, born in motes:
            age = t - born
            if age < 0 or age > 3: continue
            y = y0 - age * 4
            for j in range(1, 5):                           # a fading trail below it
                if dith(x, y + j + 2, 1 - j * 0.25): f.put(x, y + j + 2, 'f' if j < 2 else 'F')
            sparkle(f, x, y, (1, 2, 2, 1)[age], ('W', 'f', 'i'))
        frames.append(f)
    return frames


def fx_parry():
    frames = []
    f = Frame(32, 32)
    sparkle(f, 16, 16, 12, ('W', 'W', 'W', 'W', 'Y', 'Y', 'Y', 'a', 'a', 'a', 'A', 'A', 'A'))
    burst(f, 16, 16, 4, 1, 4, ['W'], rot=0.785)
    frames.append(f)
    sparks = [(a, sp) for a, sp in ((0.3, 1.0), (1.1, 0.8), (1.9, 1.1), (2.6, 0.9), (3.5, 1.0), (4.2, 0.8), (5.0, 1.1), (5.7, 0.9))]
    for t, core in ((1, 5), (2, 2), (3, 0)):
        f = Frame(32, 32)
        if core: sparkle(f, 16, 16, core, ('W', 'Y', 'Y', 'a', 'a', 'A'))
        for a, sp in sparks:
            r = (4 + t * 4) * sp
            n = 4 if t < 3 else 2
            for i in range(n):
                rr = r - i * 1.2
                x, y = 16 + math.cos(a) * rr, 16 + math.sin(a) * rr + t * t * 0.4
                if t == 3 and i: continue
                f.put(x, y, ('W', 'Y', 'a', 'A')[min(3, i + (t == 3) * 2)])
        frames.append(f)
    return frames


def fx_levelup():
    frames = []
    rays = [(9, 7), (13, 12), (18, 5), (22, 14), (27, 9), (31, 13), (35, 6), (39, 10)]
    for t in range(6):
        f = Frame(48, 48)
        cy = 42 - t * 6
        rx, ry = 17 - t * 1.0, 4.5 - t * 0.3
        d = 1 if t < 4 else (0.6 if t == 4 else 0.3)
        # shafts of light rising off the back of the ring, longer as it climbs
        if t >= 1:
            for x0, ln in rays:
                x = 24 + (x0 - 24) * rx / 17
                top = cy - ry - ln * min(1, t / 2.5) - 1
                for y in range(int(top), int(cy)):
                    if y < 1: continue
                    q = (y - top) / max(1, cy - top)
                    k = 'W' if y == int(top) else ('Y' if q < 0.2 else ('a' if q < 0.6 else 'A'))
                    if (d < 1 or y < 6 or q > 0.7) and not dith(x, y, d * (1.3 - q) if q > 0.7 else d): continue
                    f.put(x, y, k)
        for i in range(140):
            a = i / 140 * 6.283
            x, y = 24 + math.cos(a) * rx, cy + math.sin(a) * ry
            front = math.sin(a) > 0
            if d < 1 and not dith(x, y, d): continue
            f.put(x, y, ('W' if abs(math.cos(a)) < 0.35 else 'a') if front else 'A')
            if front and t < 4: f.put(x, y + 1, 'A')
        if 1 <= t <= 4:
            for (x, y, s) in ((8, 28 - t * 5, 1), (40, 25 - t * 5, 1), (24, 20 - t * 4, 2 if t == 2 else 1)):
                if 1 <= y: sparkle(f, x, y, s, ('W', 'Y', 'a'))
        frames.append(f)
    return frames


# ============================================================================================== the list

# name: (make, frame width, frame height, frame count)
FX = {
    'fx_dust': (fx_dust, 8, 8, 4),
    'fx_smoke': (fx_smoke, 8, 8, 4),
    'fx_splash': (fx_splash, 12, 8, 4),
    'fx_leaf': (fx_leaf, 4, 4, 4),
    'fx_firefly': (fx_firefly, 3, 3, 2),
    'fx_ember': (fx_ember, 2, 2, 3),
    'fx_ripple': (fx_ripple, 16, 8, 3),
    'fx_glint': (fx_glint, 7, 7, 4),
    'fx_bird': (fx_bird, 9, 5, 2),
    'fx_shadow_bird': (fx_shadow_bird, 9, 4, 1),
    'glow_warm': (glow_warm, 48, 48, 1),
    'glow_cool': (glow_cool, 48, 48, 1),
    'cloud_shadow': (cloud_shadow, 96, 64, 1),
    'vignette': (vignette, 160, 90, 1),
    'fx_slash': (fx_slash, 48, 48, 4),
    'fx_slash_crit': (fx_slash_crit, 64, 64, 5),
    'fx_hit': (fx_hit, 32, 32, 4),
    'fx_bite': (fx_bite, 32, 32, 4),
    'fx_fire': (fx_fire, 48, 48, 6),
    'fx_flare': (fx_flare, 64, 64, 6),
    'fx_water': (fx_water, 48, 48, 6),
    'fx_heal': (fx_heal, 32, 32, 5),
    'fx_parry': (fx_parry, 32, 32, 4),
    'fx_levelup': (fx_levelup, 48, 48, 6),
}


def main(out=OUT):
    os.makedirs(out, exist_ok=True)
    made = []
    for name, (make, w, h, n) in FX.items():
        frames = make()
        assert len(frames) == n and all(f.w == w and f.h == h for f in frames), name
        path = os.path.join(out, name + '.png')
        sheet(frames).save(path)
        made.append(path)
    return made


if __name__ == '__main__':
    print('\n'.join(main()))
