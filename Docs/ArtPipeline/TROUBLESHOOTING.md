# Troubleshooting

**Asset APIs return 403 / `CONNECT tunnel failed`.** The egress policy denies
`ambientcg.com`, `api.polyhaven.com`, `download.blender.org`. This is expected in
the cloud container — run `fetch_assets.py` / desktop-Blender steps on the dev
machine. Never disable TLS verification or unset the proxy.

**`ModuleNotFoundError: bmesh`.** Import `bpy` before `bmesh` (bmesh initialises
with Blender). The build scripts already order imports correctly.

**`ReferenceError: StructRNA of type Material has been removed`.** A stale
material handle after `read_factory_settings`. `kitlib.reset_scene()` clears the
material cache each build; don't hold `bpy.data` references across a scene reset.

**GLB imports but no LODGroup/collider in Unity.** Run **F1 Game → Art → Import
Kit GLBs → Prefabs**. If it says "not imported — is glTFast installed?", let
Package Manager resolve `com.unity.cloud.gltfast` first.

**URP/Lit shader not found.** Ensure URP is the active pipeline before running
the material builder.

**Content validator FAILs on missing script `.meta`.** Every `.cs` needs a
committed `.meta` (stable GUID). On Linux without Unity, generate them with
`Tools/ArtPipeline/gen_unity_meta.py`; Unity generates them automatically on
import on the dev machine.

**Unity MCP tools not visible in Claude.** The editor must be open (it hosts the
bridge); a new Claude session may need a reload. Meanwhile use the editor menus
or Unity batch mode — the pipeline never depends on live MCP.

**Blender MCP won't connect.** The socket needs the desktop GUI (N → BlenderMCP →
Connect to Claude); it cannot run in `--background`. Headless generation via the
`bpy` module does not use the socket.

**Reproduce the whole pipeline:** see README "Reproduce everything".
