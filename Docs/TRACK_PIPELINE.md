# Track pipeline

How circuits move from authored data to a live track world, and how the
authored-track runtime relates to the legacy procedural `TrackManager`.

## Data source of truth

`F1Game.Track.TrackDefinitionAsset` (ScriptableObject) is the authored source:
spline control points (position + width + camber + kerb flags), surfaces, DRS
zones, pit lane (entry / commit line / exit / stalls), grid slots, sector
boundaries, racing-line offsets, broadcast camera nodes, marshal posts, crowd
zones and lighting mood. `Validate()` reports authoring problems.

## Runtime path (authored → world)

1. `TrackSplineSampler` resamples the control points (Catmull-Rom) into an
   evenly-spaced centerline with tangent / normal / width / camber / cumulative
   distance. Deterministic: same data → same samples every run.
2. `AuthoredTrackRuntime` composes the query services over that sampling —
   `TrackSurfaceRuntime`, `TrackLimitRuntime`, `PitLaneRuntime`, `DrsRuntime`,
   `SectorRuntime`, `GridRuntime`, `AiLineRuntime`, `TrackCameraRuntime` — and
   exposes `GetProgress(worldPos)`. Its query surface mirrors the legacy
   `TrackRuntime` so a future swap is drop-in.
3. `TrackMeshBuilder` builds the road ribbon mesh + `MeshCollider` from the
   sampling, using the shared URP surface materials (`MaterialLibrary`).
4. `TrackRuntimeBuilder.Build(definition)` orchestrates: samples, builds the
   road, places grid slots / pit stalls / camera nodes / environment markers,
   and returns the world root plus the `AuthoredTrackRuntime` and a
   `TrackValidationReport`.

## Reference circuit

`ReferenceTrackGenerator.Generate()` builds an original fictional circuit
("Aurora Park") **deterministically in code, without opening Unity** — closed
loop, ~4.6 km, two DRS zones, three sectors, a 22-car grid, pit lane, camera
nodes. Editor menu **F1 Game → Track → Generate Reference Circuit Asset**
saves it to `Assets/Game/ScriptableObjects/Tracks`; **Build Authored Track
Preview** builds it through the runtime path in the scene.

This routes a reference circuit through the authored path end-to-end (data →
sampled runtime → mesh + colliders + markers), satisfying "the authored asset
is a runtime source, not merely an exporter target".

## Relationship to the legacy TrackManager (honest status)

The legacy procedural `TrackManager` (~11k lines) is still the **live race**
track: `RaceManager` queries its `TrackRuntime` for progress / pit / DRS /
surface across hundreds of call sites. Swapping those onto
`AuthoredTrackRuntime` is a Phase 3 monolith-split task (extract the
RaceManager track-query interface, then back it with either runtime). Until
then:

- Authored-track runtime is complete and buildable, exercised via the editor
  preview and the reference generator.
- The legacy procedural path remains the fallback for every circuit not yet
  migrated (all of them, currently).
- `TrackDataExporter` (editor) can sample a live legacy `TrackManager` build
  into a `TrackDefinitionAsset` as an alternative authoring route.

Static-only: none of this has been compiled, built, or run in Unity in this
environment.

## Import / external assets

Road/kerb/runoff PBR texture sets, modular environment kits (grandstands,
garages, barriers, vegetation) and track-specific landmarks are external-asset
work (Docs/ART_PIPELINE.md). The runtime uses the placeholder surface library
and lightweight environment markers until those land; nothing here claims the
placeholder surfaces or markers are final art.
