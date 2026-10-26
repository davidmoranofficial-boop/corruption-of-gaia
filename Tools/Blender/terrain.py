"""The ground of an area: one heightfield mesh built from the tile map, its painted material, and the sea.

Tile letters become soft weights (sand, grass, road, rock, forest floor...) stored on the mesh as colour
attributes; the material blends them with noise so edges look natural rather than tiled.
"""
import math

import bpy
import bmesh
import numpy as np

from common import (place, vnoise, fbm, box, distance, link, collection, Nodes, principled, output, hexc, ramp, SA, CA)

S = 4          # height samples per tile
PAD = 4        # tiles of extra ground round the map edge (so nothing is cut off at the frame edge)
WATER = -0.05  # sea level

SEA = set('~')
SAND = set(',QR')
ROAD = set('=bsDcPm')
STREAM = set('w')
PLATEAU = set('u')
CLIFF = set('h')
FOREST = set('T')
LONG = set('gk')
ROCKY = set('rR')


def padded_rows(rows):
    W = len(rows[0])
    rr = [r[0] * PAD + r + r[-1] * PAD for r in rows]
    top = [rr[0]] * PAD
    bot = [rr[-1]] * PAD
    return top + rr + bot


def fields(rows, seed=3):
    """All the per-sample fields: height and material weights. Grid is (H*S+1, W*S+1) with PAD tiles round."""
    pr = padded_rows(rows)
    TH, TW = len(pr), len(pr[0])
    H, W = TH * S + 1, TW * S + 1
    iy = np.clip((np.arange(H) - S / 2) // S, 0, TH - 1).astype(int)
    ix = np.clip((np.arange(W) - S / 2) // S, 0, TW - 1).astype(int)
    grid = np.array([list(r) for r in pr])
    codes = grid[iy][:, ix]

    def m(chars):
        return np.isin(codes, list(chars)).astype(float)

    sea = m(SEA)
    land = 1 - sea
    # signed distance to the shoreline, in metres (positive on land)
    d_land = distance(sea < 0.5) / S
    d_sea = distance(sea > 0.5) / S
    sd = np.where(sea > 0.5, -d_land + 0.5, d_sea - 0.5)
    sd = box(sd, S) + (fbm(H, W, 14, seed) - 0.5) * 2.2 + (fbm(H, W, 4, seed + 9) - 0.5) * 0.5

    n_lo = fbm(H, W, 24, seed + 1)
    n_hi = fbm(H, W, 3, seed + 2)

    # the beach rises from the water, then the land rolls a little
    h = np.where(sd >= 0, 0.10 + np.clip(sd, 0, 3) / 3 * 0.16 + (n_lo - 0.5) * 0.16 * np.clip(sd / 3, 0, 1),
                 0.10 + np.maximum(sd, -1.2) * 0.32 + np.minimum(sd + 1.2, 0) * 0.42)
    h = np.maximum(h, -2.2)
    road = box(m(ROAD), 1)
    h -= 0.05 * road
    stream = box(m(STREAM), 2)
    h -= 0.62 * np.clip(stream * 1.6, 0, 1)
    plateau = box(m(PLATEAU) + 0.5 * m(CLIFF), 2)
    plateau = np.clip((plateau - 0.25) / 0.5, 0, 1)
    cliff = box(m(CLIFF), 1)
    h += 1.3 * plateau * plateau * (3 - 2 * plateau) + (n_hi - 0.5) * 0.35 * cliff
    h += (n_hi - 0.5) * 0.03

    sand = box(m(SAND) + sea, 2)
    sand = np.clip(sand + np.clip(1 - sd / 1.5, 0, 1) * 0.6, 0, 1)
    grass = box(m('.ogu') + m(LONG) + m('HZIFKnjNEY'), 2)
    forest = box(m(FOREST), 3)
    rock = box(m(CLIFF) * 1.0 + m(ROCKY) * 0.7 + m(STREAM) * 0.8, 1)
    longg = box(m(LONG), 2)
    wet = np.clip(1 - (sd + 0.3) / 1.2, 0, 1) * (sd > -1.5)
    flowers = box(m('o'), 1)
    return dict(h=h, sand=sand, grass=grass, road=road, rock=rock, forest=forest, longg=longg, wet=wet, sd=sd,
                stream=stream, flowers=flowers, plateau=plateau, TH=TH, TW=TW, H=H, W=W)


def build_ground(F, mat, coll=None):
    H, W, h = F['H'], F['W'], F['h']
    bm = bmesh.new()
    verts = []
    for i in range(H):
        y = i / S - PAD
        row = []
        for j in range(W):
            x = j / S - PAD
            row.append(bm.verts.new(place(x, y, float(h[i, j]))))
        verts.append(row)
    for i in range(H - 1):
        for j in range(W - 1):
            bm.faces.new((verts[i][j], verts[i][j + 1], verts[i + 1][j + 1], verts[i + 1][j]))
    me = bpy.data.meshes.new('ground')
    bm.to_mesh(me)
    bm.free()
    a1 = me.color_attributes.new('mixA', 'FLOAT_COLOR', 'POINT')
    a2 = me.color_attributes.new('mixB', 'FLOAT_COLOR', 'POINT')
    A = np.stack([F['sand'], F['grass'], F['road'], F['rock']], -1).reshape(-1, 4)
    B = np.stack([F['forest'], F['longg'], F['wet'], F['flowers']], -1).reshape(-1, 4)
    a1.data.foreach_set('color', np.clip(A, 0, 1).astype(np.float32).ravel())
    a2.data.foreach_set('color', np.clip(B, 0, 1).astype(np.float32).ravel())
    for p in me.polygons: p.use_smooth = True
    me.materials.append(mat)
    ob = bpy.data.objects.new('ground', me)
    return link(ob, coll)


def ground_material():
    mat = bpy.data.materials.new('ground')
    n = Nodes(mat)
    tex = n.new('ShaderNodeTexCoord')
    A = n.new('ShaderNodeVertexColor', layer_name='mixA')
    B = n.new('ShaderNodeVertexColor', layer_name='mixB')
    sa, sb = n.new('ShaderNodeSeparateColor'), n.new('ShaderNodeSeparateColor')
    n.link(A, 'Color', sa, 'Color')
    n.link(B, 'Color', sb, 'Color')
    aa = n.new('ShaderNodeAttribute', attribute_name='mixA')   # alpha holds rock
    bb = n.new('ShaderNodeAttribute', attribute_name='mixB')   # alpha holds flowers

    def noise(scale, detail=4, rough=0.55):
        t = n.new('ShaderNodeTexNoise', in_Scale=scale, in_Detail=detail, in_Roughness=rough)
        n.link(tex, 'Object', t, 'Vector')
        return t

    N1, N2, N3, N4 = noise(0.35), noise(2.2, 6), noise(9, 8, 0.6), noise(40, 2)
    vor = n.new('ShaderNodeTexVoronoi', in_Scale=26.0)
    n.link(tex, 'Object', vor, 'Vector')

    def math_(op, a, b=None, clamp=True, ao=0, bo=0):
        nd = n.new('ShaderNodeMath', operation=op, use_clamp=clamp)
        if isinstance(a, (int, float)): nd.inputs[0].default_value = a
        else: n.link(a, ao, nd, 0)
        if b is not None:
            if isinstance(b, (int, float)): nd.inputs[1].default_value = b
            else: n.link(b, bo, nd, 1)
        return nd

    def mask(w, wo, nz, lo=0.35, hi=0.6, amt=0.55):
        # weight + noise, then a soft step: natural ragged edges
        s = math_('MULTIPLY_ADD', nz, amt, clamp=False, ao='Fac')
        s.inputs[2].default_value = -amt / 2
        a = math_('ADD', w, s, clamp=False, ao=wo)
        r = n.new('ShaderNodeMapRange', in_From_Min=lo, in_From_Max=hi)
        n.link(a, 0, r, 'Value')
        return r

    def mix(a, b, fac, fo=0):
        mx = n.new('ShaderNodeMix', data_type='RGBA', blend_type='MIX')
        n.link(fac, fo, mx, 'Factor')
        res = lambda nd: 'Result' if nd.bl_idname in ('ShaderNodeMix', 'ShaderNodeMapRange') else 0
        if not isinstance(a, tuple): n.link(a, res(a), mx, 'A')
        if not isinstance(b, tuple): n.link(b, res(b), mx, 'B')
        if isinstance(a, tuple): n.inp(mx, 'A').default_value = (*a, 1)
        if isinstance(b, tuple): n.inp(mx, 'B').default_value = (*b, 1)
        return mx

    sand_c = ramp(n, [(0.0, '#a88f66'), (0.45, '#c0a67a'), (0.8, '#cfb88c')], N2, 'Fac')
    wet_c = ramp(n, [(0.0, '#7d6c52'), (1.0, '#9a8763')], N3, 'Fac')
    grass_c = ramp(n, [(0.0, '#3a5624'), (0.35, '#4b6c2b'), (0.62, '#5f7f34'), (0.85, '#7f8a44')], N1, 'Fac')
    grass_d = ramp(n, [(0.0, '#33501f'), (0.5, '#4d6e2c'), (1.0, '#6a8038')], N3, 'Fac')
    forest_c = ramp(n, [(0.0, '#2f3a1f'), (0.45, '#4a4a28'), (0.75, '#5e4b2c'), (1.0, '#4a5a2a')], N2, 'Fac')
    road_c = ramp(n, [(0.0, '#6e5a42'), (0.5, '#8b7455'), (1.0, '#a08a68')], N3, 'Fac')
    rock_c = ramp(n, [(0.0, '#5d5a52'), (0.5, '#7d786c'), (1.0, '#9a9384')], N2, 'Fac')

    g2 = mix(grass_c, grass_d, N4, 'Fac')
    dirt_c = ramp(n, [(0.0, '#5a4a34'), (1.0, '#7a6648')], N3, 'Fac')
    c = mix(dirt_c, sand_c, mask(sa, 'Red', N2, 0.3, 0.6, 0.5), 'Result')
    c = mix(c, wet_c, mask(sb, 'Blue', N2, 0.3, 0.7, 0.3), 'Result')
    c = mix(c, g2, mask(sa, 'Green', N2, 0.42, 0.58, 0.7), 'Result')
    c = mix(c, forest_c, mask(sb, 'Red', N2, 0.35, 0.65, 0.6), 'Result')
    c = mix(c, road_c, mask(sa, 'Blue', N3, 0.38, 0.62, 0.5), 'Result')
    c = mix(c, rock_c, mask(aa, 'Alpha', N3, 0.3, 0.6, 0.5), 'Result')

    # under the sea: darker and bluer the deeper it goes
    geo = n.new('ShaderNodeNewGeometry')
    sep = n.new('ShaderNodeSeparateXYZ')
    n.link(geo, 'Position', sep, 'Vector')
    depth = n.new('ShaderNodeMapRange', in_From_Min=WATER - 0.05, in_From_Max=WATER - 1.4)
    n.link(sep, 'Z', depth, 'Value')
    c = mix(c, (0.03, 0.08, 0.10), depth, 'Result')
    depth.inputs['To Max'].default_value = 0.85

    # pebbles on the road and in the wet sand
    peb = n.new('ShaderNodeMapRange', in_From_Min=0.08, in_From_Max=0.0)
    n.link(vor, 'Distance', peb, 'Value')

    bsdf = principled(n, rough=0.85)
    n.link(c, 'Result', bsdf, 'Base Color')
    wr = n.new('ShaderNodeMapRange', in_To_Min=0.88, in_To_Max=0.35)
    n.link(sb, 'Blue', wr, 'Value')
    n.link(wr, 'Result', bsdf, 'Roughness')

    rip = n.new('ShaderNodeTexWave', wave_type='BANDS', bands_direction='Y', in_Scale=2.5, in_Distortion=7.0, in_Detail=2.0)
    n.link(tex, 'Object', rip, 'Vector')
    ripm = math_('MULTIPLY', rip, sa, ao='Fac', bo='Red')
    bump = n.new('ShaderNodeBump', in_Strength=0.35, in_Distance=0.05)
    hsum0 = math_('ADD', N3, N4, clamp=False, ao='Fac', bo='Fac')
    hsum = math_('MULTIPLY_ADD', ripm, 0.8, clamp=False)
    n.link(hsum0, 0, hsum, 2)
    n.link(hsum, 0, bump, 'Height')
    n.link(bump, 'Normal', bsdf, 'Normal')
    output(n, bsdf)
    return mat


def upsample(f, k):
    """Bilinear upsample of a field by an integer factor."""
    H, W = f.shape
    ys = np.linspace(0, H - 1, (H - 1) * k + 1)
    xs = np.linspace(0, W - 1, (W - 1) * k + 1)
    y0 = np.clip(ys.astype(int), 0, H - 2)
    x0 = np.clip(xs.astype(int), 0, W - 2)
    fy, fx = (ys - y0)[:, None], (xs - x0)[None, :]
    a, b = f[y0][:, x0], f[y0][:, x0 + 1]
    c, d = f[y0 + 1][:, x0], f[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


FOAM_K = 5   # foam picture: S*FOAM_K pixels per metre


def foam_image(F, name='foam'):
    """The surf, as a picture laid over the sea: lacy swash at the water's edge, a second fainter line behind it,
    and broken lines of foam where the waves crest further out."""
    sd = upsample(F['sd'], FOAM_K)
    H, W = sd.shape
    ppm = S * FOAM_K
    lace = fbm(H, W, ppm * 0.35, 21, 5)
    lace2 = fbm(H, W, ppm * 0.12, 22, 3)
    wob = (fbm(H, W, ppm * 2.0, 23, 3) - 0.5)
    water_edge = -0.45 + wob * 0.25
    d = sd - water_edge                     # 0 at the water's edge, negative out to sea
    swash = np.clip(1 - np.abs(d + 0.12) / 0.16, 0, 1) ** 1.5
    swash2 = np.clip(1 - np.abs(d + 0.55 + wob * 0.3) / 0.12, 0, 1) * 0.7
    holes = np.clip((lace - 0.38) * 4, 0, 1) * np.clip((lace2 - 0.3) * 3, 0, 1)
    crest = np.zeros_like(sd)
    for k, dist in enumerate((1.6, 2.9, 4.4)):
        line = np.clip(1 - np.abs(d + dist + wob * (0.6 + k * 0.3)) / (0.18 + 0.05 * k), 0, 1)
        broken = np.clip((fbm(H, W, ppm * 1.5, 30 + k, 3) - 0.45) * 5, 0, 1)
        crest = np.maximum(crest, line * broken * (0.5 - k * 0.13))
    film = np.clip((lace - 0.55) * 2, 0, 1) * np.clip(1 + d / 1.2, 0, 1) * 0.35
    f = np.maximum(np.maximum(swash, swash2) * holes, crest * holes) + film * (d < 0)
    f = np.clip(f, 0, 1) * (d < 0.02)
    img = bpy.data.images.new(name, W, H, alpha=True, float_buffer=True)
    px = np.zeros((H, W, 4), np.float32)
    px[..., :3] = 1
    px[..., 3] = f
    img.pixels.foreach_set(px[::-1].ravel())
    img.pack()
    return img


def build_water(F, coll=None):
    """The sea as a slab of water: a rippled top that bends light, with teal absorption below, plus a foam sheet."""
    TH, TW = F['TH'], F['TW']
    x0, x1, y0, y1 = -PAD - 30, TW - PAD + 2, -PAD - 2, TH - PAD + 2
    bm = bmesh.new()
    top = [bm.verts.new(place(x, y, WATER)) for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]
    bot = [bm.verts.new(place(x, y, WATER) + place(0, 0, -3) - place(0, 0, 0)) for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    for i in range(4):
        bm.faces.new((top[(i + 1) % 4], top[i], bot[i], bot[(i + 1) % 4]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new('sea')
    bm.to_mesh(me)
    bm.free()

    mat = bpy.data.materials.new('sea')
    n = Nodes(mat)
    tex = n.new('ShaderNodeTexCoord')
    w1 = n.new('ShaderNodeTexNoise', in_Scale=0.6, in_Detail=4.0, in_Distortion=1.5)
    w2 = n.new('ShaderNodeTexVoronoi', in_Scale=2.2, feature='SMOOTH_F1')
    n.link(tex, 'Object', w1, 'Vector')
    n.link(tex, 'Object', w2, 'Vector')
    add = n.new('ShaderNodeMath', operation='ADD')
    n.link(w1, 'Fac', add, 0)
    n.link(w2, 'Distance', add, 1)
    bump = n.new('ShaderNodeBump', in_Strength=0.2, in_Distance=0.06)
    n.link(add, 0, bump, 'Height')
    bsdf = principled(n, (0.62, 0.85, 0.82), 0.06, IOR=1.33)
    n.inp(bsdf, 'Transmission Weight').default_value = 1.0
    n.link(bump, 'Normal', bsdf, 'Normal')
    vol = n.new('ShaderNodeVolumeAbsorption', in_Density=2.4)
    n.inp(vol, 'Color').default_value = (*hexc('#3aa6b8'), 1)
    o = output(n, bsdf)
    n.link(vol, 0, o, 'Volume')
    me.materials.append(mat)
    sea = link(bpy.data.objects.new('sea', me), coll)
    sea.visible_shadow = False   # let the sun reach the sea bed (no caustics on the CPU)

    # foam: a sheet just above the water, white where the picture says so
    img = foam_image(F)
    bm = bmesh.new()
    Hs, Ws = F['H'], F['W']
    corners = [(-PAD, -PAD, 0, 1), (TW - PAD, -PAD, 1, 1), (TW - PAD, TH - PAD, 1, 0), (-PAD, TH - PAD, 0, 0)]
    vs = [bm.verts.new(place(x, y, WATER + 0.01)) for x, y, _, _ in corners]
    f = bm.faces.new(vs)
    uv = bm.loops.layers.uv.new('UVMap')
    for l, c in zip(f.loops, corners): l[uv].uv = (c[2], c[3])
    me = bpy.data.meshes.new('foam')
    bm.to_mesh(me)
    bm.free()
    fm = bpy.data.materials.new('foam')
    n = Nodes(fm)
    it = n.new('ShaderNodeTexImage', image=img, interpolation='Cubic')
    tr = n.new('ShaderNodeBsdfTransparent')
    wh = principled(n, (0.92, 0.95, 0.95), 0.5)
    mx = n.new('ShaderNodeMixShader')
    n.link(it, 'Alpha', mx, 'Fac')
    n.link(tr, 0, mx, 1)
    n.link(wh, 0, mx, 2)
    output(n, mx)
    me.materials.append(fm)
    link(bpy.data.objects.new('foam', me), coll)
    return sea


def sky_and_sun(elev=42, azim_deg=-35, strength=3.4):
    """A real sky for the fill light and a sun from the upper left of the screen (north-west)."""
    sc = bpy.context.scene
    world = bpy.data.worlds.new('sky')
    sc.world = world
    n = Nodes(world)
    sky = n.new('ShaderNodeTexSky', sky_type='MULTIPLE_SCATTERING')
    try:
        sky.sun_elevation = math.radians(elev)
        sky.sun_rotation = math.radians(150)
        sky.sun_disc = False
    except Exception:
        pass
    bg = n.new('ShaderNodeBackground', in_Strength=0.13)
    n.link(sky, 0, bg, 'Color')
    o = n.new('ShaderNodeOutputWorld')
    n.link(bg, 0, o, 'Surface')

    ld = bpy.data.lights.new('sun', 'SUN')
    ld.energy = strength
    ld.angle = math.radians(2.0)
    ld.color = hexc('#fff1dc')
    sun = bpy.data.objects.new('sun', ld)
    link(sun)
    from mathutils import Vector
    d = Vector((0.62, -0.42, -math.tan(math.radians(elev)) * 0.75)).normalized()
    sun.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    return sun
