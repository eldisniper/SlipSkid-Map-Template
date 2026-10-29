"""Builds a map with Unity in batch mode (no editor window), then installs it into every Thunderstore profile that
has MapKitLoader.

    python tools/build_map.py maps/<Map>/map.json       a map described in JSON (docs/JSON_MAPS.md)
    python tools/build_map.py <GeneratorClass>          an editor class with a static BatchCreateAndBuild()

Unity cannot open a project twice: close the editor first, or pass --copy to build from a temporary copy
of the project (one per generator; the generated scene then stays in the copy; the map it built is copied back
to Builds/).

    --no-install              build only, install into no profile
    --install-to=<BepInEx>    install only into these BepInEx folders (separate several with ';')

Without either flag the map goes into every profile that has the loader. Careful: a profile that already has the
map from Thunderstore then loads it twice.
"""
import os
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(ROOT, "unity", "MapKit")
UNITY_CANDIDATES = [
    r"C:\Program Files\Unity 2022.3.62f3\Editor\Unity.exe",
    r"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe",
]


def find_unity():
    if os.environ.get("UNITY_EXE"):
        return os.environ["UNITY_EXE"]
    for path in UNITY_CANDIDATES:
        if os.path.exists(path):
            return path
    sys.exit("Unity 2022.3.62f3 not found; set UNITY_EXE to its Editor\\Unity.exe.")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        sys.exit(__doc__)
    generator = args[0]
    env = dict(os.environ)
    if generator.lower().endswith(".json"):
        if not os.path.isfile(generator):
            sys.exit(f"{generator}: file not found")
        env["SLIPSKID_MAP_JSON"] = os.path.abspath(generator)
        copy_name = "json-" + os.path.basename(os.path.dirname(os.path.abspath(generator)))
        generator = "JsonMapGenerator"
    else:
        copy_name = generator
    if "--no-install" in sys.argv:
        env["SLIPSKID_INSTALL_TARGETS"] = "none"
    for a in sys.argv[1:]:
        if a.startswith("--install-to="):
            env["SLIPSKID_INSTALL_TARGETS"] = a.split("=", 1)[1]
    project = PROJECT
    started = time.time()
    if "--copy" in sys.argv:
        # One copy per generator: builds of different maps can run side by side without wiping each other's copy.
        project = os.path.join(tempfile.gettempdir(), "SlipSkidMapKitCopy-" + copy_name)
        for part in ("Assets", "Packages", "ProjectSettings"):
            shutil.rmtree(os.path.join(project, part), ignore_errors=True)
            shutil.copytree(os.path.join(PROJECT, part), os.path.join(project, part))
        if not os.path.exists(os.path.join(project, "Library")) and os.path.exists(os.path.join(PROJECT, "Library")):
            shutil.copytree(os.path.join(PROJECT, "Library"), os.path.join(project, "Library"))
    log = os.path.join(project, "Logs", "batch.log")
    os.makedirs(os.path.dirname(log), exist_ok=True)
    cmd = [find_unity(), "-batchmode", "-nographics", "-projectPath", project,
           "-executeMethod", f"SlipSkid.MapKit.Editor.{generator}.BatchCreateAndBuild", "-quit", "-logFile", log]
    print("running Unity (first run imports packages and can take minutes)...")
    code = subprocess.run(cmd, env=env).returncode
    lines = open(log, encoding="utf-8", errors="replace").read().splitlines()
    shown = sorted({l.strip() for l in lines if "error CS" in l or "[Slip & Skid]" in l or "Exception:" in l})
    print("\n".join(shown) or "(no Slip & Skid messages; see " + log + ")")
    if project != PROJECT:
        # Only what this run built: the copy keeps older builds of other maps that must not overwrite newer ones.
        src, dst = os.path.join(project, "Builds"), os.path.join(PROJECT, "Builds")
        if os.path.exists(src):
            for name in os.listdir(src):
                built = os.path.join(src, name)
                newest = max((os.path.getmtime(os.path.join(d, f)) for d, _, fs in os.walk(built) for f in fs), default=0)
                if os.path.isdir(built) and newest >= started - 1:
                    shutil.copytree(built, os.path.join(dst, name), dirs_exist_ok=True)
    if code == 0 and not any("[Slip & Skid] Built to" in l for l in lines):
        print("FAILED: Unity exited without building the map (see " + log + ")")
        code = 1
    sys.exit(code)


if __name__ == "__main__":
    main()
