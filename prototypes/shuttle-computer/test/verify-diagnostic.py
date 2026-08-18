# Compiles TraxLibrary.cs standalone (no Unity references) and RUNS its rules.
#
#   python prototypes/shuttle-computer/test/verify-library.py
#
# The shelf decides what a saved project is, what SAVE overwrites, and what a
# save file turns back into. All of that is quiet-failure territory — nothing
# crashes when a reload subtly changes a track, it just means a cassette printed
# yesterday no longer matches the project it came from. So it gets executed, not
# just compiled.
#
# Same trick as verify-port.py: Unity ships Roslyn and a .NET 6 runtime, so
# there is nothing extra to install. Keeping the ZERO-Unity-reference rule here
# too means TraxLibrary can never quietly grow a UnityEngine dependency and stop
# being testable this way.

import os
import subprocess
import sys
import glob
import io

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.join(ROOT, "build", "diag")

# The engine files the shelf leans on, plus the shelf itself.
SOURCES = [
    "TraxPrng.cs",
    "TraxScales.cs",
    "TraxParams.cs",
    "TraxPresets.cs",
    "TraxTrack.cs",
    "TraxPatterns.cs",
    "TraxClassifier.cs",
    "TraxSong.cs",
    "TraxKind.cs",
    "AlienTaste.cs",
    "SongEval.cs",
    "TapeValue.cs",
    "TapeMemory.cs",
    "TapeOffer.cs",
    "AlienFeedback.cs",
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

    src = [os.path.join(ROOT, "Assets", "3 - Scripts", "Music", f) for f in SOURCES]
    src.append(os.path.join(HERE, "TasteDiagnostic.cs"))
    src.append(os.path.join(HERE, "SaveStubs.cs"))
    for s in src:
        if not os.path.isfile(s):
            print("missing source: " + s)
            return 3

    exe = os.path.join(BUILD, "TasteDiagnostic.dll")
    rsp = os.path.join(BUILD, "library.rsp")
    with io.open(rsp, "w", encoding="utf-8") as f:
        f.write("-target:exe" + chr(10))
        f.write("-nologo" + chr(10))
        f.write("-nostdlib+" + chr(10))
        f.write("-langversion:9.0" + chr(10))
        f.write("-main:TasteDiagnostic" + chr(10))
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

    print("compiling the taste model standalone (no Unity references)...")
    r = subprocess.run([dotnet, csc, "@" + rsp], capture_output=True, text=True)
    out = (r.stdout or "") + (r.stderr or "")
    # Match both "file.cs(1,2): error CSxxxx" and bare "error CSxxxx".
    errors = [l for l in out.splitlines() if "error CS" in l]
    if errors:
        print("COMPILE FAILED:")
        for l in errors[:20]:
            print("  " + l)
        return 1
    print("  ok - AlienTaste + TapeValue compile with zero Unity dependencies")

    cfg = os.path.join(BUILD, "TasteDiagnostic.runtimeconfig.json")
    ver = os.path.basename(refdir)
    with io.open(cfg, "w", encoding="utf-8") as f:
        f.write('{"runtimeOptions":{"tfm":"net6.0","framework":' +
                '{"name":"Microsoft.NETCore.App","version":"' + ver + '"},' +
                '"rollForwardOnNoCandidateFx":2}}')

    print("running the taste model...")
    r = subprocess.run([dotnet, exe], capture_output=True, text=True)
    sys.stdout.write(r.stdout or "")
    sys.stderr.write(r.stderr or "")
    return r.returncode


sys.exit(main())
