# Compiles the pool-table ball sim standalone (no Unity references) and RUNS it.
#
#   py -3 prototypes/pool/test/verify-pool.py
#
# Same recipe as prototypes/shuttle-computer/test/verify-fishing.py. The ZERO-
# Unity-reference rule is the point: PoolPhysics2D must never grow a
# `using UnityEngine;` or this stops being testable off the Editor.

import os
import subprocess
import sys
import glob
import io

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
BUILD = os.path.join(ROOT, "build", "pool")

SOURCES = [
    os.path.join(ROOT, "Assets", "3 - Scripts", "Pool", "PoolPhysics2D.cs"),
    os.path.join(HERE, "PoolSimTests.cs"),
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
    cands = glob.glob(os.path.join(data, "NetCoreRuntime", "shared", "Microsoft.NETCore.App", "*"))
    if not cands:
        print("No .NET runtime found under the Unity install.")
        return 3
    refdir = sorted(cands)[-1]
    if not os.path.isdir(BUILD):
        os.makedirs(BUILD)
    for s in SOURCES:
        if not os.path.isfile(s):
            print("missing source: " + s)
            return 3

    exe = os.path.join(BUILD, "PoolSimTests.dll")
    rsp = os.path.join(BUILD, "pool.rsp")
    with io.open(rsp, "w", encoding="utf-8") as f:
        f.write("-target:exe\n-nologo\n-nostdlib+\n-langversion:9.0\n-main:PoolSimTests\n")
        f.write('-out:"' + exe + '"\n')
        for dll in glob.glob(os.path.join(refdir, "*.dll")):
            name = os.path.basename(dll)
            managed = name.startswith("System.") or name in ("netstandard.dll", "mscorlib.dll")
            if name.endswith(".Native.dll"):
                managed = False
            if managed:
                f.write('-r:"' + dll + '"\n')
        for s in SOURCES:
            f.write('"' + s + '"\n')

    print("compiling PoolPhysics2D standalone (no Unity references)...")
    r = subprocess.run([dotnet, csc, "@" + rsp], capture_output=True, text=True)
    out = (r.stdout or "") + (r.stderr or "")
    errors = [l for l in out.splitlines() if "error CS" in l]
    if errors:
        print("COMPILE FAILED:")
        for l in errors[:20]:
            print("  " + l)
        return 1
    print("  ok - PoolPhysics2D compiles with zero Unity dependencies")

    cfg = os.path.join(BUILD, "PoolSimTests.runtimeconfig.json")
    ver = os.path.basename(refdir)
    with io.open(cfg, "w", encoding="utf-8") as f:
        f.write('{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"'
                + ver + '"},"rollForwardOnNoCandidateFx":2}}')

    print("running the pool sim checks...")
    r = subprocess.run([dotnet, exe], capture_output=True, text=True)
    sys.stdout.write(r.stdout or "")
    sys.stderr.write(r.stderr or "")
    return r.returncode


sys.exit(main())
