"""Builds an area in 3D and renders it for the game, in bands of tile rows.

    python build_area.py coast_road            (with Blender's Python: blender -b -P build_area.py -- coast_road)

Env: PPT pixels per tile (96), SPP samples (32), BAND rows per band (24), ONLY band index to render just one,
RAW folder for the renders (default Tools/Blender/raw/<area>).
For each band it writes beauty_<k>.png (the picture) and index_<k>.png (which tile row each pixel's object
stands on, 0 for the ground), then cut.py turns them into the game's pictures.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import common
import terrain
import populate
import render

args = [a for a in sys.argv[sys.argv.index('--') + 1:]] if '--' in sys.argv else sys.argv[1:]
AREA = args[0] if args else 'coast_road'
PPT = int(os.environ.get('PPT', 96))
SPP = int(os.environ.get('SPP', 32))
BAND = int(os.environ.get('BAND', 24))
ONLY = os.environ.get('ONLY')
RAW = os.environ.get('RAW', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'raw', AREA))
os.makedirs(RAW, exist_ok=True)

t0 = time.time()
common.reset()
d, a = common.load_area(AREA)
rows = a['rows']
W, H = len(rows[0]), len(rows)
if a.get('indoor'):
    import interior
    interior.build(rows)
    sun = terrain.sky_and_sun(strength=0.0)
    sun.hide_render = True
else:
    F = terrain.fields(rows)
    terrain.build_ground(F, terrain.ground_material())
    terrain.build_water(F)
    G = populate.Ground(F, rows)
    populate.populate(G)
    terrain.sky_and_sun()
SKY = 0.05 if a.get('indoor') else 0.13
sc = render.setup(SPP, PPT)
cam = render.camera()
print('built in %.1fs' % (time.time() - t0))

# the index picture: every surface glows with its object's pass index, nothing else
idx_mat = bpy.data.materials.new('index')
n = common.Nodes(idx_mat)
oi = n.new('ShaderNodeObjectInfo')
dv = n.new('ShaderNodeMath', operation='DIVIDE')
n.link(oi, 'Object Index', dv, 0)
dv.inputs[1].default_value = 255.0
em = n.new('ShaderNodeEmission')
n.link(dv, 0, em, 'Color')
common.output(n, em)

bands = [(y, min(H, y + BAND)) for y in range(0, H, BAND)]
for k, (y0, y1) in enumerate(bands):
    if ONLY is not None and str(k) not in ONLY.split(','): continue
    t = time.time()
    render.frame(cam, 0, y0, W, y1, PPT)
    vl = bpy.context.view_layer
    # the picture
    vl.material_override = None
    sc.cycles.samples = SPP
    sc.cycles.use_denoising = True
    sc.cycles.filter_width = 1.5
    sc.view_settings.view_transform = 'AgX'
    sc.world.node_tree.nodes['Background'].inputs['Strength'].default_value = SKY
    for o in bpy.data.objects:
        if o.name.startswith('smoke'): o.hide_render = False
    sc.render.image_settings.color_depth = '8'
    render.shoot(os.path.join(RAW, 'beauty_%d.png' % k))
    # the index
    vl.material_override = idx_mat
    sc.cycles.samples = 1
    sc.cycles.use_denoising = False
    sc.cycles.filter_width = 0.01
    sc.view_settings.view_transform = 'Raw' if 'Raw' in [x.identifier for x in sc.view_settings.bl_rna.properties['view_transform'].enum_items] else 'Standard'
    try: sc.view_settings.view_transform = 'Raw'
    except Exception: sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.0
    for o in bpy.data.objects:
        if o.name.startswith('smoke'): o.hide_render = True
    sc.render.image_settings.color_depth = '8'
    render.shoot(os.path.join(RAW, 'index_%d.png' % k))
    try: sc.view_settings.look = 'AgX - Medium High Contrast'
    except Exception: pass
    print('band %d (rows %d-%d) in %.0fs' % (k, y0, y1, time.time() - t))
open(os.path.join(RAW, 'bands.txt'), 'w').write('%d %d %d %d\n' % (W, H, PPT, BAND))
print('done in %.0fs' % (time.time() - t0))
