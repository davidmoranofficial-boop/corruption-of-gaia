"""Beasts drawn from shapes rather than letter grids: the silhouette is built from ellipses and polygons,
then shaded (dark saddle on the back, pale belly and legs, light from the top left) in palette letters."""
from PIL import Image, ImageDraw


def mask(w, h, draw_fn):
    m = Image.new('L', (w, h), 0)
    draw_fn(ImageDraw.Draw(m))
    return m


def wolf():
    W, H = 64, 44
    def far(d):
        d.polygon([(26, 22), (29, 22), (29, 37), (30, 40), (26, 40), (27, 37)], fill=255)          # far front leg
        d.polygon([(45, 20), (49, 21), (52, 29), (50, 33), (50, 40), (47, 40), (47, 33), (48, 29)], fill=255)  # far hind leg
        d.polygon([(13, 6), (15, 1), (17, 8)], fill=255)                                           # far ear
    def body(d):
        d.ellipse((16, 9, 31, 26), fill=255)                       # deep chest
        d.ellipse((24, 11, 50, 22), fill=255)                      # barrel, belly tucked up
        d.ellipse((41, 9, 54, 23), fill=255)                       # haunch
        d.polygon([(11, 9), (21, 7), (26, 11), (22, 20), (14, 17)], fill=255)    # neck and ruff
        d.ellipse((6, 7, 17, 17), fill=255)                        # skull
        d.polygon([(8, 11), (0, 14), (0, 16), (3, 17), (11, 16)], fill=255)      # long muzzle, head held low
        d.polygon([(9, 9), (10, 1), (14, 8)], fill=255)            # near ear
        d.polygon([(52, 11), (57, 15), (60, 22), (60, 30), (58, 35), (55, 34), (55, 26), (52, 19), (49, 15)], fill=255)  # bushy hanging tail
    def near(d):
        d.polygon([(19, 21), (23, 21), (22, 37), (23, 40), (19, 40), (20, 37)], fill=255)          # near front leg
        d.polygon([(43, 17), (53, 18), (52, 24), (55, 31), (52, 34), (53, 40), (49, 40), (50, 34), (51, 31), (46, 24)], fill=255)  # near hind leg: thigh, hock, foot
    mf, mb, mn = mask(W, H, far), mask(W, H, body), mask(W, H, near)
    on = lambda x, y: 0 <= x < W and 0 <= y < H and (mb.getpixel((x, y)) or mn.getpixel((x, y)))
    g = [['.'] * W for _ in range(H)]
    for y in range(H):
        for x in range(W):
            if mf.getpixel((x, y)):
                g[y][x] = 'g' if y >= 38 else 'G'
            if not on(x, y):
                continue
            top = y
            while top > 0 and on(x, top - 1): top -= 1
            bottom = y
            while bottom < H - 1 and on(x, bottom + 1): bottom += 1
            depth, under = y - top, bottom - y
            is_leg = mn.getpixel((x, y)) and not mb.getpixel((x, y))
            if is_leg:
                ch = 'g' if y >= 38 else ('y' if x % 4 == 0 else 'Y')
            elif depth <= 1:
                ch = 'g'                                   # dark saddle
            elif depth <= 3:
                ch = 'G'
            elif under <= 1 and x < 40:
                ch = 'Y'                                   # pale underside
            elif x < 12 and y >= 14:
                ch = 'W'                                   # pale jaw
            elif 13 <= x <= 20 and 14 <= y <= 22:
                ch = 'Y'                                   # pale throat
            else:
                ch = 'y'
            g[y][x] = ch
    for (x, y) in ((30, 15), (34, 16), (38, 15), (31, 18), (36, 19), (44, 14), (47, 17), (27, 13), (56, 22), (57, 27)):
        if g[y][x] in 'yY': g[y][x] = 'G'
    for (x, y) in ((56, 33), (57, 34), (55, 33)):
        if g[y][x] != '.': g[y][x] = 'W'                   # pale tail tip
    g[4][10] = g[5][10] = g[6][11] = 'Q'                   # inside the ear
    g[10][9] = g[10][10] = g[10][11] = 'g'                 # heavy brow
    g[11][9], g[11][10] = 'a', 'e'                         # yellow eye
    g[14][0] = g[13][0] = g[14][1] = 'e'                   # nose
    for x in range(2, 9): g[16][x] = 'e'                   # snarl
    g[15][3] = g[15][5] = g[15][7] = 'W'
    g[17][4] = g[17][7] = 'W'
    return g, W, H


if __name__ == '__main__':
    import sys
    sys.path.insert(0, '.')
    from build import render
    g, w, h = wolf()
    img = render(g)
    img.resize((w * 8, h * 8), Image.NEAREST).save(sys.argv[1])
