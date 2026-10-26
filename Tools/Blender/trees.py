"""Trees, made from code: a trunk that bends a little, a few branches, and clumps of leaves on their ends.

Each species has a few variants (meshes); placed trees share them, so a whole forest costs little.
"""
import math
import random

import bpy
import bmesh
from mathutils import Vector, Matrix, noise

from common import Nodes, principled, output, hexc, ramp, mesh_obj


def bark_material():
    mat = bpy.data.materials.get('bark')
    if mat: return mat
    mat = bpy.data.materials.new('bark')
    n = Nodes(mat)
    tc = n.new('ShaderNodeTexCoord')
    nz = n.new('ShaderNodeTexNoise', in_Scale=6.0, in_Detail=8.0)
    n.link(tc, 'Object', nz, 'Vector')
    wv = n.new('ShaderNodeTexWave', wave_type='BANDS', bands_direction='X', in_Scale=14.0, in_Distortion=8.0)
    n.link(tc, 'Object', wv, 'Vector')
    col = ramp(n, [(0.0, '#2b2219'), (0.5, '#4a3b2c'), (1.0, '#6a5a48')], nz, 'Fac')
    b = principled(n, rough=0.9)
    n.link(col, 0, b, 'Base Color')
    bump = n.new('ShaderNodeBump', in_Strength=0.6)
    n.link(wv, 'Fac', bump, 'Height')
    n.link(bump, 'Normal', b, 'Normal')
    output(n, b)
    return mat


def leaf_material(name, dark, mid, light, tip):
    mat = bpy.data.materials.get(name)
    if mat: return mat
    mat = bpy.data.materials.new(name)
    n = Nodes(mat)
    tc = n.new('ShaderNodeTexCoord')
    oi = n.new('ShaderNodeObjectInfo')
    geo = n.new('ShaderNodeNewGeometry')
    # leaf clusters: voronoi cells read as bunches of leaves catching the light
    vor = n.new('ShaderNodeTexVoronoi', in_Scale=7.0)
    n.link(tc, 'Object', vor, 'Vector')
    nz = n.new('ShaderNodeTexNoise', in_Scale=1.6, in_Detail=3.0)
    n.link(tc, 'Object', nz, 'Vector')
    # how far up and out a point is: tops and outsides are lighter (cheap ambient occlusion)
    sep = n.new('ShaderNodeSeparateXYZ')
    n.link(geo, 'Normal', sep, 'Vector')
    up = n.new('ShaderNodeMapRange', in_From_Min=-0.6, in_From_Max=1.0)
    n.link(sep, 'Z', up, 'Value')
    m1 = n.new('ShaderNodeMath', operation='MULTIPLY_ADD')
    n.link(nz, 'Fac', m1, 0)
    m1.inputs[1].default_value = 0.5
    n.link(up, 'Result', m1, 2)
    m2 = n.new('ShaderNodeMath', operation='MULTIPLY', use_clamp=True)
    n.link(m1, 0, m2, 0)
    m2.inputs[1].default_value = 0.75
    col = ramp(n, [(0.0, dark), (0.35, mid), (0.7, light), (1.0, tip)], m2, 0)
    # every tree a slightly different green
    hsv = n.new('ShaderNodeHueSaturation')
    n.link(col, 0, hsv, 'Color')
    hm = n.new('ShaderNodeMapRange', in_To_Min=0.47, in_To_Max=0.53)
    n.link(oi, 'Random', hm, 'Value')
    n.link(hm, 'Result', hsv, 'Hue')
    vm = n.new('ShaderNodeMapRange', in_To_Min=0.85, in_To_Max=1.12)
    n.link(oi, 'Random', vm, 'Value')
    n.link(vm, 'Result', hsv, 'Value')
    b = principled(n, rough=0.55)
    n.link(hsv, 'Color', b, 'Base Color')
    try:
        n.inp(b, 'Subsurface Weight').default_value = 0.0
    except KeyError:
        pass
    tr = n.new('ShaderNodeBsdfTranslucent')
    n.link(hsv, 'Color', tr, 'Color')
    mx = n.new('ShaderNodeMixShader', in_Fac=0.22)
    n.link(b, 0, mx, 1)
    n.link(tr, 0, mx, 2)
    bump = n.new('ShaderNodeBump', in_Strength=0.9, in_Distance=0.15)
    n.link(vor, 'Distance', bump, 'Height')
    n.link(bump, 'Normal', b, 'Normal')
    output(n, mx)
    return mat


def tube(bm, pts, radii, sides=7):
    """A tapered tube along points."""
    rings = []
    for i, (p, r) in enumerate(zip(pts, radii)):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        a = d.orthogonal().normalized()
        b = d.cross(a).normalized()
        ring = [bm.verts.new(p + (a * math.cos(t) + b * math.sin(t)) * r)
                for t in (2 * math.pi * k / sides for k in range(sides))]
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(sides):
            bm.faces.new((r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k]))
    return rings


def blob(bm, centre, radius, seed, squash=0.8, rough=0.35, subdiv=3, leaves=None):
    """A lumpy ball of leaves: a dark core, with leaf cards on its skin when leaves is a list to fill."""
    tmp = bmesh.new()
    bmesh.ops.create_icosphere(tmp, subdivisions=subdiv, radius=1.0)
    off = Vector((seed * 7.3, seed * 3.1, seed * 5.7))
    for v in tmp.verts:
        p = v.co.copy()
        k = 1 + rough * (noise.noise(p * 1.6 + off)) + rough * 0.5 * noise.noise(p * 4.0 + off)
        v.co = Vector((p.x * radius * k, p.y * radius * k, p.z * radius * k * squash)) + centre
    # copy into bm
    if leaves is not None:
        bmesh.ops.subdivide_edges(tmp, edges=tmp.edges[:], cuts=1, use_grid_fill=True)
        tmp.normal_update()
        for v in tmp.verts:
            leaves.append((v.co.copy(), v.normal.copy()))
        for v in tmp.verts:
            v.co = centre + (v.co - centre) * 0.86
    vm = {v: bm.verts.new(v.co) for v in tmp.verts}
    for f in tmp.faces:
        nf = bm.faces.new([vm[v] for v in f.verts])
        nf.material_index = 2 if leaves is not None else 1
        nf.smooth = True
    tmp.free()


def leaf_cards(bm, spots, rnd, size=0.16, per=2, mat=1, long=1.0):
    """Little leaf-shaped cards on the clump skins, facing mostly outwards."""
    for p, nrm in spots:
        for k in range(per):
            pos = p + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * size * 0.9
            d = (nrm + Vector((rnd.uniform(-0.7, 0.7), rnd.uniform(-0.7, 0.7), rnd.uniform(-0.3, 0.9)))).normalized()
            a = d.orthogonal().normalized()
            a.rotate(Matrix.Rotation(rnd.uniform(0, 6.283), 3, d))
            b = d.cross(a).normalized()
            s = size * rnd.uniform(0.7, 1.25)
            tip, base = pos + a * s * long, pos - a * s * 0.25 * long
            v = [bm.verts.new(base), bm.verts.new(pos + a * s * 0.35 + b * s * 0.42),
                 bm.verts.new(tip), bm.verts.new(pos + a * s * 0.35 - b * s * 0.42)]
            f = bm.faces.new(v)
            f.material_index = mat
            f.smooth = True


def broadleaf(seed, height=6.0):
    rnd = random.Random(seed)
    bm = bmesh.new()
    lean = Vector((rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), 0))
    trunk_h = height * rnd.uniform(0.42, 0.52)
    pts = [Vector((0, 0, 0)) + lean * (t / 4) ** 2 + Vector((0, 0, trunk_h * t / 4)) for t in range(5)]
    r0 = 0.16 + height * 0.025
    tube(bm, pts, [r0 * (1.35 - 0.12 * i) for i in range(5)])
    top = pts[-1]
    ends = []
    nb = rnd.randint(4, 6)
    for i in range(nb):
        ang = 2 * math.pi * i / nb + rnd.uniform(-0.4, 0.4)
        out = rnd.uniform(0.9, 1.6) * height / 6
        upz = rnd.uniform(0.7, 1.5) * height / 6
        start = pts[rnd.randint(2, 4)]
        mid = start + Vector((math.cos(ang) * out * 0.5, math.sin(ang) * out * 0.5, upz * 0.6))
        end = start + Vector((math.cos(ang) * out, math.sin(ang) * out, upz))
        tube(bm, [start, mid, end], [r0 * 0.55, r0 * 0.35, r0 * 0.15], 5)
        ends.append(end)
    for f in bm.faces: f.material_index = 0
    # the crown: a big middle clump and one on each branch end
    crown = top + Vector((0, 0, height * 0.22))
    spots = []
    blob(bm, crown, height * 0.30, rnd.random() * 10, 0.85, leaves=spots)
    for i, e in enumerate(ends):
        blob(bm, e + Vector((0, 0, height * 0.05)), height * rnd.uniform(0.17, 0.24), rnd.random() * 10, 0.8, leaves=spots)
    for i in range(3):
        a = rnd.uniform(0, 2 * math.pi)
        blob(bm, crown + Vector((math.cos(a) * height * 0.18, math.sin(a) * height * 0.18, height * rnd.uniform(-0.05, 0.12))),
             height * rnd.uniform(0.15, 0.2), rnd.random() * 10, 0.8, subdiv=2, leaves=spots)
    leaf_cards(bm, spots, rnd, size=0.17, per=2)
    return bm


def conifer(seed, height=8.0):
    rnd = random.Random(seed)
    bm = bmesh.new()
    r0 = 0.12 + height * 0.02
    tube(bm, [Vector((0, 0, 0)), Vector((0, 0, height * 0.5)), Vector((0, 0, height))], [r0, r0 * 0.6, 0.02])
    for f in bm.faces: f.material_index = 0
    tiers = rnd.randint(5, 7)
    for i in range(tiers):
        t = i / (tiers - 1)
        z = height * (0.18 + 0.8 * t)
        rad = height * (0.30 * (1 - t) + 0.05)
        tmp = bmesh.new()
        bmesh.ops.create_cone(tmp, cap_ends=True, segments=12, radius1=rad, radius2=0.02, depth=height * 0.26)
        off = rnd.random() * 10
        for v in tmp.verts:
            p = v.co.copy()
            k = 1 + 0.18 * noise.noise(p * 2.2 + Vector((off, 0, 0)))
            v.co = Vector((p.x * k, p.y * k, p.z)) + Vector((0, 0, z))
        bmesh.ops.subdivide_edges(tmp, edges=tmp.edges[:], cuts=1, use_grid_fill=True)
        for v in tmp.verts:
            if v.co.z < z - height * 0.12:
                v.co.z -= 0.15 * abs(noise.noise(v.co * 3))
        tmp.normal_update()
        spots = [(v.co.copy(), v.normal.copy()) for v in tmp.verts if v.normal.z > -0.5]
        bmesh.ops.subdivide_edges(tmp, edges=tmp.edges[:], cuts=1)
        tmp.normal_update()
        spots += [(v.co.copy(), v.normal.copy()) for v in tmp.verts if v.normal.z > -0.5]
        vm = {v: bm.verts.new(v.co) for v in tmp.verts}
        for f in tmp.faces:
            nf = bm.faces.new([vm[v] for v in f.verts])
            nf.material_index = 2
            nf.smooth = True
        tmp.free()
        leaf_cards(bm, spots, rnd, size=0.2, per=3, long=1.6)
    return bm


_VARIANTS = {}


def variants(kind):
    """The shared meshes for a species, made once."""
    if kind in _VARIANTS: return _VARIANTS[kind]
    out = []
    if kind == 'oak':
        leaf = leaf_material('leaf_oak', '#1a2e10', '#355521', '#5c802d', '#a2ad4a')
        core = leaf_material('core_oak', '#0b1608', '#142410', '#1f3315', '#2c4219')
        for i in range(5):
            bm = broadleaf(100 + i, height=random.Random(i).uniform(5.2, 6.8))
            me = bpy.data.meshes.new('oak%d' % i)
            bm.to_mesh(me)
            bm.free()
            me.materials.append(bark_material())
            me.materials.append(leaf)
            me.materials.append(core)
            out.append(me)
    elif kind == 'pine':
        leaf = leaf_material('leaf_pine', '#0e2015', '#1d3a22', '#33582f', '#66804a')
        core = leaf_material('core_pine', '#071009', '#0d1a10', '#142416', '#1c3020')
        for i in range(4):
            bm = conifer(200 + i, height=random.Random(i + 7).uniform(7.0, 9.5))
            me = bpy.data.meshes.new('pine%d' % i)
            bm.to_mesh(me)
            bm.free()
            me.materials.append(bark_material())
            me.materials.append(leaf)
            me.materials.append(core)
            out.append(me)
    _VARIANTS[kind] = out
    return out


def plant(kind, loc, rnd, coll, name='tree', scale=1.0):
    me = rnd.choice(variants(kind))
    ob = bpy.data.objects.new(name, me)
    ob.location = loc
    ob.rotation_euler = (0, 0, rnd.uniform(0, 2 * math.pi))
    s = scale * rnd.uniform(0.85, 1.12)
    ob.scale = (s, s, s)
    coll.objects.link(ob)
    return ob
