"""The region maps. Each region is a grid of terrain tiles (one letter each) that the game walks on, and a
painted picture of the same grid that the map screen shows. Both come from here, so they always match.

    python3 Tools/Art/regions.py      (also run by build.py)

Writes the terrain rows into Assets/Game/Resources/Data/data.json (the region's places, events and beats there
are hand-written and left alone), the picture to Resources/Sprites/Maps/<region>.png, and map icons to
Resources/Sprites/Map/.

Terrain letters: ~ sea, , beach, . grass, f forest, h hills, m marsh, r river, b bridge or ford,
= road, : track, x blighted land.
"""
import json, math, os, random
from PIL import Image
from palette import PAL

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, '..', '..', 'Assets', 'Game')
T = 16


def C(k):
    return tuple(int(PAL[k][i:i + 2], 16) for i in (1, 3, 5)) + (255,)


def line(pts):
    out = []
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        n = max(abs(x1 - x0), abs(y1 - y0), 1)
        for i in range(n + 1):
            out.append((round(x0 + (x1 - x0) * i / n), round(y0 + (y1 - y0) * i / n)))
    return out


# ---------------------------------------------------------------------------------------------- north-west Loran

def loran_nw():
    W, H = 48, 36
    g = [['.'] * W for _ in range(H)]

    def setc(x, y, t):
        if 0 <= x < W and 0 <= y < H: g[y][x] = t

    def blob(cx, cy, rx, ry, t, seed, keep='.'):
        rnd = random.Random(seed)
        for y in range(H):
            for x in range(W):
                d = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2
                if d < 1 + rnd.uniform(-0.35, 0.2) and g[y][x] in keep: g[y][x] = t

    coast = lambda y: 5 + 1.6 * math.sin(y / 4.0) + (0.5 * (y - 24) if y > 24 else 0)
    for y in range(H):
        for x in range(W):
            if x < coast(y): g[y][x] = '~'
            elif x < coast(y) + 1: g[y][x] = ','
    blob(10, 5, 4, 2.5, 'm', 1)                     # the Saltmarsh
    blob(16, 12, 3, 2, 'h', 2)                      # hills above Skelmed (the quarry)
    blob(11, 20, 2.5, 1.8, 'h', 9)                  # the headland
    blob(34, 12, 3, 2.5, 'h', 3)                    # Harrow Hills
    blob(41, 21, 4, 2.5, 'h', 4)
    blob(20, 22, 4, 3.5, 'f', 5)                    # Brackenwood
    blob(39, 31, 10, 6, 'f', 6)                     # the south-eastern forest
    blob(30, 4, 5, 2, 'f', 7)                       # Eskholt's woods
    blob(43, 31, 2.2, 1.8, 'x', 8, keep='f.')       # the Rotwood
    river = [(4, 7), (9, 7), (14, 7), (19, 8), (24, 8), (29, 7), (34, 7), (39, 9), (43, 10), (47, 12)]
    for x, y in line(river): setc(x, y, 'r')
    for x, y in line(river[:3]): setc(x, y + 1, 'r')
    stream = [(13, 15), (9, 16), (5, 16)]
    for x, y in line(stream): setc(x, y, 'r')

    roads = [
        [(8, 26), (9, 23), (9, 20), (9, 17), (8, 14)],                                       # the coast road
        [(8, 14), (12, 14), (16, 15), (21, 15), (27, 16), (34, 17), (40, 17), (47, 18)],    # the old trade road
        [(27, 16), (25, 12), (24, 8), (25, 3)],                                              # north track over the Vael
    ]
    tracks = [[(12, 14), (14, 13), (16, 12)], [(16, 15), (19, 17), (21, 19)]]
    for r in tracks:
        for x, y in line(r): setc(x, y, ':')
    for r in roads:
        for x, y in line(r): setc(x, y, '=')
    for x, y in ((24, 8), (9, 16)):                 # Vael Bridge, and the ford on the coast road
        setc(x, y, 'b')
    return {'id': 'loran_nw', 'rows': [''.join(r) for r in g], 'river': river, 'stream': stream, 'coast': coast}


REGIONS = [loran_nw]

# ---------------------------------------------------------------------------------------------- painting

TREE = ["...oooo...", "..oFFFfo..", ".oFFFFFfo.", ".oFvFFFFo.", ".ovFFvFFo.", "..ovvvvo..", "....ob....", "....oo...."]
HILL = ["......oo......", "....ooDDoo....", "...oDDDdDDo...", "..oDbDDDDDDo..", ".obbbDDDDDDDo.", "oooooooooooooo"]


def paint(region):
    rows = region['rows']
    H, W = len(rows), len(rows[0])
    img = Image.new('RGBA', (W * T, H * T))
    px = img.load()

    def fill(tx, ty, base, specks, seed, density):
        rnd = random.Random(seed)
        for y in range(T):
            for x in range(T):
                px[tx * T + x, ty * T + y] = C(rnd.choice(specks) if rnd.random() < density else base)

    def put(tx, ty, sprite, ox=0, oy=0):
        for y, row in enumerate(sprite):
            for x, ch in enumerate(row):
                X, Y = tx * T + x + ox, ty * T + y + oy
                if ch != '.' and 0 <= X < W * T and 0 <= Y < H * T: px[X, Y] = C(ch)

    for ty in range(H):
        for tx in range(W):
            t, s = rows[ty][tx], ty * 977 + tx * 131
            if t == 'f':
                fill(tx, ty, 'F', ['v', 'V'], s, 0.3)
                rnd = random.Random(s)
                for ox, oy in ((-2, -4), (5, -3), (10, -1), (-1, 2), (4, 4), (9, 6), (2, 9)):
                    put(tx, ty, TREE, ox + rnd.randrange(0, 3), oy + rnd.randrange(0, 3))
            elif t == 'm':
                fill(tx, ty, 'F', ['v', 'V', 'f'], s, 0.45)
                rnd = random.Random(s)
                for _ in range(3):
                    x0, y0 = rnd.randrange(1, 10), rnd.randrange(1, 13)
                    for i in range(5):
                        px[tx * T + x0 + i, ty * T + y0] = C('w'); px[tx * T + x0 + i, ty * T + y0 + 1] = C('X')
            elif t == 'x':
                fill(tx, ty, 'u', ['P', 'g'], s, 0.3)
                rnd = random.Random(s)
                put(tx, ty, ["..o.o...", "..ogo.o.", "...ogoo.", "...ogo..", "...ogo..", "..oggo.."], rnd.randrange(2, 7), rnd.randrange(2, 8))
            else:
                fill(tx, ty, 'F', ['f', 'v'], s, 0.035)
                if t == 'h':
                    rnd = random.Random(s)
                    put(tx, ty, HILL, rnd.randrange(0, 3), rnd.randrange(4, 10))

    # The coast, smooth at pixel level
    coast = region['coast']
    for Y in range(H * T):
        c = coast(Y / T) * T + 3 * math.sin(Y / 7.0) + 2 * math.sin(Y / 2.3)
        for X in range(0, min(W * T, int(c) + 2 * T)):
            if X < c: col = 'X' if (X * 31 + Y * 17) % 41 else 'w'
            elif X < c + 2: col = 'Y'
            elif X < c + 9: col = 'd' if (X + Y) % 7 else 'D'
            else: continue
            px[X, Y] = C(col)

    # Rivers, smooth
    def smooth(pts, steps=12):
        P = [(x * T + 8, y * T + 8) for x, y in pts]
        P = [P[0]] + P + [P[-1]]
        out = []
        for i in range(1, len(P) - 2):
            p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
            for k in range(steps):
                t = k / steps
                out.append(tuple(0.5 * ((2 * p1[j]) + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t * t
                                        + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t ** 3) for j in (0, 1)))
        return out
    for pts, wide in ((region['river'], 9), (region['stream'], 5)):
        for i, (x, y) in enumerate(smooth(pts)):
            wdt = wide if (pts is region['stream'] or i < 30) else 6
            for dx in range(-wdt - 2, wdt + 3):
                for dy in range(-wdt - 2, wdt + 3):
                    X, Y = int(x + dx), int(y + dy)
                    if not (0 <= X < W * T and 0 <= Y < H * T): continue
                    d = dx * dx + dy * dy
                    if d <= (wdt / 1.6) ** 2: px[X, Y] = C('w') if (X * 13 + Y * 7) % 29 else C('C')
                    elif d <= (wdt / 1.6 + 1.5) ** 2 and px[X, Y][:3] not in (C('w')[:3], C('C')[:3]): px[X, Y] = C('V')

    # Roads and tracks, joined tile centre to tile centre
    def path(chars, col, width):
        tiles = {(x, y) for y in range(H) for x in range(W) if rows[y][x] in chars}
        for (x, y) in tiles:
            for (nx, ny) in tiles:
                if abs(nx - x) <= 1 and abs(ny - y) <= 1 and (nx, ny) != (x, y):
                    for i in range(9):
                        X = round(x * T + 8 + (nx - x) * T * i / 16); Y = round(y * T + 8 + (ny - y) * T * i / 16)
                        for d in range(-(width // 2), width - width // 2):
                            for e in range(-(width // 2), width - width // 2):
                                if rows[(Y + e) // T][(X + d) // T] == 'r': continue
                                px[X + d, Y + e] = C(col)
    path(':', 'D', 2)
    path('=b', 'b', 3)
    path('=b', 'D', 1)
    for y in range(H):
        for x in range(W):
            if rows[y][x] == 'b':                   # planks over the water
                vertical = rows[y - 1][x] not in 'r' and rows[y + 1][x] not in 'r'
                for yy in range(16):
                    for xx in range(4, 12):
                        X, Y = (x * T + xx, y * T + yy) if vertical else (x * T + yy, y * T + xx)
                        px[X, Y] = C('B' if (yy % 3 == 0) else 'b')
    return img


# ---------------------------------------------------------------------------------------------- icons

ICONS = {
    'hut': ["....oooooo....", "...oRRRRRRo...", "..oRRRRRRRRo..", ".oRRRRRRRRRRo.", "oRRRRRRRRRRRRo", ".odddddddddo..", ".odBBddddddo..", ".odBBddDdddo..", ".odBBddddddo..", ".oooooooooooo."],
    'village': ["....ooo.............", "...oRRRo.....ooo....", "..oRRRRRo...oRRRo...", ".oRRRRRRRo.oRRRRRo..", "oRRRRRRRRRoRRRRRRRo.", ".odddddddo.odddddo..", ".odBBdddDo.odddDdo..", ".odBBdaddo.odBBddo..", ".odBBdddDo.odBBdDo..", ".ooooooooo.ooooooo..", "......ooo...........", ".....oRRRo..........", "....oRRRRRo.........", "...oRRRRRRRo........", "....odddddo.........", "....odBBddo.........", "....odBBdDo.........", "....ooooooo........."],
    'town': ["...o.......o.......o...", "..oyo.....oyo.....oyo..", "o.oyo.o.o.oyo.o.o.oyo.o", "ooooooooooooooooooooooo", "oyyyyyyyyyyyyyyyyyyyyyo", "oyYyyyyyyyyyyyyyyyyyYyo", "oyyyyyyyyoooooyyyyyyyyo", "oyyyyyyyoBBBBBoyyyyyyyo", "oyYyyyyyoBBBBBoyyyyyYyo", "oyyyyyyyoBBBBBoyyyyyyyo", "ooooooooooooooooooooooo"],
    'quarry': [".....oo.....", "...ooggoo...", "..oggGggGo..", ".oggeeeegGo.", "oggeeeeeeggo", "oooooooooooo"],
    'camp': ["....o....", "...oro...", "..orAro..", ".oBBBBBo.", "..o...o.."],
    'cave': ["....oooo....", "..ooggGgoo..", ".oggeeeeggo.", "oggeeeeeeggo", "oooooooooooo"],
    'ruin': ["..o.oo..", ".oyoyYo.", ".oyyyyo.", ".oyeyyo.", ".oyyyyo.", "oyyyyyyo", "oooooooo"],
    'stones': ["o..oo..o.", "yo.oyo.oy", "yo.oyo.oy", "yo.oyo.oy", "oo.ooo.oo"],
    'cove': ["...oo...", "..oeeo..", ".oeeeeo.", "oooooooo", ".oAAAo..", ".oBBBo.."],
    'blight': ["...oo...", "..oppo..", ".opPPpo.", "..oppo..", "...oo..."],
    'spot': ["..oo..", ".oYYo.", "oYWWYo", ".oYYo.", "..oo.."],
    'rumour': ["..oooooo..", ".oaaaaaao.", "oaaooooaao", "oaao..oaao", ".oo..oaaao", "....oaaao.", "...oaaoo..", "...oaao...", "...oooo...", "...oaao...", "...oaao...", "...oooo..."],
    'you': ["...oooo...", "..oaaaao..", ".oaaWWaao.", ".oaWWWWao.", ".oaaWWaao.", "..oaaaao..", "...oaao...", "....oo...."],
}


def icon(rows):
    w = max(len(r) for r in rows)
    img = Image.new('RGBA', (w, len(rows)), (0, 0, 0, 0))
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch != '.': img.putpixel((x, y), C(ch))
    return img


def main():
    data_path = os.path.join(GAME, 'Resources', 'Data', 'data.json')
    data = json.load(open(data_path))
    data.setdefault('regions', [])
    for make in REGIONS:
        region = make()
        entry = next((r for r in data['regions'] if r['id'] == region['id']), None)
        if entry is None:
            entry = {'id': region['id']}
            data['regions'].append(entry)
        entry['rows'] = region['rows']
        out = os.path.join(GAME, 'Resources', 'Sprites', 'Maps', region['id'] + '.png')
        os.makedirs(os.path.dirname(out), exist_ok=True)
        paint(region).save(out)
    open(data_path, 'w').write(json.dumps(data, indent=2, ensure_ascii=False) + '\n')
    icon_dir = os.path.join(GAME, 'Resources', 'Sprites', 'Map')
    os.makedirs(icon_dir, exist_ok=True)
    for name, rows in ICONS.items():
        icon(rows).save(os.path.join(icon_dir, name + '.png'))
    print('regions: ' + ', '.join(m.__name__ for m in REGIONS))


if __name__ == '__main__':
    main()
