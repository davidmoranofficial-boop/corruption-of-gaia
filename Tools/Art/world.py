"""The walkable world: top-down areas you walk round tile by tile, like the old handheld RPGs.

    python3 Tools/Art/world.py      (also run by build.py)

For each area this lays out the tiles, paints the ground picture (16 x 16 pixels a tile) and an overlay drawn
over the player (the front of tall grass), and writes the area into data.json: its tile rows, the things in it
you can talk to or use, its trigger zones and its random events. To change an area, change it here.
It also draws the walking sprites (four directions, three frames each) and the small object sprites.

Tile letters:
  .  grass          g  long grass            ,  sand          ~  sea
  =  road           T  tree                       r  rock          R  rock pool
  w  stream         b  bridge                     k  bramble       h  cliff
  u  high ground    s  steps                      F  fence         H  building
  o  flowers
"""
import json, math, os, random
from PIL import Image
from palette import PAL, SKINS, HAIRS

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, '..', '..', 'Assets', 'Game')
SPR = os.path.join(GAME, 'Resources', 'Sprites')
T = 16

# Every tile letter: name, solid (can't walk on it), road (fewer fights). Written into data.json.
TILES = [
    ('.', 'Grass', False, False), ('g', 'Long grass', False, False), (',', 'Sand', False, False),
    ('~', 'Sea', True, False), ('=', 'Road', False, True), ('T', 'Tree', True, False), ('r', 'Rock', True, False),
    ('R', 'Rock pool', True, False), ('w', 'Stream', True, False), ('b', 'Bridge', False, True),
    ('k', 'Brambles', True, False), ('h', 'Cliff', True, False), ('u', 'High ground', False, False),
    ('s', 'Steps', False, True), ('F', 'Fence', True, False), ('H', 'Building', True, False), ('o', 'Flowers', False, False),
    ('D', 'Door', False, True), ('c', 'Cobbles', False, True), ('P', 'Pier', False, True), ('O', 'Well', True, False),
    ('A', 'Anvil', True, False), ('L', 'Forge', True, False), ('n', 'Barrel', True, False),
    # indoors
    ('p', 'Floor', False, True), ('W', 'Wall', True, False), ('x', 'Wall', True, False), ('M', 'Doorway', False, True),
    ('f', 'Hearth', True, False), ('B', 'Bed', True, False), ('t', 'Table', True, False),
]
SOLID = {k for k, _, solid, _ in TILES if solid}


def C(k):
    return tuple(int(PAL[k][i:i + 2], 16) for i in (1, 3, 5)) + (255,)


def hexc(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5)) + (255,)


# ============================================================================================== the coast road

def coast_road():
    W, H = 30, 64
    g = [['.'] * W for _ in range(H)]

    def setc(x, y, t):
        if 0 <= x < W and 0 <= y < H: g[y][x] = t

    def rect(x0, y0, x1, y1, t, keep=None):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if keep is None or (0 <= y < H and 0 <= x < W and g[y][x] in keep): setc(x, y, t)

    rnd = random.Random(11)
    coast = lambda y: int(round(4 + 1.6 * math.sin(y / 6.5) + (max(0, y - 40) / 5.0)))
    road = lambda y: int(round(15 + 2.6 * math.sin(y / 8.0 + 0.6)))

    # Sea and shore
    for y in range(H):
        c = coast(y)
        for x in range(W):
            if x < c: g[y][x] = '~'
            elif x < c + 2 + (y % 7 == 3): g[y][x] = ','

    # The wood along the east side, ragged
    for y in range(H):
        edge = 25 + int(round(1.5 * math.sin(y / 4.3) + rnd.uniform(-0.4, 0.4)))
        for x in range(edge, W): g[y][x] = 'T'

    # The stream, running from the wood down to the sea
    for x in range(W):
        y0 = int(round(12 + 1.2 * math.sin(x / 3.5)))
        for y in (y0, y0 + 1):
            if g[y][x] != '~': g[y][x] = 'w'

    # The headland: high ground ringed by a cliff, steps up on the west side
    hx0, hy0, hx1, hy1 = 18, 29, 24, 37
    rect(hx0, hy0, hx1, hy1, 'h')
    rect(hx0 + 1, hy0 + 1, hx1 - 1, hy1 - 1, 'u')
    setc(hx0, 33, 's')
    setc(hx0 - 1, 33, '.')

    # Patches of long grass
    for (x0, y0, x1, y1) in ((18, 17, 23, 22), (19, 41, 23, 47), (9, 30, 12, 35), (8, 18, 11, 22), (16, 52, 20, 56)):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if g[y][x] == '.' and not ((x in (x0, x1)) and (y in (y0, y1))): g[y][x] = 'g'

    # Tree clumps and lone trees
    for (x, y) in ((20, 6), (21, 6), (20, 7), (11, 24), (12, 25), (22, 24), (23, 25), (10, 41), (11, 42),
                   (21, 50), (22, 50), (22, 51), (7, 9), (12, 16), (23, 10), (17, 27), (13, 47), (14, 60),
                   (20, 60), (21, 61), (22, 59)):
        if g[y][x] in '.g': g[y][x] = 'T'

    # Flowers
    for _ in range(40):
        x, y = rnd.randrange(W), rnd.randrange(4, H)
        if g[y][x] == '.': g[y][x] = 'o'

    # Brambles on the stream's south bank
    for x in (19, 20, 21, 22):
        y = int(round(12 + 1.2 * math.sin(x / 3.5))) + 2
        if g[y][x] in '.og': g[y][x] = 'k'

    # Tide pools on the beach
    pools = []
    for (x, y, t) in ((5, 44, 'r'), (6, 44, 'R'), (6, 45, 'r'), (5, 46, 'R'), (7, 46, 'r'), (6, 47, 'r'), (4, 43, 'r')):
        x = max(x, coast(y))
        g[y][x] = t
        if t == 'R': pools.append((x, y))

    # Rocks here and there
    for (x, y) in ((8, 13), (16, 38), (24, 39), (13, 50), (7, 27), (24, 16)):
        if g[y][x] in '.o': g[y][x] = 'r'

    # Skelmed's fence across the top, with the gate on the road, and the first roofs beyond
    rect(0, 0, W - 1, 3, '.')
    for x in range(W):
        if g[4][x] != '~': g[4][x] = 'F'
    rect(2, 0, 7, 2, 'H')
    rect(19, 0, 24, 2, 'H')
    rect(25, 0, W - 1, 3, 'T')

    # Yenson's shack, with a trodden path from the door to the road
    rect(9, 55, 13, 57, 'H')
    setc(11, 57, 'D')

    # The road, two tiles wide, from the shack to the gate, with a bridge over the stream
    for y in range(0, 59):
        x = road(y)
        for xx in (x, x + 1):
            setc(xx, y, 'b' if g[y][xx] == 'w' else '=')
    for x in range(11, road(58) + 1):
        setc(x, 58, '=')
    for y in range(0, 4):
        x = road(y)
        for xx in (x - 1, x + 2):
            if g[y][xx] == 'H': continue
    # The path up to the headland steps
    for x in range(road(33) + 2, hx0 - 1):
        setc(x, 33, '=')

    rows = [''.join(r) for r in g]
    rx = road(25)
    sky = road(58)
    area = {
        'id': 'coast_road',
        'name': 'The coast road',
        'minutes': 2,
        'eventChance': 0,          # % per step: something to find, someone to meet, weather
        'fightChance': 6,          # % per step: a fight, the old way, anywhere you walk (less on the road)
        'fightGap': 10,            # at least this many steps between fights
        'spots': [
            {'id': 'shack', 'x': 11, 'y': 58, 'facing': 'down'},
            {'id': 'skelmed', 'x': road(5), 'y': 5, 'facing': 'down'},
            {'id': 'pools', 'x': pools[0][0] + 1, 'y': pools[0][1], 'facing': 'left'},
            {'id': 'headland', 'x': 21, 'y': 33, 'facing': 'down'},
            {'id': 'stream', 'x': 10, 'y': 14, 'facing': 'left'},
        ],
        'objects': [
            {'id': 'sign_shack', 'kind': 'sign', 'sprite': 'obj_sign', 'x': sky + 2, 'y': 57,
             'text': 'North: Skelmed. South: Yenson. Mind the tide.'},
            {'id': 'sign_gate', 'kind': 'sign', 'sprite': 'obj_sign', 'x': road(6) + 2, 'y': 6,
             'text': 'SKELMED. Nets, rope, fish. Mind the dogs. The word "dogs" has been scratched out and "wolves" written over it.'},
            {'id': 'pools', 'kind': 'spot', 'sprite': '', 'x': pools[0][0], 'y': pools[0][1], 'scene': 'pools'},
            {'id': 'crate', 'kind': 'item', 'sprite': 'obj_crate', 'x': coast(52) + 1, 'y': 52, 'scene': 'ev_crate', 'once': True},
            {'id': 'fisher', 'kind': 'npc', 'sprite': 'npc_fisher', 'x': coast(38) + 1, 'y': 38, 'facing': 'left',
             'scene': 'ev_fisher', 'once': True, 'when': ['!night']},
            {'id': 'lookout', 'kind': 'spot', 'sprite': 'obj_cairn', 'x': 22, 'y': 31, 'scene': 'lookout_climb'},
            {'id': 'wolf', 'kind': 'npc', 'sprite': 'obj_wolf', 'x': rx, 'y': 24, 'facing': 'down', 'when': ['!flag:dog_met'], 'scene': 'dog'},
            {'id': 'gulls', 'kind': 'item', 'sprite': 'obj_gulls', 'x': coast(21) + 1, 'y': 21, 'scene': 'ev_taint', 'once': True},
        ],
        'zones': [
            # Walking into these runs a scene. The wolf waits on the road north of the headland.
            {'x0': 0, 'y0': 25, 'x1': W - 1, 'y1': 26, 'scene': 'dog', 'when': ['!flag:dog_met']},
            {'x0': 11, 'y0': 57, 'x1': 11, 'y1': 57, 'to': 'hut_door'},
            {'x0': road(4) - 1, 'y0': 0, 'x1': road(4) + 2, 'y1': 4, 'to': 'village_gate'},
        ],
        'uses': [
            # Facing these tiles lets you use them.
            {'tile': 'w', 'scene': 'stream_drink'},
            {'tile': 'k', 'scene': 'brambles'},
        ],
        'events': [
            # Fights start straight away, Final Fantasy style. win is where you go after winning.
            {'enemy': 'dog', 'win': 'ev_wolf_won', 'kind': 'fight', 'weight': 3, 'terrain': '.go=s'},
            {'enemy': 'crab', 'win': 'ev_crab_won', 'kind': 'fight', 'weight': 3, 'terrain': ','},
        ],
        'rows': rows,
    }
    return area


# ============================================================================================== Yenson's hut

def yenson_hut():
    rows = [
        'xxxxxxxxxxxx',
        'xWWWWWfWWWWx',
        'xBppppppppnx',
        'xBppppppppnx',
        'xpppptttpppx',
        'xppppppppppx',
        'xnppppppppnx',
        'xxxxxMxxxxxx',
    ]
    return {
        'id': 'yenson_hut',
        'name': "Yenson's hut",
        'minutes': 1, 'eventChance': 0, 'fightChance': 0, 'fightGap': 10,
        'indoor': True, 'camp': False,
        'spots': [
            {'id': 'hut_bed', 'x': 2, 'y': 3, 'facing': 'right'},
            {'id': 'hut_door', 'x': 5, 'y': 6, 'facing': 'up'},
        ],
        'objects': [
            {'id': 'yenson', 'kind': 'npc', 'sprite': 'npc_yenson', 'x': 7, 'y': 2, 'facing': 'down', 'scene': 'yenson'},
        ],
        'zones': [
            {'x0': 5, 'y0': 7, 'x1': 5, 'y1': 7, 'to': 'shack'},
        ],
        'uses': [
            {'tile': 'B', 'scene': 'hut_bed'},
            {'tile': 'f', 'scene': 'hut_fire'},
        ],
        'events': [],
        'rows': rows,
    }


# ============================================================================================== Skelmed

def skelmed():
    W, H = 36, 28
    g = [['.'] * W for _ in range(H)]
    rnd = random.Random(23)

    def setc(x, y, t):
        if 0 <= x < W and 0 <= y < H: g[y][x] = t

    def rect(x0, y0, x1, y1, t):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1): setc(x, y, t)

    coast = lambda y: int(round(5 + 1.2 * math.sin(y / 5.0)))
    for y in range(H):
        c = coast(y)
        for x in range(W):
            if x < c: g[y][x] = '~'
            elif x < c + 2: g[y][x] = ','

    # woods round the north and east, the fence along the south with the gate in it
    rect(0, 0, W - 1, 1, 'T')
    for y in range(H):
        for x in range(W - 3 + (y % 3 == 0), W): g[y][x] = 'T'
    for x in range(W):
        if g[26][x] != '~': g[26][x] = 'F'
    rect(0, 27, W - 1, 27, 'T')
    for y in range(27, H): g[y][17] = g[y][18] = 'c'
    for y in range(26, H): g[y][17] = g[y][18] = 'c'

    # the square, the well in the middle
    rect(13, 10, 23, 16, 'c')
    setc(18, 13, 'O')
    # the main street from the gate up to the square
    rect(17, 16, 18, 26, 'c')

    # the harbour: a lane down to the shore and the old pier, far too long for the boats left
    rect(coast(12) + 2, 12, 12, 13, 'c')
    for x in range(0, coast(12) + 2):
        g[12][x] = g[13][x] = 'P'

    # houses: (x0, y0, x1, y1, door x)
    houses = [(7, 3, 11, 6, 9), (14, 3, 20, 6, 17), (23, 3, 27, 6, 25), (8, 18, 12, 21, 10), (27, 9, 31, 13, 29),
              (24, 18, 29, 21, 26)]
    doors = []
    for (x0, y0, x1, y1, dx) in houses:
        rect(x0, y0, x1, y1, 'H')
        doors.append((dx, y1))
    # cobbled paths from the doors to the square
    for (x0, y0, x1, y1, dx) in houses:
        end = 10 if y1 < 10 else 15 if y1 < 18 else 23
        for y in range(y1 + 1, end): setc(dx, y, 'c')
    rect(9, 9, 13, 9, 'c')
    rect(23, 9, 25, 9, 'c')
    rect(23, 14, 29, 14, 'c')
    rect(10, 22, 18, 22, 'c')
    rect(18, 22, 29, 23, 'c')

    # Garran's forge in front of the smithy: the forge itself and the anvil
    setc(24, 22, 'L')
    setc(28, 22, 'A')
    setc(30, 21, 'n'); setc(30, 22, 'n')

    # barrels and crates by the harbour, trees and flowers
    for (x, y) in ((11, 11), (12, 11), (11, 14)):
        setc(x, y, 'n')
    for (x, y) in ((4, 4), (3, 21), (13, 23), (22, 8), (32, 16), (21, 19), (6, 23), (14, 8), (31, 24)):
        if g[y][x] in '.,': g[y][x] = 'T'
    for _ in range(30):
        x, y = rnd.randrange(W), rnd.randrange(2, 26)
        if g[y][x] == '.': g[y][x] = 'o'

    rows = [''.join(r) for r in g]
    return {
        'id': 'skelmed',
        'name': 'Skelmed',
        'minutes': 1, 'eventChance': 0, 'fightChance': 0, 'fightGap': 10,
        'camp': False,
        'doors': doors,
        'spots': [
            {'id': 'village_gate', 'x': 17, 'y': 24, 'facing': 'up'},
        ],
        'objects': [
            {'id': 'garran', 'kind': 'npc', 'sprite': 'npc_garran', 'x': 27, 'y': 22, 'facing': 'right', 'scene': 'smith'},
            {'id': 'ida', 'kind': 'npc', 'sprite': 'npc_ida', 'x': 20, 'y': 13, 'facing': 'left', 'scene': 'ida'},
            {'id': 'bess', 'kind': 'npc', 'sprite': 'npc_bess', 'x': 9, 'y': 8, 'facing': 'down', 'scene': 'bess'},
            {'id': 'dockhand', 'kind': 'npc', 'sprite': 'npc_fisher', 'x': 2, 'y': 13, 'facing': 'left', 'scene': 'dockhand'},
            {'id': 'board', 'kind': 'sign', 'sprite': 'obj_sign', 'x': 15, 'y': 9,
             'text': 'The noticeboard. One notice, months old: BY ORDER OF THE CROWN, THE HARBOUR LEVY IS RAISED TO ONE HALF.'},
        ],
        'zones': [
            {'x0': 17, 'y0': 26, 'x1': 18, 'y1': 27, 'to': 'skelmed'},
        ],
        'uses': [
            {'tile': 'O', 'scene': 'well_drink'},
            {'tile': 'A', 'scene': 'smith'},
            {'tile': 'L', 'scene': 'smith'},
        ],
        'events': [],
        'rows': rows,
    }


AREAS = [coast_road, yenson_hut, skelmed]

# ============================================================================================== tile painting

def tile_painter(rows):
    H, W = len(rows), len(rows[0])
    img = Image.new('RGBA', (W * T, H * T))
    over = Image.new('RGBA', (W * T, H * T), (0, 0, 0, 0))
    px, po = img.load(), over.load()

    def at(x, y):
        if 0 <= x < W and 0 <= y < H: return rows[y][x]
        return None

    def put(tx, ty, x, y, k, layer=None):
        if 0 <= x < T and 0 <= y < T:
            (layer or px)[tx * T + x, ty * T + y] = C(k) if isinstance(k, str) else k

    def grass(tx, ty, base='F', light='f', dark='v', seed=0):
        rnd = random.Random(tx * 7919 + ty * 104729 + seed)
        for y in range(T):
            for x in range(T):
                put(tx, ty, x, y, base)
        for _ in range(5):
            x, y = rnd.randrange(1, 15), rnd.randrange(2, 15)
            put(tx, ty, x, y, light); put(tx, ty, x, y - 1, light)
            put(tx, ty, x + 1, y, dark)
        for _ in range(4):
            put(tx, ty, rnd.randrange(T), rnd.randrange(T), dark)

    def sand(tx, ty):
        rnd = random.Random(tx * 31 + ty * 977)
        for y in range(T):
            for x in range(T):
                put(tx, ty, x, y, 'd')
        for _ in range(10):
            put(tx, ty, rnd.randrange(T), rnd.randrange(T), 'D')
        for _ in range(3):
            put(tx, ty, rnd.randrange(T), rnd.randrange(T), 'W')

    def water(tx, ty, deep='X', mid='w', glint='c'):
        rnd = random.Random(tx * 13 + ty * 71)
        for y in range(T):
            for x in range(T):
                put(tx, ty, x, y, mid)
        for _ in range(3):
            x, y = rnd.randrange(0, 11), rnd.randrange(1, 15)
            for i in range(rnd.randrange(3, 6)): put(tx, ty, x + i, y, deep)
        x, y = rnd.randrange(1, 11), rnd.randrange(1, 15)
        for i in range(3): put(tx, ty, x + i, y, glint)

    def outline_blob(tx, ty, cells, fill):
        """cells: set of (x, y) inside the shape; fill(x, y) gives the colour. Draws a dark rim round it."""
        for (x, y) in cells:
            put(tx, ty, x, y, fill(x, y))
        for (x, y) in cells:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if (x + dx, y + dy) not in cells:
                    put(tx, ty, x, y, 'o')
                    break

    def circle(cx, cy, r, sy=1.0):
        return {(x, y) for y in range(T) for x in range(T) if (x - cx) ** 2 + ((y - cy) * sy) ** 2 <= r * r}

    def cobbles(tx, ty):
        rnd = random.Random(tx * 41 + ty * 67)
        for y in range(T):
            for x in range(T):
                put(tx, ty, x, y, 'G')
        # rounded stones in staggered rows
        for row in range(4):
            off = 2 if (row + ty) % 2 else 0
            for col in range(-1, 4):
                x0, y0 = col * 4 + off, row * 4
                k = rnd.choice(['y', 'y', 'Y', 'y'])
                for y in range(y0, y0 + 3):
                    for x in range(x0, x0 + 3):
                        if (x - x0, y - y0) in ((0, 0), (2, 0), (0, 2), (2, 2)): continue
                        put(tx, ty, x, y, k)
        # soften the edge where it meets grass
        for (dx, dy, xs, ys) in ((-1, 0, [0], range(T)), (1, 0, [T - 1], range(T)), (0, -1, range(T), [0]), (0, 1, range(T), [T - 1])):
            if at(tx + dx, ty + dy) in ('.', 'o', 'g', 'T', 'F'):
                for y in ys:
                    for x in xs:
                        if (x + y) % 3: put(tx, ty, x, y, 'F')

    def floor(tx, ty):
        for y in range(T):
            for x in range(T):
                k = 'D'
                if y % 4 == 3: k = 'b'
                elif (x + (y // 4) * 6 + tx * 4) % 16 == 0: k = 'b'
                elif (x * 7 + y * 3 + tx) % 23 == 0: k = 'd'
                put(tx, ty, x, y, k)

    for ty in range(H):
        for tx in range(W):
            t = rows[ty][tx]
            if t in '.o':
                grass(tx, ty)
                if t == 'o':
                    rnd = random.Random(tx * 5 + ty * 3)
                    for _ in range(3):
                        x, y = rnd.randrange(2, 13), rnd.randrange(2, 13)
                        k = rnd.choice(['W', 'a', 'r'])
                        put(tx, ty, x, y, k); put(tx, ty, x + 1, y, 'v')
            elif t == 'u':
                grass(tx, ty, 'f', 'J', 'F', 3)
            elif t == 'g':
                grass(tx, ty, 'v', 'F', 'V', 5)
                # blades: rows of little peaks; the front half also goes on the overlay
                for row, oy in enumerate((2, 7, 12)):
                    for bx in range(0, T, 4):
                        x0 = bx + (2 if row % 2 else 0)
                        for i, (dx, dy) in enumerate(((0, 3), (1, 2), (1, 1), (2, 0), (2, 1), (3, 2), (3, 3))):
                            k = 'f' if dx < 2 else 'F'
                            if dy == 0: k = 'f'
                            put(tx, ty, x0 + dx, oy + dy, k)
                            if oy >= 7: put(tx, ty, x0 + dx, oy + dy, k, po)
                        put(tx, ty, x0 + 1, oy + 4, 'V')
                        if oy >= 7: put(tx, ty, x0 + 1, oy + 4, 'V', po)
            elif t == ',':
                sand(tx, ty)
            elif t == '~':
                water(tx, ty)
            elif t == 'w':
                water(tx, ty, 'w', 'C', 'c')
            elif t == '=':
                rnd = random.Random(tx * 17 + ty * 23)
                for y in range(T):
                    for x in range(T):
                        put(tx, ty, x, y, 'D')
                for _ in range(8):
                    put(tx, ty, rnd.randrange(T), rnd.randrange(T), 'b' if rnd.random() < 0.6 else 'd')
                # soften the edge where it meets grass
                for (dx, dy, xs, ys) in ((-1, 0, [0], range(T)), (1, 0, [T - 1], range(T)), (0, -1, range(T), [0]), (0, 1, range(T), [T - 1])):
                    if at(tx + dx, ty + dy) in ('.', 'o', 'g', 'u', 'T', 'k', 'r', 'F'):
                        for y in ys:
                            for x in xs:
                                if (x + y) % 3: put(tx, ty, x, y, 'F')
            elif t == 'b':
                for y in range(T):
                    for x in range(T):
                        put(tx, ty, x, y, 'b' if y % 4 else 'B')
                for y in range(T):
                    put(tx, ty, 0, y, 'B'); put(tx, ty, T - 1, y, 'B')
            elif t == 's':
                for y in range(T):
                    for x in range(T):
                        put(tx, ty, x, y, 'Y' if y % 4 == 0 else ('y' if y % 4 == 1 else 'G'))
            elif t == 'h':
                if at(tx, ty + 1) != 'h':
                    # the south face: bare rock with a grass lip
                    for y in range(T):
                        for x in range(T):
                            k = 'G' if (y + (x // 4)) % 4 else 'g'
                            if (x * 7 + y * 3) % 11 == 0: k = 'y'
                            if y < 3: k = 'f' if y < 2 else 'o'
                            if y == T - 1: k = 'o'
                            put(tx, ty, x, y, k)
                else:
                    grass(tx, ty, 'f', 'J', 'F', 3)
                    inside_x = 'u' in (at(tx + 1, ty), at(tx - 1, ty))
                    for y in range(T):
                        for x in range(T):
                            if at(tx - 1, ty) != 'u' and at(tx - 1, ty) != 'h' and x < 3: put(tx, ty, x, y, 'G' if x else 'o')
                            if at(tx + 1, ty) != 'u' and at(tx + 1, ty) != 'h' and x > 12: put(tx, ty, x, y, 'G' if x < 15 else 'o')
                            if at(tx, ty - 1) != 'h' and at(tx, ty - 1) != 'u' and y < 2: put(tx, ty, x, y, 'o' if y == 0 else 'G')
            elif t == 'T':
                below = at(tx, ty + 1)
                grass(tx, ty)
                cells = circle(7.5, 6.5, 7.2, 1.1)
                cells = {c for c in cells if c[1] <= 12}
                outline_blob(tx, ty, cells, lambda x, y: 'F' if (x - 5) ** 2 + (y - 4) ** 2 < 10 else ('v' if (x + y) % 5 else 'V'))
                for y in range(12, 16):
                    put(tx, ty, 7, y, 'b'); put(tx, ty, 8, y, 'B')
                    put(tx, ty, 6, y, 'o'); put(tx, ty, 9, y, 'o')
                for x in range(5, 11): put(tx, ty, x, 15, 'V')
            elif t in 'rR':
                (sand if at(tx + 1, ty) == ',' or at(tx - 1, ty) == ',' or at(tx, ty) == 'R' else grass)(tx, ty)
                cells = circle(7.5, 8.5, 7, 1.3)
                outline_blob(tx, ty, cells, lambda x, y: 'Y' if (x - 5) ** 2 + (y - 6) ** 2 < 8 else ('y' if y < 10 else 'G'))
                if t == 'R':
                    pool = circle(7.5, 9, 4, 1.3)
                    for (x, y) in pool: put(tx, ty, x, y, 'w' if y > 8 else 'c')
            elif t == 'k':
                grass(tx, ty)
                cells = circle(7.5, 8, 7, 1.2)
                outline_blob(tx, ty, cells, lambda x, y: 'v' if (x * 3 + y) % 4 else 'V')
                rnd = random.Random(tx + ty * 9)
                for _ in range(6):
                    x, y = rnd.randrange(3, 13), rnd.randrange(3, 13)
                    put(tx, ty, x, y, 'u'); put(tx, ty, x + 1, y, 'P')
            elif t == 'F':
                grass(tx, ty)
                for x in range(T):
                    put(tx, ty, x, 7, 'D'); put(tx, ty, x, 8, 'b')
                    put(tx, ty, x, 11, 'D'); put(tx, ty, x, 12, 'b')
                for x in (2, 13):
                    for y in range(4, 15):
                        put(tx, ty, x, y, 'b'); put(tx, ty, x + 1, y, 'B')
                    put(tx, ty, x, 4, 'D')
            elif t in 'HD':
                grass(tx, ty)
            elif t == 'c':
                cobbles(tx, ty)
            elif t == 'P':
                water(tx, ty)
                for y in range(T):
                    for x in range(T):
                        if y % 4 == 3: put(tx, ty, x, y, 'B')
                        else: put(tx, ty, x, y, 'D' if (x + y * 5 + tx * 3) % 13 else 'b')
                if ty % 2 == 0 and at(tx, ty + 1) not in ('P',):
                    pass
                if at(tx, ty + 1) != 'P':
                    for x in (2, 13):
                        put(tx, ty, x, T - 1, 'o'); put(tx, ty, x + 1, T - 1, 'o')
            elif t == 'O':
                cobbles(tx, ty)
                ring = circle(7.5, 8, 7, 1.0)
                outline_blob(tx, ty, ring, lambda x, y: 'Y' if (x + y // 3) % 4 else 'y')
                inner = circle(7.5, 8, 4.2, 1.0)
                for (x, y) in inner: put(tx, ty, x, y, 'X' if y > 6 else 'w')
                put(tx, ty, 6, 6, 'c'); put(tx, ty, 7, 6, 'c')
            elif t == 'L':
                cobbles(tx, ty)
                box = {(x, y) for x in range(1, 15) for y in range(2, 15)}
                outline_blob(tx, ty, box, lambda x, y: 'G' if (x // 3 + y // 3) % 2 else 'y')
                for (x, y) in {(x, y) for x in range(4, 12) for y in range(5, 11)}:
                    put(tx, ty, x, y, 'a' if (x * 3 + y) % 5 == 0 else ('r' if (x + y) % 3 else 'R'))
            elif t == 'A':
                cobbles(tx, ty)
                body = {(x, y) for x in range(2, 14) for y in range(6, 9)} | {(x, y) for x in range(5, 11) for y in range(9, 12)} \
                    | {(x, y) for x in range(4, 12) for y in range(12, 14)} | {(x, y) for x in range(0, 3) for y in range(6, 8)}
                outline_blob(tx, ty, body, lambda x, y: 'y' if y < 7 else 'G')
            elif t == 'n':
                (cobbles if at(tx - 1, ty) == 'c' or at(tx + 1, ty) == 'c' or at(tx, ty + 1) == 'c' else
                 floor if at(tx + 1, ty) == 'p' or at(tx - 1, ty) == 'p' else grass)(tx, ty)
                body = {(x, y) for x in range(3, 13) for y in range(2, 15) if not ((x in (3, 12)) and (y in (2, 14)))}
                outline_blob(tx, ty, body, lambda x, y: 'B' if y in (5, 11) else ('D' if x < 6 else 'b'))
                for x in range(4, 12): put(tx, ty, x, 3, 'B')
            elif t == 'p':
                floor(tx, ty)
            elif t == 'x':
                for y in range(T):
                    for x in range(T):
                        put(tx, ty, x, y, 'e' if (x + y) % 7 else 'o')
                # a lip of wall where it meets the floor
                if at(tx, ty - 1) in ('p', 'M', 'B', 't', 'n'):
                    for x in range(T): put(tx, ty, x, 0, 'B'); put(tx, ty, x, 1, 'b')
                if at(tx + 1, ty) in ('p', 'n', 'B'):
                    for y in range(T): put(tx, ty, T - 1, y, 'B'); put(tx, ty, T - 2, y, 'b')
                if at(tx - 1, ty) in ('p', 'n', 'B'):
                    for y in range(T): put(tx, ty, 0, y, 'B'); put(tx, ty, 1, y, 'b')
            elif t in 'Wf':
                # the back wall: dark planks, a beam along the top, a skirting board
                for y in range(T):
                    for x in range(T):
                        k = 'b' if y % 5 else 'B'
                        if (x + (y // 5) * 7) % 16 == 0: k = 'B'
                        if y < 2: k = 'B'
                        if y > 13: k = 'o' if y == 15 else 'B'
                        put(tx, ty, x, y, k)
                if t == 'W' and (tx * 5 + ty) % 4 == 1:
                    # a net hung to dry
                    for y in range(3, 12):
                        for x in range(3, 13):
                            if (x + y) % 3 == 0 or (x - y) % 3 == 0: put(tx, ty, x, y, 'y' if y > 4 else 'Y')
                if t == 'f':
                    stone = {(x, y) for x in range(1, 15) for y in range(2, 16)}
                    outline_blob(tx, ty, stone, lambda x, y: 'y' if (x // 4 + y // 3) % 2 else 'G')
                    arch = {(x, y) for x in range(4, 12) for y in range(7, 16)}
                    for (x, y) in arch: put(tx, ty, x, y, 'e')
                    for (x, y, k) in ((6, 12, 'r'), (7, 11, 'a'), (8, 12, 'r'), (9, 13, 'R'), (5, 13, 'R'), (7, 13, 'a'),
                                      (8, 10, 'r'), (6, 14, 'b'), (7, 14, 'B'), (8, 14, 'b'), (9, 14, 'B'), (7, 12, 'a'), (8, 13, 'a')):
                        put(tx, ty, x, y, k)
            elif t == 'M':
                floor(tx, ty)
                for y in range(4, 14):
                    for x in range(2, 14):
                        put(tx, ty, x, y, 'o' if x in (2, 13) or y in (4, 13) else ('R' if (x + y) % 2 else 'r'))
            elif t == 'B':
                floor(tx, ty)
                top = at(tx, ty - 1) != 'B'
                bottom = at(tx, ty + 1) != 'B'
                for y in range(T):
                    for x in range(1, 15):
                        if (top and y < 1) or (bottom and y > 14): continue
                        k = 'b'
                        if x in (1, 14): k = 'o'
                        elif top and y < 3: k = 'o' if y == 1 else 'B'
                        elif bottom and y > 12: k = 'o' if y == 14 else 'B'
                        elif top and y < 9: k = 'W' if 3 < x < 12 and 3 < y < 8 else 'd'
                        else: k = 'c' if (x + y) % 6 else 'C'
                        if top and y == 8 and 1 < x < 14: k = 'C'
                        put(tx, ty, x, y, k)
            elif t == 't':
                floor(tx, ty)
                left = at(tx - 1, ty) != 't'
                right = at(tx + 1, ty) != 't'
                for y in range(2, 14):
                    for x in range(T):
                        if (left and x < 1) or (right and x > 14): continue
                        k = 'D' if y < 10 else 'b'
                        if y in (2, 13) or (left and x == 1) or (right and x == 14): k = 'o'
                        elif y == 10: k = 'B'
                        put(tx, ty, x, y, k)
                # a bowl and a cup
                if left:
                    for (x, y) in circle(8, 6, 2.6): put(tx, ty, x, y, 'W' if y < 6 else 'Y')
                else:
                    for y in range(4, 8):
                        put(tx, ty, 6, y, 'y'); put(tx, ty, 7, y, 'Y'); put(tx, ty, 8, y, 'y')
    return img, over, put


# Buildings are painted over their H tiles as one picture each.
def paint_house(img, x0, y0, w, h, wall='d', roof='R', door_x=None):
    px = img.load()
    X0, Y0, X1, Y1 = x0 * T, y0 * T, (x0 + w) * T - 1, (y0 + h) * T - 1
    roof_h = int((Y1 - Y0) * 0.55)
    for Y in range(Y0, Y1 + 1):
        for X in range(X0, X1 + 1):
            if Y < Y0 + roof_h:
                k = roof if ((Y - Y0) % 4) else ('m' if roof == 'R' else 'B')
                if X in (X0, X1) or Y == Y0: k = 'o'
            else:
                k = wall if ((X - X0) % 8) else ('D' if wall == 'd' else 'g')
                if X in (X0, X1) or Y == Y1: k = 'o'
                if Y == Y0 + roof_h: k = 'B'
            px[X, Y] = C(k)
    # windows
    for wx in range(X0 + 6, X1 - 8, 22):
        for Y in range(Y0 + roof_h + 5, Y0 + roof_h + 11):
            for X in range(wx, wx + 7):
                px[X, Y] = C('o' if X in (wx, wx + 6) or Y in (Y0 + roof_h + 5, Y0 + roof_h + 10) else 'n')
    if door_x is not None:
        DX = door_x * T + 3
        for Y in range(Y1 - 12, Y1):
            for X in range(DX, DX + 10):
                px[X, Y] = C('o' if X in (DX, DX + 9) or Y == Y1 - 12 else ('B' if (X - DX) % 3 else 'b'))
    # chimney smoke stack
    for Y in range(Y0 - 2, Y0 + 6):
        for X in range(X1 - 12, X1 - 7):
            if Y >= Y0 - 2: px[X, Y] = C('o' if X in (X1 - 12, X1 - 8) else 'G')


# ============================================================================================== walking sprites

# 16 x 24 frames. Rows: down, up, left (right is left mirrored). Letters as palette.py, plus skin s S, hair h H,
# shirt d D, trousers n N, boots b B. '.' is see-through.
WALK = {
    ('male', 'down'): """
................
.....oooooo.....
....ohhhhhho....
...ohhhhhhhho...
...ohhHhhHhho...
...ohssssssho...
...osesssseso...
...oSssssssSo...
....oSssssSo....
.....oSSSSo.....
....odddddDo....
...odDddddDdo...
..osdDddddDdso..
..osdDddddDdso..
..oSdDddddDdSo..
...oonnnnnnoo...
....onnnnNno....
....onnnnNno....
....onno.oNo....
....onno.oNo....
....onno.oNo....
....obbo.obo....
....oBBo.oBo....
....oooo.ooo....
""",
    ('male', 'up'): """
................
.....oooooo.....
....ohhhhhho....
...ohhhhhhhho...
...ohhhhhhhho...
...ohhhhhhhho...
...ohHhhhhHho...
...oHHhhhhHHo...
....oHHHHHHo....
.....oSSSSo.....
....oddddddo....
...odddddddDo...
..osdddddddDso..
..osdddddddDso..
..oSdDddddDDSo..
...oonnnnnnoo...
....onnnnNno....
....onnnnNno....
....onno.oNo....
....onno.oNo....
....onno.oNo....
....obbo.obo....
....oBBo.oBo....
....oooo.ooo....
""",
    ('male', 'left'): """
................
.....oooooo.....
....ohhhhhhho...
...ohhhhhhhhho..
...ohhhhhhHhho..
...ossssshHhho..
...oessssshhho..
...osssssSHho...
....oSsssSSo....
.....oSSSSo.....
.....odddddo....
....oddddddDo...
....oddsddDDo...
....oddsdDDDo...
....odSSdDDDo...
.....onnnnno....
.....onnnnNo....
.....onnnnNo....
.....onnnNNo....
.....onnnNNo....
.....onnnNNo....
....obbbbBBo....
....oBBBBBBo....
....oooooooo....
""",
    ('female', 'down'): """
................
.....oooooo.....
....ohhhhhho....
...ohhhhhhhho...
...ohhHhhHhho...
...ohssssssho...
..ohsesssseshо..
..ohSssssssSho..
..ohhSssssShho..
..ohHoSSSSoHho..
...oodddddDoo...
...odDddddDdo...
..osdDddddDdso..
..osdDddddDdso..
..oSdDddddDdSo..
...oonnnnnnoo...
....onnnnNno....
....onnnnNno....
....onno.oNo....
....onno.oNo....
....onno.oNo....
....obbo.obo....
....oBBo.oBo....
....oooo.ooo....
""",
    ('female', 'up'): """
................
.....oooooo.....
....ohhhhhho....
...ohhhhhhhho...
...ohhhhhhhho...
..ohhhhhhhhhho..
..ohhHhhhhHhho..
..ohHHhhhhHHho..
..ohhhhhhhhhho..
..ohHhhhhhhHho..
...ohHhhhhHho...
...odHHHHHHdo...
..osdddddddDso..
..osdddddddDso..
..oSdDddddDDSo..
...oonnnnnnoo...
....onnnnNno....
....onnnnNno....
....onno.oNo....
....onno.oNo....
....onno.oNo....
....obbo.obo....
....oBBo.oBo....
....oooo.ooo....
""",
    ('female', 'left'): """
................
.....oooooo.....
....ohhhhhhho...
...ohhhhhhhhho..
...ohhhhhhHhho..
...ossssshHhhho.
...oessssshhhho.
...osssssSHhhho.
....oSsssSShhho.
.....oSSSSohHho.
.....odddddoHo..
....oddddddDo...
....oddsddDDo...
....oddsdDDDo...
....odSSdDDDo...
.....onnnnno....
.....onnnnNo....
.....onnnnNo....
.....onnnNNo....
.....onnnNNo....
.....onnnNNo....
....obbbbBBo....
....oBBBBBBo....
....oooooooo....
""",
}


def grid(text):
    rows = [r for r in text.strip('\n').split('\n')]
    rows = [r.replace('о', 'o') for r in rows]
    return [list(r.ljust(16, '.')[:16]) for r in rows][:24]


def step_frames(g, facing):
    """Two walking frames from a standing one: one leg lifted, then the other, with a one-pixel bob."""
    frames = []
    for lift in (0, 1):
        f = [list(r) for r in g]
        if facing in ('down', 'up'):
            cols = range(4, 8) if lift == 0 else range(8, 12)
            for x in cols:
                for y in range(16, 24):
                    f[y - 1][x] = f[y][x] if y < 23 else f[y][x]
                f[23][x] = '.'
        else:
            # side view: legs apart on one frame, together and bobbed on the other
            if lift == 0:
                for y in range(18, 24):
                    row = f[y]
                    f[y] = ['.'] + row[:-1] if y >= 18 else row
                for y in range(18, 24):
                    for x in range(9, 14):
                        f[y][x] = g[y][x - 1] if g[y][x - 1] != '.' else f[y][x]
            else:
                f = [['.'] * 16] + [list(r) for r in g[:-1]]
        frames.append(f)
    return frames


def sheet(figure, swaps):
    """3 frames across (stand, step, step), 4 rows down (down, up, left, right)."""
    img = Image.new('RGBA', (16 * 3, 24 * 4), (0, 0, 0, 0))
    cmap = {k: C(k) for k in PAL}
    cmap.update({k: hexc(v) for k, v in swaps.items()})
    for row, facing in enumerate(('down', 'up', 'left', 'right')):
        base = grid(WALK[(figure, 'left' if facing == 'right' else facing)])
        frames = [base] + step_frames(base, 'left' if facing == 'right' else facing)
        for col, f in enumerate(frames):
            for y in range(24):
                for x in range(16):
                    ch = f[y][15 - x] if facing == 'right' else f[y][x]
                    if ch != '.': img.putpixel((col * 16 + x, row * 24 + y), cmap[ch])
    return img


NPCS = {
    'npc_yenson': ('male', SKINS['olive'], HAIRS['grey'], {'d': '#6e8f8c', 'D': '#46676a', 'n': '#3a4c50', 'N': '#253236'}),
    'npc_fisher': ('male', SKINS['fair'], HAIRS['red'], {'d': '#7d6b52', 'D': '#5a4634', 'n': '#46676a', 'N': '#253236'}),
    'npc_garran': ('male', SKINS['brown'], HAIRS['black'], {'d': '#5a4634', 'D': '#3a2d24', 'n': '#38343a', 'N': '#1e1a20'}),
    'npc_ida': ('female', SKINS['light'], HAIRS['red'], {'d': '#566b3e', 'D': '#3b4d30', 'n': '#5a4634', 'N': '#3a2d24'}),
    'npc_bess': ('female', SKINS['fair'], HAIRS['grey'], {'d': '#7e5a80', 'D': '#553a5a', 'n': '#38343a', 'N': '#1e1a20'}),
}

OBJECTS = {
    'obj_sign': """
................
................
..oooooooooooo..
..oDddddddddDo..
..odbbdbbbdbdo..
..odddddddddDo..
..odbbbdbbbddo..
..oDDDDDDDDDDo..
..oooooobooooo..
.......obo......
.......obo......
.......obo......
.......obo......
......obbbo.....
......ooooo.....
................
""",
    'obj_crate': """
................
................
...oooooooooo...
...obbbbbbbbo...
...oBdDBBdDBo...
...obDdbbDdbo...
...obbDbbbDbo...
...oooooooooo...
...obbbbbbbbo...
...oBdDBBdDBo...
...obDdbbDdbo...
...obbDbbbDbo...
...oooooooooo...
....wwwwwwww....
................
................
""",
    'obj_cairn': """
................
.......oo.......
......oYyo......
.....oyYyGo.....
.....ooooooo....
....oYyyoYyGo...
....oyGGoyGGo...
...ooooooooooo..
...oYyyGoYyyGo..
...oyGGGoyGGGo..
..oooooooooooo..
..oYyyGGoYyyGGo.
..oyGGGGoyGGGGo.
..oooooooooooooo
................
................
""",
    'obj_wolf': """
................
................
...oo......oo...
..oGgo....oGgo..
..oGGgooooGGgo..
..ogGGGGGGGGgo..
..oGGaoGGoaGGo..
..oGGGGGGGGGGo..
...oGGGWWGGGo...
...ogGGeeGGgo...
....oGgmmgGo....
....ogGGGGgo....
...ogGGGGGGgo...
...oGgGGGGgGo...
...ooo.oo.ooo...
................
""",
    'obj_gulls': """
................
................
................
....ooo.........
...oWWWo........
..oyWWWWo.......
...oWWWWWoo.....
....ooWWWyyo....
......oooooo....
.........ooo....
........oWWWo...
.......oWWWWyo..
........oWWWyyo.
.........oooooo.
................
................
""",
}


def object_sprite(text):
    rows = [list(r.ljust(16, '.')[:16]) for r in text.strip('\n').split('\n')]
    img = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    for y, r in enumerate(rows[:16]):
        for x, ch in enumerate(r):
            if ch != '.': img.putpixel((x, y), C(ch))
    return img


# ============================================================================================== main

def main():
    data_path = os.path.join(GAME, 'Resources', 'Data', 'data.json')
    data = json.load(open(data_path))
    data['areas'] = []
    data['tiles'] = [dict({'key': k, 'name': n}, **({'solid': True} if solid else {}), **({'road': True} if road else {}))
                     for k, n, solid, road in TILES]
    out = os.path.join(SPR, 'World')
    os.makedirs(out, exist_ok=True)
    for make in AREAS:
        area = make()
        extra_doors = [tuple(d) for d in area.pop('doors', [])]
        data['areas'].append(area)
        img, over, _ = tile_painter(area['rows'])
        rows = area['rows']
        # buildings: find each block of H tiles and paint a house over it
        seen = set()
        for y in range(len(rows)):
            for x in range(len(rows[0])):
                if rows[y][x] not in 'H' or (x, y) in seen: continue
                x1 = x
                while x1 + 1 < len(rows[0]) and rows[y][x1 + 1] == 'H': x1 += 1
                y1 = y
                while y1 + 1 < len(rows) and rows[y1 + 1][x] == 'H': y1 += 1
                for yy in range(y, y1 + 1):
                    for xx in range(x, x1 + 1): seen.add((xx, yy))
                door = next((xx for xx in range(x, x1 + 1) if rows[y1][xx] == 'D' or (xx, y1) in extra_doors), None)
                paint_house(img, x, y, x1 - x + 1, y1 - y + 1, wall='d' if (x * 3 + y) % 2 == 0 else 'y',
                            roof='B' if (x + y) % 3 else 'R', door_x=door)
        img.save(os.path.join(out, area['id'] + '.png'))
        over.save(os.path.join(out, area['id'] + '_over.png'))
    open(data_path, 'w').write(json.dumps(data, indent=2, ensure_ascii=False) + '\n')

    walk = os.path.join(SPR, 'Walk')
    os.makedirs(walk, exist_ok=True)
    n = 0
    for fig in ('male', 'female'):
        for skin, sramp in SKINS.items():
            for hair, hramp in HAIRS.items():
                sw = dict(zip('sSqQ', sramp))
                sw.update(dict(zip('hH', hramp)))
                sheet(fig, sw).save(os.path.join(walk, 'walk_%s_%s_%s.png' % (fig, skin, hair.replace(' ', '_'))))
                n += 1
    for name, (fig, sramp, hramp, cloth) in NPCS.items():
        sw = dict(zip('sSqQ', sramp)); sw.update(dict(zip('hH', hramp))); sw.update(cloth)
        sheet(fig, sw).save(os.path.join(walk, name + '.png'))
    for name, text in OBJECTS.items():
        object_sprite(text).save(os.path.join(out, name + '.png'))
    print('world: %d areas, %d walking sheets' % (len(AREAS), n + len(NPCS)))


if __name__ == '__main__':
    main()
