#!/usr/bin/env python3
"""
fetch_assets.py — deterministic, licence-aware asset acquisition for Poly Haven
and ambientCG.

Reads a declarative request file (asset_requests.json), queries each provider's
official API for metadata, downloads only the requested assets, verifies them,
records full source/licence provenance, and writes them under
Assets/ThirdParty/. It NEVER scrapes HTML when an API exists, prefers CC0,
caps the first pass under 3 GB, defaults to 2K, and refuses HTML error pages
masquerading as asset files.

Providers (official APIs only):
  * Poly Haven   — https://api.polyhaven.com   (https://polyhaven.com/our-api)
  * ambientCG    — https://ambientcg.com/api/v2/full_json

IMPORTANT — environment note:
  In the CI/cloud container both provider hosts are blocked by the egress
  policy (HTTP 403 at the proxy). Run this on the developer macOS machine where
  the hosts are reachable. `--dry-run` works anywhere (metadata plan only, no
  network writes) and is what the pipeline exercises in the blocked container.

Usage:
  python Tools/ArtPipeline/fetch_assets.py --requests Tools/ArtPipeline/asset_requests.json --dry-run
  python Tools/ArtPipeline/fetch_assets.py --requests Tools/ArtPipeline/asset_requests.json
"""

import argparse
import hashlib
import io
import json
import os
import re
import sys
import time
import urllib.request
import urllib.error
import zipfile

USER_AGENT = "F1GameArtPipeline/1.0 (+https://github.com/Jhuang2024/F1-game; contact via repo)"
POLYHAVEN_API = "https://api.polyhaven.com"
AMBIENTCG_API = "https://ambientcg.com/api/v2/full_json"

CC0 = "CC0 1.0"
MAX_FIRST_PASS_BYTES = 3 * 1024 * 1024 * 1024  # 3 GB
DEFAULT_RES = "2k"


# ---------------------------------------------------------------------------
# HTTP with bounded exponential backoff
# ---------------------------------------------------------------------------

def http_get(url, retries=4, timeout=60, binary=False):
    last = None
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=timeout) as r:
                data = r.read()
                ctype = r.headers.get("Content-Type", "")
                return data if binary else data.decode("utf-8"), ctype
        except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError) as e:
            last = e
            wait = 2 ** (attempt + 1)
            sys.stderr.write("  retry %d/%d after %ds (%s)\n"
                             % (attempt + 1, retries, wait, e))
            time.sleep(wait)
    raise RuntimeError("GET failed after %d retries: %s (%s)" % (retries, url, last))


def looks_like_html(blob):
    head = blob[:512].lstrip().lower()
    return head.startswith(b"<!doctype html") or head.startswith(b"<html")


# ---------------------------------------------------------------------------
# Provider metadata
# ---------------------------------------------------------------------------

def polyhaven_asset(slug):
    body, _ = http_get("%s/info/%s" % (POLYHAVEN_API, slug))
    return json.loads(body)


def polyhaven_files(slug):
    body, _ = http_get("%s/files/%s" % (POLYHAVEN_API, slug))
    return json.loads(body)


def ambientcg_index():
    body, _ = http_get(AMBIENTCG_API + "?type=Material&limit=1000&include=downloadData")
    return json.loads(body)


# ---------------------------------------------------------------------------
# Safe extraction / checksums
# ---------------------------------------------------------------------------

def sha256_bytes(blob):
    h = hashlib.sha256()
    h.update(blob)
    return h.hexdigest()


def safe_extract_zip(blob, dest):
    """Extract a ZIP, rejecting path traversal; return list of written files."""
    written = []
    with zipfile.ZipFile(io.BytesIO(blob)) as z:
        for member in z.namelist():
            target = os.path.normpath(os.path.join(dest, member))
            if not target.startswith(os.path.abspath(dest) + os.sep) and target != os.path.abspath(dest):
                raise RuntimeError("unsafe zip path: %s" % member)
        os.makedirs(dest, exist_ok=True)
        for member in z.namelist():
            if member.endswith("/"):
                continue
            safe_name = normalize_filename(os.path.basename(member))
            out = os.path.join(dest, safe_name)
            with z.open(member) as src, open(out, "wb") as dst:
                dst.write(src.read())
            written.append(out)
    return written


def normalize_filename(name):
    name = name.replace(" ", "_")
    return re.sub(r"[^A-Za-z0-9._-]", "", name)


# ---------------------------------------------------------------------------
# Acquisition
# ---------------------------------------------------------------------------

class Acquirer:
    def __init__(self, out_root, dry_run=False):
        self.out_root = out_root
        self.dry_run = dry_run
        self.total_bytes = 0
        self.records = []
        self.seen = set()

    def _register(self, rec):
        self.records.append(rec)

    def fetch_polyhaven(self, req):
        slug = req["slug"]
        if slug in self.seen:
            print("  skip duplicate", slug)
            return
        self.seen.add(slug)
        if self.dry_run:
            # true dry-run: plan from the request file, no network at all
            rec = {
                "asset_id": "ph_" + slug,
                "name": slug,
                "provider": "Poly Haven",
                "source_page": "https://polyhaven.com/a/" + slug,
                "license": CC0,
                "license_url": "https://polyhaven.com/license",
                "resolution": req.get("resolution", DEFAULT_RES),
                "category": req.get("category", "misc"),
                "purpose": req.get("purpose"),
                "planned": True,
            }
            self._register(rec)
            print("  [dry-run] plan %s (%s, %s)" % (slug, req.get("category"),
                  req.get("resolution", DEFAULT_RES)))
            return
        info = polyhaven_asset(slug)
        lic = info.get("license", "CC0")
        if req.get("cc0_only", True) and "cc0" not in lic.lower():
            print("  reject non-CC0:", slug, lic)
            return
        files = polyhaven_files(slug)
        res = req.get("resolution", DEFAULT_RES)
        rec = {
            "asset_id": "ph_" + slug,
            "name": info.get("name", slug),
            "creator": ", ".join(info.get("authors", {}).keys()) or "Poly Haven",
            "source_page": "https://polyhaven.com/a/" + slug,
            "provider": "Poly Haven",
            "license": CC0 if "cc0" in lic.lower() else lic,
            "license_url": "https://polyhaven.com/license",
            "attribution_required": False,
            "commercial_use": True,
            "resolution": res,
            "ai_generated": False,
            "downloads": [],
        }
        # pick maps/format (textures: hdri/tex; models: gltf)
        picked = self._pick_polyhaven_files(files, res, req)
        for url in picked:
            self._download_into(rec, url, category=req.get("category", "misc"))
        self._register(rec)

    def _pick_polyhaven_files(self, files, res, req):
        urls = []
        want = req.get("maps")  # optional explicit list
        # textures: files["Diffuse"|"nor_gl"|"rough"|"ao"|...][res]["png"|"jpg"]["url"]
        for mapname, resolutions in files.items():
            if isinstance(resolutions, dict) and res in resolutions:
                if want and mapname not in want:
                    continue
                fmt_dict = resolutions[res]
                for fmt in ("png", "jpg", "exr", "hdr", "gltf", "glb"):
                    if fmt in fmt_dict and "url" in fmt_dict[fmt]:
                        urls.append(fmt_dict[fmt]["url"])
                        break
        return urls

    def fetch_ambientcg(self, req, index=None):
        asset_id = req["id"]
        if asset_id in self.seen:
            print("  skip duplicate", asset_id)
            return
        self.seen.add(asset_id)
        res = req.get("resolution", DEFAULT_RES).upper()
        if self.dry_run:
            rec = {
                "asset_id": "acg_" + asset_id,
                "name": asset_id,
                "provider": "ambientCG",
                "source_page": "https://ambientcg.com/view?id=" + asset_id,
                "license": CC0,
                "license_url": "https://ambientcg.com/license",
                "resolution": req.get("resolution", DEFAULT_RES),
                "category": req.get("category", "Materials"),
                "purpose": req.get("purpose"),
                "planned": True,
            }
            self._register(rec)
            print("  [dry-run] plan %s (%s, %s)" % (asset_id, req.get("category"),
                  req.get("resolution", DEFAULT_RES)))
            return
        # find a PBR download url from full_json
        url = None
        if index:
            for a in index.get("foundAssets", []):
                if a.get("assetId") == asset_id:
                    for d in a.get("downloadFolders", {}).get("default", {}).get("downloadFiletypeCategories", {}).get("zip", {}).get("downloads", []):
                        attr = d.get("attribute", "")
                        if res in attr and "PNG" in attr:
                            url = d.get("downloadLink")
                            break
        rec = {
            "asset_id": "acg_" + asset_id,
            "name": asset_id,
            "creator": "Lennart Demes / ambientCG",
            "source_page": "https://ambientcg.com/view?id=" + asset_id,
            "provider": "ambientCG",
            "license": CC0,
            "license_url": "https://ambientcg.com/license",
            "attribution_required": False,
            "commercial_use": True,
            "resolution": req.get("resolution", DEFAULT_RES),
            "ai_generated": False,
            "downloads": [],
        }
        if not url:
            rec["error"] = "no matching %s PNG download in API index" % res
            self._register(rec)
            print("  no download url for", asset_id)
            return
        self._download_into(rec, url, category=req.get("category", "Materials"), is_zip=True)
        self._register(rec)

    def _download_into(self, rec, url, category, is_zip=None):
        dest = os.path.join(self.out_root, "Assets", "ThirdParty", category, rec["asset_id"])
        if is_zip is None:
            is_zip = url.lower().endswith(".zip")
        if self.dry_run:
            rec["downloads"].append({"url": url, "dest": dest, "dry_run": True})
            print("  [dry-run] would fetch %s -> %s" % (url, dest))
            return
        blob = http_get(url, binary=True)
        if isinstance(blob, tuple):
            blob = blob[0]
        if looks_like_html(blob):
            rec["error"] = "provider returned HTML for " + url
            print("  ERROR html masquerade:", url)
            return
        self.total_bytes += len(blob)
        if self.total_bytes > MAX_FIRST_PASS_BYTES:
            raise RuntimeError("first-pass 3 GB budget exceeded")
        os.makedirs(dest, exist_ok=True)
        checksum = sha256_bytes(blob)
        if is_zip:
            written = safe_extract_zip(blob, dest)
            rec["downloads"].append({"url": url, "sha256": checksum,
                                     "files": [os.path.relpath(w, self.out_root) for w in written]})
        else:
            fn = normalize_filename(os.path.basename(url.split("?")[0]))
            out = os.path.join(dest, fn)
            with open(out, "wb") as f:
                f.write(blob)
            rec["downloads"].append({"url": url, "sha256": checksum,
                                     "file": os.path.relpath(out, self.out_root)})
        print("  fetched %s (%d bytes, sha256=%s)" % (url, len(blob), checksum[:12]))

    def write_report(self, path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w") as f:
            json.dump({"dry_run": self.dry_run,
                       "total_bytes": self.total_bytes,
                       "count": len(self.records),
                       "records": self.records}, f, indent=2)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--requests", default="Tools/ArtPipeline/asset_requests.json")
    ap.add_argument("--out", default=".")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--report", default="Tools/ArtPipeline/downloads/fetch_report.json")
    args = ap.parse_args()

    with open(args.requests) as f:
        requests = json.load(f)

    acq = Acquirer(args.out, dry_run=args.dry_run)
    acg_index = None

    for req in requests.get("polyhaven", []):
        print("polyhaven:", req.get("slug"))
        try:
            acq.fetch_polyhaven(req)
        except Exception as e:  # noqa: BLE001
            print("  FAILED:", e)

    acg_reqs = requests.get("ambientcg", [])
    if acg_reqs and not args.dry_run:
        try:
            acg_index = ambientcg_index()
        except Exception as e:  # noqa: BLE001
            print("  ambientCG index failed:", e)
    for req in acg_reqs:
        print("ambientcg:", req.get("id"))
        try:
            acq.fetch_ambientcg(req, acg_index)
        except Exception as e:  # noqa: BLE001
            print("  FAILED:", e)

    acq.write_report(args.report)
    print("\n%s: %d records, %.1f MB, report -> %s"
          % ("DRY-RUN" if args.dry_run else "DONE", len(acq.records),
             acq.total_bytes / 1e6, args.report))


if __name__ == "__main__":
    main()
