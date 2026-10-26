"""MakeHuman (CC0) people in Blender, moved by CMU motion capture.

The base mesh, shape targets, skeleton and weights come from the MakeHuman repository
(github.com/makehumancommunity/makehuman, data released CC0); walks and runs come from the
CMU motion capture library (free to use). Fetch them with fetch() into MH_DATA.

    person(spec)        -> (armature, [meshes])  a dressed, rigged person
    retarget(arm, bvh)  -> frames                 drives the armature from a .bvh clip
"""
import json
import math
import os
import random
import urllib.request

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

MH_DATA = os.environ.get('MH_DATA', '/tmp/claude-0/mh')
RAW = 'https://raw.githubusercontent.com/makehumancommunity/makehuman/master/makehuman/data/'
CMU = 'https://raw.githubusercontent.com/una-dinosauria/cmu-mocap/master/data/'

RACES = ('caucasian', 'african', 'asian')
TARGETS = ['macrodetails/%s-%s-%s.target' % (r, g, a) for r in RACES for g in ('male', 'female') for a in ('child', 'young')] + [
    'macrodetails/universal-male-young-averagemuscle-minweight.target',
    'macrodetails/universal-male-young-maxmuscle-averageweight.target',
]


def fetch(clips=('07/07_01',)):
    os.makedirs(os.path.join(MH_DATA, 'targets'), exist_ok=True)
    for f in ['3dobjs/base.obj', 'rigs/default.mhskel', 'rigs/default_weights.mhw']:
        dst = os.path.join(MH_DATA, os.path.basename(f))
        if not os.path.exists(dst): urllib.request.urlretrieve(RAW + f, dst)
    for t in TARGETS:
        dst = os.path.join(MH_DATA, 'targets', t.replace('/', '_'))
        if not os.path.exists(dst): urllib.request.urlretrieve(RAW + 'targets/' + t, dst)
    for c in clips:
        dst = os.path.join(MH_DATA, os.path.basename(c) + '.bvh')
        if not os.path.exists(dst): urllib.request.urlretrieve(CMU + '0' + c + '.bvh', dst)


# ------------------------------------------------------------------ the base mesh

def load_obj():
    vs, vts, faces = [], [], []
    group = None
    for line in open(os.path.join(MH_DATA, 'base.obj')):
        if line.startswith('v '):
            x, y, z = map(float, line.split()[1:4])
            vs.append((x, y, z))
        elif line.startswith('vt '):
            vts.append(tuple(map(float, line.split()[1:3])))
        elif line.startswith('g '):
            group = line.split()[1]
        elif line.startswith('f '):
            idx = [p.split('/') for p in line.split()[1:]]
            faces.append(([int(p[0]) - 1 for p in idx], [int(p[1]) - 1 for p in idx if len(p) > 1 and p[1]], group))
    return vs, vts, faces


def load_target(name):
    d = {}
    for line in open(os.path.join(MH_DATA, 'targets', name.replace('/', '_'))):
        if line.startswith('#') or not line.strip(): continue
        p = line.split()
        d[int(p[0])] = (float(p[1]), float(p[2]), float(p[3]))
    return d


def shaped(vs, spec):
    """Blends the macro targets the way MakeHuman's age/gender/muscle/weight sliders do."""
    age = spec.get('age', 25)
    young = min(max((age - 11) / 14.0, 0.0), 1.0) ** 0.45    # a 16-year-old is most of the way to grown
    male = spec.get('male', 1.0)
    race = spec.get('race', {'caucasian': 1.0})
    w = {}
    for r, rw in race.items():
        for g, gw in (('male', male), ('female', 1 - male)):
            w['macrodetails/%s-%s-young.target' % (r, g)] = rw * gw * young
            w['macrodetails/%s-%s-child.target' % (r, g)] = rw * gw * (1 - young)
    w['macrodetails/universal-male-young-averagemuscle-minweight.target'] = spec.get('lean', 0.0) * male
    w['macrodetails/universal-male-young-maxmuscle-averageweight.target'] = spec.get('muscle', 0.0) * male
    out = [list(v) for v in vs]
    for t, wt in w.items():
        if wt <= 0: continue
        for i, d in load_target(t).items():
            out[i][0] += d[0] * wt
            out[i][1] += d[1] * wt
            out[i][2] += d[2] * wt
    # MakeHuman is Y-up in decimetres, facing +Z: Blender is Z-up in metres; the person faces -Y
    pts = [Vector((x * 0.1, -z * 0.1, y * 0.1)) for x, y, z in out]
    # stand on the ground: the soles at z = 0 (the lowest body vertex, helpers ignored)
    floor = min(pts[i].z for i in range(min(len(pts), 13380)))
    return [p - Vector((0, 0, floor)) for p in pts]


# ------------------------------------------------------------------ materials

def _nodes(m):
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    p = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(p.outputs[0], out.inputs[0])
    return nt, p


def lin(hexs):
    h = hexs.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (1.0,)


def skin_mat(name, col, lip_col='#a8645a', brow_col='#3a2618'):
    m = bpy.data.materials.new(name)
    nt, p = _nodes(m)
    p.inputs['Roughness'].default_value = 0.5
    p.inputs['Subsurface Weight'].default_value = 0.12
    # lips and brows are painted from per-vertex weights the builder writes (attributes 'lips', 'brow')
    base = lin(col)
    c = base
    for attr, tint in (('lips', lip_col), ('brow', brow_col)):
        at = nt.nodes.new('ShaderNodeAttribute')
        at.attribute_name = attr
        mx = nt.nodes.new('ShaderNodeMix')
        mx.data_type = 'RGBA'
        if isinstance(c, tuple): mx.inputs['A'].default_value = c
        else: nt.links.new(c, mx.inputs['A'])
        mx.inputs['B'].default_value = lin(tint)
        nt.links.new(at.outputs['Fac'], mx.inputs['Factor'])
        c = mx.outputs['Result']
    nt.links.new(c, p.inputs['Base Color'])
    p.inputs['Subsurface Radius'].default_value = (1.0, 0.35, 0.2)
    p.inputs['Subsurface Scale'].default_value = 0.01
    # a little blush and unevenness
    nz = nt.nodes.new('ShaderNodeTexNoise')
    nz.inputs['Scale'].default_value = 40
    bp = nt.nodes.new('ShaderNodeBump')
    bp.inputs['Strength'].default_value = 0.04
    nt.links.new(nz.outputs['Fac'], bp.inputs['Height'])
    nt.links.new(bp.outputs['Normal'], p.inputs['Normal'])
    return m


def cloth_mat(name, col, rough=0.85, weave=120.0):
    m = bpy.data.materials.new(name)
    nt, p = _nodes(m)
    p.inputs['Base Color'].default_value = lin(col)
    p.inputs['Roughness'].default_value = rough
    p.inputs['Sheen Weight'].default_value = 0.3
    tc = nt.nodes.new('ShaderNodeTexCoord')
    wv = nt.nodes.new('ShaderNodeTexWave')
    wv.inputs['Scale'].default_value = weave
    nt.links.new(tc.outputs['Object'], wv.inputs['Vector'])
    nz = nt.nodes.new('ShaderNodeTexNoise')
    nz.inputs['Scale'].default_value = 6
    nz.inputs['Detail'].default_value = 6
    nt.links.new(tc.outputs['Object'], nz.inputs['Vector'])
    bp = nt.nodes.new('ShaderNodeBump')
    bp.inputs['Strength'].default_value = 0.08
    nt.links.new(wv.outputs['Fac'], bp.inputs['Height'])
    nt.links.new(bp.outputs['Normal'], p.inputs['Normal'])
    # creases: darken the colour a touch where the noise is low
    mx = nt.nodes.new('ShaderNodeMix')
    mx.data_type = 'RGBA'
    mx.blend_type = 'MULTIPLY'
    mx.inputs['Factor'].default_value = 0.25
    mx.inputs['A'].default_value = lin(col)
    nt.links.new(nz.outputs['Color'], mx.inputs['B'])
    nt.links.new(mx.outputs['Result'], p.inputs['Base Color'])
    return m


def hair_mat(name, col):
    m = bpy.data.materials.new(name)
    nt, p = _nodes(m)
    p.inputs['Roughness'].default_value = 0.45
    p.inputs['Coat Weight'].default_value = 0.2
    tc = nt.nodes.new('ShaderNodeTexCoord')
    mp = nt.nodes.new('ShaderNodeMapping')
    mp.inputs['Scale'].default_value = (60, 60, 6)       # strands run up and down the head
    nt.links.new(tc.outputs['Object'], mp.inputs['Vector'])
    nz = nt.nodes.new('ShaderNodeTexNoise')
    nz.inputs['Detail'].default_value = 8
    nt.links.new(mp.outputs[0], nz.inputs['Vector'])
    cr = nt.nodes.new('ShaderNodeValToRGB')
    base = lin(col)
    cr.color_ramp.elements[0].color = tuple(c * 0.45 for c in base[:3]) + (1,)
    cr.color_ramp.elements[1].color = tuple(min(1, c * 1.5) for c in base[:3]) + (1,)
    nt.links.new(nz.outputs['Fac'], cr.inputs[0])
    nt.links.new(cr.outputs[0], p.inputs['Base Color'])
    bp = nt.nodes.new('ShaderNodeBump')
    bp.inputs['Strength'].default_value = 0.3
    nt.links.new(nz.outputs['Fac'], bp.inputs['Height'])
    nt.links.new(bp.outputs['Normal'], p.inputs['Normal'])
    return m


EYE_R = 0.0118


def eye_mat(iris):
    m = bpy.data.materials.new('eye')
    nt, p = _nodes(m)
    p.inputs['Roughness'].default_value = 0.05
    p.inputs['Coat Weight'].default_value = 1.0
    tc = nt.nodes.new('ShaderNodeTexCoord')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    nt.links.new(tc.outputs['Object'], sep.inputs[0])
    # the eye looks down its -Y: iris where the front is, pupil in the middle
    mr = nt.nodes.new('ShaderNodeMapRange')
    mr.inputs['From Min'].default_value = -EYE_R
    mr.inputs['From Max'].default_value = -0.72 * EYE_R
    nt.links.new(sep.outputs['Y'], mr.inputs['Value'])
    cr = nt.nodes.new('ShaderNodeValToRGB')
    e = cr.color_ramp.elements
    e[0].position, e[0].color = 0.40, (0.86, 0.84, 0.80, 1)
    e[1].position, e[1].color = 0.46, lin(iris)
    e.new(0.80).color = tuple(c * 0.6 for c in lin(iris)[:3]) + (1,)
    e.new(0.84).color = (0.01, 0.01, 0.01, 1)
    # reversed: front (y=-1) gives 1
    inv = nt.nodes.new('ShaderNodeMath')
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    nt.links.new(mr.outputs[0], inv.inputs[1])
    nt.links.new(inv.outputs[0], cr.inputs[0])
    nt.links.new(cr.outputs[0], p.inputs['Base Color'])
    return m


# ------------------------------------------------------------------ building a person

# which bones count as which piece of clothing (by the bone a vertex is most weighted to)
TOP = ('spine', 'breast', 'clavicle', 'shoulder', 'upperarm', 'pectoral', 'neck01')
BOTTOM = ('pelvis', 'upperleg', 'lowerleg', 'root')
FEET = ('foot', 'toe')

PRESETS = {
    'hanson': dict(age=16, male=1.0, lean=0.5, muscle=0.35, skin='#c99474', hair='#5a3a22', iris='#5a4632',
                   top='#7a5a3c', bottom='#3a3f4a', shoes='#3a2a1e', sleeves='short'),
}


def person(name, spec, coll=None):
    coll = coll or bpy.context.scene.collection
    vs0, vts, faces = load_obj()
    vs = shaped(vs0, spec)
    skel = json.load(open(os.path.join(MH_DATA, 'default.mhskel')))
    joints = {k: sum((vs[i] for i in idx), Vector()) / len(idx) for k, idx in skel['joints'].items()}

    # armature
    ad = bpy.data.armatures.new(name + '_rig')
    arm = bpy.data.objects.new(name, ad)
    coll.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    bones = skel['bones']
    for bn, b in bones.items():
        eb = ad.edit_bones.new(bn)
        eb.head = joints[b['head']]
        eb.tail = joints[b['tail']]
        if (eb.tail - eb.head).length < 1e-4: eb.tail = eb.head + Vector((0, 0, 0.01))
    for bn, b in bones.items():
        if b['parent']: ad.edit_bones[bn].parent = ad.edit_bones[b['parent']]
    bpy.ops.object.mode_set(mode='OBJECT')

    # weights by original vertex index
    wts = json.load(open(os.path.join(MH_DATA, 'default_weights.mhw')))['weights']
    best = {}
    for bn, lst in wts.items():
        for i, w in lst:
            if i not in best or w > best[i][1]: best[i] = (bn, w)

    body_faces = [f for f in faces if f[2] == 'body']
    used = sorted({i for f in body_faces for i in f[0]})
    remap = {o: n for n, o in enumerate(used)}

    def piece(pname, fsel, offset, mat, extra=None, soften=0, wrap=None, loose=None):
        me = bpy.data.meshes.new(pname)
        vsel = sorted({i for f in fsel for i in f[0]})
        rm = {o: n for n, o in enumerate(vsel)}
        me.from_pydata([vs[i] for i in vsel], [], [[rm[i] for i in f[0]] for f in fsel])
        me.materials.append(mat)
        for p in me.polygons: p.use_smooth = True
        ob = bpy.data.objects.new(pname, me)
        coll.objects.link(ob)
        # vertex groups from the weights file
        for bn, lst in wts.items():
            g = None
            for i, w in lst:
                if i in rm:
                    if g is None: g = ob.vertex_groups.new(name=bn)
                    g.add([rm[i]], w, 'REPLACE')
        if soften:
            bm = bmesh.new()
            bm.from_mesh(me)
            for _ in range(soften):
                bmesh.ops.smooth_vert(bm, verts=[v for v in bm.verts if not v.is_boundary], factor=0.6,
                                      use_axis_x=True, use_axis_y=True, use_axis_z=True)
            bm.to_mesh(me)
            bm.free()
        if wrap is not None:
            # a smoothed shell kept just outside the body: no bumps poke through, nothing pokes out
            sw = ob.modifiers.new('wrap', 'SHRINKWRAP')
            sw.target = wrap
            sw.wrap_method = 'NEAREST_SURFACEPOINT'
            sw.wrap_mode = 'OUTSIDE_SURFACE'
            sw.offset = offset
            bpy.context.view_layer.update()
            dg = bpy.context.evaluated_depsgraph_get()
            baked = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
            ob.modifiers.remove(sw)
            old = ob.data
            ob.data = baked
            bpy.data.meshes.remove(old)
            me = baked
            if loose:
                # cloth hangs off the body: push it out more where it falls free (hems, trouser legs)
                me.update()
                for v in me.vertices:
                    v.co += v.normal * loose(v.co)
            sol = ob.modifiers.new('thick', 'SOLIDIFY')
            sol.thickness = 0.004
            sol.offset = 1.0
        elif offset:
            me.update()
            for v in me.vertices: v.co += v.normal * offset
        if extra: extra(ob, me, rm)
        sub = ob.modifiers.new('sub', 'SUBSURF')
        sub.levels = sub.render_levels = 1
        ob.modifiers.move(len(ob.modifiers) - 1, 0)
        ob['mh_piece'] = True
        am = ob.modifiers.new('rig', 'ARMATURE')
        am.object = arm
        ob.parent = arm
        return ob

    def dom(f):
        names = [best.get(i, ('', 0))[0] for i in f[0]]
        return max(set(names), key=names.count)

    skin = skin_mat(name + '_skin', spec['skin'], spec.get('lips', '#a8645a'), spec.get('brow', '#3a2618'))
    mouth = sum((joints[bones[b]['head']] for b in bones if b.startswith('oris')), Vector()) / len([b for b in bones if b.startswith('oris')])
    eyes = [joints[bones['eye.' + s]['head']] for s in ('L', 'R')]

    def face_paint(ob, me, rm):
        lips = me.attributes.new('lips', 'FLOAT', 'POINT')
        brow = me.attributes.new('brow', 'FLOAT', 'POINT')
        for o, n in rm.items():
            v = vs[o]
            l = 0.0
            if best.get(o, ('', 0))[0].startswith('oris'):
                d = Vector(((v.x - mouth.x) / 0.026, (v.y - mouth.y) / 0.03, (v.z - mouth.z) / 0.011))
                l = max(0.0, 1.0 - d.length) ** 0.5
            b = 0.0
            for e in eyes:
                dx, dz = (v.x - e.x) / 0.024, (v.z - (e.z + 0.023 - 0.004 * abs(v.x - e.x) / 0.024)) / 0.0045
                if v.y < e.y + 0.01:
                    b = max(b, max(0.0, 1.0 - math.hypot(dx * 0.9, dz)) ** 0.4)
            lips.data[n].value = min(1.0, l * 0.8)
            brow.data[n].value = min(1.0, b * 0.9)
    meshes = [piece(name + '_body', body_faces, 0.0, skin, face_paint)]

    # clothes: the body's own surface pushed out a little, so they always fit and bend with it
    def is_top(f):
        d = dom(f)
        if any(d.startswith(t) for t in TOP): return True
        if d.startswith('lowerarm') and spec.get('sleeves') == 'long': return True
        return False
    waist = joints[bones['root']['head']].z + 0.09
    chest = joints[bones['spine01']['head']].z
    knee = joints[bones['lowerleg01.L']['head']].z

    def cz(f): return sum(vs[i].z for i in f[0]) / len(f[0])
    torso = [f for f in body_faces if is_top(f) or any(dom(f).startswith(t) for t in BOTTOM)]
    top = [f for f in torso if (is_top(f) or dom(f).startswith(('pelvis', 'root'))) and cz(f) > waist]
    bottom = [f for f in torso if not (is_top(f) and cz(f) > waist) and cz(f) <= waist + 0.02 and not dom(f).startswith(('upperarm', 'lowerarm', 'shoulder', 'clavicle'))]
    feet = [f for f in body_faces if any(dom(f).startswith(t) for t in FEET)]
    meshes.append(piece(name + '_top', top, 0.012, cloth_mat(name + '_top', spec['top']), soften=45, wrap=meshes[0],
                        loose=lambda co: 0.002 + 0.012 * max(0.0, min(1.0, (chest - co.z) / (chest - waist)))))
    meshes.append(piece(name + '_bottom', bottom, 0.009, cloth_mat(name + '_bottom', spec['bottom'], weave=200), soften=20, wrap=meshes[0],
                        loose=lambda co: 0.003 + 0.03 * max(0.0, min(1.0, (knee - co.z) / knee)) + 0.012 * max(0.0, min(1.0, (waist - 0.12 - co.z) / 0.2))))
    meshes.append(piece(name + '_shoes', feet, 0.01, cloth_mat(name + '_shoes', spec['shoes'], rough=0.5, weave=20), soften=30, wrap=meshes[0]))

    # hair: the scalp pushed out into a thick, slightly messy mass
    head_c = joints[bones['head']['head']]
    top_z = max(v.z for v in vs)
    brow_z = joints[bones['head']['head']].z + (top_z - head_c.z) * 0.42

    def scalp(f):
        if dom(f) not in ('head',): return False
        c = sum((vs[i] for i in f[0]), Vector()) / len(f[0])
        front = c.y < head_c.y - 0.055          # face side
        if front: return c.z > brow_z + 0.035
        return c.z > head_c.z - 0.01 if c.y < head_c.y + 0.02 else c.z > head_c.z - 0.05

    hair = [f for f in body_faces if scalp(f)]
    meshes.append(strands(name + '_hair', arm, coll, vs, hair, head_c, top_z, spec))

    # eyes
    em = eye_mat(spec['iris'])
    for side in ('L', 'R'):
        b = bones['eye.' + side]
        c = joints[b['head']]
        bpy.ops.mesh.primitive_uv_sphere_add(radius=EYE_R, location=c, segments=24, ring_count=16)
        eye = bpy.context.active_object
        eye.name = name + '_eye' + side
        eye.data.materials.append(em)
        for p in eye.data.polygons: p.use_smooth = True
        if eye.users_collection[0] != coll:
            for c2 in eye.users_collection: c2.objects.unlink(eye)
            coll.objects.link(eye)
        eye.parent = arm
        eye.parent_type = 'BONE'
        eye.parent_bone = 'head'
        eye.matrix_world = Matrix.Translation(c)
        meshes.append(eye)
    return arm, meshes


def strands(name, arm, coll, vs, faces, head_c, top_z, spec, n=26000, seed=4):
    """Real hair: thousands of curves grown from the scalp and combed over the head."""
    rs = random.Random(seed)
    hc = Vector((0.0, head_c.y + 0.012, head_c.z + (top_z - head_c.z) * 0.42))
    # roots spread over the scalp by area
    tris = []
    for f in faces:
        idx = f[0]
        for k in range(1, len(idx) - 1):
            a, b, c = vs[idx[0]], vs[idx[k]], vs[idx[k + 1]]
            ar = (b - a).cross(c - a).length / 2
            if ar > 0: tris.append((a, b, c, ar))
    tot = sum(t[3] for t in tris)
    cum, acc = [], 0.0
    for t in tris:
        acc += t[3] / tot
        cum.append(acc)
    import bisect
    whorl = hc + Vector((0.0, 0.05, 0.075))
    style = spec.get('hairstyle', 'short')
    base_len = {'short': 0.055, 'medium': 0.09, 'long': 0.2}.get(style, 0.055)
    # clumps: neighbouring strands share a twist and a length, like real locks
    clumps = [Vector((rs.uniform(-0.1, 0.1), rs.uniform(-0.12, 0.12), rs.uniform(-0.1, 0.12))) for _ in range(500)]
    clump_rot = [rs.uniform(-0.35, 0.35) for _ in clumps]
    clump_len = [rs.uniform(0.75, 1.25) for _ in clumps]
    SEG = 7
    paths = []
    for i in range(n):
        t = tris[min(bisect.bisect(cum, rs.random()), len(tris) - 1)]
        u, v = rs.random(), rs.random()
        if u + v > 1: u, v = 1 - u, 1 - v
        a, b, c, _ = t
        root = a + (b - a) * u + (c - a) * v
        nrm = (b - a).cross(c - a).normalized()
        if nrm.dot(root - hc) < 0: nrm = -nrm
        rel = root - hc
        ci = min(range(len(clumps)), key=lambda k: (clumps[k] - rel).length_squared) if i % 1 == 0 else 0
        front = root.y < hc.y - 0.045
        if front and style == 'short':
            d = Vector((0.55, -0.35, -0.75))           # a fringe swept to one side
        else:
            d = root - whorl
        d = (d - nrm * d.dot(nrm)).normalized()
        d = Quaternion(nrm, clump_rot[ci]) @ d
        L = base_len * clump_len[ci] * rs.uniform(0.85, 1.1) * (0.8 if root.z < head_c.z + 0.03 else 1.0)
        r0 = rel.length + 0.004
        pts = [root - nrm * 0.002]
        p = root + nrm * 0.006
        pts.append(p.copy())
        for k in range(SEG - 2):
            step = L / (SEG - 2)
            d = (d + Vector((0, 0, -0.18)) + Vector((rs.uniform(-1, 1), rs.uniform(-1, 1), rs.uniform(-1, 1))) * 0.08).normalized()
            p = p + d * step
            R = r0 + 0.006 + 0.0025 * k
            q = p - hc
            if q.length < R: p = hc + q.normalized() * R
            # re-tangent so it keeps following the head
            q = (p - hc).normalized()
            d = (d - q * max(0.0, d.dot(q)) * 0.6).normalized()
            pts.append(p.copy())
        paths.append(pts)
    cv = bpy.data.hair_curves.new(name)
    cv.add_curves([SEG] * len(paths))
    flat = [c for pts in paths for p in pts for c in p]
    cv.points.foreach_set('position', flat)
    rad = cv.attributes.get('radius') or cv.attributes.new('radius', 'FLOAT', 'POINT')
    rad.data.foreach_set('value', [0.00028 * (1 - 0.7 * k / (SEG - 1)) for _ in paths for k in range(SEG)])
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for nd in list(nt.nodes): nt.nodes.remove(nd)
    o = nt.nodes.new('ShaderNodeOutputMaterial')
    hb = nt.nodes.new('ShaderNodeBsdfHairPrincipled')
    hb.parametrization = 'MELANIN'
    hb.inputs['Melanin'].default_value = spec.get('melanin', 0.75)
    hb.inputs['Melanin Redness'].default_value = spec.get('redness', 0.45)
    hb.inputs['Roughness'].default_value = 0.35
    hb.inputs['Radial Roughness'].default_value = 0.4
    hb.inputs['Random Roughness'].default_value = 0.3
    nt.links.new(hb.outputs[0], o.inputs[0])
    cv.materials.append(m)
    ob = bpy.data.objects.new(name, cv)
    coll.objects.link(ob)
    ob.parent = arm
    ob.parent_type = 'BONE'
    ob.parent_bone = 'head'
    bpy.context.view_layer.update()
    ob.matrix_world = Matrix.Identity(4)
    return ob


# ------------------------------------------------------------------ motion capture

# CMU joint -> MakeHuman bones that follow it
MAP = {
    'LeftUpLeg': ['upperleg01.L', 'upperleg02.L'], 'LeftLeg': ['lowerleg01.L', 'lowerleg02.L'], 'LeftFoot': ['foot.L'],
    'RightUpLeg': ['upperleg01.R', 'upperleg02.R'], 'RightLeg': ['lowerleg01.R', 'lowerleg02.R'], 'RightFoot': ['foot.R'],
    'LowerBack': ['spine05', 'spine04'], 'Spine': ['spine03', 'spine02'], 'Spine1': ['spine01'],
    'Neck': ['neck01'], 'Neck1': ['neck02', 'neck03'], 'Head': ['head'],
    'LeftArm': ['upperarm01.L', 'upperarm02.L'],
    'LeftForeArm': ['lowerarm01.L', 'lowerarm02.L'],
    'RightArm': ['upperarm01.R', 'upperarm02.R'],
    'RightForeArm': ['lowerarm01.R', 'lowerarm02.R'],
}


def load_bvh(path):
    import addon_utils
    addon_utils.enable('io_anim_bvh', default_set=True)
    before = set(bpy.data.objects)
    bpy.ops.import_anim.bvh(filepath=path, global_scale=0.056444, update_scene_fps=False, update_scene_duration=False)
    src = [o for o in bpy.data.objects if o not in before][0]
    # face it the same way as the person (-Y): work out which way the actor faces in the rest pose
    bpy.context.view_layer.update()
    rl = src.matrix_world @ src.data.bones['LeftUpLeg'].head_local
    rr = src.matrix_world @ src.data.bones['RightUpLeg'].head_local
    left = (rl - rr)
    left.z = 0
    fwd = left.normalized().cross(Vector((0, 0, 1)))
    # if the actor walks somewhere, face the way they travel instead
    fr = src.animation_data.action.frame_range
    sc = bpy.context.scene
    sc.frame_set(int(fr[0]))
    a = src.matrix_world @ src.pose.bones['Hips'].head
    sc.frame_set(int(fr[1]))
    b = src.matrix_world @ src.pose.bones['Hips'].head
    trav = b - a
    trav.z = 0
    if trav.length > 1.0: fwd = trav.normalized()
    ang = math.atan2(fwd.y, fwd.x) - math.atan2(-1, 0)
    src.rotation_euler.z -= ang
    bpy.context.view_layer.update()
    return src


def retarget(arm, src, frame, in_place=True):
    """Poses `arm` like the mocap armature `src` at `frame` (rotations copied in world space)."""
    sc = bpy.context.scene
    sc.frame_set(frame)
    mw_s, mw_t = src.matrix_world, arm.matrix_world

    def wrest(ob, b):
        return (ob.matrix_world @ b.matrix_local).to_quaternion()

    # root: copy the hips' change of rotation, and its height (scaled to the person)
    hs = src.pose.bones['Hips']
    rest_hip = (mw_s @ src.data.bones['Hips'].matrix_local)
    pose_hip = (mw_s @ hs.matrix)
    dq = pose_hip.to_quaternion() @ rest_hip.to_quaternion().inverted()
    tb = arm.pose.bones['root']
    t_rest = mw_t @ arm.data.bones['root'].matrix_local
    # scale the hips' height by leg length (the clip's rest pose has its hips at the origin)
    sl = src.data.bones['LeftUpLeg'].length + src.data.bones['LeftLeg'].length
    tl = sum(arm.data.bones[n].length for n in ('upperleg01.L', 'upperleg02.L', 'lowerleg01.L', 'lowerleg02.L'))
    scale = tl / sl
    loc = pose_hip.translation * scale
    if in_place:
        loc = Vector((t_rest.translation.x, t_rest.translation.y, loc.z))
    tw = Matrix.LocRotScale(loc, dq @ t_rest.to_quaternion(), None)
    tb.matrix = mw_t.inverted() @ tw
    bpy.context.view_layer.update()

    order = [b.name for b in arm.data.bones]   # parents come before children
    want = {}
    for sname, tnames in MAP.items():
        if sname not in src.pose.bones: continue
        sb = src.data.bones[sname]
        sp = src.pose.bones[sname]
        s_rest_q = wrest(src, sb)
        s_pose_q = (mw_s @ sp.matrix).to_quaternion()
        d = s_pose_q @ s_rest_q.inverted()
        s_dir = (mw_s.to_3x3() @ (sb.tail_local - sb.head_local)).normalized()
        for tn in tnames:
            b = arm.data.bones[tn]
            t_rest_q = wrest(arm, b)
            t_dir = (mw_t.to_3x3() @ (b.tail_local - b.head_local)).normalized()
            align = t_dir.rotation_difference(s_dir)
            want[tn] = d @ align @ t_rest_q
    for tn in order:
        if tn not in want: continue
        pb = arm.pose.bones[tn]
        head = (mw_t @ pb.matrix).translation
        pb.matrix = mw_t.inverted() @ Matrix.LocRotScale(head, want[tn], None)
        bpy.context.view_layer.update()
