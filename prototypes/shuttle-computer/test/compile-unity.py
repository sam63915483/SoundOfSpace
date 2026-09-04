# Compile-checks the whole Unity project without opening the Editor.
#
#   python prototypes/shuttle-computer/test/compile-unity.py
#
# Builds Assembly-CSharp and Assembly-CSharp-Editor with the Roslyn compiler
# that ships inside the Unity install, using references taken from Unity's own
# generated .csproj files (plus whatever is in Library/ScriptAssemblies, since
# the .csproj is only regenerated when the Editor opens).
#
# This is NOT a substitute for opening Unity — it says nothing about
# serialization, prefabs, or whether anything actually works. It only catches
# compile errors, which is exactly the class of mistake that is otherwise
# invisible until the Editor is launched.

import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))


def find_unity_data():
    base = os.path.join("C:" + os.sep, "Program Files", "Unity", "Hub", "Editor")
    if not os.path.isdir(base):
        return None
    for ver in sorted(os.listdir(base), reverse=True):
        data = os.path.join(base, ver, "Editor", "Data")
        if os.path.isfile(os.path.join(data, "DotNetSdkRoslyn", "csc.dll")):
            return data
    return None


def build(data, which, label):
    gen = subprocess.run([sys.executable, os.path.join(HERE, "make-rsp.py")] +
                         ([which] if which else []),
                         capture_output=True, text=True, cwd=ROOT)
    sys.stdout.write(gen.stdout or "")
    if gen.returncode != 0:
        sys.stderr.write(gen.stderr or "")
        return 1, 0

    rsp = os.path.join(ROOT, "build", ("asm-editor" if which else "asm") + ".rsp")
    dotnet = os.path.join(data, "NetCoreRuntime", "dotnet.exe")
    csc = os.path.join(data, "DotNetSdkRoslyn", "csc.dll")

    r = subprocess.run([dotnet, csc, "@" + rsp], capture_output=True, text=True, cwd=ROOT)
    out = (r.stdout or "") + (r.stderr or "")
    errors = [l for l in out.splitlines() if "error CS" in l]
    warns = [l for l in out.splitlines()
             if "warning CS" in l and "CS2023" not in l]

    print("")
    print(label + ": " + ("FAILED - " + str(len(errors)) + " error(s)" if errors else "OK") +
          "   (" + str(len(warns)) + " warnings)")
    for l in errors[:25]:
        print("  " + l)
    return (1 if errors else 0), len(warns)


def main():
    data = find_unity_data()
    if not data:
        print("Could not find a Unity install with DotNetSdkRoslyn.")
        return 3

    print("using " + data)
    bad = 0

    rc, _ = build(data, None, "Assembly-CSharp")
    bad += rc
    # The editor assembly references the runtime one, so only try it if that
    # built — otherwise every error is just a knock-on.
    if rc == 0:
        rc2, _ = build(data, "editor", "Assembly-CSharp-Editor")
        bad += rc2
        # Same sources, UNITY_EDITOR undefined — what a player BUILD compiles.
        # Catches warnings the editor compile structurally cannot see.
        rc3, _ = build(data, "player", "Assembly-CSharp (player defines)")
        bad += rc3
    else:
        print("Assembly-CSharp-Editor: SKIPPED (runtime assembly failed)")

    print("")
    print("compile check: " + ("PASS" if bad == 0 else "FAIL"))
    return 1 if bad else 0


sys.exit(main())
