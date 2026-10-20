"""Builds every sprite the game uses from the text grids in src/ into Assets/Game/Resources/Sprites.

    python3 Tools/Art/build.py            (from the repository root)

How the art works:
- A sprite is a text grid. Each letter is one pixel, and stands for a colour in palette.py. '.' is see-through.
- Characters are paper dolls on a 32 x 48 frame: a body, then hair, then one layer per piece of gear, all lined up.
  The body and gear layers are cut out of src/male.txt and src/female.txt (a fully dressed character), so the
  starting clothes, the bare body and every layer always fit together.
- Skin and hair colours are swapped per choice at character creation (palette.SKINS, palette.HAIRS).
- Outlines are added here: a dark version of the colour they wrap. Gear only gets an outline where it sticks out
  past the body, so layers stack without lines drawn across the character.
"""
import os
from PIL import Image
from palette import PAL, SKINS, HAIRS

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'Assets', 'Game', 'Resources', 'Sprites')
W, H = 32, 48


def hexrgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def load(name, w=W, h=H):
    rows = [l.rstrip('\n') for l in open(os.path.join(HERE, 'src', name)) if not l.startswith('#')]
    rows = [r.ljust(w, '.')[:w] for r in rows][:h]
    rows += ['.' * w] * (h - len(rows))
    return [list(r) for r in rows]


def blank(w=W, h=H):
    return [['.'] * w for _ in range(h)]


def copy(g):
    return [list(r) for r in g]


def where(g, test):
    """A new grid keeping only the pixels where test(x, y, ch) is true."""
    out = blank(len(g[0]), len(g))
    for y, row in enumerate(g):
        for x, ch in enumerate(row):
            if ch != '.' and test(x, y, ch):
                out[y][x] = ch
    return out


def recolour(g, mapping):
    return [[mapping.get(ch, ch) for ch in row] for row in g]


def render(g, swaps=None, outline='full', mask=None):
    """Turns a grid into an image. outline: 'full' all round, 'outside' only where mask is see-through, or None."""
    cmap = {k: hexrgb(v) for k, v in PAL.items()}
    if swaps:
        cmap.update({k: hexrgb(v) for k, v in swaps.items()})
    w, h = len(g[0]), len(g)
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    px = img.load()
    for y in range(h):
        for x in range(w):
            if g[y][x] != '.':
                px[x, y] = cmap[g[y][x]] + (255,)
    if outline:
        o = hexrgb(PAL['o'])
        out = img.copy()
        po = out.load()
        mp = mask.load() if mask is not None else None
        for y in range(h):
            for x in range(w):
                if px[x, y][3]:
                    continue
                if outline == 'outside' and mp is not None and mp[x, y][3]:
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < w and 0 <= yy < h and px[xx, yy][3]:
                        r, gg, b, _ = px[xx, yy]
                        po[x, y] = (int(o[0] * 0.6 + r * 0.25), int(o[1] * 0.6 + gg * 0.25), int(o[2] * 0.6 + b * 0.25), 255)
                        break
        img = out
    return img


def save(img, *parts):
    path = os.path.join(OUT, *parts)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    return path


# ---------------------------------------------------------------------------------------------- the dressed figures

MALE = load('male.txt')
FEMALE = load('female.txt')
for y in range(14, H):                          # both figures share one body below the head, so gear fits both
    FEMALE[y] = list(MALE[y])
FEMALE[14][9] = 'h'                             # her hair falls past the shoulder

HAIR = set('hHjJ')
SHIRT_ROWS = range(14, 25)
ARM = lambda x, y: (x >= 19 or x == 10) and 15 <= y <= 20     # upper arms (near and far), covered by the sleeves


def is_knife(x, y, ch):
    return 26 <= y <= 31 and x >= 21 and ch in 'BbyYG' and not (y == 26 and x == 21)


shirt = where(MALE, lambda x, y, ch: y in SHIRT_ROWS and ch in 'dDb')
trousers = where(MALE, lambda x, y, ch: (y == 25 and 11 <= x <= 18) or (26 <= y <= 37 and ch in 'nN'))
boots = where(MALE, lambda x, y, ch: 38 <= y <= 42 and ch in 'bB')
gutting_knife = where(MALE, is_knife)


def bare(fig, female):
    """The body with nothing on but plain smallclothes: hair becomes scalp, clothes become skin."""
    g = copy(fig)
    for y in range(H):
        for x in range(W):
            ch = g[y][x]
            if ch == '.':
                continue
            if ch in HAIR:
                if female and MALE[y][x] not in HAIR and MALE[y][x] not in 'sSqQ':
                    g[y][x] = '.'           # loose strands, not head: the scalp is the same shape for both
                else:
                    g[y][x] = 'S' if ch in 'hj' else 'q'
            elif y in SHIRT_ROWS and ch in 'dDb':
                if ARM(x, y):
                    g[y][x] = 'q' if x == 10 else ('S' if x == 21 else 's')
                else:
                    g[y][x] = 'y' if ch == 'd' else 'G'
            elif y == 25 and 11 <= x <= 18:
                g[y][x] = 'G'
            elif 26 <= y <= 29 and ch in 'nN':
                g[y][x] = 'y' if ch == 'n' else 'G'
            elif 30 <= y <= 37 and ch in 'nN':
                g[y][x] = 's' if ch == 'n' else 'S'
            elif 38 <= y <= 42 and ch in 'bB':
                g[y][x] = 's' if ch == 'b' else 'S'
            elif is_knife(x, y, ch):
                g[y][x] = '.'
    return g


bodies = {'male': bare(MALE, False), 'female': bare(FEMALE, True)}
hairs = {'male': where(MALE, lambda x, y, ch: ch in HAIR), 'female': where(FEMALE, lambda x, y, ch: ch in HAIR)}

# ---------------------------------------------------------------------------------------------- gear layers

def jerkin():
    g = recolour(shirt, {'d': 'b', 'D': 'B', 'b': 'Q'})
    for y in (16, 18, 20, 22):
        if g[y][15] != '.':
            g[y][15] = 'J'                      # lacing
    return g


def leather_boots():
    g = recolour(boots, {'b': 'Q', 'B': 'H'})
    for y in range(34, 38):
        for x in range(W):
            if trousers[y][x] != '.':
                g[y][x] = 'b' if y == 34 else ('Q' if trousers[y][x] == 'n' else 'H')
    return g


def iron_knife():
    g = blank()
    g[26][22] = 'B'
    for x in (20, 21, 22, 23):
        g[27][x] = 'G'                          # crossguard
    for y in range(28, 34):
        g[y][21], g[y][22] = 'Y', 'y'
    g[34][21] = 'y'
    return g


def wood_axe():
    g = blank()
    for y in range(26, 42):
        g[y][22] = 'b' if y % 3 else 'B'
    for y, xs in ((36, (23, 24)), (37, (23, 24, 25)), (38, (23, 24, 25, 26)), (39, (23, 24, 25, 26)), (40, (23, 24, 25)), (41, (23, 24))):
        for x in xs:
            g[y][x] = 'Y' if x == max(xs) else ('G' if x == 23 else 'y')
    return g


def hunting_bow():
    g = blank()
    # a curved stave held upright, bowing out in front of you, the string a pale line nearer the body
    for y in range(17, 41):
        t = (y - 29) / 12.0
        x = 23 + int(round(4 * (1 - t * t)))
        g[y][x] = 'b' if y % 4 else 'B'
        if g[y][22] == '.': g[y][22] = 'W'      # clear of the arm
    for y in range(27, 31):
        g[y][27] = 'Q'                         # the grip
    return g


def buckler():
    g = blank()
    cx, cy = 9, 22
    for y in range(H):
        for x in range(W):
            d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
            if d <= 2:
                g[y][x] = 'y'
            elif d <= 4.4:
                g[y][x] = 'D' if (x < cx and y < cy) else 'b'
            elif d <= 5.6:
                g[y][x] = 'G'
    g[cy - 1][cx - 1] = 'Y'
    return g


def gloves():
    g = where(bodies['male'], lambda x, y, ch: 21 <= y <= 26 and x >= 18 and ch in 'sSqQ')
    g = recolour(g, {'s': 'b', 'S': 'B', 'q': 'B', 'Q': 'H'})
    for (x, y) in ((19, 25), (20, 24)):
        if g[y][x] != '.':
            g[y][x] = 'y'
    return g


def bone_charm():
    g = blank()
    g[14][15] = g[14][17] = 'B'
    g[15][16] = 'W'
    g[16][16] = 'Y'
    return g


GEAR = {
    # id: (grid, drawn behind the body)
    'castaway_shirt': (shirt, False),
    'castaway_trousers': (trousers, False),
    'castaway_boots': (boots, False),
    'gutting_knife': (gutting_knife, False),
    'knife': (iron_knife(), False),
    'wood_axe': (wood_axe(), False),
    'hunting_bow': (hunting_bow(), False),
    'buckler': (buckler(), True),
    'gloves': (gloves(), False),
    'iron_cap': (load('helmet.txt'), False),
    'leather_jerkin': (jerkin(), False),
    'leather_boots': (leather_boots(), False),
    'bone_charm': (bone_charm(), False),
}

ICON_ONLY = {
    'bread': [
        "................", "................", "................", "................",
        "....oooooooo....", "...oDddddddDo...", "..oDdJdddJddDo..", "..odddddddddDo..",
        "..oDDdddddDDbo..", "..obDDDDDDDbbo..", "...obbbbbbbbo...", "....oooooooo....",
    ],
    'fish': [
        "................", "................", "................", "................",
        "..........oo....", ".....ooooodoo...", "...ooyYYYyyDo.o.", "..oeYYyyyyyyDoDo",
        "..oyyyyyyyyyDDDo", "...ooGGGGGGDoo.o", ".....oooooooo...",
    ],
    'mussels': [
        "................", "................", "................", "................",
        "................", "....ooo...ooo...", "...oNnno.oNnno..", "..oNnCnNoNnCnNo.",
        "..oNnnnNoNnnnNo.", "...oNNNo.oNNNo..", "....ooo...ooo...",
    ],
    'berries': [
        "................", "................", "................", ".......Fv.......",
        "......FvF.......", "....ooo.ooo.....", "...ouupoupuo....", "...oupuooupuo...",
        "....ooouupuo....", "....oupuooo.....", "....ouupo.......", ".....ooo........",
    ],
    'flint': [
        "................", "................", "................", "................",
        "......oooo......", ".....oyYYyo.....", "....oGyYyyGo....", "....oGGyyGGo....",
        ".....oGGGGo.....", "......oooo......",
    ],
    'gem': [
        "................", "................", "................", "................",
        "......oooo......", ".....offfFo.....", "....offfFFvo....", "....oFfFFvvo....",
        ".....oFvvvo.....", "......oVVo......", ".......oo.......",
    ],
    'crab_meat': [
        "................", "................", "................", "................",
        "................", "....oooooo......", "...ordrrrrro....", "...orrdrRrrRo...",
        "....oRRRRRRo....", ".....oooooo.....",
    ],
    'mushrooms': [
        "................", "................", "................", "....oooo........",
        "...odddDo.oooo..", "..oddddDDodddDo.", "..ooooooooddDDo.", "....oWo..ooooo..",
        "....oWo...oWo...", "....oWo...oWo...", "....ooo...ooo...",
    ],
    'hazelnuts': [
        "................", "................", "................", "................",
        "......FF........", ".....ofFo.......", "....obDDbo.ooo..", "....oDdDbooDdbo.",
        "....obDDbooDDbo.", ".....oboo.obbo..", "......o....oo...",
    ],
    'samphire': [
        "................", "................", "................", "...f..f...f.....",
        "...F.fF..fF..f..", "...FfF...F..fF..", "....F..f.F.fF...", "....F.fF.FfF....",
        ".....FF..FF.....", "......vvvv......", "......vvvv......",
    ],
    'bucket': [
        "................", "................", "................", "....oooooooo....",
        "...o........o...", "...oooooooooo...", "...obDbDbDbbo...", "...oDbDbDbDbo...",
        "...oyyyyyyyyo...", "....obDbDbbo....", "....oDbDbDbo....", "....oyyyyyyo....",
        ".....oooooo.....",
    ],
    'water_bucket': [
        "................", "................", "................", "....oooooooo....",
        "...o........o...", "...oooooooooo...", "...ocwwcwwcwo...", "...oDbDbDbDbo...",
        "...oyyyyyyyyo...", "....obDbDbbo....", "....oDbDbDbo....", "....oyyyyyyo....",
        ".....oooooo.....",
    ],
    'firewood': [
        "................", "................", "................", "................",
        "..oo........oo..", ".oDDoooooooobDo.", ".odbbbbbbbbbbdo.", ".oDDoooooooobDo.",
        "..ooooooooooooo.", ".oDdbbbbbbbbbbo.", ".oddbbbbbbbbbDo.", "..ooooooooooooo.",
    ],
    'boar_meat': [
        "................", "................", "................", "................",
        "....ooooooo.....", "...omRRRRRmo....", "..omRrrRRRRmoo..", "..omRRRRRrRmoWo.",
        "..ommRRRRRmmoWo.", "...ommmmmmmooo..", "....ooooooo.....",
    ],
    'hare': [
        "................", "................", "................", "...oo...........",
        "..oyGo..........", "..oyGGo.........", "...oyGGoooooo...", "...oeyyyyyyyyo..",
        "..oyyyyyyyyyGGo.", "..oGyyyyyyyGGGWo", "...ooGGooGGGGoo.", "....oGo..oGGo...",
        "....oo....ooo...",
    ],
    'split_wood': [
        "................", "................", "................", "................",
        "......oooo......", ".....odddDo.....", "....odJddDbo....", "...odJdddDbbo...",
        "...oDDDbbbbbo...", "....obbbbbbo.oo.", ".oo..oooooo.odDo", "odDo......odDbDo",
        "obbDooooooobbbo.", ".ooooooooooooo..",
    ],
    'pail': [
        "................", "................", "................", "................",
        ".....oooooo.....", "....o......o....", "...oooooooooo...", "...oyGyyGyyGo...",
        "...oGyyGyyGyo...", "....oyGyyGyo....", "....oGyyGyGo....", ".....oooooo.....",
    ],
    'water_pail': [
        "................", "................", "................", "................",
        ".....oooooo.....", "....o......o....", "...oooooooooo...", "...ocwwcwwcwo...",
        "...oGyyGyyGyo...", "....oyGyyGyo....", "....oGyyGyGo....", ".....oooooo.....",
    ],
    'ore': [
        "................", "................", "................", "................",
        "......ooooo.....", "....ooGRyGGo....", "...oGRRyGrGGo...", "..oyGGRGGRRGgo..",
        "..oGyRrGGGyRgo..", "...oGGGRGGGgo...", "....oooooooo....",
    ],
    'pick': [
        "................", "................", "..ooo......ooo..", ".oYyyoooooooyYo.",
        "..ooGyyyyyyyGoo.", "....oooobooooo..", ".......obo......", ".......obo......",
        ".......obo......", ".......obo......", ".......obo......", ".......obo......",
        ".......oBo......", "........o.......",
    ],
    'guild_card': [
        "................", "................", "................", "..oooooooooooo..",
        "..odddddddddDo..", "..odaadddbbbDo..", "..odaAdddddddo..", "..odddddbbbbDo..",
        "..odbbbbddddDo..", "..odddddddddDo..", "..oDDDDDDDDDDo..", "..oooooooooooo..",
    ],
    'health_potion': [
        "................", "......ooo.......", ".....obbbo......", "......obo.......",
        ".....oyWyo......", "....oyWWyyo.....", "...orWrrrrRo....", "...orWrrrrRo....",
        "...orrrrrRRo....", "...oRrrrRRmo....", "....oRRRRmo.....", ".....ooooo......",
    ],
    'bandage': [
        "................", "................", "................", "................",
        "......ooooo.....", ".....oWWWddo....", "....oWWddWWdo...", "....oWdoodWdo...",
        "....oWdoodWdooo.", "....oWWddWWddWWo", ".....oWWWddoooo.", "......ooooo.....",
    ],
    'spirits': [
        "................", ".......oo.......", "......obbo......", "......oyyo......",
        "......oyyo......", ".....oyccyo.....", "....ocWccCco....", "....ocWaaaCo....",
        "....ocWaAaCo....", "....ocWcccCo....", "....oCCCCCCo....", ".....oooooo.....",
    ],
    'willow_bark': [
        "................", "................", "................", "................",
        "...ooooooo......", "..obDbDDbbooo...", "..oDbbDbDbbDbo..", "...oobDbbDbDbDo.",
        ".....oooDbbDbo..", "........ooooo...",
    ],
    'sewing_kit': [
        "................", "................", "................", "...ooooooooo....",
        "..obbbbbbbbbo...", "..obBBBBBBBbo...", "..obBaaaaaBbo.o.", "..obBBBBBBBbooWo",
        "..obbbbbbbbbo.oo", "...ooooooooo..o.", "..............o.",
    ],
    'yarrow': [
        "................", "...oo.oo.oo.....", "..oWWoWWoWWo....", "...oWWWWWWo.....",
        "....ooFoFoo.....", "......oFo.......", "....oo.Fo.oo....", "...ofFoFofFo....",
        "....ooiFFoo.....", "......oFo.......", "......oFo.......", ".......o........",
    ],
    'splint': [
        "................", "................", "................", "..oooooooooooo..",
        "..ojJJJJJJJJjo..", "..oooooooooooo..", "....oWo..oWo....", "..oooooooooooo..",
        "..ojJJJJJJJJjo..", "..oooooooooooo..",
    ],
    'copper_ring': [
        "................",
        "................",
        "................",
        "................",
        "......RRRR......",
        ".....Rr..rR.....",
        "....Rr....RR....",
        "....R......R....",
        "....R......R....",
        "....RR....RR....",
        ".....RR..RR.....",
        "......RRRR......",
    ],
}


def icon(g):
    xs = [x for row in g for x, ch in enumerate(row) if ch != '.']
    ys = [y for y, row in enumerate(g) for ch in row if ch != '.']
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    w, h = x1 - x0 + 1, y1 - y0 + 1
    out = blank(16, 16)
    ox, oy = (16 - w) // 2, (16 - h) // 2
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if 0 <= y - y0 + oy < 16 and 0 <= x - x0 + ox < 16:
                out[y - y0 + oy][x - x0 + ox] = g[y][x]
    return out


def main():
    made = []
    for fig in ('male', 'female'):
        for skin, ramp in SKINS.items():
            made.append(save(render(bodies[fig], dict(zip('sSqQ', ramp))), 'Body', 'body_%s_%s.png' % (fig, skin)))
        for hair, ramp in HAIRS.items():
            mask = render(bodies[fig])
            made.append(save(render(hairs[fig], dict(zip('hH', ramp)), 'outside', mask), 'Hair', 'hair_%s_%s.png' % (fig, hair.replace(' ', '_'))))
    mask = render(bodies['male'])
    for gid, (g, back) in GEAR.items():
        made.append(save(render(g, None, 'outside', mask), 'Gear', gid + '.png'))
        made.append(save(render(icon(g)), 'Icons', gid + '.png'))
    for gid, rows in ICON_ONLY.items():
        made.append(save(render([list(r.ljust(16, '.')) for r in rows] + [['.'] * 16] * (16 - len(rows))), 'Icons', gid + '.png'))
    from beasts import wolf
    g, w, h = wolf()
    made.append(save(render(g), 'Enemies', 'wolf.png'))
    # the pack leader: the same wolf, darker, black along the spine, a scar across the muzzle and hot yellow eyes
    alpha = recolour(g, {'Y': 'y', 'y': 'G', 'G': 'g', 'g': 'o', 'W': 'Y'})
    for (x, y) in ((3, 12), (4, 13), (5, 13), (6, 14), (7, 14)):
        if alpha[y][x] != '.': alpha[y][x] = 'r'
    alpha[11][9], alpha[11][10] = 'a', 'r'
    made.append(save(render(alpha), 'Enemies', 'wolf_alpha.png'))
    from beasts import boar
    g, w, h = boar()
    made.append(save(render(g), 'Enemies', 'boar.png'))
    from backdrops import BACKDROPS
    for name, paint in BACKDROPS.items():
        made.append(save(paint(), 'Backdrops', name + '.png'))
    import fx
    made += fx.main()
    import world
    world.main()
    print('built %d sprites into %s' % (len(made), os.path.normpath(OUT)))


if __name__ == '__main__':
    main()
