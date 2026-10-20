"""The opening cutscene's pictures, painted at the cutscene's native 320 x 180 (scaled up by whole numbers).

Story: the player is a passenger on a small trade boat caught in a storm at night; a huge wave hits; they
wash up on a grey dawn beach among the wreckage; an old fisherman (Yenson) drags them to his driftwood shack.

Written to Assets/Game/Resources/Sprites/Cutscene/:
  cs_storm_sky      320 x 180  night storm sky, sea horizon at y = 104, dark far sea below it
  cs_storm_clouds   640 x 80   ragged low scud, transparent, tiles left-right (drifts across the top)
  cs_waves_back     640 x 60   rolling swells, transparent above, tiles left-right; placed with top at y = 100
  cs_waves_front    640 x 70   big near crests with foam caps and spray, tiles; placed with top at y = 118
  cs_ship           150 x 120  two-masted trader, bow left, lantern-lit; waterline at y = 100 of the image
  cs_ship_flash     150 x 120  the same ship lit by lightning from the upper right (same pixel positions)
  cs_bolt            40 x 110  a forked lightning bolt
  cs_big_wave       360 x 180  a towering breaking wave curling to the left, to slide in from the right
  cs_beach          320 x 180  grey dawn beach with wreckage; clear sand at x 120..200, y 120..150
  cs_surf           320 x 24   a foam edge washing up the sand (slid up and down over the shoreline)
  cs_shack          320 x 180  Yenson's driftwood shack; door centred on x = 232, bottom at y = 142;
                               clear flat path from x 40 to 232 along y 135..145

Painted with the same tools as the battle backdrops (backdrops.py): banded colour ramps with ordered
dithering only at the seams, light from the upper left (except the lightning-lit ship), no anti-aliasing,
every pixel fully opaque or fully transparent, and a dark (never black) outline round the ship and debris."""
import math, os, random
import numpy as np
from PIL import Image, ImageDraw
from palette import PAL
from backdrops import (Paint, sky, cloud, ground, vnoise, fbm, line1d, facing, blob, rock, mix, hexrgb, tohex,
                       scatter, shadow, stone_courses, PEBBLES, TUFTS, WARM)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'Assets', 'Game', 'Resources', 'Sprites', 'Cutscene')

OUTLINE = PAL['o']                      # '#120f14'
OUTLINE_FLASH = '#0f0d13'


# ============================================================================================== helpers

def shift(m, dy, dx):
    """m moved by (dy, dx), zero-filled: out[y, x] = m[y - dy, x - dx]."""
    o = np.zeros_like(m)
    h, w = m.shape
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    o[yd, xd] = m[ys, xs]
    return o


def wshift(m, dy, dx):
    """Like shift, but wrapping round left-right (for tiling layers)."""
    return shift(np.roll(m, dx, axis=1), dy, 0)


def dil(m, diag=False, wrap=False):
    s = wshift if wrap else shift
    o = m | s(m, 1, 0) | s(m, -1, 0) | s(m, 0, 1) | s(m, 0, -1)
    if diag:
        o |= s(m, 1, 1) | s(m, -1, -1) | s(m, 1, -1) | s(m, -1, 1)
    return o


def outline(p, m, col=OUTLINE, diag=False, wrap=False):
    """A one-pixel outline just outside mask m."""
    p.fill(dil(m, diag, wrap) & ~m, col, raw=True)


def opaque(p):
    return p.a[..., 3] > 0


def lines(w, h, segs, width=1):
    """A mask of aliased (hard-edged) line segments / polylines."""
    im = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(im)
    for s in segs:
        d.line([(float(x), float(y)) for x, y in s], fill=255, width=width)
    return np.array(im) > 0


def polymask(w, h, pts, wrap=False):
    im = Image.new('L', (w * (3 if wrap else 1), h), 0)
    d = ImageDraw.Draw(im)
    for ox in ((-w, 0, w) if wrap else (0,)):
        d.polygon([(float(x + ox + (w if wrap else 0)), float(y)) for x, y in pts], fill=255)
    a = np.array(im) > 0
    if wrap:
        a = a[:, :w] | a[:, w:2 * w] | a[:, 2 * w:]
    return a


def bez(pts, n=40):
    """Points along a Bezier curve through control points pts."""
    out = []
    for i in range(n + 1):
        t = i / n
        q = [np.array(c, float) for c in pts]
        while len(q) > 1:
            q = [a * (1 - t) + b * t for a, b in zip(q, q[1:])]
        out.append(tuple(q[0]))
    return out


def glow(p, mask, cx, cy, bands, col, sx=1.0, sy=1.0, jit=2.5, raw=True):
    """Light falling off from a point in a few clean bands (radius, strength), dithered at the band edges."""
    d = np.hypot((p.X + 0.5 - cx) / sx, (p.Y + 0.5 - cy) / sy) + (p.T - 0.5) * jit
    t = np.zeros(d.shape)
    for r, tt in sorted(bands, reverse=True):
        t = np.where(d < r, tt, t)
    m = mask & (t > 0)
    p.tint(m, col, t, raw=raw)


def paste(dst, src, x, y):
    """Composite src's opaque pixels onto dst (a Paint) at x, y."""
    h, w = src.a.shape[:2]
    pl = dst._place(np.ones((h, w), bool), (y, x))
    if pl is None: return
    cs, ls = pl
    s = src.a[ls]
    m = s[..., 3] > 0
    t = dst.a[cs]
    t[m] = s[m]


def wrap_px(p, x, y, c):
    p.px(int(x) % p.w, y, c, raw=True)


def save(img, name, out):
    path = os.path.join(out, name + '.png')
    a = np.array(img)
    dark = a[..., :3].astype(int).sum(axis=2) < 45      # nothing darker than the outline colour
    a[dark, :3] = hexrgb(OUTLINE).astype(np.uint8)
    if a.shape[2] == 4:                     # every pixel fully opaque or fully transparent
        a[..., 3] = np.where(a[..., 3] > 0, 255, 0)
        a[a[..., 3] == 0, :3] = 0
        img = Image.fromarray(a, 'RGBA')
    img.save(path)
    return path


# ============================================================================================== the storm

STORM_SKY = ['#13151e', '#171a25', '#1c202d', '#222736', '#292f40', '#313849', '#3b4354', '#475061', '#56606f']
STORM_CLOUD = ['#14161f', '#191c27', '#1f2330', '#262b39', '#2f3544', '#3a4150', '#48505e', '#5c6470', '#747c84']
RIFT = ['#3e4a5e', '#56637a', '#71809a', '#93a0b2', '#b6bfc4']
NSEA = ['#2b3642', '#242e39', '#1e2731', '#19212a', '#151c23', '#121820']
NFOAM = ['#3d4a55', '#56656e', '#7f8f94', '#aab8b6', '#cbd5cc']
HZ = 104


def storm_sky():
    p = Paint(320, 180)
    rnd = random.Random(401)
    # the sky behind: darkest overhead, a little lighter towards the horizon
    sky(p, HZ, day=STORM_SKY[1:], night=STORM_SKY[1:], soft=0.3)
    # the rift: a long, ragged tear low in the cloud on the right, cold light pouring through it
    xs = np.arange(320)
    def tear(x0, x1, yc0, slope, thick, seed):
        u = np.clip((xs - x0) / (x1 - x0), 0, 1)
        yc = yc0 + (xs - x0) * slope + line1d(seed, 320, ((47, 1.6), (17, 0.8)))
        ht = thick * np.sin(u * math.pi) ** 0.6 * (1 + 0.25 * line1d(seed + 1, 320, ((70, 1.0), (29, 0.4))))
        ht = np.where((xs > x0) & (xs < x1), np.clip(ht, 0, None), 0)
        d = np.abs(p.Y + 0.5 - yc[None, :]) / np.maximum(ht[None, :], 0.01)
        return d, ht
    rdA, htA = tear(130, 336, 58, 0.05, 14.0, 21)
    rdB, htB = tear(52, 134, 84, -0.03, 3.0, 23)
    for rd, k in ((rdA, 4.6), (rdB, 2.6)):
        m = (rd < 1.0) & (p.Y < HZ)
        p.ramp(m, np.clip(1.0 - rd, 0, 1) ** 0.8 * k, RIFT, soft=0.25)
    near = np.clip(1 - (np.minimum(rdA, 4) - 1) / 3, 0, 1)       # 1 at the rift's edge, 0 far from it
    before = p.a[..., :3].copy()
    # cloud banks, far to near: low hazy rolls on the horizon, a heavy shelf above them, the ceiling overhead
    far = [mix(c, '#4a5566', 0.35) for c in STORM_CLOUD[3:8]]
    for i in range(9):
        cloud(p, i * 40 + rnd.uniform(-8, 8), HZ - 2 + rnd.uniform(-2, 1), rnd.uniform(48, 80), rnd, far, flat=True)
    mid = STORM_CLOUD[2:7]
    for i in range(7):
        x = i * 52 + rnd.uniform(-10, 10)
        cloud(p, x, 92 + rnd.uniform(-3, 3), rnd.uniform(70, 110), rnd, mid, flat=rnd.random() < 0.5)
    # ragged clouds riding across the bottom of the rift, lit from behind
    for i in range(9):
        x = 128 + i * 24 + rnd.uniform(-6, 6)
        cloud(p, x, 80 + rnd.uniform(-2, 4) + (x - 230) * 0.04, rnd.uniform(30, 50), rnd, STORM_CLOUD[2:7],
              flat=True)
    # the ceiling: big lobes hanging down out of a solid dark mass, lit underneath by the rift
    top = Paint(320, 90, transparent=True)
    cc = STORM_CLOUD[0:6]
    for i in range(10):
        x = i * 34 + rnd.uniform(-8, 8)
        overrift = 150 < x < 312
        w = rnd.uniform(64, 84) if overrift else rnd.uniform(56, 84)
        tgt = rnd.uniform(14, 20) if overrift else rnd.uniform(20, 28)     # where the lobe meets the ceiling
        cloud(top, x, 88 - tgt, w, rnd, cc)
    flip = top.a[::-1]
    sub = p.a[:90]
    fm = flip[..., 3] > 0
    sub[fm] = flip[fm]
    ceil = (p.Y < (25 - 12 * np.exp(-((np.arange(320) - 232) / 70.0) ** 2))[None, :] + line1d(31, 320, ((60, 2), (19, 1.0)))[None, :])
    p.ramp(ceil, 0.8 + (fbm(320, 180, 24, 33, 2) - 0.5) * 2.0, STORM_CLOUD[:4], soft=0.25)
    # everything near the rift is lit from behind it: silver edges and paler undersides
    cm = np.any(p.a[..., :3] != before, axis=2) & (p.Y < HZ)
    lit = ((rdA < 1.0) | (rdB < 1.0)) & ~cm
    nb = lambda m: shift(m, 1, 0) | shift(m, -1, 0) | shift(m, 0, 1) | shift(m, 0, -1)
    edge = cm & nb(lit)
    p.fill(edge, STORM_CLOUD[-1])
    e2 = cm & ~edge & nb(edge)
    p.dither(e2, STORM_CLOUD[-2], 0.6)
    e3 = cm & ~edge & ~e2 & nb(nb(e2))
    p.dither(e3, STORM_CLOUD[-3], 0.35)
    # a curtain of rain trailing from the shelf on the left
    rain = ((p.X + p.Y // 2) % 6 == 0) & (p.Y > 92) & (p.Y < HZ) & (p.X > 10) & (p.X < 120)
    p.dither(rain & (vnoise(320, 180, 14, 7) > 0.5), '#3c4556', 0.5)
    # the horizon: a hard, slightly lit line where the sea starts, brightest under the rift
    rx = 236
    p.ramp((p.Y >= HZ) & (p.Y < 180), (p.Y - HZ) / 76 * 5.0, NSEA, soft=0.3)
    for x in range(320):
        lit_ = 1 - min(1, abs(x - rx) / 70)
        p.px(x, HZ, '#4a5666' if lit_ > 0.2 else '#323d4a')
        if lit_ > 0.5 and x % 3: p.px(x, HZ + 1, '#3a4656')
    # the far sea: short dark chop, glints under the rift
    for _ in range(900):
        y = rnd.randrange(HZ + 2, 180)
        t = (y - HZ) / 76
        x = rnd.randrange(320)
        ln = rnd.randint(2, 4 + int(t * 8))
        lit_ = max(0, 1 - abs(x - rx - (y - HZ) * 0.3) / (40 + t * 60))
        if rnd.random() < 0.55:
            c = NFOAM[1] if (lit_ > 0.4 and rnd.random() < 0.6) else NFOAM[0] if lit_ > 0.1 or rnd.random() < 0.4 else NSEA[0]
            if lit_ > 0.75 and rnd.random() < 0.4: c = NFOAM[2]
            for i in range(ln): p.px(x + i, y, c)
            for i in range(ln + 1): p.px(x + i - 1, y + 1, NSEA[-1])
        else:
            for i in range(ln + 2): p.px(x + i, y, NSEA[min(5, 3 + int(t * 3))])
    return p.image()


def storm_clouds():
    W_, H_ = 640, 80
    p = Paint(W_, H_, transparent=True)
    rnd = random.Random(55)
    cols = ['#181b25', '#1e222d', '#262b37', '#303643', '#3c4350', '#4b535f']
    # torn scud: flattened lumps strung across the loop, far (paler, higher) ones first
    items = []
    x = 0.0
    while x < W_:
        items.append((rnd.random(), x, rnd.uniform(30, 54), rnd.uniform(50, 110)))
        x += rnd.uniform(22, 44)
    for depth, x, y, w in sorted(items, key=lambda t: -t[0]):
        cc = [mix(c, '#2c3444', 0.35 * depth) for c in cols]
        cloud(p, x, y, w, rnd, cc[:5], wrap=W_, flat=True)
    m = opaque(p)
    # rag the edges: nibble the outline with noise, and pull streamers out underneath, blown to the right
    n = fbm(W_, H_, 8, 57, 2, wrap=True)
    edge = m & ~(wshift(m, 1, 0) & wshift(m, -1, 0) & wshift(m, 0, 1) & wshift(m, 0, -1))
    bite = edge & (n < 0.5)
    p.a[bite] = 0
    m = opaque(p)
    edge = m & ~(wshift(m, 1, 0) & wshift(m, -1, 0) & wshift(m, 0, 1) & wshift(m, 0, -1))
    p.a[edge & (n < 0.4)] = 0
    m = opaque(p)
    lo = m & ~wshift(m, -1, 0)                      # bottom edge pixels
    for y, x in zip(*np.nonzero(lo)):
        if rnd.random() < 0.18:
            ln = rnd.randint(3, 12)
            for i in range(ln):
                yy = y + 1 + i // 3
                if yy >= H_: break
                if rnd.random() < 1 - i / ln * 0.5:
                    wrap_px(p, x + i, yy, cols[2] if i < ln / 2 else cols[1])
    # a darker, lumpy underside line
    m = opaque(p)
    p.fill(m & ~wshift(m, -1, 0), cols[0], raw=True)
    return p.image()


def waves_back():
    W_, H_ = 640, 60
    p = Paint(W_, H_, transparent=True)
    rnd = random.Random(61)
    top = 11 + line1d(71, W_, ((160, 3.0), (64, 2.0), (32, 1.0)), wrap=True)
    top = top.astype(int)
    face = facing(np.concatenate([top[-20:], top, top[:20]]), k=9)[20:-20]
    m = p.Y >= top[None, :]
    d = p.Y - top[None, :]
    cols = ['#121921', '#151e27', '#1a242e', '#1f2b36', '#25333f', '#2d3c48', '#384855']
    # each swell: lit near its crest, darker in the trough below, broad bands across the whole layer
    n = fbm(W_, H_ * 4, 64, 63, 3, wrap=True)[::4]
    swell = np.sin((p.Y + n * 12) * 0.42) * 0.5 + 0.5
    v = 4.6 - np.clip(d, 0, 30) * 0.12 + face[None, :] * 1.0 - (d > 2) * 0.8 + swell * 1.2 - (n - 0.5) * 1.5
    v = v - np.clip(d - 30, 0, None) * 0.05
    p.ramp(m, v, cols, soft=0.25)
    # foam on the crests that stand highest, and broken streaks of foam on the slopes
    hi = (top < 9)
    for x in range(W_):
        y = top[x]
        if hi[x] or (x * 7 % 13 == 0):
            wrap_px(p, x, y, NFOAM[2] if hi[x] and face[x] > -0.2 else NFOAM[1])
            if hi[x] and rnd.random() < 0.5: wrap_px(p, x, y + 1, NFOAM[0])
        else:
            wrap_px(p, x, y, cols[-1])
    for _ in range(260):
        x = rnd.randrange(W_)
        y = top[x] + rnd.randint(3, 40)
        if y >= H_: continue
        ln = rnd.randint(2, 7)
        c = NFOAM[1] if rnd.random() < 0.35 else NFOAM[0]
        for i in range(ln):
            wrap_px(p, x + i, y + (1 if i > ln * 0.6 else 0), c)
        for i in range(ln):
            if y + 1 < H_: wrap_px(p, x + i + 1, y + 1 + (1 if i > ln * 0.6 else 0), cols[0])
    # a few flecks of spray over the highest crests
    for x in range(W_):
        if hi[x] and rnd.random() < 0.08:
            wrap_px(p, x + rnd.randint(-1, 2), top[x] - rnd.randint(1, 3), NFOAM[1])
    return p.image()


FRONT = ['#0e141a', '#111a21', '#152029', '#1a2731', '#203039', '#283a43', '#31474e', '#3d5759']
FFOAM = ['#4a6462', '#6f8c84', '#9fb7aa', '#c9d9cb', '#e2ebe0']


def waves_front():
    W_, H_ = 640, 70
    p = Paint(W_, H_, transparent=True)
    rnd = random.Random(73)
    # crests: separate peaks of different heights and widths, steep in front, long behind (wrapping)
    xs = np.arange(W_)
    prof = np.zeros(W_)
    x = 0.0
    while x < W_ - 30:
        h = rnd.choice((rnd.uniform(6, 12), rnd.uniform(14, 26)))
        wl, wr = rnd.uniform(14, 34), rnd.uniform(28, 70)
        for ox in (-W_, 0, W_):
            u = xs - (x + ox)
            sh_ = np.where(u < 0, 1 - np.clip(-u / wl, 0, 1), 1 - np.clip(u / wr, 0, 1))
            prof = np.maximum(prof, h * (sh_ * sh_ * (3 - 2 * sh_)) ** 1.2)
        x += rnd.uniform(38, 110)
    top = (26 - prof + line1d(77, W_, ((32, 1.0), (13, 0.6)), wrap=True)).astype(int)
    top = np.clip(top, 3, 40)
    face = facing(np.concatenate([top[-20:], top, top[:20]]), k=7)[20:-20]
    m = p.Y >= top[None, :]
    d = (p.Y - top[None, :]).astype(float)
    n = fbm(W_, H_ * 3, 32, 79, 3, wrap=True)[::3]
    # body: a lit, glassy band under the crest (the light coming through the lip), then dark
    glass = np.exp(-((d - 7) / 5.0) ** 2) * np.clip(1 - top[None, :] / 30.0, 0, 1)
    v = 2.6 + glass * 3.6 + face[None, :] * 0.9 - np.clip(d - 8, 0, 40) * 0.07 + (n - 0.5) * 2.0
    p.ramp(m, v, FRONT, soft=0.22)
    # dark troughs: a line where the next swell's back rises
    tr = (np.sin((p.Y + n * 10) * 0.33) > 0.86) & (d > 12) & (fbm(W_, H_, 16, 83, 1, wrap=True) > 0.5)
    p.fill(m & tr, FRONT[0])
    # foam caps: thick white-green foam on each crest, lace spilling down the face
    lace = fbm(W_, H_, 8, 81, 2, wrap=True)
    capd = np.clip(12 - top / 2.6, 1, 9)                      # higher crests carry more foam
    cap = m & (d < capd[None, :] * (0.55 + lace * 0.7))
    fv = 4.3 - d * 0.42 + face[None, :] * 0.5 + (lace - 0.5) * 1.6
    p.ramp(cap, fv, FFOAM, soft=0.2)
    p.fill(m & (d == 0) & (top[None, :] < 26), FFOAM[4])
    # lace: streaks and holes in the foam running down the face
    sl = m & (d > 3) & (d < capd[None, :] * 2.6) & (lace > 0.55) & (((p.X + p.Y) % 3) != 0)
    p.ramp(sl, 1.0 + (lace - 0.55) * 8 - d * 0.08, FFOAM[:4], soft=0.2)
    holes = cap & (lace < 0.34) & (d > 1)
    p.fill(holes, FFOAM[0])
    # streaks of foam on the dark lower slopes
    for _ in range(320):
        x = rnd.randrange(W_)
        y = top[x] + rnd.randint(10, 60)
        if y >= H_ - 1: continue
        ln = rnd.randint(2, 9)
        c = FFOAM[1] if rnd.random() < 0.3 else FFOAM[0]
        for i in range(ln):
            wrap_px(p, x + i, y + (i * 2 // max(ln, 1)), c)
    # spray: flung up off the highest crests, blown to the right
    for x in range(W_):
        if top[x] < 18 and rnd.random() < 0.35:
            for _ in range(rnd.randint(1, 3)):
                dx, dy = rnd.randint(0, 6), rnd.randint(1, 3 + (18 - top[x]) // 2)
                y = top[x] - dy
                if y < 0: continue
                c = FFOAM[4] if dy < 3 and rnd.random() < 0.5 else FFOAM[3] if rnd.random() < 0.5 else FFOAM[2]
                wrap_px(p, x + dx, y, c)
                if rnd.random() < 0.2: wrap_px(p, x + dx + 1, y, FFOAM[2])
    return p.image()


# ============================================================================================== the ship

SW, SH = 150, 120
WATERLINE = 100

MATS = {
    #           night (lit only by the lantern and the sky)           lightning flash (pale, from the upper right)
    'wood':   (['#161416', '#1c191b', '#242020', '#2d2725', '#38302b', '#463b33', '#57493d'],
               ['#1e1f2b', '#2f3344', '#465062', '#626f82', '#8592a4', '#aebac8', '#d6dfe6']),
    'deck':   (['#191617', '#211d1d', '#2a2422', '#352c28', '#41362f', '#504237', '#625142'],
               ['#212330', '#353a4c', '#4f5a6c', '#6d7a8c', '#909eb0', '#b8c4d0', '#dde5ea']),
    'canvas': (['#1f2129', '#272a33', '#30343e', '#3b404a', '#484d56', '#575c63', '#6a6e72'],
               ['#262a38', '#3b4256', '#58637a', '#7d8aa0', '#a6b2c4', '#cfd8e2', '#eff3f1']),
    'red':    (['#1e1316', '#2a171a', '#36201f', '#452824', '#53312a', '#5f3a30', '#6c4436'],
               ['#2a1f2c', '#3e2c3a', '#58404c', '#765a64', '#98808a', '#bba8ae', '#ddd2d2']),
    'metal':  (['#18181e', '#202128', '#292b33', '#33363f', '#40444c', '#505560', '#636874'],
               ['#1c1e2a', '#2e3242', '#454c60', '#626c80', '#8590a4', '#adb8c8', '#d8e0ea']),
}


class Ship:
    def __init__(self, flash):
        self.flash = flash
        self.p = Paint(SW, SH, transparent=True)
        self.solid = np.zeros((SH, SW), bool)
        self.Y, self.X = self.p.Y, self.p.X

    def pal(self, mat):
        return MATS[mat][1 if self.flash else 0]

    def part(self, m, form, nx=0.0, ny=0.0, mat='wood', soft=0.2, edge=True):
        """Paint a solid part. form: its own brightness (0..1, plank tone, folds...). nx, ny: which way its
        surface faces (-1 = left / up, 1 = right / down)."""
        cols = self.pal(mat)
        n = len(cols) - 1
        if self.flash:
            light = np.clip(0.5 + 0.55 * nx - 0.45 * ny, 0, 1.2)
            v = (0.15 + form * 0.45 + light * 0.55 - (1 - self.X / SW) * 0.22 + (1 - self.Y / SH) * 0.08) * n
        else:
            light = np.clip(0.5 - 0.35 * nx - 0.45 * ny, 0, 1)
            v = (form * 0.62 + light * 0.22) * n
        self.p.ramp(m, v, cols, soft=soft, raw=True)
        if edge:
            # the shadow-side edge of this part, where it lies over something behind it
            far = (lambda a: shift(a, 0, -1) | shift(a, -1, 0)) if not self.flash else (lambda a: shift(a, 0, 1) | shift(a, -1, 0))
            e = m & far(~m) & far(self.solid)
            self.p.fill(e, cols[0], raw=True)
        self.solid |= m

    def rope(self, segs, lit=True):
        m = lines(SW, SH, segs)
        c = '#1a171d' if not self.flash else '#1a1b26'
        self.p.fill(m, c, raw=True)
        if lit:
            # every few pixels the rope catches a glint
            hl = m & (((self.X * 3 + self.Y * 5) % 7) == 0)
            self.p.fill(hl, '#3c4250' if not self.flash else '#9aa8bc', raw=True)
        return m


def sheer(x):
    """The top of the hull (the rail), per x: rising to the bow (left) and more to the stern."""
    x = np.asarray(x, float)
    return 80 - 7.0 * ((x - 66) / 60) ** 2 - np.clip(x - 118, 0, None) * 0.22


def ship(flash=False):
    s = Ship(flash)
    p, X, Y = s.p, s.X, s.Y
    rnd = random.Random(5)
    # ---------------------------------------------------------------- masts and spars (behind everything)
    fm_x, mm_x = 44, 86
    def mast(x0, top, bottom):
        lean = 0.05
        cx = x0 + (bottom - Y) * lean
        m = (np.abs(X + 0.5 - cx - 0.5) <= 1.0) & (Y >= top) & (Y < bottom)
        m |= (np.abs(X + 0.5 - cx - 0.5) <= 1.5) & (Y >= top + 18) & (Y < bottom)      # thicker lower down
        nx = np.clip((X + 0.5 - cx - 0.5) / 1.5, -1, 1)
        s.part(m, 0.55 + (Y > top + 18) * 0.05, nx=nx, mat='wood', soft=0.1)
        # the mast cap and a small lookout block
        cap = (np.abs(X + 0.5 - (x0 + (bottom - top) * lean) - 0.5) <= 2) & (Y >= top) & (Y < top + 2)
        s.part(cap, 0.6, ny=-0.5, mat='wood')
        return lambda y: x0 + (bottom - y) * lean + 0.5
    fmx = mast(fm_x, 6, 80)
    mmx = mast(mm_x, 1, 80)

    def spar(x0, y0, x1, y1, w=2):
        m = lines(SW, SH, [((x0, y0), (x1, y1))], width=w)
        s.part(m, 0.6, ny=-0.3, mat='wood', soft=0.1)
        return m
    # yards cocked over by the roll of the ship
    spar(25, 19, 64, 23)               # fore topsail yard
    spar(22, 39, 66, 41)               # fore course yard
    spar(66, 15, 108, 12)              # main topsail yard
    spar(67, 35, 107, 33)              # main course yard
    spar(88, 56, 134, 52)              # main boom over the deckhouse
    spar(-1, 63, 18, 74, w=3)          # bowsprit, poking out past the bow

    def furl(x0, y0, x1, y1, seed):
        """A sail reefed up tight under its yard: a lumpy roll with ties."""
        r = random.Random(seed)
        pts = []
        for i in range(int(x1 - x0) + 1):
            t = i / max(1, x1 - x0)
            x = x0 + i
            yb = y0 + (y1 - y0) * t
            th = 2 + (r.random() < 0.5) + 2.2 * math.sin(t * math.pi)
            pts.append((x, yb, th))
        m = np.zeros((SH, SW), bool)
        for (x, yb, th) in pts:
            m |= (X == x) & (Y >= yb + 1) & (Y < yb + 1 + th)
        ny = np.zeros((SH, SW))
        for (x, yb, th) in pts:
            col = (X == x)
            ny = np.where(col, (Y - yb - 1 - th / 2) / max(th / 2, 1), ny)
        ties = (((X - x0) % 6) == 3)
        s.part(m, 0.55 - ties * 0.3, ny=ny, mat='canvas', soft=0.15)
        return m
    furl(27, 20, 62, 23, 1)
    furl(69, 16, 105, 13, 2)
    furl(91, 56, 131, 53, 3)

    # the fore course: torn, half its sheet gone, billowing out to the right
    def billow(x0, x1, ytop0, ytop1, depth, belly, tear_seed, holes=()):
        r = random.Random(tear_seed)
        w = x1 - x0
        rag = line1d(tear_seed, SW, ((7, 1.6), (3, 1.0)))
        m = np.zeros((SH, SW), bool)
        u = np.clip((X + 0.5 - x0) / w, 0, 1)
        ytop = ytop0 + (ytop1 - ytop0) * u
        bot = ytop + depth * (1 - 0.35 * (u - 0.5) ** 2) + rag[np.clip(X, 0, SW - 1)] * 2.2
        # long tatters
        for _ in range(4):
            tx = r.uniform(x0 + 3, x1 - 3)
            bot = np.where(np.abs(X + 0.5 - tx) < r.uniform(1, 2.2), bot + r.uniform(3, 9), bot)
        sx = belly * np.sin(np.clip((Y - ytop) / depth, 0, 1) * math.pi * 0.8)     # bellying out
        m = (X + 0.5 >= x0 + sx * 0.4) & (X + 0.5 <= x1 + sx) & (Y > ytop + 1) & (Y < bot)
        for (hx, hy, hr) in holes:
            hd = np.hypot((X + 0.5 - hx) / hr, (Y + 0.5 - hy) / (hr * 1.3))
            m &= hd > 1 + 0.25 * np.sin(np.arctan2(Y - hy, X - hx) * 5)
        uu = (X + 0.5 - x0) / w
        folds = np.sin(uu * math.pi * 5 + (Y - ytop) * 0.08)
        nx = (uu - 0.55) * 1.4 + folds * 0.25
        form = 0.62 + folds * 0.14 - np.clip((Y - ytop) / depth, 0, 1) * 0.12
        s.part(m, form, nx=nx, ny=-0.1, mat='canvas', soft=0.22)
        return m
    billow(23, 65, 40, 42, 22, 7, 11, holes=((47, 52, 3.2), (33, 57, 2)))
    # the main course: mostly torn away, ragged strips streaming from the yard
    billow(68, 106, 34, 32, 7, 3, 12)
    for (ax, ay, bx, by, w0) in ((72, 37, 86, 58, 4), (95, 36, 112, 52, 3), (101, 36, 120, 44, 2)):
        pts = []
        for i in range(13):
            t = i / 12
            x = ax + (bx - ax) * t + math.sin(t * 7 + ax) * 2
            y = ay + (by - ay) * t
            ww = w0 * (1 - t * 0.8)
            pts.append((x, y, ww))
        m = np.zeros((SH, SW), bool)
        for (x, y, ww) in pts:
            m |= (np.hypot(X + 0.5 - x, (Y + 0.5 - y) * 1.6) < ww * 0.75 + 0.6)
        s.part(m, 0.55 + np.sin(X * 0.9) * 0.12, nx=0.3, mat='canvas', soft=0.2)
    # a torn jib still clinging to the forestay
    jm = polymask(SW, SH, [(3, 64), (24, 30), (21, 36), (17, 46), (15, 52), (12, 61), (10, 66)])
    s.part(jm, 0.6 + ((X + Y) % 5 == 0) * -0.15, nx=-0.2, mat='canvas', soft=0.2)

    # ---------------------------------------------------------------- deck cargo, deckhouse
    sh = sheer(np.arange(SW))
    shy = sh[np.clip(X, 0, SW - 1)]
    # crates and a couple of barrels lashed amidships, showing over the rail
    for (cx, cw, ch, kind) in ((52, 10, 9, 'crate'), (62, 8, 6, 'crate'), (58, 7, 13, 'crate'), (70, 6, 7, 'barrel'),
                               (75, 6, 7, 'barrel')):
        top = sheer(cx) - ch
        m = (X >= cx) & (X < cx + cw) & (Y >= top) & (Y < sheer(cx) + 2)
        if kind == 'barrel':
            m &= ~(((X == cx) | (X == cx + cw - 1)) & ((Y == top) | (Y == sheer(cx) + 1)))
            form = 0.55 - (Y == top + 2) * 0.3 - (Y == top + 5) * 0.3
            nx = (X + 0.5 - cx - cw / 2) / (cw / 2)
        else:
            form = 0.55 + ((X - cx) % 3 == 0) * -0.15 - (Y == top + ch // 2) * 0.2
            nx = 0.0
        s.part(m, form, nx=nx, ny=np.where(Y == top, -1, 0), mat='deck', soft=0.15)
    # a tarpaulin roped over the crates
    tm = polymask(SW, SH, [(51, sheer(51) - 9), (58, sheer(58) - 14), (65, sheer(65) - 13), (68, sheer(68) - 6),
                            (66, sheer(66) + 1), (51, sheer(51) + 1)])
    s.part(tm, 0.5 + np.sin(X * 1.3 + Y * 0.4) * 0.12, nx=(X - 60) / 9, ny=-0.3, mat='canvas', soft=0.2)
    # the deckhouse at the stern: plank walls, a lit window, a little roof
    hx0, hx1, htop = 103, 133, 60
    hm = (X >= hx0) & (X < hx1) & (Y >= htop) & (Y < shy + 2)
    planks = ((X - hx0) % 3 == 0)
    s.part(hm, 0.58 - planks * 0.18 + (X < hx0 + 2) * 0.1, ny=0, mat='deck', soft=0.12)
    rm = polymask(SW, SH, [(hx0 - 3, htop + 1), (hx0 - 1, htop - 3), (hx1 + 1, htop - 3), (hx1 + 3, htop + 1)])
    s.part(rm, 0.7 - ((X % 4) == 0) * 0.1, ny=-0.8, mat='wood', soft=0.12)
    # door (towards the bow) and window (lantern-lit from inside)
    dm = (X >= hx0 + 3) & (X < hx0 + 8) & (Y >= htop + 4) & (Y < shy + 2)
    s.part(dm, 0.32 + ((X - hx0) == 5) * -0.1, mat='wood', soft=0.1)
    p.px(hx0 + 7, htop + 11, '#8e6e30' if not flash else '#9aa4b0', raw=True)
    wx0, wy0 = hx0 + 14, htop + 4
    wm = (X >= wx0) & (X < wx0 + 7) & (Y >= wy0) & (Y < wy0 + 5)
    s.part(wm, 0.2, mat='wood', soft=0.1, edge=False)
    win = (X >= wx0 + 1) & (X < wx0 + 6) & (Y >= wy0 + 1) & (Y < wy0 + 4)
    p.ramp(win, 3 - (Y - wy0 - 1) * 0.6 - (X - wx0) * 0.12, WARM, soft=0.3, raw=True)
    p.fill(win & (X == wx0 + 3), MATS['wood'][1 if flash else 0][1], raw=True)

    # ---------------------------------------------------------------- the hull
    stem = bez([(8, sheer(8)), (10, 88), (16, 98), (34, 105)], 30)
    keel = [(48, 108), (80, 110), (110, 108), (128, 104), (137, 98)]
    hull_pts = [(144, sheer(144) - 4)] + [(x, sheer(x)) for x in range(142, 7, -4)] + stem + keel + \
               [(139, 92), (143, 82)]
    hm = polymask(SW, SH, hull_pts)
    hm &= Y >= shy - 1
    keel_y = np.interp(X, [8, 20, 40, 80, 110, 128, 140], [72, 100, 107, 110, 108, 104, 92])
    t = np.clip((Y - shy) / np.maximum(keel_y - shy, 1), 0, 1)            # 0 at the rail, 1 at the keel
    d = Y - shy
    strake = np.floor((d - 1) / 4)
    seam = ((d - 1) % 4 == 0) & (d > 2)
    # each strake's planks end at butt joints, staggered strake by strake
    butt = (((X + strake * 9) % 23) == 0) & (d > 2)
    tone = (np.sin(strake * 2.3 + 1) * 0.06 + np.sin((X + strake * 9) // 23 * 1.7) * 0.06)
    form = 0.62 + tone - seam * 0.32 - butt * 0.22 - t * 0.3
    form = np.where(d < 2, 0.88, form)                                     # the cap rail
    form = np.where(d == 2, 0.25, form)                                    # its shadow
    wale = (d >= 7) & (d < 10)
    form = np.where(wale, 0.42 - (d == 9) * 0.2, form)
    nx = -np.clip((34 - X) / 26, 0, 1) * 0.9 + np.clip((X - 128) / 12, 0, 1) * 0.6
    ny = np.where(d < 2, -1.0, t * 0.9 - 0.15)
    s.part(hm & ~wale, form, nx=nx, ny=ny, mat='wood', soft=0.18)
    s.part(hm & wale, form, nx=nx, ny=ny * 0.5, mat='red', soft=0.18, edge=False)
    # the transom at the stern: vertical boards, a little in shadow
    tr = hm & (X >= 139 - (Y - 70) * 0.12)
    s.part(tr, 0.45 - ((X % 2) == 0) * 0.12, nx=0.8, mat='wood', soft=0.12, edge=False)
    # scuppers and a hawse hole, nails at the butts
    for x in range(20, 136, 9):
        yy = int(sheer(x)) + 4
        p.px(x, yy, MATS['wood'][1 if flash else 0][0], raw=True)
        p.px(x + 1, yy, MATS['wood'][1 if flash else 0][1], raw=True)
    for (hx, hy) in ((15, 77),):
        p.stamp(hx, hy, ['.11.', '1221', '.11.'], [MATS['wood'][1 if flash else 0][0], OUTLINE], raw=True)
    # the anchor hung at the bow
    am = ['..1..', '.111.', '..1..', '..1..', '..1..', '1.1.1', '.111.']
    p.stamp(19, 80, am, [MATS['metal'][1 if flash else 0][3]], raw=True)
    s.solid |= opaque(p)
    # rudder at the stern, under the transom
    rud = polymask(SW, SH, [(138, 88), (143, 88), (145, 106), (137, 108)])
    s.part(rud, 0.4, nx=0.6, mat='wood', soft=0.12)
    # stanchions of the stern rail and the lantern post
    for x in range(118, 145, 6):
        m = (X == x) & (Y >= sheer(x) - 5) & (Y < sheer(x))
        s.part(m, 0.6, mat='wood', edge=False)
    rail = lines(SW, SH, [[(117, sheer(117) - 5), (145, sheer(145) - 6)]])
    s.part(rail, 0.75, ny=-1, mat='wood', soft=0.1, edge=False)
    post = (X >= 143) & (X < 145) & (Y >= 42) & (Y < sheer(144))
    s.part(post, 0.55, nx=(X - 143.5), mat='wood', soft=0.1)
    arm = (Y == 42) & (X >= 143) & (X < 148)
    s.part(arm, 0.6, ny=-1, mat='wood')

    # below the waterline the hull is dark and wet
    below = s.solid & (Y >= WATERLINE)
    wcols = FRONT[1:6] if not flash else ['#1a2a36', '#24384a', '#30485c', '#3e5a70', '#506e84']
    wn = fbm(SW, SH, 6, 9, 2)
    p.ramp(below, 3.2 - (Y - WATERLINE) * 0.35 + (wn - 0.5) * 2.0, wcols, soft=0.25, raw=True)
    # the hull's reflection just under the surface, broken up
    refl = below & (Y < WATERLINE + 5) & (((X // 2 + Y) % 3) == 0)
    p.tint(refl, '#3a2e28' if not flash else '#8a96a8', 0.4)
    # water slapping along the hull at the waterline, piling up at the bow
    fr = random.Random(3)
    for x in range(SW):
        col = np.nonzero(s.solid[:, x])[0]
        if not len(col) or col.max() < WATERLINE: continue
        wy = WATERLINE + int(round(math.sin(x * 0.21) * 1.2))
        if fr.random() < 0.8:
            p.px(x, wy, FFOAM[2] if not flash else FFOAM[4], raw=True)
        if fr.random() < 0.5:
            p.px(x, wy + 1, FFOAM[0] if not flash else FFOAM[2], raw=True)
        if fr.random() < 0.25:
            p.px(x, wy - 1, FFOAM[1] if not flash else FFOAM[3], raw=True)
    for _ in range(40):          # spray thrown up at the bow
        bx, by = fr.uniform(10, 30), WATERLINE - fr.expovariate(1 / 4.0)
        if s.solid[int(min(SH - 1, by)), int(bx)]:
            p.px(bx, by, FFOAM[fr.randint(1, 3)] if not flash else FFOAM[4], raw=True)

    # ---------------------------------------------------------------- outline, then rigging over it
    out_col = OUTLINE_FLASH if flash else OUTLINE
    outline(p, s.solid, out_col)
    allm = opaque(p)
    # rim light: the cold sky catches the top-left edges by night; the flash catches the top-right edges
    if not flash:
        rim = s.solid & (~shift(allm, 1, 0) | ~shift(allm, 0, 1)) & (Y < WATERLINE)
        p.tint(rim, '#7f8fa0', 0.45, raw=True)
    else:
        rim = s.solid & (~shift(allm, 1, 0) | ~shift(allm, 0, -1)) & (Y < WATERLINE)
        p.fill(rim, '#eef3f1', raw=True)
        rim2 = s.solid & ~rim & (shift(rim, 0, 1) | shift(rim, 1, 0)) & (Y < WATERLINE - 4) & ((X + Y) % 2 == 0)
        p.tint(rim2, '#eef3f1', 0.5, raw=True)

    # standing rigging: stays, shrouds with ratlines, braces
    ftop, mtop = (fmx(8), 8), (mmx(3), 3)
    s.rope([[(0, 63), ftop]])                                 # forestay to the bowsprit
    s.rope([[(3, 64), (24, 30)]])                              # jib stay
    s.rope([[ftop, (mmx(22), 22)]])                            # spring stay between the masts
    s.rope([[mtop, (146, 44)]])                                # backstay to the stern
    s.rope([[(mmx(5), 5), (125, 52)]])
    for (topx, topy, feet) in ((fmx(12), 12, (32, 37, 51, 56)), (mmx(8), 8, (74, 79, 94, 99))):
        segs = [[(topx, topy), (fx_, sheer(fx_) + 4)] for fx_ in feet]
        s.rope(segs, lit=False)
        # ratlines: little rungs across the shrouds
        for k in range(6):
            yy = topy + 14 + k * 6
            if yy > sheer(feet[0]): break
            for a, b in ((0, 1), (2, 3)):
                xa = topx + (feet[a] - topx) * (yy - topy) / (sheer(feet[a]) + 4 - topy)
                xb = topx + (feet[b] - topx) * (yy - topy) / (sheer(feet[b]) + 4 - topy)
                s.rope([[(xa, yy), (xb, yy)]], lit=False)
    # braces from the yard ends, and a loose line whipping in the wind
    s.rope([[(25, 19), (8, 68)], [(66, 15), (112, 52)], [(108, 12), (140, 60)]])
    s.rope([bez([(22, 39), (12, 48), (24, 54), (15, 66)], 12)])
    s.rope([bez([(107, 33), (118, 28), (126, 34), (134, 26)], 12)])

    # ---------------------------------------------------------------- the stern lantern and its light
    lx, ly = 146, 47
    if not flash:
        lit = s.solid & (Y < WATERLINE + 2)
        glow(p, lit, lx, ly, ((6, 0.42), (12, 0.26), (20, 0.14), (32, 0.06)), '#d9a04a', sx=1.0, sy=1.15)
    else:
        glow(p, s.solid, lx, ly, ((6, 0.3), (11, 0.15)), '#d9a04a')
    p.stamp(lx - 2, ly - 3, ['.11.', '1221', '1341', '1441', '1331', '.11.'],
            [OUTLINE, '#5a3a22', '#d9a04a', '#f0d27a'], raw=True)
    p.px(lx - 1, ly - 4, OUTLINE, raw=True)
    if not flash:   # a faint halo of glow around the lamp
        for (dx, dy) in ((-3, 0), (3, 0), (0, 4), (-2, -3), (2, -3), (-3, 2), (3, 2)):
            if 0 <= lx + dx < SW and p.a[ly + dy, lx + dx, 3] == 0:
                p.px(lx + dx, ly + dy, '#5a3a22', raw=True)
    return p.image()


# ============================================================================================== lightning

def bolt():
    W_, H_ = 40, 110
    p = Paint(W_, H_, transparent=True)
    rnd = random.Random(17)
    core = np.zeros((H_, W_), bool)
    thick = np.zeros((H_, W_), bool)

    def path(x, y, length, drift, depth):
        pts = [(x, y)]
        while y < length:
            step = rnd.randint(3, 8)
            y = min(length, y + step)
            x = x + rnd.choice((-1, 1)) * rnd.randint(1, 4) + drift
            x = max(3, min(W_ - 4, x))
            pts.append((x, y))
            if depth == 0 and rnd.random() < 0.22 and y < length - 30:
                path(x, y, min(H_ - 1, y + rnd.randint(18, 34)), rnd.choice((-1.6, 1.6)), 1)
        return pts
    rnd.seed(17)
    main = path(20, 0, H_ - 1, 0.0, 0)
    m = lines(W_, H_, [main])
    thick |= lines(W_, H_, [main[:len(main) * 2 // 3]], width=2)
    # the forks were drawn as they were made; draw them now as a lighter, thinner pass
    rnd.seed(29)
    forks = []
    for (fx_, fy_, ln, dr) in ((main[3][0], main[3][1], 34, -1.8), (main[6][0], main[6][1], 26, 1.7),
                                (main[9][0], main[9][1], 18, -1.4) if len(main) > 9 else (20, 70, 14, -1.4)):
        pts = [(fx_, fy_)]
        x, y = fx_, fy_
        while y < fy_ + ln:
            y += rnd.randint(3, 6)
            x += dr * rnd.uniform(1, 2.5) + rnd.choice((-1, 0, 1))
            pts.append((max(1, min(W_ - 2, x)), min(H_ - 1, y)))
        forks.append(pts)
        # a twig off the fork
        k = len(pts) // 2
        tw = [pts[k], (pts[k][0] - dr * 3, pts[k][1] + 4), (pts[k][0] - dr * 4, pts[k][1] + 8)]
        forks.append(tw)
    fm = lines(W_, H_, forks)
    core = m | thick
    # glow: two rings round the core, the outer one dithered
    g1 = dil(core | fm, diag=False) & ~(core | fm)
    g2 = dil(dil(core, diag=True), diag=False) & ~(core | fm | g1)
    p.dither(g2, '#4f6a9a', 0.55, raw=True)
    p.fill(g1, '#8fb0dc', raw=True)
    p.fill(fm, '#c9daf0', raw=True)
    p.fill(core, '#f6f7f2', raw=True)
    p.fill(thick & ~m, '#dfe9f6', raw=True)
    return p.image()


# ============================================================================================== the big wave

BIG = ['#0d151a', '#101b20', '#132227', '#17292d', '#1b3133', '#203a3a', '#274641', '#30534a', '#3c6255', '#4b7262']
BFOAM = ['#55706b', '#7b958c', '#a6bcb0', '#cddbce', '#e8efe4']


def big_wave():
    W_, H_ = 360, 180
    p = Paint(W_, H_, transparent=True)
    X, Y = p.X, p.Y
    rnd = random.Random(91)
    # the silhouette: a face sweeping up from the lower left into a lip that curls over to the left
    face = bez([(-10, 176), (70, 150), (128, 112), (150, 60)], 40)
    under = bez([(150, 60), (146, 34), (122, 32), (98, 52)], 24)        # the roof of the tube
    tongue = bez([(98, 52), (92, 58), (86, 54), (90, 40)], 10)          # the lip's tip, folding down
    back = bez([(90, 40), (104, 14), (150, 2), (210, 8), (270, 16), (370, 10)], 50)
    pts = face + under + tongue + back + [(370, 190), (-10, 190)]
    body = polymask(W_, H_, pts)
    # the inside of the tube: closed off from the lip's tip down to the face
    tube = polymask(W_, H_, [(150, 60)] + under + [(94, 58), (104, 82), (124, 104), (142, 80)]) & ~body
    # flow: streaks running along the face, the back sloping away to the right
    n = fbm(W_ * 4, H_, 48, 93, 3)[:, ::4]
    n2 = fbm(W_, H_, 20, 95, 2)
    fx = np.interp(Y, [p[1] for p in face[::-1]], [p[0] for p in face[::-1]])  # x of the face on each row
    dface = X + 0.5 - fx
    # the lit, glassy band just inside the face where light comes through the water
    glass = np.exp(-((dface - 16) / 14.0) ** 2) * np.clip((150 - Y) / 80, 0, 1) * (Y > 40)
    height = np.clip(1 - Y / 180, 0, 1)
    v = 2.0 + height * 3.6 + glass * 3.4 + (n - 0.5) * 3.0 - np.clip((X - 220) / 140, 0, 1) * 1.0
    v = v - np.clip(dface - 60, 0, None) * 0.008
    p.ramp(body, v, BIG, soft=0.22)
    # dark streak lines down the face (flow), lighter ones on the back
    streak = body & (np.abs(np.sin(X * 0.21 + n * 9 + Y * 0.04)) < 0.08) & (Y > 30)
    p.fill(streak & (dface < 90), BIG[1])
    p.fill(streak & (dface >= 90) & (Y < 80), BIG[6])
    # the tube: dark inside, with the curtain of water pouring off the lip
    p.ramp(tube, 1.4 + (Y - 40) * 0.02 + (n2 - 0.5) * 1.5, BIG[:4], soft=0.25)
    # the curtain pouring off the lip: irregular falling streaks, broken lower down
    cr = random.Random(95)
    for _ in range(26):
        x = cr.uniform(96, 146)
        y0 = 40 + cr.uniform(0, 8)
        ln = cr.uniform(10, 50)
        for y in range(int(y0), int(y0 + ln)):
            xx = x - (y - y0) * 0.12
            if 0 <= int(y) < H_ and tube[int(y), int(xx)] and cr.random() < 1.0 - (y - y0) / ln * 0.6:
                p.px(xx, y, BFOAM[2] if y - y0 < ln * 0.4 else BFOAM[1], raw=True)
    # foam: a thick lip along the crest and over the curling tongue, lace running down the back
    allw = body | tube
    top_y = np.argmax(allw, axis=0)
    dtop = Y - top_y[None, :]
    lace = fbm(W_, H_, 10, 97, 2)
    lip = allw & (dtop >= 0) & (dtop < 3 + 6 * lace + np.clip((260 - X) / 30, 0, 5))
    lip |= body & (np.hypot(X - 96, Y - 46) < 12 + lace * 4)                # the tongue itself
    fv = 4.0 - dtop * 0.35 + (lace - 0.5) * 2.0 - np.clip((X - 200) / 80, 0, 1)
    p.ramp(lip, fv, BFOAM, soft=0.2)
    p.fill(lip & (lace < 0.36) & (dtop > 2), BFOAM[0])                     # holes in the foam
    # foam streaks trailing down the face from the lip
    fl = body & (lace > 0.56) & (dtop < 40) & (dface < 50) & (((X + Y * 2) % 3) != 0)
    p.ramp(fl, 1.2 + (lace - 0.56) * 6 - dtop * 0.04, BFOAM[:4], soft=0.2)
    # the whitewater churning along the bottom where the lip comes down
    lace2 = fbm(W_, H_ * 2, 12, 99, 3)[::2]
    churn_top = 150 - (lace2 - 0.5) * 24 + np.clip(X - 140, 0, None) * 0.16
    churn = allw & (Y > churn_top)
    dch = Y - churn_top
    cv = 3.0 - dch * 0.16 + (lace2 - 0.5) * 7.0 + (lace - 0.5) * 3.0
    p.ramp(churn, cv, BFOAM, soft=0.2)
    p.fill(churn & (cv < 0.6), BIG[4])
    p.fill(churn & (cv < -0.3), BIG[2])
    # spume flung up off the churn
    for _ in range(160):
        x = rnd.uniform(0, 260)
        if not churn[:, int(x)].any(): continue
        yt = np.argmax(churn[:, int(x)])
        y = yt - rnd.expovariate(1 / 4.0)
        p.px(x, y, BFOAM[rnd.randint(2, 4)], raw=True)
    # spray: blown up and off the crest to the left and up, in clumps and single drops
    for _ in range(900):
        x = rnd.uniform(70, 350)
        ty = top_y[int(min(W_ - 1, max(0, x)))]
        dist = rnd.expovariate(1 / 7.0)
        y = ty - dist * rnd.uniform(0.5, 1.2) - 1
        x -= dist * rnd.uniform(0.2, 1.5)
        if y < 0 or x < 0: continue
        c = BFOAM[4] if dist < 4 else BFOAM[3] if dist < 9 else BFOAM[2] if dist < 15 else BFOAM[1]
        p.px(x, y, c, raw=True)
        if dist < 6 and rnd.random() < 0.4:
            p.px(x + 1, y, c, raw=True); p.px(x, y + 1, BFOAM[2], raw=True)
    # a veil of mist dithered over the crest
    mist = ~allw & (dtop > -14) & (dtop < 0) & (X > 80) & (lace > 0.45)
    p.dither(mist, BFOAM[1], np.clip(1 + dtop / 14, 0, 1) * 0.35, raw=True)
    # the face's leading edge darkened so it reads against a dark sky
    edge = body & ~shift(body, 0, 1) & ~lip & ~churn
    p.fill(edge, BIG[7])
    return p.image()


# ============================================================================================== dawn

DAWN_SKY = ['#76818c', '#808b94', '#8b959c', '#979fa4', '#a3aaab', '#b0b5b2', '#bdc0b9', '#c9cbc1']
STRATUS = ['#6c7680', '#78838c', '#868f96', '#959ca0', '#a6abac', '#b8bbb7']
GSEA = ['#8e9c98', '#7f908d', '#718583', '#647a79', '#586f6f', '#4d6566']
DSAND = ['#7c7466', '#8a8170', '#988e7a', '#a59a84', '#b2a78e', '#beb498', '#cac0a4']
WSAND = ['#5d6560', '#696f67', '#76796d', '#838374', '#90907e']
CLIFF = ['#2a2d30', '#34373a', '#3f4245', '#4b4e50', '#5a5c5c', '#6a6b69']
WRECK = ['#2e2621', '#3e332a', '#504234', '#635240', '#76644e', '#8a785e', '#a0906f']   # sea-soaked wood
DRIFT = ['#3a3834', '#4f4b44', '#666057', '#7e776b', '#958d7f', '#aaa291', '#bdb5a2']   # bleached driftwood
SAILC = ['#5f5e58', '#76746b', '#8d8a7e', '#a39f91', '#b8b3a3', '#cbc6b5']
ROPE = ['#3e3426', '#5a4a32', '#76633f', '#93804f']
WEED = ['#262a1c', '#343a24', '#444b2c', '#565c34']


def dawn_sky(p, bottom, seed, glow_x=250):
    sky(p, bottom, day=DAWN_SKY, night=DAWN_SKY, soft=0.3)
    # a pale, cold brightening low in the sky where the sun is somewhere behind the cloud
    gd = np.hypot((p.X + 0.5 - glow_x) / 90, (p.Y + 0.5 - bottom) / 26)
    p.ramp((gd < 1) & (p.Y < bottom), (1 - gd) * 3.0, ['#bdc0b9', '#c9cbc1', '#d3d2c6', '#dcd9cb'], soft=0.3)
    # long flat strands of stratus, darker underneath
    n = fbm(p.w, bottom * 5, 60, seed, 3)[::5]
    for (yc, th, k) in ((10, 9, 0.0), (24, 6, 0.35), (36, 5, 0.6), (46, 3, 0.8)):
        band = np.exp(-((p.Y[:bottom] - yc) / th) ** 2)
        d = n * band
        m = np.zeros((p.h, p.w), bool)
        m[:bottom] = d > 0.38
        v = np.zeros((p.h, p.w))
        v[:bottom] = 1.0 + (d - 0.38) * 6 + k * 2.2 - (p.Y[:bottom] > yc) * 0.8
        p.ramp(m, v, STRATUS, soft=0.25)
        under = m & ~shift(m, -1, 0)
        p.fill(under, STRATUS[max(0, int(k * 3))])


def gulls(p, pts, col='#4b4f53'):
    for (x, y, k) in pts:
        rows = [['1...1', '.1.1.'], ['11.11', '..1..'], ['.1.1.', '1.1.1']][k]
        p.stamp(x, y, rows, [col], raw=True)


class Prop:
    """A piece of wreckage painted on its own small canvas, then outlined, given a shadow and pasted down."""

    def __init__(self, w, h):
        self.p = Paint(w, h, transparent=True)
        self.w, self.h = w, h

    def down(self, scene, x, y, shadow_k=0.72, sh=(2, 1), outline_it=True):
        m = opaque(self.p)
        if outline_it:
            outline(self.p, m, OUTLINE)
        full = opaque(self.p)
        # the shadow falls down-right of the light
        sm = np.zeros((scene.h, scene.w), bool)
        pl = scene._place(full, (y + sh[1], x + sh[0]))
        if pl:
            cs, ls = pl
            sm[cs] = full[ls]
        scene.darken(sm, shadow_k)
        paste(scene, self.p, x, y)


def plank(w, h, x0, y0, x1, y1, th, rnd, cols=WRECK, nails=True, broken=(True, True)):
    """A broken plank from (x0, y0) to (x1, y1), th thick, with grain, a lit top edge and splintered ends."""
    pr = Prop(w, h)
    p = pr.p
    L = math.hypot(x1 - x0, y1 - y0)
    ux, uy = (x1 - x0) / L, (y1 - y0) / L
    vx, vy = -uy, ux
    s = (p.X + 0.5 - x0) * ux + (p.Y + 0.5 - y0) * uy       # along
    t = (p.X + 0.5 - x0) * vx + (p.Y + 0.5 - y0) * vy       # across
    jag0 = np.where(broken[0], (np.abs(np.sin(t * 2.7)) * 3.5), 0)
    jag1 = np.where(broken[1], (np.abs(np.sin(t * 2.3 + 1)) * 4.0), 0)
    m = (np.abs(t) <= th / 2) & (s >= jag0) & (s <= L - jag1)
    grain = np.sin(t * 2.2 + np.sin(s * 0.15) * 1.3) * 0.5 + 0.5
    v = 3.0 - (t / (th / 2)) * (1.0 if vy < 0 else -1.0) * 1.2 + (grain - 0.5) * 1.2
    v = np.where((np.abs(t) > th / 2 - 1) & ((t < 0) == (vy < 0)), 4.6, v)        # lit upper edge
    v = np.where((np.abs(t) > th / 2 - 1) & ((t < 0) != (vy < 0)), 1.0, v)
    p.ramp(m, v, cols, soft=0.15)
    knot = np.hypot(s - L * rnd.uniform(0.3, 0.7), t - rnd.uniform(-1, 1)) < 1.2
    p.fill(m & knot, cols[1])
    if nails:
        for k in (0.2, 0.75):
            nx_, ny_ = x0 + ux * L * k, y0 + uy * L * k
            p.px(nx_, ny_, '#5a4e46'); p.px(nx_ + 1, ny_, '#3a2e2a')
    return pr


def barrel_split(rnd):
    """A barrel lying on its side, burst open at one end, staves sprung."""
    pr = Prop(40, 22)
    p = pr.p
    X, Y = p.X, p.Y
    cx, cy, L, R = 16, 10, 22, 7.5
    u = (X + 0.5 - (cx - L / 2)) / L                        # 0 at the closed end .. 1 at the burst end
    bulge = R * (0.86 + 0.14 * np.sin(np.clip(u, 0, 1) * math.pi))
    tear = 1.0 - 0.18 * (np.abs(np.sin((Y + 0.5) * 1.3)) > 0.5)
    body = (u >= 0) & (u <= tear) & (np.abs(Y + 0.5 - cy) <= bulge)
    ny = (Y + 0.5 - cy) / np.maximum(bulge, 1)
    stave = ((Y + 0.5 - cy) * 2.2 / R)
    sv = np.abs(np.sin(stave * math.pi / 2 * 2.5))
    v = 3.6 - ny * 2.0 - (sv < 0.18) * 1.0
    p.ramp(body, v, WRECK, soft=0.15)
    # hoops
    for hu in (0.15, 0.3, 0.72):
        hm = body & (np.abs(u - hu) < 0.5 / L)
        p.ramp(hm, 2.2 - ny * 1.4, ['#2c2a2a', '#45403c', '#6a625a', '#87807a'], soft=0.1)
    # the closed end: an ellipse face, lit
    em = np.hypot((X + 0.5 - (cx - L / 2)) / 2.6, (Y + 0.5 - cy) / R) < 1
    p.ramp(em, 3.4 - (X - (cx - L / 2)) * 0.4 + ((Y % 3) == 0) * -0.6, WRECK, soft=0.15)
    # the open end: dark inside
    im_ = np.hypot((X + 0.5 - (cx + L / 2 - 1)) / 2.8, (Y + 0.5 - cy) / (R - 1.5)) < 1
    p.ramp(im_, 0.6 + (Y - cy) * 0.1, WRECK[:3], soft=0.2)
    # sprung staves fanning out of the open end
    for (ang, ln) in ((-0.5, 9), (0.15, 11), (0.6, 8)):
        x0, y0 = cx + L / 2 - 1, cy + math.sin(ang) * R * 0.9
        x1, y1 = x0 + math.cos(ang) * ln, y0 + math.sin(ang) * ln * 0.6
        sm = lines(40, 22, [[(x0, y0), (x1, y1)]], width=2)
        p.ramp(sm & ~opaque(p), 3.0 - (Y - cy) * 0.1, WRECK, soft=0.2)
    return pr


def rope_tangle(w, h, rnd, loops=5):
    pr = Prop(w, h)
    p = pr.p
    m = np.zeros((h, w), bool)
    order = np.zeros((h, w))
    k = 0
    for i in range(loops):
        cx, cy = rnd.uniform(w * 0.3, w * 0.7), rnd.uniform(h * 0.35, h * 0.65)
        rx, ry = rnd.uniform(w * 0.15, w * 0.38), rnd.uniform(h * 0.18, h * 0.38)
        a0 = rnd.uniform(0, 6.28)
        pts = [(cx + math.cos(a0 + a) * rx * (1 + 0.15 * math.sin(a * 3)),
                cy + math.sin(a0 + a) * ry * (1 + 0.15 * math.cos(a * 2))) for a in np.linspace(0, 5.5, 40)]
        lm = lines(w, h, [pts], width=2)
        m |= lm
        k += 1
        order = np.where(lm, k, order)
    # a loose end trailing off
    tail = bez([(w * 0.6, h * 0.5), (w * 0.85, h * 0.7), (w * 0.9, h * 0.95), (w - 2, h - 2)], 16)
    tm = lines(w, h, [tail], width=2)
    m |= tm
    # the lay of the rope: alternate light and dark pixels along it, lit on its upper side
    up = ~shift(m, 1, 0)
    v = 1.6 + up * 1.2 + (((p.X + p.Y) % 3) == 0) * -0.8
    p.ramp(m, v, ROPE, soft=0.1)
    return pr


def torn_sail(w, h, rnd):
    pr = Prop(w, h)
    p = pr.p
    X, Y = p.X, p.Y
    rag = line1d(rnd.randrange(999), w, ((6, 1.5), (3, 1.0)))
    ragy = line1d(rnd.randrange(999), h, ((5, 1.2), (2.5, 0.8)))
    pts = [(2, h * 0.35), (w * 0.4, 1), (w - 3, h * 0.25), (w - 2, h * 0.75), (w * 0.55, h - 2), (5, h - 3)]
    m = polymask(w, h, pts)
    m &= (Y >= 1 + np.clip(rag[np.clip(X, 0, w - 1)], 0, None))                      # torn top edge
    m &= (X <= w - 3 - np.clip(ragy[np.clip(Y, 0, h - 1)] * 1.5, 0, None))           # torn right edge
    hole = np.hypot((X - w * 0.62) / 3.5, (Y - h * 0.48) / 2.6) < 1 + 0.3 * np.sin(np.arctan2(Y - h * 0.48, X - w * 0.62) * 4)
    m &= ~hole
    folds = np.sin(X * 0.32 + Y * 0.55) + 0.5 * np.sin(X * 0.13 - Y * 0.3)
    v = 3.0 + folds * 0.9 - ((Y - h / 2) / h) * 0.6
    p.ramp(m, v, SAILC, soft=0.2)
    # bolt rope along the lower edge, with a couple of grommets
    edge = m & ~shift(m, -1, 0) & (X < w * 0.6)
    p.fill(edge, ROPE[1])
    for gx in range(6, int(w * 0.55), 7):
        ys = np.nonzero(edge[:, gx])[0]
        if len(ys): p.px(gx, ys[0] - 1, '#6a6458'); p.px(gx, ys[0] - 2, ROPE[2])
    # a seam across it
    seam = m & (np.abs((X - w * 0.3) - (Y - h * 0.2) * 0.8) < 0.5)
    p.fill(seam, SAILC[1])
    return pr


def weed(p, x, y, rnd, n=6, spread=10):
    """A clump of wet seaweed strands lying on the sand."""
    for _ in range(n):
        xx, yy = x + rnd.uniform(-spread / 2, spread / 2), y + rnd.uniform(-2, 2)
        ln = rnd.randint(4, 9)
        dx = rnd.choice((-1, 1))
        for i in range(ln):
            c = WEED[1 + (i % 3 == 0)] if i < ln - 1 else WEED[3]
            p.px(xx + i * dx, yy + math.sin(i * 0.9 + xx) * 1.2, c, raw=True)
            if i % 2 == 0: p.px(xx + i * dx, yy + math.sin(i * 0.9 + xx) * 1.2 + 1, WEED[0], raw=True)
    # bladder pods
    for _ in range(n // 2):
        p.px(x + rnd.uniform(-spread / 2, spread / 2), y + rnd.uniform(-2, 2), WEED[3], raw=True)


def beach_sea(p, top, rnd, x_from=0):
    """The grey-green sea with gentle swell between `top` and the shore."""
    m = (p.Y >= top) & (p.X >= x_from)
    p.ramp(m & (p.Y < 120), np.clip((p.Y - top) / 40, 0, 1) ** 0.8 * (len(GSEA) - 1), GSEA, soft=0.25)
    for _ in range(int(900 * (p.w - x_from) / p.w)):
        y = rnd.randrange(top + 1, 118)
        t = (y - top) / 40
        x = rnd.randrange(x_from, p.w)
        ln = rnd.randint(2, 4 + int(t * 10))
        if rnd.random() < 0.6:
            c = '#a5b2ac' if rnd.random() < 0.6 else '#b7c1b9'
            for i in range(ln): p.px(x + i, y, c)
            if t > 0.25:
                for i in range(ln + 1): p.px(x + i, y + 1, GSEA[-1] if t > 0.5 else GSEA[-2])
        else:
            for i in range(ln): p.px(x + i, y, GSEA[min(5, int(t * 5) + 1)])
    # long low swells: lighter bands with dark backs
    for (yy, amp) in ((top + 9, 1.0), (top + 17, 1.4), (top + 26, 1.8)):
        ln_ = (yy + line1d(yy, p.w, ((70, amp), (23, 0.6)))).astype(int)
        for x in range(x_from, p.w):
            if math.sin(x * 0.05 + yy) + math.sin(x * 0.017) > -0.6:
                p.px(x, ln_[x], '#9fada8' if x % 5 else '#b9c3bb')
                p.px(x, ln_[x] + 1, GSEA[-1])


def headland(p, x1, top_y, base_y, rnd, seed=3):
    """A dark cliff at the left edge, its face cut into rough planes, falling away into the sea."""
    xs = np.arange(p.w)
    u = np.clip(xs / x1, 0, 1)
    edge = top_y + u * 6 + (base_y - top_y) * np.clip((u - 0.5) / 0.5, 0, 1) ** 0.75 + line1d(seed, p.w, ((23, 1.6), (9, 0.8))) * (0.4 + u)
    edge = np.where(xs > x1, 999, edge).astype(int)
    m = (p.Y >= edge[None, :]) & (p.Y < base_y + 8)
    # the face: tall blocks split by vertical joints, each block lit on its left, ledges catching the light
    jr = random.Random(seed)
    col_id = np.zeros(p.w, int)
    x, k = 0, 0
    while x < p.w:
        wdt = jr.randint(5, 12)
        col_id[x:x + wdt] = k
        x += wdt; k += 1
    cid = col_id[np.clip(p.X, 0, p.w - 1)]
    joint = np.diff(np.pad(cid, ((0, 0), (1, 0)), mode='edge'), axis=1) != 0
    tone = np.array([jr.uniform(-0.5, 0.5) for _ in range(k + 1)])[cid]
    lx = np.zeros(p.w)
    for i in range(p.w):
        lx[i] = i - np.argmax(col_id == col_id[i])
    left = lx[np.clip(p.X, 0, p.w - 1)] < 1
    per = np.array([jr.randint(6, 17) for _ in range(k + 1)])[cid]
    offs = np.array([jr.randint(0, 16) for _ in range(k + 1)])[cid]
    ledge_y = (p.Y + offs + (p.X * 0.4).astype(int)) % per
    ledge_y = np.where(vnoise(p.w, p.h, 6, seed + 9) > 0.45, ledge_y, 5)
    joint &= vnoise(p.w, p.h, 5, seed + 11) > 0.3
    v = 2.3 + tone + left * 1.0 - (ledge_y == 0) * 0.0 + (ledge_y == 1) * 1.4 + (ledge_y == 2) * -0.9
    v = v - joint * 1.6 - np.clip(p.Y - edge[None, :], 0, 80) * 0.018 + (vnoise(p.w, p.h, 3, seed) - 0.5) * 0.6
    p.ramp(m, v, CLIFF, soft=0.1)
    # the lit rim along the top of the cliff
    p.fill(m & ~shift(m, 1, 0) & (p.X < x1), CLIFF[-1])
    # grass and thrift on the clifftop
    for x in range(p.w):
        if edge[x] < 900:
            p.px(x, edge[x], '#56603e' if x % 3 else '#6a7348')
            if x % 2 == 0: p.px(x, edge[x] - 1, '#4a5436')
            if (x * 7) % 11 == 0: p.px(x, edge[x] + 1, '#4a5436')
    # surf breaking at its foot
    for x in range(0, x1 + 12):
        if math.sin(x * 0.4) + math.sin(x * 0.13) > -0.3:
            yb = base_y + int(math.sin(x * 0.2) * 1.5)
            p.px(x, yb, '#d4d8cc');
            if x % 2: p.px(x, yb - 1, '#b8c0b8')
    return m


def beach():
    p = Paint(320, 180)
    rnd = random.Random(201)
    HZB = 60
    dawn_sky(p, HZB, 211, glow_x=250)
    gulls(p, [(208, 22, 0), (226, 30, 1), (172, 16, 2)])
    beach_sea(p, HZB, rnd)
    p.fill((p.Y == HZB), '#9aa7a3')
    # far debris adrift: a spar and a crate bobbing
    p.stamp(232, 76, ['.1111111111.', '122222222221'], ['#2e2a26', '#57493a'], raw=True)
    p.stamp(260, 84, ['.11.', '1231', '1221'], ['#2e2a26', '#4f4234', '#6e5c46'], raw=True)
    # the shore: wet sand under a wandering line, dry sand below
    shore = (100 + line1d(31, 320, ((120, 3.0), (41, 1.6), (13, 0.5)))).astype(int)
    wetb = (shore + 12 + line1d(33, 320, ((90, 2.0), (29, 1.0)))).astype(int)
    land = p.Y >= shore[None, :]
    ground(p, 96, 180, DSAND, 41, 2.6, 4.4, patch=0.35, cell=34, soft=0.3, mask=land)
    wet = land & (p.Y < wetb[None, :])
    p.ramp(wet, 0.6 + (p.Y - shore[None, :]) / np.maximum(wetb - shore, 1)[None, :] * 3.4, WSAND, soft=0.3)
    # the sky's sheen on the wet sand, in streaks
    sheen = wet & (p.Y < shore[None, :] + 6) & (np.sin(p.X * 0.09 + p.Y * 1.7) > 0.5)
    p.fill(sheen & (((p.X + p.Y) % 2) == 0), '#a0a8a6')
    # foam at the water's edge and a spent wash further up
    for x in range(320):
        y = shore[x]
        p.px(x, y, '#d4d8cd'); p.px(x, y - 1, '#b2bdb8' if x % 6 else '#d4d8cd')
        if (x // 7 + x // 19) % 3: p.px(x, y + 1, '#aab0a6')
        if math.sin(x * 0.07) + math.sin(x * 0.19) > 0.3: p.px(x, y - 3, '#b8c2bc')
    for x in range(320):
        if math.sin(x * 0.045 + 1) + math.sin(x * 0.13) > -0.2:
            p.px(x, wetb[x] - 1, '#8e8c7c')
            p.px(x, wetb[x], DSAND[1])
    # wind ripples in the dry sand: short curved dashes, lit on top
    for _ in range(420):
        x, y = rnd.randrange(320), rnd.randrange(118, 178)
        if 116 <= x <= 204 and 117 <= y <= 153: continue
        ln = rnd.randint(3, 7)
        for i in range(ln):
            yy = y + (1 if (i == 0 or i == ln - 1) else 0)
            p.px(x + i, yy, DSAND[3] if (y > 150) else DSAND[2])
            p.px(x + i, yy - 1, DSAND[5] if (y > 150) else DSAND[4])
    scatter(p, rnd, 40, 0, 320, 112, 178, PEBBLES, ['#6f6858', '#8c8370', '#b0a68c'], avoid=[(112, 206)])
    # shells
    for _ in range(14):
        x, y = rnd.randrange(320), rnd.randrange(116, 176)
        if 116 <= x <= 204 and 117 <= y <= 153: continue
        p.stamp(x, y, ['.2.', '232', '.1.'], ['#8a7f6e', '#c9c1b0', '#e0dacb'])
    # the headland on the left
    headland(p, 84, 16, 99, rnd)
    rk = ['#2e3134', '#3f4245', '#53565a', '#6a6c6c', '#83837e']
    rock(p, 14, 108, 34, 16, rnd, rk)
    rock(p, 40, 106, 16, 7, rnd, rk)
    rock(p, 4, 118, 22, 9, rnd, rk, moss=['#3c4a2c', '#4f5c34'])
    p.fill(dil((p.Y == 107) & (p.X > 28) & (p.X < 52), False) & (p.Y == 108) & ((p.X % 3) != 0), '#c2c8c0')
    # seaweed along the tide line
    for x in range(8, 316, 23):
        if 112 <= x <= 206: continue
        weed(p, x + rnd.randint(-4, 4), wetb[min(319, x)] + rnd.randint(-1, 3), rnd, n=rnd.randint(2, 5), spread=8)
    # ---------------------------------------------------------------- the wreckage
    sail = torn_sail(54, 24, rnd)
    sail.down(p, 14, 148)
    pr = plank(52, 20, 3, 15, 48, 5, 5, rnd)                              # long plank lying across the sail
    pr.down(p, 30, 140)
    pr = plank(36, 16, 3, 4, 32, 11, 4, rnd, broken=(True, False))
    pr.down(p, 70, 118)
    pr = plank(26, 12, 2, 8, 23, 3, 3, rnd, nails=False)
    pr.down(p, 210, 113)
    pr = plank(60, 24, 4, 18, 55, 6, 6, rnd, broken=(False, True))         # a heavy beam, the rail of the ship
    pr.down(p, 206, 152)
    # a few treenails sticking out of the beam
    for (x, y) in ((222, 162), (238, 158), (250, 156)):
        p.px(x, y - 1, '#8a785e'); p.px(x, y - 2, '#a0906f')
    pr = plank(30, 14, 3, 3, 26, 10, 3, rnd)
    pr.down(p, 276, 124)
    bar = barrel_split(rnd)
    bar.down(p, 238, 126)
    rope = rope_tangle(34, 20, rnd, loops=3)
    rope.down(p, 80, 140)
    pr = plank(24, 22, 3, 18, 20, 3, 3, rnd, nails=False)                  # a stave-like splinter
    pr.down(p, 286, 150)
    # a section of mast with its rope still on, half sunk into the sand at the bottom right
    pr = plank(80, 18, 3, 12, 76, 8, 7, rnd, cols=DRIFT, nails=False, broken=(True, False))
    pr.down(p, 250, 166)
    weed(p, 214, 158, rnd, n=5, spread=10)
    weed(p, 46, 150, rnd, n=4, spread=10)
    weed(p, 286, 128, rnd, n=3, spread=6)
    weed(p, 100, 160, rnd, n=4, spread=10)
    cr = Prop(22, 16)
    q = cr.p
    box = (q.X >= 2) & (q.X < 18) & (q.Y >= 4) & (q.Y < 15)
    topf = polymask(22, 16, [(2, 4), (6, 0), (21, 0), (18, 4)])
    side = polymask(22, 16, [(18, 4), (21, 0), (21, 11), (18, 15)])
    slat = ((q.Y - 4) % 4 == 3)
    q.ramp(box & ~((q.Y >= 8) & (q.Y < 11) & (q.X > 8)), 3.2 - slat * 1.4 + ((q.X % 7) == 0) * -0.8, WRECK, soft=0.1)
    q.ramp(topf & ~((q.X > 9) & (q.X < 15)), 4.6 - ((q.X + q.Y) % 4 == 0) * 0.8, WRECK, soft=0.1)
    q.ramp(side, 1.6 - slat * 0.8, WRECK, soft=0.1)
    q.fill((q.Y >= 8) & (q.Y < 11) & (q.X > 8) & (q.X < 18), WRECK[0])          # a stove-in slat
    q.fill(topf & (q.X > 9) & (q.X < 15), WRECK[0])
    cr.down(p, 156, 158)
    rope2 = lines(320, 180, [bez([(262, 170), (256, 160), (246, 168), (236, 172)], 14)], width=1)
    p.fill(rope2, ROPE[2]); p.fill(shift(rope2, 1, 0) & ~rope2, ROPE[0])
    return p.image(), shore


def surf():
    W_, H_ = 320, 24
    p = Paint(W_, H_, transparent=True)
    rnd = random.Random(301)
    edge = (6 + line1d(303, W_, ((80, 2.0), (27, 1.2), (9, 0.5)), wrap=True)).astype(int)
    d = p.Y - edge[None, :]
    lace = fbm(W_, H_ * 2, 8, 305, 2, wrap=True)[::2]
    # the thin sheet of water behind the foam: sand seen through it, paler and bluer, sparse dither
    sheet = (d >= 2) & (p.Y < H_ - 1)
    amt = np.clip(0.75 - d * 0.045, 0.08, 1) * np.clip((H_ - 1 - p.Y) / 4, 0, 1)
    p.dither(sheet & ((p.X + p.Y * 3) % 2 == 0), '#9ea6a0', amt * 1.6)
    p.dither(sheet & ((p.X + p.Y * 3) % 2 == 1), '#aeb6b0', amt * 1.1)
    # thin bubbly lines trailing behind the edge
    for k in (5, 9, 14):
        ln = (edge + k + line1d(k, W_, ((40, 1.4),), wrap=True)).astype(int)
        for x in range(W_):
            if lace[0, x] + math.sin(x * 0.3 + k) * 0.3 > 0.45 and 0 <= ln[x] < H_ - 1:
                p.px(x, ln[x], '#c2cac4' if k < 9 else '#b4beb9')
    # the leading edge: a lacy band of foam, brightest at its front
    band = (d >= -1) & (d < 3 + (lace > 0.5) * 1)
    holes = (lace < 0.35) & (d > 0)
    p.ramp(band & ~holes, 3.2 - d * 0.7 + (lace - 0.5) * 1.5, ['#9fa8a4', '#bcc5c0', '#d8ddd5', '#eceee6'], soft=0.25)
    for x in range(W_):
        y = edge[x] - 1
        if x % 3 != 1: p.px(x, y, '#eceee6')
        if rnd.random() < 0.15: p.px(x, y - 1, '#d8ddd5')
        if rnd.random() < 0.06: p.px(x, y - 2 - rnd.randint(0, 1), '#c2cac4')
    return p.image()


def shack():
    p = Paint(320, 180)
    rnd = random.Random(501)
    HZS = 62
    dawn_sky(p, HZS, 513, glow_x=120)
    gulls(p, [(64, 28, 1), (82, 22, 0)])
    beach_sea(p, HZS, rnd)
    p.fill((p.Y == HZS), '#9aa7a3')
    # a far headland, faint, at the horizon on the left
    hd = (HZS - 1 - np.clip(9 - np.arange(320) * 0.12, 0, None) + line1d(4, 320, ((20, 0.6),))).astype(int)
    p.ramp((p.Y >= hd[None, :]) & (p.Y < HZS) & (p.X < 80), 1.0, ['#7d8a8c', '#8a9698'], soft=0.3)
    # the shore runs in from the left; dunes rise behind the shack on the right
    shore = (96 + line1d(41, 320, ((110, 2.5), (37, 1.2))) - np.clip(np.arange(320) - 180, 0, None) * 0.12).astype(int)
    land = p.Y >= shore[None, :]
    ground(p, 90, 180, DSAND, 43, 2.8, 4.2, patch=0.35, cell=34, soft=0.3, mask=land)
    wetb = shore + 5
    wet = land & (p.Y < wetb[None, :])
    p.ramp(wet, 1.0 + (p.Y - shore[None, :]) * 0.6, WSAND, soft=0.3)
    for x in range(320):
        p.px(x, shore[x], '#d4d8cd')
        if x % 5: p.px(x, shore[x] - 1, '#b2bdb8')
    # dunes: two humps of pale sand with marram grass on their crests
    dune = (84 + line1d(45, 320, ((90, 3.5), (31, 1.5))) + np.clip(190 - np.arange(320), 0, None) * 0.3).astype(int)
    dm = (p.Y >= dune[None, :]) & land
    gl = 2.8 + (p.Y - 90) / 90 * 1.4                                     # the ground's own value there
    dv = gl + 0.9 * np.clip(1 - (p.Y - dune[None, :]) / 14, 0, 1) + facing(dune, 21)[None, :] * 0.9
    dv = dv + (fbm(320, 180, 20, 47, 2) - 0.5) * 0.5
    p.ramp(dm & (p.Y < 126), dv, DSAND, soft=0.3)
    MARRAM = ['#4c5434', '#5e6640', '#737a4c', '#8a8e5a']
    for x in range(110, 320):
        if dune[x] >= shore[x] + 2 and rnd.random() < 0.55:
            hh = rnd.randint(2, 7)
            lean = rnd.choice((-1, 0, 1, 1))
            for j in range(hh):
                c = MARRAM[min(3, 1 + j * 3 // hh)] if x % 3 else MARRAM[0]
                p.px(x + (lean if j > hh // 2 else 0), dune[x] + 1 - j, c)
    # ---------------------------------------------------------------- the shack
    WOODS = DRIFT
    fx0, fx1, base, wt = 202, 264, 142, 106         # front wall
    gx1 = 284                                        # the gable end runs back to here (in shadow)
    peak = 86
    # shadow on the sand
    shadow(p, 250, base + 2, 52, 4, k=0.8)
    # gable end (right side, facing away from the light)
    gm = polymask(320, 180, [(fx1, base), (fx1, wt), (fx1 + 10, peak), (gx1, wt - 3), (gx1, base - 2)])
    boards_g = ((p.X - fx1) % 4 == 0)
    gv = 1.8 - boards_g * 0.7 + (vnoise(320, 180, 3, 7) - 0.5) * 1.0
    p.ramp(gm, gv, WOODS, soft=0.15)
    # the front wall: horizontal boards of driftwood, each its own shade, some gaps and patches
    front = (p.X >= fx0) & (p.X < fx1) & (p.Y >= wt) & (p.Y < base)
    bd = np.zeros((180, 320))
    gaps = np.zeros((180, 320), bool)
    y = wt
    while y < base:
        hh = rnd.choice((3, 3, 4, 4, 5))
        x = fx0
        while x < fx1:
            ww = rnd.randint(14, 34)
            tone = rnd.uniform(2.6, 4.4)
            bd[y:y + hh, x:x + ww] = tone
            grain = (np.sin(np.arange(x, x + ww)[None, :] * 0.7 + rnd.uniform(0, 6)) > 0.75)
            bd[y + 1:y + hh - 1, x:x + ww] -= grain * 0.6
            gaps[y + hh - 1, x:x + ww] = True
            if x > fx0: gaps[y:y + hh, x] = True
            x += ww
        y += hh
    bd = bd + (vnoise(320, 180, 4, 11) - 0.5) * 0.8
    p.ramp(front, bd, WOODS, soft=0.15)
    p.fill(front & gaps, WOODS[1])
    p.fill(front & shift(gaps, 1, 0) & ~gaps, WOODS[5])          # the lit top edge of each board
    # corner posts: rough logs
    for (px_, w_) in ((fx0 - 1, 4), (fx1 - 2, 4)):
        pm = (p.X >= px_) & (p.X < px_ + w_) & (p.Y >= wt - 3) & (p.Y < base)
        p.ramp(pm, 4.4 - (p.X - px_) * 1.1 - ((p.Y % 7) == 0) * 0.8, WOODS, soft=0.1)
    # the eaves' shadow on the wall
    p.darken(front & (p.Y >= wt) & (p.Y < wt + 3), 0.72)
    # the roof: old planks laid down the slope, patched with turf, weighted with stones on ropes
    rm = polymask(320, 180, [(fx0 - 5, wt + 1), (fx0 + 6, peak), (fx1 + 10, peak), (fx1 + 5, wt + 1)])
    rv = 3.4 - (p.Y - peak) / (wt - peak) * 1.2 - (((p.X + (p.Y - peak) // 2) % 6) == 0) * 0.9 + \
        (vnoise(320, 180, 5, 13) - 0.5) * 1.2
    p.ramp(rm, rv, WOODS, soft=0.15)
    turf = rm & (fbm(320, 180, 9, 17, 2) > 0.55) & (p.Y < wt - 2)
    p.ramp(turf, 1.4 + (fbm(320, 180, 4, 19, 1) > 0.5) * 1.2 - (p.Y > peak + 10) * 0.5, MARRAM, soft=0.2)
    for x in range(fx0 + 6, fx1 + 10):
        p.px(x, peak, WOODS[6] if x % 4 else WOODS[4])
        if (x * 5) % 7 == 0: p.px(x, peak - 1, MARRAM[2])
    for x in range(fx0 - 5, fx1 + 6): p.px(x, wt + 1, WOODS[0]); p.px(x, wt, WOODS[2] if x % 3 else WOODS[1])
    # the ropes over the roof, with stones hanging on them
    for rx in (fx0 + 12, fx0 + 34, fx1 - 6):
        rl = lines(320, 180, [[(rx + 6, peak), (rx, wt + 2)]])
        p.fill(rl, ROPE[1])
        p.stamp(rx - 2, wt + 2, ['.11.', '1231', '1221', '.11.'], ['#33363a', '#5e6064', '#888884'])
    for (sx, sy) in ((fx0 + 20, peak + 4), (fx0 + 46, peak + 7)):
        p.stamp(sx, sy, ['.111.', '12331', '12221', '.111.'], ['#33363a', '#55585b', '#7a7b78'])
    # the door, open on firelight inside
    dx0, dx1, dtop = 226, 238, 122
    frame = (p.X >= dx0 - 2) & (p.X < dx1 + 2) & (p.Y >= dtop - 2) & (p.Y < base)
    p.ramp(frame, 3.2 - (p.X >= dx1) * 1.8 - (p.Y >= base - 1) * 1.0, WOODS, soft=0.1)
    door = (p.X >= dx0) & (p.X < dx1) & (p.Y >= dtop) & (p.Y < base)
    p.ramp(door, 0.6 + np.clip(p.Y - dtop, 0, 20) * 0.04, ['#1a1416', '#231a17', '#2e2119'], soft=0.25, raw=True)
    # firelight inside: a warm glow low in the room, and the hearth's flicker at the back
    glow(p, door, 233, base - 1, ((5, 0.85), (9, 0.6), (14, 0.35), (20, 0.15)), '#c27a36', sx=1.1, sy=1.4)
    p.stamp(229, base - 5, ['..3..3', '.343.4', '13443.', '144431'], ['#7a3a1c', '#a8642c', '#d9a04a', '#f0d27a'],
            raw=True)
    # the edge of the open door, swung inwards, catching a little light
    dd = (p.X >= dx0) & (p.X < dx0 + 3) & (p.Y >= dtop) & (p.Y < base)
    p.ramp(dd, 1.4 + (p.X - dx0) * 0.5, ['#2c2420', '#4a3a2c', '#6a5038'], soft=0.1, raw=True)
    # lintel and threshold; light spilling out over the sand
    for x in range(dx0 - 3, dx1 + 3): p.px(x, dtop - 2, WOODS[5] if x < dx1 else WOODS[3]); p.px(x, dtop - 3, WOODS[1])
    for x in range(dx0, dx1): p.px(x, base, '#6a5038')
    spill = (p.Y > base) & (p.Y < base + 10) & (np.abs(p.X + 0.5 - 232) < 6 + (p.Y - base) * 0.9)
    glow(p, spill, 232, base, ((5, 0.32), (9, 0.2), (13, 0.1)), '#d9a04a', sx=1.6)
    # a small shuttered window, a little glow at the crack
    wx, wy = 246, 116
    wm = (p.X >= wx) & (p.X < wx + 10) & (p.Y >= wy) & (p.Y < wy + 8)
    p.ramp(wm, 2.4 + ((p.X - wx) % 3 == 0) * -0.9 + (p.X < wx + 5) * 0.6, WOODS, soft=0.1)
    for y in range(wy + 1, wy + 7): p.px(wx + 5, y, WARM[1] if y % 2 else WARM[2], raw=True)
    for x in range(wx - 1, wx + 11): p.px(x, wy + 8, WOODS[5]); p.px(x, wy + 9, WOODS[1])
    # the stone chimney against the gable end, with a smudge of smoke
    cx0, cx1, ctop = 274, 284, 74
    cm = polymask(320, 180, [(cx0, base), (cx0 + 1, ctop), (cx1 - 1, ctop), (cx1, base)])
    stone_courses(p, cm, ['#3a3836', '#4f4c48', '#65615b', '#7b766e', '#918b80'], rnd, 1.6)
    p.darken(cm & (p.X >= cx1 - 3), 0.8)
    for x in range(cx0 - 1, cx1 + 1): p.px(x, ctop - 1, '#918b80' if x < cx1 - 2 else '#65615b'); p.px(x, ctop, '#3a3836')
    for x in range(cx0 + 2, cx1 - 2): p.px(x, ctop - 2, '#2a2422')
    smoke = ['#6e7276', '#83878a', '#979a9a', '#a9abaa']
    sr = random.Random(7)
    for i in range(22):
        t = i / 21
        sx = 279 - t * 64 - math.sin(t * 4) * 5
        sy = ctop - 3 - t * 44
        rr = 2.2 + t * 5
        if t < 0.55:
            blob(p, sx, sy, rr, smoke[max(0, 2 - int(t * 4)):] if t < 0.3 else smoke[1:], sq=0.75, rough=(sr, 0.2))
        else:
            sm = np.hypot((p.X + 0.5 - sx) / rr, (p.Y + 0.5 - sy) / (rr * 0.6)) < 1
            p.dither(sm, smoke[2], (1 - t) * 1.2)
    # ---------------------------------------------------------------- the yard: nets, boat, pots
    # nets drying on two poles, the mesh sagging between them, cork floats along the head rope
    for x in (142, 186):
        for y in range(98, 133):
            p.px(x, y, WOODS[4] if y < 132 else WOODS[1]); p.px(x + 1, y, WOODS[2])
        p.px(x, 97, WOODS[5])
    shadow(p, 168, 133.5, 26, 1.6, k=0.85)
    for x in range(144, 186):
        u = (x - 143) / 43
        sag = 6 * math.sin(u * math.pi)
        topn = 100 + sag
        botn = 124 + sag * 0.5 + math.sin(x * 0.5) * 1.2
        for y in range(int(topn), int(botn)):
            if ((x + y) % 4 == 0) or ((x - y) % 4 == 0):
                p.px(x, y, '#5e5a4c' if (x + y) % 8 else '#76705e')
        p.px(x, int(topn), ROPE[2])
        if x % 6 == 2:
            p.px(x, int(topn) + 1, '#a8642c'); p.px(x + 1, int(topn) + 1, '#8e5428')
    # a fish trap leaning on the right pole
    p.stamp(188, 124, ['.111.', '12321', '13231', '12321', '.111.'], ['#3e3426', '#6a5232', '#86693e'])
    # the upturned rowing boat, lying on two stones
    bm = Prop(56, 18)
    b = bm.p
    hull = polymask(56, 18, bez([(1, 15), (6, 3), (28, -1), (50, 3), (55, 15)], 24))
    hull &= b.Y < 15
    strk = (b.Y - 1) // 3
    bv = 4.2 - b.Y * 0.18 - ((b.Y % 3) == 0) * 1.0 - np.clip(b.X - 34, 0, None) * 0.05 + \
        np.sin(strk * 2.1) * 0.25
    b.ramp(hull, bv, WRECK, soft=0.15)
    b.fill(hull & (b.Y >= 13), WRECK[1])
    # the keel along the top
    for x in range(8, 48): b.px(x, int(1.2 + abs(x - 28) ** 2 * 0.005), WRECK[6] if x < 30 else WRECK[4])
    bm.down(p, 62, 115, sh=(3, 2))
    for (sx, sy) in ((70, 129), (106, 129)):
        p.stamp(sx, sy, ['.111.', '12331', '12221'], ['#33363a', '#55585b', '#7a7b78'])
    p.fill((p.X >= 66) & (p.X < 116) & (p.Y == 130) & ((p.X % 2) == 0), '#4a4438')
    # an oar propped against the boat
    om = lines(320, 180, [[(118, 108), (130, 131)]], width=1)
    p.fill(om, WOODS[4]); p.fill(shift(om, 0, 1) & ~om, WOODS[1])
    p.stamp(129, 129, ['11', '21', '22'], [WOODS[2], WOODS[4]])
    # a lobster pot and a barrel by the right of the shack (outside the path)
    lp = Prop(16, 12)
    lm = (np.hypot((lp.p.X + 0.5 - 8) / 7, (lp.p.Y + 0.5 - 11) / 9) < 1) & (lp.p.Y < 11)
    lp.p.ramp(lm, 2.4 + ((lp.p.X + lp.p.Y) % 2) * 1.0 - (lp.p.X > 9) * 1.0 - (lp.p.Y > 8) * 0.6,
              ['#4f3d26', '#6a5232', '#86693e', '#a0824e', '#b59a62'], soft=0.15)
    lp.down(p, 288, 136)
    bb = Prop(14, 16)
    q = bb.p
    m = (np.abs(q.X + 0.5 - 7) < 6 - (np.abs(q.Y + 0.5 - 8) > 6)) & (q.Y >= 1) & (q.Y < 16)
    q.ramp(m, 4.0 - np.abs(q.X + 1.5 - 7) * 0.5 - (q.X > 7) * 0.8, WRECK, soft=0.15)
    for yy in (4, 11): q.fill(m & (q.Y == yy), '#3a3a36')
    q.fill(m & (q.Y == 1), WRECK[5])
    bb.down(p, 266, 127)
    # ---------------------------------------------------------------- the path and the foreground
    # a trodden path from the left towards the door: flatter, a touch darker, a few footprints
    path = (p.Y >= 134) & (p.Y < 147) & (p.X >= 24) & (p.X < 240)
    pv = np.clip(1 - np.abs(p.Y + 0.5 - 140.5) / 6.5, 0, 1)
    p.dither(path & (pv > 0.2), DSAND[2], pv * 0.55)
    for x in range(38, 222, 7):
        y = 139 + (2 if (x // 7) % 2 else 0)
        p.stamp(x, y, ['.1', '1.'] if (x // 7) % 2 else ['1.', '.1'], [DSAND[1]])
    # stones and grass well away from the path
    scatter(p, rnd, 30, 0, 320, 150, 178, PEBBLES, ['#6f6858', '#8c8370', '#b0a68c'])
    scatter(p, rnd, 14, 0, 200, 112, 130, PEBBLES, ['#6f6858', '#8c8370', '#b0a68c'], avoid=[(60, 120), (140, 190)])
    for (x0, x1, y_) in ((0, 30, 180), (286, 320, 180), (296, 320, 152), (190, 204, 133)):
        for x in range(x0, x1):
            if rnd.random() < 0.6:
                hh = rnd.randint(3, 11 if y_ == 180 else 6)
                bend = rnd.uniform(-0.3, 0.4)
                for j in range(hh):
                    xx = x + int(bend * j * j / max(1, hh))
                    c = MARRAM[0] if j < hh * 0.3 else MARRAM[1] if j < hh * 0.7 else MARRAM[2 + (x % 3 == 0)]
                    p.px(xx, y_ - 1 - j, c)
    weed(p, 20, 104, rnd, n=4, spread=8)
    weed(p, 110, 101, rnd, n=3, spread=6)
    return p.image()


# ============================================================================================== main

PICTURES = {
    'cs_storm_sky': storm_sky,
    'cs_storm_clouds': storm_clouds,
    'cs_waves_back': waves_back,
    'cs_waves_front': waves_front,
    'cs_ship': lambda: ship(False),
    'cs_ship_flash': lambda: ship(True),
    'cs_bolt': bolt,
    'cs_big_wave': big_wave,
    'cs_beach': lambda: beach()[0],
    'cs_surf': surf,
    'cs_shack': shack,
}


def main(out=OUT):
    os.makedirs(out, exist_ok=True)
    made = []
    for name, make in PICTURES.items():
        made.append(save(make(), name, out))
    print('painted %d cutscene pictures into %s' % (len(made), os.path.normpath(out)))
    return made


if __name__ == '__main__':
    main()
