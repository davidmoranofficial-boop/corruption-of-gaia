"""Built things, modelled in code: Yenson's shack and the wreck on the beach.

Every model is made in its own local space (x east, y north, z up, metres) and placed with its front-left
corner on a tile. Footprints are deeper than they look: the ground is stretched north-south by 1/cos(tilt)
so tiles stay square on screen, so DEPTH(rows) gives the real depth of a number of tile rows.
"""
import math
import random

import bpy
import bmesh
from mathutils import Vector, Matrix, noise

from common import Nodes, principled, output, hexc, ramp, link, CA


def DEPTH(rows):
    return rows / CA


# ------------------------------------------------------------------ materials

def _mat(name, build):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name)
    build(Nodes(m))
    return m


def wood(name, dark, mid, light, grain_axis='X', scale=3.0, rough=0.8, bump=0.4):
    def b(n):
        tc = n.new('ShaderNodeTexCoord')
        oi = n.new('ShaderNodeObjectInfo')
        mp = n.new('ShaderNodeMapping', vector_type='POINT')
        n.link(tc, 'Object', mp, 'Vector')
        n.inp(mp, 'Scale').default_value = (1, 6, 6) if grain_axis == 'X' else ((6, 1, 6) if grain_axis == 'Y' else (6, 6, 1))
        nz = n.new('ShaderNodeTexNoise', in_Scale=scale, in_Detail=12.0, in_Roughness=0.6)
        n.link(mp, 'Vector', nz, 'Vector')
        nz2 = n.new('ShaderNodeTexNoise', in_Scale=0.8, in_Detail=2.0)
        n.link(tc, 'Object', nz2, 'Vector')
        add = n.new('ShaderNodeMath', operation='MULTIPLY_ADD')
        n.link(nz, 'Fac', add, 0)
        add.inputs[1].default_value = 0.7
        n.link(nz2, 'Fac', add, 2)
        add2 = n.new('ShaderNodeMath', operation='MULTIPLY_ADD', use_clamp=True)
        n.link(add, 0, add2, 0)
        add2.inputs[1].default_value = 0.8
        n.link(oi, 'Random', add2, 2)
        c = ramp(n, [(0.0, dark), (0.5, mid), (1.0, light)], add2, 0)
        p = principled(n, rough=rough)
        n.link(c, 0, p, 'Base Color')
        bp = n.new('ShaderNodeBump', in_Strength=bump)
        n.link(nz, 'Fac', bp, 'Height')
        n.link(bp, 'Normal', p, 'Normal')
        output(n, p)
    return _mat(name, b)


def plain(name, colour, rough=0.7, noise_scale=8.0, var=0.25, metal=0.0, bump=0.25):
    def b(n):
        tc = n.new('ShaderNodeTexCoord')
        nz = n.new('ShaderNodeTexNoise', in_Scale=noise_scale, in_Detail=6.0)
        n.link(tc, 'Object', nz, 'Vector')
        c = hexc(colour)
        dark = tuple(max(0, x * (1 - var)) for x in c)
        light = tuple(min(1, x * (1 + var)) for x in c)
        r = n.new('ShaderNodeValToRGB')
        r.color_ramp.elements[0].color = (*dark, 1)
        r.color_ramp.elements[1].color = (*light, 1)
        n.link(nz, 'Fac', r, 'Fac')
        p = principled(n, rough=rough, Metallic=metal)
        n.link(r, 0, p, 'Base Color')
        bp = n.new('ShaderNodeBump', in_Strength=bump)
        n.link(nz, 'Fac', bp, 'Height')
        n.link(bp, 'Normal', p, 'Normal')
        output(n, p)
    return _mat(name, b)


def stone_mat():
    def b(n):
        tc = n.new('ShaderNodeTexCoord')
        vor = n.new('ShaderNodeTexVoronoi', in_Scale=4.5)
        n.link(tc, 'Object', vor, 'Vector')
        nz = n.new('ShaderNodeTexNoise', in_Scale=12.0, in_Detail=8.0)
        n.link(tc, 'Object', nz, 'Vector')
        c = ramp(n, [(0.0, '#4a463e'), (0.5, '#6f6a5e'), (1.0, '#8f897a')], vor, 'Color')
        mortar = n.new('ShaderNodeTexVoronoi', feature='DISTANCE_TO_EDGE', in_Scale=4.5)
        n.link(tc, 'Object', mortar, 'Vector')
        mm = n.new('ShaderNodeMapRange', in_From_Min=0.0, in_From_Max=0.06)
        n.link(mortar, 'Distance', mm, 'Value')
        mx = n.new('ShaderNodeMix', data_type='RGBA')
        n.inp(mx, 'A').default_value = (*hexc('#2f2b25'), 1)
        n.link(c, 0, mx, 'B')
        n.link(mm, 'Result', mx, 'Factor')
        p = principled(n, rough=0.85)
        n.link(mx, 'Result', p, 'Base Color')
        bp = n.new('ShaderNodeBump', in_Strength=0.8)
        n.link(mm, 'Result', bp, 'Height')
        n.link(bp, 'Normal', p, 'Normal')
        output(n, p)
    return _mat('stone', b)


def roof_mat():
    """Old wooden shingles, grey with weather, moss in the lower rows."""
    def b(n):
        tc = n.new('ShaderNodeTexCoord')
        oi = n.new('ShaderNodeObjectInfo')
        nz = n.new('ShaderNodeTexNoise', in_Scale=3.0, in_Detail=6.0)
        n.link(tc, 'Object', nz, 'Vector')
        attr = n.new('ShaderNodeAttribute', attribute_name='shade')
        add = n.new('ShaderNodeMath', operation='MULTIPLY_ADD', use_clamp=True)
        n.link(attr, 'Fac', add, 0)
        add.inputs[1].default_value = 0.6
        n.link(nz, 'Fac', add, 2)
        c = ramp(n, [(0.0, '#2e2720'), (0.4, '#4a3f33'), (0.7, '#625545'), (1.0, '#766853')], add, 0)
        moss = n.new('ShaderNodeTexNoise', in_Scale=1.8, in_Detail=5.0)
        n.link(tc, 'Object', moss, 'Vector')
        mm = n.new('ShaderNodeMapRange', in_From_Min=0.55, in_From_Max=0.68)
        n.link(moss, 'Fac', mm, 'Value')
        mx = n.new('ShaderNodeMix', data_type='RGBA')
        n.link(c, 0, mx, 'A')
        n.inp(mx, 'B').default_value = (*hexc('#4d5e2a'), 1)
        n.link(mm, 'Result', mx, 'Factor')
        p = principled(n, rough=0.85)
        n.link(mx, 'Result', p, 'Base Color')
        bp = n.new('ShaderNodeBump', in_Strength=0.5)
        n.link(nz, 'Fac', bp, 'Height')
        n.link(bp, 'Normal', p, 'Normal')
        output(n, p)
    return _mat('shingles', b)


def glow_mat(name, colour, strength):
    def b(n):
        e = n.new('ShaderNodeEmission', in_Strength=strength)
        n.inp(e, 'Color').default_value = (*hexc(colour), 1)
        output(n, e)
    return _mat(name, b)


def cloth_mat():
    def b(n):
        tc = n.new('ShaderNodeTexCoord')
        nz = n.new('ShaderNodeTexNoise', in_Scale=6.0, in_Detail=8.0)
        n.link(tc, 'Object', nz, 'Vector')
        c = ramp(n, [(0.0, '#7d725c'), (0.6, '#b3a684'), (1.0, '#cbbf9c')], nz, 'Fac')
        p = principled(n, rough=0.95)
        n.link(c, 0, p, 'Base Color')
        tr = n.new('ShaderNodeBsdfTranslucent')
        n.link(c, 0, tr, 'Color')
        mx = n.new('ShaderNodeMixShader', in_Fac=0.3)
        n.link(p, 0, mx, 1)
        n.link(tr, 0, mx, 2)
        output(n, mx)
    return _mat('sailcloth', b)


# ------------------------------------------------------------------ geometry helpers

def cyl(bm, a, b, r, sides=10, mat=0, r2=None, wobble=0.0, seed=0, segs=1):
    """A cylinder (log) from a to b."""
    a, b = Vector(a), Vector(b)
    d = (b - a)
    L = d.length
    d.normalize()
    u = d.orthogonal().normalized()
    w = d.cross(u).normalized()
    r2 = r if r2 is None else r2
    rings = []
    for s in range(segs + 1):
        t = s / segs
        c = a + d * L * t
        rr = r + (r2 - r) * t
        ring = []
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            k2 = 1 + wobble * noise.noise(Vector((t * 3 + seed, k * 0.7, seed)))
            ring.append(bm.verts.new(c + (u * math.cos(ang) + w * math.sin(ang)) * rr * k2))
        rings.append(ring)
    faces = []
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(sides):
            f = bm.faces.new((r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k]))
            f.material_index = mat
            f.smooth = True
            faces.append(f)
    for ring, rev in ((rings[0], True), (rings[-1], False)):
        f = bm.faces.new(list(reversed(ring)) if rev else ring)
        f.material_index = mat
    return faces


def cube(bm, centre, size, mat=0, rot=None):
    """An axis box (optionally rotated by a Matrix) centred at centre with full size (sx, sy, sz)."""
    sx, sy, sz = size
    c = Vector(centre)
    pts = [Vector((x * sx / 2, y * sy / 2, z * sz / 2)) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    if rot is not None: pts = [rot @ p for p in pts]
    v = [bm.verts.new(c + p) for p in pts]
    idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    out = []
    for f in idx:
        ff = bm.faces.new([v[i] for i in f])
        ff.material_index = mat
        out.append(ff)
    return out


def finish(name, bm, mats, coll):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats: me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    link(ob, coll)
    return ob


# ------------------------------------------------------------------ Yenson's shack

def shack(coll, w_tiles=5, rows=3, door_col=2, seed=4):
    """A fisherman's log shack: stone footing, log walls with crossed corners, a shingle roof, a stone chimney."""
    rnd = random.Random(seed)
    W, D = w_tiles - 0.3, DEPTH(rows) - 0.5
    x0, y0 = 0.15, 0.25          # local origin = south-west corner of the footprint; the building sits inside it
    logm = wood('logs', '#3a2a1c', '#6a5038', '#8a6c4c', 'X', 2.0)
    logy = wood('logs_y', '#3a2a1c', '#6a5038', '#8a6c4c', 'Y', 2.0)
    mats = [logm, logy, stone_mat(), roof_mat(), wood('planks', '#2e241b', '#4b3d2e', '#665341', 'Z', 4.0), glow_mat('window', '#ffb763', 2.5),
            plain('iron', '#2a2622', 0.5, 20, 0.2, 0.6), wood('trim', '#2a2018', '#3d3024', '#4f3f2f', 'X', 6.0)]
    bm = bmesh.new()
    # footing
    cube(bm, (x0 + W / 2, y0 + D / 2, 0.12), (W + 0.25, D + 0.25, 0.42), 2)
    r = 0.13
    wall_h = 2.15
    n_logs = int(wall_h / (2 * r * 0.92))
    door_x = x0 + door_col + 0.5 - 0.15
    for i in range(n_logs):
        z = 0.38 + r + i * 2 * r * 0.92
        ofs = r if i % 2 else 0
        # south (front) and north walls along X, with the door gap in the front
        for yy, front in ((y0, True), (y0 + D, False)):
            if front and z < 0.38 + 1.85:
                cyl(bm, (x0 - 0.3, yy, z), (door_x - 0.48, yy, z), r * rnd.uniform(0.92, 1.06), 9, 0, wobble=0.05, seed=rnd.random() * 9, segs=3)
                cyl(bm, (door_x + 0.48, yy, z), (x0 + W + 0.3, yy, z), r * rnd.uniform(0.92, 1.06), 9, 0, wobble=0.05, seed=rnd.random() * 9, segs=3)
            else:
                cyl(bm, (x0 - 0.3, yy, z), (x0 + W + 0.3, yy, z), r * rnd.uniform(0.92, 1.06), 9, 0, wobble=0.05, seed=rnd.random() * 9, segs=4)
        zz = z + r * 0.92
        for xx in (x0, x0 + W):
            cyl(bm, (xx, y0 - 0.3, zz), (xx, y0 + D + 0.3, zz), r * rnd.uniform(0.92, 1.06), 9, 1, wobble=0.05, seed=rnd.random() * 9, segs=3)
    top = 0.38 + n_logs * 2 * r * 0.92 + r
    # gable ends: logs getting shorter towards the ridge
    ridge = top + 1.35
    for i in range(6):
        z = top + i * 2 * r * 0.92
        t = (z - top) / (ridge - top)
        if t >= 1: break
        half = (D / 2 + 0.2) * (1 - t)
        for xx in (x0, x0 + W):
            cyl(bm, (xx, y0 + D / 2 - half, z), (xx, y0 + D / 2 + half, z), r, 9, 1, wobble=0.05, seed=rnd.random() * 9)
    # door: planks, iron hinges
    for k in range(4):
        cube(bm, (door_x - 0.33 + k * 0.22, y0 - 0.02, 0.38 + 0.92), (0.2, 0.06, 1.84), 4)
    for zz in (0.75, 1.75):
        cube(bm, (door_x, y0 - 0.06, zz), (0.8, 0.03, 0.06), 6)
    cube(bm, (door_x, y0 - 0.02, 0.38 + 1.9), (1.0, 0.18, 0.14), 7)    # lintel
    # windows: warm light from the fire inside, a frame and a cross
    for wx in (x0 + 0.85, x0 + W - 0.85):
        cube(bm, (wx, y0 - 0.0, 1.45), (0.55, 0.1, 0.5), 5)
        cube(bm, (wx, y0 - 0.07, 1.45), (0.66, 0.06, 0.08), 7)
        cube(bm, (wx, y0 - 0.07, 1.45), (0.06, 0.06, 0.6), 7)
        cube(bm, (wx, y0 - 0.07, 1.73), (0.72, 0.1, 0.08), 7)
        cube(bm, (wx, y0 - 0.07, 1.17), (0.72, 0.12, 0.08), 7)
        # shutters, one hanging crooked
        cube(bm, (wx - 0.45, y0 - 0.1, 1.45), (0.3, 0.05, 0.62), 4)
        cube(bm, (wx + 0.47, y0 - 0.12, 1.42), (0.3, 0.05, 0.62), 4, Matrix.Rotation(0.12, 3, 'Y'))
    # roof: two slopes of shingle rows, overhanging
    over = 0.45
    for side in (-1, 1):
        rows_n = 9
        for i in range(rows_n):
            t0, t1 = i / rows_n, (i + 1.25) / rows_n
            yA = y0 + D / 2 + side * (D / 2 + over) * (1 - t0)
            yB = y0 + D / 2 + side * (D / 2 + over) * (1 - min(t1, 1))
            zA = top - 0.12 + (ridge - top + 0.12) * t0 + 0.02 * (rows_n - i)
            zB = top - 0.12 + (ridge - top + 0.12) * min(t1, 1)
            # each row split into shingles of uneven width
            xx = x0 - over
            while xx < x0 + W + over:
                sw = rnd.uniform(0.22, 0.38)
                xe = min(xx + sw, x0 + W + over)
                sag = rnd.uniform(-0.02, 0.02)
                vs = [bm.verts.new((xx, yA, zA + sag)), bm.verts.new((xe - 0.01, yA, zA + sag)),
                      bm.verts.new((xe - 0.01, yB, zB)), bm.verts.new((xx, yB, zB))]
                th = Vector((0, 0, 0.045))
                vt = [bm.verts.new(v.co + th) for v in vs]
                f1 = bm.faces.new(vt if side < 0 else list(reversed(vt)))
                f2 = bm.faces.new((vs[0], vs[1], vt[1], vt[0]) if side < 0 else (vt[0], vt[1], vs[1], vs[0]))
                for f in (f1, f2): f.material_index = 3
                xx = xe
    # ridge beam
    cyl(bm, (x0 - over, y0 + D / 2, ridge + 0.06), (x0 + W + over, y0 + D / 2, ridge + 0.06), 0.11, 8, 0, segs=3)
    # chimney: stacked stones on the east end
    cx, cy = x0 + W - 0.35, y0 + D / 2 + 0.4
    z = 0.3
    while z < ridge + 0.9:
        for k in range(4):
            a = k * math.pi / 2 + rnd.uniform(-0.1, 0.1)
            cube(bm, (cx + math.cos(a) * 0.22, cy + math.sin(a) * 0.22, z), (0.34, 0.34, 0.22), 2, Matrix.Rotation(rnd.uniform(-0.15, 0.15), 3, 'Z'))
        z += 0.2
    ob = finish('shack', bm, mats, coll)
    shade = ob.data.attributes.new('shade', 'FLOAT', 'FACE')
    rs = random.Random(seed + 1)
    shade.data.foreach_set('value', [rs.random() for _ in range(len(ob.data.polygons))])
    return ob, (cx, cy, ridge + 0.95)


def smoke(coll, at):
    """A soft plume from the chimney, drifting off with the wind."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * 2.4 + 1.0, v.co.y * 1.4, v.co.z * 3.0 + 1.5))
    me = bpy.data.meshes.new('smoke')
    bm.to_mesh(me)
    bm.free()

    def b(n):
        tc = n.new('ShaderNodeTexCoord')
        nz = n.new('ShaderNodeTexNoise', in_Scale=1.2, in_Detail=6.0)
        n.link(tc, 'Object', nz, 'Vector')
        sep = n.new('ShaderNodeSeparateXYZ')
        n.link(tc, 'Object', sep, 'Vector')
        # a plume: dense near the chimney top (origin), thinning and widening as it rises and drifts east
        lx = n.new('ShaderNodeMath', operation='MULTIPLY')
        n.link(sep, 'Z', lx, 0)
        lx.inputs[1].default_value = 0.35
        dx = n.new('ShaderNodeMath', operation='SUBTRACT')
        n.link(sep, 'X', dx, 0)
        n.link(lx, 0, dx, 1)
        dist = n.new('ShaderNodeCombineXYZ')
        n.link(dx, 0, dist, 'X')
        n.link(sep, 'Y', dist, 'Y')
        ln = n.new('ShaderNodeVectorMath', operation='LENGTH')
        n.link(dist, 0, ln, 0)
        wid = n.new('ShaderNodeMath', operation='MULTIPLY_ADD')
        n.link(sep, 'Z', wid, 0)
        wid.inputs[1].default_value = 0.16
        wid.inputs[2].default_value = 0.18
        q = n.new('ShaderNodeMath', operation='DIVIDE')
        n.link(ln, 'Value', q, 0)
        n.link(wid, 0, q, 1)
        core = n.new('ShaderNodeMapRange', in_From_Min=1.0, in_From_Max=0.0)
        n.link(q, 0, core, 'Value')
        fade = n.new('ShaderNodeMapRange', in_From_Min=4.0, in_From_Max=0.0)
        n.link(sep, 'Z', fade, 'Value')
        low = n.new('ShaderNodeMapRange', in_From_Min=-0.05, in_From_Max=0.15)
        n.link(sep, 'Z', low, 'Value')
        m1 = n.new('ShaderNodeMath', operation='MULTIPLY')
        n.link(core, 'Result', m1, 0)
        n.link(fade, 'Result', m1, 1)
        m2 = n.new('ShaderNodeMath', operation='MULTIPLY')
        n.link(m1, 0, m2, 0)
        n.link(nz, 'Fac', m2, 1)
        m3 = n.new('ShaderNodeMath', operation='MULTIPLY')
        n.link(m2, 0, m3, 0)
        n.link(low, 'Result', m3, 1)
        dens = n.new('ShaderNodeMath', operation='MULTIPLY')
        n.link(m3, 0, dens, 0)
        dens.inputs[1].default_value = 3.0
        v = n.new('ShaderNodeVolumePrincipled')
        n.inp(v, 'Color').default_value = (*hexc('#d8d4cc'), 1)
        n.link(dens, 0, v, 'Density')
        o = n.new('ShaderNodeOutputMaterial')
        n.link(v, 0, o, 'Volume')
    me.materials.append(_mat('smoke', b))
    ob = bpy.data.objects.new('smoke', me)
    ob.location = at
    link(ob, coll)
    return ob


# ------------------------------------------------------------------ the wreck

def wreck(coll, w_tiles=5, rows=3, seed=9):
    """A trading boat broken on the sand: lying over on her side, ribs showing, planks stove in, the mast snapped."""
    rnd = random.Random(seed)
    hullw = wood('hull', '#2a2119', '#4a3a2a', '#6b5844', 'X', 3.0)
    rib = wood('ribs', '#2b2016', '#4d3b2a', '#5e4a36', 'Z', 3.0)
    mats = [hullw, rib, cloth_mat(), plain('rope', '#7a6a4a', 0.9, 30, 0.3), plain('iron2', '#3b2f26', 0.6, 25, 0.35, 0.4), plain('weed', '#3d4a22', 0.6, 9, 0.4)]
    bm = bmesh.new()
    L = w_tiles + 0.6            # length along x
    B = DEPTH(rows) * 0.62        # beam
    Hh = 1.5                      # hull depth
    # hull frame: local coords along the keel (u from 0 bow to 1 stern), lying over by 'heel'
    heel = math.radians(38)
    R = Matrix.Rotation(heel, 3, 'X')
    base = Vector((w_tiles / 2, DEPTH(rows) * 0.5, 0.15))

    def section(u, ang):
        """A point on the hull skin: u along the keel, ang round the section (-1 port rail .. 1 starboard rail)."""
        width = B / 2 * (math.sin(math.pi * min(max(u, 0.02), 0.98)) ** 0.6)
        depth = Hh * (0.65 + 0.35 * math.sin(math.pi * u))
        a = ang * math.pi / 2
        y = math.sin(a) * width
        z = (1 - math.cos(a)) * depth * 0.95
        x = (u - 0.5) * L
        sheer = 0.25 * (2 * u - 1) ** 2
        return Vector((x, y, z + sheer))

    def tw(p):
        return base + R @ (p - Vector((0, 0, 0))) - Vector((0, 0, 0.35))

    # ribs (frames)
    for i in range(11):
        u = 0.06 + i * 0.088
        pts = [tw(section(u, a / 6 - 1)) for a in range(13)]
        for p0, p1 in zip(pts, pts[1:]):
            cyl(bm, p0, p1, 0.06, 6, 1)
    # keel
    kp = [tw(section(i / 12, 0) - Vector((0, 0, 0.1))) for i in range(13)]
    for p0, p1 in zip(kp, kp[1:]):
        cyl(bm, p0, p1, 0.1, 6, 0)
    # stem post rising at the bow
    cyl(bm, tw(section(0.0, 0)), tw(section(0.0, 0) + Vector((-0.35, 0, 1.9))), 0.09, 6, 0)
    # strakes (planks): most present on the low (buried) side, smashed open on the high side
    nstrakes = 9
    for s in range(nstrakes):
        a0 = -1 + 2 * s / nstrakes
        a1 = -1 + 2 * (s + 1) / nstrakes
        for i in range(14):
            u0, u1 = i / 14, (i + 1) / 14
            hole = (a0 > 0.3 and 0.3 < u0 < 0.7 and rnd.random() < 0.6) or rnd.random() < 0.05
            if hole: continue
            c = [section(u0, a0), section(u1, a0), section(u1, a1 - 0.02), section(u0, a1 - 0.02)]
            jitter = Vector((0, rnd.uniform(-0.02, 0.02), rnd.uniform(-0.02, 0.02)))
            outv = [tw(p * 1.02 + jitter) for p in c]
            inv = [tw(p * 0.97 + jitter) for p in c]
            vo = [bm.verts.new(p) for p in outv]
            vi = [bm.verts.new(p) for p in inv]
            for f in (vo, list(reversed(vi))):
                bm.faces.new(f).material_index = 0
            for k in range(4):
                bm.faces.new((vo[k], vo[(k + 1) % 4], vi[(k + 1) % 4], vi[k])).material_index = 0
    # deck beams across, a few gone
    for i in range(7):
        if rnd.random() < 0.3: continue
        u = 0.12 + i * 0.12
        cyl(bm, tw(section(u, -1) + Vector((0, 0.05, -0.05))), tw(section(u, 1) + Vector((0, -0.05, -0.05))), 0.07, 6, 1)
    # the mast, snapped: a stump in the hull and the top lying out across the sand with its yard and sail
    ms = tw(section(0.42, 0) + Vector((0, 0, 0.2)))
    cyl(bm, ms, ms + R @ Vector((0, 0, 1.3)), 0.12, 8, 0, r2=0.1)
    top0 = ms + R @ Vector((0, 0, 1.35)) + Vector((0.4, -0.6, -0.4))
    top1 = Vector((w_tiles + 1.4, -0.4, 0.08))
    top0.z = max(top0.z, 0.3)
    cyl(bm, top0, top1, 0.1, 8, 0, r2=0.08, segs=3)
    yard0, yard1 = top1 + Vector((-1.2, -0.9, 0.05)), top1 + Vector((-0.6, 1.0, 0.05))
    cyl(bm, yard0, yard1, 0.06, 6, 0)
    # torn sail: a draped sheet hanging from the yard onto the sand
    rows_s, cols_s = 6, 8
    grid = []
    for i in range(rows_s + 1):
        row = []
        for j in range(cols_s + 1):
            t = j / cols_s
            p = yard0.lerp(yard1, t)
            fall = i / rows_s
            q = p + Vector((-1.6 * fall, 0.25 * math.sin(t * 6 + i), 0))
            q.z = max(0.04, p.z - 1.2 * fall) + 0.08 * noise.noise(Vector((i * 0.7, j * 0.7, 3.1)))
            row.append(bm.verts.new(q))
        grid.append(row)
    for i in range(rows_s):
        for j in range(cols_s):
            if i > 2 and rnd.random() < 0.25: continue    # torn
            f = bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
            f.material_index = 2
            f.smooth = True
    # ropes trailing from the stump to the sand, sagging
    for k in range(3):
        a = ms + R @ Vector((0, 0, 1.2 - k * 0.2))
        b = Vector((rnd.uniform(-0.5, w_tiles + 0.5), rnd.uniform(-0.8, -0.2), 0.05))
        prev = a
        for s in range(1, 9):
            t = s / 8
            p = a.lerp(b, t)
            p.z = a.z + (b.z - a.z) * t - 0.6 * math.sin(math.pi * t) * (1 - t) - 0.0
            p.z = max(p.z, 0.04)
            cyl(bm, prev, p, 0.025, 5, 3)
            prev = p
    # loose planks and a broken barrel hoop on the sand
    for k in range(6):
        cx, cy = rnd.uniform(-0.8, w_tiles + 0.6), rnd.uniform(-0.9, DEPTH(rows) + 0.2)
        cube(bm, (cx, cy, 0.06), (rnd.uniform(0.8, 1.6), 0.2, 0.05), 0, Matrix.Rotation(rnd.uniform(0, 3.14), 3, 'Z') @ Matrix.Rotation(rnd.uniform(-0.1, 0.1), 3, 'X'))
    # seaweed caught on the low side
    for k in range(10):
        p = tw(section(rnd.uniform(0.1, 0.9), -1)) + Vector((0, -0.1, 0))
        p.z = max(0.05, p.z - rnd.uniform(0.0, 0.3))
        cube(bm, p, (rnd.uniform(0.2, 0.5), 0.12, 0.03), 5, Matrix.Rotation(rnd.uniform(0, 3.14), 3, 'Z'))
    ob = finish('wreck', bm, mats, coll)
    return ob


# ------------------------------------------------------------------ palisade, gate, bridge, brambles, steps

def palisade(coll, tiles, G, gate=False, seed=3):
    """A wall of sharpened logs along a run of tiles (list of (x, y)), each log real and a little different."""
    rnd = random.Random(seed)
    mats = [wood('palisade', '#2d2218', '#54402c', '#6e5640', 'Z', 2.5), plain('rope2', '#6b5b40', 0.9, 30, 0.3)]
    bm = bmesh.new()
    for (x, y) in tiles:
        n = 4
        for k in range(n):
            px = x + (k + 0.5) / n + rnd.uniform(-0.03, 0.03)
            py = y + 0.6 + rnd.uniform(-0.05, 0.05)
            z0 = G.z(px, py) - 0.3
            h = rnd.uniform(2.6, 3.1) * (1.25 if gate else 1.0)
            base = G.at(px, py) * 0 + __import__('common').place(px, py, z0)
            r = rnd.uniform(0.1, 0.13)
            cyl(bm, base, base + Vector((rnd.uniform(-0.04, 0.04), 0, h)), r, 8, 0, wobble=0.06, seed=rnd.random() * 9, segs=3)
            # the point
            tip = base + Vector((0, 0, h + 0.35))
            ring = [bm.verts.new(base + Vector((math.cos(a) * r, math.sin(a) * r, h))) for a in (2 * math.pi * i / 8 for i in range(8))]
            t = bm.verts.new(tip)
            for i in range(8):
                bm.faces.new((ring[i], ring[(i + 1) % 8], t)).material_index = 0
        # a lashed rail across the back
        a = __import__('common').place(x, y + 0.75, G.z(x, y + 0.75)) + Vector((0, 0, 1.9))
        b = __import__('common').place(x + 1, y + 0.75, G.z(x + 1, y + 0.75)) + Vector((0, 0, 1.9))
        cyl(bm, a, b, 0.05, 6, 1)
    return finish('palisade', bm, mats, coll)


def bridge(coll, tiles, G, seed=2):
    """Planks across the stream on two log stringers, with a rope rail."""
    rnd = random.Random(seed)
    from common import place
    mats = [wood('bplanks', '#3a2d20', '#5e4a35', '#7a6248', 'X', 5.0), wood('stringer', '#2d2218', '#4a3a2a', '#5e4a36', 'Y', 2.0)]
    bm = bmesh.new()
    for (x, y) in tiles:
        z = 0.12
        for k in range(5):
            py = y + (k + 0.5) / 5
            c = place(x + 0.5, py, z)
            cube(bm, c, (1.05, 0.18 / CA, 0.06), 0, Matrix.Rotation(rnd.uniform(-0.04, 0.04), 3, 'Z'))
        for sx in (0.1, 0.9):
            cyl(bm, place(x + sx, y, z - 0.08), place(x + sx, y + 1, z - 0.08), 0.08, 7, 1)
    return finish('bridge', bm, mats, coll)


def bramble_mat():
    def b(n):
        tc = n.new('ShaderNodeTexCoord')
        nz = n.new('ShaderNodeTexNoise', in_Scale=4.0, in_Detail=4.0)
        n.link(tc, 'Object', nz, 'Vector')
        c = ramp(n, [(0.0, '#1d2a12'), (0.5, '#33461f'), (0.8, '#4f5a2a'), (1.0, '#5e2a2a')], nz, 'Fac')
        p = principled(n, rough=0.6)
        n.link(c, 0, p, 'Base Color')
        output(n, p)
    return _mat('bramble', b)


def brambles(coll, x, y, G, seed=1):
    """A tangle of thorny canes with dark leaves and the odd berry."""
    rnd = random.Random(seed)
    from common import place
    bm = bmesh.new()
    import trees as T
    spots = []
    for k in range(3):
        c = place(x + rnd.uniform(0.25, 0.75), y + rnd.uniform(0.3, 0.8), G.z(x + 0.5, y + 0.5) + 0.3)
        T.blob(bm, c, rnd.uniform(0.3, 0.45), rnd.random() * 9, 0.7, 0.45, 2, leaves=spots)
    T.leaf_cards(bm, spots, rnd, size=0.09, per=2, mat=0)
    for f in bm.faces:
        if f.material_index == 2: f.material_index = 1
    for k in range(6):
        b0 = place(x + rnd.uniform(0.1, 0.9), y + rnd.uniform(0.2, 0.9), G.z(x, y))
        b1 = b0 + Vector((rnd.uniform(-0.6, 0.6), rnd.uniform(-0.4, 0.4), rnd.uniform(0.3, 0.7)))
        cyl(bm, b0, b1, 0.015, 4, 2)
    mats = [bramble_mat(), plain('bramble_core', '#0f1709', 0.8), plain('cane', '#4a2a22', 0.6)]
    return finish('bramble', bm, mats, coll)


def steps(coll, x, y, G, seed=1):
    """Rough stone steps up the cliff."""
    from common import place
    bm = bmesh.new()
    zl, zh = G.z(x + 0.5, y + 1.0), G.z(x + 0.5, y)
    for k in range(5):
        t = k / 5
        z = zl + (zh - zl) * (t + 0.1)
        c = place(x + 0.5, y + 1 - t - 0.1, z - 0.05)
        cube(bm, c, (0.95, 0.3 / CA, 0.18), 0, Matrix.Rotation(random.Random(k + seed).uniform(-0.05, 0.05), 3, 'Z'))
    return finish('steps', bm, [stone_mat()], coll)
