"""Paints an outdoor area from its tile rows, pixel by pixel, so the ground reads as one landscape instead of a grid.

The tile letters still decide where you can walk; this only decides how it looks. Edges between kinds of ground
(sea and sand, grass and road, the stream's banks, the headland) are pushed about by smooth noise so they come out
as natural curves, then shaded: foam and wet sand along the tide line, worn edges on the road, a rock face under the
headland. Trees, rocks, brambles, flowers, long grass, fences, bridges, wells and so on are drawn on top. Tree tops
and the front of long grass also go on the overlay, which is drawn over the player.

    img, over = terrain.paint(rows)
"""
import math, random
import numpy as np
from PIL import Image
from palette import PAL

T = 16
KEYS = list(PAL.keys())
IDX = {k: i for i, k in enumerate(KEYS)}
RGB = np.array([[int(PAL[k][i:i + 2], 16) for i in (1, 3, 5)] for k in KEYS], dtype=np.uint8)

GRASS, HIGH, SAND, SEA, STREAM, DIRT, COBBLE, MUD = range(8)

# one shade darker, for shadows
DARKER = {'f': 'i', 'i': 'F', 'F': 'v', 'v': 'V', 'V': 'V', 'z': 'd', 'd': 'D', 'D': 'b', 'b': 'B', 'B': 'o',
          'Y': 'y', 'y': 'G', 'G': 'g', 'g': 'o', 'c': 'C', 'C': 'w', 'w': 'l', 'l': 'X', 'X': 'X', 'J': 'f'}


def vnoise(h, w, cell, seed):
    """Smooth value noise, 0..1, one value every `cell` pixels."""
    rnd = np.random.RandomState(seed)
    gh, gw = h // cell + 3, w // cell + 3
    grid = rnd.rand(gh, gw)
    ys = np.arange(h) / cell
    xs = np.arange(w) / cell
    y0 = ys.astype(int); x0 = xs.astype(int)
    fy = ys - y0; fx = xs - x0
    fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
    a = grid[y0][:, x0]; b = grid[y0][:, x0 + 1]
    c = grid[y0 + 1][:, x0]; d = grid[y0 + 1][:, x0 + 1]
    top = a + (b - a) * fx[None, :]
    bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]


def ground_class(rows, x, y):
    H, W = len(rows), len(rows[0])
    t = rows[y][x]

    def near(chars):
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if 0 <= x + dx < W and 0 <= y + dy < H and rows[y + dy][x + dx] in chars: return True
        return False
    if t in '~PY': return SEA
    if t == 'm': return MUD
    if t == 'Q': return SAND
    if t in 'ZI': return MUD if near('m') else SAND if near(',~') else GRASS
    if t in 'lSe': return MUD if near('m') else COBBLE if near('c') or t == 'S' else GRASS
    if t in 'KE': return SAND if near(',~P') else MUD if near('m') else COBBLE if near('c') else GRASS
    if t == 'N': return SAND if near(',~') else GRASS
    if t == ',': return SAND
    if t in 'wb': return STREAM
    if t in '=D': return DIRT
    if t in 'OAL': return MUD if near('m') and not near('c') else COBBLE
    if t == 'c': return COBBLE
    if t == 'n': return MUD if near('m') else COBBLE if near('c') else GRASS
    if t in 'jv': return MUD if near('m') else GRASS
    if t in 'uhsq': return HIGH
    if t in 'rR': return SAND if near(',~') else GRASS
    if t == 'H': return MUD if near('m') else GRASS
    return GRASS


def paint_mud(cv, rows, mud, cls, big, small, speck, checker, touching, seed):
    H, W = len(rows), len(rows[0])
    PH, PW = mud.shape
    g = cv.g

    def fill(mask, k):
        g[mask] = IDX[k]
    fill(mud, 'b')
    fill(mud & (big > 0.62) & checker, 'D')                 # drier crusts
    fill(mud & (big > 0.66), 'D')
    fill(mud & (big > 0.66) & (small > 0.7) & checker, 'd')
    fill(mud & (big < 0.3) & (small < 0.45) & checker, 'B')  # wetter, darker hollows
    fill(mud & (big < 0.2) & (small < 0.35), 'B')
    fill(mud & (speck < 0.012), 'B')
    fill(mud & (speck > 0.99), 'D')
    grass = (cls == GRASS) | (cls == HIGH)
    # the edge: trampled grass stops raggedly, tufts left standing in the mud
    fill(touching(mud, grass, 1) & (small > 0.45), 'v')
    fill(touching(mud, grass, 1) & checker & (small > 0.55), 'F')
    fill(touching(grass, mud, 1) & (checker | (small < 0.4)), 'v')
    fill(touching(grass, mud, 2) & (speck < 0.12), 'b')

    def ok(x, y):
        return 0 <= x < PW and 0 <= y < PH and mud[y, x]

    def put(x, y, k):
        if ok(x, y): g[y, x] = IDX[k]
    at = lambda x, y: rows[y][x] if 0 <= x < W and 0 <= y < H else None
    walk = set('mDlSenKE')
    for ty in range(H):
        for tx in range(W):
            if rows[ty][tx] != 'm': continue
            r = random.Random(tx * 7213 + ty * 3571 + seed)
            X0, Y0 = tx * T, ty * T
            # ruts: two wheel tracks along a street (a run of mud at most two wide), not across the open square
            def run(dx, dy):
                n = 1
                for sgn in (1, -1):
                    x, y = tx + dx * sgn, ty + dy * sgn
                    while at(x, y) in walk: n += 1; x += dx * sgn; y += dy * sgn
                return n
            wide, tall = run(1, 0), run(0, 1)
            vert = wide == 2 and tall >= 4          # cart ruts on the two-wide streets; footpaths just get trodden
            horiz = tall == 2 and wide >= 4
            lf = at(tx - 1, ty) in walk
            up = at(tx, ty - 1) in walk
            if vert and not (wide == 2 and lf):
                for rx in ((X0 + 4, X0 + 11) if wide == 1 else (X0 + 9, X0 + 22)):
                    for y in range(Y0, Y0 + T):
                        x = rx + int(round(1.2 * math.sin((y + tx * 5) / 7.0)))
                        if r.random() < 0.12: continue
                        put(x, y, 'B'); put(x - 1, y, 'D' if (y + tx) % 3 else 'b')
                        if r.random() < 0.3: put(x + 1, y, 'B')
            if horiz and not (tall == 2 and up):
                for ry in ((Y0 + 5, Y0 + 12) if tall == 1 else (Y0 + 10, Y0 + 22)):
                    for x in range(X0, X0 + T):
                        y = ry + int(round(1.0 * math.sin((x + ty * 3) / 6.0)))
                        if r.random() < 0.12: continue
                        put(x, y, 'B'); put(x, y - 1, 'D' if (x + ty) % 3 else 'b')
            # a puddle, every few tiles: murky water with a dull grey glint, a wet dark lip
            if r.random() < 0.22:
                cx, cy = X0 + r.uniform(4, 12), Y0 + r.uniform(4, 12)
                rx, ry = r.uniform(3, 5.5), r.uniform(1.7, 2.7)
                pts = {(x, y) for (x, y) in blob(cx, cy, rx, ry) if ok(x, y)}
                if len(pts) > 6:
                    for (x, y) in pts:
                        put(x, y, 'n' if y < cy + 0.6 else 'N')
                    for (x, y) in pts:
                        if (x, y - 1) not in pts or (x - 1, y) not in pts: put(x, y, 'B')
                        elif (x, y + 1) not in pts: put(x, y + 1, 'D')
                    gx, gy = int(cx), int(round(cy))
                    if (gx, gy) in pts and (gx + 1, gy) in pts:
                        put(gx, gy, 'G'); put(gx + 1, gy, 'y' if r.random() < 0.5 else 'G')
            # footprints: little pairs of dents
            for _ in range(r.choice((0, 1, 1, 2))):
                fx, fy = X0 + r.randint(1, 13), Y0 + r.randint(1, 13)
                for (dx, dy) in ((0, 0), (0, 1), (3, 3), (3, 4)):
                    if g[fy + dy if fy + dy < PH else PH - 1, min(fx + dx, PW - 1)] != IDX['N']: put(fx + dx, fy + dy, 'B')
                put(fx, fy - 1, 'D')
            # a stone pressed into the mud, or a wisp of straw
            if r.random() < 0.2:
                sx, sy = X0 + r.randint(1, 14), Y0 + r.randint(1, 14)
                put(sx, sy, 'y'); put(sx + 1, sy, 'G'); put(sx, sy + 1, 'g'); put(sx + 1, sy + 1, 'B')
            if r.random() < 0.25:
                sx, sy = X0 + r.randint(1, 12), Y0 + r.randint(2, 13)
                d = r.choice((-1, 1))
                for i in range(r.randint(3, 4)): put(sx + i, sy + (i // 2) * d, 'J' if i % 2 == 0 else 'A')


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.g = np.full((h, w), IDX['F'], dtype=np.int16)
        self.o = np.full((h, w), -1, dtype=np.int16)    # overlay; -1 = see-through

    def put(self, x, y, k, over=False):
        if 0 <= x < self.w and 0 <= y < self.h:
            (self.o if over else self.g)[y, x] = IDX[k]

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h: return KEYS[self.g[y, x]]
        return None

    def shade(self, x, y, steps=1):
        k = self.get(x, y)
        if k is None: return
        for _ in range(steps): k = DARKER.get(k, k)
        self.g[y, x] = IDX[k]

    def images(self):
        img = Image.fromarray(RGB[self.g], 'RGB').convert('RGBA')
        ov = np.zeros((self.h, self.w, 4), dtype=np.uint8)
        m = self.o >= 0
        ov[m, :3] = RGB[self.o[m]]
        ov[m, 3] = 255
        return img, Image.fromarray(ov, 'RGBA')


def blob(cx, cy, rx, ry):
    """Pixels inside an ellipse."""
    out = []
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1: out.append((x, y))
    return out


def paint(rows, seed=1):
    H, W = len(rows), len(rows[0])
    PW, PH = W * T, H * T
    cv = Canvas(PW, PH)
    rnd = random.Random(seed)

    def at(x, y):
        return rows[y][x] if 0 <= x < W and 0 <= y < H else None

    # ------------------------------------------------------------------ the ground, with natural edges
    cls_t = np.array([[ground_class(rows, x, y) for x in range(W)] for y in range(H)], dtype=np.int8)
    tall_t = np.array([[rows[y][x] == 'g' for x in range(W)] for y in range(H)])
    yy, xx = np.mgrid[0:PH, 0:PW]

    def smooth(field, r):
        """Box blur (twice, so it's soft) of a pixel field."""
        for _ in range(2):
            for ax in (0, 1):
                c = np.cumsum(np.pad(field, [(r + 1, r) if a == ax else (0, 0) for a in (0, 1)], mode='edge'), axis=ax)
                if ax == 0: field = (c[2 * r + 1:, :] - c[:-2 * r - 1, :]) / (2 * r + 1)
                else: field = (c[:, 2 * r + 1:] - c[:, :-2 * r - 1]) / (2 * r + 1)
        return field

    # Each kind of ground gets a soft field (how much of it is around each pixel); the strongest wins.
    # That rounds off the corners of the tile grid, and a little noise roughens the curves.
    wobble = (vnoise(PH, PW, 9, seed + 1) - 0.5) * 0.5
    best = np.full((PH, PW), -9.0)
    cls = np.zeros((PH, PW), dtype=np.int8)
    for c in range(8):
        m = (cls_t == c).astype(float)
        if not m.any(): continue
        f = smooth(np.kron(m, np.ones((T, T))), 4 if c in (DIRT, COBBLE, MUD) else 7 if c == STREAM else 5)
        f = f + wobble * (1 if c % 2 else -1) * (0.4 if c in (DIRT, COBBLE) else 0.7 if c == MUD else 1)
        win = f > best
        best[win] = f[win]
        cls[win] = c
    tf = smooth(np.kron(tall_t.astype(float), np.ones((T, T))), 5) + wobble
    tall = (tf > 0.5) & (cls == GRASS)

    def touching(mask, other, d=1):
        """Pixels in mask with a pixel of `other` within d steps (4 directions)."""
        hit = np.zeros_like(mask)
        for s in range(1, d + 1):
            for ax, sh in ((0, s), (0, -s), (1, s), (1, -s)):
                hit |= np.roll(other, sh, axis=ax)
        return mask & hit

    big = vnoise(PH, PW, 30, seed + 3)
    small = vnoise(PH, PW, 6, seed + 4)
    speck = np.random.RandomState(seed + 5).rand(PH, PW)
    checker = (xx + yy) % 2 == 0
    g = cv.g

    def fill(mask, k):
        g[mask] = IDX[k]

    # grass: calm patches of light and dark, a few tufts
    m = cls == GRASS
    fill(m, 'F')
    fill(m & (big > 0.66) & checker, 'i')
    fill(m & (big > 0.72), 'i')
    fill(m & (small > 0.8) & (speck < 0.25), 'i')
    fill(m & (speck < 0.008), 'f')
    fill(m & (speck > 0.994), 'v')
    # high ground: a lighter green
    m = cls == HIGH
    fill(m, 'i')
    fill(m & (big > 0.6) & checker, 'f')
    fill(m & (big > 0.7), 'f')
    fill(m & (speck < 0.01), 'f')
    fill(m & (speck > 0.993), 'F')
    # sand: pale, a little darker by the water
    m = cls == SAND
    fill(m, 'd')
    fill(m & (big > 0.62) & (checker | (big > 0.66)), 'z')
    fill(m & (speck < 0.03), 'D')
    fill(m & (speck > 0.985), 'z')
    sea = cls == SEA
    wet = touching(m, sea, 4)
    fill(wet & (checker | touching(m, sea, 2)), 'D')
    # sea: deep away from the shore, foam on the tide line
    fill(sea, 'w')
    land = ~sea
    near_shore = touching(sea, land, 9)
    fill(sea & ~near_shore & (checker | ~touching(sea, land, 12)), 'l')
    fill(sea & ~touching(sea, land, 16) & checker, 'X')
    fill(sea & (small > 0.72) & ((yy % 6) == 0), 'C')
    fill(sea & (small > 0.8) & ((yy % 6) == 0) & ((xx % 5) != 0), 'c')
    fill(touching(sea, land, 2) & (small > 0.35), 'c')
    fill(touching(sea, land, 1) & (small > 0.25), 'W')
    # the stream: lighter water, dark banks
    st = cls == STREAM
    fill(st, 'C')
    fill(st & (small > 0.66) & ((yy % 4) == 1), 'c')
    fill(touching(st, ~st & ~sea, 1), 'w')
    bank = touching(~st & ~sea, st, 1)
    fill(bank & (cls == GRASS), 'v')
    # the road: packed earth, worn darker at the edges
    m = cls == DIRT
    fill(m, 'D')
    fill(m & (speck < 0.04), 'b')
    fill(m & (speck > 0.975), 'd')
    edge = touching(m, (cls == GRASS) | (cls == HIGH), 1)
    fill(edge & (checker | (small > 0.5)), 'b')
    # cobbles: rounded stones in staggered rows, mortar between
    m = cls == COBBLE
    fill(m, 'G')
    row = yy // 5
    off = (row % 2) * 3
    cx = (xx + off) % 6
    cy = yy % 5
    corner = ((cx == 0) | (cx == 4)) & ((cy == 0) | (cy == 3))
    stone = m & (cx < 5) & (cy < 4) & ~corner
    shade = (np.floor((xx + off) / 6) * 7 + row * 13) % 5
    fill(stone, 'y')
    fill(stone & (shade == 0), 'G')
    fill(stone & (cy == 3) & (shade != 0), 'G')
    fill(stone & (cy == 0) & (cx == 1) & (shade != 0), 'Y')
    fill(touching(m, cls == GRASS, 1) & checker, 'F')
    # mud: wet trodden earth, drier crusts, cart ruts along the streets, footprints, puddles, the odd stone or straw
    mud = cls == MUD
    if mud.any():
        paint_mud(cv, rows, mud, cls, big, small, speck, checker, touching, seed)

    # the headland: a rock face under its south edge, rims on the other sides
    high = cls == HIGH
    face_d = np.full((PH, PW), 99)
    for X in range(PW):
        col = high[:, X]
        nxt = 99
        for Y in range(PH - 1, -1, -1):
            if not col[Y]: nxt = 0
            else: nxt += 1
            face_d[Y, X] = nxt
    face = high & (face_d <= 12)
    for Y, X in zip(*np.nonzero(face)):
        d = face_d[Y, X]
        k = 'G'
        if (Y + (X // 5)) % 4 == 0: k = 'y'
        if (X * 7 + Y * 3) % 17 == 0: k = 'g'
        if d == 12: k = 'v'
        elif d == 11: k = 'y'
        elif d == 1: k = 'g'
        g[Y, X] = IDX[k]
    low = ~high
    outer = touching(high & ~face, low, 1)
    fill(touching(high & ~face, low, 2) & ~outer, 'F')
    fill(outer, 'v')
    fill(touching(low & (cls == GRASS), high, 1), 'v')
    fill(touching(low & (cls == GRASS), face, 2) & checker, 'v')

    # ------------------------------------------------------------------ long grass (front blades on the overlay)
    for Y0 in range(0, PH, 4):
        for X0 in range(-2, PW, 3):
            jx = X0 + ((Y0 // 4) % 2) + (1 if speck[min(Y0, PH - 1), max(0, X0)] > 0.7 else 0)
            by = Y0 + 4
            if not (0 <= jx < PW and 0 <= by < PH) or not tall[by, jx]: continue
            front = (by % T) >= 8
            for (dx, dy, k) in ((0, 0, 'V'), (0, -1, 'v'), (0, -2, 'F'), (0, -3, 'i'), (1, -4, 'f'), (-1, -3, 'F')):
                cv.put(jx + dx, by + dy, k)
                if front: cv.put(jx + dx, by + dy, k, over=True)

    paint_bridges(cv, rows, cls, seed)

    # ------------------------------------------------------------------ things on the ground
    trees = []
    for ty in range(H):
        for tx in range(W):
            t = rows[ty][tx]
            X0, Y0 = tx * T, ty * T
            r = random.Random(tx * 7919 + ty * 104729 + seed)
            if t == 'o':
                for _ in range(r.randint(2, 4)):
                    fx, fy = X0 + r.randint(2, 13), Y0 + r.randint(2, 13)
                    petal = r.choice(['W', 'W', 'a', 'r', 'p'])
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)): cv.put(fx + dx, fy + dy, petal)
                    cv.put(fx, fy, 'a' if petal != 'a' else 'A')
                    cv.put(fx, fy + 2, 'v')
            elif t == 'T':
                trees.append((tx, ty))
            elif t in 'rR':
                cx, cy = X0 + 8 + r.uniform(-1, 1), Y0 + 9
                for (x, y) in blob(cx + 2, cy + 3, 7, 3.5): cv.shade(x, y)
                if t == 'R':
                    pts = blob(cx, cy, 7.5, 5.5)
                    for (x, y) in pts:
                        k = 'y' if y < cy + 1 else 'G'
                        cv.put(x, y, k)
                    for (x, y) in pts:
                        if (x, y + 1) not in pts or (x + 1, y) not in pts: cv.put(x, y, 'g')
                    for (x, y) in blob(cx, cy - 0.5, 4.5, 2.8): cv.put(x, y, 'w' if y > cy - 1 else 'C')
                    cv.put(int(cx) - 2, int(cy) - 2, 'c'); cv.put(int(cx) - 1, int(cy) - 2, 'c')
                    cv.put(int(cx) + 2, int(cy) + 1, 'r')
                else:
                    pts = set(blob(cx, cy, 6.5, 5))
                    for (x, y) in pts:
                        k = 'y'
                        if (x - cx) + (y - cy) * 1.3 < -3: k = 'Y'
                        elif (x - cx) + (y - cy) * 1.3 > 3: k = 'G'
                        cv.put(x, y, k)
                    for (x, y) in pts:
                        if (x, y + 1) not in pts or (x + 1, y) not in pts: cv.put(x, y, 'g')
                        elif (x, y - 1) not in pts or (x - 1, y) not in pts: cv.put(x, y, 'G' if y > cy else 'y')
                    cv.put(int(cx) - 2, int(cy) - 2, 'W')
            elif t == 'q':
                # seams of rusty ore and the odd glint in the rock face
                for _ in range(7):
                    fx, fy = X0 + r.randint(1, 13), Y0 + r.randint(6, 13)
                    for dx in range(r.randint(1, 3)):
                        cv.put(fx + dx, fy, 'R' if (dx + fy) % 2 else 'r')
                    cv.put(fx, fy + 1, 'm')
                cv.put(X0 + r.randint(3, 12), Y0 + r.randint(6, 12), 'a')
                # pick marks
                for _ in range(3):
                    fx, fy = X0 + r.randint(2, 12), Y0 + r.randint(5, 12)
                    cv.put(fx, fy, 'W'); cv.put(fx + 1, fy + 1, 'g')
            elif t == 'k':
                cx, cy = X0 + 8, Y0 + 9
                for (x, y) in blob(cx + 1, cy + 4, 7, 3): cv.shade(x, y)
                pts = set()
                for _ in range(5):
                    pts |= set(blob(cx + r.uniform(-3.5, 3.5), cy + r.uniform(-2.5, 2.5), r.uniform(3, 4.5), r.uniform(2.5, 3.5)))
                for (x, y) in pts:
                    k = 'v' if (x * 3 + y * 5) % 7 else 'F'
                    if y < cy - 3 and (x + y) % 3 == 0: k = 'F'
                    cv.put(x, y, k)
                for (x, y) in pts:
                    if (x, y + 1) not in pts: cv.put(x, y, 'V')
                    elif (x - 1, y) not in pts or (x + 1, y) not in pts or (x, y - 1) not in pts: cv.put(x, y, 'V' if (x + y) % 2 else 'v')
                for _ in range(6):
                    bx, by = int(cx + r.uniform(-5, 5)), int(cy + r.uniform(-3, 3))
                    if (bx, by) in pts: cv.put(bx, by, 'u'); cv.put(bx + 1, by, 'P')
            elif t == 'F':
                for x in range(X0, X0 + T):
                    for y, k in ((Y0 + 6, 'd'), (Y0 + 7, 'b'), (Y0 + 10, 'd'), (Y0 + 11, 'b')):
                        cv.put(x, y, k)
                    cv.shade(x, Y0 + 8); cv.shade(x, Y0 + 12)
                for px in (X0 + 2, X0 + 10):
                    for y in range(Y0 + 3, Y0 + 14):
                        cv.put(px, y, 'D' if y > Y0 + 3 else 'd'); cv.put(px + 1, y, 'b')
                    cv.shade(px, Y0 + 14); cv.shade(px + 1, Y0 + 14); cv.shade(px + 2, Y0 + 14)
            elif t == 'b':
                pass                                      # bridges are drawn whole by paint_bridges
            elif t == 's':
                for y in range(Y0, Y0 + T):
                    for x in range(X0 + 2, X0 + T - 2):
                        k = 'Y' if (y - Y0) % 4 == 0 else ('y' if (y - Y0) % 4 == 1 else 'G')
                        cv.put(x, y, k)
                    cv.put(X0 + 1, y, 'g'); cv.put(X0 + T - 2, y, 'g')
            elif t == 'P':
                for y in range(Y0, Y0 + T):
                    for x in range(X0, X0 + T):
                        k = 'D' if (x % 4) else 'b'
                        if (x * 3 + y * 7) % 31 == 0: k = 'd'
                        cv.put(x, y, k)
                if at(tx, ty - 1) != 'P':
                    for x in range(X0, X0 + T): cv.put(x, Y0, 'd')
                    if tx % 2 == 0:   # a bollard to tie up to
                        for (x, y) in _rect(X0 + 6, Y0 + 1, X0 + 9, Y0 + 4): cv.put(x, y, 'g' if x == X0 + 9 or y == Y0 + 4 else 'G')
                        cv.put(X0 + 7, Y0 + 1, 'y')
                if at(tx, ty + 1) != 'P':
                    for x in range(X0, X0 + T):
                        cv.put(x, Y0 + T - 1, 'B'); cv.shade(x, Y0 + T); cv.shade(x, Y0 + T + 1)
                    for px in (X0 + 3, X0 + 11):
                        for y in range(Y0 + T, Y0 + T + 3): cv.put(px, y, 'B'); cv.put(px + 1, y, 'o')
            elif t == 'O':
                for (x, y) in blob(X0 + 9.5, Y0 + 11, 7, 4): cv.shade(x, y)
                ring = set(blob(X0 + 7.5, Y0 + 8, 7, 6.5))
                for (x, y) in ring:
                    k = 'Y' if ((x - X0) // 3 + (y - Y0) // 2) % 3 else 'y'
                    if (x, y + 1) not in ring or (x + 1, y) not in ring: k = 'g'
                    cv.put(x, y, k)
                for (x, y) in blob(X0 + 7.5, Y0 + 7.5, 4.2, 3.6): cv.put(x, y, 'X' if y > Y0 + 6 else 'l')
                cv.put(X0 + 6, Y0 + 6, 'c'); cv.put(X0 + 7, Y0 + 6, 'c')
                # posts, a little pitched roof and a bucket on its rope (the roof is drawn over you)
                for px in (X0 + 1, X0 + 13):
                    for y in range(Y0 - 6, Y0 + 9): cv.put(px, y, 'B', y < Y0 + 2); cv.put(px + 1, y, 'b', y < Y0 + 2)
                for y in range(Y0 - 13, Y0 - 5):
                    w = (y - (Y0 - 13))
                    for x in range(X0 + 7 - w - 1, X0 + 9 + w):
                        k = 'b' if (x - X0) % 3 else 'B'          # a rough plank roof
                        if (x + y) % 5 == 0: k = 'D'
                        if x in (X0 + 7 - w - 1, X0 + 8 + w) or y == Y0 - 6: k = 'o'
                        cv.put(x, y, k, True)
                for x in range(X0 + 2, X0 + 14): cv.put(x, Y0 - 4, 'D', True)
                for y in range(Y0 - 3, Y0 + 2): cv.put(X0 + 8, y, 'z', True)
                for (x, y) in _rect(X0 + 6, Y0 + 2, X0 + 10, Y0 + 5): cv.put(x, y, 'b' if y > Y0 + 2 else 'D', True)
            elif t == 'L':
                for (x, y) in blob(X0 + 9, Y0 + 13, 8, 3): cv.shade(x, y)
                box = {(x, y) for x in range(X0 + 1, X0 + 15) for y in range(Y0 + 2, Y0 + 15)}
                for (x, y) in box:
                    k = 'G' if ((x - X0) // 3 + (y - Y0) // 3) % 2 else 'y'
                    if (x + 1, y) not in box or (x, y + 1) not in box: k = 'g'
                    if (x - 1, y) not in box or (x, y - 1) not in box: k = 'Y'
                    cv.put(x, y, k)
                for x in range(X0 + 4, X0 + 12):
                    for y in range(Y0 + 5, Y0 + 11):
                        cv.put(x, y, 'a' if (x * 3 + y) % 5 == 0 else ('r' if (x + y) % 3 else 'R'))
            elif t == 'A':
                for (x, y) in blob(X0 + 9, Y0 + 13, 7, 2.5): cv.shade(x, y)
                body = {(x, y) for x in range(2, 14) for y in range(6, 9)} | {(x, y) for x in range(5, 11) for y in range(9, 12)} \
                    | {(x, y) for x in range(4, 12) for y in range(12, 14)} | {(x, y) for x in range(0, 3) for y in range(6, 8)}
                for (x, y) in body:
                    k = 'Y' if y == 6 else 'y' if y < 9 else 'G'
                    if (x, y + 1) not in body or (x + 1, y) not in body: k = 'g'
                    cv.put(X0 + x, Y0 + y, k)
            elif t == 'n':
                for (x, y) in blob(X0 + 9, Y0 + 14, 6, 2): cv.shade(x, y)
                body = {(x, y) for x in range(3, 13) for y in range(2, 15) if not ((x in (3, 12)) and (y in (2, 14)))}
                for (x, y) in body:
                    k = 'B' if y in (5, 11) else ('d' if x < 5 else 'D' if x < 9 else 'b')
                    if (x, y + 1) not in body or (x + 1, y) not in body or (x - 1, y) not in body: k = 'B'
                    cv.put(X0 + x, Y0 + y, k)
                for x in range(4, 12): cv.put(X0 + x, Y0 + 3, 'b')
            else:
                props(cv, rows, t, tx, ty, X0, Y0, r)

    # ------------------------------------------------------------------ the palisade and its gateposts (tips over the player)
    paint_walls(cv, rows, seed)

    # ------------------------------------------------------------------ trees: trunks and shadows below, tops over the player
    for (tx, ty) in sorted(trees, key=lambda p: p[1]):
        r = random.Random(tx * 31 + ty * 1009 + seed)
        dense = all(at(tx + dx, ty + dy) in ('T', None) for dx in (-1, 0, 1) for dy in (-1, 0, 1))
        cx = tx * T + 8 + r.uniform(-2, 2)
        cy = ty * T + 3 + r.uniform(-1.5, 1.5)
        rad = r.uniform(9.5, 11) if dense else r.uniform(8.5, 10)
        if not dense:
            for (x, y) in blob(cx + 3, ty * T + 14, rad * 0.8, 3): cv.shade(x, y)
            for y in range(ty * T + 8, ty * T + 15):
                for dx, k in ((-2, 'B'), (-1, 'b'), (0, 'b'), (1, 'B')):
                    cv.put(int(cx) + dx, y, k)
            cv.put(int(cx) - 3, ty * T + 14, 'B'); cv.put(int(cx) + 2, ty * T + 14, 'B')
        pts = set()
        lumps = [(cx, cy, rad * 0.75)] + [(cx + rad * 0.55 * math.cos(a), cy + rad * 0.5 * math.sin(a), rad * 0.5)
                                         for a in [r.uniform(0, 6.28) + i * 1.25 for i in range(5)]]
        for (lx, ly, lr) in lumps:
            pts |= set(blob(lx, ly, lr, lr * 0.92))
        leaf = random.Random(tx * 3 + ty * 5)
        for (x, y) in pts:
            dx, dy = (x - cx) / rad, (y - cy) / rad
            l = -dx * 0.6 - dy * 0.9 + leaf.uniform(-0.35, 0.35)
            k = 'f' if l > 0.75 else 'i' if l > 0.25 else 'F' if l > -0.35 else 'v'
            if (x, y + 1) not in pts: k = 'V'
            elif (x + 1, y) not in pts or (x - 1, y) not in pts: k = 'v' if dy > -0.2 else 'F'
            cv.put(x, y, k, over=True)
            if not dense and y > cy + rad * 0.4: cv.put(x, y, k)
    return cv.images()


# ------------------------------------------------------------------ village props (Skelmed, 20 Oct)

def _box(cv, pts, light, mid, dark, outline='o', over=False):
    pts = set(pts)
    for (x, y) in pts:
        k = mid
        if (x - 1, y) not in pts or (x, y - 1) not in pts: k = light
        if (x + 1, y) not in pts or (x, y + 1) not in pts: k = dark
        cv.put(x, y, k, over)
    for (x, y) in pts:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if (x + dx, y + dy) not in pts: cv.put(x + dx, y + dy, outline, over)


def _rect(x0, y0, x1, y1):
    return [(x, y) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1)]


def props(cv, rows, t, tx, ty, X0, Y0, r):
    H, W = len(rows), len(rows[0])
    at = lambda x, y: rows[y][x] if 0 <= x < W and 0 <= y < H else None
    if t == 'v':
        # a vegetable patch: dark tilled rows with cabbages and leeks
        for y in range(Y0 + 1, Y0 + 15):
            for x in range(X0 + 1, X0 + 15):
                cv.put(x, y, 'b' if (y - Y0) % 5 in (0, 4) else 'B' if (y - Y0) % 5 == 3 else 'q')
        for row in (Y0 + 3, Y0 + 8, Y0 + 13):
            for cx in range(X0 + 3, X0 + 14, 5):
                if r.random() < 0.15: continue
                kind = r.random()
                if kind < 0.6:
                    for (x, y) in [(cx, row - 1), (cx - 1, row), (cx + 1, row), (cx, row), (cx, row + 1)]:
                        cv.put(x, y, 'f' if y < row else 'i')
                    cv.put(cx, row, 'F')
                else:
                    for dy in range(3): cv.put(cx, row - dy, 'f' if dy == 2 else 'i')
                    cv.put(cx - 1, row - 2, 'i'); cv.put(cx + 1, row - 1, 'f')
    elif t == 'j':
        # a woodpile: split logs stacked, ends showing
        for (x, y) in _rect(X0 + 1, Y0 + 13, X0 + 15, Y0 + 15): cv.shade(x, y)
        for row, n0 in ((0, 0), (1, 1), (2, 2)):
            for i in range(4 - row):
                cx, cy = X0 + 3 + i * 4 + row * 2, Y0 + 12 - row * 4
                for (x, y) in blob(cx, cy, 2.2, 2.2):
                    cv.put(x, y, 'J' if (x - cx) ** 2 + (y - cy) ** 2 < 1.5 else 'd')
                cv.put(cx, cy, 'D')
                for (x, y) in blob(cx, cy, 2.6, 2.6):
                    if (x - cx) ** 2 + (y - cy) ** 2 > 4.3: cv.put(x, y, 'b')
    elif t == 'l':
        # a torch post: a crude pole jammed in among stones, an iron basket of burning pitch-wood on top (over you)
        cx = X0 + 7
        for (x, y) in blob(cx + 3, Y0 + 14, 4, 1.5): cv.shade(x, y)
        for y in range(Y0 - 8, Y0 + 14):
            over = y < Y0 + 4
            lean = 1 if y < Y0 - 1 else 0          # not quite straight
            k = 'D' if (y * 5 + tx) % 7 else 'b'
            cv.put(cx + lean, y, 'o', over); cv.put(cx + 1 + lean, y, k, over)
            cv.put(cx + 2 + lean, y, 'B', over); cv.put(cx + 3 + lean, y, 'o', over)
        for y in (Y0 + 1, Y0 + 2):                  # a lashing round it
            for x in range(cx, cx + 4): cv.put(x, y, 'J' if (x + y) % 2 else 'A', True)
        for (sx, sy) in ((cx - 2, Y0 + 13), (cx + 4, Y0 + 13), (cx + 2, Y0 + 14)):   # wedging stones
            cv.put(sx, sy, 'y'); cv.put(sx + 1, sy, 'G'); cv.put(sx, sy + 1, 'g'); cv.put(sx + 1, sy + 1, 'o')
        # the basket: iron straps flaring out from the pole top
        bx = cx + 2
        for j, hw in enumerate((4, 4, 3, 3, 2, 1)):
            y = Y0 - 13 + j
            for x in range(bx - hw, bx + hw + 1):
                edge = abs(x - bx) == hw
                k = 'o' if edge else ('g' if (x - bx) % 2 == 0 else ('R' if j < 3 else 'm'))
                if j == 0: k = 'g' if (x - bx) % 2 == 0 else 'o'
                cv.put(x, y, k, True)
        for x in range(bx - 3, bx + 4, 2): cv.put(x, Y0 - 14, 'R' if x != bx else 'r', True)
        # the flame
        for (dx, dy, k) in ((0, -15, 'r'), (-1, -15, 'a'), (1, -15, 'R'), (0, -16, 'a'), (-1, -16, 'r'), (1, -16, 'a'),
                            (0, -17, 'a'), (-1, -18, 'a'), (0, -19, 'W'), (1, -17, 'r'), (2, -15, 'a'), (-2, -15, 'r')):
            cv.put(bx + dx, Y0 + dy, k, True)
        cv.put(bx + 2, Y0 - 20, 'G', True); cv.put(bx + 3, Y0 - 22, 'y', True)    # a wisp of smoke
    elif t == 'Q':
        paint_wreck(cv, rows, tx, ty, r)
    elif t == 'Y':
        # a small fishing boat, two tiles long (drawn once, from its left tile): pointed bow, square stern, two seats
        if at(tx - 1, ty) == 'Y': return
        x0, x1, ym = X0 + 1, X0 + 30, Y0 + 8
        hull = set()
        for x in range(x0, x1 + 1):
            u = (x - x0) / (x1 - x0)
            half = 5.5 if u > 0.3 else 1.5 + 4 * (u / 0.3) ** 0.6
            for y in range(int(round(ym - half)), int(round(ym + half)) + 1): hull.add((x, y))
        for (x, y) in hull:      # ripples round it
            for dx, dy in ((0, 2), (1, 2), (-1, 1), (2, 1)):
                if (x + dx, y + dy) not in hull and cv.get(x + dx, y + dy) in ('w', 'X', 'l', 'C'): cv.put(x + dx, y + dy, 'C' if (x + y) % 3 else 'c')
        inner = {(x, y) for (x, y) in hull if all((x + dx, y + dy) in hull for dx in (-2, 0, 2) for dy in (-2, 0, 2))}
        for (x, y) in hull:
            if (x, y) in inner: k = 'b' if (y - ym) % 3 else 'B'
            else: k = 'd' if y <= ym else 'D'
            cv.put(x, y, k)
        for (x, y) in hull:
            if any((x + dx, y + dy) not in hull for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))): cv.put(x, y, 'o')
        for sx in (x0 + 12, x0 + 21):
            for y in range(ym - 4, ym + 5):
                if (sx, y) in hull and (sx, y - 1) in hull and (sx, y + 1) in hull: cv.put(sx, y, 'd'); cv.put(sx + 1, y, 'D')
        for (x, y) in blob(x0 + 26, ym, 1.8, 1.8): cv.put(x, y, 'z')        # a coil of rope in the stern
        cv.put(x0 + 26, ym, 'D')
    elif t == 'N':
        # a drying rack with a net hung over it
        for px in (X0 + 1, X0 + 14):
            for y in range(Y0 + 1, Y0 + 15): cv.put(px, y, 'b' if y > Y0 + 1 else 'd'); cv.put(px + 1, y, 'B')
        for x in range(X0 + 1, X0 + 16): cv.put(x, Y0 + 2, 'b')
        for y in range(Y0 + 3, Y0 + 13):
            sag = int(2 * math.sin(math.pi * (y - Y0) / 13))
            for x in range(X0 + 3, X0 + 14):
                if (x + y) % 3 == 0 or (x - y) % 3 == 0: cv.put(x, y + sag // 2, 'C' if y < Y0 + 8 else 'n')
        for x in range(X0 + 2, X0 + 15): cv.shade(x, Y0 + 15)
    elif t == 'K':
        # stacked crates
        for (x, y) in _rect(X0 + 2, Y0 + 13, X0 + 15, Y0 + 15): cv.shade(x, y)
        _box(cv, _rect(X0 + 1, Y0 + 7, X0 + 8, Y0 + 14), 'd', 'D', 'b')
        _box(cv, _rect(X0 + 9, Y0 + 8, X0 + 14, Y0 + 14), 'd', 'D', 'b')
        _box(cv, _rect(X0 + 3, Y0 + 1, X0 + 10, Y0 + 6), 'd', 'D', 'b')
        for (a, b, c, d) in ((1, 7, 8, 14), (3, 1, 10, 6)):
            for i in range(c - a + 1):
                cv.put(X0 + a + i, Y0 + b + int(i * (d - b) / max(1, c - a)), 'b')
    elif t == 'E':
        # lobster pots: little domed wicker cages
        for (cx, cy) in ((X0 + 5, Y0 + 10), (X0 + 11, Y0 + 11), (X0 + 8, Y0 + 5)):
            for (x, y) in blob(cx + 1, cy + 3, 4.5, 1.5): cv.shade(x, y)
            pts = set((x, y) for (x, y) in blob(cx, cy, 4, 3.5) if y <= cy + 2)
            for (x, y) in pts: cv.put(x, y, 'b' if (x - cx) % 2 else 'J' if y < cy else 'd')
            for (x, y) in pts:
                if (x, y - 1) not in pts or (x - 1, y) not in pts or (x + 1, y) not in pts: cv.put(x, y, 'B')
            for x in range(cx - 3, cx + 4): cv.put(x, cy + 2, 'B')
            cv.put(cx, cy - 1, 'o'); cv.put(cx + 1, cy - 1, 'o')
    elif t == 'S':
        # a market stall, empty: an old patched sailcloth stretched over poles (over you), a bare plank counter
        left = at(tx + 1, ty) == 'S'
        for (x, y) in _rect(X0, Y0 + 12, X0 + 15, Y0 + 15): cv.shade(x, y)
        for y in range(Y0 + 6, Y0 + 13):
            for x in range(X0, X0 + T):
                cv.put(x, y, 'o' if y in (Y0 + 6, Y0 + 12) else 'D' if y == Y0 + 7 else 'b')
        px = X0 + 1 if left else X0 + 13
        for y in range(Y0 - 8, Y0 + 13): cv.put(px, y, 'B', y < Y0 + 6); cv.put(px + 1, y, 'b', y < Y0 + 6)
        for y in range(Y0 - 12, Y0 - 4):
            for x in range(X0 - (1 if left else 0), X0 + T + (0 if left else 1)):
                sag = 1 if 3 < (x - X0) % 16 < 12 else 0
                k = 'd' if (x - X0) % 6 else 'D'
                if (y - Y0 + 12) == 1: k = 'z' if (x + tx) % 3 else 'd'
                if (x - X0 + tx * 5) % 16 in (9, 10, 11) and Y0 - 10 <= y <= Y0 - 7: k = 'D' if (x + y) % 2 else 'b'   # a patch
                if y == Y0 - 5: k = 'D' if (x % 3) else 'b'
                if y == Y0 - 12: k = 'o'
                cv.put(x, y + (sag if y > Y0 - 9 else 0), k, True)
                if y == Y0 - 5 and (x % 4 == 0 or sag): cv.put(x, y + sag + 1, 'b' if x % 4 == 0 else 'o', True)
        if left:
            for (x, y) in blob(X0 + 7, Y0 + 9, 2, 1.2): cv.put(x, y, 'z')     # one sorry basket left
    elif t == 'e':
        # a bench
        for x in range(X0 + 1, X0 + 15):
            cv.put(x, Y0 + 7, 'o'); cv.put(x, Y0 + 8, 'd'); cv.put(x, Y0 + 9, 'D'); cv.put(x, Y0 + 10, 'o')
            cv.shade(x, Y0 + 13)
        for lx in (X0 + 2, X0 + 12):
            for y in range(Y0 + 11, Y0 + 13): cv.put(lx, y, 'B'); cv.put(lx + 1, y, 'o')


# ------------------------------------------------------------------ the outpost (21 Oct): palisade, gateposts, the wreck

WALL = 'ZI'


def _log(cv, cx, base, w, h, tip, over_y, r, bark=('o', 'D', 'b', 'B'), post=False, pale=True):
    """One upright sharpened log: bark in vertical streaks lit from the left, a pale cut point on top.
    Pixels above over_y go on the overlay (drawn over whoever stands behind it)."""
    ol, lit, mid, dk = bark
    x0 = cx - w // 2
    top = base - h
    knots = {(r.randint(1, w - 2), r.randint(top + 2, base - 2)) for _ in range(2 if post else 1)}
    for y in range(top, base + 1):
        for i in range(w):
            x = x0 + i
            if i == 0 or i == w - 1 or y == base: k = ol
            elif i == 1: k = lit if (y + cx) % 5 else mid
            elif i == w - 2: k = dk
            else:
                k = mid
                if (y * 3 + i * 7 + cx) % 9 == 0 or (i == w // 2 and (y + cx) % 4 == 0): k = dk
                if post and i == 2 and (y + cx) % 3 == 0: k = lit
            if (i, y) in knots: k = 'o'
            cv.put(x, y, k, y < over_y)
    # the sharpened point: two cut facets, the lit one paler
    for j in range(tip):
        y = top - tip + j
        half = (w / 2.0) * (j + 1) / (tip + 0.5)
        for x in range(x0, x0 + w):
            d = x + 0.5 - (x0 + w / 2.0)
            if abs(d) > half + 0.25: continue
            if pale: k = 'd' if d < 0 else ('D' if (x + j) % 3 else 'J')
            else: k = 'D' if d < -1 else ('b' if d < 1.5 else 'B')
            if abs(d) > half - 0.75: k = ol
            if j == 0 and abs(d) <= 1: k = 'd' if d < 0 else 'D'
            cv.put(x, y, k, y < over_y)
    cv.put(x0 + w // 2 - (0 if w % 2 else 1), top - tip - 1, ol, True)
    # a ring where the bark was cut: the top edge of the trunk shows lighter wood
    for i in range(1, w - 1):
        cv.put(x0 + i, top, 'D' if i < w // 2 else 'b', top < over_y)


def _band(cv, x0, x1, y, over):
    """A lashing of rope across logs."""
    for x in range(x0, x1):
        cv.put(x, y, 'J' if x % 2 else 'd', over)
        cv.put(x, y + 1, 'A' if x % 2 else 'D', over)
        if x % 3 == 0: cv.put(x, y + 2, 'B', over)


def paint_walls(cv, rows, seed):
    H, W = len(rows), len(rows[0])
    at = lambda x, y: rows[y][x] if 0 <= x < W and 0 <= y < H else '.'
    logs = []     # (base, cx, w, h, tip, over_y, rnd, post, band_y), and whether the point is pale (seen side-on)
    extras = []
    for ty in range(H):
        for tx in range(W):
            t = rows[ty][tx]
            if t not in WALL: continue
            X0, Y0 = tx * T, ty * T
            r = random.Random(tx * 977 + ty * 5233 + seed)
            lf, rt = at(tx - 1, ty) in WALL, at(tx + 1, ty) in WALL
            up, dn = at(tx, ty - 1) in WALL, at(tx, ty + 1) in WALL
            if t == 'I':
                # a gatepost: one thick tall log, a crossbeam stub reaching over the gap, a skull nailed up
                for (x, y) in blob(X0 + 11, Y0 + 15, 7, 2.5): cv.shade(x, y)
                logs.append((Y0 + 14, X0 + 8, 10, 27, 5, Y0 + 4, r, True, Y0 + 4, True))
                gap = -1 if not lf else 1 if not rt else 0
                extras.append((X0, Y0, gap, r))
                continue
            horiz = lf or rt or not (up or dn)
            vert = up or dn
            # shadow: down and to the right
            if horiz:
                xa = X0 + (8 if vert and not lf else 0)
                xb = X0 + (8 if vert and not rt else T)
                for x in range(xa + 2, xb + 3):
                    for y in range(Y0 + 14, Y0 + 17): cv.shade(x, y)
                for x in range(xa, xb):        # a churned strip at the foot, a few tufts left
                    cv.put(x, Y0 + 14, 'B' if (x + ty) % 3 else 'v')
                    if r.random() < 0.15: cv.put(x, Y0 + 15, 'F'); cv.put(x, Y0 + 14, 'i')
                for i, cx in enumerate(range(xa + 2, xb, 4)):
                    logs.append((Y0 + 13, cx, 5, 17 + r.randint(-1, 2), 3, Y0 + 4, r, False, Y0 + 3, True))
            if vert:
                ya = Y0 + (0 if up else 6)
                yb = Y0 + (T if dn else 15)
                for y in range(ya, yb + 2):
                    for x in range(X0 + 12, X0 + 16): cv.shade(x, y)
                for by in range(Y0 + 4, Y0 + T + 1, 5):
                    if by > Y0 + 13 and not dn: continue
                    if by < Y0 + 8 and not up and horiz: continue
                    logs.append((by, X0 + 8 + r.choice((-1, 0, 0, 1)), 8, 17 + r.randint(-1, 1), 4, by - 9, r, False, by - 10, False))
    logs.sort(key=lambda l: (l[0], l[1]))
    for (base, cx, w, h, tip, over_y, r, post, band_y, pale) in logs:
        _log(cv, cx, base, w, h, tip, over_y, r, post=post, pale=pale)
        _band(cv, cx - w // 2 + 1, cx - w // 2 + w - 1, band_y, band_y < over_y)
        if post: _band(cv, cx - w // 2 + 1, cx - w // 2 + w - 1, band_y - 9, True)
    for (X0, Y0, gap, r) in extras:
        cx = X0 + 8
        if gap:
            # the stub of a crossbeam, pegged into the post, reaching out over the gate
            sx = cx + 5 if gap > 0 else cx - 5
            for i in range(7):
                x = sx + i * gap
                for y, k in ((Y0 - 9, 'o'), (Y0 - 8, 'D'), (Y0 - 7, 'b'), (Y0 - 6, 'B'), (Y0 - 5, 'o')):
                    cv.put(x, y, k, True)
            ex = sx + 6 * gap
            for y, k in ((Y0 - 8, 'd'), (Y0 - 7, 'D'), (Y0 - 6, 'b')): cv.put(ex, y, k, True)
            cv.put(sx + 2 * gap, Y0 - 9, 'g', True)
        # a boar's skull nailed to the post, tusks curling
        sk = ['.oooo.', 'oWWWWo', 'WoWWoW', 'oWWYWo', '.oYYo.', 'Wo..oW', '.o..o.']
        for j, row in enumerate(sk):
            for i, ch in enumerate(row):
                if ch != '.': cv.put(cx - 3 + i, Y0 - 3 + j, ch, Y0 - 3 + j < Y0 + 4)
        cv.put(cx - 1, Y0 - 4, 'g', True)


def paint_wreck(cv, rows, tx, ty, r):
    """A trade ship's hull heeled over on its side and half sunk in the sand, its stern snapped off: drawn once from
    the block's top-left tile across the whole block of Q tiles. Seen from above you get the outside of the hull
    along the top, the gunwale, then the hold with its ribs, half full of drifted sand; the lower side is buried."""
    H, W = len(rows), len(rows[0])
    at = lambda x, y: rows[y][x] if 0 <= x < W and 0 <= y < H else None
    if at(tx - 1, ty) == 'Q' or at(tx, ty - 1) == 'Q': return
    tx1, ty1 = tx, ty
    while at(tx1 + 1, ty) == 'Q': tx1 += 1
    while at(tx, ty1 + 1) == 'Q': ty1 += 1
    X0, Y0 = tx * T, ty * T
    PW, PH = (tx1 - tx + 1) * T, (ty1 - ty + 1) * T
    L = PW - 4
    hx0 = X0 + 2
    hw_max = PH * 0.40
    band = max(6, int(PH * 0.27))      # how much of the outside of the hull shows

    def hw(u):                        # half-width of the hull: full amidships, a pointed bow on the right
        if u > 0.62: return hw_max * max(0.0, 1 - ((u - 0.62) / 0.38) ** 1.6)
        return hw_max * (0.86 + 0.14 * math.sin(u / 0.62 * math.pi / 2))

    def mid(u):                       # lying a little askew
        return Y0 + PH * 0.55 + (0.5 - u) * PH * 0.12

    shell, outside, hold = {}, set(), set()
    for X in range(hx0, hx0 + L):
        u = (X - hx0) / L
        h = hw(u)
        if h < 1: continue
        yt = int(round(mid(u) - h))
        yb = int(round(mid(u) + h))
        if u < 0.13: continue         # the stern's gone: only ribs left standing
        rag = r.randint(0, 3) if u < 0.17 else 0
        for Y in range(yt + rag, yb + 1):
            shell[(X, Y)] = u
            if Y < yt + band * min(1.0, h / hw_max + 0.25): outside.add((X, Y))
            else: hold.add((X, Y))
    # its shadow on the sand, down and right
    for (X, Y) in list(shell):
        for (dx, dy) in ((2, 3), (4, 2), (3, 4)):
            if (X + dx, Y + dy) not in shell: cv.shade(X + dx, Y + dy)
    # the outside of the hull: strakes along the length, sun-bleached grey above, dark and weedy toward the keel
    tops = {}
    for (X, Y) in shell:
        tops[X] = min(tops.get(X, 9999), Y)
    for (X, Y) in outside:
        d = Y - tops[X]
        strake = d // 2
        k = ('Y', 'y', 'D', 'y', 'D', 'b', 'D', 'b', 'b')[min(strake, 8)] if d % 2 == 0 else ('y', 'G', 'D', 'b', 'b', 'B', 'b', 'B', 'B')[min(strake, 8)]
        if (X * 5 + strake * 13) % 19 == 0: k = 'g'        # butt joints, trenails
        cv.put(X, Y, k)
    # the hold: dark, the far side's planks, ribs curving across, sand drifted in
    for (X, Y) in hold:
        u = shell[(X, Y)]
        k = 'B' if (Y + X // 9) % 3 else 'o'
        cv.put(X, Y, k)
    gun = {}
    for (X, Y) in hold:
        if (X, Y - 1) in outside: gun[X] = Y
    for X in range(hx0 + 2, hx0 + L, 5):
        if X not in gun: continue
        for j in range(0, int(hw_max * 2)):
            x = X + int(round(1.5 * math.sin(j / 5.0)))
            y = gun[X] + j
            if (x, y) not in hold: break
            cv.put(x, y, 'D' if j < 3 else 'b'); cv.put(x + 1, y, 'B' if j < 3 else 'o')
            cv.put(x - 1, y, 'd' if j < 2 else cv.get(x - 1, y))
    for X, Y in gun.items():                       # the gunwale: a lit rail
        cv.put(X, Y, 'd' if X % 7 else 'D'); cv.put(X, Y + 1, 'o')
    # sand drifted into the hold, filling its low side; drifts piled against the outside of the low gunwale
    sand = set()
    wob = r.uniform(0, 6)
    for (X, Y) in hold:
        u = shell[(X, Y)]
        if Y > mid(u) + hw(u) * 0.5 + 2.5 * math.sin(X / 7.0 + wob) and (X, Y + 2) in shell: sand.add((X, Y))
    for i in range(int(L / 7)):
        X = hx0 + 10 + i * 7 + r.randint(-2, 2)
        u = min((X - hx0) / L, 0.99)
        if hw(u) < 2: continue
        sand |= set(blob(X, mid(u) + hw(u) + 2, r.uniform(4, 7), r.uniform(2, 3)))
    for i in range(3):     # and over the stern's stumps
        sand |= set(blob(hx0 + 4 + i * 5, mid(0.05) + hw(0.05) * 0.5 + r.uniform(-1, 2), r.uniform(4, 6), r.uniform(2.5, 4)))
    # outline the shell where it isn't buried
    for (X, Y) in shell:
        if any((X + dx, Y + dy) not in shell for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))): cv.put(X, Y, 'o')
    # stove in: a ragged hole through the outside planking, splintered ends
    hcx = hx0 + int(L * 0.42)
    for X in range(hcx - 6, hcx + 7):
        if X not in tops: continue
        depth = int(band * 0.8 * (1 - abs(X - hcx) / 7.5)) + r.randint(-1, 1)
        for j in range(2, max(2, depth)):
            cv.put(X, tops[X] + j, 'e' if j > 2 else 'o')
        if depth > 2: cv.put(X, tops[X] + 1, 'd' if X % 2 else 'D')
    # the snapped stern: ribs standing up like bones
    for i in range(3):
        bx = hx0 + 2 + i * 4
        u = (bx - hx0) / L
        yt = int(mid(u) - hw(u)) - 3 + (i % 2) * 3 + i
        yb = int(mid(u) + hw(u) * 0.6)
        for y in range(yt, yb):
            f = (y - yt) / max(1, yb - yt)
            x = bx - int(round(3 * (1 - f) ** 2))
            ov = y < Y0
            cv.put(x - 1, y, 'o', ov); cv.put(x, y, 'd' if y < yt + 4 else 'D', ov); cv.put(x + 1, y, 'b', ov); cv.put(x + 2, y, 'o', ov)
        cv.put(bx - 3, yt - 1, 'o', yt - 1 < Y0); cv.put(bx - 3, yt, 'o', yt < Y0)
    # the stem post at the bow, standing up
    bx = hx0 + L - 2
    while bx > hx0 and bx not in tops: bx -= 1
    for j in range(7):
        x, y = bx - 1 + j // 3, tops.get(bx, Y0) + 1 - j
        cv.put(x - 1, y, 'o', y < Y0); cv.put(x, y, 'd', y < Y0); cv.put(x + 1, y, 'D', y < Y0); cv.put(x + 2, y, 'o', y < Y0)
    cv.put(bx + 1, tops.get(bx, Y0) - 6, 'o', True); cv.put(bx + 2, tops.get(bx, Y0) - 6, 'o', True)
    # the broken mast, fallen out over the low side into the sand, its end splintered
    mx = hx0 + int(L * 0.5)
    my = int(mid(0.5) - 2)
    n = int(PH * 0.55)
    for i in range(n):
        y = my + i
        x = mx - i // 2
        for j, k in enumerate(('o', 'd', 'D', 'b', 'B', 'o')):
            cv.put(x + j, y, k)
    for j, k in enumerate(('o', 'd', 'o', 'D', 'd', 'o')):
        cv.put(mx - n // 2 + j, my + n + (j % 2), k)
    for j in range(6): cv.put(mx + j, my - 1, 'o'); cv.put(mx + j, my, 'D' if j < 3 else 'b')
    # the low gunwale, just showing above the sand
    for (X, Y) in shell:
        if (X, Y + 1) not in shell and (X, Y - 2) in shell:
            cv.put(X, Y, 'o'); cv.put(X, Y - 1, 'D' if X % 5 else 'b'); cv.put(X, Y - 2, 'o')
    # sand drifted against and into it
    for (x, y) in sand:
        k = 'd'
        if (x, y - 1) not in sand and (x, y - 1) in shell: k = 'D'      # a shadowed lip where it meets the planks
        elif (x - 1, y - 1) not in sand or (x, y - 1) not in sand: k = 'z'
        elif (x * 3 + y * 5) % 13 == 0: k = 'D'
        cv.put(x, y, k)
    # seaweed draped over the side, barnacles along the old waterline
    for _ in range(int(L / 9)):
        X = r.randint(hx0 + 12, hx0 + L - 10)
        if X not in tops: continue
        y0 = tops[X] + r.randint(0, 2)
        for j in range(r.randint(3, band + 3)):
            x = X + int(round(math.sin(j * 1.1 + X) * 1.0))
            cv.put(x, y0 + j, 'v' if j % 3 else 'F'); cv.put(x + 1, y0 + j, 'V')
    for _ in range(int(L / 3)):
        X = r.randint(hx0 + 4, hx0 + L - 8)
        if X not in tops: continue
        Y = tops[X] + band - r.randint(1, 3)
        if (X, Y) in outside: cv.put(X, Y, 'W' if r.random() < 0.3 else 'Y'); cv.put(X + 1, Y + 1, 'g')


# ------------------------------------------------------------------ bridges (21 Oct)

PLANK = {'d': ('z', 'd', 'D'), 'D': ('d', 'D', 'b'), 'b': ('D', 'b', 'B')}   # lit edge, face, shaded edge


def paint_bridges(cv, rows, cls, seed):
    """Each run of 'b' tiles becomes one bridge, drawn whole: a deck of planks laid across the way you walk, log rails
    on posts (rope rails on a one-tile footbridge), pilings standing in the water with foam round them, the deck's
    shadow on the stream, and a sill log with a few stones where it rests on each bank. Which way the bridge runs is
    read from the tiles beside it: water to the sides means you walk it up and down."""
    H, W = len(rows), len(rows[0])
    cells = {(x, y) for y in range(H) for x in range(W) if rows[y][x] == 'b'}
    seen = set()
    for c0 in sorted(cells, key=lambda p: (p[1], p[0])):
        if c0 in seen: continue
        comp, i = [c0], 0
        seen.add(c0)
        while i < len(comp):
            x, y = comp[i]; i += 1
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (x + dx, y + dy)
                if n in cells and n not in seen: seen.add(n); comp.append(n)
        _bridge(cv, rows, cls, comp, random.Random(seed * 31 + c0[0] * 977 + c0[1] * 131))


def _bridge(cv, rows, cls, comp, r):
    H, W = len(rows), len(rows[0])
    PH, PW = cls.shape

    def wet_tile(x, y):
        return 0 <= x < W and 0 <= y < H and rows[y][x] == 'w'
    side = sum(wet_tile(x - 1, y) + wet_tile(x + 1, y) for x, y in comp)
    ends = sum(wet_tile(x, y - 1) + wet_tile(x, y + 1) for x, y in comp)
    vert = side >= ends                       # walked up and down; the stream runs left to right under it
    xs, ys = [p[0] for p in comp], [p[1] for p in comp]
    a0, a1 = (min(ys), max(ys)) if vert else (min(xs), max(xs))
    c0, c1 = (min(xs), max(xs)) if vert else (min(ys), max(ys))
    V0, Wd = c0 * T, (c1 - c0 + 1) * T       # across the deck
    wide = Wd >= 2 * T

    def P(u, v):                              # bridge space (u along the way you walk, v across) to pixels
        return (V0 + v, U0 + u) if vert else (U0 + u, V0 + v)

    def water_at(x, y):
        return 0 <= x < PW and 0 <= y < PH and cls[y, x] == STREAM

    # how far the water reaches under the deck decides where the banks are
    U0 = 0
    wetA = [A for A in range((a0 - 1) * T, (a1 + 2) * T)
            if any(water_at(*((V0 + v, A) if vert else (A, V0 + v))) for v in range(Wd))]
    ws, we = (min(wetA), max(wetA)) if wetA else (a0 * T, (a1 + 1) * T - 1)
    start = max(min(a0 * T, ws - 3), a0 * T - 6)
    end = min(max((a1 + 1) * T, we + 4), (a1 + 1) * T + 6)
    U0, L = start, end - start
    wu0, wu1 = ws - U0, we - U0              # the water, in bridge space

    def put(u, v, k, over=False):
        cv.put(*P(u, v), k, over)

    def shade(u, v, n=1):
        x, y = P(u, v)
        if water_at(x, y): cv.shade(x, y, n)

    def wet(u, v):
        return water_at(*P(u, v))

    lo_d, hi_d = (-1, Wd) if wide else (2, Wd - 3)          # how far the planks reach across
    if not vert: hi_d = min(hi_d, Wd - 1)

    # the deck's shadow on the water: deep on the side away from the light, a thin rim on the lit side
    for u in range(-1, L + 1):
        for d in range(1, 6):
            shade(u, hi_d + d, 2 if d <= (3 if vert else 4) else 1)
        shade(u, lo_d - 1)
        if vert: shade(u, lo_d - 2)

    # pilings, with foam and ripples where they meet the water
    posts = [1, L - 5]
    if L > 34: posts.insert(1, L // 2 - 2)
    span = wu1 - wu0
    pu = [wu0 + span // 4, wu1 - span // 4] if span > 20 else [(wu0 + wu1) // 2 - 1]
    pu = [u for u in pu if 0 <= u < L - 3 and all(abs(u - q) > 5 for q in posts)]

    def foam(x, y, out_dx, out_dy):
        for (dx, dy, k) in ((-1, 0, 'c'), (1, 0, 'c'), (0, -1, 'c'), (0, 1, 'c'), (-1, -1, 'C'), (1, 1, 'C')):
            if water_at(x + dx, y + dy) and cv.get(x + dx, y + dy) not in 'oBbDd': cv.put(x + dx, y + dy, k)
        for i in range(2, 6):                 # a little wake trailing off, broken up
            for s in (-1, 1):
                X, Y = x + out_dx * i + (s * (i // 2) if out_dy == 0 else 0) * 0, y + out_dy * i
                X, Y = (x + out_dx * i, y + s * (i // 2)) if out_dy == 0 else (x + s * (i // 2), y + out_dy * i)
                if (i + s) % 3 and water_at(X, Y): cv.put(X, Y, 'c' if i < 4 else 'C')
        if water_at(x + out_dx * 2, y + out_dy * 2): cv.put(x + out_dx * 2, y + out_dy * 2, 'W')

    for u in pu:
        if vert:
            pw = 4 if wide else 3
            for (x0, out) in ((V0 + lo_d - pw - 1, -1), (V0 + hi_d + 2, 1)):
                y0 = U0 + u
                if not water_at(x0 + pw // 2, y0 + 3): continue
                _post(cv, x0, y0, pw, 3, 3)
                foam(x0 + (0 if out < 0 else pw - 1), y0 + 5, out, 0)
                for i in range(pw):
                    if water_at(x0 + i, y0 + 6): cv.put(x0 + i, y0 + 6, 'c' if i % 2 else 'W')
        else:
            pw = 4 if wide else 3
            if not wet(u + 1, hi_d + 4): continue
            x0, y0 = P(u, hi_d + 3)
            for j in range(4 if wide else 3):
                for i in range(pw):
                    k = ('o', 'b', 'B', 'o')[i] if pw == 4 else ('o', 'b', 'o')[i]
                    if j == 0 and 0 < i < pw - 1: k = 'D'
                    cv.put(x0 + i, y0 + j, k)
            y1 = y0 + (4 if wide else 3)
            for i in range(pw): cv.put(x0 + i, y1, 'W' if i % 2 else 'c')
            foam(x0 + pw // 2, y1, 0, 1)

    # the side of the deck: a stringer's dark edge over the water (the front face, on a bridge you cross sideways)
    for u in range(0, L):
        if vert:
            if wet(u, hi_d + 1): put(u, hi_d + 1, 'B')
        else:
            put(u, hi_d + 1, 'b' if u % 5 else 'B'); put(u, hi_d + 2, 'B'); shade(u, hi_d + 3, 2)

    # the deck: planks laid across the way, each lit along one edge and shaded on the other, with gaps between
    pitch = 5 if wide else 4
    for p in range(-(-L // pitch)):
        tone = r.choice('DDDDbbd')
        lit, mid, dark = PLANK[tone]
        lo = lo_d + r.choice((0, 0, 1)); hi = hi_d - r.choice((0, 0, 1))
        if not wide: lo, hi = lo - r.choice((0, 1)), hi + r.choice((0, 1))
        grain = [(r.randint(lo + 1, max(lo + 1, hi - 4)), r.randint(2, 5)) for _ in range(2 if wide else 1)]
        knot = r.randint(lo + 2, max(lo + 2, hi - 2)) if r.random() < 0.4 else None
        split = r.randint(lo + 3, max(lo + 3, hi - 3)) if r.random() < 0.15 else None
        for j in range(pitch):
            u = p * pitch + j
            if u >= L: break
            for v in range(lo_d - 1, hi_d + 1):
                if j == pitch - 1 or v < lo or v > hi:          # the gap between planks, or past a plank's end
                    if lo_d <= v <= hi_d or j == pitch - 1 and lo <= v <= hi:
                        put(u, v, 'o' if wet(u, v) and (v + p * 5) % 13 < 3 else 'B')
                    continue
                k = lit if j == 0 else dark if j == pitch - 2 else mid
                if v in (lo, hi) and j > 0: k = dark                      # plank ends
                if 0 < j < pitch - 2:
                    for (g0, gl) in grain:
                        if g0 <= v < g0 + gl and (j + p) % 2 == 0: k = dark
                    if knot is not None and v == knot and j == 1: k = PLANK['b'][2]
                    if split is not None and split <= v < split + 3 and j == pitch - 3: k = 'B'
                put(u, v, k)
        if wide:                                                # nails over the two inner stringers
            for sv in (Wd // 3, Wd - 1 - Wd // 3):
                if lo < sv < hi and p * pitch + 1 < L: put(p * pitch + 1, sv, 'B' if tone != 'b' else 'o')

    # sill logs where the deck rests on the banks, a few stones packed round them
    sv0, sv1 = (lo_d - 3, hi_d + 3) if wide else (lo_d - 2, hi_d + 2)
    for (s0, face) in ((-3, 'o'), (L, 'o')):
        for i, k in enumerate(('d', 'D', 'b')):
            for v in range(sv0, sv1 + 1):
                kk = k
                if v in (sv0, sv1): kk = ('D', 'b', 'B')[i]
                if (v * 3 + i) % 7 == 0 and i == 1: kk = 'b'
                put(s0 + i, v, kk)
        for v in range(sv0, sv1 + 1): put(s0 + 3, v, face if s0 == L or not vert else 'B')
        put(s0 + 1, sv0 - 1, 'o'); put(s0 + 1, sv1 + 1, 'o')
        for (sv, sd) in ((sv0 - 5, -1), (sv1 + 1, 1)):     # a big stone and a small one each side
            xa, ya = P(s0 - 1 + r.choice((0, 1)), sv); xb, yb = P(s0 + 3, sv + 4)
            _stone(cv, min(xa, xb), min(ya, yb), 5, 4)
            xa, ya = P(s0 + (4 if s0 < 0 else -3), sv + 1 + sd); xb, yb = P(s0 + (6 if s0 < 0 else -1), sv + 3 + sd)
            _stone(cv, min(xa, xb), min(ya, yb), 3, 3)

    # rails
    if wide:
        for u in range(1, L - 1):
            notch = (u * 7 + r.randint(0, 1)) % 9 == 0
            for (v0, inner_shadow) in ((lo_d, True), (hi_d - 2, False)):
                put(u, v0, 'd' if not notch else 'D'); put(u, v0 + 1, 'D' if not notch else 'b'); put(u, v0 + 2, 'b')
                if inner_shadow:
                    x, y = P(u, v0 + 3); cv.shade(x, y)
                else:
                    put(u, v0 - 1, 'B')
                    if vert: put(u, v0 + 3, 'B')
        for u in posts:
            for (v0, near) in ((lo_d - 2, False), (hi_d - 2, not vert)):
                xa, ya = P(u, v0); xb, yb = P(u + 4, v0 + 4)
                _post(cv, min(xa, xb), min(ya, yb), 5, 4, 2, over=near)
    else:
        rope = {}
        for (vr, sgn) in ((lo - 0 if False else lo_d - 1, -1), (hi_d + 1, 1)):
            for u in range(1, L - 1):
                put(u, vr, 'J' if u % 3 else 'A')
                x, y = P(u, vr + (1 if sgn < 0 else 0) + (0 if vert else 1))
                if not vert or sgn < 0: cv.shade(x, y)
            for u in posts:
                xa, ya = P(u + 1, vr - 1); xb, yb = P(u + 3, vr + 1)
                _post(cv, min(xa, xb), min(ya, yb), 3, 3, 2, over=not vert and sgn > 0)


def _post(cv, x0, y0, w, h, face, over=False):
    """A squared log post seen from above: its sawn top (lit from the upper left, a dark heart), then `face` rows of
    its front below, darkening down to where it stands."""
    for j in range(h + face):
        for i in range(w):
            corner = i in (0, w - 1) and j in (0, h + face - 1)
            if j < h:
                if corner and j == 0: k = 'B'
                elif j == 0 or i == 0: k = 'd'
                elif i == w - 1 or j == h - 1: k = 'b'
                else: k = 'D'
                if w >= 4 and h >= 3 and i == w // 2 and j == h // 2: k = 'b'
            else:
                k = ('b', 'B', 'o')[min(2, (j - h) * 3 // max(1, face))] if i < w - 1 else 'o'
                if i == 0 and j - h < face - 1: k = 'D' if j == h else 'b'
            cv.put(x0 + i, y0 + j, k)
            if over: cv.put(x0 + i, y0 + j, k, True)
    for j in range(h + face):                 # its shadow, to the right
        cv.shade(x0 + w, y0 + j + 1)


def _stone(cv, x0, y0, w, h):
    """A rounded field stone, lit from the upper left, sitting in its own shadow."""
    for i in range(w + 1): cv.shade(x0 + i, y0 + h)
    for j in range(h):
        for i in range(w):
            if i in (0, w - 1) and j in (0, h - 1) and w > 2: continue
            k = 'Y' if i + j == 1 or (i == 1 and j == 0) else 'g' if j == h - 1 or i == w - 1 else 'y' if i + j < w - 1 else 'G'
            cv.put(x0 + i, y0 + j, k)
