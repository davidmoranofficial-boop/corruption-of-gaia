"""Full-length talking portraits for the dialogue screen (David, 25 Oct: "bigger and in more detail").

Whoever you talk to stands on the left of the dialogue screen, head to boots, turned a little towards you and the
words on the right. Each person is built from rounded solids with rig.py (the same shading as the battle beasts),
then the small things are painted on top pixel by pixel: the face (one per mood), belts, buttons, seams, laces.

One picture per person per mood:  Sprites/Portrait/<npc>_<mood>.png   (moods: calm happy angry sad worried surprised)
The people (who wears what) are in PEOPLE below; their colours come from walk.py so they match the walking sprites.

    python3 Tools/Art/portrait.py      (also run by build.py)
"""
import math, os
from PIL import Image

from rig import Mat, Ell, Cap, render
import walk
from walk import (mix, SLATE, ROPE, SALTBOOT, LEATHER, DARKLEATHER, IRON, LINEN, TEAL, MOSS, PLUM, BROWNCLOTH,
                  CHARCOAL, WHITE, OLDBLUE, KINGSRED, SKIN3, HAIR3)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'Assets', 'Game', 'Resources', 'Sprites', 'Portrait')

W, H = 80, 144          # the picture; the game blows it up by a whole number
GROUND = 139            # where the boots stand
MOODS = ['calm', 'happy', 'angry', 'sad', 'worried', 'surprised']
OUTLINE = '#120f14'
GOLD = ['#c9a44f', '#8e6e30', '#5a4634']


def ramp5(r3, deep=0.35):
    """A walk.py ramp (lit, mid, shade) as a five-step rig ramp, dark to light."""
    lit, mid, shade = r3
    return [mix(shade, '#0c0a10', deep), shade, mid, lit, mix(lit, '#fff6e8', 0.22)]


def cloth(r3, fur=0.05):
    return Mat(ramp5(r3), fur=fur, line=mix(r3[2], '#0c0a10', 0.5))


def skin_mat(skin):
    s = SKIN3[skin]
    return Mat(ramp5(s, 0.25), fur=0.0, line=mix(s[2], '#0c0a10', 0.2))


def hair_mat(colour):
    h = HAIR3[colour]
    return Mat(ramp5(h, 0.4), fur=0.22, line=mix(h[2], '#0c0a10', 0.5))


# ------------------------------------------------------------------------------------------------- the people
# fig, skin, hair style, hair colour, beard,
# outfit: top (colour), sleeves ('long' | 'short' | 'rolled'), sleeve colour, legs, skirt (None | 'knee' | 'ankle'),
#         boots colour, tall boots, apron colour, belt colour, buckle colour, vest colour (a jerkin over the top),
# extras: things worn or carried (hats, quiver, bow, axe, buckler, knife)

def outfit(top, legs, boots=DARKLEATHER, sleeves='long', sleeve=None, skirt=None, tall=False, apron=None,
           belt=DARKLEATHER, buckle=IRON, vest=None, collar=None):
    return dict(top=top, legs=legs, boots=boots, sleeves=sleeves, sleeve=sleeve or top, skirt=skirt, tall=tall,
                apron=apron, belt=belt, buckle=buckle, vest=vest, collar=collar)


PEOPLE = {
    'yenson': ('male', 'olive', 'short', 'grey', 'beard', outfit(TEAL, SLATE, belt=SLATE, buckle=SLATE), ['sou_wester']),
    'fisher': ('male', 'fair', 'short', 'red', None, outfit(BROWNCLOTH, OLDBLUE, boots=SALTBOOT, belt=ROPE, buckle=ROPE, sleeves='rolled'), ['flat_cap']),
    'garran': ('male', 'brown', 'cropped', 'black', 'beard', outfit(CHARCOAL, CHARCOAL, sleeves='short', apron=LEATHER), []),
    'ida': ('female', 'light', 'ponytail', 'red', None, outfit(MOSS, BROWNCLOTH, tall=True, vest=DARKLEATHER), ['quiver', 'knife']),
    'bess': ('female', 'fair', 'bun', 'grey', None, outfit(PLUM, PLUM, skirt='ankle', apron=WHITE, sleeves='rolled', belt=PLUM, buckle=PLUM), []),
    'orlen': ('male', 'fair', 'bald', 'grey', 'long_beard', outfit(OLDBLUE, OLDBLUE, skirt='ankle', belt=GOLD, buckle=GOLD), []),
    'mira': ('female', 'olive', 'long', 'black', None, outfit(WHITE, SLATE, skirt='ankle', belt=SLATE, buckle=SLATE), []),
    'rook': ('male', 'light', 'messy', 'red', None, outfit(MOSS, BROWNCLOTH, tall=True, sleeves='rolled', vest=LEATHER), ['hunting_bow', 'quiver']),
    'wren': ('female', 'fair', 'braid', 'light brown', None, outfit(LINEN, MOSS, skirt='ankle', sleeves='rolled', vest=MOSS), []),
    'hesta': ('female', 'brown', 'bun', 'black', None, outfit(BROWNCLOTH, CHARCOAL, skirt='ankle', apron=LEATHER, sleeves='rolled', belt=CHARCOAL, buckle=CHARCOAL), []),
    'col': ('male', 'brown', 'messy', 'black', None, outfit(LINEN, CHARCOAL, boots=SALTBOOT, belt=ROPE, buckle=ROPE, sleeves='rolled'), []),
    'tobin': ('male', 'fair', 'short', 'light brown', 'beard', outfit(WHITE, BROWNCLOTH, sleeves='rolled'), []),
    'ness': ('female', 'fair', 'bun', 'blond', None, outfit(TEAL, TEAL, skirt='ankle', apron=WHITE, belt=TEAL, buckle=TEAL), []),
    'deserter': ('male', 'olive', 'cropped', 'dark brown', 'stubble', outfit(KINGSRED, SLATE, vest=LEATHER, sleeve=CHARCOAL), ['iron_cap', 'wood_axe']),
    'toll': ('male', 'light', 'short', 'dark brown', 'moustache', outfit(KINGSRED, CHARCOAL, tall=True, sleeve=CHARCOAL), ['iron_cap', 'buckler']),
    'vane': ('male', 'olive', 'cropped', 'grey', 'beard', outfit(KINGSRED, CHARCOAL, tall=True, buckle=GOLD, collar=GOLD), ['knife']),
    'sorrel': ('male', 'fair', 'swept', 'blond', 'stubble', outfit(LINEN, SLATE, boots=SALTBOOT, belt=ROPE, buckle=ROPE, vest=KINGSRED, sleeves='rolled'), []),
    'marda': ('female', 'olive', 'bun', 'grey', None, outfit(BROWNCLOTH, OLDBLUE, skirt='ankle', belt=OLDBLUE, buckle=OLDBLUE), []),
    'fenn': ('male', 'dark', 'cropped', 'black', 'beard', outfit(TEAL, BROWNCLOTH, sleeves='rolled'), ['flat_cap']),
}


# ------------------------------------------------------------------------------------------------- the figure

class Tube:
    """An upright tube with an oval cross-section that widens or narrows from top to bottom: a skirt, an apron, the body.
    folds: how many soft vertical folds shade it (strongest at the bottom); wave: how much the hem rises and falls."""
    def __init__(self, top, bottom, a0, a1, c0, c1, mat, folds=0, fold_amp=0.0, wave=0.0, phase=0.0):
        self.top, self.bottom, self.a0, self.a1, self.c0, self.c1 = top, bottom, a0, a1, c0, c1
        self.mat, self.folds, self.fold_amp, self.wave, self.phase = mat, folds, fold_amp, wave, phase

    def field(self, X, Y):
        import numpy as np
        (x0, y0, z0), (x1, y1, z1) = self.top, self.bottom
        t = (Y - y0) / max(1e-6, (y1 - y0))
        a = self.a0 + (self.a1 - self.a0) * t
        c = self.c0 + (self.c1 - self.c0) * t
        cx = x0 + (x1 - x0) * t
        zc = z0 + (z1 - z0) * t
        u = (X - cx) / np.maximum(a, 1e-6)
        hem = y1 - self.wave * (0.5 + 0.5 * np.cos(u * self.folds * math.pi + self.phase))
        inside = (Y >= y0) & (Y <= hem) & (np.abs(u) <= 1)
        h = np.sqrt(np.clip(1 - u * u, 0, 1))
        z = zc + c * h
        slope = (self.a1 - self.a0) / max(1e-6, (y1 - y0))
        nx = u / np.maximum(a, 1e-6)
        if self.folds:
            nx = nx + self.fold_amp * np.sin(u * self.folds * math.pi + self.phase) * np.clip(t, 0, 1) ** 0.7 / np.maximum(a, 1e-6)
        ny = slope * h / np.maximum(a, 1e-6) * 0.8
        nz = h / np.maximum(c, 1e-6)
        return inside, z, nx, ny, nz


class Fig:
    """Where the body's joints are, for one build. x right, y down, z towards you; the person faces a little right."""
    def __init__(self, fig):
        f = fig == 'female'
        self.female = f
        self.cx = 39
        self.head = (self.cx + 1.5, 22)
        self.head_r = (8.8, 10.6, 8.6) if f else (9.2, 11.0, 9.0)
        self.neck = (self.cx + 0.8, 35)
        self.sh_y = 42 if f else 41
        self.sh_w = 10.0 if f else 12.0          # shoulder half-width
        self.chest_y = 51
        self.waist_y = 69 if f else 71
        self.hip_y = 80
        self.hip_w = 8.4 if f else 7.4           # where the legs start, from the middle
        self.knee_y = 106
        self.ankle_y = 132
        self.limb = 1.0 if not f else 0.88


def person(name, mood):
    fig, skin, style, hc, beard, o, extras = PEOPLE[name]
    F = Fig(fig)
    sk = skin_mat(skin)
    top = cloth(o['top'])
    sleeve = cloth(o['sleeve'])
    legs = cloth(o['legs'])
    boots = Mat(ramp5(o['boots'], 0.4), fur=0.0, shine=0.15, line=mix(o['boots'][2], '#0c0a10', 0.5))
    hair = hair_mat(hc)
    vest = cloth(o['vest'], 0.07) if o['vest'] else None
    P = []
    cx, k = F.cx, F.limb
    waist_a = F.sh_w - (3.6 if F.female else 2.4)
    hip_a = F.hip_w + 5.0

    # legs: the near one (left on screen) carries the weight, the far one a touch out and back
    for side, dx, z, kneedx, footdx in ((-1, -F.hip_w * 0.55, 3, -0.5, -1.0), (1, F.hip_w * 0.55, -3, 0.8, 2.5)):
        hip = (cx + dx, F.hip_y, z)
        knee = (cx + dx + kneedx, F.knee_y, z + 1)
        ankle = (cx + dx + footdx * 0.6, F.ankle_y, z)
        P.append(Cap(hip, 6.0 * k + 0.8, knee, 4.4 * k + 0.6, legs))
        P.append(Cap(knee, 4.4 * k + 0.6, ankle, 3.4 * k + 0.4, legs))
        if o['tall']:
            P.append(Tube((knee[0], knee[1] + 3, z + 0.5), (ankle[0], ankle[1] + 1, z + 0.3), 5.0 * k + 0.6, 4.2 * k + 0.4,
                          4.6, 4.0, boots))
        else:
            P.append(Tube((ankle[0], ankle[1] - 6, z + 0.3), (ankle[0], ankle[1] + 1, z + 0.3), 4.3 * k + 0.4, 4.4 * k + 0.4,
                          4.0, 4.0, boots))
        # the foot points a little right, towards the one you talk to
        P.append(Ell(ankle[0] + 2.4, GROUND - 3.4, z + 2, 5.4, 3.4, 4.6, boots))

    # the body: shoulders to waist narrowing, waist to hips widening
    body_mat = vest or top
    P.append(Tube((cx + 0.4, F.sh_y, 0), (cx, F.waist_y, 0), F.sh_w + 0.5, waist_a, 7.6, 6.6, top))
    P.append(Ell(cx + 0.2, F.sh_y + 1.5, 0, F.sh_w + 0.5, 4.5, 7.6, top))                    # round over the shoulders
    if vest:
        P.append(Tube((cx + 0.4, F.sh_y + 2, 0.9), (cx, F.waist_y + 2, 0.9), F.sh_w - 0.6, waist_a + 0.6, 7.6, 6.9, vest))
        P.append(Ell(cx + 0.2, F.sh_y + 3, 0.9, F.sh_w - 1.0, 4.0, 7.4, vest))
    P.append(Tube((cx, F.waist_y - 1, 0), (cx, F.hip_y + 6, 0), waist_a + 0.4, hip_a, 6.8, 7.4, legs))
    if o['skirt']:
        sk_m = cloth(o['legs'], 0.03)
        bottom = F.ankle_y - 1 if o['skirt'] == 'ankle' else F.knee_y + 4
        P.append(Tube((cx, F.waist_y, 0.5), (cx + 1.0, bottom, 1.5), waist_a + 1.0, hip_a + 6.5, 7.2, 10.5, sk_m,
                      folds=5, fold_amp=1.6, wave=1.6, phase=0.8))

    # neck and head
    P.append(Cap((F.neck[0], F.neck[1] + 6, -0.5), 3.8, (F.neck[0] + 0.4, F.neck[1] - 3, 0.5), 3.5, sk))
    hx, hy = F.head
    ha, hb, hcz = F.head_r
    P.append(Ell(hx, hy, 0, ha, hb, hcz, sk))                                            # the skull
    P.append(Ell(hx + 1.6, hy + 5.0, 1.5, ha - 2.0, 5.8, hcz - 1.0, sk))                 # jaw and chin
    P.append(Ell(hx - ha + 1.6, hy + 1.5, -1.0, 1.8, 2.8, 1.8, sk))                      # the near ear

    # arms hang at the sides, a little bent; the far one half hidden behind the body
    sleeve_len = {'long': 1.0, 'rolled': 0.6, 'short': 0.0}[o['sleeves']]
    for side, z, swing in ((-1, 2.0, -0.05), (1, -3.5, 0.06)):
        sh = (cx + side * (F.sh_w - 1.8), F.sh_y + 3, z)
        el = (sh[0] + side * 2.2 + swing * 20, F.waist_y - 2, z + 0.5)
        wr = (el[0] + side * 0.4 + swing * 12, F.hip_y + 4, z + 2.5)
        if o['sleeves'] == 'short':
            P.append(Cap(sh, 3.8 * k + 0.4, el, 3.0 * k + 0.4, sk))
            P.append(Cap(sh, 4.3 * k + 0.5, (sh[0] + side * 1.4, sh[1] + 9, z + 0.2), 3.8 * k + 0.5, top))
        else:
            P.append(Cap(sh, 4.2 * k + 0.4, el, 3.4 * k + 0.4, sleeve))
        P.append(Cap(el, 3.0 * k + 0.4, wr, 2.4 * k + 0.3, sk))
        if sleeve_len > 0:
            fe = (el[0] + (wr[0] - el[0]) * sleeve_len, el[1] + (wr[1] - el[1]) * sleeve_len, el[2] + (wr[2] - el[2]) * sleeve_len)
            P.append(Cap(el, 3.5 * k + 0.4, fe, (2.9 if sleeve_len >= 1 else 3.4) * k + 0.4, sleeve))
        # the hand: palm and the fingers curled under
        P.append(Ell(wr[0] + side * 0.2, wr[1] + 3.2, z + 3, 2.6 * k + 0.3, 3.4, 2.4, sk))
        P.append(Ell(wr[0] + side * 0.4 + 0.6, wr[1] + 6.2, z + 3.4, 2.2 * k + 0.2, 2.2, 2.0, sk))

    if o['apron']:
        ap = cloth(o['apron'], 0.02)
        bottom = F.knee_y + 8
        P.append(Tube((cx + 1.0, F.waist_y - 1, 6.0), (cx + 1.6, bottom, 9.5), waist_a - 1.2, waist_a + 2.4, 2.6, 2.6, ap,
                      folds=3, fold_amp=0.9, wave=0.8))
        if F.female or name == 'garran':   # a bib up the chest
            P.append(Tube((cx + 1.0, F.chest_y - 5, 7.0), (cx + 1.0, F.waist_y, 6.6), F.sh_w - 5.0, waist_a - 1.2, 2.0, 2.0, ap))

    # hair (built round the skull so the face stays clear)
    P += hair_solids(style, F, hair)
    if beard:
        P += beard_solids(beard, F, hair)
    for x in extras:
        P += extra_solids(x, F, o)

    img = render(P, W, H, ground=GROUND, outline=OUTLINE, seed=sum(map(ord, name))).copy()
    px = img.load()
    paint_face(px, F, skin, hc, beard, mood, fig)
    paint_details(px, F, o, extras, img)
    return img


# ------------------------------------------------------------------------------------------------- hair

def hair_solids(style, F, mat):
    hx, hy = F.head
    ha, hb, hc = F.head_r
    P = []
    if style == 'bald':
        return [Ell(hx - 2.0, hy + 1.5, -1.0, ha * 0.92, 4.5, hc * 0.9, mat)]   # a fringe round the back
    # a cap of hair over the top and back of the skull; the front edge sits above the brow
    cap = {'cropped': 0.6, 'short': 1.0, 'swept': 1.3, 'messy': 1.6}.get(style, 1.2)
    P.append(Ell(hx - 1.2, hy - 2.6, -0.6, ha + cap, hb * 0.78 + cap * 0.5, hc + cap * 0.6, mat))
    P.append(Ell(hx - 3.4, hy + 0.8, -1.5, ha * 0.78 + cap * 0.6, hb * 0.82, hc * 0.85, mat))       # the back of the head
    if style == 'messy':
        for (dx, dy, r) in ((3.0, -8.6, 2.6), (-1.0, -10.0, 2.8), (-5.5, -8.0, 2.6), (5.6, -6.4, 2.2), (-8.4, -3.2, 2.4)):
            P.append(Ell(hx + dx, hy + dy, 1.0, r, r * 0.8, r, mat))
        P.append(Ell(hx + 3.8, hy - 6.0, 4.0, 3.4, 2.0, 2.4, mat))   # a lock over the brow
    if style == 'swept':
        P.append(Ell(hx + 2.0, hy - 8.0, 2.5, 6.0, 3.2, 4.0, mat, rot=-0.25))
    if style in ('long', 'braid', 'ponytail', 'bun'):
        P.append(Ell(hx + 3.0, hy - 6.4, 3.0, 4.6, 2.4, 3.2, mat, rot=-0.2))   # hair parted over the brow
    if style == 'long':
        P.append(Cap((hx - 4.5, hy - 2, -2.0), 6.8, (hx - 5.5, hy + 22, -3.5), 5.4, mat))
        P.append(Cap((hx + 4.5, hy + 2, -3.0), 3.0, (hx + 5.0, hy + 18, -4.0), 2.6, mat))
    if style == 'ponytail':
        P.append(Ell(hx - 7.5, hy - 3.0, -2.0, 2.4, 2.0, 2.2, mat))
        P.append(Cap((hx - 9.0, hy - 1.5, -2.5), 2.9, (hx - 11.0, hy + 16, -3.5), 1.6, mat))
    if style == 'braid':
        for i in range(6):
            P.append(Ell(hx - 6.8 - i * 0.35, hy + 6 + i * 3.6, 3.0 - i * 0.2, 2.6 - i * 0.12, 2.1, 2.0, mat))
    if style == 'bun':
        P.append(Ell(hx - 4.0, hy - 9.5, -1.5, 4.4, 3.8, 4.0, mat))
    return P


def beard_solids(beard, F, mat):
    hx, hy = F.head
    if beard in ('beard', 'long_beard'):
        P = [Ell(hx + 1.6, hy + 6.4, 2.5, 6.4, 5.6, 6.0, mat)]
        if beard == 'long_beard':
            P.append(Cap((hx + 2.0, hy + 9, 3.0), 5.0, (hx + 2.4, hy + 21, 4.0), 2.6, mat))
        return P
    return []   # stubble and moustaches are painted on


# ------------------------------------------------------------------------------------------------- things worn and carried

def extra_solids(x, F, o):
    hx, hy = F.head
    ha, hb, hc = F.head_r
    cx = F.cx
    if x == 'sou_wester':
        m = cloth(['#d9b24a', '#b08a2e', '#7a5c1c'], 0.02)
        return [Ell(hx - 0.6, hy - 5.4, 0.4, ha + 1.6, 6.4, hc + 1.6, m), Ell(hx - 0.6, hy - 2.4, 0.0, ha + 5.0, 1.7, hc + 4.0, m),
                Ell(hx - 4.0, hy + 2.5, -2.0, 5.5, 3.0, 4.0, m)]
    if x == 'flat_cap':
        m = cloth(['#8a8480', '#625d5a', '#3e3a3c'], 0.1)
        return [Ell(hx - 0.6, hy - 6.2, 0.4, ha + 1.2, 4.6, hc + 1.4, m), Ell(hx + 5.6, hy - 4.4, 2.4, 4.6, 1.3, 4.6, m)]
    if x == 'iron_cap':
        m = Mat(ramp5(IRON, 0.4), fur=0.0, shine=0.6, line='#2a2628')
        return [Ell(hx - 0.6, hy - 5.0, 0.4, ha + 1.3, 6.8, hc + 1.3, m), Ell(hx - 0.6, hy - 1.2, 0.2, ha + 2.0, 1.4, hc + 2.0, m)]
    if x == 'quiver':
        m = cloth(LEATHER, 0.02)
        return [Cap((cx - 9, F.sh_y - 2, -7.0), 3.2, (cx - 1, F.waist_y + 6, -7.0), 2.8, m)]
    if x == 'hunting_bow':
        # slung across the back, from over the near shoulder down past the far hip
        wood = Mat(ramp5(['#a07a4c', '#7a5838', '#55392a'], 0.4), fur=0.0, shine=0.2, line='#2a1c14')
        string = Mat(ramp5(['#d2c3a2', '#a89474', '#7d6b52'], 0.3), fur=0.0, line='#5a4634')
        P = []
        n = 16
        p0, p1 = (cx - 15, 30), (cx + 19, 96)
        dx, dy = p1[0] - p0[0], p1[1] - p0[1]
        L = math.hypot(dx, dy)
        nx, ny = -dy / L, dx / L
        def pt(t):
            bend = 7.0 * math.sin(t * math.pi)
            return (p0[0] + dx * t + nx * bend, p0[1] + dy * t + ny * bend, -7.0)
        for i in range(n):
            P.append(Cap(pt(i / n), 1.5, pt((i + 1) / n), 1.5, wood))
        P.append(Cap((p0[0], p0[1], -7.5), 0.5, (p1[0], p1[1], -7.5), 0.5, string))
        return P
    if x == 'wood_axe':
        haft = Mat(ramp5(['#a07a4c', '#7a5838', '#55392a'], 0.4), fur=0.0, line='#2a1c14')
        head = Mat(ramp5(IRON, 0.4), fur=0.0, shine=0.6, line='#2a2628')
        hx2, hy2 = cx + F.sh_w + 3.5, F.hip_y + 6     # in the far hand, head down by the boot
        return [Cap((hx2, hy2 - 6, 5), 1.5, (hx2 + 1, hy2 + 34, 5), 1.5, haft),
                Ell(hx2 + 4.5, hy2 + 31, 5.5, 5.5, 4.2, 1.5, head),
                Ell(hx2 + 8.5, hy2 + 31, 5.5, 2.0, 6.0, 1.3, head)]
    if x == 'buckler':
        m = Mat(ramp5(IRON, 0.4), fur=0.0, shine=0.5, line='#2a2628')
        rim = Mat(ramp5(DARKLEATHER, 0.4), fur=0.0, line='#2a1c14')
        bx, by = cx - F.sh_w - 4, F.hip_y + 2
        return [Ell(bx, by, 6.0, 9.5, 9.5, 2.0, rim), Ell(bx, by, 7.5, 8.0, 8.0, 1.6, m), Ell(bx, by, 9.0, 2.4, 2.4, 1.2, m)]
    if x == 'knife':
        m = cloth(DARKLEATHER, 0.0)
        return [Cap((cx + 5, F.waist_y + 7, 7.0), 1.4, (cx + 6.5, F.waist_y + 15, 7.0), 1.2, m)]
    return []


# ------------------------------------------------------------------------------------------------- painted detail

def put(px, x, y, c):
    if 0 <= x < W and 0 <= y < H:
        h = c.lstrip('#')
        px[x, y] = (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)


def get(px, x, y):
    return px[x, y] if 0 <= x < W and 0 <= y < H else (0, 0, 0, 0)


EYES = {'fair': '#4a6a7a', 'light': '#5a4a32', 'olive': '#3e3428', 'brown': '#2e2218', 'dark': '#221812'}


def paint_face(px, F, skin, hc, beard, mood, fig):
    """Eyes, brows, nose and mouth, painted on: the face changes with the mood of what's being said.
    The face is turned a little to the right, so the near eye (left) is wider and the far one sits close to the nose."""
    hx, hy = int(round(F.head[0])), int(round(F.head[1]))
    s = SKIN3[skin]
    shade = mix(s[2], '#0c0a10', 0.15)
    deep = mix(s[2], '#0c0a10', 0.5)
    lash = '#1e1a20'
    brow = mix(HAIR3[hc][2], '#0c0a10', 0.25)
    iris = EYES.get(skin, '#3e3428')
    white = '#e8e2d6'
    lip = mix(s[1], '#9a4a3a', 0.35)
    ey = hy + 1
    wide = mood == 'surprised'
    eyes = [(hx - 4, 4), (hx + 2, 3)]          # (left x, width)
    for i, (x0, w) in enumerate(eyes):
        # the socket's shadow, the lash line, then the white with the iris looking right
        for dx in range(w): put(px, x0 + dx, ey - 2, shade)
        for dx in range(-1 if i == 0 else 0, w + (1 if i == 1 else 0)): put(px, x0 + dx, ey - 1, lash)
        for dx in range(w): put(px, x0 + dx, ey, white)
        if wide:
            for dx in range(w): put(px, x0 + dx, ey + 1, white)
        ix = x0 + w - 2
        put(px, ix, ey, iris); put(px, ix + 1, ey, lash if not wide else iris)
        if wide: put(px, ix, ey + 1, iris); put(px, ix + 1, ey + 1, iris)
        put(px, ix, ey, mix(iris, '#ffffff', 0.25))
        if mood in ('sad', 'calm', 'angry'):           # heavy lids
            for dx in range(w): put(px, x0 + dx, ey - 1, lash)
        if mood == 'happy':                              # smiling eyes: the lower lid lifts
            for dx in range(1, w - 1): put(px, x0 + dx, ey + 1, shade)
        # the brow
        by = ey - 3 - (1 if wide else 0)
        n = w + 2
        for dx in range(n):
            x = x0 - 1 + dx
            t = dx / (n - 1)
            inner = t if i == 0 else 1 - t           # 1 at the end nearest the nose
            off = 0
            if mood == 'angry': off = 1 if inner > 0.55 else 0
            elif mood in ('worried', 'sad'): off = -1 if inner > 0.55 else 0
            elif mood == 'surprised': off = -1 if 0.2 < t < 0.8 else 0
            put(px, x, by + off, brow)
    if mood == 'angry':
        put(px, hx, ey - 2, deep)                       # a crease between the brows
    # the nose: the light comes from the left, so its right side is in shadow
    for dy in range(0, 4): put(px, hx + 5, ey + dy, shade)
    put(px, hx + 4, ey + 4, deep); put(px, hx + 5, ey + 4, shade)
    put(px, hx + 3, ey + 4, shade)
    # the mouth
    my = ey + 7
    mx = hx
    if mood == 'happy':
        for dx, dy in ((-1, -1), (0, 0), (1, 0), (2, 0), (3, 0), (4, -1)): put(px, mx + dx, my + dy, deep)
        for dx in (1, 2, 3): put(px, mx + dx, my + 1, lip)
        put(px, hx - 5, ey + 4, mix(s[0], '#c06050', 0.25)); put(px, hx - 4, ey + 4, mix(s[0], '#c06050', 0.25))   # a flush
    elif mood == 'sad':
        for dx, dy in ((-1, 1), (0, 0), (1, 0), (2, 0), (3, 0), (4, 1)): put(px, mx + dx, my + dy, deep)
    elif mood == 'worried':
        for dx, dy in ((0, 0), (1, 0), (2, 1), (3, 0)): put(px, mx + dx, my + dy, deep)
    elif mood == 'angry':
        for dx in range(-1, 5): put(px, mx + dx, my, deep)
        for dx in (0, 1, 2, 3): put(px, mx + dx, my + 1, '#d8d0c4')     # bared teeth
        for dx in range(0, 4): put(px, mx + dx, my + 2, deep)
    elif mood == 'surprised':
        for dx, dy in ((1, -1), (2, -1), (0, 0), (3, 0), (0, 1), (3, 1), (1, 2), (2, 2)): put(px, mx + dx, my + dy, deep)
        for dx, dy in ((1, 0), (2, 0), (1, 1), (2, 1)): put(px, mx + dx, my + dy, '#3a1a1a')
    else:
        for dx in range(0, 4): put(px, mx + dx, my, deep)
        for dx in (1, 2): put(px, mx + dx, my + 1, lip)
    if beard == 'stubble':
        st = mix(s[1], HAIR3[hc][2], 0.35)
        for y in range(ey + 5, hy + 12):
            for x in range(hx - 6, hx + 8):
                c = get(px, x, y)
                if c[3] and c[:3] != (18, 15, 20) and not (my - 1 <= y <= my + 2 and mx - 1 <= x <= mx + 4) and (x + y) % 2 == 0:
                    if ((x - hx - 1) / 7.5) ** 2 + ((y - hy - 2) / 10.5) ** 2 < 1:
                        put(px, x, y, st)
    if beard == 'moustache':
        m = HAIR3[hc][1]
        for dx in range(-2, 6): put(px, mx + dx, my - 1, m)
        for dx in range(-1, 5): put(px, mx + dx, my - 2, HAIR3[hc][0])
        put(px, mx - 2, my, m); put(px, mx + 5, my, m)


def paint_details(px, F, o, extras, img):
    cx = F.cx
    # the belt across the waist, with its buckle
    by = int(F.hip_y - 9)
    belt = o['belt']
    for x in range(W):
        for dy in (0, 1):
            c = get(px, x, by + dy)
            if c[3] and c[:3] != (18, 15, 20) and abs(x - cx) < F.sh_w + 2:
                left_edge = get(px, x - 1, by + dy)[:3] == (18, 15, 20) or not get(px, x - 1, by + dy)[3]
                put(px, x, by + dy, belt[0] if dy == 0 else belt[1])
    bx = int(cx + 2)
    for dx in range(3):
        for dy in range(-1, 3):
            if dx in (0, 2) or dy in (-1, 2):
                put(px, bx + dx, by + dy, o['buckle'][0] if dy < 1 else o['buckle'][1])
    # the neckline: a dark V at the collar, laced on a shirt
    ny = int(F.sh_y - 3)
    for i in range(5):
        put(px, int(F.neck[0] + 1 - i * 0.2), ny + i, mix(o['top'][2], '#0c0a10', 0.4) if i < 4 else o['top'][2])
    if o['collar']:
        for dx in range(-3, 5): put(px, int(F.neck[0]) + dx, ny - 1, o['collar'][0])
    # seams down the front of a vest
    if o['vest']:
        for y in range(int(F.sh_y + 1), int(F.hip_y - 10)):
            put(px, int(cx + 1.5), y, mix(o['vest'][2], '#0c0a10', 0.45))
            if y % 5 == 0: put(px, int(cx + 3), y, o['vest'][0])
    # boot laces / turn-downs
    if o['tall']:
        for side in (-1, 1):
            x0 = int(cx + side * F.hip_w * 0.55 + (1 if side > 0 else -1))
            y0 = int(F.knee_y + 3)
            for dx in range(-4, 5):
                c = get(px, x0 + dx, y0)
                if c[3] and c[:3] != (18, 15, 20): put(px, x0 + dx, y0, o['boots'][0])


# ------------------------------------------------------------------------------------------------- output

def main(out=OUT):
    os.makedirs(out, exist_ok=True)
    n = 0
    for name in PEOPLE:
        for mood in MOODS:
            person(name, mood).save(os.path.join(out, name + '_' + mood + '.png'))
            n += 1
    return n


if __name__ == '__main__':
    print('portraits:', main())
