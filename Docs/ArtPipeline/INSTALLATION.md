# Installation

## Detected environment (this run)

Ubuntu 24.04 x86_64 cloud container. macOS-specific steps (Homebrew, Blender
cask, Unity Hub) are **not applicable here** and are deferred to the dev
machine; see [SETUP_REPORT.md](SETUP_REPORT.md) for the full audit and the exact
egress-policy denials.

## Command-line tools

Already present here: git 2.43, python 3.11, uv/uvx 0.8, node 22 / npm 10,
Claude Code 2.1. Absent: git-lfs (no distro mirror in policy), Blender desktop
app (`download.blender.org` denied), Unity.

On the macOS dev machine:
```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
brew install git git-lfs uv python@3.11 node
git lfs install
uv python install 3.11
brew install --cask blender@lts   # or: brew install --cask blender
```

## Blender for the asset factory

- **Headless generation (what the pipeline uses):** the official `bpy` PyPI
  module — `pip install bpy==5.0.1` into a Python 3.11 venv. No GUI, no
  download.blender.org. This is how the 26 GLB modules in this repo were built,
  here, and how they regenerate.
- **Live Blender MCP (optional):** needs the desktop app + GUI (socket cannot
  run headless). Install the addon with
  `Tools/ArtPipeline/blender/install_blender_mcp.py` after downloading
  `addon.py` (see below), then in the GUI: **N → BlenderMCP → enable Poly Haven
  → Connect to Claude**.

## Unity MCP (CoplayDev)

Added to `Packages/manifest.json`:
`"com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main"`.
In the editor: **Window → MCP for Unity → Configure All Detected Clients**
(prefer project scope). If Unity can't find `claude`, run `which claude` and
paste the absolute path into the setup window (Unity Hub does not inherit the
login-shell PATH).

## Blender MCP server (project-scoped)

`.mcp.json` at the repo root registers it (telemetry disabled):
```bash
claude mcp add --transport stdio --scope project \
  --env UV_PYTHON_PREFERENCE=only-managed --env DISABLE_TELEMETRY=true \
  --env BLENDER_HOST=localhost --env BLENDER_PORT=9876 \
  blender -- uvx --python 3.11 blender-mcp
claude mcp list && claude mcp get blender
curl -L https://raw.githubusercontent.com/ahujasid/blender-mcp/main/addon.py -o /tmp/blender_mcp_addon.py
```

## Reconnecting MCP after a restart

- **Unity MCP:** open the project (the editor hosts the bridge), then re-run
  Configure All Detected Clients if the client list is empty. A fresh Claude
  session may need a reload to see newly-registered Unity tools — meanwhile the
  editor menus (`F1 Game → Art → …`) and Unity batch mode do the same work.
- **Blender MCP:** launch desktop Blender, N → BlenderMCP → Connect to Claude.
  Headless generation never needs the socket.

## Meshy (optional, unused here)

`MESHY_API_KEY` is absent → Meshy is unavailable and the pipeline uses
procedural generation. To enable on the dev machine: `npx skills add
meshy-dev/meshy-3d-agent`, set `MESHY_API_KEY` in an ignored `.env` (never in
`.mcp.json`, code, or logs). Use only for generic trackside props, low-cost
previews first. See [LICENSING.md](LICENSING.md).
