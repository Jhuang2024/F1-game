"""
export_game_assets.py — build & export the entire procedural kit.

Canonical entry point (alias of build_kit.main). Run:
    python Tools/ArtPipeline/blender/export_game_assets.py -- <repo_root>
or via desktop Blender:
    <blender> --background --python export_game_assets.py -- <repo_root>
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kitlib  # noqa: F401  (init bpy first)
import build_kit
if __name__ == "__main__":
    build_kit.main()
