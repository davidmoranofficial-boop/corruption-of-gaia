"""Turns an area's 3D renders into the game's pictures (plain Python 3 with Pillow and numpy).

    python3 cut.py coast_road

Writes Assets/Game/Resources/Sprites/HD/<area>/:
  g<k>.jpg       the ground: the whole picture, in bands
  o<row>.png     everything that stands on tile row <row>, cut out, so the game can draw it in front of you
                 when you're behind it (north of it) and leave it behind you when you're in front
  layout.json    sizes and where each piece goes, in pixels at ppt pixels per tile
"""
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
AREA = sys.argv[1] if len(sys.argv) > 1 else 'coast_road'
RAW = os.environ.get('RAW', os.path.join(HERE, 'raw', AREA))
OUT = os.path.join(HERE, '..', '..', 'Assets', 'Game', 'Resources', 'Sprites', 'HD', AREA)
os.makedirs(OUT, exist_ok=True)

W, H, PPT, BAND = map(int, open(os.path.join(RAW, 'bands.txt')).read().split())
bands = [(y, min(H, y + BAND)) for y in range(0, H, BAND)]
beauty = np.zeros((H * PPT, W * PPT, 3), np.uint8)
index = np.zeros((H * PPT, W * PPT), np.uint8)
for k, (y0, y1) in enumerate(bands):
    b = np.array(Image.open(os.path.join(RAW, 'beauty_%d.png' % k)).convert('RGB'))
    i = np.array(Image.open(os.path.join(RAW, 'index_%d.png' % k)))[..., 0]
    beauty[y0 * PPT:y1 * PPT] = b
    index[y0 * PPT:y1 * PPT] = i

for f in os.listdir(OUT):
    if f[0] in 'go' and (f.endswith('.png') or f.endswith('.jpg')): os.remove(os.path.join(OUT, f))
layout = {'ppt': PPT, 'w': W * PPT, 'h': H * PPT, 'ground': [], 'occluders': []}
for k, (y0, y1) in enumerate(bands):
    name = 'g%d' % k
    Image.fromarray(beauty[y0 * PPT:y1 * PPT]).save(os.path.join(OUT, name + '.jpg'), quality=92, subsampling=0)
    layout['ground'].append({'img': name, 'y': y0 * PPT, 'h': (y1 - y0) * PPT})

# which pixels could ever be in front of someone: those over a tile you can stand on (or just above one, where
# your head is), north of the thing's own row. Elsewhere (deep forest over more forest) nobody is ever behind it.
data = json.load(open(os.path.join(HERE, '..', '..', 'Assets', 'Game', 'Resources', 'Data', 'data.json')))
area = next(a for a in data['areas'] if a['id'] == AREA)
solid = {t['key'] for t in data['tiles'] if t.get('solid')}
walk_row = np.full((H + 1, W), 10 ** 6)       # per tile: its row if you can stand there
for y, r in enumerate(area['rows']):
    for x, ch in enumerate(r):
        if ch not in solid: walk_row[y, x] = y
for o in area.get('objects', []):
    if 0 <= o['y'] < H and 0 <= o['x'] < W: walk_row[o['y'], o['x']] = min(walk_row[o['y'], o['x']], o['y'])
# per pixel: the smallest row of a standable tile whose figure (a tile wide, a tile and a half tall) covers it
ty = np.arange(H * PPT) // PPT
ty2 = np.minimum((np.arange(H * PPT) + PPT // 2) // PPT, H - 1)
tx = np.arange(W * PPT) // PPT
near = np.minimum(walk_row[ty][:, tx], walk_row[ty2][:, tx])

for v in np.unique(index):
    if v < 2: continue
    row = int(v) - 2
    m = (index == v) & (near < row)
    if not m.any(): continue
    ys, xs = np.where(m)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    rgba = np.zeros((y1 - y0, x1 - x0, 4), np.uint8)
    rgba[..., :3] = beauty[y0:y1, x0:x1]
    rgba[..., 3] = m[y0:y1, x0:x1] * 255
    name = 'o%d' % row
    Image.fromarray(rgba).save(os.path.join(OUT, name + '.png'), optimize=True)
    layout['occluders'].append({'img': name, 'row': row, 'x': int(x0), 'y': int(y0), 'w': int(x1 - x0), 'h': int(y1 - y0)})
json.dump(layout, open(os.path.join(OUT, 'layout.json'), 'w'), indent=1)
print('wrote', len(layout['ground']), 'bands and', len(layout['occluders']), 'rows of things to', OUT)
