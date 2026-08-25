# Compiles ShuttleLandingLogic.cs standalone (no Unity references) and RUNS
# the landing-validity cases from the handoff's test list.
#
#   python prototypes/shuttle-autopilot/test/verify-shuttle.py
#
# Same trick as the shuttle-computer suites: Unity ships Roslyn and a .NET 6
# runtime, so nothing extra to install. The ZERO-Unity-reference rule keeps
# the validity core testable this way forever.

import os
import subprocess
import sys
import glob
import io

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
HERE = os.path.dirname(os.path.abspath(__file__))
# build/taste rather than a fresh folder: Windows Smart App Control blocked
# freshly-compiled DLLs in a NEW build directory (0x800711C7) while the
# long-established taste folder runs fine — so the shuttle suite shares it.
BUILD = os.path.join(ROOT, "build", "taste")

SOURCES = [
    os.path.join(ROOT, "Assets", "3 - Scripts", "Shuttle", "ShuttleLandingLogic.cs"),
    os.path.join(HERE, "ShuttleTravelTests.cs"),
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

    exe = os.path.join(BUILD, "ShuttleTravelTests.dll")
    rsp = os.path.join(BUILD, "shuttle.rsp")
    with io.open(rsp, "w", encoding="utf-8") as f:
        f.write("-target:exe" + chr(10))
        f.write("-nologo" + chr(10))
        f.write("-nostdlib+" + chr(10))
        f.write("-langversion:9.0" + chr(10))
        f.write("-main:ShuttleTravelTests" + chr(10))
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
        for s in SOURCES:
            f.write('"' + s + '"' + chr(10))

    print("compiling the landing-validity core standalone (no Unity references)...")
    r = subprocess.run([dotnet, csc, "@" + rsp], capture_output=True, text=True)
    out = (r.stdout or "") + (r.stderr or "")
    errors = [l for l in out.splitlines() if "error CS" in l]
    if errors:
        print("COMPILE FAILED:")
        for l in errors[:20]:
            print("  " + l)
        return 1
    print("  ok - ShuttleLandingLogic compiles with zero Unity dependencies")

    cfg = os.path.join(BUILD, "ShuttleTravelTests.runtimeconfig.json")
    ver = os.path.basename(refdir)
    with io.open(cfg, "w", encoding="utf-8") as f:
        f.write('{"runtimeOptions":{"tfm":"net6.0","framework":' +
                '{"name":"Microsoft.NETCore.App","version":"' + ver + '"},' +
                '"rollForward":"LatestMinor"}}')

    r = subprocess.run([dotnet, exe], capture_output=True, text=True)
    sys.stdout.write(r.stdout or "")
    if r.returncode != 0:
        sys.stdout.write(r.stderr or "")
        print("TESTS FAILED")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
