"""Small things scattered by the thousand (grass tufts, flowers, pebbles, shells), as instances on points.

The points are worked out in numpy from the area's fields; a Geometry Nodes modifier puts a copy of the
source object on each point with its own turn and size, so a whole meadow costs almost nothing.
"""
import math
import random

import bpy
import bmesh
import numpy as np
from mathutils import Vector, noise

from common import Nodes, principled, output, hexc, ramp, link, place


def instancer(name, pts, src, coll, rot=None, scale=None, seed=1):
    """pts: (n,3) Blender locations. src: an object (or collection) to copy onto each."""
    n = len(pts)
    rs = np.random.RandomState(seed)
    if rot is None: rot = rs.uniform(0, 2 * math.pi, n)
    if scale is None: scale = rs.uniform(0.8, 1.2, n)
    me = bpy.data.meshes.new(name + '_pts')
    me.vertices.add(n)
    me.vertices.foreach_set('co', np.asarray(pts, np.float32).ravel())
    a = me.attributes.new('rot', 'FLOAT', 'POINT')
    a.data.foreach_set('value', np.asarray(rot, np.float32))
    b = me.attributes.new('scl', 'FLOAT', 'POINT')
    b.data.foreach_set('value', np.asarray(scale, np.float32))
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)

    ng = bpy.data.node_groups.new(name + '_gn', 'GeometryNodeTree')
    ng.interface.new_socket('Geometry', in_out='INPUT', socket_type='NodeSocketGeometry')
    ng.interface.new_socket('Geometry', in_out='OUTPUT', socket_type='NodeSocketGeometry')
    N = ng.nodes
    gi, go = N.new('NodeGroupInput'), N.new('NodeGroupOutput')
    inst = N.new('GeometryNodeInstanceOnPoints')
    if isinstance(src, bpy.types.Collection):
        info = N.new('GeometryNodeCollectionInfo')
        info.inputs['Collection'].default_value = src
        info.inputs['Separate Children'].default_value = True
        info.inputs['Reset Children'].default_value = True
        ng.links.new(info.outputs[0], inst.inputs['Instance'])
        inst.inputs['Pick Instance'].default_value = True
        rnd = N.new('FunctionNodeRandomValue')
        rnd.data_type = 'INT'
        rnd.inputs['Min'].default_value = 0 if 'Min' in rnd.inputs else None
        for s in rnd.inputs:
            if s.name == 'Max' and s.type == 'INT': s.default_value = 1000
            if s.name == 'Seed': s.default_value = seed
        ng.links.new([o for o in rnd.outputs if o.type == 'INT'][0], inst.inputs['Instance Index'])
    else:
        info = N.new('GeometryNodeObjectInfo')
        info.inputs['Object'].default_value = src
        ng.links.new(info.outputs['Geometry'], inst.inputs['Instance'])
    ar = N.new('GeometryNodeInputNamedAttribute')
    ar.data_type = 'FLOAT'
    ar.inputs['Name'].default_value = 'rot'
    asc = N.new('GeometryNodeInputNamedAttribute')
    asc.data_type = 'FLOAT'
    asc.inputs['Name'].default_value = 'scl'
    cx = N.new('ShaderNodeCombineXYZ')
    ng.links.new([o for o in ar.outputs if o.type == 'VALUE'][0], cx.inputs['Z'])
    ng.links.new(gi.outputs[0], inst.inputs['Points'])
    ng.links.new(cx.outputs[0], inst.inputs['Rotation'])
    ng.links.new([o for o in asc.outputs if o.type == 'VALUE'][0], inst.inputs['Scale'])
    ng.links.new(inst.outputs[0], go.inputs[0])
    mod = ob.modifiers.new('scatter', 'NODES')
    mod.node_group = ng
    return ob


def hidden(name):
    """A collection for source objects that shouldn't render themselves."""
    c = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if c.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(c)
    c.hide_render = True
    c.hide_viewport = True
    return c


# ------------------------------------------------------------------ source objects

def grass_material(name, base, tip, dry):
    mat = bpy.data.materials.get(name)
    if mat: return mat
    mat = bpy.data.materials.new(name)
    n = Nodes(mat)
    tc = n.new('ShaderNodeTexCoord')
    sep = n.new('ShaderNodeSeparateXYZ')
    n.link(tc, 'Generated', sep, 'Vector')
    oi = n.new('ShaderNodeObjectInfo')
    pos = n.new('ShaderNodeNewGeometry')
    nz = n.new('ShaderNodeTexNoise', in_Scale=0.25, in_Detail=2.0)
    n.link(pos, 'Position', nz, 'Vector')
    c1 = ramp(n, [(0.0, base), (1.0, tip)], sep, 'Z')
    mx = n.new('ShaderNodeMix', data_type='RGBA')
    n.link(c1, 0, mx, 'A')
    n.inp(mx, 'B').default_value = (*hexc(dry), 1)
    fr = n.new('ShaderNodeMapRange', in_From_Min=0.55, in_From_Max=0.75, in_To_Max=0.7)
    n.link(nz, 'Fac', fr, 'Value')
    n.link(fr, 'Result', mx, 'Factor')
    b = principled(n, rough=0.6)
    n.link(mx, 'Result', b, 'Base Color')
    tr = n.new('ShaderNodeBsdfTranslucent')
    n.link(mx, 'Result', tr, 'Color')
    ms = n.new('ShaderNodeMixShader', in_Fac=0.3)
    n.link(b, 0, ms, 1)
    n.link(tr, 0, ms, 2)
    output(n, ms)
    return mat


def tuft(name, blades=22, h=0.28, spread=0.13, bend=0.35, mat=None, seed=0, coll=None):
    rnd = random.Random(seed)
    bm = bmesh.new()
    for i in range(blades):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0, spread)
        base = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        hh = h * rnd.uniform(0.55, 1.15)
        lean = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.2, 1.0) * bend * hh + Vector((rnd.uniform(-0.04, 0.04), rnd.uniform(-0.04, 0.04), 0))
        w = rnd.uniform(0.012, 0.022)
        side = Vector((-math.sin(a + 1.2), math.cos(a + 1.2), 0)) * w
        segs = 3
        prev = None
        for s in range(segs + 1):
            t = s / segs
            c = base + lean * t * t + Vector((0, 0, hh * t))
            ww = 1 - t * 0.92
            v1, v2 = bm.verts.new(c - side * ww), bm.verts.new(c + side * ww)
            if prev: bm.faces.new((prev[0], prev[1], v2, v1))
            prev = (v1, v2)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons: p.use_smooth = True
    if mat: me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)
    return ob


def flower(name, petal, coll, seed=0):
    rnd = random.Random(seed)
    bm = bmesh.new()
    stem_m = 0
    for k in range(rnd.randint(2, 4)):
        bx, by = rnd.uniform(-0.06, 0.06), rnd.uniform(-0.06, 0.06)
        hh = rnd.uniform(0.12, 0.26)
        v = [bm.verts.new((bx - 0.004, by, 0)), bm.verts.new((bx + 0.004, by, 0)), bm.verts.new((bx + 0.004, by, hh)), bm.verts.new((bx - 0.004, by, hh))]
        bm.faces.new(v).material_index = 0
        c = Vector((bx, by, hh))
        tmp = bmesh.new()
        bmesh.ops.create_uvsphere(tmp, u_segments=6, v_segments=4, radius=0.028)
        vm = {}
        for vv in tmp.verts:
            vm[vv] = bm.verts.new(Vector((vv.co.x, vv.co.y, vv.co.z * 0.45)) + c)
        for f in tmp.faces:
            bm.faces.new([vm[x] for x in f.verts]).material_index = 1
        tmp.free()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(grass_material('grass_short', '#2e4a1c', '#6d8f3a', '#a29a55'))
    pm = bpy.data.materials.new(name + '_petal')
    n = Nodes(pm)
    b = principled(n, hexc(petal), 0.5)
    output(n, b)
    me.materials.append(pm)
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)
    return ob


def pebble(name, coll, seed=0, size=0.06, colour='#6f695e'):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=size)
    off = Vector((seed, seed * 2, seed * 3))
    for v in bm.verts:
        k = 1 + 0.3 * noise.noise(v.co * 20 + off)
        v.co = Vector((v.co.x * k * 1.3, v.co.y * k, v.co.z * k * 0.55))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons: p.use_smooth = True
    m = bpy.data.materials.get('pebble_' + colour) or bpy.data.materials.new('pebble_' + colour)
    if not m.node_tree or len(m.node_tree.nodes) < 3:
        n = Nodes(m)
        oi = n.new('ShaderNodeObjectInfo')
        nz = n.new('ShaderNodeTexNoise', in_Scale=30.0)
        c = ramp(n, [(0.0, '#4a453d'), (0.5, colour), (1.0, '#9a9284')], oi, 'Random')
        b = principled(n, rough=0.7)
        n.link(c, 0, b, 'Base Color')
        output(n, b)
    me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)
    return ob
