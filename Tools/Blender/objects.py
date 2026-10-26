"""Things you can use in the world (crates, the sea chest, logs, signs...) and the wolf, rendered as still pictures.

Every picture is a frame 2 tiles wide and 2.5 tall; the thing stands on the bottom-middle tile of it
(x 0.5..1.5, rows 1.5..2.5), centred at (1.0, 2.1). The game draws it with that tile on the object's tile.

    python objects.py                 -> Sprites/HD/Obj/<sprite>.png for every thing below
    python objects.py obj_crate ...   -> just those
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import bmesh
from mathutils import Vector, Matrix, noise

import common
import props
import character
from common import Nodes, principled, output, hexc, ramp, link, SA, CA
from props import cyl, cube, finish, wood, plain, stone_mat


def stage(ppt):
    sc = bpy.context.scene
    import render as R
    import terrain
    R.setup(64, ppt)
    sc.render.film_transparent = True
    cam = R.camera('objcam')
    cam.data.ortho_scale = 2.0
    sc.render.resolution_x = int(ppt * 2)
    sc.render.resolution_y = int(ppt * 2.5)
    cam.location = common.place(1.0, 1.25, 0) - Vector((0, SA, -CA)) * 50
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=4)
    me = bpy.data.meshes.new('catcher')
    bm.to_mesh(me)
    bm.free()
    g = bpy.data.objects.new('catcher', me)
    link(g)
    g.location = common.place(1.0, 2.1, 0)
    g.is_shadow_catcher = True
    terrain.sky_and_sun()


def at():
    return common.place(1.0, 2.1, 0)


# ------------------------------------------------------------------ the things

def crate(coll):
    rnd = random.Random(1)
    bm = bmesh.new()
    s = 0.62
    cube(bm, (0, 0, s / 2), (s, s, s), 0)
    for zz in (0.06, s - 0.06):
        for (cx, cy, sx, sy) in ((0, -s / 2, s + 0.04, 0.05), (0, s / 2, s + 0.04, 0.05), (-s / 2, 0, 0.05, s + 0.04), (s / 2, 0, 0.05, s + 0.04)):
            cube(bm, (cx, cy, zz), (sx, sy, 0.1), 1)
    for (cx, cy) in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        cube(bm, (cx * s / 2, cy * s / 2, s / 2), (0.07, 0.07, s + 0.02), 1)
    cube(bm, (0, -s / 2 - 0.01, s / 2), (s * 1.2, 0.04, 0.08), 1, Matrix.Rotation(0.78, 3, 'Y'))
    ob = finish('crate', bm, [wood('crate_w', '#5a4630', '#7e6446', '#9c8060', 'X', 4.0), wood('crate_b', '#3a2c1e', '#54412e', '#6a543c', 'Z', 4.0)], coll)
    ob.location = at()
    ob.rotation_euler = (0, 0, 0.25)
    # wet sand darkening round its foot, and a strand of weed
    return ob


def sea_chest(coll):
    bm = bmesh.new()
    w, d, h = 0.8, 0.5, 0.42
    cube(bm, (0, 0, h / 2), (w, d, h), 0)
    # domed lid
    for i in range(8):
        a0, a1 = math.pi * i / 8, math.pi * (i + 1) / 8
        y0, z0 = -math.cos(a0) * d / 2, h + math.sin(a0) * 0.16
        y1, z1 = -math.cos(a1) * d / 2, h + math.sin(a1) * 0.16
        v = [bm.verts.new((-w / 2, y0, z0)), bm.verts.new((w / 2, y0, z0)), bm.verts.new((w / 2, y1, z1)), bm.verts.new((-w / 2, y1, z1))]
        bm.faces.new(v).material_index = 0
    for sx in (-1, 1):
        vs = [bm.verts.new((sx * w / 2, -math.cos(math.pi * i / 8) * d / 2, h + math.sin(math.pi * i / 8) * 0.16)) for i in range(9)]
        bm.faces.new(vs if sx > 0 else list(reversed(vs))).material_index = 0
    for xx in (-0.28, 0.28):
        cube(bm, (xx, 0, h / 2), (0.06, d + 0.03, h + 0.02), 1)
        for i in range(8):
            a0 = math.pi * (i + 0.5) / 8
            cube(bm, (xx, -math.cos(a0) * (d / 2 + 0.015), h + math.sin(a0) * 0.175), (0.06, 0.07, 0.025), 1, Matrix.Rotation(-(a0 - math.pi / 2), 3, 'X'))
    cube(bm, (0, -d / 2 - 0.02, h - 0.02), (0.1, 0.03, 0.12), 2)
    ob = finish('chest', bm, [wood('chest_w', '#3a2416', '#5e3a22', '#7a4e30', 'X', 5.0), plain('chest_iron', '#2b2622', 0.45, 20, 0.25, 0.7), plain('chest_brass', '#8c6c32', 0.3, 30, 0.2, 0.9)], coll)
    ob.location = at()
    ob.rotation_euler = (0.08, 0, -0.35)
    return ob


def barrel(coll):
    bm = bmesh.new()
    rnd = random.Random(3)
    n = 16
    H, r0, r1 = 0.85, 0.27, 0.32
    for k in range(n):
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 0.94) / n
        pts = []
        for i in range(7):
            t = i / 6
            r = r0 + (r1 - r0) * math.sin(math.pi * t)
            z = H * t
            pts.append((r, z))
        rings = []
        for (r, z) in pts:
            rings.append((bm.verts.new((math.cos(a0) * r, math.sin(a0) * r, z)), bm.verts.new((math.cos(a1) * r, math.sin(a1) * r, z))))
        for (a, b), (c, d) in zip(rings, rings[1:]):
            bm.faces.new((a, b, d, c)).material_index = 0
    top = [bm.verts.new((math.cos(2 * math.pi * k / n) * r0 * 0.96, math.sin(2 * math.pi * k / n) * r0 * 0.96, H - 0.03)) for k in range(n)]
    bm.faces.new(top).material_index = 0
    for t in (0.12, 0.35, 0.65, 0.88):
        r = r0 + (r1 - r0) * math.sin(math.pi * t) + 0.012
        tmp = bmesh.ops.create_cone(bm, cap_ends=False, segments=n, radius1=r, radius2=r, depth=0.045)
        for v in tmp['verts']:
            v.co.z += H * t
        for f in bm.faces:
            if all(v in tmp['verts'] for v in f.verts): f.material_index = 1
    ob = finish('barrel', bm, [wood('barrel_w', '#4a3420', '#6a4c30', '#86643f', 'Z', 5.0), plain('hoop', '#2e2925', 0.5, 20, 0.3, 0.6)], coll)
    ob.location = at()
    return ob


def sign(coll):
    bm = bmesh.new()
    cyl(bm, (0, 0, 0), (0, 0, 1.55), 0.06, 7, 0, wobble=0.08, seed=2, segs=3)
    cube(bm, (0.0, -0.08, 1.25), (0.95, 0.06, 0.4), 1, Matrix.Rotation(0.05, 3, 'Y'))
    # carved letters: dark grooves
    rnd = random.Random(4)
    for i in range(7):
        cube(bm, (-0.36 + i * 0.11, -0.115, 1.27 + rnd.uniform(-0.03, 0.03)), (0.06, 0.01, 0.12 if i % 3 else 0.06), 2)
    ob = finish('sign', bm, [wood('signpost', '#3a2c1e', '#5a4632', '#6e5840', 'Z', 3.0), wood('signboard', '#5a4630', '#7a6044', '#937a5a', 'X', 6.0), plain('groove', '#1c140e', 0.9)], coll)
    ob.location = at()
    ob.rotation_euler = (0, 0, 0.1)
    return ob


def log(coll, length=1.25, r=0.17, seed=5, name='log'):
    bm = bmesh.new()
    cyl(bm, (-length / 2, 0, r * 0.85), (length / 2, 0, r * 0.8), r, 12, 0, r2=r * 0.85, wobble=0.07, seed=seed, segs=5)
    # stubs of branches
    rnd = random.Random(seed)
    for k in range(3):
        x = rnd.uniform(-length / 2.5, length / 2.5)
        a = rnd.uniform(0, math.pi * 2)
        p = Vector((x, math.cos(a) * r * 0.8, r * 0.8 + math.sin(a) * r * 0.8))
        cyl(bm, p, p + Vector((rnd.uniform(-0.1, 0.1), math.cos(a) * 0.18, math.sin(a) * 0.18 + 0.05)), 0.035, 6, 0, r2=0.015)
    ob = finish(name, bm, [props.wood('logbark', '#2e2014', '#55402a', '#6e5638', 'X', 9.0, 0.9, 1.0)], coll)
    ob.location = at()
    ob.rotation_euler = (0, 0, rnd.uniform(-0.4, 0.4))
    # moss on top
    return ob


def trunk(coll):
    ob = log(coll, 1.75, 0.3, 9, 'trunk')
    ob.rotation_euler = (0, 0, 0.12)
    return ob


def cairn(coll):
    rnd = random.Random(7)
    obs = []
    z = 0.0
    sizes = [0.42, 0.36, 0.3, 0.24, 0.18, 0.13]
    from populate import boulder
    for i, s in enumerate(sizes):
        b = boulder('cairn%d' % i, s, rnd.random() * 50, 0.55, coll)
        b.location = at() + Vector((rnd.uniform(-0.04, 0.04), rnd.uniform(-0.04, 0.04), z + s * 0.4))
        b.rotation_euler = (0, 0, rnd.uniform(0, 6))
        z += s * 0.62
    return b


def runestone(coll):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        t = (v.co.z + 0.5)
        v.co = Vector((v.co.x * 0.55 * (1 - 0.25 * t), v.co.y * 0.32, t * 1.7))
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=6, use_grid_fill=True)
    for v in bm.verts:
        v.co += Vector((noise.noise(v.co * 3) * 0.04, noise.noise(v.co * 3 + Vector((5, 0, 0))) * 0.03, 0))
        if v.co.z > 1.5: v.co.z -= (abs(v.co.x) * 0.6)
    me = bpy.data.meshes.new('rune')
    bm.to_mesh(me)
    bm.free()
    m = bpy.data.materials.new('runestone')
    n = Nodes(m)
    tc = n.new('ShaderNodeTexCoord')
    nz = n.new('ShaderNodeTexNoise', in_Scale=4.0, in_Detail=10.0)
    n.link(tc, 'Object', nz, 'Vector')
    c = ramp(n, [(0.0, '#3d3c38'), (0.5, '#5f5c55'), (1.0, '#7e7a70')], nz, 'Fac')
    # carved runes: a band of marks down the face, faintly glowing a sick yellow (the Corruption)
    wv = n.new('ShaderNodeTexVoronoi', feature='DISTANCE_TO_EDGE', in_Scale=7.0)
    n.link(tc, 'Object', wv, 'Vector')
    mr0 = n.new('ShaderNodeMapRange', in_From_Min=0.025, in_From_Max=0.0)
    n.link(wv, 'Distance', mr0, 'Value')
    sp = n.new('ShaderNodeSeparateXYZ')
    n.link(tc, 'Object', sp, 'Vector')
    ax = n.new('ShaderNodeMath', operation='ABSOLUTE')
    n.link(sp, 'X', ax, 0)
    band = n.new('ShaderNodeMapRange', in_From_Min=0.16, in_From_Max=0.1)
    n.link(ax, 0, band, 'Value')
    hb = n.new('ShaderNodeMapRange', in_From_Min=0.35, in_From_Max=0.45)
    n.link(sp, 'Z', hb, 'Value')
    m1 = n.new('ShaderNodeMath', operation='MULTIPLY')
    n.link(mr0, 'Result', m1, 0)
    n.link(band, 'Result', m1, 1)
    mr = n.new('ShaderNodeMath', operation='MULTIPLY')
    n.link(m1, 0, mr, 0)
    n.link(hb, 'Result', mr, 1)
    p = principled(n, rough=0.85)
    n.link(c, 0, p, 'Base Color')
    n.inp(p, 'Emission Color').default_value = (*hexc('#c9b54a'), 1)
    n.link(mr, 0, p, 'Emission Strength')
    bp = n.new('ShaderNodeBump', in_Strength=0.6)
    n.link(nz, 'Fac', bp, 'Height')
    n.link(bp, 'Normal', p, 'Normal')
    output(n, p)
    me.materials.append(m)
    for pl in me.polygons: pl.use_smooth = True
    ob = bpy.data.objects.new('runestone', me)
    link(ob, coll)
    ob.location = at()
    ob.rotation_euler = (0.04, 0.03, 0.1)
    return ob


def gulls(coll):
    """Three gulls picking at something on the sand."""
    rnd = random.Random(11)
    white = character.flat_mat('gull_w', '#e6e2da', 0.6)
    grey = character.flat_mat('gull_g', '#8a8e92', 0.6)
    beak = character.flat_mat('gull_b', '#d6a73a', 0.4)
    blk = character.flat_mat('gull_k', '#1c1c1c', 0.5)
    last = None
    for k in range(3):
        base = at() + Vector((rnd.uniform(-0.5, 0.5), rnd.uniform(-0.3, 0.3), 0))
        yaw = rnd.uniform(0, 6.28)
        R = Matrix.Rotation(yaw, 4, 'Z')
        parts = [
            character.ellipsoid('gb%d' % k, 0.09, 0.17, 0.08, white, coll, (0, 0, 0.17), 3),
            character.ellipsoid('gh%d' % k, 0.055, 0.06, 0.055, white, coll, (0, -0.15, 0.25), 3),
            character.ellipsoid('gk%d' % k, 0.015, 0.05, 0.014, beak, coll, (0, -0.22, 0.24), 2),
            character.ellipsoid('gw1%d' % k, 0.05, 0.16, 0.035, grey, coll, (-0.07, 0.03, 0.2), 2),
            character.ellipsoid('gw2%d' % k, 0.05, 0.16, 0.035, grey, coll, (0.07, 0.03, 0.2), 2),
            character.ellipsoid('gt%d' % k, 0.04, 0.07, 0.02, blk, coll, (0, 0.2, 0.19), 2),
        ]
        for leg in (-0.03, 0.03):
            parts.append(character.ellipsoid('gl%d' % k, 0.008, 0.008, 0.06, beak, coll, (leg, 0, 0.06), 1))
        for p in parts:
            p.matrix_world = Matrix.Translation(base) @ R @ p.matrix_world
        last = parts[0]
    return last


def wolf(coll, lying=False):
    """The yellow-eyed wolf: gaunt, matted, head low, grown from a skeleton like the people are."""
    import character as C
    H = 0.8
    J = {'hip': (0, 0.3, 0.58), 'mid': (0, 0.0, 0.58), 'sh': (0, -0.26, 0.62), 'neck': (0, -0.42, 0.6), 'head': (0, -0.56, 0.56), 'snout': (0, -0.76, 0.5),
         'tail1': (0, 0.48, 0.54), 'tail2': (0, 0.66, 0.38), 'tail3': (0, 0.78, 0.24)}
    for s, x in (('L', -0.11), ('R', 0.11)):
        J['fs' + s] = (x, -0.26, 0.5)
        J['fe' + s] = (x, -0.29, 0.28)
        J['fp' + s] = (x, -0.33, 0.04)
        J['ff' + s] = (x, -0.39, 0.02)
        J['hs' + s] = (x, 0.3, 0.48)
        J['hk' + s] = (x, 0.2, 0.3)
        J['hh' + s] = (x, 0.34, 0.14)
        J['hp' + s] = (x, 0.32, 0.03)
        J['hf' + s] = (x, 0.26, 0.02)
    R = {'hip': 0.105, 'mid': 0.085, 'sh': 0.125, 'neck': 0.11, 'head': 0.11, 'snout': 0.05, 'tail1': 0.06, 'tail2': 0.06, 'tail3': 0.03}
    for s in 'LR':
        R.update({'fs' + s: 0.07, 'fe' + s: 0.042, 'fp' + s: 0.032, 'ff' + s: 0.034, 'hs' + s: 0.09, 'hk' + s: 0.05, 'hh' + s: 0.03, 'hp' + s: 0.028, 'hf' + s: 0.03})
    E = [('hip', 'mid'), ('mid', 'sh'), ('sh', 'neck'), ('neck', 'head'), ('head', 'snout'), ('hip', 'tail1'), ('tail1', 'tail2'), ('tail2', 'tail3')]
    for s in 'LR':
        E += [('sh', 'fs' + s), ('fs' + s, 'fe' + s), ('fe' + s, 'fp' + s), ('fp' + s, 'ff' + s),
              ('hip', 'hs' + s), ('hs' + s, 'hk' + s), ('hk' + s, 'hh' + s), ('hh' + s, 'hp' + s), ('hp' + s, 'hf' + s)]
    names = list(J)
    me = bpy.data.meshes.new('wolf')
    me.from_pydata([J[k] for k in names], [(names.index(a), names.index(b)) for a, b in E], [])
    ob = bpy.data.objects.new('wolf', me)
    link(ob, coll)
    sk = ob.modifiers.new('skin', 'SKIN')
    for i, k in enumerate(names):
        r = R[k]
        ob.data.skin_vertices[0].data[i].radius = (r * (0.8 if k in ('mid',) else 1.0), r)
    ob.data.skin_vertices[0].data[names.index('mid')].use_root = True
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.modifier_apply(modifier='skin')
    sub = ob.modifiers.new('sub', 'SUBSURF')
    sub.levels = 2
    bpy.ops.object.modifier_apply(modifier='sub')
    # matted fur: noise colour, darker back, lighter belly, strong bump
    m = bpy.data.materials.new('wolf_fur')
    n = Nodes(m)
    tc = n.new('ShaderNodeTexCoord')
    geo = n.new('ShaderNodeNewGeometry')
    nz = n.new('ShaderNodeTexNoise', in_Scale=30.0, in_Detail=10.0, in_Distortion=2.0)
    n.link(tc, 'Object', nz, 'Vector')
    sep = n.new('ShaderNodeSeparateXYZ')
    n.link(geo, 'Normal', sep, 'Vector')
    mr = n.new('ShaderNodeMath', operation='MULTIPLY_ADD', use_clamp=True)
    n.link(sep, 'Z', mr, 0)
    mr.inputs[1].default_value = -0.35
    n.link(nz, 'Fac', mr, 2)
    c = ramp(n, [(0.0, '#2e241a'), (0.35, '#54452f'), (0.65, '#7a6a4e'), (1.0, '#9a8c70')], mr, 0)
    p = principled(n, rough=0.9)
    n.link(c, 0, p, 'Base Color')
    try: n.inp(p, 'Sheen Weight').default_value = 0.2
    except KeyError: pass
    bp = n.new('ShaderNodeBump', in_Strength=0.9, in_Distance=0.02)
    n.link(nz, 'Fac', bp, 'Height')
    n.link(bp, 'Normal', p, 'Normal')
    output(n, p)
    ob.data.materials.append(m)
    for pl in ob.data.polygons: pl.use_smooth = True
    eyes = []
    for sx in (-1, 1):
        e = character.ellipsoid('weye%d' % sx, 0.022, 0.012, 0.016, character.flat_mat('wolf_eye', '#e8c22a', 0.2, emit=2.5), coll, (sx * 0.058, -0.64, 0.62), 2)
        ear = character.ellipsoid('wear%d' % sx, 0.035, 0.02, 0.07, m, coll, (sx * 0.065, -0.52, 0.68), 2)
        eyes += [e, ear]
    nose = character.ellipsoid('wnose', 0.025, 0.02, 0.02, character.flat_mat('wolf_nose', '#141210', 0.3), coll, (0, -0.81, 0.515), 2)
    for o in eyes + [nose]:
        o.parent = ob
    ob.location = at() + Vector((0.05, 0.25, 0))
    ob.rotation_euler = (0, 0, -1.25)
    if lying:
        ob.rotation_euler = (0, math.radians(80), -0.2)
        ob.location.z += 0.12
    return ob


THINGS = {
    'obj_crate': crate, 'obj_sea_chest': sea_chest, 'obj_barrel': barrel, 'obj_sign': sign, 'obj_log': log,
    'obj_trunk': trunk, 'obj_cairn': cairn, 'obj_runestone': runestone, 'obj_gulls': gulls, 'obj_wolf': wolf,
}


def render_thing(name, out_dir, ppt=96):
    common.reset()
    stage(ppt)
    THINGS[name](bpy.context.scene.collection)
    sc = bpy.context.scene
    sc.render.filepath = os.path.join(out_dir, name + '.png')
    bpy.ops.render.render(write_still=True)
    print('thing', sc.render.filepath)


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    out = os.environ.get('OUTDIR', os.path.join(common.OUT, 'Obj'))
    os.makedirs(out, exist_ok=True)
    for nm in (args or list(THINGS)):
        render_thing(nm, out, int(os.environ.get('PPT', 96)))
