"""Battlefields for the grid fights (David, 25 Oct): a handful of hand-made layouts for each kind of ground.

    python3 Tools/Art/battlemaps.py        (also run by build.py)

Each layout is 12 x 9 tiles in the world's tile letters (see world.py TILES). The outer ring is scenery: it's
painted, but nobody stands in it. The fight is on the 10 x 7 inside. Your side starts on the left (inner
columns 0-1), the enemies on the right (inner columns 7-9), so keep those mostly open.

Trees (T), rocks (r), brambles (k), crates (K), barrels (n) and woodpiles (j) block moving and arrows. Long grass
(g) makes it harder to hit someone with an arrow. A boar that charges into a tree or rock stuns itself, so a
tree in the middle is a tool, not just scenery.

Writes Sprites/Battle/<id>.png (the ground) and <id>_over.png (tree tops and anything drawn over the fighters),
and the layouts into data.json "battleMaps".
"""
import json, os
import terrain

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, '..', '..', 'Assets', 'Game')
OUT = os.path.join(GAME, 'Resources', 'Sprites', 'Battle')
DATA = os.path.join(GAME, 'Resources', 'Data', 'data.json')

MAPS = {
    # ------------------------------------------------------------------ in the woods
    'forest_glade': ('forest', [
        'TTTTTTTTTTTT',
        'T..g....T..T',
        'T.....g....T',
        'T...T......T',
        'T.......r..T',
        'T.g...T..g.T',
        'T.....gg...T',
        'T..g.......T',
        'TTTTTTTTTTTT',
    ]),
    'forest_trail': ('forest', [
        'TTTTTTTTTTTT',
        'T.g...T....T',
        'T......g.T.T',
        'T==========T',
        'T....T.....T',
        'T.gg....g..T',
        'T....g...T.T',
        'T.T........T',
        'TTTTTTTTTTTT',
    ]),
    'forest_thicket': ('forest', [
        'TTTTTTTTTTTT',
        'T...kk.....T',
        'T.g....T...T',
        'T.....g....T',
        'T..T.....g.T',
        'T.....r....T',
        'T.g..T..k..T',
        'T........g.T',
        'TTTTTTTTTTTT',
    ]),
    # ------------------------------------------------------------------ open hills
    'hills_slope': ('hills', [
        'T.r.g.T..r.T',
        '...g....g..r',
        'r.....r.....',
        '..gg.......g',
        'g....g..g...',
        '...r.......T',
        'r......gg...',
        '..g.........',
        'Tg.r..T.g.rT',
    ]),
    'hills_cairn': ('hills', [
        'r..T.g..r..T',
        '..g......g..',
        'g....g......',
        '......rr...r',
        'T..g........',
        '........g..g',
        'r.g..r......',
        '......g.....',
        'T.r..g.Tr..r',
    ]),
    # ------------------------------------------------------------------ on the road
    'road_bend': ('road', [
        'TTgTTTTgTTTT',
        'T.....g....T',
        'T..g...T...T',
        '============',
        'T...r......T',
        'T.g.....g..T',
        'T.....g....T',
        'T..T.......T',
        'TTTTgTTTTTTT',
    ]),
    'road_cart': ('road', [
        'TTTTTTTTTTTT',
        'T.g........T',
        'T....g..T..T',
        'T...====...T',
        '====.K..====',
        'T...====...T',
        'T.g.....g..T',
        'T.....T....T',
        'TTTTTTTTTTTT',
    ]),
    # ------------------------------------------------------------------ by the sea
    'coast_sand': ('coast', [
        '~~~~~~~~~~~~',
        '~,,,,,,,,,,~',
        ',,,,,r,,,,,,',
        ',..,,,,,,..,',
        '...........r',
        '..g...r..g..',
        '....gg......',
        '.g.......T..',
        'TTTTTTTTTTTT',
    ]),
    'coast_rocks': ('coast', [
        '~~~~~~~~~~~~',
        '~,,,,r,,,,,~',
        ',,,,,,,,r,,,',
        ',,r,,,,,,,,,',
        ',,,,,,,r,,,,',
        ',...,,,,,..,',
        '..g.....g...',
        '...r.....g..',
        'TTTTTTTTTTTT',
    ]),
    # ------------------------------------------------------------------ in a village
    'village_yard': ('village', [
        'ZZZZZZZZZZZZ',
        'Zmmmmmmmmmmm',
        'Zmmj...K..mZ',
        'Zmm.......mZ',
        'Zm...n....mZ',
        'Zmm.....n.mZ',
        'Zmm.......mZ',
        'Zmmmmmmmmmmm',
        'ZZZZZZZZZZZZ',
    ]),
    'village_lane': ('village', [
        'ZZZZZZZZZZZZ',
        'Z...K....j.Z',
        'Z..........Z',
        'mmmmmmmmmmmm',
        'mmmm.mmmmmmm',
        'Z...n....g.Z',
        'Z.g........Z',
        'Z.....K....Z',
        'ZZZZZZZZZZZZ',
    ]),
}

SOLID = set('TrkKnj~ZFHWx')


def check(name, rows):
    assert len(rows) == 9, name + ': needs 9 rows'
    for r in rows: assert len(r) == 12, name + ': rows must be 12 wide: ' + r
    inner = [r[1:11] for r in rows[1:8]]
    left = sum(1 for r in inner for c in r[:2] if c not in SOLID)
    right = sum(1 for r in inner for c in r[7:] if c not in SOLID)
    assert left >= 8 and right >= 10, name + ': keep the start columns open'
    # every open square joined up
    open_ = {(x, y) for y, r in enumerate(inner) for x, c in enumerate(r) if c not in SOLID}
    start = next(iter(open_))
    seen, todo = {start}, [start]
    while todo:
        x, y = todo.pop()
        for d in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            n = (x + d[0], y + d[1])
            if n in open_ and n not in seen: seen.add(n); todo.append(n)
    assert seen == open_, name + ': part of the field is cut off'


def main():
    os.makedirs(OUT, exist_ok=True)
    made = []
    out = []
    for i, (name, (terrain_kind, rows)) in enumerate(MAPS.items()):
        check(name, rows)
        img, over = terrain.paint(rows, seed=31 + i * 7)
        img.save(os.path.join(OUT, name + '.png'))
        over.save(os.path.join(OUT, name + '_over.png'))
        made.append(os.path.join(OUT, name + '.png'))
        out.append({'id': name, 'terrain': terrain_kind, 'rows': rows})
    data = json.load(open(DATA))
    data['battleMaps'] = out
    open(DATA, 'w').write(json.dumps(data, indent=2, ensure_ascii=False) + '\n')
    return made


if __name__ == '__main__':
    print('\n'.join(main()))
