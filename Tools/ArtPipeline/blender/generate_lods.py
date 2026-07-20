"""
generate_lods.py — regenerate LOD1/LOD2 for a single asset from its LOD0.
    <blender> --background --python generate_lods.py -- <in.glb> <out.glb> [r1 r2]
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kitlib as K
import bpy
def main():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    if len(a) < 2:
        print("usage: -- <in.glb> <out.glb> [r1 r2]"); return 1
    src, dst = a[0], a[1]
    ratios = tuple(float(x) for x in a[2:4]) if len(a) >= 4 else (0.5, 0.2)
    K.reset_scene(); bpy.ops.import_scene.gltf(filepath=src)
    m = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in m: o.select_set(True)
    bpy.context.view_layer.objects.active = m[0]
    if len(m) > 1: bpy.ops.object.join()
    base = bpy.context.view_layer.objects.active
    base.name = os.path.splitext(os.path.basename(dst))[0]
    K.finalize(base)
    K.export_asset(base, dst, lod_ratios=ratios, collision=True)
    ok, msg = K.validate_glb(dst); print("[lods]", dst, "OK" if ok else "FAIL", msg)
    return 0 if ok else 2
if __name__ == "__main__": sys.exit(main())
