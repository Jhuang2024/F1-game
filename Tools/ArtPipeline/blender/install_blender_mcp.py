"""
install_blender_mcp.py — install & enable the upstream Blender MCP addon.

Phase 3 of the art pipeline. Run through a *desktop* Blender (the `bpy` PyPI
module cannot host the live MCP socket — that needs the GUI):

    curl -L https://raw.githubusercontent.com/ahujasid/blender-mcp/main/addon.py \
        -o /tmp/blender_mcp_addon.py
    <blender> --background --python install_blender_mcp.py

It copies the inspected upstream addon into the user addon directory as
`blender_mcp.py`, refreshes modules, enables it and saves preferences. It never
starts the socket automatically (that is a deliberate user action — press N in
the 3D view → BlenderMCP tab → Connect to Claude).

Only run addons you have inspected. The upstream addon executes arbitrary
Blender Python by design; treat it as trusted-but-verify.
"""

import bpy
import os
import shutil

SRC = os.environ.get("BLENDER_MCP_ADDON", "/tmp/blender_mcp_addon.py")


def main():
    if not os.path.exists(SRC):
        print("[install] addon source not found:", SRC)
        print("[install] download it first:")
        print("  curl -L https://raw.githubusercontent.com/ahujasid/"
              "blender-mcp/main/addon.py -o", SRC)
        return 1
    addon_dir = bpy.utils.user_resource("SCRIPTS", path="addons", create=True)
    dst = os.path.join(addon_dir, "blender_mcp.py")
    shutil.copyfile(SRC, dst)
    print("[install] copied ->", dst)

    bpy.ops.preferences.addon_refresh()
    try:
        bpy.ops.preferences.addon_enable(module="blender_mcp")
        print("[install] enabled module 'blender_mcp'")
    except Exception as e:  # noqa: BLE001
        print("[install] FAILED to enable:", e)
        return 2
    bpy.ops.wm.save_userpref()
    print("[install] preferences saved. Next (GUI only):")
    print("  N in 3D View -> BlenderMCP tab -> enable Poly Haven -> Connect to Claude")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
