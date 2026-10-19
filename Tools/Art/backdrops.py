"""Battle backdrops, 384 x 128 pixels (3:1), painted in palette colours. The ground the fighters stand on is
the bottom fifth. One per kind of place; the game picks by the scene's place and falls back to the coast.
A place can also have a night version (name_night): the same scene, painted from the same random numbers so
the land is identical, under a dark sky with stars and a moon, with the ground darkened and cooled.

Scenes are painted as a grid of palette letters on a Canvas, then turned into an image at the end. For night,
everything painted after the sky goes through NIGHT (a palette letter to a darker, bluer palette letter);
`raw=True` skips that, for things that shine by themselves (the moon's path on the sea).

Keep the middle of the bottom part plain: you stand on the left, the enemy on the right, both on the ground."""
import math, random
from PIL import Image
from palette import PAL
from terrain import vnoise

W, H = 384, 128

# what each colour becomes by moonlight: darker, colder, less colour
NIGHT = {
    'W': 'Y', 'Y': 'y', 'y': 'G', 'G': 'g', 'g': 'e', 'e': 'o',
    'z': 'y', 'd': 'G', 'D': 'g', 'b': 'B', 'B': 'o', 'J': 'y', 'j': 'B', 'h': 'H', 'H': 'o',
    'f': 'n', 'i': 'l', 'F': 'N', 'v': 'e', 'V': 'o',
    'c': 'C', 'C': 'n', 'w': 'l', 'l': 'X', 'X': 'X', 'n': 'N', 'N': 'e',
    'a': 'A', 'A': 'B', 'r': 'R', 'R': 'm', 'm': 'o',
    's': 'S', 'S': 'q', 'q': 'Q', 'Q': 'H', 'p': 'P', 'P': 'u', 'u': 'o',
}
NIGHT_SKY = [(0, 'e'), (16, 'X'), (34, 'N'), (46, 'l')]
NIGHT_CLOUD = {'W': 'y', 'Y': 'G', 'y': 'g', 'G': 'N'}
# one step lighter, for sunlight falling through leaves
LIGHTER = {'V': 'v', 'v': 'F', 'F': 'i', 'i': 'f', 'f': 'J', 'B': 'b', 'b': 'D', 'D': 'd', 'o': 'B', 'g': 'G', 'G': 'y'}
BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def C(k):
    return tuple(int(PAL[k][i:i + 2], 16) for i in (1, 3, 5)) + (255,)


def dith(x, y, d):
    return BAYER[y & 3][x & 3] + 0.5 < d * 16


class Canvas:
    def __init__(self, fill='N'):
        self.k = [[fill] * W for _ in range(H)]
        self.day = [[fill] * W for _ in range(H)]      # what each pixel would be by day, before NIGHT
        self.remap = None

    def put(self, x, y, k, raw=False):
        if 0 <= x < W and 0 <= y < H:
            self.day[y][x] = k
            if self.remap and not raw: k = self.remap.get(k, k)
            self.k[y][x] = k

    def get(self, x, y):
        return self.k[y][x] if 0 <= x < W and 0 <= y < H else None

    def lighten(self, x, y):
        """One step lighter than it would be by day (then darkened again for night)."""
        if 0 <= x < W and 0 <= y < H:
            k = self.day[y][x]
            self.put(x, y, LIGHTER.get(k, k))

    def image(self):
        img = Image.new('RGBA', (W, H))
        px = img.load()
        cols = {k: C(k) for k in PAL}
        for y in range(H):
            for x in range(W):
                px[x, y] = cols[self.k[y][x]]
        return img


def sky(cv, horizon, bands):
    """Flat bands of sky, with a checker row where two bands meet."""
    for y in range(horizon):
        k = [c for (start, c) in bands if y >= start][-1]
        nxt = [start for (start, c) in bands if start > y]
        if nxt and nxt[0] - y == 1:
            k2 = [c for (start, c) in bands if start == nxt[0]][0]
            for x in range(W): cv.put(x, y, k2 if (x + y) % 2 else k)
            continue
        for x in range(W): cv.put(x, y, k)


def stars(cv, horizon, seed, count=120):
    rnd = random.Random(seed)
    skyk = {k for _, k in NIGHT_SKY}
    for _ in range(count):
        x, y = rnd.randrange(W), rnd.randrange(horizon - 4)
        if cv.get(x, y) not in skyk: continue
        q = rnd.random()
        if q < 0.08 and y > 2:
            cv.put(x, y, 'W')
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)): cv.put(x + dx, y + dy, 'G')
        else:
            cv.put(x, y, 'W' if q < 0.25 else 'Y' if q < 0.55 else 'y' if q < 0.85 else 'c')


def moon(cv, cx, cy, r=8):
    """A full moon with grey seas on its face, inside a dithered halo."""
    skyk = {k for _, k in NIGHT_SKY}
    for y in range(cy - r - 7, cy + r + 8):
        for x in range(cx - r - 7, cx + r + 8):
            d = math.hypot(x + .5 - cx, y + .5 - cy)
            if d <= r:
                k = 'W'
                if math.hypot(x - cx + 3, y - cy + 2) < 3 or math.hypot(x - cx - 2, y - cy - 3) < 2.3 or math.hypot(x - cx - 3, y - cy + 3) < 1.6:
                    k = 'Y'
                if d > r - 1.2 and x > cx: k = 'Y'
                cv.put(x, y, k)
            elif cv.get(x, y) in skyk:
                if d <= r + 2.5 and dith(x, y, 0.75): cv.put(x, y, 'n')
                elif d <= r + 5 and dith(x, y, 0.35): cv.put(x, y, 'l')
                elif d <= r + 7 and dith(x, y, 0.12): cv.put(x, y, 'l')


def ridge(seed, base, amp, scales=((60, 1.0), (23, 0.45), (9, 0.18))):
    """A wandering skyline: y for each x."""
    rnd = random.Random(seed)
    ph = [(s, a, rnd.uniform(0, 6.28)) for s, a in scales]
    tot = sum(a for _, a in scales)
    return [base + amp * sum(a * math.sin(x / s + p) for s, a, p in ph) / tot for x in range(W)]


def puffy_clouds(cv, rnd, horizon, spots, cols=('W', 'Y', 'y')):
    for cx, cy, n in spots:
        puffs = [(cx + rnd.randrange(-n * 5, n * 5), cy + rnd.randrange(-3, 3), rnd.randrange(6, 12)) for _ in range(n)]
        for y in range(max(0, cy - 16), min(horizon, cy + 5)):
            for x in range(max(0, cx - n * 5 - 14), min(W, cx + n * 5 + 14)):
                for (px0, py0, r) in puffs:
                    if (x - px0) ** 2 + ((y - py0) * 1.6) ** 2 < r * r:
                        top = (x - px0) ** 2 + ((y - 2 - py0) * 1.6) ** 2 >= r * r
                        cv.put(x, y, cols[0] if top else (cols[1] if y < cy + 2 else cols[2]))
                        break


# ============================================================================================== the coast

def coast(night=False):
    cv = Canvas()
    rnd = random.Random(7)
    horizon = 56
    # Overcast sky in flat bands, with a checker row where two bands meet
    sky(cv, horizon, NIGHT_SKY if night else [(0, 'N'), (14, 'n'), (30, 'C'), (46, 'y')])
    if night:
        stars(cv, horizon, 70)
        moon(cv, 318, 17)
    # Heavy clouds: clumps of overlapping rounds with flat bellies
    cc = NIGHT_CLOUD if night else {}
    for cx, cy, n in ((70, 22, 5), (175, 14, 7), (270, 26, 6), (350, 12, 4), (20, 38, 3), (215, 40, 3)):
        puffs = [(cx + rnd.randrange(-n * 5, n * 5), cy + rnd.randrange(-3, 3), rnd.randrange(6, 12)) for _ in range(n)]
        for y in range(H):
            for x in range(W):
                if y > cy + 4 or y >= horizon: continue
                for (px0, py0, r) in puffs:
                    if (x - px0) ** 2 + ((y - py0) * 1.6) ** 2 < r * r:
                        top = (x - px0) ** 2 + ((y - 2 - py0) * 1.6) ** 2 >= r * r
                        k = 'Y' if top else ('y' if y < cy + 2 else 'G')
                        cv.put(x, y, cc.get(k, k))
                        break
    if night: cv.remap = NIGHT
    # The sea, with short wave glints
    for y in range(horizon, 80):
        for x in range(W):
            cv.put(x, y, 'w' if y < horizon + 2 else 'X')
    for _ in range(70):
        x, y = rnd.randrange(150, W - 6), rnd.randrange(horizon + 3, 78)
        for i in range(rnd.randrange(2, 6)):
            cv.put(x + i, y, 'w' if rnd.random() < 0.7 else 'C')
    if night:
        # the moon's path across the water
        mr = random.Random(71)
        for y in range(horizon + 1, 80):
            spread = 4 + (y - horizon) * 0.7
            for _ in range(3):
                x = int(318 + mr.uniform(-spread, spread))
                n = mr.randrange(2, 6)
                for i in range(n):
                    cv.put(x + i, y, 'Y' if (y < horizon + 6 and i < n - 1) else ('c' if i < n - 1 else 'C'), raw=True)
    # Far headland on the left, dark with a rock face to the sea
    for x in range(0, 160):
        top = 30 + int(26 * (x / 160) ** 2.2) + int(1.5 * math.sin(x / 5.0))
        face = x > 132
        for y in range(top, 80):
            if face and y > top + 3:
                k = 'g' if rnd.random() < 0.8 else 'G'
            else:
                k = 'v' if y < top + 2 else ('V' if rnd.random() < 0.8 else 'v')
            cv.put(x, y, k)
    # Near slope of grass
    for x in range(W):
        top = 76 + int(4 * math.sin(x / 23.0)) + int(1.5 * math.sin(x / 7.0))
        for y in range(top, 100):
            r = rnd.random()
            cv.put(x, y, 'f' if y == top else ('v' if r < 0.08 else 'f' if r < 0.13 else 'F'))
    # Gorse bushes
    for _ in range(22):
        bx, by, r = rnd.randrange(W), rnd.randrange(80, 96), rnd.randrange(4, 8)
        for y in range(by - r, by + 2):
            for x in range(bx - r - 3, bx + r + 3):
                if 0 <= x < W and ((x - bx) / (r + 3)) ** 2 + ((y - by) / r) ** 2 < 1:
                    q = rnd.random()
                    cv.put(x, y, 'A' if q < 0.06 else ('V' if y > by - 2 or q < 0.4 else 'v'))
    # The road
    for x in range(W):
        edge = 99 + int(1.5 * math.sin(x / 13.0))
        for y in range(edge, H):
            r = rnd.random()
            cv.put(x, y, 'B' if y == edge else ('B' if r < 0.08 else 'D' if r < 0.14 else 'b'))
    for _ in range(36):
        x, y = rnd.randrange(W - 3), rnd.randrange(103, H - 2)
        cv.put(x, y, 'y'); cv.put(x + 1, y, 'G'); cv.put(x, y + 1, 'g'); cv.put(x + 1, y + 1, 'g')
    return cv.image()


# ============================================================================================== the deep woods

def fern(cv, x0, y0, size, rnd, flip=1, cols=('f', 'i', 'F', 'v')):
    """A fern: a few arching fronds from one root, each with little leaflets."""
    for i in range(5):
        ang = math.radians(-160 + i * 35 + rnd.uniform(-8, 8))
        ln = size * rnd.uniform(0.7, 1.0)
        for s in range(int(ln)):
            t = s / ln
            x = x0 + math.cos(ang) * s * flip
            y = y0 + math.sin(ang) * s + (t * t) * ln * 0.55          # droops at the tip
            k = cols[0] if t < 0.3 else cols[1] if t < 0.7 else cols[2]
            cv.put(int(x), int(y), k)
            if s % 2 == 0 and t < 0.9:
                lf = max(1, int((1 - t) * 3))
                for j in range(1, lf + 1):
                    cv.put(int(x), int(y) - j, cols[1] if j == 1 else cols[2])
                    cv.put(int(x), int(y) + j, cols[3])


def trunk(cv, x0, w, top, bottom, rnd, body='b', shade='B', lit='D', deep='o', moss='F'):
    """A tree trunk: lit down the left edge, shaded right, bark streaks, flaring roots at the foot."""
    for y in range(top, bottom):
        flare = max(0, (y - (bottom - 8))) * 0.6
        xl, xr = x0 - flare, x0 + w + flare
        for x in range(int(xl), int(xr) + 1):
            u = (x - xl) / max(1, xr - xl)
            k = lit if u < 0.18 else body if u < 0.62 else shade if u < 0.9 else deep
            if k == body and (x * 7 + (y // 5) * 3) % 9 == 0: k = shade          # bark streaks
            if u < 0.4 and y > bottom - 26 and rnd.random() < 0.35 * (y - bottom + 26) / 26: k = moss
            cv.put(x, y, k)


def forest(night=False):
    cv = Canvas()
    rnd = random.Random(21)
    canopy_n = vnoise(H, W, 14, 21)
    floor_n = vnoise(H, W, 4, 22)
    horizon = 64
    sky(cv, horizon, NIGHT_SKY if night else [(0, 'y'), (10, 'Y'), (24, 'W')])
    if night:
        stars(cv, horizon, 80, 90)
        moon(cv, 214, 15, 7)
    cv.remap = NIGHT if night else None
    # the wood fading into the distance: a dim haze, lighter where a glade opens far off
    haze = vnoise(H, W, 40, 23)
    for y in range(26, 100):
        for x in range(W):
            glade = haze[y][x] + 0.35 * math.exp(-((x - 200) / 70.0) ** 2) - abs(y - 70) / 60
            k = 'i' if glade > 0.55 else 'F'
            if 0.48 < glade <= 0.55 and dith(x, y, (glade - 0.48) / 0.07): k = 'i'
            cv.put(x, y, k)
    # far trunks, thin and pale with the haze
    for _ in range(46):
        x, w = rnd.randrange(-4, W), rnd.choice((2, 2, 3, 3, 4))
        if abs(x - 213) < 12: continue                               # leave the moon's gap clear
        for y in range(20, 94):
            for i in range(w):
                cv.put(x + i, y, 'F' if i == 0 and cv.get(x + i, y) == 'i' else 'v')
    # far forest floor
    for x in range(W):
        top = 90 + int(2 * math.sin(x / 17.0))
        for y in range(top, 100):
            cv.put(x, y, 'v' if y < top + 2 or floor_n[y][x] < 0.5 else 'F')
    # the canopy: a ragged mass of leaves hanging from the top, with gaps of sky
    leafy = set()
    for y in range(0, 60):
        for x in range(W):
            v = canopy_n[y][x] * 0.9 + 0.75 - y / 44.0
            if math.hypot((x - 214) / 1.6, y - 15) < 15: v -= 0.8                # the gap the moon shows through
            if v > 0.5:
                edge = v < 0.56
                k = 'v' if edge else ('V' if canopy_n[(y + 9) % H][(x + 17) % W] < 0.6 else 'v')
                if edge and canopy_n[y][(x + 5) % W] > 0.5: k = 'F'
                cv.put(x, y, k)
                leafy.add((x, y))
    # leaves: little lit sprays inside the canopy, and ragged leaves hanging off its underside
    canopy = lambda x, y: (x, y) in leafy
    for _ in range(500):
        x, y = rnd.randrange(W), rnd.randrange(1, 52)
        if canopy(x, y) and canopy(x, y + 3) and canopy(x, y - 2):
            cv.put(x, y, 'v'); cv.put(x + 1, y, 'v'); cv.put(x - 1, y + 1, 'v'); cv.put(x + 1, y - 1, 'F' if y < 40 else 'v')
    for x in range(W):
        for y in range(58, 2, -1):
            if canopy(x, y) and not canopy(x, y + 1):
                n = (x * 7 + y) % 4 if math.hypot((x - 214) / 1.6, y - 15) > 12 else 0
                for i in range(1, n + 1): cv.put(x, y + i, 'V' if i < n else 'v'); leafy.add((x, y + i))
                break
    # trees in the middle distance, behind the fight; the middle stays open
    for x0, w in ((48, 9), (96, 7), (262, 8), (318, 10), (162, 5)):
        trunk(cv, x0, w, 0, 101, rnd)
    # light falling through the gaps in long slanting shafts
    for s0, sw in ((150, 12), (196, 7), (232, 16), (300, 6)):
        for y in range(10, 108):
            for x in range(W):
                u = x - s0 - (y - 10) * 0.45
                if 0 <= u < sw:
                    if canopy(x, y) and y < 60: continue                             # not across the leaves
                    edge = min(u, sw - 1 - u)
                    fadein = min(1, max(0, (y - 22) / 24))
                    if dith(x, y, (0.5 if edge > 1 else 0.25) * fadein):
                        cv.lighten(x, y)
    # the forest floor where the fight is: moss and leaf litter, dappled with light
    for y in range(98, H):
        for x in range(W):
            top = 98 + int(1.5 * math.sin(x / 11.0))
            if y < top: continue
            n = floor_n[y][x]
            k = 'F' if n > 0.64 else ('b' if n < 0.2 else 'v')
            if 0.61 < n <= 0.64 or 0.2 <= n < 0.23: k = 'v' if (x + y) % 2 else k
            if y == top: k = 'V'
            r = rnd.random()
            if r < 0.015: k = 'A'
            elif r < 0.025: k = 'D' if k == 'b' else 'i'
            cv.put(x, y, k)
    for cx, cy, rx, ry in ((205, 112, 26, 5), (262, 120, 18, 4), (150, 106, 12, 3), (310, 108, 10, 2.5), (110, 122, 16, 3)):
        for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
            for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
                d = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2
                if d < 1 and (d < 0.55 or dith(x, y, 0.5)):
                    cv.lighten(x, y)
    # roots across the floor from the near trees
    for x0, y0, dx in ((26, 104, 1), (358, 106, -1), (30, 116, 1), (354, 120, -1)):
        for i in range(30):
            x, y = x0 + i * dx, y0 + int(2 * math.sin(i / 5.0)) + i // 10
            cv.put(x, y, 'b'); cv.put(x, y + 1, 'B')
            if i < 18: cv.put(x, y - 1, 'D')
    # ferns round the edges, out of the way of the fighters
    for x, y, s, fl in ((40, 104, 12, 1), (70, 101, 9, -1), (318, 102, 10, 1), (344, 108, 13, -1), (100, 99, 7, 1),
                        (284, 100, 7, -1), (12, 124, 14, 1), (372, 126, 14, -1), (132, 99, 6, -1), (240, 99, 5, 1)):
        fern(cv, x, y, s, rnd, fl)
    # the great trunks right in front, framing the scene
    trunk(cv, -6, 24, 0, 128, rnd, body='B', shade='o', lit='b', deep='o', moss='v')
    trunk(cv, 366, 22, 0, 128, rnd, body='B', shade='o', lit='b', deep='o', moss='v')
    # leaves hanging in front at the top
    for y in range(0, 16):
        for x in range(W):
            v = canopy_n[(y * 3) % H][(x * 2) % W] + 0.55 - y / 14.0
            if v > 0.62: cv.put(x, y, 'V' if v > 0.7 else 'v')
    return cv.image()


# ============================================================================================== the hills

def boulder(cv, cx, cy, rx, ry, rnd):
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            dx, dy = (x + .5 - cx) / rx, (y + .5 - cy) / ry
            d = dx * dx + dy * dy
            if d > 1 or y > cy + ry * 0.55: continue
            lit = dx * -0.6 + dy * -0.8
            k = 'Y' if lit > 0.45 else 'y' if lit > -0.1 else 'G' if lit > -0.55 else 'g'
            if d > 0.82: k = 'g' if lit < 0.2 else 'G'
            if rnd.random() < 0.08: k = 'G' if k in 'Yy' else k
            cv.put(x, y, k)
    for x in range(int(cx - rx), int(cx + rx) + 1):
        cv.put(x, int(cy + ry * 0.55) + 1, 'V')                           # a shadow on the grass


def bush(cv, cx, cy, r, rnd, cols=('F', 'v', 'V')):
    for y in range(int(cy - r), int(cy + 2)):
        for x in range(int(cx - r * 1.4), int(cx + r * 1.4) + 1):
            if ((x - cx) / (r * 1.4)) ** 2 + ((y - cy) / r) ** 2 >= 1: continue
            q = rnd.random()
            up = y < cy - r * 0.45 and x < cx + r * 0.3
            cv.put(x, y, cols[0] if (up and q < 0.5) else cols[2] if (y > cy - 2 or q < 0.3) else cols[1])


def hills(night=False):
    cv = Canvas()
    rnd = random.Random(31)
    horizon = 60
    sky(cv, horizon, NIGHT_SKY if night else [(0, 'C'), (14, 'c'), (28, 'Y'), (40, 'W')])
    if night:
        stars(cv, horizon, 90, 140)
        moon(cv, 96, 16)
    cc = NIGHT_CLOUD if night else {}
    puffy_clouds(cv, rnd, horizon, ((40, 16, 4), (150, 24, 6), (290, 12, 5), (360, 30, 3)),
                 tuple(cc.get(k, k) for k in ('W', 'Y', 'y')))
    cv.remap = NIGHT if night else None
    # the far ridge, blue with distance
    for seed, base, amp, peak, body, top_k in ((32, 48, 6, 7, 'c', 'Y'), (35, 57, 4, 3, 'C', 'c')):
        far = ridge(seed, base, amp)
        far = [f - peak * abs(math.sin(x / 38.0 + seed)) ** 3 for x, f in enumerate(far)]     # a few peaks
        for x in range(W):
            top = int(far[x])
            for y in range(top, 76):
                cv.put(x, y, top_k if y == top else body)
    # two rolling hills, nearer and greener, each lit on the slopes facing left
    for base, amp, seed, light, mid, dark in ((64, 6, 33, 'i', 'F', 'v'), (78, 5, 34, 'f', 'i', 'F')):
        line = ridge(seed, base, amp, ((47, 1.0), (19, 0.35)))
        for x in range(W):
            top = int(line[x])
            slope = line[min(W - 1, x + 3)] - line[max(0, x - 3)]
            for y in range(top, 100):
                if y <= top + 1: k = light
                elif slope > 1.2: k = dark if dith(x, y, 0.6) else mid
                else: k = mid if (rnd.random() > 0.07) else dark
                cv.put(x, y, k)
        # a dry-stone wall following the hill
        if seed == 34:
            for x in range(0, 150):
                y = int(line[x]) + 6 + int(x * 0.05)
                cv.put(x, y - 1, 'Y' if x % 3 else 'y'); cv.put(x, y, 'y' if x % 4 else 'G'); cv.put(x, y + 1, 'g')
    # scrub and rocks on the slopes
    for _ in range(26):
        x, y = rnd.randrange(W), rnd.randrange(72, 92)
        if 140 < x < 250 and y > 84: continue
        bush(cv, x, y, rnd.randrange(2, 5), rnd)
    for x, y, rx, ry in ((60, 86, 7, 5), (300, 82, 9, 6), (226, 78, 4, 3), (112, 80, 3, 2), (350, 88, 5, 4)):
        boulder(cv, x, y, rx, ry, rnd)
    # the near ground: grass, with a worn path across it where the fight is
    for y in range(94, H):
        for x in range(W):
            top = 94 + int(2 * math.sin(x / 19.0))
            if y < top: continue
            r = rnd.random()
            k = 'f' if y == top else ('i' if r < 0.12 else 'F' if r < 0.18 else 'f')
            path = 112 + int(3 * math.sin(x / 41.0))
            pd = abs(y - path)
            if pd < 7 or (pd < 9 and dith(x, y, 0.5)):
                k = 'D' if r < 0.7 else ('b' if r < 0.88 else 'd')
            cv.put(x, y, k)
    for _ in range(140):
        x, y = rnd.randrange(W), rnd.randrange(96, H - 2)
        if cv.get(x, y) in 'fiF':
            cv.put(x, y, 'F'); cv.put(x, y - 1, 'i')                     # tufts
    for _ in range(20):
        x, y = rnd.randrange(W), rnd.randrange(97, H)
        if cv.get(x, y) == 'f': cv.put(x, y, rnd.choice('aWa'))         # flowers
    # big rocks and scrub in the near corners
    boulder(cv, 18, 118, 22, 15, rnd)
    boulder(cv, 372, 120, 20, 13, rnd)
    bush(cv, 44, 112, 6, rnd)
    bush(cv, 344, 112, 5, rnd)
    return cv.image()


# ============================================================================================== the edge of Skelmed

def house(cv, x0, w, base, wall_h, roof_h, roof, wall, door=None, windows=(), chimney=None, gable=False):
    top = base - wall_h
    # the wall
    for y in range(top, base):
        for x in range(x0, x0 + w):
            if wall == 'stone':
                row = (y - top) // 3
                k = 'G' if (y - top) % 3 == 2 or (x + row * 3) % 6 == 0 else ('Y' if (x + y) % 11 == 0 else 'y')
            elif wall == 'plank':
                k = 'b' if (x - x0) % 4 == 3 else ('d' if (x - x0) % 4 == 0 else 'D')
            else:                                                   # plaster and black timbers
                k = 'd' if not ((x - x0) % 12 in (0, 1) or y == top + wall_h // 2) else 'B'
            if x == x0 + w - 1: k = 'g' if wall == 'stone' else 'B'
            if y == base - 1: k = 'g' if wall == 'stone' else 'B'
            cv.put(x, y, k)
    for y in range(top, top + 2):
        for x in range(x0, x0 + w): cv.put(x, y, 'B')                # the shadow under the eaves
    for wx in windows:
        for y in range(top + 5, top + 10):
            for x in range(wx, wx + 5):
                edge = x in (wx, wx + 4) or y in (top + 5, top + 9)
                cv.put(x, y, 'B' if edge else ('c' if (x, y) == (wx + 1, top + 6) else 'e'))
        for x in range(wx - 1, wx + 6): cv.put(x, top + 10, 'D')
    if door is not None:
        for y in range(base - 11, base):
            for x in range(door, door + 6):
                edge = x in (door, door + 5) or y == base - 11
                cv.put(x, y, 'B' if edge else ('D' if (x - door) % 2 else 'b'))
        cv.put(door + 4, base - 6, 'a')
    # the roof
    if chimney is not None:
        for y in range(top - roof_h - 4, top - 2):
            for x in range(chimney, chimney + 5):
                cv.put(x, y, 'G' if x == chimney + 4 else ('Y' if x == chimney else 'y'))
        for x in range(chimney - 1, chimney + 6): cv.put(x, top - roof_h - 5, 'g')
    for y in range(top - roof_h, top + 1):
        t = (y - (top - roof_h)) / roof_h
        if gable:
            half = w / 2 + 2
            xl, xr = x0 + w / 2 - half * t, x0 + w / 2 + half * t
        else:
            xl, xr = x0 - 3 + (1 - t) * 6, x0 + w + 2 - (1 - t) * 6
        for x in range(int(xl), int(xr) + 1):
            row = (y - (top - roof_h))
            if roof == 'thatch':
                k = 'j' if row % 3 == 2 else ('z' if row == 0 else 'J')
                if (x * 5 + row * 7) % 13 == 0: k = 'j'
            elif roof == 'slate':
                k = 'n' if row % 3 == 2 or (x + (row // 3) * 4) % 8 == 0 else ('c' if row == 0 else 'C')
            else:
                k = 'R' if row % 3 == 2 or (x + (row // 3) * 3) % 6 == 0 else ('r')
            if y == top: k = 'B' if roof != 'thatch' else 'b'
            if x == int(xr) or x == int(xl): k = {'thatch': 'b', 'slate': 'N', 'tile': 'm'}[roof]
            cv.put(x, y, k)
    if chimney is not None and roof != 'slate':
        pass


def smoke(cv, x, y, rnd, n=6):
    for i in range(n):
        cx, cy, r = x + i * 3 + rnd.uniform(-1, 1), y - i * 5, 2.2 + i * 0.9
        d = 1 - i / (n + 1)
        for yy in range(int(cy - r), int(cy + r) + 1):
            for xx in range(int(cx - r), int(cx + r) + 1):
                if (xx - cx) ** 2 + (yy - cy) ** 2 <= r * r and dith(xx, yy, d):
                    cv.put(xx, yy, 'W' if yy < cy - r * 0.3 else 'Y')


def village():
    cv = Canvas()
    rnd = random.Random(41)
    horizon = 70
    sky(cv, horizon, [(0, 'n'), (16, 'C'), (32, 'c'), (52, 'Y')])
    puffy_clouds(cv, rnd, horizon, ((60, 14, 5), (190, 22, 6), (320, 10, 5)))
    # a hill behind, and the trees along it
    back = ridge(42, 60, 6)
    for x in range(W):
        for y in range(int(back[x]), 100):
            cv.put(x, y, 'i' if y < int(back[x]) + 2 else 'F')
    for _ in range(40):
        bush(cv, rnd.randrange(W), rnd.randrange(62, 74), rnd.randrange(4, 9), rnd, ('F', 'v', 'V'))
    # the cottages
    house(cv, 8, 58, 92, 22, 16, 'thatch', 'plaster', door=40, windows=(14, 26), chimney=18)
    house(cv, 84, 30, 90, 20, 18, 'slate', 'stone', windows=(96,), gable=True)
    house(cv, 286, 54, 92, 22, 15, 'tile', 'plank', door=300, windows=(318, 330), chimney=326)
    house(cv, 350, 40, 90, 20, 18, 'thatch', 'stone', windows=(362,), gable=True)
    smoke(cv, 20, 42, rnd)
    smoke(cv, 328, 44, rnd)
    # a cart-path verge and a fence along it
    for y in range(92, 104):
        for x in range(W):
            r = rnd.random()
            cv.put(x, y, 'f' if y == 92 else ('i' if r < 0.15 else 'F' if r < 0.22 else 'f'))
    for x in range(W):
        for y, k in ((94, 'D'), (95, 'b'), (99, 'D'), (100, 'b')):
            cv.put(x, y, k)
    for px0 in range(4, W, 22):
        for y in range(89, 104):
            for x in range(px0, px0 + 3):
                cv.put(x, y, 'd' if y == 89 else ('D' if x == px0 else 'b' if x == px0 + 1 else 'B'))
        cv.put(px0 + 3, 103, 'V'); cv.put(px0 + 4, 103, 'V')
    # flowers and tufts along the fence
    for _ in range(60):
        x, y = rnd.randrange(W), rnd.randrange(101, 105)
        cv.put(x, y, rnd.choice('fFiv'))
    for _ in range(16):
        x = rnd.randrange(W)
        cv.put(x, 101, rnd.choice('aWr')); cv.put(x, 102, 'F')
    # the cobbles
    for y in range(104, H):
        for x in range(W): cv.put(x, y, 'g')
    y = 104
    row = 0
    while y < H:
        sh = 3 + (y - 104) // 9                                    # bigger stones nearer you
        sw = sh * 2
        off = (row % 2) * (sw // 2 + 1)
        for x0 in range(-sw + off, W, sw + 1):
            ww = sw + rnd.randrange(-1, 2)
            for yy in range(y, y + sh):
                for xx in range(x0, x0 + ww):
                    dx = (xx - x0 + .5) / ww * 2 - 1
                    dy = (yy - y + .5) / sh * 2 - 1
                    if dx * dx + dy * dy * 0.6 > 1: continue
                    k = 'Y' if (dy < -0.4 and dx < 0.3) else ('G' if dy > 0.45 or dx > 0.7 else 'y')
                    cv.put(xx, yy, k)
        y += sh + 1
        row += 1
    for _ in range(50):
        x, yy = rnd.randrange(W), rnd.randrange(105, H)
        if cv.get(x, yy) == 'g' and (x < 90 or x > 300 or rnd.random() < 0.3): cv.put(x, yy, 'F')
    return cv.image()


# ============================================================================================== the road

def road():
    cv = Canvas()
    rnd = random.Random(51)
    horizon = 62
    sky(cv, horizon, [(0, 'n'), (14, 'C'), (30, 'c'), (48, 'Y'), (58, 'W')])
    # long thin clouds
    for cx, cy, ln in ((60, 12, 70), (200, 22, 90), (330, 8, 60), (130, 40, 50), (300, 36, 40)):
        for i in range(3):
            y = cy + i
            l = ln - i * 18
            for x in range(cx - l // 2 + i * 6, cx + l // 2 + i * 3):
                cv.put(x, y, 'W' if i == 0 else 'Y' if i == 1 else ('y' if x % 2 else 'Y'))
    # the sea, far off to the right, and the land sloping down to it
    for y in range(horizon - 4, horizon + 6):
        for x in range(180, W):
            cv.put(x, y, 'c' if y == horizon - 4 else 'w')
    for _ in range(30):
        x, y = rnd.randrange(240, W - 4), rnd.randrange(horizon - 2, horizon + 6)
        for i in range(rnd.randrange(2, 5)): cv.put(x + i, y, 'c')
    def land(x):
        if x < 200: return horizon - 6 - 10 * ((200 - x) / 200) ** 1.5 + 1.5 * math.sin(x / 15.0)
        return horizon - 6 + 12 * min(1, (x - 200) / 60) ** 0.8 + (x - 260) * 0.02 * (x > 260)
    for x in range(W):
        top = int(land(x))
        for y in range(top, 100):
            k = 'C' if y < top + 2 and top < horizon - 2 else ('F' if y < horizon + 10 else 'i')
            if top >= horizon - 3 and y == top: k = 'z'                       # a pale strand where it meets the sea
            cv.put(x, y, k)
    # far fields with hedgerows along them
    for y0, th in ((horizon + 8, 1), (horizon + 14, 2), (horizon + 22, 2)):
        for x in range(W):
            y = y0 + int(2 * math.sin(x / 31.0 + y0))
            if (x // 13) % 6 == 5: continue                                  # gateways
            bump = 1 if (x // 3) % 3 == 0 else 0
            for i in range(th + bump):
                cv.put(x, y - i, 'V' if i == 0 else 'v')
            cv.put(x, y - th - bump, 'F')
    # the near grassland, long and windblown
    for y in range(horizon + 22, H):
        for x in range(W):
            r = rnd.random()
            cv.put(x, y, 'f' if r < 0.6 else 'i' if r < 0.85 else ('J' if r < 0.92 else 'F'))
    for _ in range(420):
        x, y = rnd.randrange(W), rnd.randrange(horizon + 24, H)
        ln = 2 + (y - horizon) // 18
        k = rnd.choice('FiJj')
        for i in range(ln): cv.put(x + i // 2, y - i, k)
    # the road, from under your feet to the horizon
    for y in range(horizon + 2, H):
        t = (y - horizon) / (H - horizon)
        cx = 196 + 40 * (1 - t) ** 2 * math.sin(2.2 * (1 - t)) - 30 * t
        half = 2 + 200 * t ** 1.6
        for x in range(int(cx - half) - 1, int(cx + half) + 2):
            u = (x - cx) / half
            if abs(u) > 1: continue
            r = rnd.random()
            k = 'D' if r < 0.55 else ('d' if r < 0.68 else 'b')
            if 0.45 < abs(u) < 0.6: k = 'b' if r < 0.7 else 'B'               # wheel ruts
            if abs(u) < 0.1 and t > 0.1: k = 'F' if r < 0.5 else 'i'          # grass down the middle
            if abs(u) > 0.92: k = 'i' if r < 0.5 else 'b'                     # ragged verge
            cv.put(x, y, k)
    for _ in range(40):
        x, y = rnd.randrange(W - 2), rnd.randrange(104, H - 1)
        if cv.get(x, y) in 'Ddb':
            cv.put(x, y, 'y'); cv.put(x + 1, y, 'G'); cv.put(x, y + 1, 'g')
    # a lone windbent thorn tree on the left
    for y in range(58, 100):
        bend = ((100 - y) / 42) ** 1.6 * 16
        x = 44 + bend
        wd = 4 if y > 90 else 3
        for i in range(wd): cv.put(int(x) + i, y, 'B' if i == wd - 1 else ('D' if i == 0 else 'b'))
    for i in range(14):                                                     # a branch reaching downwind
        cv.put(58 + i, 66 - i // 3, 'b'); cv.put(58 + i, 67 - i // 3, 'B')
    for x in range(40, 52): cv.put(x, 100, 'V')
    for (bx, by, r) in ((60, 50, 9), (74, 50, 8), (50, 53, 7), (86, 56, 7), (66, 58, 8), (78, 61, 5)):
        for y in range(by - r, by + r):
            for x in range(bx - int(r * 1.6), bx + int(r * 1.6) + 1):
                d = ((x - bx) / (r * 1.6)) ** 2 + ((y - by) / r) ** 2
                if d >= 1: continue
                q = rnd.random()
                lit = y < by - r * 0.3 and x < bx + r * 0.5
                cv.put(x, y, ('f' if q < 0.5 else 'i') if lit else ('V' if (y > by + r * 0.3 or q < 0.2) else 'v'))
    # a milestone by the right of the road
    for y in range(88, 100):
        for x in range(316, 323):
            if y == 88 and x in (316, 322): continue
            cv.put(x, y, 'Y' if x == 316 else ('G' if x >= 321 else 'y'))
    cv.put(318, 92, 'g'); cv.put(319, 92, 'g'); cv.put(318, 94, 'g')
    for x in range(314, 326): cv.put(x, 100, 'V')
    return cv.image()


BACKDROPS = {
    'coast': coast,
    'forest': forest,
    'hills': hills,
    'village': village,
    'road': road,
    'coast_night': lambda: coast(night=True),
    'forest_night': lambda: forest(night=True),
    'hills_night': lambda: hills(night=True),
}

if __name__ == '__main__':
    import sys
    name = sys.argv[2] if len(sys.argv) > 2 else 'coast'
    BACKDROPS[name]().resize((384 * 4, 128 * 4), Image.NEAREST).save(sys.argv[1])
