"""A tiny 3D-ish renderer for the beasts in battle.

A beast is built from rounded solids (ellipsoids and tapered capsules) placed in pixel space, side on and facing
left: x right, y down, z towards you. Each pixel takes the nearest solid in front, which gives it a surface normal,
and that is lit from the upper left and stepped down into a short palette ramp with a little ordered dithering.
Then: a dark line where something in front overlaps something behind (so legs read against the body), the usual
near-black outline round the outside, and hand-placed details (eyes, teeth, tusks) on top.

Because the shapes hang off a few joints, moving the joints gives animation frames that stay consistent:
see beasts2.py for the wolf, boar and crab and their poses.
"""
import math
import numpy as np
from PIL import Image

from palette import PAL

BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], float) / 16.0 - 0.47
LIGHT = np.array([-0.5, -0.62, 0.62])
LIGHT /= np.linalg.norm(LIGHT)
RIM = np.array([0.75, -0.45, 0.0])
RIM /= np.linalg.norm(RIM)


def hexrgb(h):
    h = PAL.get(h, h)
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


class Mat:
    """A material: a ramp of colours from dark to light, plus how furry and how shiny it is.
    under: a second ramp used where the surface faces down (a pale belly), from that much downward lean."""
    def __init__(self, ramp, under=None, under_at=0.45, fur=0.10, shine=0.0, shift=0, line=None, top=None, top_at=-0.6):
        self.ramp = [hexrgb(c) for c in ramp]
        self.under = [hexrgb(c) for c in under] if under else None
        self.under_at = under_at
        self.top = [hexrgb(c) for c in top] if top else None
        self.top_at = top_at
        self.fur, self.shine, self.shift = fur, shine, shift
        self.line = hexrgb(line) if line else self.ramp[0]


class Ell:
    def __init__(self, x, y, z, a, b, c, mat, rot=0.0):
        self.x, self.y, self.z, self.a, self.b, self.c, self.mat, self.rot = x, y, z, a, b, c, mat, rot


class Cap:
    def __init__(self, p1, r1, p2, r2, mat):
        self.p1, self.r1, self.p2, self.r2, self.mat = p1, r1, p2, r2, mat


def _ell(e, X, Y):
    cs, sn = math.cos(e.rot), math.sin(e.rot)
    dx, dy = X - e.x, Y - e.y
    u = dx * cs + dy * sn
    v = -dx * sn + dy * cs
    q = (u / e.a) ** 2 + (v / e.b) ** 2
    inside = q <= 1
    h = np.sqrt(np.clip(1 - q, 0, 1))
    z = e.z + e.c * h
    nu, nv, nz = u / (e.a * e.a), v / (e.b * e.b), h / e.c
    nx = nu * cs - nv * sn
    ny = nu * sn + nv * cs
    return inside, z, nx, ny, nz


def _cap(c, X, Y):
    (x1, y1, z1), (x2, y2, z2) = c.p1, c.p2
    dx, dy = x2 - x1, y2 - y1
    L2 = dx * dx + dy * dy or 1e-6
    t = np.clip(((X - x1) * dx + (Y - y1) * dy) / L2, 0, 1)
    cx, cy = x1 + t * dx, y1 + t * dy
    r = c.r1 + (c.r2 - c.r1) * t
    ox, oy = X - cx, Y - cy
    d2 = ox * ox + oy * oy
    inside = d2 <= r * r
    h = np.sqrt(np.clip(r * r - d2, 0, None))
    z = z1 + (z2 - z1) * t + h
    rr = np.maximum(r, 1e-6)
    return inside, z, ox / rr, oy / rr, h / rr


def render(prims, W, H, ground=None, details=(), seed=1, outline='o', sc=1.0):
    """Draws the solids into a W x H RGBA image. details: (x, y, colour) pixels drawn on top, in front of everything.
    sc: draws the model that much bigger or smaller (W and H are the finished size; the model is in its own units)."""
    Y, X = np.mgrid[0:H, 0:W].astype(float)
    PX, PY = X.copy(), Y.copy()            # output pixels, for the dither and fur patterns
    X = (X + 0.5) / sc
    Y = (Y + 0.5) / sc
    zb = np.full((H, W), -1e9)
    nx = np.zeros((H, W)); ny = np.zeros((H, W)); nz = np.ones((H, W))
    mid = np.full((H, W), -1, int)
    mats = []
    for p in prims:
        if p.mat not in mats: mats.append(p.mat)
        mi = mats.index(p.mat)
        inside, z, a, b, c = p.field(X, Y) if hasattr(p, 'field') else _ell(p, X, Y) if isinstance(p, Ell) else _cap(p, X, Y)
        win = inside & (z > zb)
        zb[win] = z[win]; nx[win] = a[win]; ny[win] = b[win]; nz[win] = c[win]; mid[win] = mi
    filled = mid >= 0
    n = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-9
    nx, ny, nz = nx / n, ny / n, nz / n

    lam = np.clip(nx * LIGHT[0] + ny * LIGHT[1] + nz * LIGHT[2], 0, 1)
    rim = np.clip(nx * RIM[0] + ny * RIM[1] + nz * RIM[2], 0, 1) ** 3 * (1 - nz) * 0.9
    inten = 0.22 + 0.80 * lam + rim * 0.35
    if ground is not None:   # a touch darker near the ground (the light can't get in under the beast)
        inten *= 0.78 + 0.22 * np.clip((ground - Y) / 10.0, 0, 1)
    rng = np.random.default_rng(seed)
    fur = rng.random((H, W))
    fur = (fur + np.roll(fur, 1, 1) + np.roll(fur, -1, 1) + 0.6 * np.roll(fur, 1, 0)) / 3.6   # short streaks along the body
    dither = BAYER[PY.astype(int) % 4, PX.astype(int) % 4]

    out = np.zeros((H, W, 4), np.uint8)
    for mi, m in enumerate(mats):
        sel = mid == mi
        if not sel.any(): continue
        I = inten + (fur - 0.5) * m.fur * 2 + m.shine * np.clip(lam - 0.85, 0, 1) * 3
        for ramp, which in ((m.ramp, sel),):
            pass
        k = len(m.ramp)
        lvl = np.clip(np.round(I * (k - 1) + dither * 0.42) + m.shift, 0, k - 1).astype(int)
        cols = np.array(m.ramp)[lvl]
        if m.under is not None:
            ku = len(m.under)
            lu = np.clip(np.round(I * (ku - 1) + dither * 0.42) + m.shift, 0, ku - 1).astype(int)
            under = sel & (ny > m.under_at + dither * 0.3)
            cols[under] = np.array(m.under)[lu][under]
        if m.top is not None:
            kt = len(m.top)
            lt = np.clip(np.round(I * (kt - 1) + dither * 0.42) + m.shift, 0, kt - 1).astype(int)
            top = sel & (ny < m.top_at - dither * 0.3)
            cols[top] = np.array(m.top)[lt][top]
        out[sel, :3] = cols[sel]
        out[sel, 3] = 255

    # Where a nearer solid overlaps a farther one, the farther pixel at the seam goes dark: legs read against the body.
    zf = np.where(filled, zb, -1e9)
    seam = np.zeros((H, W), bool)
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        nb = np.roll(np.roll(zf, dy, 0), dx, 1)
        nbm = np.roll(np.roll(mid, dy, 0), dx, 1)
        seam |= filled & (nb - zf > 3.2) & (nbm >= 0)
    for mi, m in enumerate(mats):
        s = seam & (mid == mi)
        out[s, :3] = m.line

    for (x, y, c) in details:
        x, y = int(round(x * sc - 0.01)), int(round(y * sc - 0.01))
        if 0 <= x < W and 0 <= y < H:
            out[y, x, :3] = hexrgb(c)
            out[y, x, 3] = 255

    # outline round the outside
    a = out[:, :, 3] > 0
    ring = np.zeros_like(a)
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        ring |= np.roll(np.roll(a, dy, 0), dx, 1)
    ring &= ~a
    ring[0, :] = ring[-1, :] = False
    ring[:, 0] = ring[:, -1] = False
    out[ring, :3] = hexrgb(outline)
    out[ring, 3] = 255
    return Image.fromarray(out, 'RGBA')


def sheet(frames):
    w, h = frames[0].size
    s = Image.new('RGBA', (w * len(frames), h), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        s.paste(f, (i * w, 0))
    return s


# ---------------------------------------------------------------- small geometry helpers

def rot(p, pivot, ang):
    """Turns a point (x, y, z) round pivot (x, y) in the picture plane; positive turns clockwise on screen."""
    x, y, z = p
    cs, sn = math.cos(ang), math.sin(ang)
    dx, dy = x - pivot[0], y - pivot[1]
    return (pivot[0] + dx * cs - dy * sn, pivot[1] + dx * sn + dy * cs, z)


def add(p, d):
    return (p[0] + d[0], p[1] + d[1], p[2] + (d[2] if len(d) > 2 else 0))


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(len(a)))
