"""
process_external_asset.py — normalise an externally-sourced model into a
game-ready GLB with LODs + collision proxy.

Use this on the dev machine for legally-sourced meshes downloaded by
fetch_assets.py (Poly Haven models, etc.) — those CDNs are egress-blocked in
the CI/cloud container, so this step runs where the download succeeded.

    <blender> --background --python process_external_asset.py -- <in.glb|in.fbx> <out.glb> [--smooth]

It applies transforms, recalculates normals, removes doubles, generates
LOD1/LOD2 and a `_COL` convex proxy, and re-exports GLB using the same naming
contract as the procedural kit (read by GltfKitImporter).
"""

import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kitlib as K  # noqa: E402
import bpy  # noqa: E402


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(argv) < 2:
        print("usage: process_external_asset.py -- <in> <out.glb> [--smooth]")
        return 1
    src, dst = argv[0], argv[1]
    smooth = "--smooth" in argv

    K.reset_scene()
    ext = os.path.splitext(src)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=src)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=src)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=src)
    else:
        print("unsupported input:", ext)
        return 2

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        print("no mesh imported")
        return 3
    # join into a single base object
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    base = bpy.context.view_layer.objects.active
    base.name = os.path.splitext(os.path.basename(dst))[0]
    K.cleanup_mesh(base)
    K.finalize(base, smooth=smooth)
    os.makedirs(os.path.dirname(dst) or ".", exist_ok=True)
    stats = K.export_asset(base, dst, lod_ratios=(0.5, 0.2), collision=True,
                           category="external")
    ok, msg = K.validate_glb(dst)
    print("[process] %s -> %s  LOD0=%d  %s (%s)"
          % (src, dst, stats["lod0_tris"], "OK" if ok else "FAIL", msg))
    return 0 if ok else 4


if __name__ == "__main__":
    sys.exit(main())
