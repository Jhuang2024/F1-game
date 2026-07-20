# Performance budgets & repo hygiene

Target stable desktop performance; profile each optimisation rather than guess.

## Geometry / texture budgets

| Class | LOD0 tris | Textures |
|---|---|---|
| Hero car | ≤ 120k (LOD1 45k / LOD2 12k / LOD3 2.5k) | 4K body, 2K wheels/cockpit |
| Nearby opponent | ≤ 60k | 2K |
| Distant opponent | ≤ 12k | 1K / atlas |
| Large structure (grandstand, gantry) | ≤ 3k (kit LOD0 is far under) | 2K shared |
| Medium prop | ≤ 1.5k | 1–2K |
| Small prop | ≤ 500 | 1K |
| Vegetation | billboard/impostor at distance | atlas |

The generated kit LOD0s are 24–536 tris (see `THIRD_PARTY_ASSETS.md`) — trivial;
budget headroom is spent on the car and density, not module complexity.

## Texture policy

2K default for environment · 1K for small props · 4K only for a justified hero
asset · **no 8K runtime textures** · atlas where sensible · GPU instancing for
repeated props (importer enables it) · LODGroups on all kit prefabs · impostors
for distant vegetation · occlusion culling · pooled instances for runtime spawns.

## Runtime budgets (measure on the dev machine, record here)

draw calls, visible tris, shadow casters, reflection probes, decals, CPU
dressing-generation time, GPU frame time, memory, build size — capture
before/after in `VisualComparisons/`.

## Repo hygiene / Git LFS

`.gitattributes` routes `*.blend *.fbx *.glb *.gltf *.exr *.hdr *.tif *.tiff`
through LFS (run `git lfs install` on the dev machine). PNGs stay in normal git
(the 20 surface sets total ~12 MB at 1K — not worth LFS). Never commit: `Library`,
`Temp`, `Logs`, downloads, extracted ZIPs, `.blend1`, `bpyenv`, failed output,
`.env`, API responses, local paths, or licence-uncertain assets — all gitignored.

> git-lfs is not installable in the cloud container (no distro mirror in the
> egress policy), so the initial tiny GLBs are committed as plain blobs. Run
> `git lfs migrate import --include="*.glb"` on the dev machine if the binary
> footprint ever grows enough to justify rewriting history.
