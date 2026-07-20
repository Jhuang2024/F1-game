"""
build_kit.py — orchestrates the full procedural modular track-dressing kit.

Run headless:
    <blender> --background --python Tools/ArtPipeline/blender/build_kit.py -- <out_root>
or with the bpy module:
    python Tools/ArtPipeline/blender/run_build.py

Produces one GLB per module under <out_root>/Assets/Art/... , each carrying
LOD0..LODn + a `_COL` collision proxy, plus writes build_report.json.

Every module is generated from primitives — no external, ripped, or branded
geometry. Fictional motorsport styling only.
"""

import sys
import os
import json
import math

# allow running as `blender --python build_kit.py`
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kitlib as K  # noqa: E402  (imports bpy first)
import bmesh  # noqa: E402  (bmesh must be imported after bpy is initialised)
from kitlib import add_box, add_cylinder, mat, new_object, cleanup_mesh, finalize  # noqa: E402


# ---------------------------------------------------------------------------
# BARRIERS
# ---------------------------------------------------------------------------

def build_armco():
    """Metal Armco guardrail module, 4 m span, double-wave rail on posts."""
    bm = bmesh.new()
    steel = 0
    # two posts
    for x in (-1.8, 1.8):
        add_cylinder(bm, (x, 0, 0.35), 0.05, 0.70, segments=8, mat_index=steel)
    # corrugated rail: 3 stacked shallow boxes to fake the W-profile
    for z, y in ((0.62, 0.0), (0.70, 0.06), (0.54, 0.06)):
        add_box(bm, (0, y, z), (4.0, 0.04, 0.14), mat_index=steel)
    ob = new_object("Barrier_Armco_4m", bm, [mat("steel")])
    cleanup_mesh(ob)
    finalize(ob, smooth=True)
    return ob


def build_concrete_barrier():
    """Concrete (Jersey-profile) barrier, 3 m."""
    bm = bmesh.new()
    add_box(bm, (0, 0, 0.10), (3.0, 0.60, 0.20), mat_index=0)   # wide base
    add_box(bm, (0, 0, 0.45), (3.0, 0.32, 0.50), mat_index=0)   # tapered body
    add_box(bm, (0, 0, 0.78), (3.0, 0.20, 0.18), mat_index=0)   # cap
    ob = new_object("Barrier_Concrete_3m", bm, [mat("concrete")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_tecpro():
    """Generic energy-absorbing barrier block (TecPro-like), 1 m."""
    bm = bmesh.new()
    add_box(bm, (0, 0, 0.45), (1.0, 0.55, 0.90), mat_index=0)
    # two horizontal cylinders to suggest the absorbing tubes
    add_cylinder(bm, (0, 0.30, 0.30), 0.14, 1.0, segments=12, mat_index=1, axis="X")
    add_cylinder(bm, (0, 0.30, 0.62), 0.14, 1.0, segments=12, mat_index=1, axis="X")
    ob = new_object("Barrier_Tecpro_1m", bm, [mat("paint_blue"), mat("paint_yellow")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_tire_wall():
    """Stacked tyre barrier module, 2 m, 3-high pyramid."""
    bm = bmesh.new()
    rows = [(-0.65, 0.0), (0.0, 0.0), (0.65, 0.0)]
    for level in range(3):
        z = 0.30 + level * 0.55
        offset = 0.30 * (level % 2)
        for (x, y) in rows:
            add_cylinder(bm, (x + offset, y, z), 0.30, 0.45, segments=14,
                         mat_index=0, axis="Y")
    ob = new_object("Barrier_TireWall_2m", bm, [mat("rubber")])
    cleanup_mesh(ob)
    finalize(ob, smooth=True)
    return ob


# ---------------------------------------------------------------------------
# FENCING
# ---------------------------------------------------------------------------

def build_fence_post():
    bm = bmesh.new()
    add_cylinder(bm, (0, 0, 1.5), 0.06, 3.0, segments=10, mat_index=0)
    add_box(bm, (0, 0, 3.0), (0.20, 0.10, 0.10), mat_index=0)  # top bracket
    ob = new_object("Fence_Post_3m", bm, [mat("galv")])
    cleanup_mesh(ob)
    finalize(ob, smooth=True)
    return ob


def build_catch_fence():
    """Debris catch-fence panel, 5 m x 3 m, framed mesh."""
    bm = bmesh.new()
    frame = 0
    # frame
    add_box(bm, (0, 0, 0.05), (5.0, 0.08, 0.10), mat_index=frame)
    add_box(bm, (0, 0, 3.0), (5.0, 0.08, 0.10), mat_index=frame)
    for x in (-2.5, 0, 2.5):
        add_box(bm, (x, 0, 1.5), (0.08, 0.08, 3.0), mat_index=frame)
    # mesh represented as a thin panel (real chain-link comes from the alpha texture)
    add_box(bm, (0, 0, 1.55), (5.0, 0.01, 2.9), mat_index=1)
    ob = new_object("Fence_CatchPanel_5m", bm, [mat("galv"), mat("fence")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_fence_gate():
    bm = bmesh.new()
    for x in (-1.5, 1.5):
        add_box(bm, (x, 0, 1.5), (0.12, 0.12, 3.0), mat_index=0)
    add_box(bm, (0, 0, 2.9), (3.0, 0.10, 0.15), mat_index=0)
    add_box(bm, (0, 0, 1.4), (2.7, 0.02, 2.6), mat_index=1)
    ob = new_object("Fence_Gate_3m", bm, [mat("galv"), mat("fence")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


# ---------------------------------------------------------------------------
# CURBS
# ---------------------------------------------------------------------------

def build_kerb_painted():
    """Standard painted kerb, 4 m, red/white striped (two material slices)."""
    bm = bmesh.new()
    seg = 8
    for i in range(seg):
        x = -2.0 + (i + 0.5) * (4.0 / seg)
        mi = 0 if i % 2 == 0 else 1
        add_box(bm, (x, 0, 0.03), (4.0 / seg, 0.50, 0.06), mat_index=mi)
    # slight rise on outer edge
    for i in range(seg):
        x = -2.0 + (i + 0.5) * (4.0 / seg)
        mi = 0 if i % 2 == 0 else 1
        add_box(bm, (x, 0.20, 0.09), (4.0 / seg, 0.10, 0.06), mat_index=mi)
    ob = new_object("Kerb_Painted_4m", bm, [mat("kerb_red"), mat("kerb_white")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_kerb_sausage():
    """Aggressive 'sausage' kerb bump, 1 m."""
    bm = bmesh.new()
    add_cylinder(bm, (0, 0, 0.0), 0.12, 1.0, segments=16, mat_index=0, axis="X")
    ob = new_object("Kerb_Sausage_1m", bm, [mat("kerb_yellow" if False else "paint_yellow")])
    cleanup_mesh(ob)
    finalize(ob, smooth=True)
    return ob


def build_kerb_flat():
    bm = bmesh.new()
    seg = 8
    for i in range(seg):
        x = -2.0 + (i + 0.5) * (4.0 / seg)
        mi = 0 if i % 2 == 0 else 1
        add_box(bm, (x, 0, 0.015), (4.0 / seg, 0.60, 0.03), mat_index=mi)
    ob = new_object("Kerb_Flat_4m", bm, [mat("kerb_red"), mat("kerb_white")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_drain():
    """Drainage channel with grate, 2 m."""
    bm = bmesh.new()
    add_box(bm, (0, 0, -0.10), (2.0, 0.40, 0.20), mat_index=0)     # channel
    for i in range(10):
        x = -0.9 + i * 0.2
        add_box(bm, (x, 0, 0.0), (0.08, 0.40, 0.04), mat_index=1)  # grate bars
    ob = new_object("Track_Drain_2m", bm, [mat("concrete_dk"), mat("steel")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


# ---------------------------------------------------------------------------
# PIT LANE
# ---------------------------------------------------------------------------

def build_pit_wall():
    bm = bmesh.new()
    add_box(bm, (0, 0, 0.55), (4.0, 0.25, 1.10), mat_index=0)
    add_box(bm, (0, 0.16, 1.12), (4.0, 0.05, 0.06), mat_index=1)  # top rail
    ob = new_object("Pit_Wall_4m", bm, [mat("concrete"), mat("paint_white")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_garage_bay():
    """Open garage bay module, 6 m wide."""
    bm = bmesh.new()
    add_box(bm, (0, 3.0, 0.0), (6.0, 6.0, 0.02), mat_index=0)      # floor
    add_box(bm, (0, 3.0, 3.5), (6.0, 6.0, 0.15), mat_index=1)      # roof
    for x in (-3.0, 3.0):
        add_box(bm, (x, 3.0, 1.75), (0.20, 6.0, 3.5), mat_index=1)  # side walls
    add_box(bm, (0, 6.0, 1.75), (6.0, 0.20, 3.5), mat_index=1)      # back wall
    ob = new_object("Pit_GarageBay_6m", bm, [mat("concrete_dk"), mat("concrete")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_garage_door():
    bm = bmesh.new()
    add_box(bm, (0, 0, 1.6), (5.4, 0.10, 3.2), mat_index=0)
    for i in range(8):
        z = 0.2 + i * 0.4
        add_box(bm, (0, 0.06, z), (5.4, 0.02, 0.05), mat_index=1)  # slats
    ob = new_object("Pit_GarageDoor_6m", bm, [mat("paint_white"), mat("galv")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


# ---------------------------------------------------------------------------
# STRUCTURES / PROPS
# ---------------------------------------------------------------------------

def build_start_gantry():
    """Starting-light gantry spanning ~16 m with 5 light pods."""
    bm = bmesh.new()
    for x in (-8.0, 8.0):
        add_box(bm, (x, 0, 3.5), (0.4, 0.4, 7.0), mat_index=0)     # legs
    add_box(bm, (0, 0, 7.2), (16.8, 0.6, 0.6), mat_index=0)        # beam
    for i in range(5):
        x = -4.0 + i * 2.0
        add_box(bm, (x, 0.35, 6.7), (1.2, 0.15, 0.9), mat_index=1)   # housing
        add_cylinder(bm, (x, 0.45, 6.9), 0.14, 0.05, segments=12, mat_index=2, axis="Y")
        add_cylinder(bm, (x, 0.45, 6.5), 0.14, 0.05, segments=12, mat_index=2, axis="Y")
    ob = new_object("Struct_StartGantry_16m", bm,
                    [mat("steel"), mat("concrete_dk"), mat("emis_red")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_timing_gantry():
    bm = bmesh.new()
    for x in (-7.0, 7.0):
        add_box(bm, (x, 0, 3.0), (0.35, 0.35, 6.0), mat_index=0)
    add_box(bm, (0, 0, 6.1), (14.5, 0.5, 0.5), mat_index=0)
    add_box(bm, (0, 0.3, 5.6), (10.0, 0.1, 1.0), mat_index=1)      # banner face
    ob = new_object("Struct_TimingGantry_14m", bm, [mat("steel"), mat("board")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_braking_board():
    """Braking distance marker board (fictional numbers via texture later)."""
    bm = bmesh.new()
    add_cylinder(bm, (0, 0, 1.0), 0.06, 2.0, segments=8, mat_index=0)
    add_box(bm, (0, 0.06, 1.7), (0.7, 0.05, 0.9), mat_index=1)
    ob = new_object("Prop_BrakingBoard", bm, [mat("galv"), mat("board")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_marshal_post():
    """Marshal shelter + light panel."""
    bm = bmesh.new()
    add_box(bm, (0, 0, 0.02), (2.2, 1.6, 0.04), mat_index=0)       # platform
    for (x, y) in ((-1.0, -0.7), (1.0, -0.7), (-1.0, 0.7), (1.0, 0.7)):
        add_box(bm, (x, y, 1.2), (0.1, 0.1, 2.4), mat_index=1)     # posts
    add_box(bm, (0, 0, 2.5), (2.4, 1.8, 0.12), mat_index=2)        # roof
    add_box(bm, (0, -0.85, 1.8), (1.0, 0.06, 0.5), mat_index=3)    # light panel
    ob = new_object("Prop_MarshalPost", bm,
                    [mat("concrete_dk"), mat("galv"), mat("tarp"), mat("emis_green")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_camera_tower():
    bm = bmesh.new()
    add_box(bm, (0, 0, 0.1), (1.2, 1.2, 0.2), mat_index=0)
    for (x, y) in ((-0.4, -0.4), (0.4, -0.4), (-0.4, 0.4), (0.4, 0.4)):
        add_box(bm, (x, y, 3.0), (0.1, 0.1, 6.0), mat_index=1)
    add_box(bm, (0, 0, 6.1), (1.4, 1.4, 0.1), mat_index=0)         # platform
    add_box(bm, (0, 0.6, 6.5), (0.4, 0.3, 0.3), mat_index=2)       # camera head
    ob = new_object("Struct_CameraTower_6m", bm,
                    [mat("concrete_dk"), mat("steel"), mat("rubber")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_light_tower():
    bm = bmesh.new()
    add_box(bm, (0, 0, 0.15), (1.0, 1.0, 0.3), mat_index=0)
    add_cylinder(bm, (0, 0, 5.0), 0.15, 10.0, segments=10, mat_index=1)
    for i in range(6):
        x = -0.75 + (i % 3) * 0.75
        z = 9.7 + (i // 3) * 0.5
        add_box(bm, (x, 0.3, z), (0.5, 0.15, 0.4), mat_index=2)    # floods
    ob = new_object("Struct_LightTower_10m", bm,
                    [mat("concrete_dk"), mat("galv"), mat("emis_white")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_speaker_pole():
    bm = bmesh.new()
    add_cylinder(bm, (0, 0, 2.5), 0.08, 5.0, segments=8, mat_index=0)
    for a in (0.6, -0.6):
        add_box(bm, (0, a, 4.6), (0.3, 0.3, 0.4), mat_index=1)
    ob = new_object("Prop_SpeakerPole_5m", bm, [mat("galv"), mat("rubber")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_ped_bridge():
    """Simple pedestrian bridge spanning ~14 m."""
    bm = bmesh.new()
    for x in (-7.0, 7.0):
        add_box(bm, (x, 0, 2.5), (0.6, 3.0, 5.0), mat_index=0)     # towers
    add_box(bm, (0, 0, 5.0), (15.0, 3.0, 0.4), mat_index=1)        # deck
    add_box(bm, (0, 1.4, 5.7), (15.0, 0.1, 1.2), mat_index=2)      # rail
    add_box(bm, (0, -1.4, 5.7), (15.0, 0.1, 1.2), mat_index=2)
    ob = new_object("Struct_PedBridge_14m", bm,
                    [mat("concrete"), mat("concrete_dk"), mat("fence")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_grandstand():
    """Modular grandstand section, ~12 m wide, 10 tiers."""
    bm = bmesh.new()
    tiers = 10
    for i in range(tiers):
        y = i * 0.9
        z = i * 0.55
        add_box(bm, (0, -y, z), (12.0, 0.9, 0.1), mat_index=0)      # step
        add_box(bm, (0, -y - 0.35, z + 0.25), (12.0, 0.05, 0.4), mat_index=1)  # seats
    # supporting frame
    for x in (-5.5, 0, 5.5):
        add_box(bm, (x, -tiers * 0.45, tiers * 0.27), (0.3, tiers * 0.95, 0.3), mat_index=2)
    ob = new_object("Struct_Grandstand_12m", bm,
                    [mat("concrete"), mat("paint_blue"), mat("steel")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_guard_hut():
    bm = bmesh.new()
    add_box(bm, (0, 0, 1.2), (2.0, 2.0, 2.4), mat_index=0)
    add_box(bm, (0, 0, 2.5), (2.3, 2.3, 0.15), mat_index=1)
    add_box(bm, (0, -1.0, 1.5), (1.2, 0.05, 1.0), mat_index=2)     # window
    ob = new_object("Prop_GuardHut", bm, [mat("concrete"), mat("tarp"), mat("glass")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_utility_cabinet():
    bm = bmesh.new()
    add_box(bm, (0, 0, 0.6), (0.8, 0.5, 1.2), mat_index=0)
    add_box(bm, (0, -0.26, 0.6), (0.7, 0.02, 1.0), mat_index=1)    # door
    ob = new_object("Prop_UtilityCabinet", bm, [mat("galv"), mat("paint_yellow")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


def build_maintenance_cart():
    bm = bmesh.new()
    add_box(bm, (0, 0, 0.6), (1.6, 0.8, 0.5), mat_index=0)         # bed
    for (x, y) in ((-0.6, -0.35), (0.6, -0.35), (-0.6, 0.35), (0.6, 0.35)):
        add_cylinder(bm, (x, y, 0.25), 0.22, 0.15, segments=12, mat_index=1, axis="X")
    add_box(bm, (0.6, 0, 1.1), (0.3, 0.7, 0.5), mat_index=2)       # seat
    ob = new_object("Prop_MaintenanceCart", bm,
                    [mat("paint_yellow"), mat("rubber"), mat("tarp")])
    cleanup_mesh(ob)
    finalize(ob)
    return ob


# ---------------------------------------------------------------------------
# REGISTRY  (builder, folder-relative-to-Assets/Art, lod_ratios)
# ---------------------------------------------------------------------------

REGISTRY = [
    (build_armco,            "Track/Barriers", (0.5, 0.2)),
    (build_concrete_barrier, "Track/Barriers", (0.6, 0.3)),
    (build_tecpro,           "Track/Barriers", (0.5, 0.25)),
    (build_tire_wall,        "Track/Barriers", (0.4, 0.15)),
    (build_fence_post,       "Track/Fencing",  (0.5, 0.25)),
    (build_catch_fence,      "Track/Fencing",  (0.6, 0.3)),
    (build_fence_gate,       "Track/Fencing",  (0.6, 0.3)),
    (build_kerb_painted,     "Track/Curbs",    (0.6, 0.3)),
    (build_kerb_sausage,     "Track/Curbs",    (0.5, 0.25)),
    (build_kerb_flat,        "Track/Curbs",    (0.6, 0.3)),
    (build_drain,            "Track/Curbs",    (0.6, 0.3)),
    (build_pit_wall,         "PitLane",        (0.6, 0.3)),
    (build_garage_bay,       "PitLane",        (0.6, 0.3)),
    (build_garage_door,      "PitLane",        (0.6, 0.3)),
    (build_start_gantry,     "Props",          (0.5, 0.2)),
    (build_timing_gantry,    "Props",          (0.5, 0.2)),
    (build_braking_board,    "Props",          (0.5, 0.25)),
    (build_marshal_post,     "Props",          (0.5, 0.2)),
    (build_camera_tower,     "Props",          (0.5, 0.2)),
    (build_light_tower,      "Props",          (0.5, 0.2)),
    (build_speaker_pole,     "Props",          (0.5, 0.25)),
    (build_ped_bridge,       "Props",          (0.5, 0.2)),
    (build_grandstand,       "Grandstands",    (0.5, 0.2)),
    (build_guard_hut,        "Props",          (0.5, 0.25)),
    (build_utility_cabinet,  "Props",          (0.5, 0.25)),
    (build_maintenance_cart, "Props",          (0.5, 0.25)),
]


def main():
    argv = sys.argv
    out_root = argv[-1] if "--" in argv else os.getcwd()
    art_root = os.path.join(out_root, "Assets", "Art")
    report = {"assets": [], "art_root": art_root}
    for builder, folder, lods in REGISTRY:
        K.reset_scene()
        ob = builder()
        base = ob.name
        dest_dir = os.path.join(art_root, folder)
        os.makedirs(dest_dir, exist_ok=True)
        path = os.path.join(dest_dir, base + ".glb")
        stats = K.export_asset(ob, path, lod_ratios=lods, collision=True,
                               category=folder)
        ok, msg = K.validate_glb(path)
        stats["valid"] = ok
        stats["validation"] = msg
        stats["rel"] = os.path.relpath(path, out_root)
        report["assets"].append(stats)
        print("[build] %-34s LOD0=%5d tris  COL=%4d  %s"
              % (base, stats["lod0_tris"], stats["collision_tris"], msg))
    report_path = os.path.join(out_root, "Tools", "ArtPipeline", "blender",
                               "build_report.json")
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2)
    print("\n[build] %d assets -> %s" % (len(report["assets"]), art_root))
    print("[build] report -> %s" % report_path)


if __name__ == "__main__":
    main()
