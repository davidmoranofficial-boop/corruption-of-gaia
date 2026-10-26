"""Cartoon look for the MakeHuman people (Dragon Quest / Hades feel): flat banded shading and ink outlines.

Needs EEVEE (Shader to RGB doesn't exist in Cycles); run with EGL_PLATFORM=surfaceless here.
"""
import math
import random

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import mhuman


def toon_mat(name, base, shade=None, shadow_tint=(0.86, 0.7, 0.82), hi=1.12, band=0.32, spec=False):
    """Lit/shadow bands from a diffuse shader; the shadow is the base colour darkened and tinted (warm-purple)."""
    b = mhuman.lin(base)[:3]
    sh = mhuman.lin(shade)[:3] if shade else tuple(c * 0.52 * t for c, t in zip(b, shadow_tint))
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    df = nt.nodes.new('ShaderNodeBsdfDiffuse')
    s2r = nt.nodes.new('ShaderNodeShaderToRGB')
    nt.links.new(df.outputs[0], s2r.inputs[0])
    bw = nt.nodes.new('ShaderNodeRGBToBW')
    nt.links.new(s2r.outputs[0], bw.inputs[0])
    cr = nt.nodes.new('ShaderNodeValToRGB')
    cr.color_ramp.interpolation = 'CONSTANT'
    e = cr.color_ramp.elements
    e[0].position, e[0].color = 0.0, sh + (1,)
    e[1].position, e[1].color = band, b + (1,)
    if hi: e.new(0.9).color = tuple(min(1.0, c * hi) for c in b) + (1,)
    nt.links.new(bw.outputs[0], cr.inputs[0])
    em = nt.nodes.new('ShaderNodeEmission')
    nt.links.new(cr.outputs[0], em.inputs[0])
    nt.links.new(em.outputs[0], out.inputs[0])
    return m


def ink(name, col=(0.06, 0.04, 0.035)):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    em.inputs[0].default_value = col + (1,)
    nt.links.new(em.outputs[0], out.inputs[0])
    m.use_backface_culling = True
    m.use_backface_culling_shadow = True     # the inside-out shell must not shadow the body it wraps
    return m


def outline(ob, thick, mat):
    ob.data.materials.append(mat)
    so = ob.modifiers.new('ink', 'SOLIDIFY')
    so.thickness = thick
    so.offset = 1.0
    so.use_flip_normals = True
    so.use_rim = False
    so.material_offset = len(ob.data.materials) - 1
    return so


def toonify(meshes, spec, thick=0.006):
    ik = ink('ink')
    skin = spec['skin']
    for ob in meshes:
        n = ob.name.split('_', 1)[-1]
        if n == 'body':
            m = toon_mat('t_skin', skin, shade=spec.get('skin_shade'), hi=0, band=0.3)
        elif n.startswith('eye'):
            continue
        elif n in ('hair', 'haircap'):
            m = toon_mat('t_hair', spec['hair'], hi=1.5, band=0.3)
        else:
            m = toon_mat('t_' + n, spec.get(n, '#888888'))
        if ob.type == 'MESH':
            ob.data.materials.clear()
            ob.data.materials.append(m)
            outline(ob, thick * {'body': 0.6, 'hair': 0.45, 'haircap': 0.6}.get(n, 1.0), ik)
    return ik


# ------------------------------------------------------------------ cartoon hair: a cap plus big flat spikes

def _bone_parent(ob, arm):
    ob.parent = arm
    ob.parent_type = 'BONE'
    ob.parent_bone = 'head'
    bpy.context.view_layer.update()
    ob.matrix_world = Matrix.Identity(4)


def spikes(name, arm, cap, mat, n=80, seed=3, coll=None):
    """Chunky flat locks growing out of the hair cap, combed from a crown whorl, with a fringe over the brow."""
    coll = coll or bpy.context.scene.collection
    rs = random.Random(seed)
    hc = Vector(arm['head_c'])
    top_z = arm['top_z']
    hcc = Vector((0.0, hc.y + 0.012, hc.z + (top_z - hc.z) * 0.42))
    dg = bpy.context.evaluated_depsgraph_get()
    ev = cap.evaluated_get(dg)
    me = ev.to_mesh()
    pts = []
    for v in me.vertices:
        co = cap.matrix_world @ v.co
        nr = (cap.matrix_world.to_3x3() @ v.normal).normalized()
        if nr.dot(co - hcc) > 0.3 * (co - hcc).length: pts.append((co, nr))
    ev.to_mesh_clear()
    rs.shuffle(pts)
    roots = []
    for c, nr in pts:
        if all((c - r[0]).length > 0.021 for r in roots): roots.append((c, nr))
        if len(roots) >= n: break
    whorl = hcc + Vector((0.0, 0.06, 0.08))
    bm = bmesh.new()
    SEG, RING = 6, 6
    down = Vector((0, 0, -1))
    for root, nr in roots:
        front = root.y < hcc.y - 0.035
        sx = 1 if root.x >= 0 else -1
        if front:
            d = Vector((sx * rs.uniform(0.1, 0.45), -0.3, -1.0))
        else:
            d = (root - whorl) + down * 0.03
        d = (d - nr * d.dot(nr)).normalized()
        L = rs.uniform(0.065, 0.1) * (1.05 if front else 1.0)
        R0 = (root - hcc).length
        p = root - nr * 0.006
        path = [p.copy()]
        for k in range(SEG - 1):
            d = (d + down * 0.18 + nr * (0.25 if k == 0 else -0.04)).normalized()
            p = p + d * (L / (SEG - 1))
            q = p - hcc
            if q.length < R0 + 0.002: p = hcc + q.normalized() * (R0 + 0.002)
            path.append(p.copy())
        w0 = rs.uniform(0.03, 0.042)
        rings = []
        for k, c in enumerate(path):
            t = k / (SEG - 1)
            tg = (path[min(k + 1, SEG - 1)] - path[max(k - 1, 0)]).normalized()
            out = (c - hcc).normalized()
            sv = tg.cross(out).normalized()
            uv = sv.cross(tg).normalized()
            if k == SEG - 1:
                rings.append([bm.verts.new(c)])
                continue
            w = w0 * (1 - t) ** 0.8 * 0.5
            th = 0.011 * (1 - t) ** 0.8 * 0.5
            rings.append([bm.verts.new(c + sv * math.cos(a) * w + uv * math.sin(a) * th)
                          for a in (2 * math.pi * j / RING for j in range(RING))])
        for k in range(SEG - 2):
            for j in range(RING):
                bm.faces.new((rings[k][j], rings[k][(j + 1) % RING], rings[k + 1][(j + 1) % RING], rings[k + 1][j]))
        tip = rings[-1][0]
        for j in range(RING):
            bm.faces.new((rings[-2][j], rings[-2][(j + 1) % RING], tip))
        bm.faces.new(list(reversed(rings[0])))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for pl in me.polygons: pl.use_smooth = True
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    _bone_parent(ob, arm)
    return ob


# ------------------------------------------------------------------ cartoon eyes and brows, painted on a decal

EW, EH, EC = 0.050, 0.064, 0.36        # decal size (m) and where the eye centre sits up it


def _eye_image(name, iris, brow, outer=1, scale=1.25, W=220, H=280):
    """Big readable cartoon eye: white, large dark iris, pupil, catch-light, heavy upper lid, thick brow. sRGB pixels."""
    def hx(h): h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    X = (u[None, :] - 0.5) * EW * outer
    Z = (v[:, None] - EC) * EH
    X, Z = X / scale, Z / scale
    img = np.zeros((H, W, 4))

    def put(mask, col, a=1.0):
        img[mask, :3] = col
        img[mask, 3] = a
    rx, rz = 0.0115, 0.0092
    scl = (X / rx) ** 2 + ((Z + 0.0004) / (rz * (1.0 if True else 1))) ** 2 < 1
    put(scl, hx('#f6f1e8'))
    ic = np.array([0.0006, -0.0006])
    di = np.hypot(X - ic[0], Z - ic[1])
    ir = 0.0080
    put(scl & (di < ir), hx(iris))
    put(scl & (di < ir) & (Z - ic[1] > 0.003), hx(iris) * 0.55)          # shadow of the lid on the iris
    put(scl & (di < ir) & (di > ir - 0.0009), hx(iris) * 0.4)
    put(scl & (di < 0.0034), hx('#120c0a'))
    put(np.hypot(X - ic[0] + 0.0024, Z - ic[1] - 0.0026) < 0.0017, hx('#ffffff'))
    put(scl & (np.hypot(X - ic[0] - 0.003, Z - ic[1] + 0.0035) < 0.0008), hx('#ffffff'))
    # upper lid: a thick arc over the top of the white, flicking out at the outer corner
    arc = rz * np.sqrt(np.clip(1 - (X / (rx * 1.04)) ** 2, 0, 1)) + 0.0002
    lid = (np.abs(X) < rx * 1.04) & (Z > arc - 0.0011 - 0.0006 * (X > 0)) & (Z < arc + 0.0016)
    flick = (X > rx * 0.7) & (X < rx * 1.32) & (np.abs(Z - (0.0026 - (X - rx * 0.7) * 0.25)) < 0.0013 * (1.32 * rx - X) / (0.62 * rx))
    put(lid | flick, hx('#1a1210'))
    # lower lid: a short thin line on the outer half
    lo = (X > rx * 0.15) & (X < rx * 0.98) & (np.abs(Z + rz * np.sqrt(np.clip(1 - (X / rx) ** 2, 0, 1)) + 0.0003) < 0.0005)
    put(lo, hx('#5a3a2c'))
    # brow: thick, a little lower at the inner end (determined)
    bz = 0.0175 + 0.0016 * (X / 0.013)
    thick = 0.0024 * (1 - 0.45 * np.clip((X + 0.002) / 0.016, 0, 1))
    put((X > -0.012) & (X < 0.0145) & (np.abs(Z - bz) < thick), hx(brow))
    im = bpy.data.images.new(name, W, H, alpha=True)
    im.pixels = img.ravel().tolist()
    im.pack()
    return im


def _decal_mat(name, im):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    tx = nt.nodes.new('ShaderNodeTexImage')
    tx.image = im
    tx.extension = 'CLIP'
    em = nt.nodes.new('ShaderNodeEmission')
    nt.links.new(tx.outputs['Color'], em.inputs[0])
    tr = nt.nodes.new('ShaderNodeBsdfTransparent')
    mx = nt.nodes.new('ShaderNodeMixShader')
    nt.links.new(tx.outputs['Alpha'], mx.inputs[0])
    nt.links.new(tr.outputs[0], mx.inputs[1])
    nt.links.new(em.outputs[0], mx.inputs[2])
    nt.links.new(mx.outputs[0], out.inputs[0])
    return m


def _decal(dname, arm, bvh, c, w, h, cz, mat, coll, lift=0.0016):
    """A small grid laid over the face (ray-cast from the front), smoothed so it floats over lids and creases."""
    NX, NZ = 22, 28
    grid = []
    for iz in range(NZ):
        row = []
        for ix in range(NX):
            x = c.x + (ix / (NX - 1) - 0.5) * w
            z = c.z + (iz / (NZ - 1) - cz) * h
            hit = bvh.ray_cast(Vector((x, c.y - 0.4, z)), Vector((0, 1, 0)), 0.6)
            row.append(hit[0].y if hit[0] is not None else None)
        grid.append(row)
    fallback = min([y for r in grid for y in r if y is not None] + [c.y])
    ys = [[min([grid[j][i] for j in range(max(0, iz - 3), min(NZ, iz + 4)) for i in range(max(0, ix - 3), min(NX, ix + 4))
                if grid[j][i] is not None] or [fallback]) for ix in range(NX)] for iz in range(NZ)]
    for _ in range(4):
        ys = [[sum(ys[j][i] for j in range(max(0, iz - 1), min(NZ, iz + 2)) for i in range(max(0, ix - 1), min(NX, ix + 2))) /
               (len(range(max(0, iz - 1), min(NZ, iz + 2))) * len(range(max(0, ix - 1), min(NX, ix + 2)))) for ix in range(NX)] for iz in range(NZ)]
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new()
    vv = [[bm.verts.new((c.x + (ix / (NX - 1) - 0.5) * w, ys[iz][ix] - lift, c.z + (iz / (NZ - 1) - cz) * h))
           for ix in range(NX)] for iz in range(NZ)]
    for iz in range(NZ - 1):
        for ix in range(NX - 1):
            f = bm.faces.new((vv[iz][ix], vv[iz][ix + 1], vv[iz + 1][ix + 1], vv[iz + 1][ix]))
            for l, (a, b) in zip(f.loops, ((ix, iz), (ix + 1, iz), (ix + 1, iz + 1), (ix, iz + 1))):
                l[uvl].uv = (a / (NX - 1), b / (NZ - 1))
    me = bpy.data.meshes.new(dname)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    ob = bpy.data.objects.new(dname, me)
    coll.objects.link(ob)
    ob.visible_shadow = False
    _bone_parent(ob, arm)
    return ob


MW, MH, MC = 0.05, 0.03, 0.5


def _mouth_image(name, lip='#7a3a30', W=240, H=144):
    """A simple mouth: one ink stroke, set and a touch stubborn, with a short lower-lip mark."""
    def hx(h): h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])
    X = ((np.arange(W) + 0.5) / W - 0.5)[None, :] * MW
    Z = ((np.arange(H) + 0.5) / H - MC)[:, None] * MH
    img = np.zeros((H, W, 4))
    half = 0.0105
    line_z = 0.0006 * (np.abs(X) / half) ** 2 - 0.0002
    th = 0.00085 * np.clip(1 - (np.abs(X) / half) ** 3, 0, 1) + 0.0002
    m = (np.abs(X) < half) & (np.abs(Z - line_z) < th)
    img[m, :3] = hx('#3a1e18'); img[m, 3] = 1
    lo = (np.abs(X) < 0.0035) & (np.abs(Z + 0.0042 + 0.00015 * (X / 0.0035) ** 2 * 10) < 0.00045)
    img[lo, :3] = hx(lip); img[lo, 3] = 0.85
    im = bpy.data.images.new(name, W, H, alpha=True)
    im.pixels = img.ravel().tolist()
    im.pack()
    return im


def eyes(name, arm, body, eyeballs, spec, coll=None):
    """Swap the small realistic eyes and lips for big painted cartoon ones laid over the face."""
    coll = coll or bpy.context.scene.collection
    dg = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(body, dg)
    brow = spec.get('brow_col', '#2a1a12')
    out = []
    for eb in eyeballs:
        c = eb.matrix_world.translation.copy()
        outer = 1 if c.x > 0 else -1
        im = _eye_image(eb.name + '_paint', spec['iris'], brow, outer, spec.get('eye_scale', 1.25))
        out.append(_decal(eb.name + '_paint', arm, bvh, c, EW, EH, EC, _decal_mat(eb.name + '_paint', im), coll))
        eb.hide_render = True
    oris = [b.head_local for b in arm.data.bones if b.name.startswith('oris')]
    mc = sum(oris, Vector()) / len(oris)
    out.append(_decal(name + '_mouth_paint', arm, bvh, mc, MW, MH, MC,
                      _decal_mat(name + '_mouth_paint', _mouth_image(name + '_mouth')), coll, lift=0.0012))
    return out


def face_normals(arm, body, head_scale=1.0):
    """Anime-style face shading: the face takes its normals from a smooth egg round the head, so the light
    falls in one clean shape instead of picking out every crease of a realistic face."""
    hd = arm.data.bones['head']
    hc = hd.head_local
    top = arm['top_z']
    c = Vector((0.0, hc.y + 0.004, hc.z + (top - hc.z) * 0.38))
    bpy.ops.mesh.primitive_uv_sphere_add(radius=1.0, location=c, segments=48, ring_count=32)
    egg = bpy.context.active_object
    egg.name = body.name + '_egg'
    k = (top - hc.z) * 0.62
    egg.scale = (k * 0.86, k * 0.95, k * 1.05)
    bpy.ops.object.transform_apply(scale=True)
    for p in egg.data.polygons: p.use_smooth = True
    egg.hide_render = True
    egg.hide_viewport = False
    for cl in list(egg.users_collection): cl.objects.unlink(egg)
    body.users_collection[0].objects.link(egg)
    _bone_parent(egg, arm)
    egg.matrix_world = Matrix.Identity(4)
    # weight: the head (not the neck), faded out towards the jaw line
    g = body.vertex_groups.new(name='face_n')
    me = body.data
    def in_head(b):
        while b is not None:
            if b.name == 'head': return True
            b = b.parent
        return False
    hg = {g.index for g in body.vertex_groups if g.name in arm.data.bones and in_head(arm.data.bones[g.name])}
    for v in me.vertices:
        w = min(1.0, sum(ge.weight for ge in v.groups if ge.group in hg))
        if w > 0:
            w *= max(0.0, min(1.0, (v.co.z - (hc.z - 0.02)) / 0.05))
            if w > 0: g.add([v.index], w, 'REPLACE')
    dt = body.modifiers.new('face_n', 'DATA_TRANSFER')
    dt.object = egg
    dt.use_loop_data = True
    dt.data_types_loops = {'CUSTOM_NORMAL'}
    dt.loop_mapping = 'POLYINTERP_NEAREST'
    dt.vertex_group = 'face_n'
    dt.mix_factor = 1.0
    # no self-shadowing: a nose or lip shadow on a cartoon face reads as dirt (clothes and hair still cast)
    body.visible_shadow = False
    return egg


# softer, simpler features: small nose, round head, no lines or bags (MakeHuman detail targets)
CARTOON_FACE = {
    'head/head-round.target': 0.7,
    'nose/nose-scale-depth-decr.target': 0.7, 'nose/nose-volume-decr.target': 0.7,
    'nose/nose-scale-vert-decr.target': 0.4, 'nose/nose-nostrils-width-decr.target': 0.6,
    'nose/nose-hump-decr.target': 1.0, 'nose/nose-flaring-decr.target': 0.8,
    'mouth/mouth-laugh-lines-in.target': 1.0, 'mouth/mouth-scale-horiz-decr.target': 0.3,
    'mouth/mouth-lowerlip-volume-decr.target': 0.5,
    'chin/chin-prominent-decr.target': 0.4, 'chin/chin-cleft-decr.target': 1.0,
    'cheek/l-cheek-bones-decr.target': 0.6, 'cheek/r-cheek-bones-decr.target': 0.6,
    'eyes/l-eye-bag-decr.target': 1.0, 'eyes/r-eye-bag-decr.target': 1.0,
}


def cartoon(name, spec, coll=None, thick=0.006):
    """One call: a MakeHuman person with cartoon proportions, spiky hair, painted eyes, flat shading and ink."""
    spec = dict(spec, shape=dict(CARTOON_FACE, **spec.get('shape', {})))
    arm, ms = mhuman.person(name, spec, coll)
    body = next(m for m in ms if m.name.endswith('_body'))
    cap = next(m for m in ms if m.name.endswith('_haircap'))
    eyeballs = [m for m in ms if '_eye' in m.name]
    ms.append(spikes(name + '_hair', arm, cap, cap.data.materials[0], coll=coll))
    ms += eyes(name, arm, body, eyeballs, spec, coll)
    face_normals(arm, body)
    toonify([m for m in ms if not m.name.endswith('_paint')], spec, thick)
    calm_face_ink(arm, body)
    return arm, ms


def calm_face_ink(arm, body):
    """No ink inside the nose and mouth creases (the inside-out shell pokes through there as dark smudges);
    the jaw and cheek silhouette keep their line."""
    B = arm.data.bones
    oris = [b.head_local for b in B if b.name.startswith('oris')]
    mc = sum(oris, Vector()) / len(oris)
    ez = (B['eye.L'].head_local.z + B['eye.R'].head_local.z) / 2
    hy = B['head'].head_local.y
    g = body.vertex_groups.new(name='ink_w')
    for v in body.data.vertices:
        c = v.co
        inside = min((0.034 - abs(c.x)) / 0.012, (c.z - (mc.z - 0.03)) / 0.012, (ez - 0.004 - c.z) / 0.012, (hy - 0.03 - c.y) / 0.01)
        g.add([v.index], 1.0 - max(0.0, min(1.0, inside)), 'REPLACE')
    ink_mod = body.modifiers['ink']
    ink_mod.vertex_group = 'ink_w'
    ink_mod.thickness_vertex_group = 0.0
