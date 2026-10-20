"""The body chart on the Survival screen: a plain figure seen from the front, cut into parts
(head, chest, belly, arms, legs), one sprite per part on the same 48 x 64 canvas so they stack.

    python3 Tools/Art/bodychart.py      (also run by build.py; writes Sprites/Ui/)

The parts are drawn in light greys so the game can tint each one by how badly it's hurt
(Project Zomboid's health panel does the same), with a dark outline the tint can't wash out.
"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'Assets', 'Game', 'Resources', 'Sprites', 'Ui')
W, H = 48, 64

FILL, SHADE, LIGHT, LINE = (226, 222, 212), (190, 184, 172), (246, 243, 236), (28, 26, 24)


def head(x, y):
    return ((x - 23.5) / 6.2) ** 2 + ((y - 8.5) / 7.2) ** 2 <= 1


def chest(x, y):
    if 15 <= y <= 17 and 21 <= x <= 26: return True          # neck
    if 18 <= y <= 30:
        inset = 2 if y == 18 else 1 if y in (19, 29, 30) else 0   # rounded shoulders, a waist
        return 16 + inset <= x <= 31 - inset
    return False


def belly(x, y):
    if 31 <= y <= 38:
        return 16 <= x <= 31
    return False


def arm(side):
    def f(x, y):
        if not 19 <= y <= 44: return False
        if y == 19 and side == 'left': return 12 <= x <= 14            # round over the shoulder
        if y == 19: return 33 <= x <= 35
        x0 = 11 if y <= 30 else 10                                     # the forearm hangs a little out
        if side == 'right': x0 = 47 - x0 - 3
        if y == 44: return x0 + 1 <= x <= x0 + 2                       # fingertips
        return x0 <= x <= x0 + 3
    return f


def leg(side):
    def f(x, y):
        if not 39 <= y <= 61: return False
        if side == 'left':
            return (16 <= x <= 22) if y < 60 else (14 <= x <= 22)
        return (25 <= x <= 31) if y < 60 else (25 <= x <= 33)
    return f


PARTS = {
    'head': head, 'chest': chest, 'belly': belly,
    'left_arm': arm('left'), 'right_arm': arm('right'),
    'left_leg': leg('left'), 'right_leg': leg('right'),
}


def draw(inside):
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    px = im.load()
    for y in range(H):
        for x in range(W):
            if inside(x, y):
                edge_r = not inside(x + 1, y) or not inside(x + 2, y)
                edge_l = not inside(x - 1, y)
                px[x, y] = SHADE + (255,) if edge_r else LIGHT + (255,) if edge_l else FILL + (255,)
    # a one-pixel dark outline just outside the shape
    for y in range(H):
        for x in range(W):
            if px[x, y][3] == 0 and any(0 <= x + dx < W and 0 <= y + dy < H and inside(x + dx, y + dy)
                                        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                px[x, y] = LINE + (255,)
    return im


def main(out=OUT):
    os.makedirs(out, exist_ok=True)
    made = []
    for name, inside in PARTS.items():
        path = os.path.join(out, 'chart_' + name + '.png')
        draw(inside).save(path)
        made.append(path)
    return made


if __name__ == '__main__':
    print('\n'.join(main()))
