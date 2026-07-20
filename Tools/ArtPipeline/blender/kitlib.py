"""
kitlib — shared helpers for the F1-game procedural Blender asset factory.

Runs under headless Blender (the `bpy` PyPI module or a Blender `--background
--python` invocation). Every builder produces a game-ready modular asset that
satisfies the standards in Docs/ArtPipeline/BLENDER_PIPELINE.md:

  * metres, +Y forward / +Z up authored, applied transforms, sensible origins;
  * clean object names, reusable Principled-BSDF materials;
  * valid geometry (recalculated normals, no stray doubles);
  * LOD variants (decimated) named `<Name>_LOD1/_LOD2`;
  * a simplified convex collision proxy named `<Name>_COL`;
  * GLB export (the project interchange format) — one file per asset carrying
    LOD0..LODn + collision, which the Unity `GltfKitImporter` reassembles into
    an LODGroup + MeshCollider.

Deterministic: seeded RNG only, no wall-clock, no external assets. Nothing here
uses copyrighted geometry, real team/sponsor branding or ripped content — all
shapes are generated from primitives and first-principles maths.
"""

import bpy
import bmesh
import math
import random
import hashlib
from mathutils import Vector

# ----------------------------------------------------------------------------
# Scene / determinism
# ----------------------------------------------------------------------------

def seed_for(name):
    """Stable integer seed derived from an asset name (reproducible builds)."""
    return int(hashlib.sha256(name.encode("utf-8")).hexdigest()[:8], 16)


def reset_scene():
    """Empty factory scene — deterministic starting point."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # read_factory_settings wipes all datablocks; drop stale material handles.
    _MAT_CACHE.clear()
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0


# ----------------------------------------------------------------------------
# Materials (reusable, keyed by name)
# ----------------------------------------------------------------------------

_MAT_CACHE = {}


def material(name, base_color, metallic=0.0, roughness=0.8, emission=None,
             emission_strength=1.0):
    """Reusable Principled-BSDF material. base_color/emission are RGB 0..1."""
    try:
        if name in _MAT_CACHE and _MAT_CACHE[name].users is not None:
            return _MAT_CACHE[name]
    except ReferenceError:
        _MAT_CACHE.pop(name, None)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    r, g, b = base_color
    bsdf.inputs["Base Color"].default_value = (r, g, b, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        er, eg, eb = emission
        # input name differs slightly across versions; set defensively
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = (er, eg, eb, 1.0)
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = emission_strength
    _MAT_CACHE[name] = mat
    return mat


# Shared palette — fictional, generic motorsport colours.
PAL = {
    "steel":     lambda: material("M_Kit_Steel", (0.55, 0.56, 0.58), metallic=1.0, roughness=0.35),
    "galv":      lambda: material("M_Kit_Galvanised", (0.62, 0.63, 0.64), metallic=1.0, roughness=0.5),
    "concrete":  lambda: material("M_Kit_Concrete", (0.68, 0.67, 0.64), metallic=0.0, roughness=0.9),
    "concrete_dk": lambda: material("M_Kit_ConcreteDark", (0.5, 0.49, 0.47), metallic=0.0, roughness=0.92),
    "kerb_red":  lambda: material("M_Kit_KerbRed", (0.62, 0.09, 0.09), roughness=0.6),
    "kerb_white": lambda: material("M_Kit_KerbWhite", (0.85, 0.85, 0.85), roughness=0.6),
    "rubber":    lambda: material("M_Kit_Rubber", (0.05, 0.05, 0.055), roughness=0.85),
    "paint_blue": lambda: material("M_Kit_PaintBlue", (0.08, 0.20, 0.45), roughness=0.5),
    "paint_yellow": lambda: material("M_Kit_PaintYellow", (0.80, 0.62, 0.05), roughness=0.55),
    "paint_white": lambda: material("M_Kit_PaintWhite", (0.82, 0.82, 0.82), roughness=0.55),
    "fence":     lambda: material("M_Kit_Fence", (0.4, 0.41, 0.42), metallic=0.9, roughness=0.6),
    "glass":     lambda: material("M_Kit_Glass", (0.6, 0.7, 0.8), metallic=0.0, roughness=0.1),
    "emis_red":  lambda: material("M_Kit_EmisRed", (0.6, 0.02, 0.02), emission=(1.0, 0.05, 0.05), emission_strength=4.0),
    "emis_green": lambda: material("M_Kit_EmisGreen", (0.02, 0.5, 0.05), emission=(0.1, 1.0, 0.1), emission_strength=4.0),
    "emis_white": lambda: material("M_Kit_EmisWhite", (0.9, 0.9, 0.9), emission=(1.0, 1.0, 0.95), emission_strength=3.0),
    "board":     lambda: material("M_Kit_Board", (0.9, 0.9, 0.9), roughness=0.4),
    "asphalt":   lambda: material("M_Kit_Asphalt", (0.09, 0.09, 0.10), roughness=0.95),
    "gravel":    lambda: material("M_Kit_Gravel", (0.62, 0.58, 0.5), roughness=1.0),
    "grass":     lambda: material("M_Kit_Grass", (0.22, 0.35, 0.14), roughness=1.0),
    "tarp":      lambda: material("M_Kit_Tarp", (0.15, 0.16, 0.2), roughness=0.7),
    "wood":      lambda: material("M_Kit_Wood", (0.35, 0.24, 0.13), roughness=0.8),
}


def mat(key):
    return PAL[key]()


# ----------------------------------------------------------------------------
# Mesh construction
# ----------------------------------------------------------------------------

def new_object(name, bm, mats=None):
    """Create an object from a bmesh, link it, assign materials, cleanup."""
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    if mats:
        for m in mats:
            me.materials.append(m)
    return ob


def add_box(bm, center, size, mat_index=0):
    """Append an axis-aligned box (size = full extents) to a bmesh."""
    cx, cy, cz = center
    sx, sy, sz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
    verts = [
        bm.verts.new((cx - sx, cy - sy, cz - sz)),
        bm.verts.new((cx + sx, cy - sy, cz - sz)),
        bm.verts.new((cx + sx, cy + sy, cz - sz)),
        bm.verts.new((cx - sx, cy + sy, cz - sz)),
        bm.verts.new((cx - sx, cy - sy, cz + sz)),
        bm.verts.new((cx + sx, cy - sy, cz + sz)),
        bm.verts.new((cx + sx, cy + sy, cz + sz)),
        bm.verts.new((cx - sx, cy + sy, cz + sz)),
    ]
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1),
             (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    fs = []
    for f in faces:
        face = bm.faces.new([verts[i] for i in f])
        face.material_index = mat_index
        fs.append(face)
    return fs


def add_cylinder(bm, center, radius, height, segments=16, mat_index=0, axis="Z"):
    """Append a capped cylinder to a bmesh."""
    cx, cy, cz = center
    ring_bot, ring_top = [], []
    for i in range(segments):
        a = 2.0 * math.pi * i / segments
        x = radius * math.cos(a)
        y = radius * math.sin(a)
        if axis == "Z":
            ring_bot.append(bm.verts.new((cx + x, cy + y, cz - height / 2)))
            ring_top.append(bm.verts.new((cx + x, cy + y, cz + height / 2)))
        elif axis == "Y":
            ring_bot.append(bm.verts.new((cx + x, cy - height / 2, cz + y)))
            ring_top.append(bm.verts.new((cx + x, cy + height / 2, cz + y)))
        else:  # X
            ring_bot.append(bm.verts.new((cx - height / 2, cy + x, cz + y)))
            ring_top.append(bm.verts.new((cx + height / 2, cy + x, cz + y)))
    for i in range(segments):
        j = (i + 1) % segments
        f = bm.faces.new([ring_bot[i], ring_bot[j], ring_top[j], ring_top[i]])
        f.material_index = mat_index
    bm.faces.new(list(reversed(ring_bot))).material_index = mat_index
    bm.faces.new(ring_top).material_index = mat_index


def cleanup_mesh(ob, merge_dist=0.0005):
    """Recalculate normals, remove doubles, triangulate n-gons > 4."""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge_dist)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()


# ----------------------------------------------------------------------------
# Transforms / finishing
# ----------------------------------------------------------------------------

def finalize(ob, smooth=False, origin_to_base=True):
    """Apply scale/rotation, set a sensible origin, optional smooth shading."""
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if origin_to_base:
        # move origin to the object's XY centre at its minimum Z (ground pivot)
        me = ob.data
        min_z = min((ob.matrix_world @ v.co).z for v in me.vertices)
        xs = [(ob.matrix_world @ v.co).x for v in me.vertices]
        ys = [(ob.matrix_world @ v.co).y for v in me.vertices]
        cursor = bpy.context.scene.cursor
        prev = cursor.location.copy()
        cursor.location = Vector(((min(xs) + max(xs)) / 2,
                                  (min(ys) + max(ys)) / 2, min_z))
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
        cursor.location = prev
    if smooth:
        bpy.ops.object.shade_smooth()
    else:
        bpy.ops.object.shade_flat()


def tri_count(ob):
    ob.data.calc_loop_triangles()
    return len(ob.data.loop_triangles)


# ----------------------------------------------------------------------------
# LODs and collision proxies
# ----------------------------------------------------------------------------

def make_lod(ob, ratio, name):
    """Decimated copy of `ob` at the given ratio (0..1)."""
    lod = ob.copy()
    lod.data = ob.data.copy()
    lod.name = name
    lod.data.name = name
    bpy.context.collection.objects.link(lod)
    if ratio < 0.999:
        m = lod.modifiers.new("Decimate", "DECIMATE")
        m.ratio = ratio
        bpy.context.view_layer.objects.active = lod
        bpy.ops.object.modifier_apply(modifier=m.name)
    return lod


def make_collision(ob, name):
    """Low-poly convex-hull collision proxy of `ob`."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    ch = bmesh.ops.convex_hull(bm, input=bm.verts)
    # keep only hull geometry
    geom = ch["geom"]
    keep = set(g for g in geom)
    for f in list(bm.faces):
        if f not in keep:
            bm.faces.remove(f)
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    col = bpy.data.objects.new(name, me)
    col.matrix_world = ob.matrix_world.copy()
    bpy.context.collection.objects.link(col)
    return col


# ----------------------------------------------------------------------------
# Export
# ----------------------------------------------------------------------------

def export_asset(base_obj, filepath, lod_ratios=(0.45, 0.18), collision=True,
                 category="prop"):
    """
    Build LODs + collision from `base_obj` and export a single GLB.

    Object naming inside the GLB (read by GltfKitImporter):
      <Base>_LOD0, <Base>_LOD1, ...  and  <Base>_COL
    """
    base_name = base_obj.name
    base_obj.name = base_name + "_LOD0"
    base_obj.data.name = base_name + "_LOD0"
    exported = [base_obj]
    for i, ratio in enumerate(lod_ratios, start=1):
        exported.append(make_lod(base_obj, ratio, "%s_LOD%d" % (base_name, i)))
    if collision:
        exported.append(make_collision(base_obj, base_name + "_COL"))

    bpy.ops.object.select_all(action="DESELECT")
    for ob in exported:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = exported[0]
    bpy.ops.export_scene.gltf(
        filepath=filepath,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
    )
    stats = {
        "name": base_name,
        "category": category,
        "file": filepath,
        "lod0_tris": tri_count(exported[0]),
        "lods": len(lod_ratios) + 1,
        "collision_tris": tri_count(exported[-1]) if collision else 0,
    }
    return stats


def validate_glb(filepath):
    """Cheap structural validation: GLB magic + JSON chunk parse + mesh count."""
    import struct
    import json
    with open(filepath, "rb") as f:
        header = f.read(12)
        if header[:4] != b"glTF":
            return False, "bad magic"
        length = struct.unpack("<I", header[8:12])[0]
        # first chunk = JSON
        clen = struct.unpack("<I", f.read(4))[0]
        f.read(4)  # chunk type
        js = json.loads(f.read(clen).decode("utf-8"))
    meshes = len(js.get("meshes", []))
    nodes = len(js.get("nodes", []))
    if meshes == 0:
        return False, "no meshes"
    return True, "ok meshes=%d nodes=%d bytes=%d" % (meshes, nodes, length)
