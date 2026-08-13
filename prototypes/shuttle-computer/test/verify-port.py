# Compiles the five C# engine files standalone (no Unity references at all) and
# runs them against the golden vectors dumped from the JS engine.
#
#   python prototypes/shuttle-computer/test/verify-port.py
#
# Two things are being proven here:
#   1. The C# engine produces byte-identical patterns to the browser prototype.
#   2. The engine layer genuinely has no Unity dependency — if someone adds a
#      `using UnityEngine;` to one of these files, this build breaks, which is
#      the early warning that the port boundary is eroding.
#
# Uses the Roslyn compiler and .NET runtime that ship inside the Unity install,
# so there is nothing extra to install.

import os
import subprocess
import sys
import glob
import io

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.join(ROOT, "build", "port")

ENGINE_FILES = [
    "TraxPrng.cs",
    "TraxScales.cs",
    "TraxParams.cs",
    "TraxPresets.cs",
    "TraxTrack.cs",
    "TraxPatterns.cs",
    "TraxClassifier.cs",
]

GOLDEN = os.path.join(ROOT, "Assets", "StreamingAssets", "Trax", "trax-golden.txt")


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

    src = [os.path.join(ROOT, "Assets", "3 - Scripts", "Music", f) for f in ENGINE_FILES]
    src.append(os.path.join(HERE, "TraxGoldenRunner.cs"))
    for s in src:
        if not os.path.isfile(s):
            print("missing source: " + s)
            return 3

    exe = os.path.join(BUILD, "TraxGoldenRunner.dll")
    rsp = os.path.join(BUILD, "port.rsp")
    with io.open(rsp, "w", encoding="utf-8") as f:
        f.write("-target:exe" + chr(10))
        f.write("-nologo" + chr(10))
        f.write("-nostdlib+" + chr(10))
        f.write("-langversion:9.0" + chr(10))
        f.write("-main:TraxGoldenRunner" + chr(10))
        f.write('-out:"' + exe + '"' + chr(10))
        # Deliberately NO Unity references. If the engine ever needs one, this
        # fails loudly instead of the port boundary rotting quietly.
        #
        # The runtime folder mixes managed assemblies with native ones
        # (coreclr.dll, ucrtbase.dll, api-ms-win-*). Referencing a native DLL is
        # a hard error, so take only the managed set.
        for dll in glob.glob(os.path.join(refdir, "*.dll")):
            name = os.path.basename(dll)
            managed = (name.startswith("System.") or
                       name in ("netstandard.dll", "mscorlib.dll"))
            # ...except the ones that are native despite the System. prefix.
            if name.endswith(".Native.dll"):
                managed = False
            if not managed:
                continue
            f.write('-r:"' + dll + '"' + chr(10))
        for s in src:
            f.write('"' + s + '"' + chr(10))

    print("compiling engine standalone (no Unity references)...")
    r = subprocess.run([dotnet, csc, "@" + rsp], capture_output=True, text=True)
    out = (r.stdout or "") + (r.stderr or "")
    # Match both "file.cs(1,2): error CSxxxx" and bare "error CSxxxx" — the
    # second form has no file prefix and a ": error " filter misses it entirely.
    errors = [l for l in out.splitlines() if "error CS" in l]
    if errors:
        print("COMPILE FAILED:")
        for l in errors[:20]:
            print("  " + l)
        return 1
    print("  ok - engine compiles with zero Unity dependencies")

    cfg = os.path.join(BUILD, "TraxGoldenRunner.runtimeconfig.json")
    ver = os.path.basename(refdir)
    with io.open(cfg, "w", encoding="utf-8") as f:
        f.write('{"runtimeOptions":{"tfm":"net6.0","framework":' +
                '{"name":"Microsoft.NETCore.App","version":"' + ver + '"},' +
                '"rollForwardOnNoCandidateFx":2}}')

    if not os.path.isfile(GOLDEN):
        print("golden file missing: " + GOLDEN + "  (run: node test/make-golden.js)")
        return 3

    print("running golden vectors...")
    r = subprocess.run([dotnet, exe, GOLDEN], capture_output=True, text=True)
    sys.stdout.write(r.stdout or "")
    sys.stderr.write(r.stderr or "")
    return r.returncode


sys.exit(main())
