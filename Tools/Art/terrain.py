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

GRASS, HIGH, SAND, SEA, STREAM, DIRT, COBBLE = range(7)

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
    if t in '~P': return SEA
    if t == ',': return SAND
    if t in 'wb': return STREAM
    if t in '=D': return DIRT
    if t in 'cOAL': return COBBLE
    if t == 'n': return COBBLE if near('c') else GRASS
    if t in 'uhs': return HIGH
    if t in 'rR': return SAND if near(',~') else GRASS
    if t == 'H': return GRASS
    return GRASS


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
    for c in range(7):
        m = (cls_t == c).astype(float)
        if not m.any(): continue
        f = smooth(np.kron(m, np.ones((T, T))), 4 if c in (DIRT, COBBLE) else 7 if c == STREAM else 5)
        f = f + wobble * (1 if c % 2 else -1) * (0.4 if c in (DIRT, COBBLE) else 1)
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
                for y in range(Y0, Y0 + T):
                    for x in range(X0 + 1, X0 + T - 1):
                        k = 'D' if (y - Y0) % 4 else 'b'
                        if (x * 5 + y) % 23 == 0: k = 'd'
                        cv.put(x, y, k)
                    for x in (X0, X0 + T - 1): cv.put(x, y, 'B')
                for x in (X0 + 1, X0 + T - 2):
                    for y in range(Y0, Y0 + T): cv.put(x, y, 'b' if y % 4 else 'B')
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
