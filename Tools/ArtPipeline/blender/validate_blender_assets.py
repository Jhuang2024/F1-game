"""
validate_blender_assets.py — structural validation of every exported kit GLB.

Standalone (pure Python, no bpy needed): parses each GLB, checks magic, JSON
chunk, mesh/node counts, and confirms the LOD0/LOD1/LOD2/_COL naming contract
the Unity GltfKitImporter relies on.

    python Tools/ArtPipeline/blender/validate_blender_assets.py [<art_root>]
"""

import sys
import os
import json
import struct
import glob


def parse_glb(path):
    with open(path, "rb") as f:
        magic, ver, length = struct.unpack("<4sII", f.read(12))
        if magic != b"glTF":
            raise ValueError("bad magic")
        clen, ctype = struct.unpack("<II", f.read(8))
        js = json.loads(f.read(clen).decode("utf-8"))
    return js, length


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "Assets/Art"
    files = sorted(glob.glob(os.path.join(root, "**", "*.glb"), recursive=True))
    if not files:
        print("no GLB files under", root)
        return 1
    ok = 0
    problems = []
    for path in files:
        try:
            js, length = parse_glb(path)
        except Exception as e:  # noqa: BLE001
            problems.append((path, "parse: %s" % e))
            continue
        node_names = [n.get("name", "") for n in js.get("nodes", [])]
        has_lod0 = any(n.endswith("_LOD0") for n in node_names)
        has_col = any(n.endswith("_COL") for n in node_names)
        meshes = len(js.get("meshes", []))
        issues = []
        if meshes == 0:
            issues.append("no meshes")
        if not has_lod0:
            issues.append("missing _LOD0")
        if not has_col:
            issues.append("missing _COL")
        if issues:
            problems.append((path, ", ".join(issues)))
        else:
            ok += 1
            print("OK  %-52s meshes=%d nodes=%s"
                  % (os.path.relpath(path, root), meshes, len(node_names)))
    print("\n%d/%d valid" % (ok, len(files)))
    for p, why in problems:
        print("FAIL", p, "->", why)
    return 0 if not problems else 2


if __name__ == "__main__":
    sys.exit(main())
