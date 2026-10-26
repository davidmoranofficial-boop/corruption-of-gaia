"""Light for the painted areas (25 Oct): the pass that makes a flat map look lit.

After an area is painted (ground, props, houses, tree tops on the overlay), this works out how tall everything is,
then lets a late-morning sun fall from the upper left:

  - cast shadows: trees, houses, walls, rocks and crates throw a soft, cool shadow down and to the right, longer
    for taller things, in a few crisp steps so it still reads as pixel art;
  - ambient occlusion: the ground darkens a little where it meets anything standing on it;
  - sun dapple: big, slow patches of brighter and duller light, as if through broken cloud;
  - a colour grade: shadows lean blue-violet, lit parts lean warm, greens and the sea get a little more life.

The same grade (`grade`) is used on the overlay so tree tops match the ground they shade.

    img, over = light.apply(img, over, rows)
"""
import numpy as np
from PIL import Image

T = 16

# how tall each solid tile letter stands, in pixels (trees come from the overlay)
HEIGHT = {'r': 6, 'R': 3, 'H': 34, 'Z': 24, 'I': 28, 'K': 11, 'n': 11, 'Q': 16, 'F': 7, 'k': 7, 'l': 20, 'O': 12,
          'j': 9, 'N': 13, 'S': 15, 'E': 7, 'Y': 8, 'e': 4, 'A': 6, 'L': 16, 'v': 3, 'h': 0}

SUN = (1.0, 0.62)       # the direction shadows fall, per pixel of distance (right and down)
LENGTH = 0.55           # how far a shadow reaches per pixel of height


def box(f, r):
    """Box blur, radius r, twice (soft)."""
    if r <= 0: return f
    for _ in range(2):
        for ax in (0, 1):
            pad = [(r + 1, r) if a == ax else (0, 0) for a in (0, 1)]
            c = np.cumsum(np.pad(f, pad, mode='edge'), axis=ax)
            f = (c[2 * r + 1:, :] - c[:-2 * r - 1, :]) if ax == 0 else (c[:, 2 * r + 1:] - c[:, :-2 * r - 1])
            f = f / (2 * r + 1)
    return f


def vnoise(h, w, cell, seed):
    rnd = np.random.RandomState(seed)
    g = rnd.rand(h // cell + 3, w // cell + 3)
    ys, xs = np.arange(h) / cell, np.arange(w) / cell
    y0, x0 = ys.astype(int), xs.astype(int)
    fy, fx = ys - y0, xs - x0
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    a, b, c, d = g[y0][:, x0], g[y0][:, x0 + 1], g[y0 + 1][:, x0], g[y0 + 1][:, x0 + 1]
    top, bot = a + (b - a) * fx[None, :], c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]


def lum(c):
    return c[..., 0] * 0.299 + c[..., 1] * 0.587 + c[..., 2] * 0.114


def grade(c):
    """The colour grade, on floats 0..1 (h, w, 3): cool shadows, warm light, a touch more life in greens and water."""
    L = lum(c)[..., None]
    # split toning: dark parts toward blue-violet, light parts toward warm straw
    shadow_tint = np.array([0.86, 0.90, 1.10])
    light_tint = np.array([1.07, 1.03, 0.90])
    w = np.clip((L - 0.18) / 0.5, 0, 1)
    c = c * (shadow_tint * (1 - w) + light_tint * w)
    # saturation: more for greens and blues, a little for the rest
    L = lum(c)[..., None]
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    green = np.clip((g - np.maximum(r, b)) * 6, 0, 1)[..., None]
    blue = np.clip((b - r) * 4, 0, 1)[..., None]
    sat = 1.12 + 0.18 * green + 0.14 * blue
    c = L + (c - L) * sat
    # a gentle S-curve for contrast
    c = np.clip(c, 0, 1)
    c = c + 0.10 * (c - 0.5) * (1 - np.abs(2 * c - 1))
    return np.clip(c, 0, 1)


def heights(rows, over_alpha):
    H, W = len(rows) * T, len(rows[0]) * T
    h = np.zeros((H, W))
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            v = HEIGHT.get(ch, 0)
            if v: h[y * T:(y + 1) * T, x * T:(x + 1) * T] = v
    canopy = over_alpha > 0.5
    h[canopy] = np.maximum(h[canopy], 22)
    return h


def cast(h):
    """1 where a pixel is in something's shadow, softened at the edges."""
    H, W = h.shape
    sh = np.zeros((H, W))
    hmax = int(h.max())
    if hmax <= 0: return sh
    for s in range(1, int(hmax * LENGTH) + 2):
        sx, sy = int(round(s * SUN[0])), int(round(s * SUN[1]))
        if sx >= W or sy >= H: break
        src = np.zeros((H, W))
        src[sy:, sx:] = h[:H - sy, :W - sx]
        sh = np.maximum(sh, (src * LENGTH > s).astype(float))
    return sh


def apply(img, over, rows, seed=7, sea_letters='~'):
    """Lights a painted area. img and over are RGBA PIL images; returns new ones."""
    g = np.asarray(img.convert('RGB')).astype(float) / 255
    o = np.asarray(over.convert('RGBA')).astype(float) / 255
    H, W = g.shape[:2]
    h = heights(rows, o[..., 3])

    # where things stand (their own pixels aren't shadowed by themselves)
    standing = h > 0
    sh = cast(h)
    sh = box(sh, 1)
    sh[standing & (o[..., 3] < 0.5)] *= 0.25
    # crisp steps: a light edge and a full core
    sh = np.where(sh > 0.7, 1.0, np.where(sh > 0.3, 0.6, np.where(sh > 0.08, 0.3, 0.0)))

    # ambient occlusion around anything standing
    ao = box(standing.astype(float), 2)
    ao = np.clip(ao * 1.6, 0, 1) * (~standing)
    ao = np.where(ao > 0.5, 0.5, np.where(ao > 0.15, 0.25, 0.0))

    # sea: no cast shadows on water (it reads as reflections), but deeper further out
    sea = np.zeros((H, W), bool)
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch in sea_letters: sea[y * T:(y + 1) * T, x * T:(x + 1) * T] = True
    shore = box((~sea).astype(float), 10)
    deep = np.clip(1 - shore * 3, 0, 1) * sea

    # sun dapple: slow patches of light
    dap = vnoise(H, W, 64, seed) - 0.5
    dap = np.round(dap * 4) / 4      # in steps, not smooth

    light = 1.0 - 0.30 * sh * (~sea) - 0.16 * ao + 0.07 * dap * (~sea) - 0.22 * deep
    shade_tint = np.array([0.80, 0.86, 1.08])        # shadow is cool: blue sky light filling it
    tint = 1 + (shade_tint - 1) * (sh * (~sea))[..., None]
    out = g * light[..., None] * tint
    out = grade(out)

    # the overlay: same grade, and its own lower edge darker where the canopy overhangs
    oc = grade(o[..., :3] * 0.98)
    ov = np.concatenate([oc, o[..., 3:4]], axis=2)

    img2 = Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGB').convert('RGBA')
    over2 = Image.fromarray((np.clip(ov, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA')
    return img2, over2
