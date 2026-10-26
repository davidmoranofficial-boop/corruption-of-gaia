"""Indoor areas (Yenson's hut): plank floor, log walls, the hearth with a real fire lighting the room, furniture.

The wall nearest the camera (the bottom row) is kept low so you can see in, the way old RPGs cut rooms open.
Tile letters: x outer wall, W back wall, p floor, M doorway, B bed, t table, n barrel, f hearth.
"""
import math
import random

import bpy
import bmesh
from mathutils import Vector, Matrix, noise

import common
import props
import character
from common import place, link, collection, Nodes, principled, output, hexc, ramp, CA
from props import cyl, cube, finish, wood, plain, stone_mat


class Flat:
    """Ground helper for a flat floor."""
    def __init__(self, rows):
        self.rows = rows
        self.W, self.H = len(rows[0]), len(rows)

    def z(self, x, y): return 0.0

    def code(self, x, y):
        xi, yi = int(math.floor(x)), int(math.floor(y))
        if 0 <= yi < self.H and 0 <= xi < self.W: return self.rows[yi][xi]
        return None

    def at(self, x, y, dz=0.0): return place(x, y, dz)


def build(rows, seed=3):
    rnd = random.Random(seed)
    G = Flat(rows)
    things = collection('things')
    W, H = G.W, G.H
    floor_m = wood('floorboards', '#3a2a1c', '#5a4430', '#6e5640', 'Y', 6.0, 0.75, 0.5)
    log_m = wood('hutlogs', '#33251a', '#5a4430', '#76604a', 'X', 2.0)
    logy = wood('hutlogs_y', '#33251a', '#5a4430', '#76604a', 'Y', 2.0)
    tall = []

    # floor: boards running north-south, each its own shade
    bm = bmesh.new()
    for x in range(W):
        for k in range(3):
            x0 = x + k / 3
            cube(bm, place(x0 + 1 / 6, H / 2, -0.03), (1 / 3 - 0.012, H / CA + 0.2, 0.06), 0)
    floor = finish('floor', bm, [floor_m], things)
    sh = floor.data.attributes.new('shade', 'FLOAT', 'FACE')
    sh.data.foreach_set('value', [rnd.random() for _ in floor.data.polygons])

    # walls: logs along every wall tile run; the back wall (rows 0-1) full height, the front (last row) cut low
    wall_h = 2.3
    for y in range(H):
        for x in range(W):
            c = G.code(x, y)
            if c not in 'xW': continue
            front = y == H - 1
            back = y <= 1
            hh = 0.45 if front else wall_h
            if back and y == 0: continue          # the top row is the wall's top seen from above; built by row 1
            bm = bmesh.new()
            r = 0.12
            n = int(hh / (2 * r * 0.92))
            horiz = back or front
            for i in range(max(1, n)):
                z = r + i * 2 * r * 0.92
                if horiz:
                    yy = 1.65 if back else y + 0.5
                    a, b = place(x - 0.02, yy, 0) + Vector((0, 0, z)), place(x + 1.02, yy, 0) + Vector((0, 0, z))
                    cyl(bm, a, b, r * rnd.uniform(0.92, 1.05), 8, 0, wobble=0.04, seed=rnd.random() * 9, segs=2)
                else:
                    a, b = place(x + 0.5, y - 0.02, 0) + Vector((0, 0, z)), place(x + 0.5, y + 1.02, 0) + Vector((0, 0, z))
                    cyl(bm, a, b, r * rnd.uniform(0.92, 1.05), 8, 1, wobble=0.04, seed=rnd.random() * 9, segs=2)
            ob = finish('wall_%d_%d' % (x, y), bm, [log_m, logy], things)
            ob.pass_index = y + 2 if not back else 0
            if not back: tall.append(ob)

    # the hearth on the back wall: a stone chimney breast, a fire, a pot on a hook
    for y in range(H):
        for x in range(W):
            if G.code(x, y) != 'f': continue
            bm = bmesh.new()
            base = place(x + 0.5, 1.75, 0)
            for i in range(9):
                for j in range(4):
                    z = 0.12 + i * 0.25
                    if i < 4 and 1 <= j <= 2: continue      # the opening
                    cube(bm, base + Vector((-0.55 + j * 0.37, -0.15, z)), (0.36, 0.42, 0.24), 0, Matrix.Rotation(rnd.uniform(-0.06, 0.06), 3, 'Z'))
            cube(bm, base + Vector((0, -0.35, 1.05)), (1.5, 0.25, 0.14), 1)   # mantel beam
            finish('hearth', bm, [stone_mat(), wood('mantel', '#2a1e14', '#4a3624', '#5e4630', 'X', 4.0)], things)
            # embers and flames
            fire_m = bpy.data.materials.new('fire')
            n = Nodes(fire_m)
            tc = n.new('ShaderNodeTexCoord')
            nz = n.new('ShaderNodeTexNoise', in_Scale=8.0, in_Detail=4.0)
            n.link(tc, 'Object', nz, 'Vector')
            cr = ramp(n, [(0.3, '#3a0a00'), (0.55, '#ff6a10'), (0.8, '#ffc860')], nz, 'Fac')
            em = n.new('ShaderNodeEmission', in_Strength=6.0)
            n.link(cr, 0, em, 'Color')
            output(n, em)
            for k in range(5):
                f = character.ellipsoid('flame%d' % k, 0.08, 0.06, rnd.uniform(0.14, 0.24), fire_m, things,
                                        base + Vector((rnd.uniform(-0.18, 0.18), -0.2 + rnd.uniform(-0.05, 0.05), 0.22)), 2)
            for k in range(3):
                cyl(bm2 := bmesh.new(), base + Vector((-0.25, -0.2 + k * 0.05, 0.1)), base + Vector((0.25, -0.15 + k * 0.03, 0.12)), 0.05, 6, 0)
                finish('firelog%d' % k, bm2, [wood('charred', '#120c08', '#2a1c12', '#3a2a1c', 'X', 6.0)], things)
            ld = bpy.data.lights.new('firelight', 'POINT')
            ld.energy = 420
            ld.color = hexc('#ff9a48')
            ld.shadow_soft_size = 0.25
            lo = bpy.data.objects.new('firelight', ld)
            lo.location = base + Vector((0, -0.45, 0.5))
            link(lo, things)
            pot = character.ellipsoid('pot', 0.16, 0.16, 0.13, plain('potiron', '#1e1b18', 0.4, 20, 0.2, 0.7), things, base + Vector((0, -0.22, 0.55)), 3)

    # furniture
    def tile_runs(ch):
        out = []
        seen = set()
        for y in range(H):
            for x in range(W):
                if G.code(x, y) != ch or (x, y) in seen: continue
                xs = [x]
                while G.code(xs[-1] + 1, y) == ch: xs.append(xs[-1] + 1)
                ys = [y]
                while G.code(x, ys[-1] + 1) == ch: ys.append(ys[-1] + 1)
                for xx in xs:
                    for yy in ys: seen.add((xx, yy))
                out.append((x, y, len(xs), len(ys)))
        return out

    plank = wood('furn', '#3a2a1c', '#5c4630', '#745a40', 'X', 5.0)
    straw = plain('straw', '#a08a50', 0.95, 30, 0.3)
    blanket = character.cloth_mat('blanket', '#6a3a2a', 0.95, 40, 0.25)
    for (x, y, w, h) in tile_runs('B'):
        bm = bmesh.new()
        c = place(x + w / 2, y + h / 2, 0)
        L, Wd = h / CA - 0.1, w - 0.15
        for sx in (-1, 1):
            for sy in (-1, 1):
                cube(bm, c + Vector((sx * Wd / 2, sy * L / 2, 0.25)), (0.08, 0.08, 0.5 if sy > 0 else 0.4), 0)
        cube(bm, c + Vector((0, 0, 0.32)), (Wd, L, 0.08), 0)
        cube(bm, c + Vector((0, L / 2, 0.55)), (Wd, 0.06, 0.4), 0)
        ob = finish('bed', bm, [plank], things)
        ob.pass_index = y + h + 1
        tall.append(ob)
        m = character.ellipsoid('mattress', Wd / 2 - 0.02, L / 2 - 0.03, 0.08, straw, things, c + Vector((0, 0, 0.42)), 3)
        b = character.ellipsoid('blanket', Wd / 2, L / 3, 0.07, blanket, things, c + Vector((0, -L / 6, 0.47)), 3)
        p = character.ellipsoid('pillow', Wd / 3, 0.15, 0.06, character.cloth_mat('pillow', '#b8ac90'), things, c + Vector((0, L / 2 - 0.25, 0.5)), 3)
        for o in (m, b, p): o.pass_index = y + h + 1
    for (x, y, w, h) in tile_runs('t'):
        bm = bmesh.new()
        c = place(x + w / 2, y + h / 2, 0)
        L, Wd = w - 0.2, h / CA - 0.2
        cube(bm, c + Vector((0, 0, 0.74)), (L, Wd, 0.06), 0)
        for sx in (-1, 1):
            for sy in (-1, 1):
                cube(bm, c + Vector((sx * (L / 2 - 0.08), sy * (Wd / 2 - 0.08), 0.37)), (0.07, 0.07, 0.74), 0)
        # stools
        for sx in (-0.5, 0.5):
            cs = c + Vector((sx * L * 0.6, -Wd / 2 - 0.35, 0))
            cyl(bm, cs + Vector((0, 0, 0.42)), cs + Vector((0, 0, 0.46)), 0.17, 10, 0)
            for a in range(3):
                ang = a * 2.1
                cyl(bm, cs + Vector((math.cos(ang) * 0.1, math.sin(ang) * 0.1, 0)), cs + Vector((math.cos(ang) * 0.05, math.sin(ang) * 0.05, 0.42)), 0.025, 5, 0)
        ob = finish('table', bm, [plank], things)
        ob.pass_index = y + h + 1
        tall.append(ob)
        # things on the table: a candle, a bowl, a knife
        cand = character.ellipsoid('candle', 0.03, 0.03, 0.07, plain('wax', '#e0d6b8', 0.5), things, c + Vector((-0.3, 0.05, 0.84)), 2)
        flame = character.ellipsoid('cflame', 0.012, 0.012, 0.03, character.flat_mat('cflame', '#ffd080', 0.5, emit=30), things, c + Vector((-0.3, 0.05, 0.93)), 2)
        cl = bpy.data.lights.new('candle', 'POINT')
        cl.energy = 6
        cl.color = hexc('#ffb860')
        co = bpy.data.objects.new('candle', cl)
        co.location = c + Vector((-0.3, 0.05, 0.98))
        link(co, things)
        bowl = character.ellipsoid('bowl', 0.12, 0.12, 0.05, wood('bowl', '#4a3020', '#6a4a30', '#7e5e40', 'Z', 8.0), things, c + Vector((0.25, 0.0, 0.8)), 3)
        for o in (cand, flame, bowl): o.pass_index = y + h + 1
    import objects as OB
    for (x, y, w, h) in tile_runs('n'):
        for xx in range(w):
            for yy in range(h):
                old = set(bpy.data.objects)
                OB.barrel(things)
                for o in set(bpy.data.objects) - old:
                    o.location = place(x + xx + 0.5, y + yy + 0.6, 0)
                    o.scale = (0.85, 0.85, 0.85)
                    o.pass_index = y + yy + 2
                    tall.append(o)

    # nets and floats hanging on the back wall, a shelf with jars
    netm = plain('net', '#6a5e44', 0.95, 40, 0.3)
    for k in range(2):
        cx = 2.2 + k * 6.5
        bm = bmesh.new()
        for i in range(9):
            a = place(cx - 0.6 + i * 0.15, 1.78, 0) + Vector((0, 0, 2.0))
            b = place(cx - 0.5 + i * 0.13, 1.78, 0) + Vector((0, -0.05, 0.9 + 0.2 * math.sin(i)))
            cyl(bm, a, b, 0.01, 4, 0)
        for j in range(5):
            z = 1.0 + j * 0.2
            cyl(bm, place(cx - 0.55, 1.75, 0) + Vector((0, -0.04, z)), place(cx + 0.6, 1.75, 0) + Vector((0, -0.04, z + 0.05)), 0.01, 4, 0)
        finish('net%d' % k, bm, [netm], things)
        for f in range(3):
            character.ellipsoid('float%d%d' % (k, f), 0.06, 0.06, 0.06, plain('cork', '#b06a2a', 0.6), things,
                                place(cx - 0.4 + f * 0.4, 1.7, 0) + Vector((0, -0.08, 1.1 + 0.15 * f)), 2)
    bm = bmesh.new()
    sx = 9.5
    cube(bm, place(sx, 1.72, 0) + Vector((0, -0.1, 1.5)), (1.4, 0.25, 0.05), 0)
    finish('shelf', bm, [plank], things)
    for j in range(4):
        character.ellipsoid('jar%d' % j, 0.07, 0.07, 0.11, plain('jar%d' % j, ['#6a7a5a', '#8a6a4a', '#5a5a6a', '#a08a5a'][j], 0.3), things,
                            place(sx - 0.5 + j * 0.32, 1.72, 0) + Vector((0, -0.12, 1.64)), 2)

    # dim daylight through the door, and a little sky fill
    ld = bpy.data.lights.new('doorlight', 'AREA')
    ld.energy = 60
    ld.size = 1.2
    ld.color = hexc('#cfe0ff')
    lo = bpy.data.objects.new('doorlight', ld)
    for x in range(W):
        if G.code(x, H - 1) == 'M':
            lo.location = place(x + 0.5, H - 0.3, 0) + Vector((0, 0, 1.5))
    lo.rotation_euler = (math.radians(-60), 0, 0)
    link(lo, things)
    # soft light bouncing round the room (what the fire and the door light up, filling the shadows)
    fd = bpy.data.lights.new('fill', 'AREA')
    fd.energy = 140
    fd.size = 8
    fd.color = hexc('#ffd9b0')
    fo = bpy.data.objects.new('fill', fd)
    fo.location = place(W / 2, H / 2, 0) + Vector((0, 0, 3.5))
    link(fo, things)
    # a shaft of morning light through a gap in the logs, across the floor
    sd = bpy.data.lights.new('shaft', 'SPOT')
    sd.energy = 300
    sd.spot_size = math.radians(14)
    sd.spot_blend = 0.6
    sd.color = hexc('#fff0d0')
    so = bpy.data.objects.new('shaft', sd)
    so.location = place(W - 1.2, 1.6, 0) + Vector((0, 0, 2.6))
    so.rotation_euler = (math.radians(55), 0, math.radians(30))
    link(so, things)
    return tall
