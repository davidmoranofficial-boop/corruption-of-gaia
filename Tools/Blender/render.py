"""Cameras and rendering for the 3D areas."""
import math
import os

import bpy
from mathutils import Vector

from common import place, ALPHA, SA, CA, link


def setup(samples=64, px_per_tile=96):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    try: sc.cycles.denoiser = 'OPENIMAGEDENOISE'
    except Exception: pass
    sc.cycles.max_bounces = 6
    sc.cycles.transmission_bounces = 6
    sc.cycles.volume_bounces = 1
    sc.cycles.caustics_reflective = False
    sc.cycles.caustics_refractive = False
    sc.cycles.film_exposure = 1.0
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.resolution_percentage = 100
    sc.view_settings.view_transform = 'AgX'
    try: sc.view_settings.look = 'AgX - Medium High Contrast'
    except Exception: pass
    sc['px_per_tile'] = px_per_tile
    return sc


def camera(name='cam'):
    cd = bpy.data.cameras.new(name)
    cd.type = 'ORTHO'
    cd.sensor_fit = 'HORIZONTAL'
    cd.clip_start = 0.1
    cd.clip_end = 400
    ob = bpy.data.objects.new(name, cd)
    link(ob)
    ob.rotation_euler = (ALPHA, 0, 0)
    bpy.context.scene.camera = ob
    return ob


def frame(cam, x0, y0, x1, y1, ppt):
    """Points the camera at map tiles x0..x1, y0..y1 (screen rows) and sizes the picture to ppt pixels per tile."""
    sc = bpy.context.scene
    w, h = x1 - x0, y1 - y0
    cam.data.ortho_scale = w
    sc.render.resolution_x = int(round(w * ppt))
    sc.render.resolution_y = int(round(h * ppt))
    centre = place((x0 + x1) / 2, (y0 + y1) / 2, 0)
    fwd = Vector((0, SA, -CA))
    cam.location = centre - fwd * 150


def shoot(path):
    sc = bpy.context.scene
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
