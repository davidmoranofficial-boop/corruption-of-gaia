"""Battle backdrops, 360 x 136 pixels, drawn at a whole-number scale behind the fighters.

Layout (every backdrop):
- The fighters stand on a flat floor. Front fighters' feet are on FEET (y = 124); companions stand a little
  behind (y 114..120). The band from FLOOR_TOP (y ~ 100) down is kept calm: low-contrast ground, a few small
  clustered marks, nothing tall in the two fighter zones (10%..40% and 60%..90% of the width).
- Tall scenery (trees, houses, rocks) sits at the far left and right edges and in the background.
- Light comes from the upper left, so lit faces point up-left and shadows fall down-right.

Each outdoor backdrop also has a clouds layer, <name>_clouds: a 720 x 136 transparent PNG that tiles
horizontally (its right edge runs on into its left edge). The game drifts it slowly across the sky. The
backdrop itself only has the clear sky, so the two never double up. (The forest's layer holds drifting mist
between the trunks instead, as the canopy hides the sky.)

Night versions (<name>_night) paint the same land from the same random numbers, with every colour sent
through `nightify` (darker, colder, less colour), a night sky with stars and a moon, and lights that shine by
themselves (windows, the moon's path on the sea) painted `raw`, untouched by the night.

How the painting works: a Paint is a numpy RGBA canvas. Most things are painted with `ramp`: a mask, a float
"value" per pixel and a short list of colours (dark to light). Each pixel takes the colour its value falls
on, with ordered (Bayer) dithering only in a narrow band where two colours meet, so shapes get a few clean
colour steps and tidy dithered seams instead of noise."""
import math, random
import numpy as np
from PIL import Image, ImageDraw
from palette import PAL

W, H = 360, 136
CW = 720                      # clouds layer width (tiles horizontally)
FEET = 124                    # where the front fighters' feet are
FLOOR_TOP = 100               # the calm floor starts here

BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], float)
LIGHT = np.array([-0.55, -0.72, 0.42])
LIGHT /= np.linalg.norm(LIGHT)


def P(k):
    """A palette colour by its letter."""
    return PAL[k]


# Local colours: the same muted, slightly dusty family as palette.py, for things the game palette has no
# colour for (a summer sky, haze, dry grass, sand, heather).
SKY_DAY = ['#58758a', '#67849a', '#7a96a8', '#8fa9b4', '#a8bcbe', '#c2cdc4', '#d3d6c8']
CLOUD_DAY = ['#7f8a99', '#98a2ac', '#b3bab9', '#cdd0c8', '#e3e1d5']
SKY_NIGHT = ['#121826', '#171f2f', '#1d2738', '#243043', '#2c3a4e', '#364659', '#415264']
CLOUD_NIGHT = ['#1f2838', '#2a3446', '#374357', '#4a566a', '#65708a']
HAZE = '#c9cfc3'              # what far things fade into by day
HAZE_NIGHT = '#3c4c5e'
FOG_NIGHT = '#27344a'           # the dark distance by night
STAR = ['#5e6b80', '#8e98a8', '#c4c8cc', '#e8e6dc']
MOON = ['#8e96a0', '#b9bcb8', '#dcdccf', '#efeee3']
WARM = ['#5a3a22', '#a8642c', '#d9a04a', '#f0d27a']   # lamplight, ordered dark to bright


def hexrgb(h):
    return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], float)


def tohex(c):
    c = np.clip(np.round(c), 0, 255).astype(int)
    return '#%02x%02x%02x' % tuple(c)


def mix(a, b, t):
    """A colour t of the way from a to b."""
    return tohex(hexrgb(a) * (1 - t) + hexrgb(b) * t)


_NIGHT = {}


def nightify(h):
    """A colour as it looks by moonlight: darker, much less colour, shifted blue. Kept light enough that the
    fighters still read against it."""
    if h in _NIGHT:
        return _NIGHT[h]
    c = hexrgb(h) / 255
    l = 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]
    c = l + (c - l) * 0.38
    c = np.power(np.clip(c, 0, 1), 1.18) * 0.66 * np.array([0.80, 0.93, 1.16]) + np.array([0.012, 0.022, 0.045])
    _NIGHT[h] = tohex(c * 255)
    return _NIGHT[h]


# ============================================================================================== noise

def vnoise(w, h, cell, seed, wrap=False):
    """Smooth value noise in 0..1. wrap=True makes it tile horizontally (w must divide by cell)."""
    rng = np.random.RandomState(seed)
    gw = (w // cell) if wrap else int(math.ceil(w / cell)) + 2
    gh = int(math.ceil(h / cell)) + 2
    g = rng.rand(gh, gw)
    xs, ys = np.arange(w) / cell, np.arange(h) / cell
    x0, y0 = np.floor(xs).astype(int), np.floor(ys).astype(int)
    fx, fy = xs - x0, ys - y0
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    x1 = x0 + 1
    if wrap:
        x0, x1 = x0 % gw, x1 % gw
    a = g[y0][:, x0] * (1 - fx) + g[y0][:, x1] * fx
    b = g[y0 + 1][:, x0] * (1 - fx) + g[y0 + 1][:, x1] * fx
    return a * (1 - fy[:, None]) + b * fy[:, None]


def fbm(w, h, cell, seed, octaves=3, wrap=False):
    tot, amp, out = 0, 1.0, np.zeros((h, w))
    for o in range(octaves):
        c = max(2, cell >> o)
        if wrap:
            while w % c: c -= 1
        out += vnoise(w, h, c, seed + o * 101, wrap) * amp
        tot += amp
        amp *= 0.5
    return out / tot


def line1d(seed, n, scales, wrap=False):
    """A wandering line: n values around 0, built from sines. scales: ((wavelength, amplitude), ...)."""
    rnd = random.Random(seed)
    xs = np.arange(n, dtype=float)
    out = np.zeros(n)
    for s, a in scales:
        if wrap:
            s = n / max(1, round(n / s))
        out += a * np.sin(xs * 2 * math.pi / s + rnd.uniform(0, 6.28))
    return out


def facing(line, k=15):
    """How much a skyline faces the light, per x (-1 = away, 1 = towards), from its smoothed slope."""
    ker = np.ones(k) / k
    pad = np.pad(line.astype(float), k, mode='edge')
    sm = np.convolve(pad, ker, mode='same')[k:-k]
    return np.clip(-np.gradient(sm) * 2.5, -1, 1)


# ============================================================================================== the canvas

class Paint:
    def __init__(self, w=W, h=H, night=False, transparent=False):
        self.w, self.h, self.night = w, h, night
        self.a = np.zeros((h, w, 4), np.uint8)
        if not transparent:
            self.a[..., 3] = 255
        self.Y, self.X = np.mgrid[0:h, 0:w]
        self.T = (BAYER[self.Y & 3, self.X & 3] + 0.5) / 16

    fog = 0.0           # at night, how far colours are pulled towards the dark distance (for far layers)

    def col(self, h, raw=False):
        if raw or not self.night:
            return hexrgb(h)
        c = hexrgb(nightify(h))
        return c * (1 - self.fog) + hexrgb(FOG_NIGHT) * self.fog if self.fog else c

    def _place(self, mask, off):
        """Clip a local mask placed at off=(y, x) to the canvas: returns (canvas slices, local slices)."""
        y0, x0 = off
        mh, mw = mask.shape
        cy0, cx0 = max(0, y0), max(0, x0)
        cy1, cx1 = min(self.h, y0 + mh), min(self.w, x0 + mw)
        if cy1 <= cy0 or cx1 <= cx0:
            return None
        return (slice(cy0, cy1), slice(cx0, cx1)), (slice(cy0 - y0, cy1 - y0), slice(cx0 - x0, cx1 - x0))

    def ramp(self, mask, v, cols, soft=0.3, raw=False, off=(0, 0), alpha=255):
        """Paint mask with cols (dark to light) by value v (0 = first colour, len-1 = last). Dithers only where
        the fractional part of v is within `soft` of a step."""
        if np.isscalar(v):
            v = np.full(mask.shape, float(v))
        pl = self._place(mask, off)
        if pl is None: return
        cs, ls = pl
        m, v = mask[ls], v[ls]
        n = len(cols)
        v = np.clip(v, 0, n - 1)
        i = np.floor(v)
        f = v - i
        f = np.clip((f - 0.5) / max(soft, 1e-3) + 0.5, 0, 1)
        idx = np.clip(i + (f > self.T[cs]), 0, n - 1).astype(int)
        lut = np.array([self.col(c, raw) for c in cols])
        tgt = self.a[cs]
        tgt[..., :3][m] = lut[idx[m]]
        if alpha == 255:
            tgt[..., 3][m] = 255
        else:
            tgt[..., 3][m] = np.maximum(tgt[..., 3][m], alpha)

    def fill(self, mask, c, raw=False, off=(0, 0)):
        self.ramp(mask, 0.0, [c], raw=raw, off=off)

    def dither(self, mask, c, amount, raw=False, off=(0, 0)):
        """Sprinkle colour c over mask in an ordered pattern, `amount` (0..1, or an array) of the pixels."""
        pl = self._place(mask, off)
        if pl is None: return
        cs, ls = pl
        amt = amount[ls] if not np.isscalar(amount) else amount
        m = mask[ls] & (amt > self.T[cs])
        tgt = self.a[cs]
        tgt[..., :3][m] = self.col(c, raw)
        tgt[..., 3][m] = 255

    def tint(self, mask, c, t, raw=False, off=(0, 0)):
        """Blend colour c over mask by t (used for light shafts and haze)."""
        pl = self._place(mask, off)
        if pl is None: return
        cs, ls = pl
        m = mask[ls]
        tgt = self.a[cs]
        cc = self.col(c, raw)
        tt = t[ls][m][:, None] if not np.isscalar(t) else t
        tgt[..., :3][m] = np.clip(tgt[..., :3][m] * (1 - tt) + cc * tt, 0, 255)

    def darken(self, mask, k=0.8, cool=True, off=(0, 0)):
        """Shadow: darken what's already there (and cool it a little)."""
        pl = self._place(mask, off)
        if pl is None: return
        cs, ls = pl
        m = mask[ls]
        tgt = self.a[cs]
        mul = np.array([k * 0.94, k * 0.97, k * 1.04]) if cool else np.array([k, k, k])
        tgt[..., :3][m] = np.clip(tgt[..., :3][m] * mul, 0, 255)

    def px(self, x, y, c, raw=False):
        x, y = int(x), int(y)
        if 0 <= x < self.w and 0 <= y < self.h:
            self.a[y, x, :3] = self.col(c, raw)
            self.a[y, x, 3] = 255

    def stamp(self, x, y, rows, cols, raw=False, flip=False):
        """A tiny hand-drawn mark: rows of digits 1..n picking cols[n-1], '.' left alone."""
        for j, row in enumerate(rows):
            if flip: row = row[::-1]
            for i, ch in enumerate(row):
                if ch != '.':
                    self.px(x + i, y + j, cols[int(ch) - 1], raw)

    def poly(self, pts):
        """A full-canvas mask of a polygon."""
        im = Image.new('L', (self.w, self.h), 0)
        ImageDraw.Draw(im).polygon([(float(x), float(y)) for x, y in pts], fill=255)
        return np.array(im) > 0

    def image(self):
        return Image.fromarray(self.a, 'RGBA')


# ============================================================================================== building blocks

def sky(p, bottom, day=SKY_DAY, night=SKY_NIGHT, top=0, soft=0.28):
    """The sky as a few clean colour steps, top (darkest) to bottom (palest), dithered at the seams."""
    cols = night if p.night else day
    m = (p.Y < bottom) & (p.Y >= top)
    v = (p.Y - top) / max(1, bottom - top) * (len(cols) - 1) * 1.0
    p.ramp(m, v, cols, soft=soft, raw=True)


def stars(p, bottom, seed, count=70):
    if not p.night: return
    rnd = random.Random(seed)
    for _ in range(count):
        x, y = rnd.randrange(p.w), rnd.randrange(int(bottom * 0.85))
        fade = 1 - y / bottom
        q = rnd.random()
        if q < 0.06 and y > 2:
            p.px(x, y, STAR[3], True)
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                p.px(x + dx, y + dy, STAR[0], True)
        elif q < 0.35 * (0.4 + fade):
            p.px(x, y, STAR[2] if q < 0.2 else STAR[1], True)
        else:
            p.px(x, y, STAR[0], True)


def moon(p, cx, cy, r=7):
    if not p.night: return
    hal = (np.hypot(p.X + 0.5 - cx, p.Y + 0.5 - cy))
    sk = hal > r
    p.dither(sk & (hal < r + 9), '#2a384b', np.clip((r + 9 - hal) / 9, 0, 1) * 0.9, raw=True)
    p.dither(sk & (hal < r + 4), '#33425a', np.clip((r + 4 - hal) / 4, 0, 1) * 0.9, raw=True)
    m = hal <= r
    nx, ny = (p.X + 0.5 - cx) / r, (p.Y + 0.5 - cy) / r
    v = 3 - np.clip((nx * 0.6 + ny * 0.3 + 0.55), 0, 1) * 1.6
    # grey seas on the face
    for (dx, dy, rr) in ((-2.5, -1.5, 2.6), (2, 2.5, 2.0), (2.5, -3, 1.4)):
        v = np.where(np.hypot(p.X + 0.5 - cx - dx, p.Y + 0.5 - cy - dy) < rr, v - 1, v)
    p.ramp(m, v, MOON, soft=0.15, raw=True)


def blob(p, cx, cy, r, cols, sq=1.0, lo=-0.35, hi=0.95, soft=0.3, raw=False, cut=None, yfade=None,
         wrap=0, alpha=255, rough=None):
    """A sphere-shaded lump (cloud puff, leaf clump, bush): lit up-left, dark down-right.
    cut: no pixels at or below this y (flat cloud bottoms). yfade: (y_ref, per_px) darkens lower pixels.
    rough: (rnd, amount) nibbles the outline so it isn't a perfect circle."""
    ry = r * sq
    x0, x1 = int(math.floor(cx - r)) - 1, int(math.ceil(cx + r)) + 2
    y0, y1 = int(math.floor(cy - ry)) - 1, int(math.ceil(cy + ry)) + 2
    ys, xs = np.mgrid[y0:y1, x0:x1]
    nx, ny = (xs + 0.5 - cx) / r, (ys + 0.5 - cy) / ry
    d2 = nx * nx + ny * ny
    lim = 1.0
    if rough is not None:
        rr, amt = rough
        ang = np.arctan2(ny, nx)
        ph = [rr.uniform(0, 6.28) for _ in range(3)]
        lim = 1 + amt * (np.sin(ang * 5 + ph[0]) * 0.5 + np.sin(ang * 9 + ph[1]) * 0.35 + np.sin(ang * 13 + ph[2]) * 0.25)
        lim = lim * lim
    m = d2 <= lim
    if cut is not None:
        m &= ys < cut
    nz = np.sqrt(np.clip(1 - d2, 0, 1))
    lam = nx * LIGHT[0] + ny * LIGHT[1] + nz * LIGHT[2]
    v = np.clip((lam - lo) / (hi - lo), 0, 1) * (len(cols) - 1)
    if yfade is not None:
        v = v - np.clip(ys - yfade[0], 0, None) * yfade[1]
    for ox in ([0] if not wrap else [0, -wrap, wrap]):
        p.ramp(m, v, cols, soft=soft, raw=raw, off=(y0, x0 + ox), alpha=alpha)


def cloud(p, cx, cy, w, rnd, cols, wrap=0, flat=False, alpha=255):
    """A cumulus: a lumpy crown of domes over a row of smaller lobes, with a flat, shaded bottom.
    The lobes are shaded one over another (so each lump shows its own lit edge), then the whole cloud is
    darkened towards its base."""
    lobes = []
    n = max(2, int(w / rnd.uniform(10, 15)))
    for i in range(n):                                  # the bottom row
        t = (i + 0.5) / n
        r = (w / n) * rnd.uniform(0.62, 0.9) * (1.0 - 0.4 * abs(t - 0.45))
        if flat: r *= 0.8
        lobes.append((cx - w / 2 + t * w + rnd.uniform(-2, 2), cy - r * 0.3, r, 0.7 if not flat else 0.5))
    if not flat:
        k = rnd.randint(1, 3)
        crown = []
        for _ in range(k):                              # the domes on top
            r = w * rnd.uniform(0.17, 0.26)
            crown.append((cx + w * rnd.uniform(-0.25, 0.15), cy - r * 0.75 - w * 0.04, r, 0.88))
        for (x, y, r, sq) in list(crown):               # small lumps on the domes
            for _ in range(rnd.randint(0, 2)):
                rr = r * rnd.uniform(0.4, 0.6)
                a = rnd.uniform(math.pi * 1.1, math.pi * 1.9)
                crown.append((x + math.cos(a) * r * 0.75, y + math.sin(a) * r * 0.7, rr, 0.9))
        lobes = crown + lobes                           # domes behind, the bottom row in front
    base = int(round(cy + 1))
    top = min(y - r * sq for (x, y, r, sq) in lobes)
    x0 = int(math.floor(min(x - r for (x, y, r, sq) in lobes))) - 1
    x1 = int(math.ceil(max(x + r for (x, y, r, sq) in lobes))) + 2
    y0 = int(math.floor(top)) - 1
    ys, xs = np.mgrid[y0:base, x0:x1]
    M = np.zeros(ys.shape, bool)
    V = np.zeros(ys.shape)
    for (x, y, r, sq) in lobes:
        nx, ny = (xs + 0.5 - x) / r, (ys + 0.5 - y) / (r * sq)
        d2 = nx * nx + ny * ny
        m = d2 <= 1
        nz = np.sqrt(np.clip(1 - d2, 0, 1))
        lam = nx * LIGHT[0] + ny * LIGHT[1] + nz * LIGHT[2]
        V[m] = lam[m]
        M |= m
    h = max(4.0, base - top)
    height = 1 - (ys + 0.5 - top) / h                  # 1 at the top of the cloud, 0 at its base
    v = (np.clip((V + 0.45) / 1.35, 0, 1) * 0.55 + height * 0.65 - 0.05) * (len(cols) - 1)
    v = np.where(ys >= base - 2, np.minimum(v, 0.6), v)  # the flat underside
    for ox in ([0] if not wrap else [0, -wrap, wrap]):
        p.ramp(M, v, cols, soft=0.22, raw=True, off=(y0, x0 + ox), alpha=alpha)


def clouds_layer(seed, bottom, night=False, count=9, mist=None):
    """The drifting clouds over a sky that ends at `bottom`: a 720-wide strip that tiles horizontally."""
    p = Paint(CW, H, night=night, transparent=True)
    rnd = random.Random(seed)
    cols = CLOUD_NIGHT if night else CLOUD_DAY
    # spread the clouds out evenly round the loop, then nudge
    slots = [(i + rnd.uniform(0.1, 0.7)) * CW / count for i in range(count)]
    items = []
    for x in slots:
        depth = rnd.random()                            # 0 = high and near, 1 = low and far
        y = 20 + depth * (bottom - 28) + rnd.uniform(-2, 2)
        w = (74 - 46 * depth) * rnd.uniform(0.75, 1.2)
        items.append((depth, x, y, w))
    for depth, x, y, w in sorted(items, key=lambda t: -t[0]):   # far ones first
        haze = HAZE_NIGHT if night else SKY_DAY[-2]
        cc = [mix(c, haze, 0.55 * depth) for c in cols]
        cloud(p, x, y, w, rnd, cc, wrap=CW, flat=depth > 0.7)
        # a small companion puff trailing behind some clouds
        if rnd.random() < 0.45:
            cloud(p, x + w * 0.62 * rnd.choice((-1, 1)), y + rnd.uniform(-2, 3), w * 0.35, rnd, cc, wrap=CW,
                  flat=True)
    if night:
        # thin strands of mist low over the land
        n = fbm(CW, H * 3, 60, seed + 7, 4, wrap=True)[::3]
        band = np.exp(-((p.Y - (bottom + 2)) / 7.0) ** 2)
        v = (n - 0.45) * 3.2 * band
        free = p.a[..., 3] == 0
        for lo, a in ((0.08, 40), (0.35, 64)):
            m = free & (v > lo) & ((v - lo) * 6 > p.T)
            p.ramp(m, 0.0, ['#6a7a90'], raw=True, alpha=a)
            p.a[..., 3][m] = a
    return p.image()


def ground(p, top, bottom, cols, seed, v0, v1, patch=0.7, cell=26, soft=0.25, mask=None):
    """A floor between rows top and bottom: value v0 at the top to v1 at the bottom, plus broad soft patches."""
    m = (p.Y >= top) & (p.Y < bottom)
    if mask is not None: m &= mask
    t = np.clip((p.Y - top) / max(1, bottom - top), 0, 1)
    # patches stretched sideways: it's a floor seen at a low angle
    n = fbm(p.w, p.h * 2, cell, seed, 2)[::2]
    v = v0 + (v1 - v0) * t + (n - 0.5) * patch * 2
    p.ramp(m, v, cols, soft=soft)


# little marks for the ground, drawn as digit grids (1 = darkest of the colours given)
TUFTS = [
    ["..3..", ".2.3.", "2.22.", "12121"],
    [".3.", "2.3", "121"],
    ["3...3.", ".3.2..", ".2.2.3", "12121."],
    ["..3", ".2.", "12."],
]
PEBBLES = [
    [".32.", "3221", ".11."],
    ["32", "21"],
    [".3.", "321", ".1."],
    [".322.", "32221", ".111."],
]
CRACKS = [
    ["1..", ".11", "...", ],
    ["11.", "..1"],
    [".1..", "1.11"],
]


def scatter(p, rnd, n, x0, x1, y0, y1, marks, cols, avoid=None, flip=True):
    """Sprinkle n marks at random over a box, not inside `avoid` zones [(x0, x1), ...]."""
    for _ in range(n):
        x, y = rnd.randrange(x0, x1), rnd.randrange(y0, y1)
        if avoid and any(a <= x < b for a, b in avoid): continue
        p.stamp(x, y, rnd.choice(marks), cols, flip=flip and rnd.random() < 0.5)


def shadow(p, cx, cy, rx, ry, k=0.78):
    """A soft oval shadow on the ground: a solid core with a dithered rim."""
    d = np.hypot((p.X + 0.5 - cx) / rx, (p.Y + 0.5 - cy) / ry)
    core = d < 0.75
    rim = (d >= 0.75) & (d < 1.0) & (((1.0 - d) / 0.25 * 0.9) > p.T)
    p.darken(core | rim, k)


def rock(p, cx, base, w, h, rnd, cols, moss=None, seed_facets=4, flat_top=0.0):
    """A chunky boulder sitting on `base`, w wide and h tall, cut into planes like hand-painted pixel rock:
    a pale top, a lit left face, a mid front and a dark right side, with a dark outline down-right."""
    k = 14
    pts = []
    sq = 0.55 + rnd.uniform(-0.1, 0.1)                      # squarer than an ellipse
    for i in range(k + 1):
        a = math.pi + i * math.pi / k
        ca, sa = math.cos(a), math.sin(a)
        rr = rnd.uniform(0.88, 1.06)
        x = cx + np.sign(ca) * abs(ca) ** sq * w / 2 * rr
        y = base + np.sign(sa) * abs(sa) ** (sq + flat_top) * h * rr
        pts.append((x, y))
    pts = [(cx - w / 2, base + 1)] + pts + [(cx + w / 2, base + 1)]
    m = p.poly(pts)
    nx = (p.X + 0.5 - cx) / (w / 2)
    ny = (p.Y + 0.5 - base) / h                              # -1 at the top, 0 at the foot
    jit = (vnoise(p.w, p.h, 4, rnd.randrange(9999)) - 0.5) * 0.35
    # plane boundaries, nudged by noise so the facets are irregular
    top = (ny + jit < -0.62 + 0.2 * np.abs(nx)) 
    left = nx + jit < -0.2
    right = nx + jit > 0.42
    tone = np.where(top, 3.6, np.where(left, 2.7, np.where(right, 0.9, 1.8)))
    # extra facets: a few straight cuts that step the tone up or down
    for _ in range(seed_facets):
        a = rnd.uniform(0, math.pi)
        ox = cx + rnd.uniform(-w / 3, w / 3)
        oy = base - h * rnd.uniform(0.2, 0.7)
        side = (np.cos(a) * (p.X - ox) + np.sin(a) * (p.Y - oy)) > 0
        tone = tone + np.where(side & ~top, rnd.choice((0.5, -0.5)), 0)
    tone = tone - np.clip(ny + 0.25, 0, None) * 1.6            # darker towards the ground
    n = len(cols) - 1
    p.ramp(m, np.clip(tone, 0, n) * n / 4, cols, soft=0.12)
    rgt = np.zeros_like(m); rgt[:, :-1] = m[:, 1:]
    dn = np.zeros_like(m); dn[:-1] = m[1:]
    up = np.zeros_like(m); up[1:] = m[:-1]
    p.fill(m & ~up & (nx < 0.3) & (p.Y < base - 1), cols[-1])     # the lit rim
    p.fill(m & (~rgt | ~dn) & (nx > -0.3), cols[0])                # dark outline on the shadow side
    for _ in range(rnd.randint(1, 3)):                              # cracks
        x = cx + rnd.uniform(-w / 3, w / 4)
        y = base - h * rnd.uniform(0.3, 0.8)
        for s_ in range(rnd.randint(2, 5)):
            if 0 <= int(y) < p.h and 0 <= int(x) < p.w and m[int(y), int(x)]:
                p.px(x, y, cols[0])
                if 0 <= int(x) - 1 < p.w and m[int(y), int(x) - 1]: p.px(x - 1, y, cols[min(n, 3)])
            x += rnd.choice((0, 1, 1))
            y += 1
    if moss:
        mm = m & top & (fbm(p.w, p.h, 5, rnd.randrange(999), 2) > 0.5)
        p.ramp(mm, np.where(nx < 0, 1.0, 0.0), moss, soft=0.1)
    return m


def grass_fringe(p, ys, cols, rnd, density=0.5, h=(2, 5), x0=0, x1=None):
    """Blades poking up along an edge ys[x], lit on the left."""
    x1 = p.w if x1 is None else x1
    for x in range(x0, x1):
        if rnd.random() < density:
            hh = rnd.randint(*h)
            lean = rnd.choice((0, 0, 1, -1))
            for j in range(hh):
                c = cols[min(len(cols) - 1, 1 + (j > hh // 2))] if x % 3 else cols[0]
                p.px(x + (lean if j > hh // 2 else 0), ys[x] - j, c)


def tall_grass(p, x0, x1, base, rnd, cols, height=14, density=0.6):
    """A clump of tall grass at a screen edge (foreground)."""
    for x in range(x0, x1):
        if rnd.random() > density: continue
        hh = int(height * rnd.uniform(0.4, 1.0))
        bend = rnd.uniform(-0.25, 0.35)
        for j in range(hh):
            xx = x + int(bend * j * j / max(1, hh))
            t = j / hh
            c = cols[0] if t < 0.3 else cols[1] if t < 0.7 else cols[2]
            if (x * 7) % 5 == 0 and t > 0.5: c = cols[3] if len(cols) > 3 else c
            p.px(xx, base - j, c)


# ============================================================================================== the coast

def sea(p, top, bottom, rnd, far, near, glint, foam_y=None, moonx=None):
    """Open sea between top (horizon) and bottom: hazy and pale far off, deeper near, with short glints."""
    m = (p.Y >= top) & (p.Y < bottom)
    t = (p.Y - top) / max(1, bottom - top)
    cols = far + near
    v = (np.clip(t, 0, 1) ** 0.7) * (len(cols) - 1)
    p.ramp(m, v, cols, soft=0.2)
    # glints: short horizontal dashes, longer and more spaced nearer the shore
    for _ in range(int((bottom - top) * 3.2)):
        y = rnd.randrange(top + 1, bottom - 1)
        tt = (y - top) / (bottom - top)
        x = rnd.randrange(p.w)
        ln = int(2 + tt * 6 * rnd.random()) if tt > 0.15 else rnd.randint(3, 8)
        c = glint[0] if rnd.random() < 0.7 else glint[1]
        for i in range(ln): p.px(x + i, y, c)
        if tt > 0.4 and rnd.random() < 0.5:   # the dark trough under a wave crest
            for i in range(ln + 1): p.px(x + i, y + 1, cols[-1])
    if moonx is not None and p.night:
        for y in range(top, bottom):
            tt = (y - top) / (bottom - top)
            spread = 3 + tt * 22
            for _ in range(int(2 + tt * 5)):
                if rnd.random() < 0.7:
                    x = int(moonx + rnd.gauss(0, spread * 0.5))
                    ln = rnd.randint(1, int(2 + tt * 5))
                    c = MOON[2] if rnd.random() < 0.4 else MOON[0]
                    for i in range(ln): p.px(x + i, y, c, raw=True)


def coast(night=False):
    p = Paint(night=night)
    rnd = random.Random(11)
    hz = 62
    sky(p, hz)
    stars(p, hz, 5)
    moon(p, 268, 20)
    p.fog = 0.5
    # a far headland on the left, hazy
    head = (hz - 3 - np.clip(16 - np.arange(W) * 0.16, 0, None) + line1d(3, W, ((40, 2.0), (13, 0.8)))).astype(int)
    hm = (p.Y >= head[None, :]) & (p.Y < hz) & (p.X < 120)
    hcol = ['#8a9c98', '#9aaba4', '#adbab0']
    p.ramp(hm, 1.0 + (p.Y - head[None, :]) * 0.05 - (p.X < 70) * 0.6 + (p.Y - head[None, :] < 2) * 0.9, hcol, soft=0.3)
    p.fog = 0.0
    # the sea
    shore = (97 + line1d(21, W, ((90, 2.5), (31, 1.2)))).astype(int)
    sea(p, hz, 98, rnd, ['#9fb3b2', '#86a1a5', '#6b8c96'], ['#557a87', '#46697a', '#3b5d6e'],
        ['#c3cfca', '#a6bcbc'], moonx=268)
    # a tiny far sail
    for j, row in enumerate(["..1", ".21", "221", "3333"]):
        for i, ch in enumerate(row):
            if ch != '.': p.px(214 + i, hz - 3 + j, {'1': '#d9d6c8', '2': '#b9b8ae', '3': '#5d6866'}[ch])
    # wet sand, the foam lines and the dry beach
    sand = ['#8e7f63', '#9e8d6e', '#ae9c7a', '#bba985', '#c7b690']
    wet = ['#5f6a64', '#6f7466', '#80806a', '#8e8a70']
    bm = p.Y >= shore[None, :]
    ground(p, 92, H, sand, 31, 2.9, 1.7, patch=0.3, cell=30, soft=0.3, mask=bm)
    wetm = bm & (p.Y < shore[None, :] + 6)
    p.ramp(wetm, (p.Y - shore[None, :]) * 0.7, wet, soft=0.3)
    # foam: the line where the wave runs up, and a broken line further out
    for x in range(W):
        y = shore[x]
        p.px(x, y, '#d8dccf'); p.px(x, y - 1, '#b7c5c0' if x % 7 else '#d8dccf')
        if (x // 9 + x // 23) % 3: p.px(x, y + 1, '#a9b0a2')
    foam2 = (shore - 6 + line1d(8, W, ((47, 1.5),))).astype(int)
    for x in range(W):
        if math.sin(x * 0.11) + math.sin(x * 0.037 + 1) > -0.3:
            p.px(x, foam2[x], '#c4cfc8')
            if math.sin(x * 0.29) > 0.6: p.px(x, foam2[x] - 1, '#a8bab8')
    foam3 = (shore - 13 + line1d(9, W, ((61, 1.2),))).astype(int)
    for x in range(W):
        if math.sin(x * 0.07 + 2) + math.sin(x * 0.023) > 0.4: p.px(x, foam3[x], '#a8bab8')
    # the beach: shingle drifts and a few shells and weed, kept sparse and low-contrast
    pcol = ['#8a7a60', '#a39070', '#c4b28e']
    scatter(p, rnd, 70, 0, W, 104, H - 2, PEBBLES, pcol)
    scatter(p, rnd, 30, 0, W, 100, 108, PEBBLES[1:3], ['#6f6a5a', '#8e846a', '#b1a584'])
    for _ in range(9):    # strands of weed along the tide line
        x, y = rnd.randrange(W), rnd.randrange(101, 106)
        for i in range(rnd.randint(3, 7)): p.px(x + i, y + (i % 3 == 1), '#4e5434' if i % 2 else '#5f6440')
    # rocks at the edges, with shadows to the right
    rk = ['#3e3c40', '#55525a', '#6d6a6e', '#878282', '#a09a92']
    moss = ['#5a6b3c', '#73844c']
    shadow(p, 30, 112, 34, 5)
    shadow(p, 340, 116, 24, 4)
    rock(p, 6, 104, 44, 42, rnd, rk, moss=moss)
    rock(p, 34, 108, 28, 20, rnd, rk)
    rock(p, 18, 113, 22, 9, rnd, rk)
    rock(p, 352, 102, 40, 50, rnd, rk, moss=moss)
    rock(p, 330, 110, 24, 16, rnd, rk)
    rock(p, 346, 116, 14, 7, rnd, rk)
    # a far sea stack
    rock(p, 296, 74, 18, 6, rnd, [mix(c, '#86a1a5', 0.5) for c in rk], seed_facets=2)
    for x in range(286, 307, 2): p.px(x, 74, '#b7c5c0')
    # foreground pebbles at the very bottom edge
    scatter(p, rnd, 14, 0, 40, 126, H - 2, PEBBLES, pcol)
    scatter(p, rnd, 14, 320, W, 126, H - 2, PEBBLES, pcol)
    return p.image()


def coast_clouds(night=False):
    return clouds_layer(101, 58, night)


# ============================================================================================== trees

LEAF = ['#1f2b1d', '#2c3b26', '#3b4d30', '#4d6136', '#62763f', '#7a8a4b', '#95a05a']
BARK = ['#2a211b', '#3a2d24', '#4f3e2f', '#655140', '#7d6a55']


def canopy(p, cx, cy, rx, ry, rnd, leaf=LEAF, size=(4, 7), density=1.0, sweep=0.0):
    """A tree crown made of many leafy clumps: dark clumps behind, lit clumps in front, brighter up-left."""
    n = int(rx * ry * 0.09 * density) + 4
    pts = []
    for _ in range(n):
        a, d = rnd.uniform(0, 6.283), rnd.random() ** 0.6
        x, y = cx + math.cos(a) * rx * d, cy + math.sin(a) * ry * d
        x += sweep * (cy - y) * 0.4
        pts.append((x, y, rnd.uniform(*size)))
    # the shadowed inside of the crown
    for (x, y, r) in pts:
        blob(p, x + 1.5, y + 2, r, leaf[:4], sq=0.85, rough=(rnd, 0.12))
    # lit clumps: the ones high and to the left get the brightest colours
    for (x, y, r) in sorted(pts, key=lambda t: rnd.random()):
        hi = ((cy - y) / ry * 0.6 + (cx - x) / rx * 0.4)       # -1..1
        k = int(np.clip(2 + hi * 1.8 + rnd.uniform(-0.4, 0.4), 1, len(leaf) - 4))
        if rnd.random() < 0.55:
            blob(p, x, y, r * 0.8, leaf[k - 1:k + 3], sq=0.85, rough=(rnd, 0.15), soft=0.35)


def trunk(p, x, base, top, w, rnd, bark=BARK, lean=0.0, flare=1.8, roots=True):
    """A trunk from base up to top, w wide at the bottom, curving by `lean` pixels at the top."""
    ht = base - top
    for y in range(int(top), int(base) + 1):
        t = (base - y) / ht                               # 0 at the foot, 1 at the top
        c = x + lean * t * t
        ww = w * (1 - 0.4 * t)
        if t < 0.12 and roots: ww *= 1 + (flare - 1) * (1 - t / 0.12) ** 2
        xs = np.arange(int(c - ww / 2), int(c + ww / 2) + 1)
        u = (xs + 0.5 - (c - ww / 2)) / max(ww, 1)           # 0 left edge .. 1 right edge
        v = (1 - np.abs(u - 0.32) * 1.9) * (len(bark) - 1) * 0.9 + 0.3
        for x_, v_, u_ in zip(xs, v, u):
            # bark furrows: thin vertical dark lines that wander
            fur = math.sin(x_ * 1.7 + math.sin(y * 0.21 + x_) * 1.2) > 0.82 and 0.15 < u_ < 0.9
            k = int(np.clip(v_ - (1.5 if fur else 0) + (p.T[y % p.h, x_ % p.w] - 0.5) * 0.6, 0, len(bark) - 1))
            p.px(x_, y, bark[k])


def tree(p, x, base, h, rnd, leaf=LEAF, bark=BARK, lean=0.0, crown=1.0, sweep=0.0, shade=True):
    """A broadleaf tree standing on `base`, h tall."""
    tw = max(3, h * 0.085)
    th = h * 0.62
    if shade: shadow(p, x + h * 0.28, base + 1.5, h * 0.42 * crown, 3.5, k=0.72)
    trunk(p, x, base, base - th, tw, rnd, bark, lean=lean)
    cx, cy = x + lean + sweep * h * 0.15, base - h * 0.66
    canopy(p, cx, cy, h * 0.34 * crown, h * 0.27, rnd, leaf, size=(max(3, h * 0.05), max(5, h * 0.09)), sweep=sweep)


def bush(p, cx, base, w, h, rnd, leaf=LEAF, berries=None):
    shadow(p, cx + w * 0.3, base + 1, w * 0.6, 2.5, k=0.75)
    canopy(p, cx, base - h * 0.5, w / 2, h / 2, rnd, leaf, size=(3, 5), density=1.4)
    if berries:
        for _ in range(int(w / 4)):
            p.px(cx + rnd.uniform(-w / 2.5, w / 2.5), base - h * rnd.uniform(0.3, 0.8), berries)


# ============================================================================================== the road

GRASS_DRY = ['#58623a', '#6a7243', '#7c824c', '#8e9256', '#a3a063', '#b5ad70']
DIRT = ['#76624a', '#857055', '#937e60', '#a08b6b', '#ad9877']


def road(night=False):
    p = Paint(night=night)
    rnd = random.Random(23)
    hz = 66
    sky(p, hz)
    stars(p, hz, 9)
    moon(p, 84, 22)
    # far hills on the left running down into the sea on the right
    far = 50 + line1d(4, W, ((110, 4.0), (37, 1.6), (13, 0.6))) + np.clip(np.arange(W) - 170, 0, None) * 0.22
    far = np.minimum(far.astype(int), hz + 1)
    p.fog = 0.55
    sea_cols = ['#a2b5b2', '#8ea7a9', '#7b98a0']
    p.ramp((p.Y >= hz) & (p.Y < 100), (p.Y - hz) * 0.3, sea_cols, soft=0.3)
    for _ in range(18):
        x, y = rnd.randrange(240, W), rnd.randrange(hz + 1, hz + 8)
        for i in range(rnd.randint(2, 6)): p.px(x + i, y, '#c3cfca')
    fm = (p.Y >= far[None, :]) & ((p.Y < hz + 1) | (p.X < 236))
    fv = 1.2 + facing(far)[None, :] * 0.9 - (p.Y - far[None, :]) * 0.04
    p.ramp(fm & (p.Y < 100), fv, ['#8e9f97', '#9cab9f', '#acb8a9'], soft=0.3)
    # a nearer ridge, a little less hazy
    r2 = (62 + line1d(24, W, ((90, 3.0), (29, 1.2))) + np.clip(np.arange(W) - 120, 0, None) * 0.12).astype(int)
    r2m = (p.Y >= r2[None, :]) & (p.X < 250) & (p.Y < 100)
    p.ramp(r2m, 1.3 + facing(r2)[None, :] * 0.9 - (p.Y - r2[None, :]) * 0.05, ['#82947e', '#8fa086', '#9eac90'],
           soft=0.3)
    p.fog = 0.2
    # rolling farmland: a patchwork of fields with hedgerows following the land
    mid = (76 + line1d(7, W, ((140, 4.0), (47, 2.0)))).astype(int)
    mm = (p.Y >= mid[None, :]) & (p.Y < 100)
    dy = p.Y - mid[None, :]
    band = (dy >= 6).astype(int) + (dy >= 13)
    seg = np.floor((p.X + dy * 1.4 + band * 37) / 58).astype(int)
    fid = (band * 7 + seg * 3) % 5
    fcols = ['#76835a', '#86915f', '#98a066', '#aaac72', '#9a9a62']
    fv = np.choose(fid, [0, 1, 2, 3, 4]).astype(float)
    p.ramp(mm, fv, fcols, soft=0.15)
    hedge = ['#4a5a38', '#5f6e44', '#73804e']
    hb = mm & ((dy == 6) | (dy == 13) | (dy == 0))
    hs = mm & (np.diff(np.pad(seg, ((0, 0), (1, 0)), mode='edge'), axis=1) != 0)
    p.ramp(hb | hs, 1.0 + (p.T > 0.5) * 0.8, hedge, soft=0.1)
    for x in range(0, W, 2):                              # trees standing in the hedgerows
        for row in (0, 6, 13):
            if math.sin(x * 0.07 + row) > 0.6 and rnd.random() < 0.35:
                rr = rnd.uniform(1.5, 2.6) * (1 + row * 0.03)
                blob(p, x, mid[x] + row - rr * 0.6, rr, hedge, sq=0.9)
    p.fog = 0.0
    # the near grassland
    near = (90 + line1d(12, W, ((90, 1.5), (23, 0.7)))).astype(int)
    ground(p, 0, H, GRASS_DRY, 51, 3.4, 2.4, patch=0.35, cell=34, mask=p.Y >= near[None, :])
    grass_fringe(p, near, GRASS_DRY[2:], rnd, density=0.35, h=(1, 3))
    # a track running off into the hills, narrowing with distance
    for y in range(mid[200] + 3, 106):
        t = (y - mid[200]) / (106 - mid[200])
        c = 196 + 22 * math.sin(t * 3.2) - 18 * t
        hw = 1 + t * t * 9
        xs = np.arange(int(c - hw), int(c + hw) + 1)
        for x in xs:
            u = (x - (c - hw)) / (2 * hw + 1)
            k = 2 if 0.2 < u < 0.8 else 1
            if t < 0.35: k = 3
            p.px(x, y, DIRT[k])
    # the road itself, across the floor
    rt = (103 + line1d(14, W, ((120, 1.6), (37, 0.6)))).astype(int)
    rb = (131 + line1d(15, W, ((150, 1.4), (41, 0.6)))).astype(int)
    rm = (p.Y >= rt[None, :]) & (p.Y < rb[None, :])
    ground(p, 100, 132, DIRT, 61, 2.7, 2.1, patch=0.3, cell=40, soft=0.3, mask=rm)
    # worn wheel ruts: faint, broken, slightly darker
    for ry in (110, 121):
        rl = (ry + line1d(ry, W, ((80, 1.2),))).astype(int)
        for x in range(W):
            if math.sin(x * 0.05 + ry) + math.sin(x * 0.13) > -0.6:
                p.px(x, rl[x], DIRT[1])
                if x % 5 == 0: p.px(x, rl[x] + 1, DIRT[1])
    scatter(p, rnd, 40, 0, W, 106, 130, PEBBLES, [DIRT[0], DIRT[2], DIRT[4]])
    grass_fringe(p, rt, GRASS_DRY[1:], rnd, density=0.45, h=(1, 3))
    # tufts on the verges
    scatter(p, rnd, 24, 0, W, 92, 101, TUFTS, GRASS_DRY[1:4])
    scatter(p, rnd, 30, 0, W, 131, H - 3, TUFTS, GRASS_DRY[1:4])
    for x in range(W):                                   # the far edge of the lower verge
        for y in range(rb[x], H):
            if y == rb[x]: p.px(x, y, GRASS_DRY[1] if x % 3 else GRASS_DRY[2])
    # the wind-bent tree at the left edge and a milestone on the right
    tree(p, 14, 101, 84, rnd, lean=14, crown=1.1, sweep=0.9)
    rk = ['#4a4646', '#625d5a', '#7d7670', '#988f86', '#b0a698']
    shadow(p, 348, 113, 10, 2.5)
    st = (np.abs(p.X + 0.5 - 342) <= 5) & (p.Y < 114) & (p.Y >= 99) & ~((p.Y < 103) & (np.hypot(p.X + 0.5 - 342, p.Y - 103.5) > 5))
    p.ramp(st, 3.2 - np.clip(p.X - 339, 0, None) * 0.45 - (p.Y > 111) * 0.6, rk, soft=0.2)
    for j, row in enumerate(["1.1", ".1.", "1.1"]):
        for i, ch in enumerate(row):
            if ch != '.': p.px(341 + i, 104 + j, rk[1])
    for x in range(337, 348): p.px(x, 113, rk[0])
    tall_grass(p, 334, 352, 114, rnd, GRASS_DRY[1:], height=6, density=0.5)
    bush(p, 362, 104, 26, 18, rnd, ['#2c3524', '#3a4630', '#4f5c38', '#62703f', '#77844a'])
    # foreground grass at the bottom corners
    tall_grass(p, 0, 26, H, rnd, GRASS_DRY[1:], height=14, density=0.6)
    tall_grass(p, 334, W, H, rnd, GRASS_DRY[1:], height=12, density=0.6)
    return p.image()


# ============================================================================================== the forest

def forest(night=False):
    p = Paint(night=night)
    rnd = random.Random(31)
    # the depth behind: dark under the canopy, misty and lighter in the middle distance
    mist = ['#2a382c', '#33443a', '#3f5246', '#4d6252', '#5c725e', '#6e836a', '#83967c']
    v = 6 - np.abs(p.Y - 70) / 70 * 6.5
    p.ramp(p.Y < 100, v, mist, soft=0.3)
    # sky glimpsed through the very top of the canopy
    skyc = ['#8fa9b4', '#a8bcbe', '#c2cdc4'] if not night else ['#2c3a4e', '#364659', '#415264']
    p.ramp(p.Y < 22, (22 - p.Y) / 11.0, skyc, soft=0.3, raw=night)
    # far trunks, hazy, then nearer ones, darker
    for layer, (count, wr, haze) in enumerate(((16, (2, 4), 0.62), (9, (5, 8), 0.38))):
        xs = sorted(rnd.uniform(30, 330) for _ in range(count))
        for x in xs:
            bk = [mix(c, mist[5 - layer * 2], haze) for c in BARK]
            trunk(p, x, 96 - layer * 2 + rnd.randint(-2, 2), -2, rnd.uniform(*wr), rnd, bk, lean=rnd.uniform(-3, 3),
                  flare=1.4)
        # a band of hazy foliage hanging down from the canopy at this depth
        for _ in range(26):
            lc = [mix(c, mist[5 - layer * 2], haze + 0.1) for c in LEAF]
            blob(p, rnd.uniform(0, W), rnd.uniform(8, 30 + layer * 6), rnd.uniform(5, 10), lc[1:6], sq=0.8,
                 rough=(rnd, 0.15))
    # shafts of sun from the upper left, between the trunks
    for x0 in (70, 130, 175, 248):
        w = rnd.uniform(9, 16)
        sh = ((p.X - x0 - (p.Y * 0.55)) > 0) & ((p.X - x0 - (p.Y * 0.55)) < w) & (p.Y < 104)
        t = np.where(p.Y < 60, 0.16, 0.16 * np.clip((104 - p.Y) / 44, 0, 1))
        p.tint(sh, '#e3dcae' if not night else '#9fb4cc', t * (0.7 if night else 1.0), raw=night)
    # undergrowth along the back of the clearing
    ug = ['#26341f', '#2f3f27', '#3b4d30', '#4d6136', '#5d7140']
    for x in range(-10, W + 10, 9):
        if 110 < x < 250 and rnd.random() < 0.35: continue
        blob(p, x + rnd.uniform(-3, 3), 96 + rnd.uniform(-2, 2), rnd.uniform(5, 9), ug, sq=0.65, rough=(rnd, 0.18))
    # the canopy overhead
    for x in range(-10, W + 12, 7):
        y = rnd.uniform(-4, 8) + (10 if 120 < x < 240 else 0) * rnd.random()
        blob(p, x, y, rnd.uniform(7, 12), LEAF[0:5], sq=0.8, rough=(rnd, 0.18))
    # the clearing's floor: moss and grass in soft patches, with dapples of sunlight
    fl = ['#3e4e2f', '#4a5b35', '#56693b', '#627641', '#6f8247']
    fm = p.Y >= 98
    ground(p, 98, H, fl, 41, 2.0, 2.6, patch=0.35, cell=30, mask=fm)
    edge = (98 + line1d(5, W, ((40, 1.5), (13, 0.8)))).astype(int)
    for x in range(W):
        for y in range(96, edge[x] + 1): p.px(x, y, fl[0] if y == edge[x] else ug[1])
    # dapples of sun: irregular splashes, one or two steps lighter, their edges dithered
    dap = fbm(W, H * 3, 14, 44, 3)[::3]                    # flattened: patches seen at a low angle
    shafts = np.zeros((H, W))
    for x0 in (70, 130, 175, 248):                         # where the sun shafts reach the floor
        shafts += np.exp(-((p.X - x0 - p.Y * 0.55 - 6) / 16.0) ** 2)
    dv = (dap - 0.6) * 8 + shafts * 0.7 - 0.45
    p.ramp(fm & (p.Y >= 101) & (dv > 0), np.clip(dv, 0, 1.4) + 0.0, [fl[4], '#7d8c4e', '#8f9b56'], soft=0.45)
    scatter(p, rnd, 30, 40, 320, 102, H - 2, TUFTS, fl[1:4] + ['#7d8c4e'])
    for _ in range(26):                                   # fallen leaves
        x, y = rnd.randrange(30, 330), rnd.randrange(104, H - 1)
        c = rnd.choice(['#6e5a3a', '#7d6640', '#5d4a32'])
        p.px(x, y, c); p.px(x + 1, y, c)
    # the big trunks at the edges, their roots, and ferns at their feet
    for (x, lean, w) in ((14, 4, 26), (346, -5, 30)):
        shadow(p, x + 24, 108, 34, 5, k=0.7)
        trunk(p, x, 106, -4, w, rnd, BARK, lean=lean, flare=1.9)
        for side in (-1, 1):                              # roots
            for k in range(2):
                rx0, ry0 = x + side * w * 0.35, 100 + k * 3
                for i in range(int(rnd.uniform(8, 14))):
                    xx = rx0 + side * i
                    yy = ry0 + i * 0.45 + k
                    for j in range(3 - i // 6):
                        p.px(xx, yy + j, BARK[3 - j] if j < 2 else BARK[0])
        # moss up the lower trunk
        mm = (np.abs(p.X - x) < w * 0.45) & (p.Y > 70) & (p.Y < 104) & (fbm(W, H, 5, x, 2) > 0.6) & \
             (p.X < x - w * 0.05)
        p.ramp(mm, 1.0 + (p.T > 0.6), ['#4d6136', '#62763f', '#7a8a4b'], soft=0.2)
    blob(p, 6, 2, 22, LEAF[:6], sq=0.7, rough=(rnd, 0.2))
    blob(p, 354, 4, 24, LEAF[:6], sq=0.7, rough=(rnd, 0.2))
    for (x, flip) in ((22, 1), (40, 1), (338, -1), (318, -1)):
        fern(p, x, 110 + rnd.randint(0, 6), rnd, flip)
    tall_grass(p, 0, 22, H, rnd, fl[1:] , height=12, density=0.5)
    tall_grass(p, 336, W, H, rnd, fl[1:], height=12, density=0.5)
    return p.image()


def fern(p, x, base, rnd, flip=1):
    cols = ['#2f3f27', '#4d6136', '#62763f', '#7a8a4b']
    for f in range(5):
        ang = math.radians(-160 + f * 35) if flip > 0 else math.radians(-20 - f * 35)
        ln = rnd.uniform(7, 12)
        for i in range(int(ln)):
            t = i / ln
            xx = x + math.cos(ang) * i
            yy = base + math.sin(ang) * i + t * t * 5
            p.px(xx, yy, cols[2 if i < ln * 0.6 else 3])
            if i % 2 == 0 and i > 1:
                p.px(xx, yy + 1, cols[1])
                p.px(xx + 1, yy - 1, cols[3] if t < 0.5 else cols[2])


def forest_clouds(night=False):
    """No sky to speak of in the woods: the drifting layer is thin mist hanging between the trunks."""
    p = Paint(CW, H, night=night, transparent=True)
    n = fbm(CW, H * 3, 90, 501, 4, wrap=True)[::3]
    band = np.exp(-((p.Y - 74) / 12.0) ** 2)
    v = (n - 0.42) * 3.0 * band                                # 0..~1 where there is mist
    c = '#c9d1bc' if not night else '#8293ac'
    # three thicknesses, each edge broken up with an ordered dither instead of a hard line
    for lo, a in ((0.08, 34), (0.3, 52), (0.55, 70)):
        m = (v > lo) & ((v - lo) * 6 > p.T)
        p.ramp(m, 0.0, [c], raw=True, alpha=a)
        p.a[..., 3][m] = a
    return p.image()


# ============================================================================================== the hills

HEATHER = ['#4e4250', '#5e4f5c', '#6f5e69', '#816f78', '#958489']


def hills(night=False):
    p = Paint(night=night)
    rnd = random.Random(43)
    hz = 74
    sky(p, hz)
    stars(p, hz, 13)
    moon(p, 300, 18)
    p.fog = 0.55
    # the far ridge, with a quarry cut into it
    far = (56 + line1d(5, W, ((150, 4.0), (60, 2.0))) - 13 * np.exp(-((np.arange(W) - 268) / 42.0) ** 2)).astype(int)
    fm = (p.Y >= far[None, :])
    p.ramp(fm, 1.5 + facing(far)[None, :] * 0.9 - (p.Y - far[None, :]) * 0.03,
           ['#8a9a96', '#98a8a0', '#a8b6ab', '#b8c3b6'], soft=0.25)
    qx, qw = 240, 46
    qt = far[qx + qw // 2] + 6
    # the cut: a pale back wall in steps, its right side in shadow, a lip of turf along the top
    for b, (inset, hh) in enumerate(((0, 5), (5, 4), (10, 4))):
        y0 = qt + sum(h_ for _, h_ in ((0, 5), (5, 4), (10, 4))[:b])
        x0, x1 = qx + inset, qx + qw - inset - 6
        for y in range(y0, y0 + hh):
            for x in range(x0, x1):
                c = '#93998f' if x < x0 + 4 else '#b6baae'
                if y == y0 + hh - 1: c = '#8f978f'                    # the shadowed bench edge
                if (x * 3 + y * 5) % 17 == 0 and y < y0 + hh - 1 and x >= x0 + 4: c = '#a7ab9f'
                p.px(x, y, c)
    for x in range(qx - 1, qx + qw - 5):
        p.px(x, qt - 1, '#7f8f86'); p.px(x, qt - 2, '#8d9c92' if x % 2 else '#7f8f86')
    for x in range(qx + qw - 8, qx + qw + 14):                       # the spoil heap
        hh = int(7 - abs(x - (qx + qw + 3)) * 0.6)
        for y in range(qt + 13 - hh, qt + 13):
            p.px(x, y, '#b0b4a8' if x < qx + qw + 3 - (y - (qt + 13 - hh)) * 0.3 else '#989e96')
    p.fog = 0.25
    # the middle upland: grass and heather, with a rocky tor on its crest
    mid = (72 + line1d(8, W, ((170, 6.0), (55, 2.4)))).astype(int)
    mid += np.clip(60 - np.arange(W), 0, None) // 6
    mm = p.Y >= mid[None, :]
    gm = ['#6f7c5b', '#7c8862', '#8a946a', '#99a274']
    p.ramp(mm, 1.6 + facing(mid)[None, :] * 0.8 + (p.Y - mid[None, :]) * 0.02, gm, soft=0.25)
    # heather: patches built from small clumps, darker and browner than the grass
    hz_heather = [mix(c, '#a6b2a8', 0.3) for c in HEATHER]
    patches = fbm(W, H * 2, 26, 81, 3)[::2]
    hmask = mm & (patches > 0.6) & (p.Y > mid[None, :] + 2)
    p.ramp(hmask, 1.0 + (patches - 0.6) * 8, hz_heather[1:4], soft=0.4)
    for _ in range(500):
        x, y = rnd.uniform(0, W), rnd.uniform(70, 100)
        if y < mid[int(x)] + 2 or patches[int(y), int(x)] < 0.53: continue
        blob(p, x, y, rnd.uniform(1.2, 2.4), hz_heather[0:4], sq=0.6)
    tx = 96
    tor_cols = [mix(c, '#a8b4ac', 0.3) for c in ['#45424a', '#5c5860', '#77726f', '#938c84', '#aea596']]
    tb = mid[tx] + 3
    shadow(p, tx + 18, tb + 1, 20, 2)
    rock(p, tx, tb, 34, 9, rnd, tor_cols, seed_facets=1, flat_top=0.6)
    rock(p, tx - 3, tb - 8, 24, 7, rnd, tor_cols, seed_facets=1, flat_top=0.6)
    rock(p, tx + 1, tb - 14, 16, 6, rnd, tor_cols, seed_facets=1, flat_top=0.6)
    rock(p, tx + 20, tb, 12, 6, rnd, tor_cols, seed_facets=1)
    # a drystone wall following the lie of the land
    for x in range(140, W):
        y = int(mid[x] + 9 + (x - 140) * 0.04)
        for j in range(3):
            k = 3 if j == 0 else (2 if (x + j * 2) % 4 else 0)
            if j == 2: k = 1 if x % 3 else 0
            p.px(x, y + j, ['#5d605a', '#767870', '#8f9086', '#a8a89c'][k])
        p.darken((p.X == x + 1) & (p.Y == y + 3), 0.85)
    p.fog = 0.0
    # near slope and the floor: rough grass, a few heather clumps, stones
    near = (94 + line1d(18, W, ((100, 2.0), (31, 1.0)))).astype(int)
    fl = ['#5d6a44', '#6a774b', '#788452', '#86905a', '#959c63']
    nm = p.Y >= near[None, :]
    ground(p, 0, H, fl, 91, 2.8, 2.1, patch=0.35, cell=30, mask=nm)
    grass_fringe(p, near, fl[1:], rnd, density=0.4, h=(1, 4))
    for (cx, cy, w) in ((20, 100, 40), (70, 97, 18), (150, 96, 22), (300, 98, 30), (345, 104, 30), (224, 96, 14)):
        for _ in range(int(w / 2)):
            x = cx + rnd.uniform(-w / 2, w / 2)
            y = cy + rnd.uniform(-2, 2.5)
            blob(p, x, y, rnd.uniform(1.8, 3.2), HEATHER[0:4], sq=0.6)
    scatter(p, rnd, 26, 0, W, 102, H - 2, TUFTS, fl[0:3] + [fl[4]])
    scatter(p, rnd, 12, 0, W, 104, H - 2, PEBBLES, ['#5f5c58', '#7d7670', '#a09a90'])
    # boulders at the edges
    rk = ['#3e3c40', '#55525a', '#6d6a6e', '#878282', '#a09a92']
    lich = ['#8c8a5a', '#a6a46a']
    shadow(p, 28, 112, 30, 4)
    rock(p, 10, 110, 40, 30, rnd, rk, moss=lich, flat_top=0.3)
    rock(p, 34, 113, 18, 8, rnd, rk)
    shadow(p, 350, 118, 22, 3.5)
    rock(p, 350, 117, 34, 22, rnd, rk, moss=lich)
    tall_grass(p, 0, 24, H, rnd, fl, height=12, density=0.55)
    tall_grass(p, 330, W, H, rnd, fl, height=10, density=0.55)
    for _ in range(14):
        blob(p, rnd.uniform(0, 30) if rnd.random() < 0.5 else rnd.uniform(326, W), rnd.uniform(128, 136),
             rnd.uniform(2, 3.5), HEATHER[0:4], sq=0.6)
    return p.image()


# ============================================================================================== the village

WALL_STONE = ['#4e4a46', '#67615a', '#7e776c', '#958c7e', '#aaa190']
WALL_WASH = ['#8e8a7c', '#a8a290', '#bdb6a2', '#cec7b2', '#dcd5bf']
THATCH = ['#4f3d26', '#6a5232', '#86693e', '#a0824e', '#b59a62']
SLATE = ['#27323a', '#33414a', '#41525a', '#52646a', '#66787a']
WOOD = ['#2e241c', '#45372a', '#5e4b38', '#776048', '#8c7558']


def stone_courses(p, mask, cols, rnd, base_v=2.0):
    """Fill a wall mask with courses of rough stones: each stone a flat colour with a lit top-left pixel."""
    ys, xs = np.nonzero(mask)
    if not len(ys): return
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    vals = np.zeros(mask.shape)
    mortar = np.zeros(mask.shape, bool)
    lit = np.zeros(mask.shape, bool)
    y = y0
    while y < y1:
        hh = rnd.choice((3, 3, 4))
        x = x0 - rnd.randint(0, 4)
        while x < x1:
            ww = rnd.randint(4, 8)
            vals[y:y + hh, max(x, 0):x + ww] = base_v + rnd.uniform(-0.8, 0.8)
            mortar[y + hh - 1, max(x, 0):x + ww] = True
            if x + ww - 1 >= 0: mortar[y:y + hh, min(p.w - 1, x + ww - 1)] = True
            if 0 <= x < p.w: lit[y, x] = True
            x += ww
        y += hh
    p.ramp(mask, vals, cols, soft=0.15)
    p.fill(mask & mortar, cols[0])
    p.fill(mask & lit & ~mortar, cols[-1])


def window(p, x, y, w, h, lit_at_night=True):
    fr = WOOD
    m = (p.X >= x) & (p.X < x + w) & (p.Y >= y) & (p.Y < y + h)
    p.fill(m, fr[1])
    inner = (p.X >= x + 1) & (p.X < x + w - 1) & (p.Y >= y + 1) & (p.Y < y + h - 1)
    if p.night and lit_at_night:
        p.ramp(inner, 3 - (p.Y - y) * 0.25 - (p.X - x) * 0.1, WARM, soft=0.3, raw=True)
        # glow on the wall around it
        d = np.hypot((p.X + 0.5 - (x + w / 2)) / (w * 1.4), (p.Y + 0.5 - (y + h / 2)) / (h * 1.2))
        p.tint((d < 1) & ~m, '#d9a04a', np.clip(1 - d, 0, 1) * 0.35, raw=True)
        p.fill(inner & (p.X == x + w // 2), fr[1])
    else:
        p.ramp(inner, 1.6 - (p.Y - y) * 0.4, ['#1e2a30', '#2e3e46', '#6e8a8c'], soft=0.2)
        p.fill(inner & (p.X == x + w // 2), fr[2])
        p.px(x + 1, y + 1, '#8fa4a4')
    for i in range(x - 1, x + w + 1): p.px(i, y + h, WALL_WASH[3])   # sill
    for i in range(x, x + w + 1): p.px(i, y + h + 1, WALL_STONE[0])


def cottage(p, x, base, w, wall_h, roof_h, side, rnd, wall='stone', roof='thatch', windows=(), door=None,
            chimney=None, scale=1.0):
    """A cottage seen at a slant: its lit gable end on the left (side px wide), its long front facing us."""
    wt = base - wall_h                         # top of the walls
    peak = wt - roof_h
    fx = x + side                              # where the front wall starts
    wc = WALL_STONE if wall == 'stone' else WALL_WASH
    rc = THATCH if roof == 'thatch' else SLATE
    shadow(p, fx + w * 0.55, base + 1, w * 0.62, 3, k=0.75)
    # gable end (lit)
    gm = p.poly([(x, base), (x, wt), (x + side / 2, peak + 1), (x + side, wt), (x + side, base)])
    front = p.poly([(fx, base), (fx, wt), (fx + w, wt), (fx + w, base)])
    if wall == 'stone':
        stone_courses(p, gm, wc, rnd, 3.0)
        stone_courses(p, front, wc, rnd, 1.8)
    else:
        p.ramp(gm, 3.6 - (p.Y > base - 3) * 1.2, wc, soft=0.2)
        p.ramp(front, 2.4 - (p.Y > base - 3) * 1.0 + (vnoise(W, H, 5, 9) > 0.75) * -0.6, wc, soft=0.2)
    p.fill(front & (p.X == fx), wc[0])
    # front roof plane, overhanging the eaves
    ov = 2
    rm = p.poly([(x + side / 2, peak), (fx + w + 1 + side / 2 - side / 2, peak),
                 (fx + w + ov, wt + 1), (fx - ov + 1, wt + 1)])
    rm &= ~gm | (p.Y < wt)
    rv = 2.6 - (p.Y - peak) / max(1, (wt - peak)) * 1.0 + (p.X - fx) / w * -0.4
    if roof == 'thatch':
        rows = ((p.Y - peak) % 3 == 2) & ((p.X * 7 + p.Y * 3) % 5 != 0)
        rv = rv - rows * 0.9 + (((p.X * 5 + (p.Y // 3) * 3) % 11) == 0) * 0.7
    else:
        rows = (p.Y - peak) % 3 == 2
        joints = ((p.X + ((p.Y - peak) // 3) * 3) % 6 == 0)
        rv = rv - rows * 1.0 - joints * 0.7
    p.ramp(rm, rv, rc, soft=0.15)
    # roof edges: the ridge (lit), the barge along the gable, the dark drip line
    for i in range(int(x + side / 2), int(fx + w + 1)): p.px(i, peak, rc[-1])
    for i in range(int(fx - ov + 1), int(fx + w + ov)): p.px(i, wt + 1, rc[0])
    gl = p.poly([(x - 1, wt + 1), (x + side / 2, peak - 1), (x + side / 2 + 1, peak - 1), (x + 2, wt + 1)])
    p.ramp(gl, 4.0 - (p.X - x) * 0.05, rc, soft=0.2)
    # the shadow of the eaves on the front wall
    p.darken(front & (p.Y >= wt + 2) & (p.Y < wt + 4), 0.72)
    for wx in windows:
        window(p, fx + wx, wt + 5, 6, 6)
    if door is not None:
        dm = (p.X >= fx + door) & (p.X < fx + door + 7) & (p.Y >= base - 12) & (p.Y < base)
        p.ramp(dm, 1.8 + ((p.X - fx - door) % 2 == 0) * 0.6 - (p.Y < base - 10) * 0.8, WOOD, soft=0.1)
        p.fill(dm & ((p.X == fx + door) | (p.Y == base - 12)), WOOD[0])
        p.px(fx + door + 5, base - 6, '#c9a44f')
        if p.night:
            for i in range(fx + door + 1, fx + door + 6): p.px(i, base - 1, WARM[1], raw=True)
    if chimney is not None:
        cx = fx + chimney
        cm = (p.X >= cx) & (p.X < cx + 5) & (p.Y >= peak - 6) & (p.Y < peak + 3)
        stone_courses(p, cm, WALL_STONE, rnd, 2.2)
        for i in range(cx - 1, cx + 6): p.px(i, peak - 7, WALL_STONE[3 if i < cx + 3 else 1])


def fence(p, x0, x1, base, h, cols=WOOD, gap=None):
    for x in range(x0, x1):
        if gap and gap[0] <= x < gap[1]: continue
        post = (x - x0) % 9 == 0
        if post:
            for y in range(base - h - 1, base):
                p.px(x, y, cols[3]); p.px(x + 1, y, cols[1])
            p.px(x, base - h - 2, cols[4])
        for ry in (base - h + 1, base - h // 2):
            if not post and not (post and x == x0):
                p.px(x, ry, cols[3]); p.px(x, ry + 1, cols[1])
    for x in range(x0, x1):
        if gap and gap[0] <= x < gap[1]: continue
        p.darken((p.X == x + 2) & (p.Y == base), 0.8)


def village(night=False):
    p = Paint(night=night)
    rnd = random.Random(53)
    hz = 66
    sky(p, hz)
    stars(p, hz, 17)
    moon(p, 228, 16)
    p.fog = 0.5
    # a green hill behind the village
    far = (58 + line1d(6, W, ((130, 4.0), (40, 1.5), (13, 0.5)))).astype(int)
    fm = p.Y >= far[None, :]
    p.ramp(fm, 2.0 + facing(far)[None, :] * 0.8 - (p.Y - far[None, :]) * 0.03,
           ['#8a9c8a', '#98a894', '#a8b4a0', '#b6bfac'], soft=0.25)
    for x in range(0, W, 3):
        if rnd.random() < 0.35:
            blob(p, x, far[x] + rnd.uniform(4, 12), rnd.uniform(2, 3.5), ['#7c8e7c', '#8a9a88', '#98a694'], sq=0.8)
    p.fog = 0.2
    # the far houses of the village, hazy
    def haze(cols, t=0.5): return [mix(c, '#b6bfac', t) for c in cols]
    for (x, w, wh, rh) in ((70, 26, 9, 8), (104, 20, 8, 7), (196, 30, 10, 8), (232, 22, 8, 7), (262, 26, 9, 8)):
        b = 84
        wm = (p.X >= x) & (p.X < x + w) & (p.Y >= b - wh) & (p.Y < b)
        p.ramp(wm, 2.6 - (p.X > x + 4) * 0.8, haze(WALL_WASH, 0.45), soft=0.1)
        rm = p.poly([(x - 1, b - wh), (x + 5, b - wh - rh), (x + w - 2, b - wh - rh), (x + w + 1, b - wh)])
        p.ramp(rm, 2.4 - (p.Y - (b - wh - rh)) * 0.15, haze(SLATE if x % 2 else THATCH, 0.45), soft=0.2)
        for wx in range(x + 6, x + w - 3, 8):
            if night: p.px(wx, b - wh + 3, WARM[2], raw=True); p.px(wx + 1, b - wh + 3, WARM[1], raw=True)
            else: p.px(wx, b - wh + 3, mix(SLATE[1], '#b6bfac', 0.4)); p.px(wx + 1, b - wh + 3, mix(SLATE[1], '#b6bfac', 0.4))
    p.fog = 0.0
    # meadow between the far houses and the yard
    mg = ['#6b7a52', '#78865a', '#869262', '#949e6a']
    gm = (p.Y >= 84) & (p.Y < 100)
    p.ramp(gm, 2.4 - (p.Y - 84) * 0.06, mg, soft=0.3)
    # the yard: packed earth, a little cobbling near the doors
    yd = ['#6e5d48', '#7d6b52', '#8a775c', '#988565', '#a5926f']
    edge = (97 + line1d(2, W, ((70, 1.2), (19, 0.6)))).astype(int)
    ym = p.Y >= edge[None, :]
    ground(p, 92, H, yd, 71, 3.0, 2.2, patch=0.3, cell=34, mask=ym)
    grass_fringe(p, edge, mg, rnd, density=0.5, h=(1, 3))
    # a drystone wall with a gap, and a wooden fence, across the back of the yard
    for x in range(60, 160):
        yb = edge[x] - 1
        hh = 6
        for y in range(yb - hh, yb + 1):
            row = (y - (yb - hh)) // 3
            k = 2 if ((x + row * 3) % 6 and (y - (yb - hh)) % 3 != 2) else 0
            if y == yb - hh: k = 4 if (x % 6) else 3
            if (x + row * 3) % 6 == 1 and (y - (yb - hh)) % 3 == 0: k = 3
            p.px(x, y, WALL_STONE[k])
    p.darken((p.X >= 62) & (p.X < 162) & (p.Y == edge[None, :] + 0), 0.8)
    fence(p, 204, 300, 99, 9)
    # cottages at the edges and one set back in the middle
    cottage(p, 152, 86, 44, 13, 12, 12, rnd, wall='wash', roof='slate', windows=(9, 30), door=19, chimney=34)
    cottage(p, -14, 99, 58, 26, 22, 18, rnd, wall='stone', roof='thatch', windows=(8, 38), door=22, chimney=6)
    cottage(p, 304, 101, 58, 28, 22, 16, rnd, wall='wash', roof='thatch', windows=(10,), door=26, chimney=38)
    # fishing gear by the right cottage: a net drying on poles, a lobster pot and a barrel
    for x in (292, 312):
        for y in range(84, 104): p.px(x, y, WOOD[3] if y < 103 else WOOD[1])
    for x in range(293, 312):
        sag = int(3 * math.sin((x - 292) / 20 * math.pi))
        for y in range(86 + sag, 99 + sag // 2):
            if (x + y) % 3 == 0 or (x - y) % 3 == 0: p.px(x, y, '#7d7462' if (x + y) % 2 else '#65604f')
        p.px(x, 85 + sag, WOOD[3])
    def barrel(cx, b):
        shadow(p, cx + 5, b + 1, 7, 1.8)
        m = (np.abs(p.X + 0.5 - cx) < 5 - (np.abs(p.Y + 0.5 - (b - 6)) > 4)) & (p.Y >= b - 11) & (p.Y < b)
        p.ramp(m, 3.2 - np.abs(p.X + 1.5 - cx) * 0.55 - (p.X > cx) * 0.6, WOOD, soft=0.2)
        for yy in (b - 9, b - 3): p.fill(m & (p.Y == yy), '#3a3a36')
        p.fill(m & (p.Y == b - 11), WOOD[4])
    barrel(286, 110)
    barrel(52, 109)
    # lobster pot: a little domed wicker cage
    pm = (np.hypot((p.X + 0.5 - 276) / 6, (p.Y + 0.5 - 108) / 5) < 1) & (p.Y < 108)
    p.ramp(pm, 2 + ((p.X + p.Y) % 2) * 1.0 - (p.X > 277) * 1.0, THATCH, soft=0.2)
    for x in range(270, 283): p.px(x, 108, THATCH[0])
    # cobbles in front of the doors, a few stones and tufts on the yard
    for (cx, cy, rx, ry) in ((30, 104, 22, 4), (336, 106, 24, 4)):
        for y in range(int(cy - ry), int(cy + ry)):
            for x in range(int(cx - rx), int(cx + rx), 3):
                xx = x + (y % 2) * 1
                if ((xx - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 < 1 and y % 2 == 0:
                    p.px(xx, y, yd[4]); p.px(xx + 1, y, yd[3]); p.px(xx, y + 1, yd[1]); p.px(xx + 1, y + 1, yd[1])
    scatter(p, rnd, 24, 0, W, 104, H - 2, PEBBLES, [yd[0], yd[2], yd[4]])
    scatter(p, rnd, 18, 0, W, 102, H - 2, TUFTS, mg[0:3])
    tall_grass(p, 0, 20, H, rnd, mg, height=9, density=0.5)
    tall_grass(p, 340, W, H, rnd, mg, height=9, density=0.5)
    return p.image()


def sky_clouds(seed, bottom):
    return lambda night=False: clouds_layer(seed, bottom, night)


BACKDROPS = {}
for _name, _fn, _cl in (('road', road, sky_clouds(103, 62)), ('coast', coast, coast_clouds),
                        ('forest', forest, forest_clouds), ('hills', hills, sky_clouds(107, 66)),
                        ('village', village, sky_clouds(109, 60))):
    BACKDROPS[_name] = _fn
    BACKDROPS[_name + '_night'] = (lambda f: lambda: f(True))(_fn)
    BACKDROPS[_name + '_clouds'] = (lambda f: lambda: f(False))(_cl)
    BACKDROPS[_name + '_night_clouds'] = (lambda f: lambda: f(True))(_cl)
