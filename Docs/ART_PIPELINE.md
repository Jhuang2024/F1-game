# Art pipeline & asset import specification

> **See also `Docs/ArtPipeline/`** — the executable pipeline that implements much
> of this spec: a headless-Blender procedural asset factory (real GLB modules
> under `Assets/Art/`), a procedural URP surface library, a licence-aware
> acquisition tool, a Unity GLB→prefab importer, and a non-destructive
> procedural track-dressing system. This document remains the target spec for
> the car and final hero art; the new pipeline fills the modular-environment and
> surface-library gaps called out below.


Everything currently in the repo marked `*_Placeholder` or carrying a
`PlaceholderArtMarker` component is **not final art**. This document is the
spec the real assets must meet. Sources: original modelling, properly licensed
packs, or commissioned work. **No official Formula 1 branding, liveries,
logos, circuit likenesses or audio may be introduced.**

## Car (per CarRigSpec — `Assets/Game/Code/Vehicle/CarRigSpec.cs`)

Hierarchy/pivots are already defined and validated by
`F1 Game → Art → Validate Car Prefab`. The model must arrive as FBX with:

- Correct open-wheel silhouette; ~5.6 m length, ~2.0 m width, 3.6 m wheelbase.
- Separate meshes: chassis, front wing, rear wing (+ DRS flap hinged on local
  X), floor, sidepods, engine cover, halo, cockpit tub, steering wheel
  (rotation around column axis), driver (helmet/gloves/arms + simple rig),
  4 × (tyre, rim, brake disc), rain light.
- Pivots: suspension travel (vertical) per corner; steering yaw pivot on
  fronts; spin pivot on all wheels; DRS flap hinge.
- Clean non-overlapping UVs; texel density ≥ 512 px/m body, ≥ 256 px/m
  underside.
- Material slots exactly: Paint, CarbonFibre, Rubber, Metal, Glass, Decals,
  Emissive (URP/Lit; livery mask R=primary G=secondary B=accent in Paint's
  detail mask; number/name/sponsor layers via the Decals slot).
- PBR texture sets: BaseColor, Normal, MaskMap (metallic/AO/smoothness),
  Emissive. 4K body, 2K wheels/cockpit.
- LOD0 ≤ 120k tris, LOD1 ≤ 45k, LOD2 ≤ 12k, LOD3 ≤ 2.5k; LODGroup percentages
  already configured on the prefab (50/18/7/1.5%).
- Separate convex collision mesh ≤ 256 tris (replaces the box proxy).
- Damage: detachable front/rear wing meshes + optional damaged variants.
- Cockpit detail sufficient for the onboard camera (wheel display, mirrors).

## Track surface library (`Assets/Game/Resources/BaseMaterials`)

Each `M_*_Placeholder` material becomes a full URP/Lit PBR set (BaseColor +
Normal + MaskMap, 2K, tiling ~2 m): dry asphalt, rubbered line, wet asphalt,
painted lines, kerbs, concrete, gravel, grass, artificial turf, metal barrier,
tyre wall, fencing, pit concrete, garage floor. Decal set (grid boxes, pit
markings, skid marks, oil, drains, cracks, fictional sponsor markings) via
URP decal projector — added when the URP decal renderer feature is enabled.

## Reference track

Authored via `TrackDefinitionAsset` (see Docs/TRACK_PIPELINE section in
REFACTOR_MAP). One track only until the pipeline proves out. Environment kit
(garages, grandstands, trees, barriers) must be modular authored meshes with
LODs; the current procedural buildings/props remain explicitly interim.

## UI art

- Icon set (real iconography, not Unicode): navigation, tyres, weather, flags,
  input glyphs (kb/xbox/ps/wheel keyed by `DevicePromptService.GlyphId`).
  SVG-sourced, exported 64/128 px, 9-slice panels for cards/buttons.
- Until then the theme renders flat colour surfaces and text glyph ids — by
  design, so missing art is visible rather than faked.

## Audio banks (`AudioBank` asset, Resources/Audio/MainAudioBank)

Fill slots keyed by the existing generator names (e.g. "ui click",
"start light", "vehicle_engine_loop"). Engine: ≥ 5 RPM-band loops per
perspective (onboard/external), on/off-throttle variants; tyre scrub, lockup,
wheelspin, kerb, gravel, wind; impacts (carbon, barrier, wheel-to-wheel);
weather beds; crowd/pit ambience; UI set; radio processing chain. 48 kHz WAV,
loops seamless, -18 LUFS integrated for beds, -12 for one-shots.
