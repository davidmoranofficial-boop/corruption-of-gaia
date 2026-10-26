"""Shared bits for the 3D opening (25 Oct): the camera maths, the map, noise and small Blender helpers.

The world is drawn from above at an angle, like a painted old-school RPG. One map tile is one metre.
The camera looks north and down at ELEV degrees. So that every tile still lands on exactly one tile of the
screen (walking and collisions stay the same as the 2D game), the world is laid out "sheared":
a point at tile (x, y) and height z sits at Blender (x, -(y + z*sin a)/cos a, z), where a = 90 - ELEV.
Things that stand up (trees, houses, people) are real 3D models placed at their base point.
"""
import json
import math
import os
import random

import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DATA = os.path.join(ROOT, 'Assets', 'Game', 'Resources', 'Data', 'data.json')
OUT = os.path.join(ROOT, 'Assets', 'Game', 'Resources', 'Sprites', 'HD')

ELEV = 50.0                       # camera elevation above the horizon, degrees
ALPHA = math.radians(90 - ELEV)   # camera tilt from straight down
SA, CA = math.sin(ALPHA), math.cos(ALPHA)


def place(x, y, z=0.0):
    """Tile coordinates (x right, y down the map, z up, in metres) to a Blender location."""
    return Vector((x, -(y + z * SA) / CA, z))


def load_area(aid):
    d = json.load(open(DATA))
    return d, next(a for a in d['areas'] if a['id'] == aid)


# ------------------------------------------------------------------ noise (numpy)

def vnoise(h, w, cell, seed):
    rnd = np.random.RandomState(seed)
    g = rnd.rand(int(h / cell) + 3, int(w / cell) + 3)
    ys, xs = np.arange(h) / cell, np.arange(w) / cell
    y0, x0 = ys.astype(int), xs.astype(int)
    fy, fx = ys - y0, xs - x0
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    a, b, c, d = g[y0][:, x0], g[y0][:, x0 + 1], g[y0 + 1][:, x0], g[y0 + 1][:, x0 + 1]
    top, bot = a + (b - a) * fx[None, :], c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]


def fbm(h, w, cell, seed, octaves=4):
    out, amp, tot = np.zeros((h, w)), 1.0, 0.0
    for o in range(octaves):
        out += amp * vnoise(h, w, max(1.0, cell / (2 ** o)), seed + o * 17)
        tot += amp
        amp *= 0.5
    return out / tot


def box(f, r):
    """Soft blur, radius r samples."""
    r = int(r)
    if r <= 0: return f
    for _ in range(2):
        for ax in (0, 1):
            pad = [(r + 1, r) if a == ax else (0, 0) for a in (0, 1)]
            c = np.cumsum(np.pad(f, pad, mode='edge'), axis=ax)
            f = (c[2 * r + 1:, :] - c[:-2 * r - 1, :]) if ax == 0 else (c[:, 2 * r + 1:] - c[:, :-2 * r - 1])
            f = f / (2 * r + 1)
    return f


def distance(mask):
    """Distance (in samples) from every cell to the nearest True cell, chamfer 3-4."""
    H, W = mask.shape
    INF = 10 ** 9
    d = np.where(mask, 0, INF).astype(np.int64)
    for y in range(H):
        row = d[y]
        if y > 0:
            up = d[y - 1]
            row = np.minimum(row, up + 3)
            row[1:] = np.minimum(row[1:], up[:-1] + 4)
            row[:-1] = np.minimum(row[:-1], up[1:] + 4)
        for x in range(1, W):
            if row[x - 1] + 3 < row[x]: row[x] = row[x - 1] + 3
        d[y] = row
    for y in range(H - 1, -1, -1):
        row = d[y]
        if y < H - 1:
            dn = d[y + 1]
            row = np.minimum(row, dn + 3)
            row[1:] = np.minimum(row[1:], dn[:-1] + 4)
            row[:-1] = np.minimum(row[:-1], dn[1:] + 4)
        for x in range(W - 2, -1, -1):
            if row[x + 1] + 3 < row[x]: row[x] = row[x + 1] + 3
        d[y] = row
    return d / 3.0


# ------------------------------------------------------------------ blender helpers

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def link(obj, coll=None):
    (coll or bpy.context.scene.collection).objects.link(obj)
    return obj


def collection(name):
    c = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if c.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(c)
    return c


def mesh_obj(name, bm, coll=None, mat=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if mat is not None:
        if isinstance(mat, (list, tuple)):
            for m in mat: me.materials.append(m)
        else:
            me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    return link(ob, coll)


def smooth(ob):
    for p in ob.data.polygons: p.use_smooth = True


class Nodes:
    """Tiny builder for shader node trees: n = Nodes(mat); n.new('ShaderNodeMix', ...); n.link(a, 'Out', b, 'In')."""

    def __init__(self, mat):
        mat.use_nodes = True
        self.t = mat.node_tree
        self.t.nodes.clear()
        self.x = 0

    def new(self, kind, **props):
        nd = self.t.nodes.new(kind)
        nd.location = (self.x, 0)
        self.x += 200
        for k, v in props.items():
            if k.startswith('in_'):
                key = k[3:].replace('_', ' ')
                self.inp(nd, key).default_value = v
            else:
                setattr(nd, k, v)
        return nd

    ALIAS = {'Fac': 'Factor', 'Factor': 'Fac'}

    @staticmethod
    def inp(nd, key):
        if isinstance(key, int): return nd.inputs[key]
        if key not in [s.name for s in nd.inputs] and key in Nodes.ALIAS: key = Nodes.ALIAS[key]
        for s in nd.inputs:
            if s.name == key and s.enabled: return s
        for s in nd.inputs:
            if s.name == key: return s
        raise KeyError('%s has no input %s: %s' % (nd.bl_idname, key, [s.name for s in nd.inputs]))

    @staticmethod
    def out(nd, key):
        if isinstance(key, int): return nd.outputs[key]
        if key not in [s.name for s in nd.outputs] and key in Nodes.ALIAS: key = Nodes.ALIAS[key]
        for s in nd.outputs:
            if s.name == key and s.enabled: return s
        for s in nd.outputs:
            if s.name == key: return s
        raise KeyError('%s has no output %s: %s' % (nd.bl_idname, key, [s.name for s in nd.outputs]))

    def link(self, a, ao, b, bi):
        self.t.links.new(self.out(a, ao), self.inp(b, bi))


def principled(n, color=None, rough=0.6, **kw):
    p = n.new('ShaderNodeBsdfPrincipled')
    if color is not None: n.inp(p, 'Base Color').default_value = (*color, 1)
    n.inp(p, 'Roughness').default_value = rough
    for k, v in kw.items(): n.inp(p, k.replace('_', ' ')).default_value = v
    return p


def output(n, shader, disp=None):
    o = n.new('ShaderNodeOutputMaterial')
    n.link(shader, 0, o, 'Surface')
    if disp is not None: n.link(disp, 0, o, 'Displacement')
    return o


def hexc(h):
    """'#rrggbb' to a linear colour tuple."""
    h = h.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c)


def ramp(n, stops, fac_from=None, fac_out=None):
    """A colour ramp with (pos, '#hex') stops."""
    r = n.new('ShaderNodeValToRGB')
    el = r.color_ramp.elements
    while len(el) > 1: el.remove(el[-1])
    el[0].position = stops[0][0]
    el[0].color = (*hexc(stops[0][1]), 1)
    for p, c in stops[1:]:
        e = el.new(p)
        e.color = (*hexc(c), 1)
    if fac_from is not None: n.link(fac_from, fac_out or 0, r, 'Fac')
    return r
