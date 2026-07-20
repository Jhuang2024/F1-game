# Procedural track dressing

`Assets/Game/Code/Track/Dressing/` — a non-destructive presentation layer over
the authored circuit. It fills the documented gap in `TrackRuntimeBuilder`
(kerbs/barriers/runoff/environment) without a second track representation.

## Contract

- **Non-destructive:** never mutates `TrackDefinitionAsset`, the road mesh,
  racing line, colliders or any gameplay value. Only spawns child objects.
- **Deterministic:** output is a pure function of (definition, profile, seed).
  Uses `System.Random` only — never `UnityEngine.Random`.
- **Reversible:** `Clear` removes exactly what it built; overrides survive.
- **Side-aware:** left/right edges and inside/outside of corners derived from
  signed curvature; honours exclusion zones and auto-opens the pit lane span.
- **Graceful:** missing prefab slots warn, never throw.

## Data it reads

The deterministic `TrackSplineSampler` (position/tangent/normal/width/distance)
plus authored `kerbLeft/kerbRight`, `surfaces`, `pitLane`, `marshalPosts`,
`cameraNodes`, `crowdZones`, `startFinishDistance`, `supportsNightSession`,
`environmentStyle`.

## What it places

barriers along both edges (concrete on straights, armco/tyres in corners, no
gaps — stepped by arc length) · kerbs on authored/curved corners (painted +
sausage) · catch fencing behind barriers · start gantry at S/F · marshal posts &
camera towers at authored nodes · braking boards before detected corner entries
· grandstands over crowd zones · light towers for night circuits · scattered
vegetation/clutter in a lateral band.

## Circuit visual profiles

`CircuitVisualProfile` (ScriptableObject) = one visual identity for a class of
circuits (green permanent, desert, tropical, forest, urban street, night urban,
coastal, high-altitude). Map a circuit via `TrackDefinitionAsset.environmentStyle`
keywords, then add per-circuit overrides. Assign the imported kit prefabs to the
profile's slots. Create many circuits from a few profiles instead of 24
independent sets.

## Manual overrides

Objects under a child named `Overrides`, or carrying `DressingOverrideMarker`,
are rescued across regeneration.

## Usage

Attach `TrackDressing` to an empty child of the track world; assign a definition
+ profile + seed; **Regenerate** (context menu) or
**F1 Game → Art → Dress Open Scene (reference circuit)**. Orientation assumes kit
modules are authored length-along-local-X (matches the Blender→y-up-GLB export);
adjust profile module lengths if you re-author a module along a different axis.
