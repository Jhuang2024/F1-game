# Art Pipeline

A legally-clean, reproducible pipeline that turns the F1-game project from a
placeholder-material prototype into a dressed, visually-coherent racing world —
**without** touching gameplay, physics, AI, circuit geometry, RNG, save formats
or tuning.

## What's in the box

| Area | Deliverable | State |
|---|---|---|
| Procedural asset factory | `Tools/ArtPipeline/blender/` (headless Blender / `bpy`) | **Runs here** → 26 GLB modules under `Assets/Art/` (LOD0/1/2 + collision) |
| PBR surface library | `Tools/ArtPipeline/generate_textures.py` | **Runs here** → 20 tiling surfaces (BaseColor/Normal/MaskMap) under `Assets/Art/Materials/` |
| Asset acquisition | `Tools/ArtPipeline/fetch_assets.py` + `asset_requests.json` | Written & dry-run-tested; live download blocked by egress policy (dev machine) |
| Provenance / licensing | `Assets/ThirdParty/asset_manifest.json`, `THIRD_PARTY_ASSETS.md` | **Generated** with real SHA-256 |
| Unity import | `Assets/Game/Code/Editor/ArtPipeline/KitAssetImporter.cs` | C# written (static-only; run in Unity on dev machine) |
| URP materials | `UrpMaterialBuilder.cs` | C# written (static-only) |
| Track dressing | `Assets/Game/Code/Track/Dressing/` | C# written (static-only) |
| MCP servers | `.mcp.json` (Blender), `Packages/manifest.json` (Unity MCP) | Configured |

## Read next

- [SETUP_REPORT.md](SETUP_REPORT.md) — **start here**: exact environment, what was
  verified vs. what needs your Unity machine.
- [INSTALLATION.md](INSTALLATION.md) — tools, MCP, reconnect steps.
- [ASSET_SOURCING.md](ASSET_SOURCING.md) — how assets are acquired & licence-checked.
- [BLENDER_PIPELINE.md](BLENDER_PIPELINE.md) — the procedural factory.
- [UNITY_IMPORT.md](UNITY_IMPORT.md) — GLB → prefab (LODGroup + collider + provenance).
- [TRACK_DRESSING.md](TRACK_DRESSING.md) — the procedural dressing system & profiles.
- [MATERIALS_AND_LIGHTING.md](MATERIALS_AND_LIGHTING.md) — URP materials, wetness, decals, lighting profiles.
- [PERFORMANCE_BUDGETS.md](PERFORMANCE_BUDGETS.md) — budgets & repo hygiene.
- [LICENSING.md](LICENSING.md) — legal policy.
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Reproduce everything (from repo root)

```bash
python3.11 -m venv .venv && source .venv/bin/activate
pip install bpy==5.0.1 pillow numpy
python Tools/ArtPipeline/blender/build_kit.py -- "$PWD"          # 26 GLB modules
python Tools/ArtPipeline/generate_textures.py --size 1024        # 20 PBR surfaces
python Tools/ArtPipeline/build_manifest.py                       # manifest + checksums
python Tools/ArtPipeline/blender/validate_blender_assets.py Assets/Art
python Tools/ContentValidation/validate_content.py              # repo-wide, must PASS
```

## Validation checklist for the dev machine (Phase 18 — needs Unity 2022.3.62f1)

The cloud container has no Unity editor, so these could not be executed here.
On macOS with Unity 2022.3.62f1:

1. Open the project; let Package Manager resolve `com.unity.cloud.gltfast` and
   `com.coplaydev.unity-mcp`; confirm the console compiles clean.
2. `F1 Game → Art → Import Kit GLBs → Prefabs` (builds LODGroup + collider +
   `SourceAssetMetadata` per module).
3. `F1 Game → Art → Build URP Materials From Generated Textures`.
4. Create a `CircuitVisualProfile`, assign the imported kit prefabs.
5. `F1 Game → Art → Dress Open Scene (reference circuit)`.
6. `F1 Game → Art → Validate Art Provenance`.
7. Run EditMode + PlayMode tests; drive a lap; confirm physics/AI/HUD/pit
   unchanged and no barrier intrudes on the racing line.
8. Capture before/after benchmark screenshots into `VisualComparisons/`.
