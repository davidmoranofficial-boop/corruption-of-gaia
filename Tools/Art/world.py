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
  o  flowers        q  ore seam (in a cliff face)
"""
import json, math, os, random
from PIL import Image
from palette import PAL, SKINS, HAIRS
import terrain

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
    ('q', 'Ore seam', True, False),
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
    """The coast road: from Yenson's shack, and the copse south of it, north along the shore to Skelmed's gate."""
    W, H = 32, 120
    g = [['.'] * W for _ in range(H)]

    def setc(x, y, t):
        if 0 <= x < W and 0 <= y < H: g[y][x] = t

    def rect(x0, y0, x1, y1, t, keep=None):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if keep is None or (0 <= y < H and 0 <= x < W and g[y][x] in keep): setc(x, y, t)

    rnd = random.Random(11)
    # the shore: wavy, with a bay biting inland halfway down
    coast = lambda y: int(round(4 + 1.6 * math.sin(y / 6.5) + 5.5 * math.exp(-((y - 60) / 6.5) ** 2)))
    road = lambda y: int(round(16 + 3.0 * math.sin(y / 9.0 + 0.6) + 1.6 * math.sin(y / 21.0) + 2.0 * math.exp(-((y - 60) / 8.0) ** 2)))
    SHACK_Y = 92            # the bottom row of the shack, where its door is
    ROAD_END = SHACK_Y + 1
    DOOR_X = 19

    for y in range(H):
        c = coast(y)
        for x in range(W):
            if x < c: g[y][x] = '~'
            elif x < c + 2 + (y % 7 == 3): g[y][x] = ','

    # the wood along the east side, ragged
    for y in range(H):
        edge = 27 + int(round(1.5 * math.sin(y / 4.3) + rnd.uniform(-0.4, 0.4)))
        for x in range(edge, W): g[y][x] = 'T'

    # the stream, from the wood down to the sea
    stream_y = lambda x: int(round(12 + 1.2 * math.sin(x / 3.5)))
    for x in range(W):
        y0 = stream_y(x)
        for y in (y0, y0 + 1):
            if g[y][x] != '~': g[y][x] = 'w'

    # the headland: a rounded rise ringed by a low cliff, steps up its west side
    hcx, hcy = 23.0, 33
    for y in range(26, 41):
        for x in range(17, 30):
            d = ((x - hcx) / 3.9) ** 2 + ((y - hcy) / 4.9) ** 2
            if d <= 1.0: setc(x, y, 'h')
            if d <= 0.5: setc(x, y, 'u')
    steps_x = next(x for x in range(W) if g[33][x] == 'h')
    setc(steps_x, 33, 's')

    # long grass
    for (x0, y0, x1, y1) in ((19, 17, 24, 22), (8, 18, 11, 22), (9, 28, 12, 34), (20, 46, 25, 52), (10, 70, 13, 76),
                             (19, 64, 23, 70), (17, 80, 22, 85)):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if g[y][x] == '.' and not ((x in (x0, x1)) and (y in (y0, y1))): g[y][x] = 'g'

    # trees, alone and in clumps
    for (x, y) in ((20, 6), (21, 6), (20, 7), (11, 24), (12, 25), (23, 24), (24, 25), (10, 42), (11, 43), (22, 56),
                   (23, 56), (23, 57), (7, 9), (12, 16), (24, 10), (18, 27), (13, 48), (14, 79), (21, 77), (22, 78),
                   (23, 76), (9, 52), (25, 87), (26, 88), (7, 86), (8, 87)):
        if g[y][x] in '.g': g[y][x] = 'T'
    for _ in range(45):
        x, y = rnd.randrange(W), rnd.randrange(5, 94)
        if g[y][x] == '.': g[y][x] = 'o'

    # brambles on the stream's south bank
    for x in (19, 20, 21, 22):
        y = stream_y(x) + 2
        if g[y][x] in '.og': g[y][x] = 'k'

    # tide pools in the bay
    pools = []
    for (dx, y, t) in ((0, 60, 'r'), (1, 60, 'R'), (1, 61, 'r'), (0, 62, 'R'), (2, 62, 'r'), (1, 63, 'r'), (0, 59, 'r')):
        x = coast(y) + dx
        g[y][x] = t
        if t == 'R': pools.append((x, y))

    # rocks here and there
    for (x, y) in ((8, 13), (17, 38), (25, 44), (14, 54), (7, 27), (25, 16), (12, 82), (19, 90)):
        if g[y][x] in '.o': g[y][x] = 'r'

    # Skelmed's fence across the top, the gate on the road, the first roofs beyond
    rect(0, 0, W - 1, 3, '.')
    for x in range(W):
        if g[4][x] != '~': g[4][x] = 'F'
    rect(2, 0, 7, 2, 'H')
    rect(19, 0, 24, 2, 'H')
    rect(27, 0, W - 1, 3, 'T')

    # the copse south of the shack: trees in clumps round a spring and a clearing
    crnd = random.Random(5)
    for y in range(ROAD_END + 2, H):
        for x in range(coast(y) + 3, W):
            if g[y][x] in '.go' and crnd.random() < (0.32 if y < H - 4 else 0.95):
                g[y][x] = 'T'
    spring = [(9, 104), (10, 104), (11, 104), (9, 105), (10, 105), (11, 105), (10, 103)]
    clear = [(8, 101, 15, 108), (17, 106, 24, 113), (18, 93, 20, 101), (12, 99, 19, 101), (17, 99, 19, 106)]
    for (x0, y0, x1, y1) in clear:
        rect(x0, y0, x1, y1, '.', keep='T.go')
    for (x, y) in spring: setc(x, y, 'w')
    # a trodden path from the shack down to the spring and on to the clearing
    for y in range(ROAD_END, 101): setc(DOOR_X, y, '=')
    for x in range(12, DOOR_X): setc(x, 100, '=')
    for y in range(100, 107): setc(18, y, '=')

    # Yenson's shack, a path from its door to the road
    rect(DOOR_X - 2, SHACK_Y - 2, DOOR_X + 2, SHACK_Y, 'H')
    setc(DOOR_X, SHACK_Y, 'D')

    # the road, two tiles wide, from the shack to the gate, with a bridge over the stream
    for y in range(0, ROAD_END + 1):
        x = road(y)
        for xx in (x, x + 1):
            setc(xx, y, 'b' if g[y][xx] == 'w' else '=')
    for x in range(road(ROAD_END), DOOR_X + 1):
        setc(x, ROAD_END, '=')
    # the path up to the headland steps
    for x in range(road(33) + 2, steps_x):
        setc(x, 33, '=')

    rows = [''.join(r) for r in g]
    rx = road(41)
    north = [0, 5, W - 1, ROAD_END - 4]            # fights happen north of the shack, not round it or in the copse
    area = {
        'id': 'coast_road',
        'name': 'The coast road',
        'minutes': 2,
        'eventChance': 0,
        'fightChance': 6,          # % per step: a fight, the old way, anywhere you walk (less on the road)
        'fightGap': 10,            # at least this many steps between fights
        'spots': [
            {'id': 'shack', 'x': DOOR_X, 'y': ROAD_END, 'facing': 'down'},
            {'id': 'skelmed', 'x': road(5), 'y': 5, 'facing': 'down'},
            {'id': 'pools', 'x': pools[0][0] + 1, 'y': pools[0][1], 'facing': 'left'},
            {'id': 'headland', 'x': 23, 'y': 33, 'facing': 'down'},
            {'id': 'stream', 'x': 10, 'y': 14, 'facing': 'left'},
            {'id': 'spring', 'x': 12, 'y': 104, 'facing': 'left'},
        ],
        'objects': [
            {'id': 'sign_shack', 'kind': 'sign', 'sprite': 'obj_sign', 'x': road(ROAD_END) - 1, 'y': ROAD_END - 1,
             'text': 'North: Skelmed. South: Yenson. Mind the tide.'},
            {'id': 'sign_gate', 'kind': 'sign', 'sprite': 'obj_sign', 'x': road(6) + 2, 'y': 6,
             'text': 'SKELMED. Nets, rope, fish. Mind the dogs. The word "dogs" has been scratched out and "wolves" written over it.'},
            {'id': 'pools', 'kind': 'spot', 'sprite': '', 'x': pools[0][0], 'y': pools[0][1], 'scene': 'pools'},
            {'id': 'crate', 'kind': 'item', 'sprite': 'obj_crate', 'x': coast(80) + 1, 'y': 80, 'scene': 'ev_crate', 'once': True},
            {'id': 'fisher', 'kind': 'npc', 'sprite': 'npc_fisher', 'x': coast(70) + 1, 'y': 70, 'facing': 'left',
             'scene': 'ev_fisher', 'once': True, 'when': ['!night']},
            {'id': 'lookout', 'kind': 'spot', 'sprite': 'obj_cairn', 'x': 24, 'y': 31, 'scene': 'lookout_climb'},
            {'id': 'wolf', 'kind': 'npc', 'sprite': 'obj_wolf', 'x': rx, 'y': 41, 'facing': 'down', 'when': ['!flag:dog_met'], 'scene': 'dog'},
            {'id': 'gulls', 'kind': 'item', 'sprite': 'obj_gulls', 'x': coast(21) + 1, 'y': 21, 'scene': 'ev_taint', 'once': True},
            {'id': 'log1', 'kind': 'item', 'sprite': 'obj_log', 'x': 14, 'y': 103, 'scene': 'deadfall', 'once': True},
            {'id': 'log2', 'kind': 'item', 'sprite': 'obj_log', 'x': 9, 'y': 107, 'scene': 'deadfall', 'once': True},
            {'id': 'log3', 'kind': 'item', 'sprite': 'obj_log', 'x': 16, 'y': 98, 'scene': 'deadfall', 'once': True},
            {'id': 'log4', 'kind': 'item', 'sprite': 'obj_log', 'x': 22, 'y': 107, 'scene': 'deadfall', 'once': True},
            {'id': 'log5', 'kind': 'item', 'sprite': 'obj_log', 'x': 19, 'y': 112, 'scene': 'deadfall', 'once': True},
            {'id': 'boar', 'kind': 'npc', 'sprite': 'obj_boar', 'x': 21, 'y': 110, 'facing': 'left', 'scene': 'boar',
             'when': ['quest:boar', '!flag:boar_killed']},
        ],
        'zones': [
            {'x0': DOOR_X, 'y0': SHACK_Y, 'x1': DOOR_X, 'y1': SHACK_Y, 'to': 'hut_door'},
            {'x0': road(4) - 1, 'y0': 0, 'x1': road(4) + 2, 'y1': 4, 'to': 'village_gate'},
            # the wolf waits on the road below the headland; the boar charges when you get close
            {'x0': 0, 'y0': 42, 'x1': W - 1, 'y1': 43, 'scene': 'dog', 'when': ['!flag:dog_met']},
            {'x0': 18, 'y0': 107, 'x1': 24, 'y1': 113, 'scene': 'boar', 'when': ['quest:boar', '!flag:boar_killed', '!flag:boar_seen']},
        ],
        'uses': [
            {'tile': 'w', 'scene': 'stream_drink'},
            {'tile': 'k', 'scene': 'brambles'},
        ],
        'events': [
            # Fights start straight away, Final Fantasy style. win is where you go after winning.
            {'enemy': 'crab', 'win': 'ev_crab_won', 'kind': 'fight', 'weight': 3, 'terrain': ',', 'area': north},
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

    # the lane north out of the village, up into the hills
    for y in range(0, 10):
        setc(12, y, '='); setc(13, y, '=')

    # the net shed down by the harbour
    rect(8, 15, 10, 16, 'H')

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
        if g[y][x] == '.' and not (11 <= x <= 14 and y < 3): g[y][x] = 'o'

    rows = [''.join(r) for r in g]
    return {
        'id': 'skelmed',
        'name': 'Skelmed',
        'minutes': 1, 'eventChance': 0, 'fightChance': 0, 'fightGap': 10,
        'camp': False,
        'doors': doors,
        'spots': [
            {'id': 'village_gate', 'x': 17, 'y': 24, 'facing': 'up'},
            {'id': 'skelmed_north', 'x': 12, 'y': 2, 'facing': 'down'},
        ],
        'objects': [
            {'id': 'garran', 'kind': 'npc', 'sprite': 'npc_garran', 'x': 27, 'y': 22, 'facing': 'right', 'scene': 'smith'},
            # the hunters' lodge is the long house at the top; its chopping block and water butt stand outside
            {'id': 'ida', 'kind': 'npc', 'sprite': 'npc_ida', 'x': 19, 'y': 8, 'facing': 'down', 'scene': 'ida'},
            {'id': 'block', 'kind': 'item', 'sprite': 'obj_block', 'x': 21, 'y': 6, 'scene': 'chop'},
            {'id': 'butt', 'kind': 'item', 'sprite': 'obj_butt', 'x': 15, 'y': 7, 'scene': 'butt'},
            # the guild post is the house east of the square
            {'id': 'mira', 'kind': 'npc', 'sprite': 'npc_mira', 'x': 30, 'y': 14, 'facing': 'left', 'scene': 'mira'},
            # the net shed fire, once it's started
            {'id': 'fire1', 'kind': 'item', 'sprite': 'obj_fire', 'x': 9, 'y': 14, 'scene': 'fire1',
             'when': ['flag:fire_started', '!flag:fire1_out']},
            {'id': 'fire2', 'kind': 'item', 'sprite': 'obj_fire', 'x': 11, 'y': 16, 'scene': 'fire2',
             'when': ['flag:fire_started', '!flag:fire2_out']},
            {'id': 'fire3', 'kind': 'item', 'sprite': 'obj_fire', 'x': 7, 'y': 16, 'scene': 'fire3',
             'when': ['flag:fire_started', '!flag:fire3_out']},
            {'id': 'orlen_fire', 'kind': 'npc', 'sprite': 'npc_orlen', 'x': 13, 'y': 15, 'facing': 'left', 'scene': 'orlen_at_fire',
             'when': ['flag:fire_started', '!flag:fire_out']},
            {'id': 'bess', 'kind': 'npc', 'sprite': 'npc_bess', 'x': 9, 'y': 8, 'facing': 'down', 'scene': 'bess'},
            {'id': 'dockhand', 'kind': 'npc', 'sprite': 'npc_fisher', 'x': 2, 'y': 13, 'facing': 'left', 'scene': 'dockhand'},
            {'id': 'board', 'kind': 'sign', 'sprite': 'obj_sign', 'x': 15, 'y': 9,
             'text': 'The noticeboard. One notice, months old: BY ORDER OF THE CROWN, THE HARBOUR LEVY IS RAISED TO ONE HALF.'},
        ],
        'zones': [
            {'x0': 17, 'y0': 26, 'x1': 18, 'y1': 27, 'to': 'skelmed'},
            {'x0': 12, 'y0': 0, 'x1': 13, 'y1': 0, 'to': 'hills_gate'},
            # Bess sees the wolf bite as you walk in, and shows you how to treat it
            {'x0': 14, 'y0': 22, 'x1': 21, 'y1': 23, 'scene': 'bess_patch', 'when': ['untreated:bite', '!flag:patch_lesson']},
        ],
        'uses': [
            {'tile': 'O', 'scene': 'well_drink'},
            {'tile': 'A', 'scene': 'smith'},
            {'tile': 'L', 'scene': 'smith'},
        ],
        'events': [],
        'rows': rows,
    }


# ============================================================================================== the hills

def hills():
    """The hills north of Skelmed: Rook's snare line in the woods to the west, the stream, old Orlen's cottage,
    and Garran's old quarry at the top."""
    W, H = 34, 40
    g = [['.'] * W for _ in range(H)]
    rnd = random.Random(31)

    def setc(x, y, t):
        if 0 <= x < W and 0 <= y < H: g[y][x] = t

    def rect(x0, y0, x1, y1, t, keep=None):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if keep is None or (0 <= y < H and 0 <= x < W and g[y][x] in keep): setc(x, y, t)

    # the path from the village (bottom) winding up to the quarry (top)
    path = lambda y: int(round(16 + 2.5 * math.sin(y / 6.0) - 1.5 * math.sin(y / 13.0)))

    # woods: thick round the edges, scattered inside, thicker in the west where the snares are
    for y in range(H):
        for x in range(W):
            edge = min(x, W - 1 - x, y)
            p = 0.95 if edge < 2 else (0.30 if x < 12 else 0.12)
            if rnd.random() < p: g[y][x] = 'T'
    rect(0, H - 1, W - 1, H - 1, 'T')

    # the quarry: a bite out of the hillside, high ground behind a rock face with seams of ore in it
    for y in range(1, 9):
        for x in range(8, 27):
            d = ((x - 17) / 9.5) ** 2 + ((y - 1) / 7.5) ** 2
            if d <= 1.0: setc(x, y, 'h')
            if d <= 0.55: setc(x, y, 'u')
    for y in range(9, 13):                # the quarry floor: gravel and spoil, spilling out from under the face
        for x in range(8, 27):
            if ((x - 17) / 8.2) ** 2 + ((y - 8.6) / 3.3) ** 2 <= 1 and g[y][x] not in 'hqu': setc(x, y, ',')
    for x in range(12, 23):
        if g[8][x] == 'h' and x % 3 != 1: setc(x, 8, 'q')
    for (x, y) in ((11, 10), (23, 10), (20, 11), (13, 11), (24, 9)):
        setc(x, y, 'r')

    # the stream, from the woods in the east down to the west
    stream_y = lambda x: int(round(19 + 1.4 * math.sin(x / 4.0)))
    for x in range(W):
        y0 = stream_y(x)
        for y in (y0, y0 + 1): setc(x, y, 'w')

    # old Orlen's cottage on the slope east of the path, herbs round it
    rect(22, 26, 27, 29, '.')
    rect(21, 30, 28, 33, '.')
    rect(23, 26, 26, 29, 'H')
    for (x, y) in ((22, 30), (27, 30), (28, 31), (21, 32), (28, 33), (22, 33)):
        setc(x, y, 'o')
    for y in range(30, 34): setc(24, y, '=')

    # clearings along the snare line in the west woods
    for (x0, y0, x1, y1) in ((3, 24, 8, 28), (4, 12, 9, 16), (2, 6, 6, 10), (6, 28, 14, 30), (6, 16, 9, 24), (4, 9, 8, 13)):
        rect(x0, y0, x1, y1, '.', keep='T.')
    # long grass
    for (x0, y0, x1, y1) in ((4, 25, 7, 27), (25, 13, 29, 16), (19, 33, 21, 36), (3, 13, 5, 15)):
        rect(x0, y0, x1, y1, 'g', keep='.')
    for _ in range(25):
        x, y = rnd.randrange(W), rnd.randrange(12, H - 1)
        if g[y][x] == '.': g[y][x] = 'o'
    for (x, y) in ((28, 22), (10, 33), (13, 24)):
        if g[y][x] in '.o': g[y][x] = 'r'

    # the path itself, two wide, with a footbridge over the stream; a track off it to Orlen's door
    # and a narrower one west along the snare line
    prev = None
    for y in range(9, H):
        x = path(16) if 16 <= y <= 22 else path(y)
        lo, hi = (x, x + 1) if prev is None else (min(x, prev), max(x, prev) + 1)
        for xx in range(lo, hi + 1):
            setc(xx, y, 'b' if g[y][xx] == 'w' else '=')
        prev = x
    for x in range(path(31) + 2, 25): setc(x, 31, '=')
    for x in range(5, path(29)):
        if g[29][x] != 'w': setc(x, 29, '=')
    for y in range(13, 29): setc(7, y, 'b' if g[y][7] == 'w' else '=')
    for x in range(4, 8): setc(x, 13, '=')
    # the bottom gate: the lane down to the village
    gx = path(H - 1)
    for y in range(H - 3, H): setc(gx, y, '='); setc(gx + 1, y, '=')

    # the den: a hollow under rocks in the north-east corner
    for (x, y) in ((27, 2), (31, 2), (31, 4), (30, 2)):
        setc(x, y, 'r')

    rows = [''.join(r) for r in g]
    snares = [(4, 26), (8, 14), (3, 8)]
    return {
        'id': 'skelmed_hills',
        'name': 'The hills above Skelmed',
        'minutes': 2, 'eventChance': 0, 'fightChance': 0, 'fightGap': 14,
        'doors': [(24, 29)],
        'spots': [
            {'id': 'hills_gate', 'x': gx, 'y': H - 3, 'facing': 'up'},
        ],
        'objects': [
            {'id': 'orlen', 'kind': 'npc', 'sprite': 'npc_orlen', 'x': 26, 'y': 31, 'facing': 'left', 'scene': 'orlen',
             'when': ['!flag:fire_started|flag:fire_out']},
            {'id': 'brazier', 'kind': 'item', 'sprite': 'obj_brazier', 'x': 22, 'y': 30, 'scene': 'brazier',
             'when': ['!flag:brazier_lit']},
            {'id': 'brazier_lit', 'kind': 'item', 'sprite': 'obj_brazier_lit', 'x': 22, 'y': 30, 'scene': 'brazier',
             'when': ['flag:brazier_lit']},
            {'id': 'rook', 'kind': 'npc', 'sprite': 'npc_rook', 'x': 9, 'y': 29, 'facing': 'right', 'scene': 'rook',
             'when': ['!follower:rook', '!flag:dead_rook', '!flag:rook_escort']},
            # the pack leader's den, in the north-east corner
            {'id': 'alpha', 'kind': 'npc', 'sprite': 'obj_wolf_alpha', 'x': 29, 'y': 3, 'facing': 'down', 'scene': 'den',
             'when': ['quest:pack', '!flag:alpha_dead']},
            {'id': 'snare1', 'kind': 'item', 'sprite': 'obj_snare', 'x': snares[0][0], 'y': snares[0][1], 'scene': 'snare1',
             'when': ['!flag:snare1_done']},
            {'id': 'snare2', 'kind': 'item', 'sprite': 'obj_snare', 'x': snares[1][0], 'y': snares[1][1], 'scene': 'snare2',
             'when': ['!flag:snare2_done']},
            {'id': 'snare3', 'kind': 'item', 'sprite': 'obj_snare', 'x': snares[2][0], 'y': snares[2][1], 'scene': 'snare3',
             'when': ['!flag:snare3_done']},
            # herbs for wounds: yarrow by the path, willow by the stream (both come back the next day)
            {'id': 'yarrow1', 'kind': 'item', 'sprite': 'obj_yarrow', 'x': 19, 'y': 23, 'scene': 'herb_yarrow1', 'when': ['!flag:yarrow1_today']},
            {'id': 'yarrow2', 'kind': 'item', 'sprite': 'obj_yarrow', 'x': 5, 'y': 17, 'scene': 'herb_yarrow2', 'when': ['!flag:yarrow2_today']},
            {'id': 'willow', 'kind': 'item', 'sprite': 'obj_willow', 'x': 12, 'y': 21, 'scene': 'herb_willow', 'when': ['!flag:willow_today']},
            {'id': 'glint', 'kind': 'item', 'sprite': 'obj_glint', 'x': 21, 'y': 10, 'scene': 'quarry_gem',
             'when': ['!flag:gem_found']},
            {'id': 'sign_hills', 'kind': 'sign', 'sprite': 'obj_sign', 'x': path(36) + 2, 'y': 36,
             'text': 'Up: the old quarry. Right: O. Vey. Left: SNARES, KEEP OFF. (Rook)'},
        ],
        'zones': [
            {'x0': gx, 'y0': H - 1, 'x1': gx + 1, 'y1': H - 1, 'to': 'skelmed_north'},
            {'x0': 26, 'y0': 2, 'x1': 31, 'y1': 6, 'scene': 'den', 'when': ['quest:pack', '!flag:alpha_dead', '!flag:den_found']},
        ],
        'uses': [
            {'tile': 'w', 'scene': 'hill_stream'},
            {'tile': 'q', 'scene': 'quarry_dig'},
        ],
        'events': [],   # no random wolves (David, 19 Oct): only the scripted one on the road
        'rows': rows,
    }


AREAS = [coast_road, yenson_hut, skelmed, hills]

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


# Buildings are painted over their H tiles as one picture each: a pitched roof seen from the front and above,
# the front wall with its door and windows, a chimney, and a shadow cast down and to the right.
ROOFS = {
    'slate': ('c', 'C', 'n', 'N'),      # light, mid, dark, darkest
    'thatch': ('a', 'A', 'b', 'B'),
    'tile': ('r', 'R', 'm', 'o'),
}
WALLS = {
    'plank': ('d', 'D', 'b'),
    'stone': ('Y', 'y', 'G'),
}


def paint_house(img, x0, y0, w, h, wall='plank', roof='thatch', door_x=None):
    px = img.load()
    X0, Y0, X1, Y1 = x0 * T, y0 * T, (x0 + w) * T - 1, (y0 + h) * T - 1
    rl, rm, rd, rx = ROOFS[roof]
    wl, wm, wd = WALLS[wall]
    roof_h = int((Y1 - Y0 + 1) * 0.58)
    RY = Y0 + roof_h                      # where the roof's front edge meets the wall

    def put(X, Y, k):
        if 0 <= X < img.width and 0 <= Y < img.height: px[X, Y] = C(k)

    def darken(X, Y):
        if 0 <= X < img.width and 0 <= Y < img.height:
            r, g, b, a = px[X, Y]
            px[X, Y] = (int(r * 0.62), int(g * 0.62), int(b * 0.7), a)

    # shadow on the ground, down and to the right
    for Y in range(Y0 + 6, Y1 + 5):
        for X in range(X1 + 1, X1 + 5):
            darken(X, Y)
    for Y in range(Y1 + 1, Y1 + 4):
        for X in range(X0 + 3, X1 + 5):
            darken(X, Y)

    # the front wall
    for Y in range(RY, Y1 + 1):
        for X in range(X0 + 1, X1):
            if wall == 'plank':
                k = wd if (X - X0) % 6 == 0 else (wl if (X - X0) % 6 == 1 else wm)
            else:
                row = (Y - RY) // 4
                k = wd if (Y - RY) % 4 == 3 or (X - X0 + row * 3) % 8 == 0 else (wl if (X + Y) % 5 == 0 else wm)
            if Y <= RY + 2: k = 'o' if Y == RY else 'B'      # the shadow under the eaves
            if Y >= Y1 - 1: k = 'g' if wall == 'stone' else 'B'
            put(X, Y, k)
        put(X0, Y, 'o'); put(X1, Y, 'o')
    for X in range(X0, X1 + 1): put(X, Y1, 'o')

    # the roof: courses of shingles or straw, lit from the top left, a ridge along the top
    for Y in range(Y0, RY + 1):
        for X in range(X0 - 1, X1 + 2):
            t = (Y - Y0) / max(1, roof_h)
            course = (Y - Y0) // 3
            k = rm
            if (Y - Y0) % 3 == 2: k = rd
            elif roof == 'thatch' and (X + course * 2) % 5 == 0: k = rl
            elif roof != 'thatch' and (X - X0 + (course % 2) * 3) % 6 == 0: k = rd
            if t < 0.18: k = rl if (Y - Y0) % 3 != 2 else rm
            if X >= X1 - 2: k = rd
            if X in (X0 - 1, X1 + 1) or Y == Y0 or Y == RY: k = 'o'
            put(X, Y, k)
    for X in range(X0 + 1, X1): put(X, Y0 + 1, rl)

    # windows with shutters and sills
    windows = []
    span = list(range(X0 + 7, X1 - 9, 20))
    for wx in span:
        if door_x is not None and abs(wx + 4 - (door_x * T + 8)) < 14: continue
        windows.append(wx)
    wy = RY + 5
    for wx in windows:
        for Y in range(wy, wy + 7):
            for X in range(wx, wx + 8):
                k = 'n'
                if X in (wx, wx + 7) or Y in (wy, wy + 6): k = 'o'
                elif X == wx + 4: k = 'b'
                elif (X, Y) in ((wx + 1, wy + 1), (wx + 5, wy + 1)): k = 'c'
                elif Y >= wy + 4: k = 'N'
                put(X, Y, k)
        for Y in range(wy, wy + 7):
            put(wx - 2, Y, 'B'); put(wx - 1, Y, 'b'); put(wx + 8, Y, 'b'); put(wx + 9, Y, 'B')
        for X in range(wx - 1, wx + 9): put(X, wy + 7, 'd')

    # the door, with a step in front
    if door_x is not None:
        DX = door_x * T + 3
        top = Y1 - 13
        for Y in range(top, Y1):
            for X in range(DX, DX + 10):
                k = 'B' if (X - DX) % 3 == 0 else 'b'
                if X in (DX, DX + 9) or Y == top: k = 'o'
                if Y == top + 1 and X in (DX + 1, DX + 8): k = 'o'
                put(X, Y, k)
        put(DX + 7, top + 7, 'a')
        for X in range(DX - 1, DX + 11):
            put(X, Y1, 'y'); put(X, Y1 + 1, 'G')

    # chimney
    cx = X1 - 12
    for Y in range(Y0 - 5, Y0 + 7):
        for X in range(cx, cx + 6):
            k = 'G' if (Y + X) % 4 else 'y'
            if X in (cx, cx + 5) or Y == Y0 - 5: k = 'o'
            if Y == Y0 - 4 and X not in (cx, cx + 5): k = 'g'
            put(X, Y, k)


# ============================================================================================== walking sprites

# 16 x 24 frames, three across (standing, step, other step), four rows down (down, up, left, right; right is left
# mirrored). A frame is put together from a body (rows 10 down, one per frame), a head (hair style) dropped onto it,
# and then any extras a character wears (an apron, a robe, a bow on the back). The dark outline is added last, all
# the way round, so the extras change the silhouette too.
# Letters as palette.py, plus: skin s S, hair h H, shirt d D, trousers n N, boots b B (the swaps), sleeves 1 2 (shirt
# colours unless a character's arms are bare). '.' is see-through.

WALK_BODY = {
    'down': ["""
....ddddddDD....
...1dddddddD2...
...1odddddDo2...
...1odddddDo2...
...sobbaabboS...
...sonnnnnNoS...
.....nnnnnN.....
.....nN..nN.....
.....nN..nN.....
.....nN..nN.....
.....nN..nN.....
.....bB..bB.....
....bbB..bbB....
""", """
....ddddddDD....
...1dddddddD2...
...1odddddDo2...
...1odddddDoS...
...sobbaabboS...
...sonnnnnNo....
.....nnnnnN.....
.....nN..nN.....
.....nN..nN.....
.....nN..nN.....
.....bB..nN.....
....bbB..bB.....
.........bbB....
""", """
....ddddddDD....
...1dddddddD2...
...1odddddDo2...
...sodddddDo2...
...sobbaabboS...
....onnnnnNoS...
.....nnnnnN.....
.....nN..nN.....
.....nN..nN.....
.....nN..nN.....
.....nN..bB.....
.....bB..bbB....
....bbB.........
"""],
    'up': ["""
....ddddddDD....
...1dddddddD2...
...1odddddDo2...
...1odddddDo2...
...sobbbbbboS...
...sonnnnnNoS...
.....nnnnnN.....
.....nN..nN.....
.....nN..nN.....
.....nN..nN.....
.....nN..nN.....
.....bB..bB.....
....bbB..bbB....
""", """
....ddddddDD....
...1dddddddD2...
...1odddddDo2...
...SodddddDo2...
...sobbbbbboS...
....onnnnnNoS...
.....nnnnnN.....
.....nN..nN.....
.....nN..nN.....
.....nN..nN.....
.....bB..nN.....
....bbB..bB.....
.........bbB....
""", """
....ddddddDD....
...1dddddddD2...
...1odddddDo2...
...1odddddDoS...
...sobbbbbboS...
...sonnnnnNo....
.....nnnnnN.....
.....nN..nN.....
.....nN..nN.....
.....nN..nN.....
.....nN..bB.....
.....bB..bbB....
....bbB.........
"""],
    'left': ["""
.....dddddD.....
.....dd22dD.....
.....dd22dD.....
.....dd22dD.....
.....bbsSbB.....
.....nnsSnN.....
.....nnnnnN.....
......nnNN......
......nnNN......
......nnNN......
......nnNN......
......bbBB......
.....bbbbB......
""", """
................
.....dddddD.....
.....dd22dD.....
.....ddd22d.....
.....bbbb22S....
.....nnnnnsS....
.....nnnnnN.....
.....nN..NN.....
....nN....NN....
....nN....NN....
...bbB.....BB...
...bbB.....BB...
""", """
................
.....dddddD.....
.....d22ddD.....
....22ddddD.....
...S2bbbbbB.....
...sSnnnnnN.....
.....nnnnnN.....
.....NN..nn.....
....NN....nn....
....NN....nn....
...BBB.....bb...
...BBB.....bb...
"""],
}
WALK_BOB = {'down': (0, 0, 0), 'up': (0, 0, 0), 'left': (0, 1, 1)}   # how far the upper body drops in each frame

# heads: rows 0 down, drawn over the body
WALK_HEAD = {
    ('short', 'down'): """
................
................
.....hhhhhh.....
....hhhhhhhh....
....hhhhhhhH....
....hhhhhhHH....
....hhshhshH....
....ssesseSS....
....ssssssSS....
.....SssssS.....
""",
    ('short', 'up'): """
................
................
.....hhhhhh.....
....hhhhhhhh....
....hhhhhhhH....
....hhhhhhhH....
....hhhhhhHH....
....hhhhhhHH....
....hHhhhHHH....
.....HSSSSH.....
""",
    ('short', 'left'): """
................
................
.....hhhhhh.....
....hhhhhhhh....
....hhhhhhhhH...
....hhhhhhhHH...
....sshhhhhHH...
....sesSShhHH...
...sssssSShH....
....SssssH......
""",
    ('long', 'down'): """
................
................
.....hhhhhh.....
....hhhhhhhh....
...hhhhhhhhhH...
...hhhhhhhhHH...
...hhshhhhshH...
...hssesseSSH...
...hssssssSSH...
...hhSssssSHH...
...hH......HH...
...h........H...
""",
    ('long', 'up'): """
................
................
.....hhhhhh.....
....hhhhhhhh....
...hhhhhhhhhH...
...hhhhhhhhhH...
...hhhhhhhhHH...
...hhhhhhhhHH...
...hhhhhhhhHH...
...hhhhhhhhHH...
...hHhhhhhHHH...
....hHhhhHHH....
""",
    ('long', 'left'): """
................
................
.....hhhhhh.....
....hhhhhhhhh...
....hhhhhhhhhH..
....hhhhhhhhHH..
....sshhhhhhHH..
....sesSShhhHH..
...sssssShhHHH..
....SssssShHH...
.........hhHH...
.........hHH....
""",
}
WALK_HEAD.update({
    ('bun', 'down'): """
................
......hhhH......
.....hhhhhH.....
....hhhhhhhH....
....hhhhhhhH....
....hhhhhhHH....
....hssssssH....
....ssesseSS....
....ssssssSS....
.....SssssS.....
""",
    ('bun', 'up'): """
................
......hhhH......
.....hhhhhH.....
....hhhhhhhH....
....hhhHHhhH....
....hhHhhHhH....
....hhhHHhHH....
....hhhhhhHH....
....hHhhhHHH....
.....SSSSSS.....
""",
    ('bun', 'left'): """
................
................
.....hhhhhh.....
....hhhhhhhhHh..
....hhhhhhhHhhH.
....hhhhhhhHHH..
....sshhhhhHH...
....sesSShhHH...
...sssssSShH....
....SssssH......
""",
    ('pony', 'down'): """
................
................
.....hhhhhh.....
....hhhhhhhh....
....hhhhhhhH....
....hhhhhhHH....
....hshhhhsH....
...hssesseSSH...
...hssssssSSH...
.....SssssS.....
""",
    ('pony', 'up'): """
................
................
.....hhhhhh.....
....hhhhhhhh....
....hhhhhhhH....
....hhhhhhhH....
....hhhhhhHH....
....hhhhhhHH....
....hHhaahHH....
.....HShHSH.....
.......hH.......
.......hH.......
.......hH.......
.......H........
""",
    ('pony', 'left'): """
................
................
.....hhhhhh.....
....hhhhhhhh....
....hhhhhhhhH...
....hhhhhhhHah..
....sshhhhhHhhH.
....sesSShhH.hH.
...sssssSShH..hH
....SssssH....H.
""",
    ('bald', 'down'): """
................
................
.....ssssss.....
....ssssssSS....
....sssssssS....
....hssssssH....
....hhSssShH....
....hsesseSH....
....ssssssSS....
.....SssssS.....
""",
    ('bald', 'up'): """
................
................
.....ssssss.....
....ssssssSS....
....sssssSSS....
....hssssSSH....
....hhhhhhHH....
....hhhhhhHH....
....hHhhhHHH....
.....HSSSSH.....
""",
    ('bald', 'left'): """
................
................
.....ssssss.....
....ssssssSS....
....sssssssSS...
....ssssssSSS...
....shSsshhhH...
....sesSShhHH...
...sssssSShH....
....SssssH......
""",
})

# extras: {facing: [(rows, how)]}. rows: text whose first line is '@' and the row it starts on; one text for all three
# frames (moved down with the upper body), or a list of three. how: 'over' paints over, 'cloth' paints over but leaves
# hands showing, 'under' only paints where nothing else is.
X = lambda *rows: '\n'.join(rows)
WALK_EXTRAS = {
    'sou_wester': {
        'down': [(X('@1', '......aaaa......', '.....aaaaaA.....', '....aaaaaaAA....', '..AaaaaaaaaaAA..', '...AAAAAAAAAA...'), 'over')],
        'up': [(X('@1', '......aaaa......', '.....aaaaaA.....', '....aaaaaaAA....', '..aaaaaaaaaaAA..', '...AaaaaaaaaAA..', '....AAAAAAAA....'), 'over')],
        'left': [(X('@1', '......aaaa......', '.....aaaaaA.....', '....aaaaaaAA....', '..AAaaaaaaaaA...', '.........aaaAA..', '.........AAAA...'), 'over')],
    },
    'flat_cap': {
        'down': [(X('@2', '.....GGGGGG.....', '....GGGGGGGg....', '....GGGGGGgg....', '....gggggggg....'), 'over')],
        'up': [(X('@2', '.....GGGGGG.....', '....GGGGGGGg....', '....GGGGGGgg....', '....GGGGGggg....'), 'over')],
        'left': [(X('@2', '.....GGGGGG.....', '....GGGGGGGg....', '....GGGGGGggg...', '..ggggGGGGgg....'), 'over')],
    },
    'beard': {
        'down': [(X('@8', '....hhhhhhhH....', '.....hhhhhH.....', '......hhhH......'), 'over')],
        'left': [(X('@8', '...shhhhhH......', '....hhhhH.......', '.....hhH........'), 'over')],
    },
    'long_beard': {
        'down': [(X('@8', '....shhhhhhS....', '.....hhhhhH.....', '.....hhhhhH.....', '......hhhH......', '.......hH.......', '.......h........'), 'over')],
        'left': [(X('@8', '...shhhhSH......', '...hhhhhH.......', '...hhhhH........', '....hhH.........', '....hH..........'), 'over')],
    },
    'robe': {
        'down': [(X('@14', '.....JJJJJJ.....', '.....ddddDD.....', '....dddddDDD....', '....ddddDdDD....', '....ddddDdDD....',
                    '...dddddDdDDD...', '...dddddDdDDD...', '...ddddDDDDDD...'), 'cloth')],
        'up': [(X('@14', '.....JJJJJJ.....', '.....ddddDD.....', '....dddddDDD....', '....ddddddDD....', '....ddddddDD....',
                  '...ddddddDDDD...', '...ddddddDDDD...', '...ddddDDDDDD...'), 'cloth')],
        'left': [([X('@14', '.....JJJJJJ.....', '.....dddddD.....', '....ddddddDD....', '....dddddDDD....', '....dddddDDD....',
                     '...ddddddDDDD...', '...ddddddDDDD...', '...dddddDDDDD...'),
                   X('@15', '.....JJJJJJ.....', '.....ddddddD....', '....ddddddDDD...', '...ddddddDDDD...',
                     '...dddddddDDDD..', '..ddddddddDDDD..', '..dddddddDDDDD..'),
                   X('@15', '.....JJJJJJ.....', '.....ddddddD....', '....ddddddDD....', '...ddddddDDDD...',
                     '...dddddddDDD...', '..ddddddddDDDD..', '..dddddddDDDDD..')], 'cloth')],
    },
    'chin_beard': {
        'down': [(X('@8', '....h......H....', '.....hhhhhH.....', '......hhH.......'), 'over')],
        'left': [(X('@8', '........S.......', '....hhhhH.......', '.....hhH........'), 'over')],
    },
    'burly': {
        'down': [(X('@10', '...1........2...', '..1..........2..', '..1..........2..', '..1..........2..', '..s..........S..'), 'under')],
        'up': [(X('@10', '...1........2...', '..1..........2..', '..1..........2..', '..1..........2..', '..s..........S..'), 'under')],
    },
    'apron': {
        'down': [(X('@10', '.....j....j.....', '.....jjjjjb.....', '.....jjjjjb.....', '.....jjjjjb.....', '.....jjjjjb.....',
                    '.....jjjjjb.....', '....jjjjjjbb....', '....jjjjjjbb....', '....jbjjjjbb....', '....jjjjjjbb....'), 'cloth')],
        'up': [(X('@10', '.....j....j.....', '......j..j......', '.......jj.......', '......j..j......', '.......AA.......',
                  '................', '....j......b....', '....j......b....', '....j......b....', '....j......b....'), 'cloth')],
        'left': [(X('@10', '.......j........', '......j.........', '....jj..........', '....jj..........', '....jj..........',
                    '...jjj..........', '...jjj..........', '...jjb..........', '...jjb..........', '...jbb..........'), 'cloth')],
    },
    'white_apron': {
        'down': [(X('@11', '......WWWY......', '......WWWY......', '......WWWY......', '.....aWWWWa.....', '.....WWWWWY.....',
                    '.....WWWWWY.....', '....WWWWWWYY....', '....WWWWWWYY....', '....WWWWWYYY....'), 'cloth')],
        'up': [(X('@14', '......WYYW......', '.......YY.......', '......Y..Y......'), 'cloth')],
        'left': [(X('@11', '....W...........', '....W...........', '....WW..........', '....aW..........', '....WW..........',
                    '...WWW..........', '...WWW..........', '...WWY..........', '...WYY..........'), 'cloth')],
    },
    'skirt': {
        'down': [(X('@15', '.....nnnnnN.....', '.....nnnnnN.....', '....nnnnnnNN....', '....nnnnnnNN....', '....nnnnnnNN....',
                    '...nnnnnnnNNN...'), 'cloth')],
        'up': [(X('@15', '.....nnnnnN.....', '.....nnnnnN.....', '....nnnnnnNN....', '....nnnnnnNN....', '....nnnnnnNN....',
                  '...nnnnnnnNNN...'), 'cloth')],
        'left': [([X('@15', '.....nnnnnN.....', '.....nnnnnN.....', '....nnnnnnN.....', '....nnnnnnNN....', '....nnnnnnNN....',
                     '...nnnnnnnNN....'),
                   X('@16', '.....nnnnnN.....', '.....nnnnnN.....', '....nnnnnnNN....', '....nnnnnnNN....', '...nnnnnnnNNN...'),
                   X('@16', '.....nnnnnN.....', '.....nnnnnN.....', '....nnnnnnNN....', '....nnnnnnNN....', '...nnnnnnnNNN...')],
                  'cloth')],
    },
    'ribbon': {
        'down': [(X('@10', '.......rR.......'), 'over')],
        'left': [(X('@10', '.....r..........'), 'over')],
    },
    'quiver': {
        'down': [(X('@8', '............rW..', '............Wr..', '............Bb..'), 'under'),
                 (X('@10', '..........b.....', '.........b......', '........b.......', '.......b........', '......b.........'), 'cloth')],
        'up': [(X('@7', '.........rW.....', '.........Wr.....', '........bBB.....', '........bbB.....', '........bbB.....',
                  '........bbB.....', '.........bB.....', '.........BB.....'), 'over')],
        'left': [(X('@7', '............rW..', '...........Wr...', '..........bBB...', '..........bbB...', '..........bbB...',
                    '...........bB...', '...........BB...'), 'under')],
    },
    'bow': {
        'down': [(X('@6', '..J.............', '..J.............', '..j.............', '...J............'), 'under'),
                 (X('@16', '............J...', '............j...', '.............J..'), 'under'),
                 (X('@10', '.....Y..........', '......Y.........', '.......Y........', '........Y.......', '.........Y......'), 'cloth')],
        'up': [(X('@6', '............J...', '............J...', '...........j....', '...........J....', '..........J.....',
                  '.........j......', '........J.......', '.......J........', '......j.........', '.....J..........',
                  '....J...........', '...j............', '...J............'), 'over')],
        'left': [(X('@5', '............J...', '............J...', '...........j....', '...........J....', '..........J.....',
                    '.........j......', '........J.......', '.......J........', '......j.........', '.....J..........',
                    '....J...........', '...j............', '...J............'), 'under'),
                 (X('@10', '.........Y......', '........Y.......', '.......Y........', '......Y.........'), 'cloth')],
    },
}

WALK_FIGURES = {'male': ('short', []), 'female': ('long', [])}


def walk_frame(head, extras, facing, i, sleeves=('d', 'D')):
    g = [['.'] * 16 for _ in range(24)]
    for y, r in enumerate(WALK_BODY[facing][i].strip('\n').split('\n')):
        g[10 + y] = list(r)
    bob = WALK_BOB[facing][i]
    for y, r in enumerate(WALK_HEAD[(head, facing)].strip('\n').split('\n')):
        for x, ch in enumerate(r):
            if ch != '.': g[y + bob][x] = ch
    for name in extras:
        for rows, how in WALK_EXTRAS[name].get(facing, []):
            shift = bob
            if isinstance(rows, list):
                rows, shift = rows[i], 0
            lines = rows.split('\n')
            top = int(lines[0][1:]) + shift
            for y, r in enumerate(lines[1:]):
                for x, ch in enumerate(r):
                    if ch == '.' or not 0 <= top + y < 24: continue
                    here = g[top + y][x]
                    if how == 'under' and here != '.': continue
                    if how == 'cloth' and here in 'sS': continue
                    g[top + y][x] = ch
    swap = {'1': sleeves[0], '2': sleeves[1]}
    g = [[swap.get(ch, ch) for ch in row] for row in g]
    out = [list(r) for r in g]
    for y in range(24):                     # the outline, all the way round
        for x in range(16):
            if g[y][x] != '.': continue
            if any(0 <= x + dx < 16 and 0 <= y + dy < 24 and g[y + dy][x + dx] != '.' for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                out[y][x] = 'o'
    return out


def sheet(head, extras, swaps, sleeves=('d', 'D')):
    """3 frames across (stand, step, step), 4 rows down (down, up, left, right)."""
    img = Image.new('RGBA', (16 * 3, 24 * 4), (0, 0, 0, 0))
    cmap = {k: C(k) for k in PAL}
    cmap.update({k: hexc(v) for k, v in swaps.items()})
    for row, facing in enumerate(('down', 'up', 'left', 'right')):
        for col in range(3):
            f = walk_frame(head, extras, 'left' if facing == 'right' else facing, col, sleeves)
            for y in range(24):
                for x in range(16):
                    ch = f[y][15 - x] if facing == 'right' else f[y][x]
                    if ch != '.': img.putpixel((col * 16 + x, row * 24 + y), cmap[ch])
    return img


NPCS = {
    # name: (head, skin, hair, cloth colours, extras, sleeves)
    'npc_yenson': ('short', SKINS['olive'], HAIRS['grey'], {'d': '#6e8f8c', 'D': '#46676a', 'n': '#3a4c50', 'N': '#253236'}, ['beard', 'sou_wester'], 'dD'),
    'npc_fisher': ('short', SKINS['fair'], HAIRS['red'], {'d': '#7d6b52', 'D': '#5a4634', 'n': '#46676a', 'N': '#253236'}, ['flat_cap'], 'dD'),
    'npc_garran': ('short', SKINS['brown'], HAIRS['black'], {'n': '#38343a', 'N': '#1e1a20'}, ['chin_beard', 'apron', 'burly'], 'sS'),
    'npc_ida': ('pony', SKINS['light'], HAIRS['red'], {'d': '#566b3e', 'D': '#3b4d30', 'n': '#5a4634', 'N': '#3a2d24'}, ['quiver'], 'dD'),
    'npc_bess': ('bun', SKINS['fair'], HAIRS['grey'], {'d': '#7e5a80', 'D': '#553a5a', 'n': '#7e5a80', 'N': '#553a5a'}, ['skirt', 'white_apron'], 'dD'),
    # old Orlen in a robe that was royal blue once; Mira the guild clerk; Rook, Ida's son
    'npc_orlen': ('bald', SKINS['fair'], HAIRS['grey'], {'d': '#46676a', 'D': '#253236', 'n': '#46676a', 'N': '#253236'}, ['robe', 'long_beard'], 'dD'),
    'npc_mira': ('long', SKINS['olive'], HAIRS['black'], {'d': '#cfc6bb', 'D': '#a69e9a', 'n': '#3a4c50', 'N': '#253236'}, ['skirt', 'ribbon'], 'dD'),
    'npc_rook': ('short', SKINS['light'], HAIRS['red'], {'d': '#566b3e', 'D': '#3b4d30', 'n': '#7d6b52', 'N': '#5a4634'}, ['bow'], 'dD'),
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
    'obj_log': """
................
................
................
................
................
................
...oooooooooo...
..obbBbbbBbbDo..
.oDdbbbbbbbbddo.
.odDbBbbbbBbDdo.
..obbbbbBbbbbo..
...oooooooooo...
....v......v....
................
................
................
""",
    'obj_boar': """
................
................
................
....oo..........
...oBBoooooo....
..obBBBBBBBBoo..
.oDbaBBbBbBbBBo.
oDDbbbbbbbbbbbBo
oeWbbbDbbbbbbBo.
.oWobbbbbbbbbBo.
..oo.oBbooBbBo..
.....oBo.oBo....
.....oo..oo.....
................
................
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
    'obj_wolf_alpha': """
................
..oo........oo..
.ogGo......oGgo.
.oggGoooooogggo.
.ogggggggggggGo.
.ogGaoggGgoaGgo.
.oggggggggggggo.
..oggRggggGggo..
..ogggWWWWgggo..
..ogggeeeegggo..
...oggmmmmggo...
...oggggggggo...
..oggggggggggo..
..oggggggggggo..
..ooo..oo..ooo..
................
""",
    # a snare: a bent sapling and a wire loop by a run in the grass
    'obj_snare': """
................
................
..........oo....
.........obo....
........obo.....
.......obo......
......obo.......
......obo.......
.....obo..ooo...
.....obo.oY.Yo..
.....obo.Y...Y..
....obbo.oY.Yo..
....oBBo..ooo...
...ovvvvo.......
................
................
""",
    # the lodge's chopping block, an axe bitten into it, split wood beside
    'obj_block': """
................
.........oo.....
........oyYo....
.......oyYo.....
......obyo......
.....obBo.......
....oooooooo....
...odddddddDo...
...oDdbdddDbo...
...obbbbbbbbo...
...oDbBbbBbBo...
...obbbbbbbbo...
...obBbbbbBbo...
....oooooooo....
....vvvvvvvv....
................
""",
    # a water butt: a big barrel with a lid, a ladle hung on it
    'obj_butt': """
................
...oooooooooo...
..obbbbbbbbbbo..
..oBBBBBBBBBBo..
..oDdddDDdddDo..
..odDdddDdddDo..
..oBBBBBBBBBBo..
..oDdddDddddWo..
..odDddDdddWWo..
..oDdddDddddWo..
..oBBBBBBBBBBo..
..oDdddDddddDo..
..odDddDdddDDo..
...oooooooooo...
...vvvvvvvvvv...
................
""",
    # a clump of yarrow: flat white flower heads on feathery stems
    'obj_yarrow': """
................
................
................
................
...oo.....oo....
..oWWo...oWWo...
..oWYWo.oWYWWo..
...oWo..oWWWo...
...ofo...ofo..oo
...of.oo.of..oWWo
..oF.oWWo.fo.oYWo
..oFf.oWo.Fo..ofo
...oFfofoFf..oFo.
...ovFFfFFvooFo..
....ovvvvvvvvvo..
.....ooooooooo...
""",
    # a young willow by the stream, bark peeling
    'obj_willow': """
.....oooooo.....
...ooFffffFoo...
..oFfiffiffiFo..
.oFiffFfFfffiFo.
.oFfiFffiFFfFFo.
oFfFfvFfFfvFfiFo
oFiFvoFbbFovFfFo
.oFvo.oDbo.ovFo.
.ofo..odbo..oFo.
.oFo..obDo..ofo.
..o...oDbo...o..
......obdo......
......oDbo......
.....obDdbo.....
....oBbbbbBo....
.....oooooo.....
""",
    # something glinting in the spoil heap
    'obj_glint': """
................
................
................
................
.......W........
.......W........
.....WWfWW......
.......W........
....ooo.Wooo....
...oyyYooyYYo...
..oyGyyyGyyyGo..
.oGyyGyyyGGyyGo.
.oGGyGGGyGyGGGo.
..ooooooooooooo.
................
................
""",
    'obj_brazier': """
................
................
................
................
................
...oooooooooo...
...oGgggggggo...
...oybBbBbBgo...
...ooyyyyyyoo...
....oGyyyyGo....
.....oGyyGo.....
......oGGo......
.....oGooGo.....
....oGo..oGo....
....oo....oo....
................
""",
    'obj_brazier_lit': """
.......a........
......aa..a.....
.....araa.a.....
....aarRaaa.....
....arRRrRa.....
...oarRRRrao....
...oGarrraGo....
...oybBbBbBgo...
...ooyyyyyyoo...
....oGyyyyGo....
.....oGyyGo.....
......oGGo......
.....oGooGo.....
....oGo..oGo....
....oo....oo....
................
""",
    'obj_fire': """
......a.........
.....aa....a....
.....ara..aa....
....aarRa.ara...
...aarRRaarRa...
...arRRRRrRRaa..
..aarRmRRRmRra..
..arRRmmRmmRRa..
..aRRmmmmmmRRa..
..aRmmGGGGmmRa..
..oRmGgGgGGmRo..
..obBbBbBbBbBo..
...oooooooooo...
....g.g..g.g....
................
................
""",
}


def object_sprite(text):
    lines = text.strip('\n').split('\n')
    rows = [list(r.ljust(16, '.')[:16]) for r in lines]
    img = Image.new('RGBA', (16, max(16, len(rows))), (0, 0, 0, 0))
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch != '.': img.putpixel((x, y), C(ch))
    return img


# ============================================================================================== weather

def fx_sprites(out):
    """Rain drawn over the world: long thin streaks slanting a little with the wind, in a tile that repeats."""
    for name, count, length, alpha in (('fx_drizzle', 18, 7, 70), ('fx_rain', 30, 12, 110), ('fx_storm', 55, 16, 140)):
        rnd = random.Random(name)
        img = Image.new('RGBA', (96, 96), (0, 0, 0, 0))
        for _ in range(count):
            x, y = rnd.randrange(96), rnd.randrange(96)
            n = length + rnd.randrange(-2, 3)
            for i in range(n):
                px, py = (x - i // 4) % 96, (y + i) % 96
                a = int(alpha * (0.45 + 0.55 * i / n))           # fades in towards the head of the drop
                img.putpixel((px, py), (150, 180, 205, a))
        img.save(os.path.join(out, name + '.png'))


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
        if area.get('indoor'):
            img, over, _ = tile_painter(area['rows'])
        else:
            img, over = terrain.paint(area['rows'])
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
                pick = (x * 7 + y * 3) % 6
                paint_house(img, x, y, x1 - x + 1, y1 - y + 1, wall='stone' if pick in (1, 4) else 'plank',
                            roof=('thatch', 'slate', 'tile')[pick % 3], door_x=door)
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
                head, extras = WALK_FIGURES[fig]
                sheet(head, extras, sw).save(os.path.join(walk, 'walk_%s_%s_%s.png' % (fig, skin, hair.replace(' ', '_'))))
                n += 1
    for name, (head, sramp, hramp, cloth, extras, sleeves) in NPCS.items():
        sw = dict(zip('sSqQ', sramp)); sw.update(dict(zip('hH', hramp))); sw.update(cloth)
        sheet(head, extras, sw, tuple(sleeves)).save(os.path.join(walk, name + '.png'))
    for name, text in OBJECTS.items():
        object_sprite(text).save(os.path.join(out, name + '.png'))
    fx_sprites(out)
    print('world: %d areas, %d walking sheets' % (len(AREAS), n + len(NPCS)))


if __name__ == '__main__':
    main()
