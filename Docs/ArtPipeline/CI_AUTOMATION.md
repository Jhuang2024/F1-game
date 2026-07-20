# GitHub-native Unity art integration (CI)

The previous pass added assets + static code but nothing rendered, because the
Unity **editor** operations were never executed. This workflow executes them
inside GitHub Actions on Unity `2022.3.62f1` via GameCI.

## Workflow

`.github/workflows/unity-art-integration.yml`

| Trigger | Behaviour |
|---|---|
| `workflow_dispatch` (`mode: full`) | integrate + build + tests, open PR of generated files |
| `workflow_dispatch` (`mode: validate-only`) | integrate + build + tests, no PR |
| `push` to `claude/**` | same as full |
| `pull_request` → `main` | integrate + build + tests (validation), no PR |

Jobs:
1. **preflight** — verifies Unity licence secrets exist; **fails fast and names
   the missing secret** if not (never skips Unity and claims success).
2. **integrate** — checkout (LFS) → cache `Library` → `game-ci/unity-builder@v4`
   runs `CIBuild.IntegrateAndBuild` (art integration + standalone build in one
   Unity session) → `game-ci/unity-test-runner@v4` (EditMode + PlayMode) →
   uploads report / test-results / screenshots / build / logs → opens a PR from
   `automation/unity-art-integration` into `main` with the generated assets.

Loop-safe: the `push` trigger watches only `claude/**` (never `automation/**`),
and the generated commit carries `[skip ci]`. Concurrency-guarded, timeouts set.

## Required GitHub secrets

Provide **either** a personal licence **or** the pro/plus trio (never printed):

| Secret | Licence type |
|---|---|
| `UNITY_LICENSE` | Personal — full contents of the `.ulf` activation file |
| `UNITY_EMAIL` + `UNITY_PASSWORD` + `UNITY_SERIAL` | Pro/Plus |

Set them under **Settings → Secrets and variables → Actions**. The `GITHUB_TOKEN`
used for the PR is provided automatically.

## What the Unity session does (`AutomatedArtIntegration.Run`)

Reuses `KitAssetImporter.RunImport` and `UrpMaterialBuilder.RunBuild` (no
duplicated logic), then creates + populates 9 `CircuitVisualProfile` assets,
builds the `CircuitProfileCatalog` (Resources) mapping circuits → profiles,
validates materials + profile slots, saves + refreshes, prints a structured
report, and **exits non-zero on any failure**. `CIBuild.IntegrateAndBuild` then
produces a `StandaloneLinux64` build and attempts screenshots.

## Runtime effect

`TrackManager.Build` calls `RuntimeTrackDressing.TryDress` at the tail of every
track build. It loads the catalog from Resources, selects the circuit's profile,
and places the modular kit as an **additive, visual-only** layer (colliders
disabled → cannot block the racing line, create invisible walls, or break pit
entry/exit; deduped on reload; manual overrides preserved). Until the catalog is
generated + committed it is a **no-op**, so the hook is safe to merge before the
art exists — and activates automatically once the PR from this workflow lands.

## Honest status / blocker

This workflow file, the C# entry points and the runtime hook are committed and
static-validated. A **successful run requires the Unity licence secrets above**,
which are the repository owner's Unity credentials and cannot be provisioned from
outside the repo. If they are absent, the `preflight` job fails immediately with
the exact missing-secret name — by design. Add the secrets, then re-run
(`workflow_dispatch`) to complete the integration.
