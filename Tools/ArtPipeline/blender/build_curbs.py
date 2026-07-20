"""
build_curbs.py — thin wrapper: build build_kerb_painted, build_kerb_sausage, build_kerb_flat, build_drain and export as game-ready GLB(s).

Regenerates only this category. The geometry lives in build_kit.py (single
source of truth); this script reuses those builders so per-category and
full-kit builds never diverge.

    <blender> --background --python build_curbs.py -- <repo_root>
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kitlib as K
import build_kit as B

BUILDERS = ["build_kerb_painted", "build_kerb_sausage", "build_kerb_flat", "build_drain"]
FOLDER = "Track/Curbs"

def main():
    argv = sys.argv
    root = argv[-1] if "--" in argv else os.getcwd()
    dest = os.path.join(root, "Assets", "Art", FOLDER)
    os.makedirs(dest, exist_ok=True)
    for fn_name in BUILDERS:
        K.reset_scene()
        ob = getattr(B, fn_name)()
        path = os.path.join(dest, ob.name + ".glb")
        stats = K.export_asset(ob, path, lod_ratios=(0.5, 0.2), collision=True, category=FOLDER)
        ok, msg = K.validate_glb(path)
        print("[%s] %s LOD0=%d %s %s" % ("build_curbs", ob.name, stats["lod0_tris"],
              "OK" if ok else "FAIL", msg))

if __name__ == "__main__":
    main()
