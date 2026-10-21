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
  m  mud            Z  palisade (log wall)        I  gatepost      Q  wrecked hull (on the beach)
"""
import json, math, os, random
from PIL import Image
from palette import PAL
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
    ('v', 'Vegetable patch', True, False), ('j', 'Woodpile', True, False), ('l', 'Lamp post', True, False),
    ('Y', 'Boat', True, False), ('N', 'Net rack', True, False), ('K', 'Crates', True, False),
    ('E', 'Lobster pots', True, False), ('S', 'Market stall', True, False), ('e', 'Bench', True, False),
    # the outpost look (21 Oct): mud underfoot, a log palisade with a gatepost each side, a wrecked hull on the beach
    ('m', 'Mud', False, True), ('Z', 'Palisade', True, False), ('I', 'Gatepost', True, False), ('Q', 'Wreck', True, False),
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

    # Skelmed's palisade across the top, a gatepost each side of the road, the first roofs beyond
    rect(0, 0, W - 1, 3, '.')
    for x in range(W):
        if g[4][x] != '~': g[4][x] = 'Z'
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

    # the gate gap in the palisade, with its posts
    for x in range(road(4) - 1, road(4) + 3): g[4][x] = '='
    g[4][road(4) - 2] = g[4][road(4) + 3] = 'I'

    # the wreck of your boat (David, 21 Oct): the beach widens below the headland of the bay north of the shack,
    # and what's left of the hull lies in the sand with the crate, the chest and the barrel the tide dropped round it
    for y in range(78, 91):
        c = coast(y)
        for x in range(c, c + 6 + (y % 4 == 1)):
            if g[y][x] in '.goT': g[y][x] = ','
    WRECK = (5, 82)                                   # top left of the hull: 5 tiles wide, 3 deep
    rect(WRECK[0], WRECK[1], WRECK[0] + 4, WRECK[1] + 2, 'Q')

    rows = [''.join(r) for r in g]
    rx = road(41)
    north = [0, 5, W - 1, ROAD_END - 4]            # fights happen north of the shack, not round it or in the copse
    area = {
        'id': 'coast_road',
        # Skelmed's first houses, beyond the palisade: log, like the rest of the outpost
        'styles': {'2,0': {'wall': 'log', 'roof': 'shingle'}, '19,0': {'wall': 'log', 'roof': 'thatch', 'smoke': True},
                   '17,90': {'wall': 'log', 'roof': 'thatch', 'smoke': True, 'moss': True}},
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
            {'id': 'wreck_crate', 'kind': 'item', 'sprite': 'obj_crate', 'x': 8, 'y': 86, 'scene': 'wreck_crate', 'once': True},
            {'id': 'wreck_chest', 'kind': 'item', 'sprite': 'obj_sea_chest', 'x': 10, 'y': 81, 'scene': 'wreck_chest', 'once': True},
            {'id': 'wreck_barrel', 'kind': 'item', 'sprite': 'obj_barrel', 'x': 6, 'y': 88, 'scene': 'wreck_barrel', 'once': True},
            {'id': 'fisher', 'kind': 'npc', 'sprite': 'npc_fisher', 'x': coast(70) + 1, 'y': 70, 'facing': 'left',
             'scene': 'ev_fisher', 'once': True, 'when': ['!night']},
            {'id': 'lookout', 'kind': 'spot', 'sprite': 'obj_cairn', 'x': 24, 'y': 31, 'scene': 'lookout_climb'},
            {'id': 'wolf', 'kind': 'npc', 'sprite': 'obj_wolf', 'x': rx, 'y': 38, 'facing': 'down', 'when': ['!flag:dog_met'], 'scene': 'wolf_rescue'},
            {'id': 'gulls', 'kind': 'item', 'sprite': 'obj_gulls', 'x': coast(21) + 1, 'y': 21, 'scene': 'ev_taint', 'once': True},
            {'id': 'log1', 'kind': 'item', 'sprite': 'obj_log', 'x': 14, 'y': 103, 'scene': 'deadfall', 'once': True},
            {'id': 'log2', 'kind': 'item', 'sprite': 'obj_log', 'x': 9, 'y': 107, 'scene': 'deadfall', 'once': True},
            {'id': 'log3', 'kind': 'item', 'sprite': 'obj_log', 'x': 16, 'y': 98, 'scene': 'deadfall', 'once': True},
            {'id': 'log4', 'kind': 'item', 'sprite': 'obj_log', 'x': 22, 'y': 107, 'scene': 'deadfall', 'once': True},
            {'id': 'log5', 'kind': 'item', 'sprite': 'obj_log', 'x': 19, 'y': 112, 'scene': 'deadfall', 'once': True},
        ],
        'zones': [
            {'x0': DOOR_X, 'y0': SHACK_Y, 'x1': DOOR_X, 'y1': SHACK_Y, 'to': 'hut_door'},
            {'x0': road(4) - 1, 'y0': 0, 'x1': road(4) + 2, 'y1': 4, 'to': 'village_gate'},
            # the wolf waits on the road below the headland, and Ida's arrows find it (a cutscene)
            {'x0': 0, 'y0': 42, 'x1': W - 1, 'y1': 43, 'scene': 'wolf_rescue', 'when': ['!flag:dog_met']},
        ],
        'uses': [
            {'tile': 'w', 'scene': 'stream_drink'},
            {'tile': 'k', 'scene': 'brambles'},
            {'tile': 'Q', 'scene': 'wreck_hull'},
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
    """Skelmed: what's left of a port, now a rough outpost behind a log palisade (David, 21 Oct): mud underfoot,
    log houses, the harbour open to the sea on the west."""
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

    # woods round the north and east, outside the wall
    rect(0, 0, W - 1, 0, 'T')
    for y in range(H):
        for x in range(W - 3 + (y % 3 == 0), W): g[y][x] = 'T'
    rect(0, 27, W - 1, 27, 'T')

    # the square, trodden to mud, the well in the middle; the main street from the gate up to it
    rect(13, 10, 23, 16, 'm')
    setc(18, 13, 'O')
    rect(17, 16, 18, 27, 'm')

    # the harbour: a lane down to the shore and the old pier, far too long for the boats left
    rect(coast(12) + 2, 12, 12, 13, 'm')
    for x in range(0, coast(12) + 2):
        g[12][x] = g[13][x] = 'P'

    # houses: (x0, y0, x1, y1, door x)
    houses = [(7, 3, 11, 6, 9), (14, 3, 20, 6, 17), (23, 3, 27, 6, 25), (8, 18, 12, 21, 10), (27, 9, 31, 13, 29),
              (24, 18, 29, 21, 26)]
    doors = []
    for (x0, y0, x1, y1, dx) in houses:
        rect(x0, y0, x1, y1, 'H')
        doors.append((dx, y1))
    # muddy tracks from the doors to the square
    for (x0, y0, x1, y1, dx) in houses:
        end = 10 if y1 < 10 else 15 if y1 < 18 else 23
        for y in range(y1 + 1, end): setc(dx, y, 'm')
    rect(9, 9, 13, 9, 'm')
    rect(23, 9, 25, 9, 'm')
    rect(23, 14, 29, 14, 'm')
    rect(10, 22, 18, 22, 'm')
    rect(18, 22, 29, 23, 'm')

    # the lane north out of the village, up into the forest
    for y in range(0, 10):
        setc(12, y, '='); setc(13, y, '=')

    # the net shed down by the harbour
    rect(8, 15, 10, 16, 'H')

    # the palisade: sharpened logs along the north, east and south, open to the harbour on the west;
    # a gate in the south wall onto the coast road and one in the north onto the forest lane
    for x in range(coast(1) + 2, W - 3): setc(x, 1, 'Z')
    for y in range(1, 27): setc(W - 4, y, 'Z')
    for x in range(coast(26) + 2, W - 3): setc(x, 26, 'Z')
    setc(12, 1, '='); setc(13, 1, '='); setc(11, 1, 'I'); setc(14, 1, 'I')
    setc(17, 26, 'm'); setc(18, 26, 'm'); setc(16, 26, 'I'); setc(19, 26, 'I')

    # barrels by the harbour, a few trees left standing inside the wall
    for (x, y) in ((11, 11), (12, 11), (11, 14), (30, 21)):
        setc(x, y, 'n')
    for (x, y) in ((4, 4), (3, 21), (13, 23), (22, 8), (21, 19), (6, 23), (14, 8), (31, 24)):
        if g[y][x] in '.,': g[y][x] = 'T'
    # ---- life in the village: boats, nets and pots by the harbour, an empty stall and torches on the square,
    # a garden and woodpiles, and the empty houses gone to seed (half the village has left)
    for x in (1, 2): setc(x, 10, 'Y'); setc(x, 16, 'Y')          # two boats tied up by the pier
    for (x, y) in ((6, 10), (7, 11)): setc(x, y, 'E')             # lobster pots on the shingle
    for (x, y) in ((6, 17), (6, 19)): setc(x, y, 'N')             # nets drying on racks
    setc(12, 16, 'K')                                             # crates by the net shed
    setc(21, 11, 'S'); setc(22, 11, 'S')                          # the market stall, nothing on it
    setc(23, 11, 'K')                                             # and the crates it came in
    setc(13, 16, 'l'); setc(23, 16, 'l'); setc(13, 10, 'l')       # torch posts round the square
    setc(20, 16, 'e')                                             # a bench
    setc(21, 4, 'j'); setc(31, 22, 'j'); setc(22, 5, 'j')         # woodpiles by the lodge and the east houses
    rect(28, 4, 30, 5, 'v')                                       # a vegetable patch behind the east house
    for x in range(28, 31): setc(x, 6, 'F')
    # mud wherever feet and carts go, grass hanging on by the walls; long grass round the empty houses
    for _ in range(70):
        x, y = rnd.randrange(7, W - 4), rnd.randrange(2, 26)
        if g[y][x] == '.' and any(g[y + dy][x + dx] == 'm' for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
            g[y][x] = 'm'
    for (x, y) in ((26, 8), (27, 8), (31, 8), (31, 9), (31, 11), (31, 12), (26, 12), (13, 19), (14, 19), (13, 20), (7, 20), (13, 21)):
        if g[y][x] == '.': g[y][x] = 'g'

    rows = [''.join(r) for r in g]
    return {
        'id': 'skelmed',
        # who lives where: Bess's inn, Ida's hunters' lodge, a family at the east end; the rest stand empty
        'styles': {
            '7,3': {'wall': 'log', 'roof': 'shingle', 'sign': 'inn', 'smoke': True},
            '14,3': {'wall': 'log', 'roof': 'thatch', 'sign': 'lodge', 'smoke': True, 'moss': True},
            '23,3': {'wall': 'log', 'roof': 'thatch', 'smoke': True},
            '24,18': {'wall': 'log', 'roof': 'shingle', 'boarded': True},
            '27,9': {'wall': 'log', 'roof': 'thatch', 'boarded': True},
            '8,18': {'wall': 'log', 'roof': 'shingle', 'boarded': True},
            '8,15': {'wall': 'log', 'roof': 'shingle'},
        },
        'name': 'Skelmed',
        'minutes': 1, 'eventChance': 0, 'fightChance': 0, 'fightGap': 10,
        'camp': False,
        'doors': doors,
        'spots': [
            {'id': 'village_gate', 'x': 17, 'y': 24, 'facing': 'up'},
            {'id': 'skelmed_north', 'x': 12, 'y': 2, 'facing': 'down'},
        ],
        'objects': [
            # the hunters' lodge is the long house at the top; Ida's outside it unless she's up in the forest with you
            {'id': 'ida', 'kind': 'npc', 'sprite': 'npc_ida', 'x': 19, 'y': 8, 'facing': 'down', 'scene': 'ida'},
            {'id': 'rook', 'kind': 'npc', 'sprite': 'npc_rook', 'x': 15, 'y': 7, 'facing': 'down', 'scene': 'rook',
             'when': ['!follower:rook', '!flag:dead_rook', '!flag:rook_escort']},
            {'id': 'empty_house', 'kind': 'sign', 'x': 29, 'y': 14,
             'text': 'Boarded up. Someone has chalked on the door: GONE INLAND.'},
            {'id': 'old_smithy', 'kind': 'sign', 'x': 26, 'y': 22,
             'text': "Boarded up, a cold forge behind the shutters. The smith went inland with the rest. Somebody has nailed a horseshoe over the door, upside down, so the luck runs out."},
            # Bess keeps the inn; but when you first walk in bitten she's down at the gate (she was buying fish) and
            # sees to you there, then walks home once the bite's seen to (David, 20 Oct)
            {'id': 'bess', 'kind': 'npc', 'sprite': 'npc_bess', 'x': 9, 'y': 8, 'facing': 'down', 'scene': 'bess',
             'when': ['flag:patch_done|!untreated:bite']},
            {'id': 'bess_gate', 'kind': 'npc', 'sprite': 'npc_bess', 'x': 19, 'y': 24, 'facing': 'left', 'scene': 'bess_gate',
             'when': ['!flag:patch_done', 'untreated:bite']},
            {'id': 'dockhand', 'kind': 'npc', 'sprite': 'npc_fisher', 'x': 2, 'y': 13, 'facing': 'left', 'scene': 'dockhand'},
            {'id': 'board', 'kind': 'sign', 'sprite': 'obj_sign', 'x': 15, 'y': 9,
             'text': 'The noticeboard. One notice, months old: BY ORDER OF THE CROWN, THE HARBOUR LEVY IS RAISED TO ONE HALF.'},
        ],
        'zones': [
            {'x0': 17, 'y0': 27, 'x1': 18, 'y1': 27, 'to': 'skelmed'},
            {'x0': 12, 'y0': 1, 'x1': 13, 'y1': 1, 'scene': 'north_gate', 'when': ['flag:rook_escort']},
            {'x0': 12, 'y0': 1, 'x1': 13, 'y1': 1, 'scene': 'north_gate_alone', 'when': ['flag:ida_alone']},
            {'x0': 12, 'y0': 0, 'x1': 13, 'y1': 0, 'to': 'hills_gate'},
            # Bess sees the wolf bite as you walk in, and shows you how to treat it
            {'x0': 14, 'y0': 22, 'x1': 21, 'y1': 23, 'scene': 'bess_patch', 'when': ['untreated:bite', '!flag:patch_lesson']},
        ],
        'uses': [
            {'tile': 'O', 'scene': 'well_drink'},
        ],
        'events': [],
        'rows': rows,
    }


# ============================================================================================== the forest

def hills():
    """The forest above Skelmed (David, 21 Oct): an old camp clearing near the bottom, deadwood along the paths,
    the boar's glade and Rook's snare line to the west, and the pack leader's den up in the north-east."""
    W, H = 34, 40
    g = [['.'] * W for _ in range(H)]
    rnd = random.Random(31)

    def setc(x, y, t):
        if 0 <= x < W and 0 <= y < H: g[y][x] = t

    def rect(x0, y0, x1, y1, t, keep=None):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if keep is None or (0 <= y < H and 0 <= x < W and g[y][x] in keep): setc(x, y, t)

    # the path from the village (bottom) winding up through the trees
    path = lambda y: int(round(16 + 2.5 * math.sin(y / 6.0) - 1.5 * math.sin(y / 13.0)))

    # forest: thick round the edges, close everywhere, thicker still in the west and the north
    for y in range(H):
        for x in range(W):
            edge = min(x, W - 1 - x, y)
            p = 0.95 if edge < 2 else (0.42 if x < 12 or y < 12 else 0.26)
            if rnd.random() < p: g[y][x] = 'T'
    rect(0, H - 1, W - 1, H - 1, 'T')

    # an old clearing up at the top of the path, and the den: a hollow under rocks in the north-east corner
    rect(12, 4, 21, 9, '.', keep='T.')
    rect(14, 5, 18, 7, 'g', keep='.')
    rect(24, 2, 31, 8, '.', keep='T.')
    for (x, y) in ((27, 2), (31, 2), (31, 4), (30, 2), (24, 2)):
        setc(x, y, 'r')
    for x in range(20, 26): setc(x, 7, '=')

    # the stream, from the woods in the east down to the west
    stream_y = lambda x: int(round(19 + 1.4 * math.sin(x / 4.0)))
    for x in range(W):
        y0 = stream_y(x)
        for y in (y0, y0 + 1): setc(x, y, 'w')

    # Ida's camp: a clearing east of the path, near the bottom
    rect(20, 26, 29, 35, '.')
    for (x, y) in ((20, 26), (29, 26), (20, 35), (29, 35), (29, 34)):
        setc(x, y, 'T')

    # clearings along the snare line in the west woods, and the boar's glade
    for (x0, y0, x1, y1) in ((3, 23, 9, 29), (4, 12, 9, 16), (2, 6, 6, 10), (6, 28, 14, 30), (6, 16, 9, 24), (4, 9, 8, 13)):
        rect(x0, y0, x1, y1, '.', keep='T.')
    # long grass
    for (x0, y0, x1, y1) in ((4, 25, 6, 27), (25, 13, 29, 16), (3, 13, 5, 15)):
        rect(x0, y0, x1, y1, 'g', keep='.')
    for (x, y) in ((28, 22), (10, 33), (13, 24)):
        if g[y][x] in '.': g[y][x] = 'r'

    # the path itself, two wide, with a footbridge over the stream; a track off it to the camp
    # and a narrower one west along the snare line
    prev = None
    for y in range(6, H):
        x = path(16) if 16 <= y <= 22 else path(y)
        lo, hi = (x, x + 1) if prev is None else (min(x, prev), max(x, prev) + 1)
        for xx in range(lo, hi + 1):
            setc(xx, y, 'b' if g[y][xx] == 'w' else '=')
        prev = x
    for x in range(path(31) + 2, 22): setc(x, 31, '=')
    for x in range(5, path(29)):
        if g[29][x] != 'w': setc(x, 29, '=')
    for y in range(13, 29): setc(7, y, 'b' if g[y][7] == 'w' else '=')
    for x in range(4, 8): setc(x, 13, '=')
    # the bottom gate: the lane down to the village
    gx = path(H - 1)
    for y in range(H - 3, H): setc(gx, y, '='); setc(gx + 1, y, '=')

    # Ida's marked trees round the camp, the young ash on the stream bank: the ground under them clear
    marked = [(21, 27), (28, 27), (21, 34), (27, 33)]
    ash = (24, stream_y(24) + 2)
    for (x, y) in marked + [ash, (5, 17), (path(36) + 2, 36)]: setc(x, y, '.')
    setc(ash[0], ash[1] + 1, '.'); setc(ash[0] - 1, ash[1] + 1, '.')
    for (x, y) in ((path(34) + 3, 34), (24, 28), (10, 30), (27, 33)): setc(x, y, '.')   # deadwood for the hunt

    rows = [''.join(r) for r in g]
    snares = [(4, 26), (8, 14), (3, 8)]
    objects = [
        # deadwood for the village, while you're out with Rook (the hunt)
        {'id': 'hwood1', 'kind': 'item', 'sprite': 'obj_log', 'x': path(34) + 3, 'y': 34, 'scene': 'forest_wood1', 'when': ['quest:hunt', '!flag:hunt_wood1']},
        {'id': 'hwood2', 'kind': 'item', 'sprite': 'obj_log', 'x': 24, 'y': 28, 'scene': 'forest_wood2', 'when': ['quest:hunt', '!flag:hunt_wood2']},
        {'id': 'hwood3', 'kind': 'item', 'sprite': 'obj_log', 'x': 10, 'y': 30, 'scene': 'forest_wood3', 'when': ['quest:hunt', '!flag:hunt_wood3']},
        {'id': 'hwood4', 'kind': 'item', 'sprite': 'obj_log', 'x': 27, 'y': 33, 'scene': 'forest_wood4', 'when': ['quest:hunt', '!flag:hunt_wood4']},
        # the boar, rooting in the glade west of the camp once Ida sends you after it
        {'id': 'boar', 'kind': 'npc', 'sprite': 'obj_boar', 'x': 6, 'y': 25, 'facing': 'right', 'scene': 'boar',
         'when': ['quest:hunt', '!flag:boar_killed']},
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
        {'id': 'sign_hills', 'kind': 'sign', 'sprite': 'obj_sign', 'x': path(36) + 2, 'y': 36,
         'text': "Left: SNARES, KEEP OFF. (Rook) Someone has scratched a wolf's head underneath, and a cross through it."},
    ]
    return {
        'id': 'skelmed_hills',
        'name': 'The forest above Skelmed',
        'minutes': 2, 'eventChance': 0, 'fightChance': 0, 'fightGap': 14,
        'spots': [
            {'id': 'hills_gate', 'x': gx, 'y': H - 3, 'facing': 'up'},
            {'id': 'forest_camp', 'x': 23, 'y': 31, 'facing': 'right'},
        ],
        'objects': objects,
        'zones': [
            {'x0': gx, 'y0': H - 1, 'x1': gx + 1, 'y1': H - 1, 'to': 'skelmed_north'},
            {'x0': 26, 'y0': 2, 'x1': 31, 'y1': 6, 'scene': 'den', 'when': ['quest:pack', '!flag:alpha_dead', '!flag:den_found']},
            # the boar charges when you come into its glade
            {'x0': 3, 'y0': 23, 'x1': 9, 'y1': 29, 'scene': 'boar', 'when': ['quest:hunt', '!flag:boar_killed', '!flag:boar_seen']},
        ],
        'uses': [
            {'tile': 'w', 'scene': 'hill_stream'},
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
    'shingle': ('Y', 'y', 'G', 'g'),    # split-wood shakes, weathered grey
}
ROUGH_THATCH = ('A', 'j', 'b', 'B')     # old straw gone dull and brown
WALLS = {
    'plank': ('d', 'D', 'b'),
    'stone': ('Y', 'y', 'G'),
    'log': ('D', 'b', 'B'),             # round logs laid flat, mud chinking between
}


def rough_roof(put, X0, Y0, X1, RY, roof, cols, r, moss, holes):
    """Repaints a roof rough: courses that sag toward the middle, uneven split shakes (shingle) or patchy straw (thatch),
    a ragged eave."""
    rl, rm, rd, rx = cols
    span = max(1, X1 - X0)
    roof_h = max(1, RY - Y0)
    sag = lambda X: 2.2 * math.sin(math.pi * (X - X0) / span)
    shakes = {}

    def shake(c, X):
        """Which shake of course c is at X, and its tone and length (cached per course)."""
        if c not in shakes:
            row, x = [], X0 - 1
            while x <= X1 + 1:
                wdt = r.randint(2, 5)
                tone = r.choice((rl, rm, rm, rd, rd)) if r.random() > 0.15 else r.choice(('D', 'b'))
                row.append((x, x + wdt, tone, r.random() < 0.3, holes and r.random() < 0.05))
                x += wdt + 1
            shakes[c] = row
        for s in shakes[c]:
            if s[0] <= X <= s[1]: return s
        return (X, X, rm, False, False)

    for X in range(X0 - 1, X1 + 2):
        sg = sag(X)
        eave = RY + int(round(sg * 0.8))
        for Y in range(Y0, eave + 1):
            f = (Y - Y0) / roof_h
            yy = (Y - Y0) - sg * f                   # position down the roof, bent by the sag
            t = yy / roof_h
            if roof == 'shingle':
                c = int(yy // 4)
                cy = yy - c * 4
                x0s, x1s, tone, longer, hole = shake(c, X)
                k = tone
                if cy < 1: k = rx                              # shadow under the course above
                elif X == x1s + 1 or X == x0s - 1: k = rx      # the gap between shakes
                elif cy >= 3: k = rd if not longer else tone
                elif X == x0s and cy < 2.5 and tone != rl: k = rl
                if hole and 1 <= cy < 3: k = 'e'
            else:
                c = int(yy // 3)
                cy = yy - c * 3
                k = rm
                if cy >= 2: k = rd
                elif (X + c * 2 + (c * 7) % 3) % 4 == 0: k = rl
                elif (X * 5 + c * 3) % 11 == 0: k = 'y'                 # grey, weathered straw
                if t < 0.15 and cy < 2: k = rl if (X + c) % 3 else rm
            if X >= X1 - 2: k = rd if k not in ('e', rx) else k
            if X in (X0 - 1, X1 + 1) or Y == Y0 or Y == eave: k = 'o'
            put(X, Y, k)
        for Y in range(eave + 1, RY + 3):          # where the eave sags, the shadow under it follows
            put(X, Y, 'o' if Y == eave + 1 else 'B')
        put(X, Y0 + 1, rl if X not in (X0 - 1, X1 + 1) else 'o')
        if roof != 'shingle' and r.random() < 0.45:  # straws hanging ragged off the eave
            for j in range(r.randint(1, 2)): put(X, eave + j, rd if j == 0 else rx)
    # patches: rotten dark hollows, newer repairs, moss
    for _ in range(span // 14 + 2):
        cx, cy = r.randint(X0 + 4, X1 - 6), r.randint(Y0 + 4, RY - 4)
        k = rx if r.random() < 0.6 else ('D' if roof == 'thatch' else 'b')
        for (dx, dy) in ((0, 0), (1, 0), (2, 0), (-1, 1), (0, 1), (1, 1), (2, 1), (3, 1), (0, 2), (1, 2)):
            if r.random() < 0.75: put(cx + dx, cy + dy + int(sag(cx)), k)
    if moss:
        for _ in range(span // 8 + 2):
            cx, cy = r.randint(X0 + 2, X1 - 4), r.randint(Y0 + 3, RY - 3)
            for (dx, dy) in ((0, 0), (1, 0), (0, 1), (-1, 0), (1, 1), (2, 0)):
                if r.random() < 0.7: put(cx + dx, cy + dy, 'i' if (dx + dy) % 2 else 'F')


def log_corners(put, X0, X1, RY, Y1):
    """The round ends of the wall logs sticking out past each corner, alternating long and short like a notched cabin."""
    for n, Y in enumerate(range(RY + 5, Y1 - 1, 5)):
        for side in (-1, 1):
            stick = 2 if (n + (side > 0)) % 2 else 1
            cx = (X0 - stick) if side < 0 else (X1 + stick)
            cy = Y + 1
            for dy in range(-2, 3):
                for dx in range(-2, 3):
                    d = dx * dx + dy * dy
                    if d > 5: continue
                    k = 'o' if d >= 4 else ('J' if d == 0 else 'd' if dx + dy < 0 else 'D')
                    put(cx + dx, cy + dy, k)
            put(cx, cy, 'b')                     # the heart of the log


def paint_house(img, x0, y0, w, h, wall='plank', roof='thatch', door_x=None, over=None, boarded=False, sign=None,
                flowers=False, smoke=False, moss=False, rough=False):
    """A house seen from above and in front. boarded: an empty house, windows nailed shut and the roof gone mossy.
    sign: a board hung by the door ('inn', 'smith', 'lodge'). flowers: window boxes. smoke: a curl from the chimney (over).
    wall: 'plank', 'stone' or 'log' (a frontier cabin). roof: 'thatch', 'slate', 'tile' or 'shingle' (grey split shakes).
    rough: an old patchy roof, sagging in the middle (thatch goes dull brown; log houses are always rough)."""
    px = img.load()
    opx = over.load() if over is not None else None
    X0, Y0, X1, Y1 = x0 * T, y0 * T, (x0 + w) * T - 1, (y0 + h) * T - 1
    rough = rough or wall == 'log'
    rl, rm, rd, rx = ROUGH_THATCH if (rough and roof == 'thatch') else ROOFS[roof]
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
            elif wall == 'log':
                # round logs, 5 px a course: lit top, bark, shaded underside, a line of mud chinking
                c = (Y - RY) % 5
                n = (Y - RY) // 5
                k = (wl, wm, wm, wd, 'j')[c]
                if c in (1, 2) and (X * 3 + n * 11 + c * 5) % 13 == 0: k = wd           # bark cracks, knots
                if c == 0 and (X + n * 7) % 9 == 0: k = wm
                if c == 4 and (X * 7 + n) % 5 == 0: k = 'J' if (X + n) % 2 else 'B'    # straw and grit in the mud
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
    # weathering: the odd lighter or darker shingle, and moss on an old or empty roof
    wr = random.Random(x0 * 131 + y0 * 17)
    for _ in range(w * h * 3):
        X, Y = wr.randint(X0 + 1, X1 - 3), wr.randint(Y0 + 3, RY - 2)
        if (Y - Y0) % 3 != 2: put(X, Y, rl if wr.random() < 0.5 else rd); put(X + 1, Y, rl if wr.random() < 0.5 else rd)
    if moss or boarded:
        for _ in range(w * 2 + 2):
            cx, cy = wr.randint(X0 + 2, X1 - 4), wr.randint(Y0 + 3, RY - 3)
            for (dx, dy) in ((0, 0), (1, 0), (0, 1), (-1, 0), (1, 1), (2, 0)):
                if wr.random() < 0.8: put(cx + dx, cy + dy, 'i' if (dx + dy) % 2 else 'F')
    # eaves: a lit edge along the bottom of the roof, and gable ends
    for X in range(X0, X1 + 1): put(X, RY - 1, rd)
    for Y in range(Y0 + 1, RY):
        put(X0, Y, rl); put(X1, Y, rx)
    if roof == 'shingle' or rough:
        rough_roof(put, X0, Y0, X1, RY, roof, (rl, rm, rd, rx), random.Random(x0 * 53 + y0 * 29), moss or boarded, boarded)
    if wall == 'log':
        log_corners(put, X0, X1, RY, Y1)

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
        if boarded:
            # nailed shut: two planks across, shutters gone
            for (Y, k) in ((wy + 2, 'D'), (wy + 3, 'b'), (wy + 4, 'd'), (wy + 5, 'b')):
                for X in range(wx - 1, wx + 9): put(X, Y, k)
            put(wx, wy + 2, 'g'); put(wx + 7, wy + 4, 'g')
        elif flowers:
            for X in range(wx - 1, wx + 9):
                put(X, wy + 8, 'b'); put(X, wy + 9, 'B')
                if (X - wx) % 2 == 0: put(X, wy + 7, 'r' if (X // 2) % 3 else 'a')
                else: put(X, wy + 7, 'i')

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
        if boarded:
            for i in range(10):
                put(DX + i, top + 2 + i, 'd'); put(DX + i, top + 3 + i, 'b')
                put(DX + 9 - i, top + 2 + i, 'd'); put(DX + 9 - i, top + 3 + i, 'b')
        if sign:
            # a board hung from a bracket beside the door
            SX, SY = DX + 13, top - 2
            for X in range(SX - 1, SX + 9): put(X, SY, 'o')
            put(SX + 1, SY + 1, 'g'); put(SX + 6, SY + 1, 'g')
            for Y in range(SY + 2, SY + 9):
                for X in range(SX - 1, SX + 9):
                    edge = X in (SX - 1, SX + 8) or Y in (SY + 2, SY + 8)
                    put(X, Y, 'o' if edge else 'd' if Y == SY + 3 else 'D')
            if sign == 'inn':      # a mug of ale
                for Y in range(SY + 4, SY + 8):
                    for X in range(SX + 2, SX + 5): put(X, Y, 'a' if Y > SY + 4 else 'W')
                put(SX + 5, SY + 5, 'b'); put(SX + 5, SY + 6, 'b')
            elif sign == 'smith':  # an anvil
                for X in range(SX + 1, SX + 7): put(X, SY + 4, 'G')
                for X in range(SX + 3, SX + 5): put(X, SY + 5, 'g'); put(X, SY + 6, 'g')
                for X in range(SX + 2, SX + 6): put(X, SY + 7, 'g')
            elif sign == 'lodge':  # crossed arrows
                for i in range(6):
                    put(SX + 1 + i, SY + 3 + i, 'b'); put(SX + 6 - i, SY + 3 + i, 'b')
                put(SX + 1, SY + 3, 'W'); put(SX + 6, SY + 3, 'W')

    # chimney
    cx = X1 - 12
    for Y in range(Y0 - 5, Y0 + 7):
        for X in range(cx, cx + 6):
            k = 'G' if (Y + X) % 4 else 'y'
            if X in (cx, cx + 5) or Y == Y0 - 5: k = 'o'
            if Y == Y0 - 4 and X not in (cx, cx + 5): k = 'g'
            put(X, Y, k)
    if smoke and opx is not None:
        # a thin curl of smoke going up from the chimney, drawn over everything
        sr = random.Random(x0 * 7 + y0)
        for i, (dx, dy, rad) in enumerate(((2, -9, 2.2), (4, -14, 2.8), (2, -20, 3.2), (5, -26, 3.4), (8, -31, 3))):
            for Y in range(int(Y0 + dy - rad) - 1, int(Y0 + dy + rad) + 2):
                for X in range(int(cx + dx - rad) - 1, int(cx + dx + rad) + 2):
                    if (X - cx - dx) ** 2 + (Y - Y0 - dy) ** 2 <= rad * rad and 0 <= X < over.width and 0 <= Y < over.height:
                        if (X + Y + i) % 2 == 0 or i < 2:
                            c = C('W' if (X - cx - dx) + (Y - Y0 - dy) < 0 else 'Y')
                            opx[X, Y] = c[:3] + (150 - i * 22,)


# The walking sprites (you and the townsfolk) are made by walk.py.

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


# ============================================================================================== big object sprites

class Spr:
    """A small transparent picture drawn with palette letters (objects are drawn bottom-centre on their tile)."""
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        self.px = self.img.load()

    def put(self, x, y, k, a=255):
        x, y = int(x), int(y)
        if 0 <= x < self.w and 0 <= y < self.h: self.px[x, y] = C(k)[:3] + (a,)

    def get(self, x, y):
        return self.px[x, y][3] if 0 <= x < self.w and 0 <= y < self.h else 0

    def shadow(self, cx, cy, rx, ry):
        """A soft shadow on the ground (dark green, see-through), under whatever is drawn after."""
        for (x, y) in terrain.blob(cx, cy, rx, ry):
            if not self.get(x, y): self.put(x, y, 'V', 110)

    def outline(self, k='o'):
        """A dark rim round everything solid drawn so far (shadows aside)."""
        solid = {(x, y) for x in range(self.w) for y in range(self.h) if self.px[x, y][3] == 255}
        for (x, y) in solid:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if (x + dx, y + dy) not in solid: self.put(x + dx, y + dy, k)

    def save(self, out, name):
        self.img.save(os.path.join(out, name + '.png'))


def _canopy(s, cx, cy, rad, seed, airy=False):
    """A round leafy crown built from lumps, lit from the top left, like the painted trees."""
    r = random.Random(seed)
    pts = set()
    n = 7 if airy else 5
    lumps = [(cx, cy, rad * (0.55 if airy else 0.75))] + [
        (cx + rad * 0.6 * math.cos(a), cy + rad * 0.55 * math.sin(a), rad * (0.38 if airy else 0.5))
        for a in [r.uniform(0, 6.28) + i * 6.28 / n for i in range(n)]]
    for (lx, ly, lr) in lumps:
        pts |= set(terrain.blob(lx, ly, lr, lr * 0.92))
    if airy:      # gaps the sky shows through
        for _ in range(int(rad * 0.6)):
            hx, hy = cx + r.uniform(-rad, rad) * 0.8, cy + r.uniform(-rad, rad) * 0.7
            pts -= set(terrain.blob(hx, hy, r.uniform(0.8, 1.6), r.uniform(0.8, 1.4)))
    for (x, y) in pts:
        dx, dy = (x - cx) / rad, (y - cy) / rad
        l = -dx * 0.6 - dy * 0.9 + r.uniform(-0.35, 0.35)
        k = 'f' if l > 0.75 else 'i' if l > 0.25 else 'F' if l > -0.35 else 'v'
        if airy: k = 'f' if l > 0.45 else 'i' if l > -0.15 else 'F'
        if (x, y + 1) not in pts: k = 'v' if airy else 'V'
        elif (x + 1, y) not in pts or (x - 1, y) not in pts: k = ('F' if airy else 'v') if dy > -0.2 else ('i' if airy else 'F')
        s.put(x, y, k)
    for (x, y) in pts:          # a dark rim so it stands off the grass
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if (x + dx, y + dy) not in pts and not s.get(x + dx, y + dy): s.put(x + dx, y + dy, 'V' if dy >= 0 and dx >= 0 else 'v')
    return pts


def big_objects(out):
    """Things to chop and scavenge, drawn with PIL: trees wider and taller than a tile, stumps, fire pits,
    wreckage washed up on the beach."""
    # --- a big tree you can fell: a crown like the painted ones, a thick trunk, roots, a pale axe blaze
    s = Spr(36, 48)
    s.shadow(20, 45, 13, 3)
    tx = 17
    for y in range(24, 46):
        flare = 0 if y < 43 else (y - 42)
        for x in range(tx - 3 - flare, tx + 4 + flare):
            k = 'b'
            if x <= tx - 2 - flare: k = 'D' if (y + x) % 4 else 'b'
            elif x >= tx + 2 + flare: k = 'B'
            elif (x * 5 + y * 3) % 11 == 0: k = 'B'
            s.put(x, y, k)
    for (x, y) in ((tx - 7, 46), (tx - 6, 45), (tx + 7, 46), (tx + 6, 45), (tx - 8, 46), (tx + 8, 46)):   # roots
        s.put(x, y, 'b' if x < tx else 'B')
    s.outline()
    # the blaze: a strip of bark hacked off, pale wood showing
    for y in range(35, 40):
        s.put(tx - 1, y, 'J' if y < 38 else 'd'); s.put(tx, y, 'd' if y > 35 else 'J'); s.put(tx + 1, y, 'B')
    s.put(tx - 1, 34, 'o'); s.put(tx, 34, 'o'); s.put(tx - 1, 40, 'o'); s.put(tx, 40, 'o')
    s.put(tx + 3, 44, 'J'); s.put(tx - 5, 45, 'd')          # chips on the ground
    _canopy(s, 18, 17, 15, 11)
    s.save(out, 'obj_tree')

    # --- a young ash: straight pale grey pole of a trunk, light airy leaves (a bow stave)
    s = Spr(22, 40)
    s.shadow(12, 38, 7, 2)
    for y in range(8, 39):
        for x, k in ((10, 'Y'), (11, 'y')):
            s.put(x, y, 'G' if (y * 7) % 9 == 0 and x == 11 else k)
    s.put(9, 38, 'y'); s.put(12, 38, 'G')
    s.outline()
    pts = _canopy(s, 11, 15, 9, 21, airy=True)
    pts |= _canopy(s, 10, 9, 7, 5, airy=True)
    for (x0, y0, dx) in ((11, 24, 1), (10, 20, -1)):     # thin twigs below the leaves
        for i in range(4): s.put(x0 + dx * (i + 1), y0 - i, 'y' if i < 2 else 'G')
    s.save(out, 'obj_ash')

    # --- a fresh stump: pale cut top with rings, chips round it
    s = Spr(16, 16)
    s.shadow(9, 13, 7, 2.2)
    for y in range(8, 14):
        for x in range(3, 13):
            s.put(x, y, 'D' if x < 5 else 'B' if x > 10 else 'b' if (x + y) % 4 else 'B')
    s.put(2, 13, 'b'); s.put(13, 13, 'B'); s.put(1, 14, 'b'); s.put(14, 14, 'B')
    s.outline()
    for (x, y) in terrain.blob(7.5, 8, 5.2, 2.6):
        d = ((x - 7.5) / 5.2) ** 2 + ((y - 8) / 2.6) ** 2
        k = 'z' if d < 0.15 else 'J' if d < 0.45 else 'D' if d < 0.55 else 'J' if d < 0.8 else 'd'
        s.put(x, y, k)
    for x in range(3, 13):
        if s.get(x, 5): s.put(x, 5, 'o')
    for (x, y) in terrain.blob(7.5, 8, 5.2, 2.6):
        if not any((x + dx, y + dy) in set(terrain.blob(7.5, 8, 5.2, 2.6)) for dx, dy in ((0, -1),)): s.put(x, y - 1, 'o')
    for (x, y, k) in ((1, 11, 'J'), (2, 11, 'd'), (14, 11, 'J'), (12, 15, 'd'), (13, 15, 'J'), (4, 15, 'J'), (0, 14, 'd')):
        s.put(x, y, k)
    s.save(out, 'obj_stump')

    def ring(s, oy, lit=False):
        """A ring of fire stones round a bed of ash."""
        s.shadow(9, 12 + oy, 7.5, 2.6)
        for (x, y) in terrain.blob(8, 10.5 + oy, 5.5, 3):
            s.put(x, y, 'g' if (x + y) % 3 else 'G')
        for (x, y) in terrain.blob(8, 10.5 + oy, 3.5, 1.8):
            s.put(x, y, ('y' if (x * 3 + y) % 4 else 'Y') if not lit else ('m' if (x + y) % 2 else 'R'))
        stones = []
        for i in range(9):
            a = i / 9 * 2 * math.pi + 0.3
            stones.append((8 + 6 * math.cos(a), 10.5 + oy + 3.4 * math.sin(a)))
        for (sx, sy) in sorted(stones, key=lambda p: p[1]):
            pts = set(terrain.blob(sx, sy, 1.6, 1.2))
            for (x, y) in pts:
                k = 'y'
                if (x, y - 1) not in pts or (x - 1, y) not in pts: k = 'Y'
                if (x, y + 1) not in pts or (x + 1, y) not in pts: k = 'G'
                s.put(x, y, k)
            for (x, y) in pts:
                if (x, y + 1) not in pts: s.put(x, y + 1, 'o')

    # --- a cold fire pit: a ring of stones, ash and a charred log end
    s = Spr(16, 16)
    ring(s, 0)
    for (x, y, k) in ((6, 10, 'o'), (7, 10, 'g'), (8, 11, 'o'), (9, 10, 'B'), (10, 11, 'g'), (7, 11, 'W')):
        s.put(x, y, k)
    s.save(out, 'obj_firepit')

    # --- the same ring with a small fire going, logs crossed
    s = Spr(16, 20)
    ring(s, 4, lit=True)
    for i in range(8):        # two logs crossed over the coals
        for (x, y, k) in ((4 + i, 15 - i // 2, 'b'), (4 + i, 16 - i // 2, 'B'), (11 - i, 15 - i // 2, 'D'), (11 - i, 16 - i // 2, 'b')):
            s.put(x, y, k)
    s.put(3, 15, 'J'); s.put(12, 15, 'J')
    flame = [
        '.......a........',
        '......aa...a....',
        '......ara..a....',
        '.....aarRa.aa...',
        '....aarRRaarRa..',
        '....arRRWRrRRa..',
        '...aarRWWRRRra..',
        '...arRRWaRRmRa..',
        '...aRRmRRRmmRa..',
        '....aRmmRmmRa...',
        '.....aRmmmRa....',
    ]
    for j, row in enumerate(flame):
        for i, ch in enumerate(row):
            if ch != '.': s.put(i, j + 1, ch)
    s.save(out, 'obj_campfire')

    # --- a sea chest washed up: iron bands, half sunk in the sand
    s = Spr(16, 16)
    s.shadow(9, 13, 7.5, 2)
    for y in range(4, 14):
        for x in range(2, 14):
            if y < 7:
                k = 'D' if y == 4 else 'b'          # the curved lid
                if (x in (2, 13)) and y == 4: continue
            else:
                k = 'b' if (y - 7) % 3 else 'B'
                if x < 4: k = 'D' if y % 3 else 'b'
            if x >= 12: k = 'B'
            s.put(x, y, k)
    s.outline()
    for x in range(1, 15):
        if s.get(x, 7): s.put(x, 7, 'o')
    for bx in (4, 11):                              # iron bands, rusted
        for y in range(3, 14):
            if s.get(bx, y): s.put(bx, y, 'G' if y % 4 else 'R'); s.put(bx + 1, y, 'g')
    for (x, y, k) in ((7, 7, 'g'), (8, 7, 'g'), (7, 8, 'G'), (8, 8, 'y'), (7, 9, 'g'), (8, 9, 'g')):   # the hasp
        s.put(x, y, k)
    sand = set(terrain.blob(4, 14, 7, 3.2)) | set(terrain.blob(12, 15, 5, 2))
    for (x, y) in sand:
        s.put(x, y, 'z' if (x, y - 1) not in sand else ('d' if (x + y) % 5 else 'D'))
    for (x, y) in ((13, 9), (13, 10), (14, 11), (14, 12)): s.put(x, y, 'v')    # a strand of weed
    s.save(out, 'obj_sea_chest')

    # --- a barrel stove in, lying on its side
    s = Spr(16, 16)
    s.shadow(9, 13, 7.5, 2)
    for y in range(6, 14):
        for x in range(3, 15):
            k = 'D' if y < 8 else 'b' if y < 12 else 'B'
            if (y - 6) % 3 == 0 and y > 6: k = 'B'
            s.put(x, y, k)
    s.outline()
    for hx in (6, 11):                              # hoops
        for y in range(6, 14): s.put(hx, y, 'g' if y > 7 else 'G')
    # the open end, staves broken in
    for (x, y) in terrain.blob(3, 9.5, 2.2, 3.6):
        s.put(x, y, 'e')
    for (x, y) in ((1, 7), (1, 12), (3, 6), (3, 13), (2, 6), (0, 9), (0, 10)):
        s.put(x, y, 'o')
    for (x, y, k) in ((1, 8, 'd'), (2, 9, 'D'), (1, 11, 'b'), (0, 8, 'd')):
        s.put(x, y, k)
    for (x, y, k) in ((9, 5, 'o'), (10, 4, 'd'), (11, 4, 'o'), (9, 7, 'e'), (10, 7, 'e'), (10, 6, 'o')):   # a stave sprung loose
        s.put(x, y, k)
    s.save(out, 'obj_barrel')

    # --- a boar hide stretched on a pole frame to dry
    s = Spr(16, 24)
    s.shadow(9, 22, 7, 1.8)
    for px in (1, 13):
        for y in range(1, 23):
            s.put(px, y, 'D' if y > 1 else 'd'); s.put(px + 1, y, 'B')
    for x in range(0, 16):
        s.put(x, 3, 'b'); s.put(x, 4, 'B')
        s.put(x, 18, 'b'); s.put(x, 19, 'B')
    s.outline()
    hide = set(terrain.blob(8, 11, 5.2, 6.2)) - {(x, y) for x in range(16) for y in range(0, 6) if abs(x - 8) > 2} \
        | {(5, 6), (11, 6), (4, 16), (12, 16)}
    for (x, y) in hide:
        k = 'j' if (x * 3 + y) % 5 else 'J'          # the flesh side, scraped pale
        if (x - 8) ** 2 + (y - 11) ** 2 < 6: k = 'J'
        if (x + 1, y) not in hide or (x, y + 1) not in hide: k = 'b'
        if (x - 1, y) not in hide or (x, y - 1) not in hide: k = 'B'
        s.put(x, y, k)
    for (x, y) in ((8, 5), (8, 4), (3, 7), (13, 7), (3, 15), (13, 15), (5, 17), (11, 17)):   # lashings
        s.put(x, y, 'z')
    for (x, y) in ((4, 7), (12, 7), (4, 15), (12, 15)): s.put(x, y, 'o')
    for (x, y) in ((7, 9), (9, 12), (6, 13)): s.put(x, y, 'q')
    s.save(out, 'obj_hide_frame')

    # --- a wide working stump with a hatchet bitten into it
    s = Spr(16, 16)
    s.shadow(9, 14, 7.5, 2)
    for y in range(9, 15):
        for x in range(1, 15):
            s.put(x, y, 'D' if x < 3 else 'B' if x > 12 else 'b' if (x * 3 + y) % 5 else 'B')
    s.outline()
    top = set(terrain.blob(7.5, 9, 6.6, 2.8))
    for (x, y) in top:
        d = ((x - 7.5) / 6.6) ** 2 + ((y - 9) / 2.8) ** 2
        s.put(x, y, 'd' if d > 0.75 else 'J' if (int(d * 6)) % 2 else 'D')
    for (x, y) in top:
        if (x, y - 1) not in top: s.put(x, y - 1, 'o')
    for (x, y) in ((3, 10), (5, 9), (9, 10), (11, 8)): s.put(x, y, 'B')        # old cut marks
    # the hatchet: head bitten into the top, handle up to the right
    for i in range(7):
        s.put(9 + i // 1 - 0, 7 - i, 'o'); s.put(9 + i, 8 - i, 'D' if i < 5 else 'b'); s.put(10 + i, 8 - i, 'o')
    for (x, y, k) in ((6, 7, 'o'), (7, 7, 'G'), (8, 7, 'y'), (9, 7, 'g'), (6, 8, 'o'), (7, 8, 'Y'), (8, 8, 'G'), (9, 8, 'g'),
                      (5, 7, 'o'), (6, 6, 'o'), (7, 6, 'o'), (8, 6, 'o')):
        s.put(x, y, k)
    for (x, y, k) in ((0, 15, 'J'), (14, 15, 'd'), (15, 13, 'J')): s.put(x, y, k)
    s.save(out, 'obj_workstump')


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
        styles = area.pop('styles', {})
        data['areas'].append(area)
        if area.get('indoor'):
            img, over, _ = tile_painter(area['rows'])
        else:
            img, over = terrain.paint(area['rows'])
            img, over = img.copy(), over.copy()
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
                style = dict({'wall': 'stone' if pick in (1, 4) else 'plank', 'roof': ('thatch', 'slate', 'tile')[pick % 3]},
                             **styles.get('%d,%d' % (x, y), {}))
                paint_house(img, x, y, x1 - x + 1, y1 - y + 1, door_x=door, over=over, **style)
        img.save(os.path.join(out, area['id'] + '.png'))
        over.save(os.path.join(out, area['id'] + '_over.png'))
    open(data_path, 'w').write(json.dumps(data, indent=2, ensure_ascii=False) + '\n')

    for name, text in OBJECTS.items():
        object_sprite(text).save(os.path.join(out, name + '.png'))
    big_objects(out)
    fx_sprites(out)
    print('world: %d areas' % len(AREAS))


if __name__ == '__main__':
    main()
