# Unity import (GLB → prefab)

## glTFast

`com.unity.cloud.gltfast` 6.0.1 is added to `Packages/manifest.json`. It
registers a ScriptedImporter, so every `.glb` under `Assets/Art/` is imported as
a `GameObject` automatically. Package Manager may offer a newer 6.x compatible
with 2022.3 — accept it; the importer below is version-agnostic.

## KitAssetImporter (`Assets/Game/Code/Editor/ArtPipeline/`)

**F1 Game → Art → Import Kit GLBs → Prefabs.** For each imported GLB it:

- reads the `_LOD0/_LOD1/_LOD2` meshes → builds an `LODGroup`
  (transitions 0.5 / 0.18 / 0.02);
- reads the `_COL` mesh → a convex `MeshCollider` (renderer stripped);
- sets Batching/Occluder/Occludee/GI static flags;
- enables GPU instancing on materials;
- writes a `SourceAssetMetadata` provenance asset (with SHA-256) next to the
  prefab under `SourceMetadata/`;
- saves the prefab under `<category>/Prefabs/` and a report to
  `Docs/ArtPipeline/KIT_IMPORT_REPORT.md`.

It is **package-agnostic**: it post-processes the already-imported GameObject via
`AssetDatabase`, so it compiles whether or not glTFast is resolved; an
un-imported GLB is skipped with a clear message rather than failing the build.
It never overwrites manually-edited prefabs outside its own output folders.

## SourceAssetMetadata

Editor-only `ScriptableObject` linking each processed asset to its source +
licence (mirrors `asset_manifest.json`). **F1 Game → Art → Validate Art
Provenance** flags any prefab without a record and any incomplete record.

## Colour space & texture settings

`UrpMaterialBuilder` sets normal maps to `NormalMap` type and MaskMaps to linear
(sRGB off); base colour stays sRGB. Mipmaps on. See MATERIALS_AND_LIGHTING.
