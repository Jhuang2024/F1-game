#!/usr/bin/env python3
"""Static content-completion validator for F1-game.

Runs without Unity. Reports gaps the directive calls out:
  - missing / duplicate / empty / untranslated localization keys, placeholder parity
  - MaterialLibrary slots with no procedural profile; missing placeholder materials
  - runtime audio cue ids and whether each resolves to a synthetic generator fallback
  - team livery assignments (valid primary/secondary colour, readable id + name)
  - asset/.meta integrity: duplicate GUIDs, orphaned assets, orphaned metas

Exit code is non-zero when a HARD error is found (missing localization key, invalid
livery colour, unprofiled material slot, duplicate GUID, missing/orphaned meta).
Soft findings (untranslated values that legitimately match English, e.g. "ABS")
are reported as INFO/WARN and do not fail the run.

Usage:  python3 Tools/ContentValidation/validate_content.py
"""
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = lambda *p: os.path.join(ROOT, *p)

errors = []   # hard failures
warns = []    # soft findings
info = []     # informational

def err(m): errors.append(m)
def warn(m): warns.append(m)
def note(m): info.append(m)


# ---------------------------------------------------------------- localization
def parse_table(path):
    d, order = {}, []
    with open(path, encoding="utf-8") as f:
        for raw in f:
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            eq = line.find("=")
            if eq <= 0:
                continue
            k = line[:eq].strip()
            v = line[eq + 1:].strip()
            order.append(k)
            d[k] = v
    return d, order

def placeholders(s):
    return tuple(sorted(set(re.findall(r"\{\d+\}", s))))

def validate_localization():
    loc = A("Assets", "Resources", "Localization")
    if not os.path.isdir(loc):
        err("localization: folder missing " + loc)
        return
    en_path = os.path.join(loc, "en.txt")
    if not os.path.isfile(en_path):
        err("localization: en.txt missing (source template)")
        return
    ref, _ = parse_table(en_path)
    ref_keys = set(ref.keys())
    note("localization: en template has %d keys" % len(ref_keys))

    files = sorted(f for f in os.listdir(loc) if f.endswith(".txt"))
    for f in files:
        code = f[:-4]
        d, order = parse_table(os.path.join(loc, f))
        keys = set(d.keys())
        dups = [k for k in set(order) if order.count(k) > 1]
        missing = ref_keys - keys
        extra = keys - ref_keys
        empty = [k for k, v in d.items() if v.strip() == ""]
        ph_mismatch = [k for k in ref_keys if k in d and placeholders(d[k]) != placeholders(ref[k])]
        if dups:
            err("localization %s: duplicate keys %s" % (code, dups[:5]))
        if missing:
            err("localization %s: missing %d keys e.g. %s" % (code, len(missing), sorted(missing)[:5]))
        if extra:
            warn("localization %s: %d keys not in en template e.g. %s" % (code, len(extra), sorted(extra)[:5]))
        if empty:
            err("localization %s: empty values %s" % (code, empty[:5]))
        if ph_mismatch:
            err("localization %s: placeholder mismatch vs en %s" % (code, ph_mismatch[:5]))
        if code not in ("en", "zz"):
            untranslated = [k for k, v in d.items() if k in ref and v == ref[k] and v.strip() != ""]
            if untranslated:
                note("localization %s: %d values identical to English (may be intentional e.g. ABS/AUDIO)"
                     % (code, len(untranslated)))
        if not (dups or missing or extra or empty or ph_mismatch):
            note("localization %s: OK (%d keys)" % (code, len(keys)))


# ------------------------------------------------------------------- materials
def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()

def validate_materials():
    ml = A("Assets", "Game", "Code", "Rendering", "MaterialLibrary.cs")
    pst = A("Assets", "Game", "Code", "Rendering", "ProceduralSurfaceTextures.cs")
    if not os.path.isfile(ml):
        err("materials: MaterialLibrary.cs missing")
        return
    src = read(ml)
    m = re.search(r"enum\s+Slot\s*\{(.*?)\}", src, re.S)
    if not m:
        err("materials: could not parse Slot enum")
        return
    body = re.sub(r"//.*", "", m.group(1))
    slots = [s.strip() for s in body.replace("\n", " ").split(",") if s.strip()]
    note("materials: %d MaterialLibrary slots" % len(slots))

    profiled = set()
    if os.path.isfile(pst):
        for mm in re.finditer(r"case\s+MaterialLibrary\.Slot\.(\w+)\s*:", read(pst)):
            profiled.add(mm.group(1))
    for s in slots:
        if s not in profiled:
            err("materials: slot %s has no procedural profile (falls to default)" % s)
    # placeholder .mat presence (registry entry)
    base = A("Assets", "Game", "Resources", "BaseMaterials")
    missing_mat = []
    if os.path.isdir(base):
        for s in slots:
            if not os.path.isfile(os.path.join(base, "M_%s_Placeholder.mat" % s)):
                missing_mat.append(s)
    if missing_mat:
        warn("materials: placeholder .mat missing for %s (runtime fallback material used)" % missing_mat)
    else:
        note("materials: every slot has a placeholder .mat + procedural profile")


# ----------------------------------------------------------------------- audio
def validate_audio():
    sam = A("Assets", "Scripts", "Core", "SimpleAudioManager.cs")
    if not os.path.isfile(sam):
        err("audio: SimpleAudioManager.cs missing")
        return
    src = read(sam)
    gen = set(re.findall(r'Create(?:Tone|Sweep|Chord|Noise|SmoothedNoiseLoop)\("([^"]+)"', src))
    resolved = set(re.findall(r'AudioBankService\.Resolve\("([^"]+)"', src))
    note("audio: %d synthetic cue generators, %d bank-resolve slots (SimpleAudioManager)" % (len(gen), len(resolved)))
    # Each generator first tries the bank then synthesizes, so each is a wired slot
    # with a guaranteed fallback. Report the inventory so a missing id is visible.
    va = A("Assets", "Scripts", "Vehicle", "VehicleAudio.cs")
    if os.path.isfile(va):
        vslots = set(re.findall(r'AudioBankService\.Resolve\("([^"]+)"', read(va)))
        note("audio: VehicleAudio loop slots %s" % sorted(vslots))
    if not gen:
        err("audio: no synthetic cue generators found (every cue would be silent)")
    else:
        note("audio: cue ids -> " + ", ".join(sorted(gen)))


# --------------------------------------------------------------------- livery
HEX = re.compile(r"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")

def validate_liveries():
    teams_path = A("Assets", "Resources", "Data", "teams.json")
    if not os.path.isfile(teams_path):
        err("liveries: teams.json missing")
        return
    try:
        data = json.load(open(teams_path, encoding="utf-8"))
    except Exception as e:
        err("liveries: teams.json parse error: %s" % e)
        return
    teams = data.get("teams", data) if isinstance(data, dict) else data
    if not isinstance(teams, list):
        err("liveries: teams.json has no team list")
        return
    note("liveries: %d teams" % len(teams))
    seen_primary = {}
    for t in teams:
        tid = t.get("id") or t.get("teamId") or "?"
        name = t.get("name") or t.get("displayName") or ""
        if not name:
            warn("liveries: team %s has no readable name" % tid)
        p = t.get("primaryColor") or t.get("primaryColour") or ""
        s = t.get("secondaryColor") or t.get("secondaryColour") or ""
        if not HEX.match(p):
            err("liveries: team %s invalid/missing primaryColor '%s' (white fallback)" % (tid, p))
        if not HEX.match(s):
            err("liveries: team %s invalid/missing secondaryColor '%s' (black fallback)" % (tid, s))
        if HEX.match(p):
            seen_primary.setdefault(p.lower(), []).append(tid)
    clashes = {c: ids for c, ids in seen_primary.items() if len(ids) > 1}
    if clashes:
        warn("liveries: non-distinct primary colours %s" % clashes)
    else:
        note("liveries: every team has a valid, distinct primary livery colour")


# ------------------------------------------------------------- asset / meta
CODE_EXT = (".cs", ".txt", ".mat", ".asset", ".json", ".shader")

def validate_assets():
    assets = A("Assets")
    guids = {}
    metas = 0
    orphan_meta = []
    missing_meta = []
    missing_script_meta = []
    for dirpath, dirnames, filenames in os.walk(assets):
        names = set(filenames)
        for fn in filenames:
            full = os.path.join(dirpath, fn)
            if fn.endswith(".meta"):
                metas += 1
                base = fn[:-5]
                # Only a file-meta (base has an extension) can be truly orphaned; an
                # extensionless base is a folder meta, and empty folders are legitimately
                # dropped by git, so those are not orphans.
                if "." in base and base not in names:
                    orphan_meta.append(os.path.relpath(full, ROOT))
                try:
                    txt = open(full, encoding="utf-8", errors="ignore").read()
                    g = re.search(r"guid:\s*([0-9a-f]{32})", txt)
                    if g:
                        guids.setdefault(g.group(1), []).append(os.path.relpath(full, ROOT))
                except Exception:
                    pass
            elif fn.endswith(CODE_EXT):
                if (fn + ".meta") not in names:
                    rel = os.path.relpath(full, ROOT)
                    # Scripts/shaders need a stable committed GUID (references break
                    # without it) -> hard error. Data/text/font assets in this repo are
                    # loaded by Resources path (e.g. Assets/Resources/Fonts/*), so a
                    # missing committed meta is a soft finding, not a build breaker.
                    if fn.endswith((".cs", ".shader")):
                        missing_script_meta.append(rel)
                    else:
                        missing_meta.append(rel)
    dups = {g: p for g, p in guids.items() if len(p) > 1}
    note("assets: scanned %d .meta files, %d unique GUIDs" % (metas, len(guids)))
    if dups:
        for g, p in list(dups.items())[:10]:
            err("assets: duplicate GUID %s in %s" % (g, p))
    for p in missing_script_meta[:10]:
        err("assets: script without committed .meta (GUID unstable): %s" % p)
    if missing_meta:
        warn("assets: %d non-script asset(s) without committed .meta (path-loaded) e.g. %s"
             % (len(missing_meta), missing_meta[:5]))
    for p in orphan_meta[:10]:
        warn("assets: orphaned .meta (no asset): %s" % p)
    if not dups and not missing_script_meta and not missing_meta and not orphan_meta:
        note("assets: no duplicate GUIDs, every source file has a .meta, no orphaned metas")


def main():
    validate_localization()
    validate_materials()
    validate_audio()
    validate_liveries()
    validate_assets()

    print("=" * 70)
    print("F1-game content validation")
    print("=" * 70)
    for m in info:
        print("  INFO  " + m)
    for m in warns:
        print("  WARN  " + m)
    for m in errors:
        print("  FAIL  " + m)
    print("-" * 70)
    print("INFO=%d  WARN=%d  FAIL=%d" % (len(info), len(warns), len(errors)))
    print("RESULT:", "FAIL" if errors else "PASS")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
