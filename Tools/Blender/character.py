"""People, modelled and animated in code, rendered as walking sheets for the game.

A figure is a little hierarchy of smooth parts hinged at the joints (hips, knees, shoulders, elbows, neck),
dressed in clothes that are slightly larger shells over the body. walk(phase) poses it; the sheet is
3 frames (standing, left foot forward, right foot forward) x 4 facings (down, up, left, right), the same
layout as the pixel-art sheets, rendered from the game's camera with the same sun.

    python character.py player_male            -> Sprites/HD/Walk/<name>.png
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import bmesh
from mathutils import Vector, Matrix, Euler

import common
from common import Nodes, principled, output, hexc, ramp, link, ALPHA, SA, CA


# ------------------------------------------------------------------ materials

def skin_mat(name, colour):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name)
    n = Nodes(m)
    tc = n.new('ShaderNodeTexCoord')
    nz = n.new('ShaderNodeTexNoise', in_Scale=40.0, in_Detail=3.0)
    n.link(tc, 'Object', nz, 'Vector')
    c = hexc(colour)
    r = n.new('ShaderNodeValToRGB')
    r.color_ramp.elements[0].color = (*[x * 0.9 for x in c], 1)
    r.color_ramp.elements[1].color = (*[min(1, x * 1.06) for x in c], 1)
    n.link(nz, 'Fac', r, 'Fac')
    p = principled(n, rough=0.5)
    n.link(r, 0, p, 'Base Color')
    try:
        n.inp(p, 'Subsurface Weight').default_value = 0.25
        n.inp(p, 'Subsurface Radius').default_value = (0.02, 0.008, 0.005)
    except KeyError:
        pass
    output(n, p)
    return m


def cloth_mat(name, colour, rough=0.85, weave=60.0, var=0.18, sheen=0.3):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name)
    n = Nodes(m)
    tc = n.new('ShaderNodeTexCoord')
    nz = n.new('ShaderNodeTexNoise', in_Scale=6.0, in_Detail=6.0)
    n.link(tc, 'Object', nz, 'Vector')
    wv = n.new('ShaderNodeTexWave', wave_type='BANDS', bands_direction='Z', in_Scale=weave, in_Distortion=1.0)
    n.link(tc, 'Object', wv, 'Vector')
    c = hexc(colour)
    r = n.new('ShaderNodeValToRGB')
    r.color_ramp.elements[0].color = (*[x * (1 - var) for x in c], 1)
    r.color_ramp.elements[1].color = (*[min(1, x * (1 + var)) for x in c], 1)
    n.link(nz, 'Fac', r, 'Fac')
    p = principled(n, rough=rough)
    n.link(r, 0, p, 'Base Color')
    try: n.inp(p, 'Sheen Weight').default_value = sheen
    except KeyError: pass
    bp = n.new('ShaderNodeBump', in_Strength=0.15)
    n.link(wv, 'Fac', bp, 'Height')
    n.link(bp, 'Normal', p, 'Normal')
    output(n, p)
    return m


def leather_mat(name, colour):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name)
    n = Nodes(m)
    tc = n.new('ShaderNodeTexCoord')
    nz = n.new('ShaderNodeTexNoise', in_Scale=14.0, in_Detail=8.0)
    n.link(tc, 'Object', nz, 'Vector')
    c = hexc(colour)
    r = n.new('ShaderNodeValToRGB')
    r.color_ramp.elements[0].color = (*[x * 0.7 for x in c], 1)
    r.color_ramp.elements[1].color = (*[min(1, x * 1.2) for x in c], 1)
    n.link(nz, 'Fac', r, 'Fac')
    p = principled(n, rough=0.55)
    n.link(r, 0, p, 'Base Color')
    bp = n.new('ShaderNodeBump', in_Strength=0.3)
    n.link(nz, 'Fac', bp, 'Height')
    n.link(bp, 'Normal', p, 'Normal')
    output(n, p)
    return m


def hair_mat(name, colour):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name)
    n = Nodes(m)
    tc = n.new('ShaderNodeTexCoord')
    wv = n.new('ShaderNodeTexWave', wave_type='BANDS', bands_direction='Z', in_Scale=40.0, in_Distortion=4.0, in_Detail=4.0)
    n.link(tc, 'Object', wv, 'Vector')
    c = hexc(colour)
    r = n.new('ShaderNodeValToRGB')
    r.color_ramp.elements[0].color = (*[x * 0.6 for x in c], 1)
    r.color_ramp.elements[1].color = (*[min(1, x * 1.3 + 0.02) for x in c], 1)
    n.link(wv, 'Fac', r, 'Fac')
    p = principled(n, rough=0.6)
    n.link(r, 0, p, 'Base Color')
    try: n.inp(p, 'Sheen Weight').default_value = 0.15
    except KeyError: pass
    bp = n.new('ShaderNodeBump', in_Strength=0.5)
    n.link(wv, 'Fac', bp, 'Height')
    n.link(bp, 'Normal', p, 'Normal')
    output(n, p)
    return m


def flat_mat(name, colour, rough=0.4, emit=0.0):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name)
    n = Nodes(m)
    p = principled(n, hexc(colour), rough)
    if emit:
        n.inp(p, 'Emission Color').default_value = (*hexc(colour), 1)
        n.inp(p, 'Emission Strength').default_value = emit
    output(n, p)
    return m


# ------------------------------------------------------------------ shapes

def capsule(name, length, r0, r1, mat, coll, sx=1.0, sy=1.0, segs=12, rings=8):
    """A smooth tapered capsule hanging down from the origin (the joint) along -Z."""
    bm = bmesh.new()
    verts = []
    prof = []
    for i in range(rings + 1):          # top cap
        a = (math.pi / 2) * (1 - i / rings)
        prof.append((math.cos(a) * r0, math.sin(a) * r0))
    for i in range(1, 6):
        t = i / 6
        prof.append((r0 + (r1 - r0) * t, -length * t))
    for i in range(rings + 1):          # bottom cap
        a = (math.pi / 2) * (i / rings)
        prof.append((math.cos(a) * r1, -length - math.sin(a) * r1))
    for (r, z) in prof:
        ring = [bm.verts.new((math.cos(2 * math.pi * k / segs) * r * sx, math.sin(2 * math.pi * k / segs) * r * sy, z)) for k in range(segs)]
        verts.append(ring)
    for a, b in zip(verts, verts[1:]):
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons: p.use_smooth = True
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)
    return ob


def ellipsoid(name, rx, ry, rz, mat, coll, centre=(0, 0, 0), subdiv=3, shape=None):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    for v in bm.verts:
        p = Vector((v.co.x * rx, v.co.y * ry, v.co.z * rz))
        if shape: p = shape(p, v.co.copy())
        v.co = p + Vector(centre)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons: p.use_smooth = True
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)
    return ob


def parent(child, par, at):
    child.parent = par
    child.location = at
    return child


# ------------------------------------------------------------------ the figure

PRESETS = {
    # starting clothes after the wreck: a torn linen shirt, dark trousers, worn boots
    'player_male': dict(height=1.76, build=1.0, skin='#c8946e', hair='short', hair_col='#5a3a22', beard=None,
                        shirt='#a39578', trousers='#4a4134', boots='#4a3424', belt='#2e2218', female=False),
    'player_female': dict(height=1.66, build=0.9, skin='#c8946e', hair='long', hair_col='#3a2618', beard=None,
                          shirt='#b9ab8c', trousers='#3d3a33', boots='#4a3424', belt='#3a2a1c', female=True),
    'npc_ida': dict(height=1.70, build=0.95, skin='#d0a07a', hair='long', hair_col='#7a3a1e', beard=None,
                    shirt='#6a6a48', trousers='#3c3a2c', boots='#3a2a1c', belt='#2e2218', coat='#6e4c2c', female=True),
    'npc_yenson': dict(height=1.70, build=1.05, skin='#b07a58', hair='bald_ring', hair_col='#b8b4ac', beard='full', stoop=0.12, beard_col='#c8c4bc',
                       shirt='#5d6b5a', trousers='#4b4236', boots='#2e2620', belt='#2e2218', coat='#7a6a3a', female=False),
    'npc_fisher': dict(height=1.74, build=1.1, skin='#bf8a62', hair='cap', hair_col='#5a3a22', beard='stubble',
                       shirt='#6f7f8a', trousers='#3b3a35', boots='#2a2622', belt='#3a2a1c', coat='#a38a3c', female=False),
}


class Figure:
    """A seamless body grown from a stick skeleton (Blender's skin modifier), smoothed, bound to an armature,
    painted into shirt, trousers, boots and skin by where each face sits; a modelled head, hair and beard ride
    on the head bone."""

    def __init__(self, name, spec, coll):
        self.spec = spec
        H = spec['height']
        b = spec.get('build', 1.0)
        f = spec.get('female', False)
        self.H = H
        self.name = name
        mats = {
            'skin': skin_mat(name + '_skin', spec['skin']),
            'shirt': cloth_mat(name + '_shirt', spec['shirt']),
            'trousers': cloth_mat(name + '_trousers', spec['trousers'], weave=90),
            'boots': leather_mat(name + '_boots', spec['boots']),
            'coat': cloth_mat(name + '_coat', spec.get('coat') or spec['shirt'], rough=0.6, sheen=0.1),
            'belt': leather_mat(name + '_belt', spec['belt']),
        }
        order = ['skin', 'shirt', 'trousers', 'boots', 'coat', 'belt']
        sw = 0.105 * H * (1 if not f else 0.92) * b
        hip = 0.052 * H * (1 if not f else 1.1)
        J = {   # joint positions (x, y, z) in metres; the figure faces -y
            'pelvis': (0, 0, 0.50 * H), 'waist': (0, 0.005, 0.585 * H), 'chest': (0, 0.0, 0.70 * H), 'neck': (0, 0.01, 0.80 * H),
            'neck2': (0, 0.0, 0.835 * H),
            'sh.L': (-sw, 0.01, 0.775 * H), 'el.L': (-sw - 0.02 * H, 0.015, 0.615 * H), 'wr.L': (-sw - 0.025 * H, -0.005, 0.47 * H), 'ha.L': (-sw - 0.027 * H, -0.01, 0.415 * H),
            'sh.R': (sw, 0.01, 0.775 * H), 'el.R': (sw + 0.02 * H, 0.015, 0.615 * H), 'wr.R': (sw + 0.025 * H, -0.005, 0.47 * H), 'ha.R': (sw + 0.027 * H, -0.01, 0.415 * H),
            'hip.L': (-hip, 0, 0.475 * H), 'kn.L': (-hip * 1.05, -0.012 * H, 0.27 * H), 'an.L': (-hip * 1.05, 0.0, 0.05 * H), 'to.L': (-hip * 1.08, -0.085 * H, 0.018 * H),
            'hip.R': (hip, 0, 0.475 * H), 'kn.R': (hip * 1.05, -0.012 * H, 0.27 * H), 'an.R': (hip * 1.05, 0.0, 0.05 * H), 'to.R': (hip * 1.08, -0.085 * H, 0.018 * H),
        }
        R = {   # skin radii (x, y)
            'pelvis': (0.078 * H * b, 0.058 * H), 'waist': (0.066 * H * b * (0.92 if f else 1), 0.05 * H), 'chest': (0.083 * H * b, 0.056 * H), 'neck': (0.03 * H, 0.03 * H),
            'neck2': (0.028 * H, 0.028 * H),
            'sh.L': (0.036 * H * b, 0.036 * H * b), 'el.L': (0.027 * H, 0.027 * H), 'wr.L': (0.02 * H, 0.018 * H), 'ha.L': (0.022 * H, 0.014 * H),
            'hip.L': (0.052 * H * b, 0.052 * H * b), 'kn.L': (0.034 * H, 0.034 * H), 'an.L': (0.025 * H, 0.025 * H), 'to.L': (0.03 * H, 0.022 * H),
        }
        for k in list(R):
            if k.endswith('.L'): R[k[:-2] + '.R'] = R[k]
        E = [('pelvis', 'waist'), ('waist', 'chest'), ('chest', 'neck'), ('neck', 'neck2'),
             ('chest', 'sh.L'), ('sh.L', 'el.L'), ('el.L', 'wr.L'), ('wr.L', 'ha.L'),
             ('chest', 'sh.R'), ('sh.R', 'el.R'), ('el.R', 'wr.R'), ('wr.R', 'ha.R'),
             ('pelvis', 'hip.L'), ('hip.L', 'kn.L'), ('kn.L', 'an.L'), ('an.L', 'to.L'),
             ('pelvis', 'hip.R'), ('hip.R', 'kn.R'), ('kn.R', 'an.R'), ('an.R', 'to.R')]
        names = list(J)
        me = bpy.data.meshes.new(name + '_skel')
        me.from_pydata([J[k] for k in names], [(names.index(a), names.index(b)) for a, b in E], [])
        body = bpy.data.objects.new(name + '_body', me)
        link(body, coll)
        sk = body.modifiers.new('skin', 'SKIN')
        sk.use_smooth_shade = True
        for i, k in enumerate(names):
            body.data.skin_vertices[0].data[i].radius = R[k]
        body.data.skin_vertices[0].data[names.index('pelvis')].use_root = True
        bpy.context.view_layer.objects.active = body
        body.select_set(True)
        bpy.ops.object.modifier_apply(modifier='skin')
        sub = body.modifiers.new('sub', 'SUBSURF')
        sub.levels = 2
        sub.render_levels = 2
        bpy.ops.object.modifier_apply(modifier='sub')
        for m in order: body.data.materials.append(mats[m])

        self.body = body

        # the armature, bone by bone along the skeleton
        ad = bpy.data.armatures.new(name + '_rig')
        arm = bpy.data.objects.new(name + '_rig', ad)
        link(arm, coll)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode='EDIT')
        bones = {}

        def bone(nm, a, b, par=None):
            bb = ad.edit_bones.new(nm)
            bb.head = Vector(J[a]) if isinstance(a, str) else Vector(a)
            bb.tail = Vector(J[b]) if isinstance(b, str) else Vector(b)
            bb.roll = 0
            if par: bb.parent = bones[par]
            bones[nm] = bb
            return bb
        bone('root', (0, 0, 0.42 * H), 'pelvis')
        bone('spine', 'pelvis', 'chest', 'root')
        bone('neck', 'chest', 'neck2', 'spine')
        bone('head', 'neck2', (0, 0, 0.95 * H), 'neck')
        for s in 'LR':
            bone('upper.' + s, 'sh.' + s, 'el.' + s, 'spine')
            bone('fore.' + s, 'el.' + s, 'wr.' + s, 'upper.' + s)
            bone('hand.' + s, 'wr.' + s, 'ha.' + s, 'fore.' + s)
            bone('thigh.' + s, 'hip.' + s, 'kn.' + s, 'root')
            bone('shin.' + s, 'kn.' + s, 'an.' + s, 'thigh.' + s)
            bone('foot.' + s, 'an.' + s, 'to.' + s, 'shin.' + s)
        bpy.ops.object.mode_set(mode='OBJECT')
        self.arm = arm

        # weights: each vertex to its nearest bones, blended smoothly near the joints
        segs = [(b.name, b.head_local.copy(), b.tail_local.copy()) for b in ad.bones if b.name != 'root']
        groups = {nm: body.vertex_groups.new(name=nm) for nm, _, _ in segs}
        groups['root'] = body.vertex_groups.new(name='root')

        def dist(p, a, b):
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
            return (a + ab * t - p).length, t
        for v in body.data.vertices:
            p = v.co
            ds = sorted(((dist(p, a, b)[0], nm) for nm, a, b in segs))
            # keep legs off the other leg and arms off the body
            d0, n0 = ds[0]
            cand = [(d, nm) for d, nm in ds[:3] if d < d0 * 1.35 + 0.01]
            ws = [(nm, 1.0 / max(d, 1e-4) ** 4) for d, nm in cand]
            tot = sum(w for _, w in ws)
            for nm, w in ws:
                groups[nm].add([v.index], w / tot, 'REPLACE')
        # paint the regions: by the bone each face mostly follows, and its height
        coat = bool(spec.get('coat'))
        gi = {g.index: g.name for g in body.vertex_groups}
        vbone = []
        for v in body.data.vertices:
            best = max(v.groups, key=lambda g: g.weight) if len(v.groups) else None
            vbone.append(gi[best.group] if best else 'spine')
        sleeve = 'coat' if coat else 'shirt'
        for p in body.data.polygons:
            z = p.center.z / H
            names_ = [vbone[i] for i in p.vertices]
            bn = max(set(names_), key=names_.count)
            part = bn.split('.')[0]
            if part in ('head', 'neck'): m = 'skin' if z > 0.79 else sleeve
            elif part == 'hand': m = 'skin'
            elif part == 'fore': m = 'coat' if coat else ('skin' if z < 0.56 else 'shirt')
            elif part == 'upper': m = sleeve
            elif part == 'spine':
                if z > 0.565: m = sleeve
                elif z > 0.535 and not coat: m = 'belt'
                else: m = 'coat' if coat else 'trousers'
            elif part == 'thigh': m = 'coat' if coat and z > 0.36 else 'trousers'
            elif part == 'shin': m = 'trousers' if z > 0.15 else 'boots'
            else: m = 'boots'
            p.material_index = order.index(m)

        mod = body.modifiers.new('rig', 'ARMATURE')
        mod.object = arm
        body.parent = arm

        # head, face, hair and beard, riding on the head bone
        r = 0.082 * H
        self.head_r = r
        hb = ad.bones['head']
        base = Vector(J['neck2'])
        hs = []

        def head_shape(p, u):
            if u.z < -0.2: p = Vector((p.x * (1 - 0.28 * (-u.z - 0.2)), p.y * (1 - 0.12 * (-u.z - 0.2)), p.z))
            return p
        hs.append(ellipsoid(name + '_head', r * 0.86, r * 0.96, r * 1.08, mats['skin'], coll, base + Vector((0, 0.005, r * 0.95)), 3, head_shape))
        hs.append(ellipsoid(name + '_nose', r * 0.13, r * 0.2, r * 0.2, mats['skin'], coll, base + Vector((0, -r * 0.93, r * 0.85)), 2))
        eye = flat_mat('eye', '#120e0b', 0.15)
        white = flat_mat('eyewhite', '#d8d0c4', 0.3)
        brow = hair_mat(name + '_brow', spec['hair_col'])
        for sx in (-1, 1):
            hs.append(ellipsoid(name + '_eyew%d' % sx, r * 0.15, r * 0.06, r * 0.1, white, coll, base + Vector((sx * r * 0.34, -r * 0.83, r * 1.05)), 2))
            hs.append(ellipsoid(name + '_eye%d' % sx, r * 0.08, r * 0.05, r * 0.09, eye, coll, base + Vector((sx * r * 0.34, -r * 0.87, r * 1.05)), 2))
            hs.append(ellipsoid(name + '_brow%d' % sx, r * 0.2, r * 0.06, r * 0.045, brow, coll, base + Vector((sx * r * 0.34, -r * 0.86, r * 1.22)), 2))
            hs.append(ellipsoid(name + '_ear%d' % sx, r * 0.08, r * 0.19, r * 0.25, mats['skin'], coll, base + Vector((sx * r * 0.88, 0, r * 0.95)), 2))
        hs += self._hair(name, spec, r, coll, base)
        hs += self._beard(name, spec, r, coll, base)
        hs += self._extras(name, spec, coll, J, H)
        mw = (arm.matrix_world @ hb.matrix_local @ Matrix.Translation((0, hb.length, 0)))
        for o in hs:
            o.parent = arm
            o.parent_type = 'BONE'
            o.parent_bone = o.get('bone') or 'head'
            pb = ad.bones[o.parent_bone]
            o.matrix_parent_inverse = (arm.matrix_world @ pb.matrix_local @ Matrix.Translation((0, pb.length, 0))).inverted()
        self.root = arm
        self.stoop = spec.get('stoop', 0.0)
        for pb in arm.pose.bones: pb.rotation_mode = 'XYZ'

    def _hair(self, name, spec, r, coll, base):
        style = spec.get('hair')
        mat = hair_mat(name + '_hair', spec['hair_col'])
        out = []

        def cap_shape(lo, back_lo, fringe, puff=1.0):
            def f(p, u):
                cut = lo + (back_lo - lo) * max(0.0, u.y) + fringe * max(0.0, -u.y)
                if u.z < cut: p = Vector((p.x, p.y, cut * r * 1.12))
                n = 1 + 0.04 * puff * math.sin(u.x * 23 + u.z * 17) * math.cos(u.y * 19)
                return p * n
            return f
        c = base + Vector((0, 0.01, r * 1.0))
        if style == 'short':
            out.append(ellipsoid(name + '_hair', r * 0.94, r * 1.03, r * 1.12, mat, coll, c, 3, cap_shape(0.15, -0.6, 0.25)))
        elif style == 'long':
            out.append(ellipsoid(name + '_hair', r * 0.96, r * 1.05, r * 1.13, mat, coll, c, 3, cap_shape(0.15, -0.65, 0.25)))
            back = capsule(name + '_hairback', r * 2.0, r * 0.78, r * 0.5, mat, coll, 1.12, 0.55)
            back.location = base + Vector((0, r * 0.55, r * 1.25))
            back.rotation_euler = (-0.2, 0, 0)
            out.append(back)
        elif style == 'bald_ring':
            def ring(p, u):
                if u.z > 0.25: p = Vector((p.x * 0.8, p.y * 0.8, 0.25 * r))
                return p
            out.append(ellipsoid(name + '_hair', r * 0.92, r * 1.0, r * 0.95, mat, coll, c + Vector((0, r * 0.08, 0)), 3, ring))
        elif style == 'cap':
            cm = cloth_mat(name + '_capc', spec.get('cap_col', '#4a3428'), 0.9)
            out.append(ellipsoid(name + '_hair', r * 0.93, r * 1.02, r * 1.1, mat, coll, c, 3, cap_shape(0.1, -0.6, 0.35)))
            out.append(ellipsoid(name + '_cap', r * 1.0, r * 1.06, r * 0.62, cm, coll, c + Vector((0, 0.0, r * 0.62)), 3))
            out.append(ellipsoid(name + '_brim', r * 0.68, r * 0.48, r * 0.07, cm, coll, c + Vector((0, -r * 0.9, r * 0.55)), 2))
        return out

    def _beard(self, name, spec, r, coll, base):
        b = spec.get('beard')
        if not b: return []
        if b == 'full':
            mat = hair_mat(name + '_beardm', spec.get('beard_col', spec['hair_col']))

            def shape(p, u):
                return Vector((p.x, p.y, p.z * (1.4 if u.z < 0 else 0.5)))
            return [ellipsoid(name + '_beard', r * 0.66, r * 0.42, r * 0.5, mat, coll, base + Vector((0, -r * 0.6, r * 0.42)), 3, shape),
                    ellipsoid(name + '_tache', r * 0.4, r * 0.15, r * 0.1, mat, coll, base + Vector((0, -r * 0.95, r * 0.62)), 2)]
        if b == 'stubble':
            sm = hair_mat(name + '_stubble', '#4a3c30')
            return [ellipsoid(name + '_beard', r * 0.8, r * 0.7, r * 0.42, sm, coll, base + Vector((0, -r * 0.3, r * 0.45)), 3)]
        return []

    def _extras(self, name, spec, coll, J, H):
        """A belt and buckle; whatever else the preset carries."""
        out = []
        if not spec.get('coat'):
            bk = ellipsoid(name + '_buckle', 0.02, 0.012, 0.016, flat_mat('brass', '#9c7c3c', 0.35), coll, (0, -0.052 * H, 0.55 * H), 2)
            bk['bone'] = 'spine'
            out.append(bk)
        return out

    def pose(self, phase, amount=1.0):
        """Walking pose. phase 0..1 round the cycle; amount 0 is standing still."""
        P = self.arm.pose.bones
        s = math.sin(2 * math.pi * phase) * amount
        c = math.cos(2 * math.pi * phase) * amount
        for side, sg in (('L', 1), ('R', -1)):
            P['thigh.' + side].rotation_euler = (sg * s * 0.33, 0, 0)
            P['shin.' + side].rotation_euler = (KNEE * (max(0.0, -sg * s) * 0.45 + 0.04 * amount), 0, 0)
            P['foot.' + side].rotation_euler = (-sg * s * 0.2, 0, 0)
            P['upper.' + side].rotation_euler = (-sg * s * 0.4, 0, 0)
            P['fore.' + side].rotation_euler = (0.3 + 0.25 * max(0.0, sg * s), 0, 0)
        P['spine'].rotation_euler = (-self.stoop - 0.03 * amount, s * 0.08, 0)
        P['neck'].rotation_euler = (self.stoop * 0.7, -s * 0.05, 0)
        P['root'].location = (0, -0.012 * abs(c), 0)


# ------------------------------------------------------------------ rendering a sheet

KNEE = float(os.environ.get('KNEE', 1))
FACINGS = [('down', 0.0), ('up', math.pi), ('left', -math.pi / 2), ('right', math.pi / 2)]


def stage(ppt):
    """Camera and light for a figure frame 1.5 tiles wide and 2 tall. The figure's own tile is the bottom-middle one
    (x 0.25..1.25, rows 1..2); its feet stand 3/4 of the way down it, at (0.75, 1.75)."""
    sc = bpy.context.scene
    import render as R
    R.setup(48, ppt)
    sc.render.film_transparent = True
    cam = R.camera('figcam')
    cam.data.ortho_scale = 1.5
    sc.render.resolution_x = int(ppt * 1.5)
    sc.render.resolution_y = int(ppt * 2)
    centre = common.place(0.75, 1.0, 0)
    fwd = Vector((0, SA, -CA))
    cam.location = centre - fwd * 50
    # a ground that only catches the shadow
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=3)
    me = bpy.data.meshes.new('catcher')
    bm.to_mesh(me)
    bm.free()
    g = bpy.data.objects.new('catcher', me)
    link(g)
    g.location = common.place(0.75, 1.75, 0)
    g.is_shadow_catcher = True
    import terrain
    terrain.sky_and_sun()
    return cam


def sheet(name, spec, out_dir, ppt=96):
    common.reset()
    coll = bpy.context.scene.collection
    fig = Figure(name, spec, coll)
    stage(ppt)
    fig.root.location = common.place(0.75, 1.75, 0)
    sc = bpy.context.scene
    frames = []
    tmp = os.path.join(out_dir, '_tmp')
    os.makedirs(tmp, exist_ok=True)
    for row, (facing, rz) in enumerate(FACINGS):
        fig.root.rotation_euler = (0, 0, rz)
        for col, (ph, amt) in enumerate(((0.0, 0.0), (0.25, 1.0), (0.75, 1.0))):
            fig.pose(ph, amt)
            bpy.context.view_layer.update()
            p = os.path.join(tmp, '%s_%d_%d.png' % (name, row, col))
            sc.render.filepath = p
            bpy.ops.render.render(write_still=True)
            frames.append((row, col, p))
    # stitch with Blender's image API (no Pillow inside Blender's Python)
    import numpy as np
    W, Hh = int(ppt * 1.5), int(ppt * 2)
    big = np.zeros((Hh * 4, W * 3, 4), np.float32)
    for row, col, p in frames:
        im = bpy.data.images.load(p)
        px = np.array(im.pixels[:], np.float32).reshape(Hh, W, 4)[::-1]
        big[row * Hh:(row + 1) * Hh, col * W:(col + 1) * W] = px
        bpy.data.images.remove(im)
    img = bpy.data.images.new(name, W * 3, Hh * 4, alpha=True)
    img.pixels.foreach_set(big[::-1].ravel())
    img.filepath_raw = os.path.join(out_dir, name + '.png')
    img.file_format = 'PNG'
    img.save()
    for _, _, p in frames: os.remove(p)
    print('sheet', img.filepath_raw)


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    out = os.environ.get('OUTDIR', os.path.join(common.OUT, 'Walk'))
    os.makedirs(out, exist_ok=True)
    for nm in (args or ['player_male']):
        sheet(nm, PRESETS[nm], out, int(os.environ.get('PPT', 96)))
