# Blender procedural asset factory

Headless Blender (`bpy` 5.0.1 module, or `blender --background --python`).
Deterministic, seeded, first-principles geometry — no ripped or branded content.

## Layout

```
Tools/ArtPipeline/blender/
  kitlib.py                     # engine: materials, mesh ops, LODs, collision, GLB export, validate
  build_kit.py                  # single source of truth: all 26 module builders + registry + main
  export_game_assets.py         # canonical full-kit runner (alias of build_kit.main)
  build_<category>.py           # thin per-category wrappers (reuse build_kit builders)
  process_external_asset.py     # normalise a downloaded model -> game-ready GLB
  generate_lods.py              # regenerate LODs for one GLB
  generate_collision_proxies.py # export a convex proxy for one GLB
  validate_blender_assets.py    # standalone structural GLB validation (no bpy)
  install_blender_mcp.py        # install/enable the upstream Blender MCP addon (GUI socket)
```

## Asset standards (enforced by `kitlib`)

metres · applied rotation/scale · ground-plane origin · clean names · reusable
Principled materials · recalculated normals · merged doubles · LOD1/LOD2 by
decimation · convex `_COL` collision proxy · GLB export (`export_yup`) · every
asset reproducible from its script.

## Naming contract (read by the Unity importer)

One GLB per module, containing objects `<Name>_LOD0`, `<Name>_LOD1`,
`<Name>_LOD2`, `<Name>_COL`. `KitAssetImporter` turns these into an LODGroup +
MeshCollider on a prefab.

## The kit (26 modules, all validated here)

Barriers (armco, concrete, energy-absorbing, tyre wall) · fencing (post, catch
panel, gate) · curbs (painted, sausage, flat, drain) · pit (wall, garage bay,
door) · structures (start gantry, timing gantry, braking board, marshal post,
camera tower, light tower, speaker pole, pedestrian bridge) · grandstand ·
props (guard hut, utility cabinet, maintenance cart). See `build_report.json`
and `Assets/ThirdParty/THIRD_PARTY_ASSETS.md` for per-asset tris + checksums.

## Regenerate

```bash
python Tools/ArtPipeline/blender/build_kit.py -- "$PWD"
python Tools/ArtPipeline/blender/validate_blender_assets.py Assets/Art   # 26/26 valid
python Tools/ArtPipeline/build_manifest.py
```
