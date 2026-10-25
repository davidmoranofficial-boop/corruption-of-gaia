"""The battle beasts, built for rig.py and animated (David, 20 Oct: "much better models", "better animations").

Every beast is one sheet of frames in a row, all the same size, always in this order:
    idle 0-3 (breathing), tell 0-1 (gathering itself to strike), attack 0-2 (the lunge and the bite),
    hurt 0 (flinching), dead 0 (down for good)
eleven frames in all. The ground (where the feet stand) is the third row from the bottom of every frame.
The game cuts the sheet up by frame count, so a new beast only needs the same eleven frames.

    python3 Tools/Art/beasts2.py        (also run by build.py; writes Sprites/Enemies/<name>.png)
"""
import math, os
from rig import Mat, Ell, Cap, render, sheet, rot, add, lerp

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'Assets', 'Game', 'Resources', 'Sprites', 'Enemies')

SCALE = 1.0   # main(grid=True) draws them small for the battle grid (25 Oct)

FRAMES = ['idle0', 'idle1', 'idle2', 'idle3', 'tell0', 'tell1', 'atk0', 'atk1', 'atk2', 'hurt', 'dead']


def ik(s, p, l1, l2, bend):
    """Two-bone reach from s to p (x, y); returns the middle joint. bend +1 puts the joint behind (to the right), -1 in front."""
    dx, dy = p[0] - s[0], p[1] - s[1]
    d = math.hypot(dx, dy)
    d = min(max(d, abs(l1 - l2) + 0.01), l1 + l2 - 0.01)
    a = (l1 * l1 - l2 * l2 + d * d) / (2 * d)
    h = math.sqrt(max(0.0, l1 * l1 - a * a))
    ux, uy = dx / d, dy / d
    bx, by = s[0] + ux * a, s[1] + uy * a
    nx, ny = -uy, ux
    # pick the side the joint should stick out to
    e1 = (bx + nx * h, by + ny * h)
    e2 = (bx - nx * h, by - ny * h)
    return e1 if (e1[0] - e2[0]) * bend > 0 else e2


# ------------------------------------------------------------------ the wolf

WOLF = dict(
    fur=['e', 'g', 'G', 'y', 'Y'], pale=['G', 'y', 'Y', 'W', 'W'], dark=['e', 'e', 'g', 'G', 'y'],
    saddle=['e', 'e', 'g', 'G', 'y'], eye='a', pupil='e', size=0.8, scar=False,
)
ALPHA = dict(
    fur=['e', 'e', 'g', 'G', 'y'], pale=['g', 'G', 'y', 'Y', 'W'], dark=['o', 'e', 'e', 'g', 'G'],
    saddle=['o', 'e', 'e', 'g', 'G'], eye='r', pupil='a', size=1.0, scar=True,
)

WOLF_POSES = {
    #        body dx, dy, tilt   head dx, dy, turn   jaw   ears   tail   breathe  eyes   legs (paw dx, dy for fn, ff, hn, hf)
    'idle0': dict(),
    'idle1': dict(dy=0.5, breathe=0.6, hdy=0.6, tail=0.04),
    'idle2': dict(dy=0.9, breathe=1.0, hdy=1.0, tail=0.08),
    'idle3': dict(dy=0.5, breathe=0.6, hdy=0.5, tail=0.04),
    'tell0': dict(dx=3, dy=3.5, tilt=0.06, hdx=-1, hdy=4, hturn=0.18, jaw=0.18, ears=0.55, tail=-0.25,
                  legs=[(1, 0), (2, 0), (-2, 0), (-1, 0)]),
    'tell1': dict(dx=5, dy=6, tilt=0.10, hdx=-1, hdy=6, hturn=0.26, jaw=0.32, ears=0.8, tail=-0.35,
                  legs=[(1, 0), (2, 0), (-4, 0), (-3, 0)]),
    'atk0':  dict(dx=-9, dy=-5, tilt=-0.16, hdx=-3, hdy=-2, hturn=-0.12, jaw=0.62, ears=0.9, tail=0.25,
                  legs=[(-12, -9), (-10, -7), (6, 0), (7, -1)]),
    'atk1':  dict(dx=-16, dy=0, tilt=0.05, hdx=-4, hdy=4, hturn=0.25, jaw=0.08, ears=0.9, tail=0.15,
                  legs=[(-14, 0), (-11, 0), (-6, 0), (-4, -2)]),
    'atk2':  dict(dx=-7, dy=1, tilt=0.02, hdx=-1, hdy=1, hturn=0.08, jaw=0.2, ears=0.5, tail=0.05,
                  legs=[(-6, 0), (-5, 0), (-3, 0), (-2, 0)]),
    'hurt':  dict(dx=5, dy=1, tilt=-0.1, hdx=3, hdy=-3, hturn=-0.42, jaw=0.45, ears=1.0, tail=-0.4, eyes='shut',
                  legs=[(4, 0), (5, 0), (2, 0), (3, 0)]),
    'dead':  dict(dead=True),
}


def wolf_frame(look, P):
    k = look['size']
    W, H = int(116 * k), int(76 * k)
    G = H - 3                                   # the ground
    ox = 26 * k                                  # leave room in front for the lunge
    fur = Mat(look['fur'], under=look['pale'], under_at=0.42, fur=0.07, top=look['saddle'], top_at=-0.62)
    furfar = Mat(look['dark'], fur=0.06)
    pale = Mat(look['pale'], fur=0.08)
    ruff = Mat(look['fur'], under=look['pale'], under_at=0.1, fur=0.12, top=look['saddle'], top_at=-0.5)
    nose = Mat(['o', 'e', 'g'], fur=0.0, shine=0.3)
    mouth = Mat(['m', 'R', 'r'], fur=0.0)
    ear_in = Mat(['Q', 'q', 'q'], fur=0.0)

    dead = P.get('dead')
    dx, dy, tilt = P.get('dx', 0) * k, P.get('dy', 0) * k, P.get('tilt', 0)
    breathe = P.get('breathe', 0)
    if dead:
        dx, dy, tilt = 4 * k, 15 * k, 0.02

    def S(x, y):             # the base pose, scaled, placed in the frame
        return ox + x * k, (y + 7) * k

    centre = S(52, 36)
    def body(x, y, z=0.0):
        bx, by = S(x, y)
        p = rot((bx, by, z * k), centre, tilt)
        return (p[0] + dx, p[1] + dy, p[2])

    prims = []
    # torso: deep chest, tucked barrel, haunch
    prims.append(Ell(*body(40, 34 - breathe * 0.3), 11.5 * k, (10 + breathe * 0.5) * k, 10 * k, fur, rot=-0.15 + tilt))
    prims.append(Ell(*body(54, 33), 13 * k, 8.3 * k, 9.2 * k, fur, rot=0.04 + tilt))
    prims.append(Ell(*body(67, 32), 9.5 * k, 9.5 * k, 9.5 * k, fur, rot=tilt))
    # hackles along the spine
    for i, (hx, hh) in enumerate(((35, 1.6), (40, 2.0), (45, 1.7), (50, 1.2))):
        base = body(hx, 25.5, 0)
        tip = body(hx + 3, 25.5 - hh - (P.get('ears', 0) * 2.2 if i < 3 else 0), 0)
        prims.append(Cap(base, 2.6 * k, tip, 0.8 * k, ruff))

    # neck and head: they hang off the neck base, turned by hturn
    neck_base = body(37, 30)
    hx, hy, turn = P.get('hdx', 0) * k, P.get('hdy', 0) * k, P.get('hturn', 0)
    if dead:
        hx, hy, turn = -2 * k, 9 * k, 0.5
    pivot = (neck_base[0], neck_base[1])
    def head(x, y, z=0.0):
        bx, by = S(x, y)
        bx += dx; by += dy
        p = rot((bx + hx, by + hy, z * k), (pivot[0] + hx, pivot[1] + hy), turn)
        return p
    prims.append(Cap(neck_base, 7.5 * k, head(27, 24), 5.6 * k, ruff))
    prims.append(Ell(*head(32, 30), 7 * k, 8.5 * k, 8.5 * k, ruff, rot=0.3))           # ruff round the throat
    prims.append(Ell(*head(24.5, 21.5), 7.6 * k, 6.4 * k, 6.4 * k, fur, rot=0.15 + turn))  # skull
    jaw = P.get('jaw', 0)
    if dead: jaw = 0.12
    hinge = head(21, 25.5)
    # mouth inside, then the jaws
    if jaw > 0.05:
        prims.append(Cap(head(19, 26.5, -1), 2.2 * k, head(12, 28, -1), 1.6 * k, mouth))
    up_tip = head(10.5, 26.2)
    prims.append(Cap(head(20, 24.2), 4.2 * k, up_tip, 2.4 * k, pale))                     # upper jaw, muzzle
    lo_tip = rot(head(12.5, 28.6), (hinge[0], hinge[1]), -jaw)
    prims.append(Cap(hinge, 2.5 * k, lo_tip, 1.5 * k, pale))                             # lower jaw
    prims.append(Ell(*add(up_tip, (-0.6 * k, -0.6 * k, 0.5 * k)), 1.7 * k, 1.4 * k, 1.5 * k, nose))
    # ears, laid back as it gets angry
    ears = P.get('ears', 0) + (0.9 if dead else 0)
    for z, dxe, mat in ((3.2, 0, fur), (-3.5, -2.5, furfar)):
        root = head(26.5 + dxe, 16.5, z)
        tip = rot(head(29.5 + dxe, 7.5, z), (root[0], root[1]), ears * 0.9)
        prims.append(Cap(root, 3.0 * k, tip, 0.5 * k, mat))
        if z > 0:
            prims.append(Cap(add(root, (0, 0, 0.6 * k)), 1.1 * k, add(lerp(root, tip, 0.6), (0, 0, 0.6 * k)), 0.3 * k, ear_in))

    # legs: shoulder/hip on the body, paws on the ground; elbows and knees found by reaching
    legs = P.get('legs', [(0, 0)] * 4)
    lift = [0, 0, 0, 0]
    def leg(i, top, paw_x, z, mat, front):
        pdx, pdy = legs[i]
        paw = (S(paw_x, 0)[0] + pdx * k + (dx if dead else 0), G - 1.8 * k + pdy * k)
        if dead:   # folded under the body
            paw = (top[0] + (-6 if front else 4) * k, top[1] + 7 * k)
        if front:
            wrist = (paw[0] + 0.8 * k, paw[1] - 4.5 * k)
            elbow = ik(top, wrist, 9.5 * k, 9.0 * k, +1)
            prims.append(Cap((top[0], top[1], z), 4.0 * k, (elbow[0], elbow[1], z), 2.5 * k, mat))
            prims.append(Cap((elbow[0], elbow[1], z), 2.4 * k, (wrist[0], wrist[1], z), 1.8 * k, mat))
            prims.append(Cap((wrist[0], wrist[1], z), 1.8 * k, (paw[0] - 0.6 * k, paw[1], z), 1.7 * k, mat))
            prims.append(Ell(paw[0] - 1.6 * k, paw[1] + 0.2 * k, z, 2.8 * k, 1.6 * k, 2.0 * k, mat))
        else:
            hock = (paw[0] + 3.0 * k, paw[1] - 8.5 * k)
            knee = ik(top, hock, 10 * k, 8.5 * k, -1)
            prims.append(Cap((top[0], top[1], z), 6.0 * k, (knee[0], knee[1], z), 3.0 * k, mat))
            prims.append(Cap((knee[0], knee[1], z), 2.8 * k, (hock[0], hock[1], z), 1.8 * k, mat))
            prims.append(Cap((hock[0], hock[1], z), 1.8 * k, (paw[0], paw[1], z), 1.6 * k, mat))
            prims.append(Ell(paw[0] - 1.4 * k, paw[1] + 0.2 * k, z, 2.8 * k, 1.6 * k, 2.0 * k, mat))

    leg(1, body(45, 38, -6), 45, -6 * k, furfar, True)
    leg(3, body(70, 35, -6), 71, -6 * k, furfar, False)
    leg(0, body(39, 39, 6), 35, 6 * k, fur, True)
    leg(2, body(65, 36, 6), 62, 6 * k, fur, False)

    # tail: a bushy curve that hangs, swings, and lifts when it charges
    t = P.get('tail', 0) + (0.6 if dead else 0)
    root = body(76, 28)
    mid = rot(body(83, 37), (root[0], root[1]), -t)
    tip = rot(body(85, 49), (root[0], root[1]), -t * 1.3)
    prims.append(Cap(root, 3.4 * k, mid, 4.0 * k, ruff))
    prims.append(Cap(mid, 4.0 * k, tip, 1.6 * k, ruff))
    prims.append(Cap(lerp(mid, tip, 0.55), 2.2 * k, tip, 1.0 * k, pale))

    # details: the eye, teeth, a scar on the leader
    det = []
    ex, ey, _ = head(20.5, 20.6, 5)
    if P.get('eyes') == 'shut' or dead:
        det += [(ex - 1, ey, 'e'), (ex, ey, 'e'), (ex + 1, ey - 0.2, 'e')]
    else:
        det += [(ex - 1, ey, 'e'), (ex, ey, look['eye']), (ex + 1, ey, look['eye']), (ex, ey - 1, 'e'), (ex + 1, ey - 1, 'e'),
                (ex - 0.2, ey + 0.1, look['pupil'])]
    if jaw > 0.12 and not dead:
        for f in (0.15, 0.45, 0.8):
            p = lerp(head(19, 26.4), head(11.5, 27.4), f)
            det.append((p[0], p[1], 'W'))
            q = lerp(hinge, lo_tip, f + 0.05)
            det.append((q[0], q[1] - 1.2 * k, 'W'))
    elif not dead:
        p = head(12.6, 27.6)
        det.append((p[0], p[1], 'W'))                     # a fang over the lip
    if look['scar']:
        for f in range(4):
            p = head(22 + f * 0.9, 17.6 + f * 1.5, 6)
            det.append((p[0], p[1], 'R'))
    return render(prims, int(W * SCALE), int(H * SCALE), ground=G, details=det, seed=7, sc=SCALE)


# ------------------------------------------------------------------ the boar

BOAR_POSES = {
    'idle0': dict(),
    'idle1': dict(dy=0.4, breathe=0.6, hdy=0.5),
    'idle2': dict(dy=0.8, breathe=1.0, hdy=1.0, tail=0.3),
    'idle3': dict(dy=0.4, breathe=0.6, hdy=0.4),
    'tell0': dict(dx=3, dy=2.5, tilt=0.05, hdy=4, hturn=0.2, legs=[(2, 0), (3, 0), (-1, 0), (0, 0)], scrape=1),
    'tell1': dict(dx=5, dy=3.5, tilt=0.08, hdy=6, hturn=0.3, legs=[(-3, -4), (3, 0), (-2, 0), (-1, 0)], scrape=2),
    'atk0':  dict(dx=-10, dy=-2, tilt=-0.08, hdy=3, hturn=0.25, legs=[(-12, -5), (-9, -3), (4, 0), (5, 0)]),
    'atk1':  dict(dx=-18, dy=1, tilt=0.06, hdy=6, hturn=-0.35, legs=[(-16, 0), (-14, 0), (-8, 0), (-6, 0)]),
    'atk2':  dict(dx=-8, dy=0.5, tilt=0.02, hdy=2, hturn=0.05, legs=[(-7, 0), (-6, 0), (-4, 0), (-3, 0)]),
    'hurt':  dict(dx=5, dy=0.5, tilt=-0.08, hdx=2, hdy=-3, hturn=-0.35, eyes='shut', legs=[(3, 0), (4, 0), (2, 0), (2, 0)], tail=0.6),
    'dead':  dict(dead=True),
}


def boar_frame(P):
    k = 1.0
    W, H = 118, 72
    G = H - 3
    ox = 26
    hide = Mat(['o', 'B', 'B', 'b', 'q'], under=['o', 'B', 'b', 'q', 'D'], under_at=0.55, fur=0.16, top=['o', 'H', 'B', 'B', 'b'], top_at=-0.5)
    hidefar = Mat(['o', 'o', 'B', 'B', 'b'], fur=0.12)
    bristle = Mat(['o', 'e', 'H', 'B', 'G'], fur=0.2)
    snout = Mat(['Q', 'q', 'S', 's', 's'], fur=0.05)
    hoof = Mat(['o', 'e', 'g', 'G'], fur=0.0, shine=0.4)
    tusk = Mat(['D', 'd', 'W', 'W'], fur=0.0, shine=0.5)

    dead = P.get('dead')
    dx, dy, tilt = P.get('dx', 0), P.get('dy', 0), P.get('tilt', 0)
    if dead: dx, dy, tilt = 3, 11, 0.03
    br = P.get('breathe', 0)

    def S(x, y): return ox + x, y
    centre = S(50, 38)
    def body(x, y, z=0.0):
        p = rot((ox + x, y + 6, z), centre, tilt)
        return (p[0] + dx, p[1] + dy, p[2])

    prims = []
    # massive humped forequarters, barrel, small rump
    prims.append(Ell(*body(36, 34 - br * 0.3), 15.5, 15 + br * 0.4, 14, hide, rot=-0.2 + tilt))
    prims.append(Ell(*body(52, 37), 16, 12.5 + br * 0.3, 13, hide, rot=0.08 + tilt))
    prims.append(Ell(*body(66, 38), 10.5, 11, 11, hide, rot=tilt))
    # bristle crest along the back
    for i, x in enumerate(range(28, 64, 3)):
        h = 4.6 - abs(x - 34) * 0.09
        base = body(x, 22 + (x - 30) * 0.13 + (0 if x < 40 else 1), 0)
        tip = body(x + 2.5, 22 + (x - 30) * 0.13 - h, 0)
        prims.append(Cap(base, 2.2, tip, 0.5, bristle))

    # the head: a heavy wedge, snout disc, tusks
    hx, hy, turn = P.get('hdx', 0), P.get('hdy', 0), P.get('hturn', 0)
    if dead: hx, hy, turn = -1, 7, 0.35
    neck = body(26, 34)
    def head(x, y, z=0.0):
        p = rot((ox + x + dx + hx, y + 6 + dy + hy, z), (neck[0] + hx, neck[1] + hy), turn)
        return p
    prims.append(Cap(neck, 11.5, head(18, 36), 8.0, hide))
    prims.append(Cap(head(18, 34), 8.0, head(5, 41), 4.0, hide))                 # long wedge of a face
    prims.append(Ell(*head(3.5, 41.5, 1), 2.4, 3.6, 3.0, snout, rot=0.0))         # snout disc
    for z, d in ((4.5, 0), (-4.5, 1.5)):
        r = head(20 + d, 27.5, z)
        tip = head(23 + d, 22.5, z)
        prims.append(Cap(r, 2.6, tip, 0.6, bristle if z > 0 else hidefar))       # ears
    # tusks hook up out of the lower jaw
    t0 = head(9.0, 44.0, 5)
    t1 = head(6.0, 39.0, 5.5)
    t2 = head(8.5, 35.5, 5.5)
    prims.append(Cap(t0, 1.9, t1, 1.4, tusk))
    prims.append(Cap(t1, 1.4, t2, 0.6, tusk))

    legs = P.get('legs', [(0, 0)] * 4)
    def leg(i, top, paw_x, z, mat, front):
        pdx, pdy = legs[i]
        foot = (ox + paw_x + pdx + (dx if dead else 0), G - 1.5 + pdy)
        if dead: foot = (top[0] + (-6 if front else 5), top[1] + 6)
        knee = ik(top, (foot[0], foot[1] - 3), 5.5, 5.5, +1 if front else -1)
        prims.append(Cap((top[0], top[1], z), 5.2 if front else 6.0, (knee[0], knee[1], z), 3.0, mat))
        prims.append(Cap((knee[0], knee[1], z), 3.0, (foot[0], foot[1] - 3, z), 2.2, mat))
        prims.append(Cap((foot[0], foot[1] - 3, z), 2.2, (foot[0] - 0.5, foot[1], z), 2.0, hoof))
    leg(1, body(40, 44, -7), 38, -7, hidefar, True)
    leg(3, body(68, 44, -7), 67, -7, hidefar, False)
    leg(0, body(35, 45, 7), 33, 7, hide, True)
    leg(2, body(64, 45, 7), 63, 7, hide, False)

    # a thin tail with a tassel
    t = P.get('tail', 0)
    root = body(75, 32)
    mid = rot(body(79, 38), (root[0], root[1]), -t)
    prims.append(Cap(root, 1.4, mid, 1.0, hide))
    prims.append(Cap(mid, 1.2, add(mid, (1, 4, 0)), 1.6, bristle))

    det = []
    ex, ey, _ = head(14, 32.5, 6)
    if P.get('eyes') == 'shut' or dead:
        det += [(ex - 1, ey, 'e'), (ex, ey, 'e')]
    else:
        det += [(ex - 1, ey, 'e'), (ex, ey, 'e'), (ex, ey - 1, 'r'), (ex - 1, ey - 1, 'e')]
    n = head(2.6, 41, 4)
    det += [(n[0], n[1], 'o'), (n[0], n[1] + 2, 'o')]
    if P.get('scrape'):   # dust kicked up by the scraping front hoof
        for j in range(P['scrape'] * 3):
            det.append((ox + 26 + dx - j * 2.3, G - 1 - (j % 3) * 1.4, 'd'))
    sc = 0.75 * SCALE
    return render(prims, int(W * sc), int(H * sc), ground=G, details=det, seed=11, sc=sc)


# ------------------------------------------------------------------ the rock crab

CRAB_POSES = {
    'idle0': dict(),
    'idle1': dict(dy=0.4, claws=0.06),
    'idle2': dict(dy=0.8, claws=0.12, eyes=1),
    'idle3': dict(dy=0.4, claws=0.06),
    'tell0': dict(dy=-2, claws=-0.5, open=0.4),
    'tell1': dict(dy=-3, claws=-0.85, open=0.7),
    'atk0':  dict(dx=-8, dy=-1, claws=-0.2, open=0.8, reach=6),
    'atk1':  dict(dx=-14, dy=0, claws=0.3, open=0.0, reach=12),
    'atk2':  dict(dx=-6, dy=0.5, claws=0.1, open=0.2, reach=3),
    'hurt':  dict(dx=4, dy=-1, claws=0.5, open=0.6, eyes=2),
    'dead':  dict(dead=True),
}


def crab_frame(P):
    """Seen from the front, the way a crab faces you down: a broad stony shell, eyes on stalks, legs splayed both
    sides, and two big claws held up in front, the right one bigger."""
    W, H = 96, 60
    G = H - 3
    cx = 50
    shell = Mat(['e', 'g', 'G', 'y', 'Y'], under=['o', 'R', 'r', 'd', 'z'], under_at=0.55, fur=0.10, shine=0.2, top=['g', 'G', 'y', 'Y', 'W'], top_at=-0.75)
    claw = Mat(['m', 'R', 'r', 'r', 'z'], under=['m', 'R', 'R', 'r', 'd'], under_at=0.55, fur=0.02, shine=0.45)
    legm = Mat(['o', 'm', 'm', 'R', 'r'], fur=0.03)
    legfar = Mat(['o', 'o', 'm', 'm', 'R'], fur=0.02)
    rock = Mat(['g', 'G', 'y', 'Y', 'W'], fur=0.2)
    eye = Mat(['o', 'e', 'g', 'y'], shine=0.7)
    dead = P.get('dead')
    dx, dy = P.get('dx', 0), P.get('dy', 0)
    if dead: dx, dy = 0, 7
    cl, op, reach = P.get('claws', 0), P.get('open', 0), P.get('reach', 0)
    def S(x, y, z=0.0): return (cx + x + dx, y + dy, z)

    prims = []
    # legs first (behind the shell): four a side, bent at the knee, tips on the ground
    for side in (-1, 1):
        for i in range(4):
            top = S(side * (12 + i * 2.6), 39 + i * 0.5, 4 - i * 4)
            knee = S(side * (21 + i * 5.2), (24 + i * 2.6) if not dead else 44, 3 - i * 4)
            foot = S(side * (25 + i * 6.4), G - dy - 0.5 - (3 - i) * 0.6 if not dead else 52, 2 - i * 4)
            m = legm if i < 2 else legfar
            prims.append(Cap(top, 2.3, knee, 1.9, m))
            prims.append(Cap(knee, 1.8, foot, 0.7, m))
    # the shell: a wide dome, crusted, with a stone it wears
    prims.append(Ell(*S(0, 35), 22, 11, 13, shell))
    prims.append(Ell(*S(-1, 41, 4), 16, 5, 9, shell))
    prims.append(Ell(*S(5, 27, 2), 6.5, 4.0, 5, rock, rot=0.15))
    for (bx, by) in ((-10, 32), (-4, 29), (11, 33), (14, 38), (-15, 37), (2, 33)):
        prims.append(Ell(*S(bx, by, 11), 1.5, 1.2, 1.3, rock))
    # eyes on stalks
    for side in (-1, 1):
        base = S(side * 4, 30, 8)
        tip = S(side * (5 + P.get('eyes', 0) * 0.5), 22 if not dead else 29, 9)
        prims.append(Cap(base, 1.3, tip, 1.1, legm))
        prims.append(Ell(tip[0], tip[1] - 0.5, 10, 1.7, 1.7, 1.7, eye))
    # claws, held up in front; on the lunge they reach towards you (left)
    for side, big in ((-1, 1.1), (1, 0.85)):
        sh = S(side * 13, 41, 10)
        reachx = -reach if side < 0 else -reach * 0.5
        elbow = S(side * 22 + reachx * 0.5, 46 + cl * 6, 13)
        hand = S(side * 16 + reachx, 42 + cl * 12 + reach * 0.4, 15)
        if dead:
            elbow, hand = S(side * 20, 48, 13), S(side * 16, 50, 15)
        prims.append(Cap(sh, 2.8 * big, elbow, 2.4 * big, claw))
        prims.append(Cap(elbow, 2.4 * big, hand, 2.6 * big, claw))
        prims.append(Ell(hand[0], hand[1], hand[2], 5.0 * big, 6.0 * big, 4.6 * big, claw, rot=0.2 * side))
        # two fingers in a V: the fixed one up the inside, the moving one on the outside, opening outwards
        a = op * 0.7
        root_in = (hand[0] - side * 1.5 * big, hand[1] - 4.5 * big)
        root_out = (hand[0] + side * 2.2 * big, hand[1] - 4.0 * big)
        tip_in = (hand[0] - side * 2.5 * big, hand[1] - 14 * big)
        tip_out = rot((hand[0] + side * 4.5 * big, hand[1] - 12.5 * big, 0), root_out, side * a)
        prims.append(Cap((root_in[0], root_in[1], hand[2] + 2), 2.7 * big, (tip_in[0], tip_in[1], hand[2] + 2), 0.7 * big, claw))
        prims.append(Cap((root_out[0], root_out[1], hand[2] + 1), 2.2 * big, (tip_out[0], tip_out[1], hand[2] + 1), 0.6 * big, claw))
    det = []
    # a mouth of little feelers under the eyes
    for x in (-2, 0, 2):
        det.append((cx + x + dx, 44 + dy, 'm'))
    sc = 0.85 * SCALE
    return render(prims, int(W * sc), int(H * sc), ground=G, details=det, seed=5, sc=sc)


def shadow():
    """The soft shadow under anyone standing in a fight: an ellipse, solid in the middle, dithered at the rim."""
    from PIL import Image
    W, H = 40, 8
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    B = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]
    for y in range(H):
        for x in range(W):
            q = ((x + 0.5 - W / 2) / (W / 2)) ** 2 + ((y + 0.5 - H / 2) / (H / 2)) ** 2
            if q > 1: continue
            d = 1 - q
            if d > 0.45 or B[y % 4][x % 4] < d * 30:
                im.putpixel((x, y), (18, 15, 20, 110))
    return im


def main(out=OUT, grid=False):
    """grid: the small versions that stand on the battle grid (Sprites/Battle/Units), same eleven frames."""
    global SCALE
    if grid:
        SCALE = 0.36
        out = os.path.join(out, '..', 'Battle', 'Units')
    os.makedirs(out, exist_ok=True)
    made = []
    ui = os.path.join(out, '..', 'Ui')
    os.makedirs(ui, exist_ok=True)
    shadow().save(os.path.join(ui, 'shadow.png'))
    for name, make in (('wolf', lambda p: wolf_frame(WOLF, p)), ('wolf_alpha', lambda p: wolf_frame(ALPHA, p)),
                       ('boar', boar_frame), ('crab', crab_frame)):
        poses = BOAR_POSES if name == 'boar' else CRAB_POSES if name == 'crab' else WOLF_POSES
        frames = [make(poses[f]) for f in FRAMES]
        path = os.path.join(out, name + '.png')
        sheet(frames).save(path)
        made.append(path)
    SCALE = 1.0
    return made


if __name__ == '__main__':
    print('\n'.join(main()))
