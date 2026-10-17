"""Battle backdrops, 384 x 128 pixels (3:1), painted in palette colours. The ground the fighters stand on is
the bottom fifth. One per kind of place; the game picks by the scene's place and falls back to the coast."""
import math, random
from PIL import Image
from palette import PAL


def C(k):
    return tuple(int(PAL[k][i:i + 2], 16) for i in (1, 3, 5)) + (255,)


def coast():
    W, H = 384, 128
    img = Image.new('RGBA', (W, H))
    px = img.load()
    rnd = random.Random(7)
    horizon = 56
    # Overcast sky in flat bands, with a checker row where two bands meet
    bands = [(0, 'N'), (14, 'n'), (30, 'C'), (46, 'y')]
    for y in range(horizon):
        k = [c for (start, c) in bands if y >= start][-1]
        nxt = [start for (start, c) in bands if start > y]
        if nxt and nxt[0] - y == 1:
            k2 = [c for (start, c) in bands if start == nxt[0]][0]
            for x in range(W): px[x, y] = C(k2 if (x + y) % 2 else k)
            continue
        for x in range(W): px[x, y] = C(k)
    # Heavy clouds: clumps of overlapping rounds with flat bellies
    for cx, cy, n in ((70, 22, 5), (175, 14, 7), (270, 26, 6), (350, 12, 4), (20, 38, 3), (215, 40, 3)):
        puffs = [(cx + rnd.randrange(-n * 5, n * 5), cy + rnd.randrange(-3, 3), rnd.randrange(6, 12)) for _ in range(n)]
        for y in range(H):
            for x in range(W):
                if y > cy + 4 or y >= horizon: continue
                for (px0, py0, r) in puffs:
                    if (x - px0) ** 2 + ((y - py0) * 1.6) ** 2 < r * r:
                        top = (x - px0) ** 2 + ((y - 2 - py0) * 1.6) ** 2 >= r * r
                        px[x, y] = C('Y' if top else ('y' if y < cy + 2 else 'G'))
                        break
    # The sea, with short wave glints
    for y in range(horizon, 80):
        for x in range(W):
            px[x, y] = C('w' if y < horizon + 2 else 'X')
    for _ in range(70):
        x, y = rnd.randrange(150, W - 6), rnd.randrange(horizon + 3, 78)
        for i in range(rnd.randrange(2, 6)):
            px[x + i, y] = C('w' if rnd.random() < 0.7 else 'C')
    # Far headland on the left, dark with a rock face to the sea
    for x in range(0, 160):
        top = 30 + int(26 * (x / 160) ** 2.2) + int(1.5 * math.sin(x / 5.0))
        face = x > 132
        for y in range(top, 80):
            if face and y > top + 3:
                k = 'g' if rnd.random() < 0.8 else 'G'
            else:
                k = 'v' if y < top + 2 else ('V' if rnd.random() < 0.8 else 'v')
            px[x, y] = C(k)
    # Near slope of grass
    for x in range(W):
        top = 76 + int(4 * math.sin(x / 23.0)) + int(1.5 * math.sin(x / 7.0))
        for y in range(top, 100):
            r = rnd.random()
            px[x, y] = C('f' if y == top else ('v' if r < 0.08 else 'f' if r < 0.13 else 'F'))
    # Gorse bushes
    for _ in range(22):
        bx, by, r = rnd.randrange(W), rnd.randrange(80, 96), rnd.randrange(4, 8)
        for y in range(by - r, by + 2):
            for x in range(bx - r - 3, bx + r + 3):
                if 0 <= x < W and ((x - bx) / (r + 3)) ** 2 + ((y - by) / r) ** 2 < 1:
                    q = rnd.random()
                    px[x, y] = C('A' if q < 0.06 else ('V' if y > by - 2 or q < 0.4 else 'v'))
    # The road
    for x in range(W):
        edge = 99 + int(1.5 * math.sin(x / 13.0))
        for y in range(edge, H):
            r = rnd.random()
            px[x, y] = C('B' if y == edge else ('B' if r < 0.08 else 'D' if r < 0.14 else 'b'))
    for _ in range(36):
        x, y = rnd.randrange(W - 3), rnd.randrange(103, H - 2)
        px[x, y] = C('y'); px[x + 1, y] = C('G'); px[x, y + 1] = C('g'); px[x + 1, y + 1] = C('g')
    return img


BACKDROPS = {'coast': coast}

if __name__ == '__main__':
    import sys
    coast().resize((384 * 4, 128 * 4), Image.NEAREST).save(sys.argv[1])
