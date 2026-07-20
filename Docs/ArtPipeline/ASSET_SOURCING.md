# Asset sourcing

## Sources & order of preference

1. **Procedural generation** (Blender `bpy` + NumPy) — the primary source here.
   Original, project-owned, CC0-equivalent. No licence risk, fully reproducible.
2. **Poly Haven** (CC0) — HDRIs, generic rocks/plants. Official API only
   (`https://api.polyhaven.com`), honest User-Agent `F1GameArtPipeline/1.0`.
3. **ambientCG** (CC0) — PBR material scans. Official API
   (`https://ambientcg.com/api/v2/full_json`).

> In this environment Poly Haven and ambientCG are **egress-blocked (HTTP 403)**,
> so the surface library is generated procedurally instead
> (`generate_textures.py`). Run `fetch_assets.py` on the dev machine to swap in
> 2K CC0 scans where preferred.

## The downloader (`Tools/ArtPipeline/fetch_assets.py`)

Declarative plan in `asset_requests.json`. The tool:
queries metadata first · prefers CC0 · verifies SHA-256 · retries transient
failures with bounded exponential backoff · rejects HTML masquerading as an
asset · extracts ZIPs safely (no path traversal) · normalises filenames ·
avoids duplicates · supports `--dry-run` · never downloads 8K by default (2K) ·
keeps the first pass under 3 GB · never fetches a whole provider library.

```bash
python Tools/ArtPipeline/fetch_assets.py --requests Tools/ArtPipeline/asset_requests.json --dry-run
python Tools/ArtPipeline/fetch_assets.py --requests Tools/ArtPipeline/asset_requests.json
```

## Provenance is mandatory

Every asset has a record in `Assets/ThirdParty/asset_manifest.json` (regenerate
with `build_manifest.py`) capturing id, name, creator, source page, provider,
date, licence + URL, attribution, commercial-use, format, resolution/polycount,
modifications, LODs, Unity destination, SHA-256, AI-generation flag, notices.
An asset whose source/creator/licence cannot be established is **rejected**.

## Processing external models

Legally-sourced meshes are normalised on the dev machine with
`Tools/ArtPipeline/blender/process_external_asset.py` (apply transforms, clean
normals/doubles, LOD1/2, convex `_COL`, GLB re-export using the same naming
contract as the kit) → then imported by `KitAssetImporter`.
