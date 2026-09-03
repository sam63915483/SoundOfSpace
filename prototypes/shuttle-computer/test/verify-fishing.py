# Compiles the fishing rulebook standalone (no Unity references) and RUNS it.
#
#   python prototypes/shuttle-computer/test/verify-fishing.py
#
# Fishing is a money loop, so its rules get executed, not just compiled — the
# same reasoning as verify-taste.py. Keeping the ZERO-Unity-reference rule means
# FishingRules/FishFightSim can never quietly grow a UnityEngine dependency and
# stop being testable this way; if someone adds `using UnityEngine;` to either,
# this script fails loudly instead of the tests silently disappearing.

import os
import subprocess
import sys
import glob
import io

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.join(ROOT, "build", "fishing")

SOURCES = [
    "FishingRules.cs",
    "FishFightSim.cs",
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

    src = [os.path.join(ROOT, "Assets", "3 - Scripts", "Fishing", f) for f in SOURCES]
    src.append(os.path.join(HERE, "FishingTests.cs"))
    for s in src:
        if not os.path.isfile(s):
            print("missing source: " + s)
            return 3

    exe = os.path.join(BUILD, "FishingTests.dll")
    rsp = os.path.join(BUILD, "fishing.rsp")
    with io.open(rsp, "w", encoding="utf-8") as f:
        f.write("-target:exe" + chr(10))
        f.write("-nologo" + chr(10))
        f.write("-nostdlib+" + chr(10))
        f.write("-langversion:9.0" + chr(10))
        f.write("-main:FishingTests" + chr(10))
        f.write('-out:"' + exe + '"' + chr(10))
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

    print("compiling the fishing rulebook standalone (no Unity references)...")
    r = subprocess.run([dotnet, csc, "@" + rsp], capture_output=True, text=True)
    out = (r.stdout or "") + (r.stderr or "")
    errors = [l for l in out.splitlines() if "error CS" in l]
    if errors:
        print("COMPILE FAILED:")
        for l in errors[:20]:
            print("  " + l)
        return 1
    print("  ok - FishingRules + FishFightSim compile with zero Unity dependencies")

    cfg = os.path.join(BUILD, "FishingTests.runtimeconfig.json")
    ver = os.path.basename(refdir)
    with io.open(cfg, "w", encoding="utf-8") as f:
        f.write('{"runtimeOptions":{"tfm":"net6.0","framework":' +
                '{"name":"Microsoft.NETCore.App","version":"' + ver + '"},' +
                '"rollForwardOnNoCandidateFx":2}}')

    # [TEST] 4 - bait accounting. The consumption itself lives in a Unity
    # coroutine and cannot be executed here, so it is guarded STRUCTURALLY:
    # exactly one consume call, sitting in the bite block, and nothing bait-
    # related anywhere near the cast path. That is the regression that actually
    # matters -- a future edit sliding the consume up into CastBobber would
    # charge the player for casts, which is precisely what [BUILD] 3 forbids.
    bobber = io.open(os.path.join(ROOT, "Assets", "3 - Scripts", "Fishing", "Bobber.cs"),
                     encoding="utf-8").read()
    consumes = bobber.count("FishingBait.Consume(")
    if consumes != 1:
        print("BAIT ACCOUNTING FAILED: expected exactly 1 FishingBait.Consume call "
              "in Bobber.cs, found " + str(consumes))
        return 1
    before_consume = bobber.split("FishingBait.Consume(")[0]
    if "isStriking = true" in before_consume:
        print("BAIT ACCOUNTING FAILED: bait is consumed AFTER the strike window "
              "opens - it must be spent on the bite itself.")
        return 1
    if "FishingBait" in bobber.split("IEnumerator FishingRoutine")[0]:
        print("BAIT ACCOUNTING FAILED: bait is referenced outside the bite "
              "routine (cast path?).")
        return 1
    print("  ok - bait is consumed exactly once, on the bite, not on the cast")

    print("running the fishing rules...")
    r = subprocess.run([dotnet, exe], capture_output=True, text=True)
    sys.stdout.write(r.stdout or "")
    sys.stderr.write(r.stderr or "")
    return r.returncode


sys.exit(main())
