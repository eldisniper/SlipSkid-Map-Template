"""Zips a built map for Thunderstore: maps/<Map>/thunderstore/ (manifest.json, README.md, icon.png, CHANGELOG.md)
plus the map build (unity/MapKit/Builds/<Map>/: bundle, mapkit.json, images) into dist/<Name>-<version>.zip.

    python tools/pack_map.py <Map>          e.g. MyFirstMap

Build the map first (python tools/build_map.py maps/<Map>/map.json --no-install) and test it in game. The map's
mapkit.json gets the package's version. Upload the zip on thunderstore.io (community Slip & Skid).
"""
import json
import os
import re
import struct
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NAME_RE = re.compile(r"^[A-Za-z0-9_]+$")
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")
DEP_RE = re.compile(r"^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$")
REQUIRED = ["manifest.json", "README.md", "icon.png"]
LOADER = "LesAmisDeIronWings-MapKitLoader-"


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    return struct.unpack(">II", head[16:24]) if head[:8] == b"\x89PNG\r\n\x1a\n" else None


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    name = sys.argv[1]
    ts = os.path.join(ROOT, "maps", name, "thunderstore")
    build = os.path.join(ROOT, "unity", "MapKit", "Builds", name)
    problems = [f"missing maps/{name}/thunderstore/{f}" for f in REQUIRED if not os.path.exists(os.path.join(ts, f))]
    if problems:
        sys.exit("\n".join(problems))
    manifest = json.load(open(os.path.join(ts, "manifest.json"), encoding="utf-8"))
    if not NAME_RE.match(manifest.get("name", "")):
        problems.append("manifest name: letters, digits or _ only")
    if not VERSION_RE.match(manifest.get("version_number", "")):
        problems.append("manifest version_number must be X.Y.Z (bump it for every upload)")
    if len(manifest.get("description", "")) > 250:
        problems.append("manifest description is longer than 250 characters")
    deps = manifest.get("dependencies", [])
    problems += [f"bad dependency '{d}' (Team-Package-X.Y.Z)" for d in deps if not DEP_RE.match(d)]
    if not any(d.startswith(LOADER) for d in deps):
        problems.append(f"dependencies must include {LOADER}<version> (the loader that plays the map)")
    size = png_size(os.path.join(ts, "icon.png"))
    if size != (256, 256):
        problems.append(f"icon.png must be a 256x256 PNG (is {size})")
    if not os.path.exists(os.path.join(build, "mapkit.json")):
        problems.append(f"no build in unity/MapKit/Builds/{name}: run tools/build_map.py maps/{name}/map.json first")
    if problems:
        sys.exit(f"{name}: " + "\n  ".join([""] + problems))

    version = manifest["version_number"]
    out = os.path.join(ROOT, "dist", f"{manifest['name']}-{version}.zip")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    files = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(os.listdir(ts)):
            if os.path.isfile(os.path.join(ts, f)):
                z.write(os.path.join(ts, f), f)
                files += 1
        for f in sorted(os.listdir(build)):
            src = os.path.join(build, f)
            if not os.path.isfile(src) or f in ("track.png",):
                continue
            if f == "mapkit.json":
                data = json.load(open(src, encoding="utf-8"))
                data["version"] = version   # the map's own version follows the package's
                z.writestr(f, json.dumps(data, indent=2) + "\n")
            else:
                z.write(src, f)
            files += 1
    print(f"{manifest['name']} {version} -> dist/{os.path.basename(out)} ({os.path.getsize(out) / 1e6:.2f} MB, {files} files)")
    if os.path.getsize(out) > 10_000_000:
        print("  over 10 MB: thunderstore.io's browser upload refuses it; lighten the decor.")


if __name__ == "__main__":
    main()
