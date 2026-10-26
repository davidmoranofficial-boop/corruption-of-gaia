"""Fills an area with everything that stands on the ground: trees, rocks, grass, flowers, pebbles and shells.

Anything tall enough to hide you gets pass_index = (its front tile row + 1), so the cut-out step can split
the render into rows that the game draws in front of or behind you.
"""
import math
import random

import bpy
import bmesh
import numpy as np
from mathutils import Vector, noise

import trees
import props
import scatter
from common import place, link, collection, Nodes, principled, output, hexc, ramp
from terrain import S, PAD


class Ground:
    def __init__(self, F, rows):
        self.F, self.rows = F, rows
        self.W, self.H = len(rows[0]), len(rows)

    def z(self, x, y):
        i = int(round((y + PAD) * S))
        j = int(round((x + PAD) * S))
        i = min(max(i, 0), self.F['H'] - 1)
        j = min(max(j, 0), self.F['W'] - 1)
        return float(self.F['h'][i, j])

    def f(self, key, x, y):
        i = min(max(int(round((y + PAD) * S)), 0), self.F['H'] - 1)
        j = min(max(int(round((x + PAD) * S)), 0), self.F['W'] - 1)
        return float(self.F[key][i, j])

    def code(self, x, y):
        xi, yi = int(math.floor(x)), int(math.floor(y))
        if 0 <= yi < self.H and 0 <= xi < self.W: return self.rows[yi][xi]
        return None

    def at(self, x, y, dz=0.0):
        return place(x, y, self.z(x, y) + dz)


def rock_material():
    mat = bpy.data.materials.get('rock')
    if mat: return mat
    mat = bpy.data.materials.new('rock')
    n = Nodes(mat)
    tc = n.new('ShaderNodeTexCoord')
    geo = n.new('ShaderNodeNewGeometry')
    nz = n.new('ShaderNodeTexNoise', in_Scale=2.5, in_Detail=10.0, in_Roughness=0.65)
    n.link(tc, 'Object', nz, 'Vector')
    col = ramp(n, [(0.0, '#4b4840'), (0.45, '#77736a'), (0.75, '#949082'), (1.0, '#b2ac9c')], nz, 'Fac')
    # moss and lichen on the tops
    sep = n.new('ShaderNodeSeparateXYZ')
    n.link(geo, 'Normal', sep, 'Vector')
    nz2 = n.new('ShaderNodeTexNoise', in_Scale=5.0, in_Detail=4.0)
    n.link(tc, 'Object', nz2, 'Vector')
    add = n.new('ShaderNodeMath', operation='ADD')
    n.link(sep, 'Z', add, 0)
    n.link(nz2, 'Fac', add, 1)
    mm = n.new('ShaderNodeMapRange', in_From_Min=1.15, in_From_Max=1.35)
    n.link(add, 0, mm, 'Value')
    mx = n.new('ShaderNodeMix', data_type='RGBA')
    n.link(col, 0, mx, 'A')
    n.inp(mx, 'B').default_value = (*hexc('#55682e'), 1)
    n.link(mm, 'Result', mx, 'Factor')
    b = principled(n, rough=0.8)
    n.link(mx, 'Result', b, 'Base Color')
    bump = n.new('ShaderNodeBump', in_Strength=0.5)
    n.link(nz, 'Fac', bump, 'Height')
    n.link(bump, 'Normal', b, 'Normal')
    output(n, b)
    return mat


def boulder(name, size, seed, squash=0.65, coll=None):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=4, radius=1.0)
    off = Vector((seed * 3.3, seed * 1.7, seed * 2.9))
    rs = random.Random(seed)
    # a few flat cuts make it read as stone, not a blob
    planes = [(Vector((rs.uniform(-1, 1), rs.uniform(-1, 1), rs.uniform(-0.3, 1))).normalized(), rs.uniform(0.55, 0.85)) for _ in range(5)]
    for v in bm.verts:
        p = v.co.copy()
        k = 1 + 0.22 * noise.noise(p * 1.3 + off) + 0.06 * noise.noise(p * 5 + off)
        q = p * k
        for nrm, d in planes:
            e = q.dot(nrm)
            if e > d: q -= nrm * (e - d)
        v.co = Vector((q.x * size * rs.uniform(1.0, 1.25), q.y * size, max(q.z, -0.3) * size * squash))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons: p.use_smooth = True
    me.materials.append(rock_material())
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)
    return ob


def populate(G, seed=5):
    rnd = random.Random(seed)
    things = collection('things')
    src = scatter.hidden('sources')
    tall = []

    # ---- trees
    forest_n = 0
    for y in range(G.H):
        for x in range(G.W):
            c = G.code(x, y)
            if c != 'T': continue
            nbrs = sum(1 for dx in (-1, 0, 1) for dy in (-1, 0, 1) if G.code(x + dx, y + dy) == 'T')
            deep = nbrs >= 8
            jx, jy = rnd.uniform(-0.25, 0.25), rnd.uniform(-0.2, 0.2)
            px, py = x + 0.5 + jx, y + 0.55 + jy
            if deep:
                kind = 'pine' if rnd.random() < 0.35 else 'oak'
                sc = rnd.uniform(0.85, 1.15)
            else:
                kind = 'oak' if rnd.random() < 0.8 else 'pine'
                sc = rnd.uniform(0.62, 0.8) if nbrs <= 2 else rnd.uniform(0.75, 0.95)
            ob = trees.plant(kind, G.at(px, py, -0.05), rnd, things, 'tree_%d_%d' % (x, y), sc)
            ob.pass_index = y + 2
            tall.append(ob)

    # ---- buildings and the wreck: each connected block of H (or Q) tiles
    seen = set()
    for y in range(G.H):
        for x in range(G.W):
            c = G.code(x, y)
            if c not in 'HQ' or (x, y) in seen: continue
            stack, comp = [(x, y)], []
            while stack:
                p = stack.pop()
                if p in seen: continue
                cc = G.code(*p)
                if cc not in (c, 'D' if c == 'H' else c): continue
                seen.add(p)
                comp.append(p)
                for d in ((1, 0), (-1, 0), (0, 1), (0, -1)): stack.append((p[0] + d[0], p[1] + d[1]))
            tx, ty = min(p[0] for p in comp), min(p[1] for p in comp)
            w, nr = max(p[0] for p in comp) - tx + 1, max(p[1] for p in comp) - ty + 1
            doors = [p for p in comp if G.code(*p) == 'D']
            z = G.z(tx + w / 2, ty + nr / 2)
            origin = G.at(tx, ty + nr) * 0 + __import__('common').place(tx, ty + nr, z)
            if c == 'H':
                ob, chim = props.shack(things, w, nr, (doors[0][0] - tx) if doors else w // 2, seed=x * 7 + y)
                ob.location = origin
                if doors: props.smoke(things, origin + __import__('mathutils').Vector(chim))
            else:
                ob = props.wreck(things, w, nr, seed=x + y)
                ob.location = origin
            ob.pass_index = ty + nr + 1
            tall.append(ob)

    # ---- the palisade, gateposts, bridges, brambles and steps
    for y in range(G.H):
        run = [(x, y) for x in range(G.W) if G.code(x, y) == 'Z']
        if run:
            ob = props.palisade(things, run, G, seed=y)
            ob.pass_index = y + 2
            tall.append(ob)
        gp = [(x, y) for x in range(G.W) if G.code(x, y) == 'I']
        if gp:
            ob = props.palisade(things, gp, G, gate=True, seed=y + 50)
            ob.pass_index = y + 2
            tall.append(ob)
        for x in range(G.W):
            c = G.code(x, y)
            if c == 'k':
                ob = props.brambles(things, x, y, G, seed=x * 31 + y)
                ob.pass_index = y + 2
            elif c == 's':
                props.steps(things, x, y, G)
    btiles = [(x, y) for y in range(G.H) for x in range(G.W) if G.code(x, y) == 'b']
    if btiles: props.bridge(things, btiles, G)

    # ---- rocks
    for y in range(G.H):
        for x in range(G.W):
            c = G.code(x, y)
            if c == 'r':
                ob = boulder('rock_%d_%d' % (x, y), rnd.uniform(0.42, 0.55), rnd.random() * 100, coll=things)
                ob.location = G.at(x + 0.5, y + 0.55, -0.08)
                ob.rotation_euler = (0, 0, rnd.uniform(0, 6.3))
                ob.pass_index = y + 2
                tall.append(ob)
            elif c == 'R':
                for k in range(3):
                    ob = boulder('pool_%d_%d_%d' % (x, y, k), rnd.uniform(0.18, 0.3), rnd.random() * 100, coll=things)
                    a = 2 * math.pi * k / 3
                    ob.location = G.at(x + 0.5 + 0.32 * math.cos(a), y + 0.5 + 0.32 * math.sin(a), -0.05)
            elif c == 'h':
                for k in range(2):
                    ob = boulder('cliff_%d_%d_%d' % (x, y, k), rnd.uniform(0.35, 0.6), rnd.random() * 100, 0.9, coll=things)
                    ob.location = G.at(x + rnd.uniform(0.2, 0.8), y + rnd.uniform(0.3, 0.8), -0.1)
                    ob.rotation_euler = (0, 0, rnd.uniform(0, 6.3))
                    ob.pass_index = y + 2

    # ---- grass, flowers, pebbles, shells
    gm_short = scatter.grass_material('grass_short', '#2e4a1c', '#6d8f3a', '#a29a55')
    gm_long = scatter.grass_material('grass_long', '#3f5a22', '#8fae4c', '#c9bf72')
    gm_dune = scatter.grass_material('grass_dune', '#5b6a35', '#a4a467', '#c8bd86')
    shorts = [scatter.tuft('tuft_s%d' % i, 22, 0.22, 0.12, 0.4, gm_short, i, src) for i in range(3)]
    longs = [scatter.tuft('tuft_l%d' % i, 30, 0.55, 0.16, 0.5, gm_long, 10 + i, src) for i in range(3)]
    dunes = [scatter.tuft('tuft_d%d' % i, 14, 0.42, 0.08, 0.7, gm_dune, 20 + i, src) for i in range(2)]
    flowers = [scatter.flower('flower_%d' % i, c, src, i) for i, c in enumerate(['#e8e4d8', '#e5c53c', '#b06ad0', '#e07a5a'])]
    pebbles = [scatter.pebble('peb_%d' % i, src, i, 0.05 + 0.02 * i) for i in range(3)]
    shells = [scatter.pebble('shell_%d' % i, src, 10 + i, 0.035, '#e8ddd0') for i in range(2)]

    rs = np.random.RandomState(seed)
    W, H = G.W, G.H
    buckets = {k: [] for k in ('short', 'long', 'dune', 'flower', 'pebble', 'shell')}
    n = int(W * H * 60)
    xs, ys = rs.uniform(0, W, n), rs.uniform(0, H, n)
    roll = rs.uniform(0, 1, n)
    for x, y, r in zip(xs, ys, roll):
        c = G.code(x, y)
        if c is None or c in '~HQwTZIbDsr': continue
        if c == ',' and any(G.code(x + dx, y + dy) == 'Q' for dx in (-1, 0, 1) for dy in (-1, 0, 1)) and r > 0.3: continue
        grass = G.f('grass', x, y)
        road = G.f('road', x, y)
        sand = G.f('sand', x, y)
        forest = G.f('forest', x, y)
        longg = G.f('longg', x, y)
        wet = G.f('wet', x, y)
        if road > 0.45:
            if r < 0.025: buckets['pebble'].append((x, y))
            continue
        if c == ',' or sand > 0.6:
            if wet < 0.3 and r < 0.02 and sand < 0.95: buckets['dune'].append((x, y))
            elif wet > 0.2 and r < 0.025: buckets['shell'].append((x, y))
            elif r < 0.004: buckets['pebble'].append((x, y))
            continue
        if longg > 0.4 and r < 0.75 * min(1, (longg - 0.4) * 3):
            buckets['long'].append((x, y))
        elif grass > 0.5 and r < 0.6 * (1 - road) * (1 - 0.5 * forest):
            buckets['short'].append((x, y))
        elif c == 'o' and r < 0.45:
            buckets['flower'].append((x, y))
        if (c == 'o' and r > 0.9) or (grass > 0.6 and r > 0.997):
            buckets['flower'].append((x, y))

    def emit(name, pts, srcs, smin, smax):
        if not pts: return
        pts = np.array(pts)
        groups = rs.randint(0, len(srcs), len(pts))
        for gi, s in enumerate(srcs):
            sel = pts[groups == gi]
            if len(sel) == 0: continue
            locs = [tuple(G.at(px, py, -0.02)) for px, py in sel]
            scatter.instancer('%s_%d' % (name, gi), locs, s, things, scale=rs.uniform(smin, smax, len(sel)), seed=gi + 1)

    emit('short', buckets['short'], shorts, 0.75, 1.3)
    emit('long', buckets['long'], longs, 0.8, 1.25)
    emit('dune', buckets['dune'], dunes, 0.8, 1.3)
    emit('flower', buckets['flower'], flowers, 0.8, 1.2)
    emit('pebble', buckets['pebble'], pebbles, 0.6, 1.4)
    emit('shell', buckets['shell'], shells, 0.7, 1.3)
    print('scatter', {k: len(v) for k, v in buckets.items()})
    return tall
