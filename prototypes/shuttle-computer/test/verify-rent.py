# Compiles the rent ledger + the cassette machine against headless stubs and
# RUNS them.
#
#   python prototypes/shuttle-computer/test/verify-rent.py
#
# MushroomQuest.cs owns what you owe and when the plugin embargo bites.
# CassetteDeck.cs owns what is in the slot and what is on the eject. Both are
# pure arithmetic and state transitions over things Unity happens to hold, and
# both fail QUIETLY: a lockout that fires a day early, a TAPE II that evaporates
# on a failed eject, a printed tape that doesn't survive a reload. None of that
# announces itself in play until it has already cost the player something.
#
# Unlike verify-port/verify-library, these files legitimately DO reference Unity
# (Mathf) and the game's singletons, so this build supplies stubs for them
# (RentDeckStubs.cs) instead of forbidding the imports. Keep the stubs as mean
# as the real classes — a Hotbar with infinite room would test nothing.

import os
import subprocess
import sys
import glob
import io

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.join(ROOT, "build", "rent")

SCRIPTS = os.path.join(ROOT, "Assets", "3 - Scripts")

# The two files under test, plus the engine the print table is built on.
SOURCES = [
    os.path.join(SCRIPTS, "Music", "TraxPrng.cs"),
    os.path.join(SCRIPTS, "Music", "TraxScales.cs"),
    os.path.join(SCRIPTS, "Music", "TraxParams.cs"),
    os.path.join(SCRIPTS, "Music", "TraxPresets.cs"),
    os.path.join(SCRIPTS, "Music", "TraxTrack.cs"),
    os.path.join(SCRIPTS, "Music", "TraxPatterns.cs"),
    os.path.join(SCRIPTS, "Music", "TraxClassifier.cs"),
    os.path.join(SCRIPTS, "Music", "TraxSong.cs"),
    os.path.join(SCRIPTS, "Music", "TraxKind.cs"),
    os.path.join(SCRIPTS, "Music", "TraxLibrary.cs"),
    os.path.join(SCRIPTS, "Music", "TraxPrints.cs"),
    os.path.join(SCRIPTS, "Music", "CassetteDeck.cs"),
    os.path.join(SCRIPTS, "Story", "MushroomQuest.cs"),
]


def find_unity_data():
    base = os.path.join("C:" + os.sep, "Program Files", "Unity", "Hub", "Editor")
    if not os.path.isdir(base):
        return None
    for ver in sorted(os.listdir(base), reverse=True):
        data = os.path.join(base, ver, "Editor", "Data")
        if os.path.isfile(os.path.join(data, "DotNetSdkRoslyn", "csc.dll")):
            return data
    return None


def main():
    data = find_unity_data()
    if not data:
        print("Could not find a Unity install with DotNetSdkRoslyn.")
        return 3

    dotnet = os.path.join(data, "NetCoreRuntime", "dotnet.exe")
    csc = os.path.join(data, "DotNetSdkRoslyn", "csc.dll")
    refdir = os.path.join(data, "NetCoreRuntime", "shared", "Microsoft.NETCore.App", "6.0.21")
    if not os.path.isdir(refdir):
        cands = glob.glob(os.path.join(data, "NetCoreRuntime", "shared",
                                       "Microsoft.NETCore.App", "*"))
        if not cands:
            print("No .NET runtime found under the Unity install.")
            return 3
        refdir = sorted(cands)[-1]

    if not os.path.isdir(BUILD):
        os.makedirs(BUILD)

    src = list(SOURCES)
    src.append(os.path.join(HERE, "RentDeckStubs.cs"))
    src.append(os.path.join(HERE, "RentDeckTests.cs"))
    for s in src:
        if not os.path.isfile(s):
            print("missing source: " + s)
            return 3

    exe = os.path.join(BUILD, "RentDeckTests.dll")
    rsp = os.path.join(BUILD, "rent.rsp")
    with io.open(rsp, "w", encoding="utf-8") as f:
        f.write("-target:exe" + chr(10))
        f.write("-nologo" + chr(10))
        f.write("-nostdlib+" + chr(10))
        f.write("-langversion:9.0" + chr(10))
        f.write("-main:RentDeckTests" + chr(10))
        f.write('-out:"' + exe + '"' + chr(10))
        # The runtime folder mixes managed assemblies with native ones;
        # referencing a native DLL is a hard error, so take only the managed set.
        for dll in glob.glob(os.path.join(refdir, "*.dll")):
            name = os.path.basename(dll)
            managed = (name.startswith("System.") or
                       name in ("netstandard.dll", "mscorlib.dll"))
            if name.endswith(".Native.dll"):
                managed = False
            if not managed:
                continue
            f.write('-r:"' + dll + '"' + chr(10))
        for s in src:
            f.write('"' + s + '"' + chr(10))

    print("compiling the rent ledger + cassette machine against stubs...")
    r = subprocess.run([dotnet, csc, "@" + rsp], capture_output=True, text=True)
    out = (r.stdout or "") + (r.stderr or "")
    errors = [l for l in out.splitlines() if "error CS" in l]
    if errors:
        print("COMPILE FAILED:")
        for l in errors[:20]:
            print("  " + l)
        return 1
    print("  ok")

    cfg = os.path.join(BUILD, "RentDeckTests.runtimeconfig.json")
    ver = os.path.basename(refdir)
    with io.open(cfg, "w", encoding="utf-8") as f:
        f.write('{"runtimeOptions":{"tfm":"net6.0","framework":' +
                '{"name":"Microsoft.NETCore.App","version":"' + ver + '"},' +
                '"rollForwardOnNoCandidateFx":2}}')

    r = subprocess.run([dotnet, exe], capture_output=True, text=True)
    sys.stdout.write(r.stdout or "")
    sys.stderr.write(r.stderr or "")
    return r.returncode


sys.exit(main())
