# Materials & lighting

Keeps **URP 14** (no HDRP migration).

## Surface library

20 procedurally-generated tiling PBR sets under `Assets/Art/Materials/<surface>/`
(BaseColor sRGB, Normal linear, MaskMap linear). The MaskMap packs
R=Metallic, G=Occlusion, B=0, A=Smoothness — which maps onto URP/Lit's
`_MetallicGlossMap` (R metallic, A smoothness) and `_OcclusionMap` (G) using the
same texture. **F1 Game → Art → Build URP Materials From Generated Textures**
creates `M_<surface>.mat` and fixes importer settings.

Surfaces: asphalt (clean/worn/patched), concrete (pit/smooth), gravel, grass
(green/dry), curb paint, road paint, metal (painted/galvanised/brushed), rubber,
rubber-marbles, carbon weave, glass, wire mesh, compacted dirt, rock.

These supersede the flat `M_*_Placeholder` library (`MaterialLibrary`) when
wired in; the existing runtime `ProceduralSurfaceTextures` fallback still covers
any slot left unassigned, so nothing renders untextured.

## Wetness (design)

Wetness must not make everything uniformly mirror-like. The intended URP
approach (implement against the existing weather data — **do not change weather
simulation** to serve visuals): a global wetness float darkens porous albedo and
lowers roughness; a puddle mask concentrates standing-water reflection in low
areas; the racing line dries first if the weather system exposes usable data;
transitions are parameter lerps (no material swap, no popping). Wire to the
`RaceManager.Weather` read-only signals only.

## Decals

URP decal projector (enable the decal renderer feature): skid marks, braking
rubber, racing-line buildup, cracks, asphalt repairs, oil, curb wear, dirt,
grid markings, pit boxes, fictional track branding. Vary placement by seed to
avoid obvious repeats.

## Lighting / post profiles

Reusable URP volume profiles per condition: clear midday, overcast, golden hour,
dusk, night, wet-day, wet-night. Configure directional + ambient + skybox +
reflection/light probes, shadow distance/cascades, SSAO, tonemapping, restrained
bloom, colour adjustment, exposure, fog; DoF/motion-blur only on replay/cinematic
cameras. **Gameplay cameras stay readable at speed** — no bloom/CA/vignette/
motion-blur used to mask weak art. Hook into the existing
`Resources/LightingMoods` + `Resources/CameraProfiles` and
`RaceManager.Lighting` rather than adding a parallel system.
