"""The road from Skelmed to Eastmere (David, 22 Oct): the game's first travel section, walked tile by tile like
everywhere else. Five areas, each made here and painted by world.py:

  forest_road  out of the forest above Skelmed: a camp clearing, the charcoal burners up a side track,
               the burnt cart where you find Wren, her father down in a hollow, and the crossroads
  old_track    the short way: narrow, dark, something sick in the trees, the old watchtower where the
               burners' boy is held
  greyfell     the long way: open moor, cairns, the hill shrine where a wounded deserter shelters
  brannock     the river: the king's men taking a toll on the only bridge, and a ford upstream
  thornby      Wren's village: six houses round a well that's gone foul, and the road on east to Eastmere

All names are placeholders. The tile letters are listed at the top of world.py.
"""
import math, random


def grid(W, H, fill='.'):
    g = [[fill] * W for _ in range(H)]

    def setc(x, y, t):
        if 0 <= x < W and 0 <= y < H: g[y][x] = t

    def rect(x0, y0, x1, y1, t, keep=None):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if 0 <= y < H and 0 <= x < W and (keep is None or g[y][x] in keep): g[y][x] = t

    def blob(cx, cy, rx, ry, t, keep=None):
        for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
            for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                    if 0 <= y < H and 0 <= x < W and (keep is None or g[y][x] in keep): g[y][x] = t

    return g, setc, rect, blob


def woods(g, rnd, p_edge=0.95, p_in=0.5, edge=2):
    H, W = len(g), len(g[0])
    for y in range(H):
        for x in range(W):
            e = min(x, W - 1 - x, y, H - 1 - y)
            if rnd.random() < (p_edge if e < edge else p_in): g[y][x] = 'T'


def walk_path(setc, g, pts, width=2, tile='='):
    """A trodden path through a list of points; every step joins edge to edge (you walk in four directions), and
    there are bridges where it crosses water."""
    def lay(x, y, horiz):
        for d in range(width):
            xx, yy = (x, y + d) if horiz else (x + d, y)
            if 0 <= yy < len(g) and 0 <= xx < len(g[0]):
                setc(xx, yy, 'b' if g[yy][xx] in 'wb' else tile)
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        horiz = abs(x1 - x0) >= abs(y1 - y0)
        n = max(abs(x1 - x0), abs(y1 - y0), 1)
        px, py = x0, y0
        for i in range(n + 1):
            x = round(x0 + (x1 - x0) * i / n)
            y = round(y0 + (y1 - y0) * i / n)
            if x != px and y != py: lay(x, py, horiz)     # the corner, so it isn't only touching diagonally
            lay(x, y, horiz)
            px, py = x, y


def flowers(g, rnd, n, keep='.'):
    H, W = len(g), len(g[0])
    for _ in range(n):
        x, y = rnd.randrange(W), rnd.randrange(H)
        if g[y][x] in keep: g[y][x] = 'o'


def rows(g):
    return [''.join(r) for r in g]


CAMP = ['follower:rook|follower:wren']   # the fire's for talking with whoever's walking with you


# ============================================================================================== the forest road

def forest_road():
    W, H = 46, 34
    g, setc, rect, blob = grid(W, H)
    rnd = random.Random(401)
    woods(g, rnd, p_in=0.42)

    road = lambda x: int(round(23 - 3.0 * math.sin(x / 8.0) + 1.5 * math.sin(x / 3.7)))
    # the stream from the north, across the road and away south
    stream = lambda y: int(round(18 + 1.6 * math.sin(y / 3.4)))
    for y in range(H):
        setc(stream(y), y, 'w'); setc(stream(y) + 1, y, 'w')

    # the camp clearing north of the road, by the stream: an old fire ring, deadwood, mushrooms
    blob(11, 14, 6, 4.2, '.')
    blob(11, 14, 3, 2, 'g', keep='.')
    # the burners' clearing up the side track: kilns, a turf hut, woodpiles
    blob(30, 6, 8.5, 4.6, '.')
    rect(34, 3, 36, 4, 'H')                 # the hut
    setc(25, 5, 'j'); setc(26, 5, 'j'); setc(36, 7, 'j')
    # the cart's clearing by the road near the east end, and the hollow south of it where Tobin crawled
    blob(36, 21, 5, 3, '.')
    blob(38, 29.5, 4, 2.6, '.')
    # the crossroads clearing
    blob(43, 18, 3, 4, '.')

    # the road: in from the west (the forest above Skelmed), out east (the old track); a branch north (Greyfell)
    pts = [(x, road(x)) for x in range(0, W, 3)] + [(W - 1, road(W - 1))]
    walk_path(setc, g, pts)
    for y in range(0, road(43) + 1): setc(43, y, '='); setc(44, y, '=')
    # the side track up to the burners, the track to the camp
    walk_path(setc, g, [(27, road(27)), (27, 12), (29, 9)], width=1)
    walk_path(setc, g, [(11, road(11)), (11, 17)], width=1)
    # tracks off the road to the hollow (only marks in the grass: no path)
    for y in range(road(39) + 2, 28): setc(39, y, '.')

    # long grass and flowers in the clearings
    for (x0, y0, x1, y1) in ((3, 26, 6, 29), (20, 27, 24, 30), (31, 25, 33, 28)):
        rect(x0, y0, x1, y1, 'g', keep='.T')
    flowers(g, rnd, 30)
    for (x, y) in ((8, 12), (15, 16), (22, 9), (38, 9), (41, 26), (5, 20)):
        if g[y][x] in '.go': setc(x, y, 'r')

    objects = [
        {'id': 'fr_sign', 'kind': 'sign', 'sprite': 'obj_sign', 'x': 2, 'y': road(2) - 1,
         'text': 'An old marker post, half eaten by moss. EAST: EASTMERE. Below it someone has carved a crude kiln and an arrow pointing up the road.'},
        {'id': 'fr_fire', 'kind': 'spot', 'sprite': 'obj_firepit', 'x': 11, 'y': 13, 'scene': 'campfire'},
        {'id': 'fr_wood1', 'kind': 'item', 'sprite': 'obj_log', 'x': 7, 'y': 15, 'scene': 'road_wood1', 'when': ['!flag:road_wood1_today']},
        {'id': 'fr_wood2', 'kind': 'item', 'sprite': 'obj_log', 'x': 15, 'y': 12, 'scene': 'road_wood2', 'when': ['!flag:road_wood2_today']},
        {'id': 'fr_mush', 'kind': 'item', 'sprite': 'obj_yarrow', 'x': 8, 'y': 11, 'scene': 'road_mushrooms', 'when': ['!flag:road_mushrooms_today']},
        # the charcoal burners
        {'id': 'hesta', 'kind': 'npc', 'sprite': 'npc_hesta', 'x': 30, 'y': 6, 'facing': 'down', 'scene': 'hesta'},
        {'id': 'col_home', 'kind': 'npc', 'sprite': 'npc_col', 'x': 33, 'y': 7, 'facing': 'left', 'scene': 'col', 'when': ['flag:col_freed']},
        {'id': 'kiln1', 'kind': 'sign', 'sprite': 'obj_kiln', 'x': 27, 'y': 4,
         'text': 'A charcoal kiln: a dome of turf over a stack of wood, smoking slow. It will burn like this for days. The heat comes off it like a sleeping animal.'},
        {'id': 'kiln2', 'kind': 'sign', 'sprite': 'obj_kiln', 'x': 31, 'y': 3,
         'text': 'A kiln gone out and cold. Nobody has opened it. Whoever tends it has had other things to think about.'},
        # the burnt cart, and Wren by it until she comes with you
        {'id': 'cart', 'kind': 'sign', 'sprite': 'obj_cart_burnt', 'x': 36, 'y': road(36) - 2, 'scene': 'cart'},
        {'id': 'wren', 'kind': 'npc', 'sprite': 'npc_wren', 'x': 34, 'y': road(34) - 2, 'facing': 'right', 'scene': 'wren_meet',
         'when': ['!flag:wren_met']},
        {'id': 'fr_tracks', 'kind': 'sign', 'sprite': 'obj_tracks', 'x': 39, 'y': road(39) + 3, 'scene': 'tracks', 'when': ['flag:wren_met', '!flag:tobin_found']},
        {'id': 'tobin', 'kind': 'npc', 'sprite': 'npc_tobin', 'x': 38, 'y': 30, 'facing': 'up', 'scene': 'tobin',
         'when': ['flag:wren_met', '!flag:tobin_carried', '!flag:tobin_left_food', '!flag:tobin_with_wren']},
        # if you left them there, they stay, until her mother sends men for them
        {'id': 'tobin_left', 'kind': 'npc', 'sprite': 'npc_tobin', 'x': 38, 'y': 30, 'facing': 'up', 'scene': 'wren_hollow',
         'when': ['flag:tobin_with_wren', '!flag:tobin_rescued']},
        {'id': 'wren_left', 'kind': 'npc', 'sprite': 'npc_wren', 'x': 37, 'y': 30, 'facing': 'right', 'scene': 'wren_hollow',
         'when': ['flag:tobin_with_wren', '!flag:tobin_rescued']},
        {'id': 'cross_sign', 'kind': 'sign', 'sprite': 'obj_sign', 'x': 42, 'y': road(42) - 2,
         'text': 'A crossroads post. EAST: THE OLD TRACK, BRANNOCK BRIDGE. NORTH: GREYFELL. Someone has added under EAST, in charcoal: DESERTERS. and under NORTH: COLD.'},
    ]
    return {
        'id': 'forest_road',
        'name': 'The forest road',
        'minutes': 2, 'eventChance': 0, 'fightChance': 0, 'fightGap': 14,
        'styles': {'34,3': {'wall': 'log', 'roof': 'shingle', 'smoke': True, 'moss': True}},
        'doors': [(35, 4)],
        'spots': [
            {'id': 'fr_west', 'x': 1, 'y': road(1), 'facing': 'right'},
            {'id': 'fr_east', 'x': W - 2, 'y': road(W - 2), 'facing': 'left'},
            {'id': 'fr_north', 'x': 43, 'y': 1, 'facing': 'down'},
            {'id': 'fr_camp', 'x': 12, 'y': 15, 'facing': 'left'},
        ],
        'objects': objects,
        'zones': [
            {'x0': 0, 'y0': road(0) - 1, 'x1': 0, 'y1': road(0) + 2, 'to': 'hills_east'},
            {'x0': W - 1, 'y0': road(W - 1) - 1, 'x1': W - 1, 'y1': road(W - 1) + 2, 'to': 'ot_west'},
            {'x0': 43, 'y0': 0, 'x1': 44, 'y1': 0, 'to': 'gf_south'},
            # first sight of the cart from the road
            {'x0': 29, 'y0': 19, 'x1': 41, 'y1': 28, 'scene': 'cart_seen', 'when': ['!flag:cart_seen']},
        ],
        'uses': [{'tile': 'w', 'scene': 'road_stream'}],
        'events': [],
        'rows': rows(g),
    }


# ============================================================================================== the old track

def old_track():
    W, H = 38, 34
    g, setc, rect, blob = grid(W, H)
    rnd = random.Random(402)
    woods(g, rnd, p_in=0.62)          # close and dark

    # the old watchtower in a clearing on a rise to the north, the deserters' camp round it
    blob(25, 7, 7, 4.5, '.')
    rect(24, 3, 26, 5, 'H')
    setc(21, 8, 'j'); setc(29, 9, 'K')
    # a hollow where the deer lies, a soft patch where the ground gives way
    blob(9, 20, 3, 2.2, '.')
    blob(18, 15, 2.5, 2, 'g')
    # the track: narrow, one tile, winding
    pts = [(0, 27), (5, 27), (8, 22), (13, 19), (18, 15), (22, 15), (27, 17), (31, 14), (35, 15), (W - 1, 15)]
    walk_path(setc, g, pts, width=1)
    walk_path(setc, g, [(25, 15), (25, 9)], width=1)
    for (x, y) in ((6, 25), (12, 16), (30, 18), (33, 12), (16, 24)):
        if g[y][x] == 'T': setc(x, y, 'k')
    for (x, y) in ((20, 6), (30, 5), (8, 18)):
        if g[y][x] in '.g': setc(x, y, 'r')

    objects = [
        {'id': 'deer', 'kind': 'sign', 'sprite': 'obj_deer_dead', 'x': 9, 'y': 20, 'scene': 'deer'},
        {'id': 'deserter1', 'kind': 'npc', 'sprite': 'npc_deserter', 'x': 24, 'y': 7, 'facing': 'down', 'scene': 'deserters',
         'when': ['!flag:col_freed', '!flag:deserters_gone']},
        {'id': 'deserter2', 'kind': 'npc', 'sprite': 'npc_deserter', 'x': 27, 'y': 8, 'facing': 'left', 'scene': 'deserters',
         'when': ['!flag:col_freed', '!flag:deserters_gone']},
        {'id': 'col', 'kind': 'npc', 'sprite': 'npc_col', 'x': 26, 'y': 6, 'facing': 'down', 'scene': 'deserters',
         'when': ['!flag:col_freed']},
        {'id': 'ot_fire', 'kind': 'spot', 'sprite': 'obj_firepit', 'x': 22, 'y': 9, 'scene': 'campfire', 'when': ['flag:deserters_gone|flag:col_freed']},
    ]
    return {
        'id': 'old_track',
        'name': 'The old forest track',
        'minutes': 2, 'eventChance': 0, 'fightChance': 0, 'fightGap': 14,
        'styles': {'24,3': {'wall': 'stone', 'roof': 'slate', 'boarded': True, 'moss': True}},
        'spots': [
            {'id': 'ot_west', 'x': 1, 'y': 27, 'facing': 'right'},
            {'id': 'ot_east', 'x': W - 2, 'y': 15, 'facing': 'left'},
        ],
        'objects': objects,
        'zones': [
            {'x0': 0, 'y0': 26, 'x1': 0, 'y1': 28, 'to': 'fr_east'},
            {'x0': W - 1, 'y0': 14, 'x1': W - 1, 'y1': 16, 'to': 'br_west_south'},
            # the ground gives under Rook
            {'x0': 17, 'y0': 14, 'x1': 19, 'y1': 16, 'scene': 'rook_fall', 'when': ['follower:rook', '!flag:rook_fell']},
            # the deserters see you coming up the rise
            {'x0': 24, 'y0': 11, 'x1': 26, 'y1': 12, 'scene': 'deserters', 'when': ['!flag:col_freed', '!flag:deserters_gone', '!flag:tower_seen']},
        ],
        'uses': [],
        'events': [],
        'rows': rows(g),
    }


# ============================================================================================== Greyfell

def greyfell():
    W, H = 38, 30
    g, setc, rect, blob = grid(W, H, 'g')
    rnd = random.Random(403)
    # open moor: long grass, short turf, rock, a few stunted trees, an outcrop with a cliff
    for y in range(H):
        for x in range(W):
            r = rnd.random()
            if r < 0.30: g[y][x] = '.'
            elif r < 0.33: g[y][x] = 'r'
            elif r < 0.345: g[y][x] = 'T'
    rect(0, H - 2, W - 1, H - 1, 'T')
    rect(0, 0, W - 1, 0, 'h')
    blob(9, 8, 5, 3.5, 'h')
    blob(9, 8, 3.2, 2, 'u')
    setc(9, 11, 's')
    # the shrine on its knoll, with a spring below it
    blob(22, 9, 6, 4, '.')
    rect(21, 6, 23, 8, 'H')
    blob(31, 6, 2, 1.4, 'w')
    # the path: up from the south-west, past the cairns, by the shrine, down east to the river
    pts = [(4, H - 1), (5, 24), (9, 20), (14, 16), (19, 12), (22, 10), (27, 10), (31, 13), (W - 1, 14)]
    walk_path(setc, g, pts, width=1)
    walk_path(setc, g, [(9, 12), (11, 16)], width=1)

    objects = [
        {'id': 'cairn1', 'kind': 'sign', 'sprite': 'obj_cairn', 'x': 7, 'y': 21,
         'text': 'A cairn: a stack of stones as tall as you. Travellers add a stone for luck. You add one. It feels like the right thing to do up here.'},
        {'id': 'cairn2', 'kind': 'sign', 'sprite': 'obj_cairn', 'x': 16, 'y': 16,
         'text': 'Another cairn. Someone has wedged a wooden token between the stones: a little carved boat.'},
        {'id': 'gf_lookout', 'kind': 'spot', 'sprite': 'obj_cairn', 'x': 9, 'y': 7, 'scene': 'gf_lookout'},
        {'id': 'sorrel', 'kind': 'npc', 'sprite': 'npc_sorrel', 'x': 24, 'y': 9, 'facing': 'left', 'scene': 'sorrel',
         'when': ['!flag:sorrel_gone']},
        {'id': 'gf_fire', 'kind': 'spot', 'sprite': 'obj_firepit', 'x': 20, 'y': 10, 'scene': 'campfire'},
        {'id': 'shrine', 'kind': 'sign', 'x': 22, 'y': 8, 'scene': 'shrine'},
    ]
    return {
        'id': 'greyfell',
        'name': 'Greyfell',
        'minutes': 2, 'eventChance': 0, 'fightChance': 0, 'fightGap': 14,
        'styles': {'21,6': {'wall': 'stone', 'roof': 'slate', 'moss': True}},
        'doors': [(22, 8)],
        'spots': [
            {'id': 'gf_south', 'x': 4, 'y': H - 3, 'facing': 'up'},
            {'id': 'gf_east', 'x': W - 2, 'y': 14, 'facing': 'left'},
        ],
        'objects': objects,
        'zones': [
            {'x0': 3, 'y0': H - 1, 'x1': 5, 'y1': H - 1, 'to': 'fr_north'},
            {'x0': W - 1, 'y0': 13, 'x1': W - 1, 'y1': 15, 'to': 'br_west_north'},
        ],
        'uses': [{'tile': 'w', 'scene': 'road_stream'}],
        'events': [],
        'rows': rows(g),
    }


# ============================================================================================== Brannock bridge

def brannock():
    W, H = 40, 32
    g, setc, rect, blob = grid(W, H)
    rnd = random.Random(404)
    woods(g, rnd, p_edge=0.9, p_in=0.12)
    # the river, north to south, four wide, a little wider at the ford where it runs shallow over stones
    river = lambda y: int(round(18 + 1.5 * math.sin(y / 5.0)))
    for y in range(H):
        x0 = river(y)
        for x in range(x0, x0 + 4): setc(x, y, 'w')
        setc(x0 - 1, y, ',' if rnd.random() < 0.5 else g[y][x0 - 1])
        setc(x0 + 4, y, ',' if rnd.random() < 0.5 else g[y][x0 + 4])
    for y in (3, 4):
        for x in range(river(y) - 1, river(y) + 5): setc(x, y, ',')
        for x in range(river(y), river(y) + 4): setc(x, y, 'w')
    # roads: from the old track (south-west) and Greyfell (north-west) to the bridge; on east to Thornby
    by = 17
    walk_path(setc, g, [(0, 15), (6, 15), (10, by), (river(by) - 1, by)])
    walk_path(setc, g, [(0, 25), (6, 25), (10, 21), (10, by)])
    for x in range(river(by), river(by) + 4):
        setc(x, by, 'b'); setc(x, by + 1, 'b')
    walk_path(setc, g, [(river(by) + 4, by), (W - 1, by)])
    # the path up the west bank to the ford, and down the east bank from it
    walk_path(setc, g, [(9, by), (9, 8), (river(4) - 1, 4)], width=1)
    walk_path(setc, g, [(river(4) + 4, 4), (river(4) + 7, 9), (river(4) + 7, by)], width=1)
    # the toll house on the west bank
    rect(11, 12, 14, 14, 'H')
    setc(15, 14, 'n'); setc(11, 15, 'K')
    flowers(g, rnd, 25)

    bx = river(by) - 1
    objects = [
        {'id': 'vane', 'kind': 'npc', 'sprite': 'npc_vane', 'x': bx, 'y': by, 'facing': 'left', 'scene': 'toll', 'when': ['!flag:toll_done']},
        {'id': 'toll1', 'kind': 'npc', 'sprite': 'npc_toll', 'x': bx, 'y': by + 1, 'facing': 'left', 'scene': 'toll', 'when': ['!flag:toll_done']},
        {'id': 'toll2', 'kind': 'npc', 'sprite': 'npc_toll', 'x': 12, 'y': 16, 'facing': 'down', 'scene': 'toll', 'when': ['!flag:toll_done']},
        {'id': 'br_sign', 'kind': 'sign', 'sprite': 'obj_sign', 'x': 8, 'y': by - 2,
         'text': "A new board, nailed over an old one. BRANNOCK BRIDGE. TOLL BY ORDER OF HIS MAJESTY KING MELVIN. Under it, the old board's last line still shows: FREE PASSAGE TO ALL."},
        {'id': 'br_fire', 'kind': 'spot', 'sprite': 'obj_firepit', 'x': 5, 'y': 21, 'scene': 'campfire'},
    ]
    return {
        'id': 'brannock',
        'name': 'Brannock bridge',
        'minutes': 2, 'eventChance': 0, 'fightChance': 0, 'fightGap': 14,
        'styles': {'11,12': {'wall': 'plank', 'roof': 'tile', 'smoke': True}},
        'doors': [(12, 14)],
        'spots': [
            {'id': 'br_west_north', 'x': 1, 'y': 15, 'facing': 'right'},
            {'id': 'br_west_south', 'x': 1, 'y': 25, 'facing': 'right'},
            {'id': 'br_east', 'x': W - 2, 'y': by, 'facing': 'left'},
            {'id': 'br_ford_west', 'x': river(4) - 2, 'y': 4, 'facing': 'right'},
            {'id': 'br_ford_east', 'x': river(4) + 5, 'y': 4, 'facing': 'left'},
            {'id': 'br_bridge_east', 'x': river(by) + 4, 'y': by, 'facing': 'right'},
        ],
        'objects': objects,
        'zones': [
            {'x0': 0, 'y0': 14, 'x1': 0, 'y1': 16, 'to': 'gf_east'},
            {'x0': 0, 'y0': 24, 'x1': 0, 'y1': 26, 'to': 'ot_east'},
            {'x0': W - 1, 'y0': by - 1, 'x1': W - 1, 'y1': by + 2, 'to': 'th_west'},
            # wading the ford, either way
            {'x0': river(4) - 1, 'y0': 3, 'x1': river(4) - 1, 'y1': 4, 'scene': 'ford'},
            {'x0': river(4) + 4, 'y0': 3, 'x1': river(4) + 4, 'y1': 4, 'scene': 'ford_back'},
            # the king's men call out as you come up to the bridge
            {'x0': bx - 3, 'y0': by - 1, 'x1': bx - 2, 'y1': by + 2, 'scene': 'toll', 'when': ['!flag:toll_done', '!flag:toll_seen']},
        ],
        'uses': [{'tile': 'w', 'scene': 'road_stream'}],
        'events': [],
        'rows': rows(g),
    }


# ============================================================================================== Thornby

def thornby():
    W, H = 36, 28
    g, setc, rect, blob = grid(W, H)
    rnd = random.Random(405)
    woods(g, rnd, p_edge=0.9, p_in=0.08)
    rect(2, 9, W - 3, 21, '.')
    # the green round the well, trodden to mud; the road through, west to east
    blob(17, 14, 6, 3.5, 'm')
    setc(17, 14, 'O')
    walk_path(setc, g, [(0, 17), (11, 17), (17, 17), (W - 1, 15)], tile='m')
    # six houses round the green; two of them empty
    houses = [(7, 9, 10, 11, 9), (12, 8, 15, 10, 13), (19, 8, 22, 10, 21), (24, 9, 27, 11, 25), (8, 19, 11, 21, 9), (23, 19, 26, 21, 24)]
    doors = []
    for (x0, y0, x1, y1, dx) in houses:
        rect(x0, y0, x1, y1, 'H')
        doors.append((dx, y1))
        for y in range(y1 + 1, 14): setc(dx, y, 'm')
        for y in range(15, y0): setc(dx, y, 'm')
    # the barn, the stall, gardens, a woodpile, barrels
    rect(29, 18, 32, 21, 'H')
    doors.append((30, 21))
    setc(20, 12, 'S'); setc(21, 12, 'S'); setc(22, 12, 'K')
    rect(3, 10, 5, 12, 'v'); rect(28, 10, 30, 11, 'v')
    setc(6, 19, 'j'); setc(12, 11, 'n'); setc(27, 18, 'n')
    # the spring the well's fed from, up in the trees to the north-east, and the path to it
    blob(29, 4, 2.5, 1.6, 'w')
    blob(29, 5, 4, 3, '.', keep='T.')
    for (x, y) in ((28, 2), (29, 2), (30, 2)): setc(x, y, 'w')
    walk_path(setc, g, [(23, 13), (23, 7), (28, 7)], width=1)
    flowers(g, rnd, 20)

    objects = [
        {'id': 'marda', 'kind': 'npc', 'sprite': 'npc_marda', 'x': 16, 'y': 12, 'facing': 'down', 'scene': 'marda'},
        {'id': 'fenn', 'kind': 'npc', 'sprite': 'npc_fenn', 'x': 21, 'y': 13, 'facing': 'down', 'scene': 'fenn'},
        {'id': 'ness', 'kind': 'npc', 'sprite': 'npc_ness', 'x': 13, 'y': 12, 'facing': 'down', 'scene': 'ness'},
        {'id': 'wren_home', 'kind': 'npc', 'sprite': 'npc_wren', 'x': 14, 'y': 12, 'facing': 'down', 'scene': 'wren_home',
         'when': ['flag:wren_home']},
        {'id': 'tobin_home', 'kind': 'npc', 'sprite': 'npc_tobin', 'x': 12, 'y': 12, 'facing': 'down', 'scene': 'tobin_home',
         'when': ['flag:tobin_home']},
        {'id': 'carcass', 'kind': 'sign', 'sprite': 'obj_boar_dead', 'x': 28, 'y': 5, 'scene': 'carcass', 'when': ['!flag:carcass_burnt']},
        {'id': 'th_sign', 'kind': 'sign', 'sprite': 'obj_sign', 'x': 3, 'y': 15,
         'text': 'THORNBY. Somebody has painted a sheaf of wheat on the post, a long time ago, and nobody has painted it since.'},
        {'id': 'barn', 'kind': 'sign', 'x': 30, 'y': 21, 'scene': 'barn'},
        {'id': 'th_fire', 'kind': 'spot', 'sprite': 'obj_firepit', 'x': 13, 'y': 15, 'scene': 'campfire'},
    ]
    return {
        'id': 'thornby',
        'name': 'Thornby',
        'minutes': 1, 'eventChance': 0, 'fightChance': 0, 'fightGap': 14,
        'camp': False,
        'styles': {
            '7,9': {'wall': 'plank', 'roof': 'thatch', 'smoke': True},
            '12,8': {'wall': 'stone', 'roof': 'thatch', 'smoke': True, 'flowers': True},
            '19,8': {'wall': 'plank', 'roof': 'thatch', 'boarded': True},
            '24,9': {'wall': 'plank', 'roof': 'slate', 'smoke': True},
            '8,19': {'wall': 'plank', 'roof': 'thatch', 'boarded': True},
            '23,19': {'wall': 'stone', 'roof': 'thatch'},
            '29,18': {'wall': 'plank', 'roof': 'thatch', 'moss': True},
        },
        'doors': doors,
        'spots': [
            {'id': 'th_west', 'x': 1, 'y': 17, 'facing': 'right'},
            {'id': 'th_east', 'x': W - 3, 'y': 15, 'facing': 'left'},
        ],
        'objects': objects,
        'zones': [
            {'x0': 0, 'y0': 16, 'x1': 0, 'y1': 18, 'to': 'br_east'},
            {'x0': W - 1, 'y0': 14, 'x1': W - 1, 'y1': 16, 'scene': 'eastmere_end'},
            {'x0': 9, 'y0': 16, 'x1': 11, 'y1': 18, 'scene': 'wren_arrives', 'when': ['follower:wren', '!flag:wren_home']},
        ],
        'uses': [
            {'tile': 'O', 'scene': 'th_well'},
            {'tile': 'w', 'scene': 'th_spring'},
        ],
        'events': [],
        'rows': rows(g),
    }


AREAS = [forest_road, old_track, greyfell, brannock, thornby]
