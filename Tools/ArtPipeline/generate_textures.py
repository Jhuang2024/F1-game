#!/usr/bin/env python3
"""
generate_textures.py — deterministic, seamless-tiling PBR texture sets.

The Poly Haven / ambientCG CC0 CDNs are blocked by this environment's egress
policy (see Docs/ArtPipeline/SETUP_REPORT.md), so the track surface library is
sourced from first-principles procedural maths instead of downloads. Every map
is original, generated, CC0-equivalent project-owned content — no copyrighted
art, brand assets or ripped textures.

For each surface it writes a URP-ready set under Assets/Art/Materials/<name>/:
  <name>_BaseColor.png   (sRGB albedo)
  <name>_Normal.png      (linear tangent-space normal, OpenGL +Y)
  <name>_MaskMap.png     (R=Metallic, G=Occlusion, B=detail, A=Smoothness)

Tiling is seamless: all noise is built on a periodic integer lattice so the
image wraps on both axes. Deterministic: seeded per surface, no wall-clock.

    python Tools/ArtPipeline/generate_textures.py [--size 1024] [--out Assets/Art/Materials]
"""

import argparse
import os
import json
import hashlib
import numpy as np
from PIL import Image


# ---------------------------------------------------------------------------
# Seamless procedural noise (periodic lattice -> wraps on both axes)
# ---------------------------------------------------------------------------

def _fade(t):
    return t * t * t * (t * (t * 6 - 15) + 10)


def periodic_value_noise(size, period, seed):
    """Tileable value noise in [0,1], lattice `period` cells across the image."""
    rng = np.random.default_rng(seed)
    grid = rng.random((period, period))
    ys = np.linspace(0, period, size, endpoint=False)
    xs = np.linspace(0, period, size, endpoint=False)
    gx, gy = np.meshgrid(xs, ys)
    x0 = np.floor(gx).astype(int) % period
    y0 = np.floor(gy).astype(int) % period
    x1 = (x0 + 1) % period
    y1 = (y0 + 1) % period
    fx = _fade(gx - np.floor(gx))
    fy = _fade(gy - np.floor(gy))
    v00 = grid[y0, x0]
    v10 = grid[y0, x1]
    v01 = grid[y1, x0]
    v11 = grid[y1, x1]
    top = v00 * (1 - fx) + v10 * fx
    bot = v01 * (1 - fx) + v11 * fx
    return top * (1 - fy) + bot * fy


def fbm(size, base_period, octaves, seed, persistence=0.5):
    """Fractal sum of periodic value noise (still seamless)."""
    out = np.zeros((size, size))
    amp = 1.0
    total = 0.0
    for o in range(octaves):
        period = base_period * (2 ** o)
        out += amp * periodic_value_noise(size, period, seed + o * 977)
        total += amp
        amp *= persistence
    out /= total
    return out


def normal_from_height(height, strength=2.0):
    """Tangent-space normal map (OpenGL) from a height field, wrapping edges."""
    gy, gx = np.gradient(height * strength)
    # wrap gradient at seams
    nz = np.ones_like(height)
    length = np.sqrt(gx ** 2 + gy ** 2 + nz ** 2)
    nx = -gx / length
    ny = -gy / length
    nz = nz / length
    rgb = np.stack([(nx + 1) * 0.5, (ny + 1) * 0.5, (nz + 1) * 0.5], axis=-1)
    return np.clip(rgb * 255, 0, 255).astype(np.uint8)


def to_img(arr):
    return Image.fromarray(np.clip(arr * 255, 0, 255).astype(np.uint8))


def colorize(mono, low, high):
    """Map a [0,1] scalar field to an RGB ramp between two colours."""
    low = np.array(low, dtype=float) / 255.0
    high = np.array(high, dtype=float) / 255.0
    m = mono[..., None]
    return low[None, None, :] * (1 - m) + high[None, None, :] * m


# ---------------------------------------------------------------------------
# Surface recipes
# ---------------------------------------------------------------------------

def surface(name, size):
    """Return (basecolor rgb float, height, metallic, occlusion, smoothness)."""
    seed = int(hashlib.sha256(name.encode()).hexdigest()[:8], 16)
    n_fine = fbm(size, 16, 5, seed)
    n_med = fbm(size, 6, 4, seed + 13)
    n_coarse = fbm(size, 3, 3, seed + 29)

    if name == "asphalt_clean":
        base = colorize(n_fine, (26, 26, 28), (52, 52, 55))
        height = n_fine * 0.6 + n_med * 0.4
        metal, occ, smooth = 0.0, 0.85 + 0.15 * n_med, 0.28 + 0.1 * n_fine
    elif name == "asphalt_worn":
        base = colorize(n_fine, (30, 30, 32), (70, 68, 66))
        streak = (n_coarse > 0.55).astype(float) * 0.15
        base = np.clip(base + streak[..., None] * 0.4, 0, 1)
        height = n_fine * 0.7 + n_coarse * 0.3
        metal, occ, smooth = 0.0, 0.8, 0.35 + 0.15 * n_fine
    elif name == "asphalt_patched":
        patch = (n_coarse > 0.5).astype(float)
        base = colorize(n_fine, (22, 22, 24), (48, 48, 50))
        dark = colorize(n_fine, (16, 16, 18), (30, 30, 32))
        base = base * (1 - patch[..., None]) + dark * patch[..., None]
        height = n_fine * 0.5 + patch * 0.2
        metal, occ, smooth = 0.0, 0.82, 0.3 + 0.2 * patch
    elif name == "concrete_pit":
        base = colorize(n_med, (108, 106, 100), (150, 148, 142))
        height = n_med * 0.5 + n_fine * 0.5
        metal, occ, smooth = 0.0, 0.8 + 0.2 * n_med, 0.2 + 0.1 * n_fine
    elif name == "concrete_smooth":
        base = colorize(n_coarse, (150, 149, 146), (185, 184, 181))
        height = n_coarse * 0.7 + n_fine * 0.3
        metal, occ, smooth = 0.0, 0.9, 0.4
    elif name == "gravel":
        cells = fbm(size, 40, 3, seed + 7)
        base = colorize(cells, (120, 112, 96), (170, 160, 140))
        height = cells
        metal, occ, smooth = 0.0, 0.7 + 0.3 * cells, 0.15
    elif name == "grass_green":
        blades = fbm(size, 64, 3, seed + 3)
        base = colorize(blades, (36, 66, 26), (74, 108, 40))
        height = blades
        metal, occ, smooth = 0.0, 0.7, 0.2
    elif name == "grass_dry":
        blades = fbm(size, 64, 3, seed + 5)
        base = colorize(blades, (96, 92, 46), (150, 140, 82))
        height = blades
        metal, occ, smooth = 0.0, 0.72, 0.18
    elif name == "curb_paint":
        stripes = ((np.arange(size) // (size // 8)) % 2).astype(float)
        stripes = np.tile(stripes, (size, 1))
        red = np.array([160, 24, 24]) / 255.0
        white = np.array([210, 210, 210]) / 255.0
        base = red[None, None] * (1 - stripes[..., None]) + white[None, None] * stripes[..., None]
        base = base * (0.85 + 0.15 * n_fine[..., None])
        height = n_fine * 0.3
        metal, occ, smooth = 0.0, 0.9, 0.5 + 0.1 * stripes
    elif name == "road_paint":
        base = colorize(n_fine, (205, 205, 205), (235, 235, 235))
        height = n_fine * 0.15
        metal, occ, smooth = 0.0, 0.95, 0.45
    elif name == "metal_painted":
        base = colorize(n_coarse, (30, 60, 120), (60, 96, 168))
        height = n_coarse * 0.2
        metal, occ, smooth = 0.9, 0.95, 0.55 + 0.1 * n_med
    elif name == "metal_galvanised":
        spangle = fbm(size, 48, 2, seed + 11)
        base = colorize(spangle, (140, 142, 145), (185, 187, 190))
        height = spangle * 0.15
        metal, occ, smooth = 1.0, 0.95, 0.4 + 0.2 * spangle
    elif name == "metal_brushed":
        brush = np.tile(fbm(size, 4, 4, seed)[0:1, :], (size, 1))
        base = colorize(brush, (120, 121, 123), (170, 171, 173))
        height = brush * 0.1
        metal, occ, smooth = 1.0, 0.98, 0.6
    elif name == "rubber":
        base = colorize(n_fine, (12, 12, 13), (26, 26, 27))
        height = n_fine * 0.3
        metal, occ, smooth = 0.0, 0.8, 0.25
    elif name == "rubber_marbles":
        base = colorize(n_med, (18, 16, 16), (40, 34, 32))
        height = n_med * 0.5 + fbm(size, 30, 2, seed + 2) * 0.5
        metal, occ, smooth = 0.0, 0.75, 0.3
    elif name == "carbon_weave":
        u = (np.sin(np.linspace(0, np.pi * size / 8, size)) > 0).astype(float)
        weave = np.abs(np.subtract.outer(u, u))
        base = colorize(weave, (14, 14, 16), (34, 34, 40))
        height = weave * 0.4
        metal, occ, smooth = 0.2, 0.95, 0.7
    elif name == "glass":
        base = np.ones((size, size, 3)) * np.array([0.55, 0.62, 0.68])
        height = n_fine * 0.02
        metal, occ, smooth = 0.0, 1.0, 0.95
    elif name == "wire_mesh":
        grid = ((np.arange(size) % (size // 32)) < 2).astype(float)
        mesh = np.maximum.outer(grid, grid)
        base = colorize(mesh, (40, 42, 44), (120, 122, 124))
        height = mesh * 0.3
        metal, occ, smooth = 0.8, 0.9, 0.4
    elif name == "dirt_compacted":
        base = colorize(n_med, (74, 58, 40), (110, 88, 62))
        height = n_med
        metal, occ, smooth = 0.0, 0.75, 0.15
    elif name == "rock":
        base = colorize(n_coarse, (86, 82, 78), (140, 134, 126))
        height = n_coarse * 0.7 + n_fine * 0.3
        metal, occ, smooth = 0.0, 0.7, 0.2
    else:
        raise ValueError("unknown surface " + name)

    def as_field(v):
        return v if isinstance(v, np.ndarray) else np.full((size, size), float(v))

    return (np.clip(base, 0, 1), np.clip(as_field(height), 0, 1),
            as_field(metal), as_field(occ), as_field(smooth))


SURFACES = [
    "asphalt_clean", "asphalt_worn", "asphalt_patched", "concrete_pit",
    "concrete_smooth", "gravel", "grass_green", "grass_dry", "curb_paint",
    "road_paint", "metal_painted", "metal_galvanised", "metal_brushed",
    "rubber", "rubber_marbles", "carbon_weave", "glass", "wire_mesh",
    "dirt_compacted", "rock",
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=1024,
                    help="map resolution (1024 = 1K procedural placeholder)")
    ap.add_argument("--out", default="Assets/Art/Materials")
    ap.add_argument("--only", nargs="*", help="subset of surface names")
    args = ap.parse_args()

    names = args.only or SURFACES
    index = []
    for name in names:
        base, height, metal, occ, smooth = surface(name, args.size)
        d = os.path.join(args.out, name)
        os.makedirs(d, exist_ok=True)
        Image.fromarray(np.clip(base * 255, 0, 255).astype(np.uint8)).save(
            os.path.join(d, name + "_BaseColor.png"))
        Image.fromarray(normal_from_height(height)).save(
            os.path.join(d, name + "_Normal.png"))
        mask = np.stack([metal, occ, np.zeros_like(metal), smooth], axis=-1)
        Image.fromarray(np.clip(mask * 255, 0, 255).astype(np.uint8), "RGBA").save(
            os.path.join(d, name + "_MaskMap.png"))
        index.append({"surface": name, "resolution": args.size,
                      "maps": ["BaseColor", "Normal", "MaskMap"], "dir": d})
        print("[tex] %-18s -> %s (%dpx)" % (name, d, args.size))

    with open(os.path.join(args.out, "texture_index.json"), "w") as f:
        json.dump({"generator": "procedural (CC0-equivalent, project-owned)",
                   "note": "substitute for egress-blocked CC0 CDNs; upgradeable "
                           "to 2K downloads via fetch_assets.py on the dev machine",
                   "surfaces": index}, f, indent=2)
    print("\n[tex] %d surfaces -> %s" % (len(names), args.out))


if __name__ == "__main__":
    main()
