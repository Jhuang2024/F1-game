"""
generate_collision_proxies.py — export just the convex collision proxy of a GLB.
    <blender> --background --python generate_collision_proxies.py -- <in.glb> <out.glb>
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kitlib as K
import bpy
def main():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    if len(a) < 2:
        print("usage: -- <in.glb> <out.glb>"); return 1
    src, dst = a[0], a[1]
    K.reset_scene(); bpy.ops.import_scene.gltf(filepath=src)
    m = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in m: o.select_set(True)
    bpy.context.view_layer.objects.active = m[0]
    if len(m) > 1: bpy.ops.object.join()
    base = bpy.context.view_layer.objects.active
    col = K.make_collision(base, os.path.splitext(os.path.basename(dst))[0] + "_COL")
    bpy.ops.object.select_all(action="DESELECT"); col.select_set(True)
    bpy.context.view_layer.objects.active = col
    bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", use_selection=True)
    print("[collision]", dst, "tris=", K.tri_count(col)); return 0
if __name__ == "__main__": sys.exit(main())
