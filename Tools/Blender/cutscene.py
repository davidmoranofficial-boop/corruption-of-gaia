"""The opening cutscene, filmed in 3D: the trade boat in the storm, the wave, the beach at dawn, Yenson's shack.

Frames go to Sprites/HD/Cutscene/<shot>_<nnn>.jpg at 640 x 360; GameUI.Cutscene plays them in time with the
captions and sounds it already had (the shot timings below match its constants).

    python cutscene.py storm beach shack        (ONLY=first,last frame numbers to render a few; SPP samples)
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix, Euler, noise

import common
import props
import character
from common import Nodes, principled, output, hexc, ramp, link
from props import cyl, cube, finish, wood, plain

W, H = 640, 360
OUT = os.path.join(common.OUT, 'Cutscene')
SPP = int(os.environ.get('SPP', 16))

# shot timings, matching GameUI.Cutscene (seconds)
STORM_FPS, STORM_LEN = 8, 16.5          # storm until the wave has swallowed everything
LIGHTNING = [3.9, 9.4, 13.3, 14.9]
BEACH_FPS, BEACH_LEN = 10, 14.0
SHACK_FPS, SHACK_LEN = 10, 12.0


def setup(spp=SPP):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = spp
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 4
    sc.cycles.caustics_reflective = False
    sc.cycles.caustics_refractive = False
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'JPEG'
    sc.render.image_settings.quality = 88
    sc.view_settings.view_transform = 'AgX'
    try: sc.view_settings.look = 'AgX - Medium High Contrast'
    except Exception: pass
    return sc


def camera(loc, look, lens=35):
    cd = bpy.data.cameras.new('cam')
    cd.lens = lens
    cd.clip_end = 2000
    cam = bpy.data.objects.new('cam', cd)
    link(cam)
    cam.location = loc
    cam.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam
    return cam


def world(colour_top, colour_horizon, strength=1.0, fog=None):
    sc = bpy.context.scene
    wd = bpy.data.worlds.new('w')
    sc.world = wd
    n = Nodes(wd)
    tc = n.new('ShaderNodeTexCoord')
    sep = n.new('ShaderNodeSeparateXYZ')
    n.link(tc, 'Generated', sep, 'Vector')
    nz = n.new('ShaderNodeTexNoise', in_Scale=3.0, in_Detail=8.0, in_Roughness=0.6)
    n.link(tc, 'Generated', nz, 'Vector')
    mm = n.new('ShaderNodeMapRange', in_From_Min=0.0, in_From_Max=0.6)
    n.link(sep, 'Z', mm, 'Value')
    r = n.new('ShaderNodeMix', data_type='RGBA')
    n.inp(r, 'A').default_value = (*hexc(colour_horizon), 1)
    n.inp(r, 'B').default_value = (*hexc(colour_top), 1)
    n.link(mm, 'Result', r, 'Factor')
    # cloud breakup
    m2 = n.new('ShaderNodeMix', data_type='RGBA', blend_type='MULTIPLY')
    n.link(r, 'Result', m2, 'A')
    cr = ramp(n, [(0.3, '#6a6a6a'), (0.7, '#ffffff')], nz, 'Fac')
    n.link(cr, 0, m2, 'B')
    m2.inputs[0].default_value = 0.4
    bg = n.new('ShaderNodeBackground', in_Strength=strength)
    n.link(m2, 'Result', bg, 'Color')
    o = n.new('ShaderNodeOutputWorld')
    n.link(bg, 0, o, 'Surface')
    return wd


def dawn(elev=9.0, toward=(0.25, -1.0), strength=0.35, sun_energy=3.2):
    """A physical dawn sky with the sun low over the sea ahead of the camera, and the sun lamp to match."""
    sc = bpy.context.scene
    wd = bpy.data.worlds.new('dawn')
    sc.world = wd
    n = Nodes(wd)
    sky = n.new('ShaderNodeTexSky', sky_type='MULTIPLE_SCATTERING')
    sky.sun_elevation = math.radians(elev)
    # the sun sits opposite the way its light travels
    sky.sun_rotation = math.atan2(-toward[0], -toward[1])
    sky.sun_disc = True
    bg = n.new('ShaderNodeBackground', in_Strength=strength)
    n.link(sky, 0, bg, 'Color')
    o = n.new('ShaderNodeOutputWorld')
    n.link(bg, 0, o, 'Surface')
    d = Vector((toward[0], toward[1], -math.tan(math.radians(elev)) * math.hypot(*toward)))
    return sun(d, '#ffc890', sun_energy, 1.5)


def sun(direction, colour, energy, angle=3.0):
    ld = bpy.data.lights.new('sun', 'SUN')
    ld.energy = energy
    ld.color = hexc(colour)
    ld.angle = math.radians(angle)
    ob = bpy.data.objects.new('sun', ld)
    link(ob)
    ob.rotation_euler = Vector(direction).normalized().to_track_quat('-Z', 'Y').to_euler()
    return ob


def sea_material(deep, shallow, foam_amt=0.0):
    m = bpy.data.materials.new('sea')
    n = Nodes(m)
    p = principled(n, hexc(deep), 0.08)
    n.inp(p, 'Transmission Weight').default_value = 0.2
    # foam on the crests: the ocean modifier writes a foam attribute
    at = n.new('ShaderNodeAttribute', attribute_name='foam')
    fr = n.new('ShaderNodeMapRange', in_From_Min=0.3, in_From_Max=1.0, in_To_Max=foam_amt)
    n.link(at, 'Fac', fr, 'Value')
    fw = principled(n, (0.85, 0.9, 0.92), 0.7)
    mx = n.new('ShaderNodeMixShader')
    n.link(fr, 'Result', mx, 'Fac')
    n.link(p, 0, mx, 1)
    n.link(fw, 0, mx, 2)
    output(n, mx)
    return m


def ocean(size=120, res=12, scale=2.0, chop=1.4, wind=22.0, foam=True, mat=None, loc=(0, 0, 0)):
    me = bpy.data.meshes.new('ocean')
    me.from_pydata([(0, 0, 0)], [], [])
    ob = bpy.data.objects.new('ocean', me)
    link(ob)
    m = ob.modifiers.new('ocean', 'OCEAN')
    m.geometry_mode = 'GENERATE'
    m.size = 1.0
    m.spatial_size = size
    m.resolution = res
    m.wave_scale = scale
    m.choppiness = chop
    m.wind_velocity = wind
    m.repeat_x = 2
    m.repeat_y = 2
    if foam:
        m.use_foam = True
        m.foam_layer_name = 'foam'
        m.foam_coverage = -0.45
    ob.location = Vector(loc) - Vector((size, size, 0))
    if mat: me.materials.append(mat)
    return ob, m


# ------------------------------------------------------------------ the trade boat

def ship(coll):
    """A small single-masted trading cog: clinker hull, a deck, a stern castle, a square sail half torn, lanterns."""
    rnd = random.Random(2)
    hullw = wood('cog_hull', '#3e2c1c', '#624a36', '#7e6448', 'X', 3.0)
    deckw = wood('cog_deck', '#4a3a28', '#6a5440', '#84705a', 'X', 6.0)
    sailm = props.cloth_mat()
    rope = plain('cog_rope', '#6a5a40', 0.9, 30, 0.3)
    lamp = character.flat_mat('lamp', '#ffb050', 0.4, emit=25)
    iron = plain('cog_iron', '#22201e', 0.5, 20, 0.2, 0.6)
    bm = bmesh.new()
    L, B, D = 9.0, 3.4, 2.0

    def sec(u, a):
        width = B / 2 * (math.sin(math.pi * min(max(u, 0.03), 0.97)) ** 0.55)
        depth = D * (0.75 + 0.25 * math.sin(math.pi * u))
        ang = a * math.pi / 2
        return Vector(((u - 0.5) * L, math.sin(ang) * width, (1 - math.cos(ang)) * depth * 0.95 - D + 0.35 * (2 * u - 1) ** 2 * 2))

    ns = 10
    for s in range(ns):
        a0, a1 = -1 + 2 * s / ns, -1 + 2 * (s + 1) / ns + 0.03
        for i in range(16):
            u0, u1 = i / 16, (i + 1) / 16
            c = [sec(u0, a0), sec(u1, a0), sec(u1, a1), sec(u0, a1)]
            v = [bm.verts.new(p) for p in c]
            vi = [bm.verts.new(p * 0.97) for p in c]
            bm.faces.new(v).material_index = 0
            bm.faces.new(list(reversed(vi))).material_index = 0
    # deck
    for i in range(16):
        u0, u1 = i / 16, (i + 1) / 16
        c = [sec(u0, -1), sec(u1, -1), sec(u1, 1), sec(u0, 1)]
        for p in c: p.z -= 0.25
        bm.faces.new([bm.verts.new(p) for p in c]).material_index = 1
    # rails
    for side in (-1, 1):
        pts = [sec(i / 16, side) + Vector((0, 0, 0.35)) for i in range(17)]
        for p0, p1 in zip(pts, pts[1:]): cyl(bm, p0, p1, 0.06, 6, 0)
    # stern castle
    cube(bm, (L * 0.36, 0, 0.6), (1.8, B * 0.75, 1.4), 0)
    cube(bm, (L * 0.36, 0, 1.35), (2.0, B * 0.8, 0.12), 1)
    # mast, yard, sail
    mast_top = Vector((-0.4, 0, 9.5))
    cyl(bm, (-0.4, 0, -1.5), mast_top, 0.18, 10, 0, r2=0.1)
    yard0, yard1 = Vector((-0.4, -3.4, 8.3)), Vector((-0.4, 3.4, 8.3))
    cyl(bm, yard0, yard1, 0.09, 8, 0)
    cyl(bm, (L / 2 - 0.2, 0, 0.2), (L / 2 + 2.6, 0, 1.4), 0.1, 8, 0)    # bowsprit
    # sail: a billowing sheet with a ragged tear
    rows_s, cols_s = 10, 12
    grid = []
    for i in range(rows_s + 1):
        row = []
        for j in range(cols_s + 1):
            t, d = j / cols_s, i / rows_s
            p = yard0.lerp(yard1, t)
            billow = math.sin(math.pi * t) * math.sin(math.pi * min(1, d * 1.1)) * 1.3
            q = p + Vector((-billow - 0.1, 0, -5.6 * d))
            q.x += 0.12 * noise.noise(Vector((i * 0.6, j * 0.6, 1)))
            row.append(bm.verts.new(q))
        grid.append(row)
    for i in range(rows_s):
        for j in range(cols_s):
            if i > 5 and 7 <= j <= 9 and rnd.random() < 0.8: continue
            f = bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
            f.material_index = 2
            f.smooth = True
    # shrouds and stays
    for side in (-1, 1):
        for k in range(3):
            cyl(bm, mast_top + Vector((0, 0, -0.5 - k * 0.3)), sec(0.45 + k * 0.05, side), 0.025, 4, 3)
    cyl(bm, mast_top, Vector((L / 2 + 2.5, 0, 1.4)), 0.03, 4, 3)
    cyl(bm, mast_top, Vector((L * 0.45, 0, 1.4)), 0.03, 4, 3)
    # lanterns at the stern and the bow
    for p in (Vector((L * 0.45, 0, 2.0)), Vector((-L * 0.45, 0, 1.0))):
        cube(bm, p, (0.25, 0.25, 0.35), 4)
        cube(bm, p + Vector((0, 0, 0.22)), (0.3, 0.3, 0.06), 5)
    ob = finish('ship', bm, [hullw, deckw, sailm, rope, lamp, iron], coll)
    for p in (Vector((L * 0.45, 0, 2.0)), Vector((-L * 0.45, 0, 1.0))):
        ld = bpy.data.lights.new('lantern', 'POINT')
        ld.energy = 120
        ld.color = hexc('#ffa040')
        lo = bpy.data.objects.new('lantern', ld)
        lo.parent = ob
        lo.location = p
        link(lo, coll)
    return ob


def rain(cam, n=900, depth=(4, 30), seed=1):
    """Streaks of rain in front of the camera, as a mesh that moves down a little each frame."""
    rs = random.Random(seed)
    bm = bmesh.new()
    for i in range(n):
        d = rs.uniform(*depth)
        x = rs.uniform(-1, 1) * d * 0.9
        y = rs.uniform(-1, 1) * d * 0.55
        ln = rs.uniform(0.6, 1.4) * (0.4 + d * 0.03)
        w = 0.004 + d * 0.0008
        c = Vector((x, y, -d))
        sl = Vector((0.18, 1, 0)).normalized() * ln
        v = [bm.verts.new(c - Vector((w, 0, 0))), bm.verts.new(c + Vector((w, 0, 0))), bm.verts.new(c + Vector((w, 0, 0)) + sl), bm.verts.new(c - Vector((w, 0, 0)) + sl)]
        bm.faces.new(v)
    me = bpy.data.meshes.new('rain')
    bm.to_mesh(me)
    bm.free()
    m = bpy.data.materials.new('rain')
    n = Nodes(m)
    tr = n.new('ShaderNodeBsdfTransparent')
    em = n.new('ShaderNodeEmission', in_Strength=0.3)
    n.inp(em, 'Color').default_value = (0.7, 0.75, 0.8, 1)
    mx = n.new('ShaderNodeMixShader', in_Fac=0.16)
    n.link(tr, 0, mx, 1)
    n.link(em, 0, mx, 2)
    output(n, mx)
    me.materials.append(m)
    ob = bpy.data.objects.new('rain', me)
    link(ob)
    ob.parent = cam
    ob.visible_shadow = False
    return ob


def bolt(start, end, seed=3):
    rs = random.Random(seed)
    bm = bmesh.new()
    pts = [Vector(start)]
    for i in range(1, 14):
        t = i / 14
        p = Vector(start).lerp(Vector(end), t) + Vector((rs.uniform(-1, 1) * 4, rs.uniform(-1, 1) * 2, 0))
        pts.append(p)
    pts.append(Vector(end))
    for a, b in zip(pts, pts[1:]): cyl(bm, a, b, 0.25, 5, 0)
    for k in range(3):
        i = rs.randint(3, 10)
        a = pts[i]
        b = a + Vector((rs.uniform(-8, 8), rs.uniform(-3, 3), -rs.uniform(6, 14)))
        cyl(bm, a, b, 0.12, 4, 0)
    m = character.flat_mat('bolt', '#e8f0ff', 0.5, emit=60)
    return finish('bolt', bm, [m], None)


# ------------------------------------------------------------------ shots

def shot_storm(frames=None):
    common.reset()
    sc = setup()
    world('#0c1118', '#2a3440', 0.9)
    moon = sun((0.4, 0.8, -0.35), '#9fb4d0', 1.2, 8)
    seam = sea_material('#06100f', '#1a3a3c', 0.8)
    oc, om = ocean(size=160, res=12, scale=1.8, chop=1.3, wind=28, mat=seam, loc=(0, 40, 0))
    boat = ship(bpy.context.scene.collection)
    cam = camera((-19, -22, 5.0), (0, 0, 3.5), 30)
    rn = rain(cam)
    # the giant wave: a curling sheet that comes in from the right at the end
    wave = wave_mesh()
    # lightning: a bright flash light and the bolt, only on their frames
    flash = sun((-0.3, 0.6, -0.7), '#dfe8ff', 0.0, 2)
    bl = bolt((40, 120, 70), (25, 90, 0))
    n_frames = int(STORM_LEN * STORM_FPS)
    for f in range(n_frames):
        if frames and not (frames[0] <= f <= frames[1]): continue
        t = f / STORM_FPS
        om.time = 10 + t * 0.9
        heave = math.sin(t * 1.3 + 0.6) * 0.9 + math.sin(t * 2.9) * 0.3
        roll = math.radians(math.sin(t * 1.1) * 9 + math.sin(t * 2.3 + 1) * 3)
        pitch = math.radians(math.sin(t * 0.9 + 0.3) * 5)
        if t > 13.5: roll += math.radians((t - 13.5) * 22)
        boat.location = (0, 0, 1.9 + heave + (-(t - 14) * 1.5 if t > 14 else 0))
        boat.rotation_euler = (roll, pitch, math.radians(-25))
        rn.location = (0, -((t * 14) % 6), 0)
        lit = 0.0
        for at in LIGHTNING:
            d = t - at
            if 0 <= d < 0.3: lit = max(lit, 1.0 if d < 0.15 else 0.4)
        flash.data.energy = 18.0 * lit
        bl.hide_render = lit < 0.9
        w = min(max((t - 13.4) / 2.2, 0), 1)
        ws = w * w * (3 - 2 * w)
        wave.hide_render = t < 13.4
        # it rises out of the sea behind the ship and comes at the camera, curling over the boat
        away = Vector((19, 22, 0)).normalized()
        wave.location = away * (42 - 24 * ws) + Vector((0, 0, -16 + 15 * ws))
        wave.rotation_euler = (0, 0, math.atan2(away.y, away.x))
        sc.render.filepath = os.path.join(OUT, 'storm_%03d.jpg' % f)
        bpy.ops.render.render(write_still=True)
        print('frame storm', f)


def wave_mesh():
    """A breaking wave: a tall curved wall of water with a white curling lip."""
    bm = bmesh.new()
    rows, cols = 14, 30
    grid = []
    for i in range(rows + 1):
        row = []
        for j in range(cols + 1):
            t = i / rows
            y = (j / cols - 0.5) * 120
            ang = t * math.pi * 1.15
            r = 9
            x = -math.sin(ang) * r * (0.6 + 0.4 * t) - (1 - t) * 12
            z = (1 - math.cos(ang)) * r * 0.9
            x += 0.6 * noise.noise(Vector((i * 0.5, j * 0.4, 0.3)))
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for i in range(rows):
        for j in range(cols):
            f = bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
            f.smooth = True
            f.material_index = 1 if i >= rows - 4 else 0
    m0 = bpy.data.materials.new('wave')
    n = Nodes(m0)
    # streaks of foam dragged up the face, and a rough, rippled surface
    tc = n.new('ShaderNodeTexCoord')
    mp = n.new('ShaderNodeMapping')
    n.inp(mp, 'Scale').default_value = (0.35, 0.05, 0.6)
    n.link(tc, 'Object', mp, 'Vector')
    st = n.new('ShaderNodeTexNoise', in_Scale=3.0, in_Detail=10.0, in_Roughness=0.65)
    n.link(mp, 0, st, 'Vector')
    col = ramp(n, [(0.5, '#0e3a40'), (0.62, '#2a6a70'), (0.7, '#b8cccc')], st, 'Fac')
    p = principled(n, rough=0.15)
    n.link(col, 0, p, 'Base Color')
    n.inp(p, 'Transmission Weight').default_value = 0.15
    rip = n.new('ShaderNodeTexNoise', in_Scale=6.0, in_Detail=8.0)
    n.link(tc, 'Object', rip, 'Vector')
    bp = n.new('ShaderNodeBump', in_Strength=0.5)
    n.link(rip, 'Fac', bp, 'Height')
    n.link(bp, 0, p, 'Normal')
    output(n, p)
    m1 = bpy.data.materials.new('wavefoam')
    n = Nodes(m1)
    tc = n.new('ShaderNodeTexCoord')
    nz = n.new('ShaderNodeTexNoise', in_Scale=2.0, in_Detail=8.0)
    n.link(tc, 'Object', nz, 'Vector')
    c = ramp(n, [(0.3, '#7a9a9a'), (0.6, '#e8f0f0')], nz, 'Fac')
    p = principled(n, rough=0.6)
    n.link(c, 0, p, 'Base Color')
    output(n, p)
    return finish('bigwave', bm, [m0, m1], None)


def beach_set(coll, sun_dir, shack_too=False):
    """Sand, a sea that washes in and out, debris from the wreck."""
    # sand: a big gently sloping plane, rippled
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=80, y_segments=80, size=60)
    for v in bm.verts:
        x, y = v.co.x, v.co.y
        v.co.z = -0.05 * y + 0.08 * noise.noise(Vector((x * 0.3, y * 0.3, 0))) + (0.4 * max(0, -y - 8) * 0 )
    me = bpy.data.meshes.new('sand')
    bm.to_mesh(me)
    bm.free()
    m = bpy.data.materials.new('sand')
    n = Nodes(m)
    tc = n.new('ShaderNodeTexCoord')
    nz = n.new('ShaderNodeTexNoise', in_Scale=4.0, in_Detail=8.0)
    n.link(tc, 'Object', nz, 'Vector')
    geo = n.new('ShaderNodeNewGeometry')
    sp = n.new('ShaderNodeSeparateXYZ')
    n.link(geo, 'Position', sp, 'Vector')
    wet = n.new('ShaderNodeMapRange', in_From_Min=-0.05, in_From_Max=0.25)
    n.link(sp, 'Z', wet, 'Value')
    c1 = ramp(n, [(0.0, '#6e5c44'), (1.0, '#8a7656')], nz, 'Fac')
    c2 = ramp(n, [(0.0, '#b8a07a'), (1.0, '#d4bf96')], nz, 'Fac')
    mx = n.new('ShaderNodeMix', data_type='RGBA')
    n.link(c1, 0, mx, 'A')
    n.link(c2, 0, mx, 'B')
    n.link(wet, 'Result', mx, 'Factor')
    p = principled(n, rough=0.8)
    n.link(mx, 'Result', p, 'Base Color')
    rr = n.new('ShaderNodeMapRange', in_To_Min=0.25, in_To_Max=0.9)
    n.link(wet, 'Result', rr, 'Value')
    n.link(rr, 'Result', p, 'Roughness')
    rip = n.new('ShaderNodeTexWave', wave_type='BANDS', bands_direction='Y', in_Scale=3.0, in_Distortion=6.0)
    n.link(tc, 'Object', rip, 'Vector')
    bp = n.new('ShaderNodeBump', in_Strength=0.25)
    n.link(rip, 'Fac', bp, 'Height')
    n.link(bp, 'Normal', p, 'Normal')
    output(n, p)
    me.materials.append(m)
    for pl in me.polygons: pl.use_smooth = True
    sand = bpy.data.objects.new('sand', me)
    link(sand, coll)
    # the sea: a clear sheet over the low sand, its edge moving in and out
    seam = bpy.data.materials.new('shallows')
    n = Nodes(seam)
    tc = n.new('ShaderNodeTexCoord')
    nz = n.new('ShaderNodeTexNoise', in_Scale=1.5, in_Detail=5.0)
    n.link(tc, 'Object', nz, 'Vector')
    p = principled(n, (0.6, 0.85, 0.85), 0.05, IOR=1.33)
    n.inp(p, 'Transmission Weight').default_value = 1.0
    bp = n.new('ShaderNodeBump', in_Strength=0.2)
    n.link(nz, 'Fac', bp, 'Height')
    n.link(bp, 'Normal', p, 'Normal')
    vol = n.new('ShaderNodeVolumeAbsorption', in_Density=0.6)
    n.inp(vol, 'Color').default_value = (*hexc('#3aa6b8'), 1)
    o = output(n, p)
    n.link(vol, 0, o, 'Volume')
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * 160, v.co.y * 140 + 60, v.co.z * 2 - 1))
    me = bpy.data.meshes.new('water')
    bm.to_mesh(me)
    bm.free()
    me.materials.append(seam)
    water = bpy.data.objects.new('water', me)
    water.visible_shadow = False
    link(water, coll)
    # foam line riding the edge
    fm = bpy.data.materials.new('edgefoam')
    n = Nodes(fm)
    tc = n.new('ShaderNodeTexCoord')
    nz = n.new('ShaderNodeTexNoise', in_Scale=3.0, in_Detail=8.0)
    n.link(tc, 'Object', nz, 'Vector')
    sp = n.new('ShaderNodeSeparateXYZ')
    n.link(tc, 'Object', sp, 'Vector')
    band = n.new('ShaderNodeMapRange', in_From_Min=0.0, in_From_Max=1.0)
    n.link(sp, 'Y', band, 'Value')
    edge = n.new('ShaderNodeMath', operation='PINGPONG')
    n.link(band, 'Result', edge, 0)
    edge.inputs[1].default_value = 0.5
    lace = n.new('ShaderNodeMath', operation='MULTIPLY', use_clamp=True)
    n.link(edge, 0, lace, 0)
    n.link(nz, 'Fac', lace, 1)
    th = n.new('ShaderNodeMapRange', in_From_Min=0.2, in_From_Max=0.35)
    n.link(lace, 0, th, 'Value')
    tr = n.new('ShaderNodeBsdfTransparent')
    wh = principled(n, (0.9, 0.92, 0.92), 0.6)
    mx = n.new('ShaderNodeMixShader')
    n.link(th, 'Result', mx, 'Fac')
    n.link(tr, 0, mx, 1)
    n.link(wh, 0, mx, 2)
    output(n, mx)
    bm = bmesh.new()
    vs = [bm.verts.new(v) for v in ((-60, 0, 0), (60, 0, 0), (60, 1, 0), (-60, 1, 0))]
    bm.faces.new(vs)
    me = bpy.data.meshes.new('foam')
    bm.to_mesh(me)
    bm.free()
    me.materials.append(fm)
    foam = bpy.data.objects.new('foam', me)
    foam.visible_shadow = False
    link(foam, coll)
    # wreckage: planks, a barrel, rope
    rs = random.Random(5)
    import objects as OB
    for k in range(9):
        bm = bmesh.new()
        cube(bm, (0, 0, 0.04), (rs.uniform(1.0, 2.4), 0.22, 0.06), 0)
        pl = finish('plank%d' % k, bm, [wood('plank', '#3a2c1e', '#5a4430', '#6e5640', 'X', 5.0)], coll)
        pl.location = (rs.uniform(-12, 12), rs.uniform(-3, 7), 0.0)
        pl.location.z = -0.05 * pl.location.y + 0.02
        pl.rotation_euler = (0, 0, rs.uniform(0, 3.14))
    return sand, water, foam


def lying(fig, loc, yaw):
    """Face down on the sand, one arm flung out."""
    P = fig.arm.pose.bones
    fig.pose(0, 0)
    P['upper.L'].rotation_euler = (0.2, 0, 1.3)
    P['upper.R'].rotation_euler = (0.3, 0, 0.4)
    P['fore.L'].rotation_euler = (0.4, 0, 0)
    P['thigh.L'].rotation_euler = (-0.1, 0, -0.15)
    P['thigh.R'].rotation_euler = (0.05, 0, 0.12)
    P['shin.R'].rotation_euler = (0.15, 0, 0)
    P['neck'].rotation_euler = (0, 0, 0.6)
    fig.root.rotation_euler = (math.radians(88), 0, yaw)
    fig.root.location = loc


def shot_beach(frames=None):
    """Dawn. You lie in the wash; the tide runs up round you and back; Yenson comes along the sand and stops."""
    common.reset()
    sc = setup()
    coll = sc.collection
    sn = dawn()
    sand, water, foam = beach_set(coll, None)
    you = character.Figure('you', character.PRESETS['player_male'], coll)
    lying(you, Vector((0, 1.5, 0.06)), math.radians(80))
    yen = character.Figure('yen', character.PRESETS['npc_yenson'], coll)
    cam = camera((-3.2, -4.2, 1.5), (0.0, 1.8, 0.2), 30)
    n_frames = int(BEACH_LEN * BEACH_FPS)
    for f in range(n_frames):
        if frames and not (frames[0] <= f <= frames[1]): continue
        t = f / BEACH_FPS
        # the wash: the water's edge runs up past your legs and drains back
        edge = 0.5 + (math.sin(t * 1.2) * 0.5 + 0.5) * 2.2
        # the sand drops 5 cm a metre toward the sea, so a water surface at -0.05*edge meets it at y = edge
        water.location = (0, 0, -0.05 * edge)
        foam.location = (0, edge - 0.5, -0.05 * edge + 0.012)
        foam.scale = (1, 0.6 + 0.4 * math.sin(t * 2.4), 1)
        # Yenson: walks in from the left from 6.5 s, stops beside you, looks down
        walk_from, speed = 6.5, 1.25
        dist = max(0, t - walk_from) * speed
        stop = 7.4
        arrived = dist >= stop
        x = -9.0 + min(dist, stop)
        yen.root.location = (x, 1.0, -0.05 * 1.0 + 0.02)
        yen.root.rotation_euler = (0, 0, math.radians(-90 if not arrived or t < walk_from + stop / speed + 1.5 else -150))
        if t < walk_from:
            yen.root.location = (-20, 1, 0)
        if arrived:
            yen.pose(0, 0)
            yen.arm.pose.bones['neck'].rotation_euler = (0.5, 0, 0)
        else:
            yen.pose((dist / 1.5) % 1.0, 1.0)
        sc.render.filepath = os.path.join(OUT, 'beach_%03d.jpg' % f)
        bpy.ops.render.render(write_still=True)
        print('frame beach', f)


def shot_shack(frames=None):
    """He drags you up the sand to his shack by the shoulders, walking backwards, and in through the door."""
    common.reset()
    sc = setup()
    coll = sc.collection
    dawn(elev=13, toward=(0.55, -0.8))
    sand, water, foam = beach_set(coll, None)
    # up by the shack the ground is level and the sea is behind the camera
    water.hide_render = True
    foam.hide_render = True
    sand.rotation_euler.x = math.atan(0.05)
    for o in bpy.data.objects:
        if o.name.startswith('plank'): o.hide_render = True
    hut, chim = props.shack(coll, 5, 3, 2, seed=5)
    hut.location = Vector((3.0, 7.0, 0.0))
    hut.rotation_euler = (0, 0, 0)
    props.smoke(coll, hut.location + Vector(chim))
    import trees
    rs = random.Random(3)
    for k in range(9):
        trees.plant('oak' if k % 3 else 'pine', Vector((rs.uniform(-14, 18), rs.uniform(12, 22), 0)), rs, coll, 'tree%d' % k, rs.uniform(0.9, 1.2))
    # dune grass thickening toward the shack and the trees, and a few stones
    import scatter
    import numpy as np
    src = scatter.hidden('cut_src')
    gm_dune = scatter.grass_material('grass_dune', '#5b6a35', '#a4a467', '#c8bd86')
    gm_long = scatter.grass_material('grass_long', '#3f5a22', '#8fae4c', '#c9bf72')
    dunes = [scatter.tuft('tuft_d%d' % i, 14, 0.42, 0.08, 0.7, gm_dune, 20 + i, src) for i in range(2)]
    longs = [scatter.tuft('tuft_l%d' % i, 30, 0.55, 0.16, 0.5, gm_long, 10 + i, src) for i in range(2)]
    rg = np.random.RandomState(4)
    for gi, (srcs, n_pts, y0) in enumerate(((dunes, 2600, -1.0), (longs, 2200, 5.0))):
        p = np.c_[rg.uniform(-22, 26, n_pts), rg.uniform(y0, 26, n_pts), np.zeros(n_pts)]
        # denser further up the beach; keep the path from the sea to the door clear
        keep = rg.uniform(0, 1, n_pts) < np.clip((p[:, 1] - y0) / 9.0, 0, 1)
        lane = np.abs((p[:, 0] + 6.0) - (p[:, 1] + 2.0) * (11.35 / 9.0)) < 1.6
        p = p[keep & ~lane]
        for k, sobj in enumerate(srcs):
            sel = p[k::len(srcs)]
            scatter.instancer('cut_grass%d_%d' % (gi, k), sel, sobj, coll, scale=rg.uniform(0.8, 1.5, len(sel)), seed=gi * 3 + k)
    you = character.Figure('you', character.PRESETS['player_male'], coll)
    yen = character.Figure('yen', character.PRESETS['npc_yenson'], coll)
    cam = camera((-7.0, -9.0, 3.4), (2.5, 4.5, 1.2), 30)
    door = Vector((3.0 + 2.5 - 0.15 + 0.0, 7.0 + 0.0, 0))
    start = Vector((-6.0, -2.0, 0))
    n_frames = int(SHACK_LEN * SHACK_FPS)
    for f in range(n_frames):
        if frames and not (frames[0] <= f <= frames[1]): continue
        t = f / SHACK_FPS
        u = min(t / 9.6, 1.0)
        p = start.lerp(door, u)
        d = (door - start).normalized()
        yaw = math.atan2(d.y, d.x) - math.pi / 2 + math.pi       # walking backwards: facing back down the beach
        yen.root.location = p + Vector((0, 0, 0.0))
        yen.root.rotation_euler = (0, 0, yaw)
        yen.pose((-t * 0.55) % 1.0, 0.7)
        yen.arm.pose.bones['upper.L'].rotation_euler = (-1.0, 0, 0)
        yen.arm.pose.bones['upper.R'].rotation_euler = (-1.0, 0, 0)
        yen.arm.pose.bones['spine'].rotation_euler = (-0.35, 0, 0)
        # you, on your back, dragged by the shoulders, heels in the sand
        lying(you, p - d * 1.2 + Vector((0, 0, 0.1)), yaw + math.pi)
        you.root.rotation_euler = (math.radians(80), 0, yaw + math.pi / 2 * 0)
        inside = t > 9.6
        yen.root.hide_render = inside
        you.root.hide_render = inside
        for o in list(yen.arm.children) + list(you.arm.children): o.hide_render = inside
        sc.render.filepath = os.path.join(OUT, 'shack_%03d.jpg' % f)
        bpy.ops.render.render(write_still=True)
        print('frame shack', f)


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    os.makedirs(OUT, exist_ok=True)
    only = os.environ.get('ONLY')
    frames = tuple(int(x) for x in only.split(',')) if only else None
    for s in (args or ['storm', 'beach', 'shack']):
        {'storm': shot_storm, 'beach': shot_beach, 'shack': shot_shack}[s](frames)
