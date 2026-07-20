# Art Pipeline — Setup & Environment Report

_Generated during the art-pipeline overhaul. No secrets recorded._

## 1. Repository facts (confirmed)

| Item | Value |
|------|-------|
| Repository | `Jhuang2024/F1-game` |
| Remote | `origin` (proxy-injected GitHub token) |
| Working branch | `claude/f1-game-art-pipeline-1szwf7` |
| Base commit at start | `a9c892d` (Time Trial [GhostDiag] …) |
| Unity editor version | `2022.3.62f1` (from `ProjectSettings/ProjectVersion.txt`) |
| Render pipeline | Universal Render Pipeline **14.0.11** (`com.unity.render-pipelines.universal`) |
| Input | `com.unity.inputsystem` 1.7.0 |
| Cinemachine | 2.9.7 |
| Test framework | `com.unity.test-framework` 1.1.33 |
| Working tree at start | clean |

Confirmed: URP 14, Unity 2022.3.62f1, existing gameplay/circuit/simulation
systems intact. **No HDRP migration performed. No gameplay, physics, AI,
racing-line, pit, RNG, save-format, circuit-geometry or tuning value changed.**

## 2. Machine facts (this session — IMPORTANT)

The task text is written for the developer's **macOS Apple-Silicon** machine
with Unity Hub and Blender GUI. **This session does not run on that machine.**
It runs in an ephemeral Linux cloud container:

| Item | Value |
|------|-------|
| OS | Ubuntu 24.04.4 LTS |
| Kernel | Linux 6.18.5 |
| CPU arch | **x86_64** (not Apple Silicon) |
| RAM | 15 GiB |
| Disk (writable allowance) | ~30 GiB free |
| Display / GUI | none (headless) |

### Toolchain detected

| Tool | Status |
|------|--------|
| git | 2.43.0 ✅ |
| git-lfs | **absent** — cannot `apt install` (no distro mirror in egress policy); LFS `.gitattributes` staged for the dev machine to honour |
| python3 | 3.11.15 ✅ |
| uv / uvx | 0.8.17 ✅ |
| node / npm / npx | 22.22.2 / 10.9.7 ✅ |
| Homebrew | absent (Linux container; not applicable — macOS-only steps deferred to dev machine) |
| Claude Code | 2.1.211 ✅ |
| Blender (desktop app) | **absent**; `download.blender.org` is policy-denied |
| Blender as `bpy` PyPI module | **installed 5.0.1** ✅ — headless Python API, used for the asset factory |
| Unity Hub / Editor 2022.3.62f1 | **absent** — cannot be installed/licensed/run here |

### Egress policy (agent proxy)

Outbound HTTPS is filtered by an organisation egress policy. Verified reachable:
`raw.githubusercontent.com`, `pypi.org`/`files.pythonhosted.org`, npm registry,
`github.com` git operations (token-injected). **Verified denied (HTTP 403 at the
proxy):** `ambientcg.com`, `api.polyhaven.com`, `download.blender.org`,
`cdn.jsdelivr.net`. Per the proxy README these denials must be reported, not
routed around.

## 3. What this environment can and cannot do

**Can be done and verified here (and was):**
- Full repo/machine audit.
- **Headless Blender procedural asset generation** via the `bpy` module →
  real `.glb` modular kit with LOD variants and collision proxies
  (`Tools/ArtPipeline/blender/`, output under `Assets/Art/`). Verified by
  re-parsing every exported GLB.
- **Procedural PBR texture generation** (NumPy) as the substitute for the
  blocked CC0 CDNs → real `.png` map sets under `Assets/Art/Materials/`.
- The asset-acquisition tool (`Tools/ArtPipeline/fetch_assets.py`) — written,
  self-tested in dry-run; live download blocked by egress policy (see §2).
- All Unity C# tooling, `.mcp.json`, `manifest.json` edits, `.gitattributes`,
  documentation, asset manifest with real SHA-256 checksums.

**Cannot be done here (genuine environment barriers — deferred to the dev machine):**
- Running the Unity editor: import, C# compilation, EditMode/PlayMode tests,
  Play-Mode lap, in-editor prefab/LODGroup/material authoring, screenshots.
- Unity MCP **live** connection (needs the editor running).
- Blender MCP **live socket** (needs the Blender GUI; headless generation is
  used instead).
- Downloading from Poly Haven / ambientCG (egress-denied).
- Homebrew / macOS cask installs.
- `MESHY_API_KEY` — not present; Meshy left optional/unused.

The repo already documents this exact constraint for its own code
(_"Static-only: none of this has been compiled, built, or run in Unity in this
environment"_ — `Docs/TRACK_PIPELINE.md`). This work follows the same
convention: **correct static C# + real out-of-Unity generated binary assets +
honest docs.** Nothing is claimed to render or pass in Unity that was not
actually run.

## 4. Consequence for publishing

Phase 19 asks to fast-forward `main`. Because Unity validation (Phase 18)
**cannot be executed in this container**, the completed work is committed to the
designated branch `claude/f1-game-art-pipeline-1szwf7` and **not** fast-forwarded
onto `main` from here. Fast-forwarding unverified editor C# onto `main` would
violate the task's own rule ("do not claim … unless actually verified") and
risks breaking project compilation. The single remaining action for the
developer is to open the branch in Unity `2022.3.62f1` on the macOS machine and
run Phase 18 validation before merging. See `Docs/ArtPipeline/README.md` §
"Validation checklist for the dev machine".
