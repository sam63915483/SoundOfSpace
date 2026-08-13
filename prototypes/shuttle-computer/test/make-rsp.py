# Builds a Roslyn response file that compiles Assembly-CSharp the way Unity
# would, so C# changes can be compile-checked without opening the Editor.
#
#   python prototypes/shuttle-computer/test/make-rsp.py
#   "<unity>/Editor/Data/NetCoreRuntime/dotnet.exe" "<unity>/Editor/Data/DotNetSdkRoslyn/csc.dll" @build/rsp.txt
#
# References come from Unity's own generated Assembly-CSharp.csproj, so they
# stay correct as packages change. Sources are globbed rather than taken from
# the csproj because the csproj is only regenerated when the Editor opens — new
# files would otherwise be invisible.

import re
import glob
import os
import io
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
OUT_DIR = os.path.join(ROOT, "build")

EDITOR = len(sys.argv) > 1 and sys.argv[1] == "editor"
PROJ = "Assembly-CSharp-Editor.csproj" if EDITOR else "Assembly-CSharp.csproj"
NAME = "asm-editor" if EDITOR else "asm"


_cache = {}


def resolve_by_name(basename):
    """Find a DLL by filename under Library/, for stale csproj references."""
    if not _cache:
        for base in ("Library/ScriptAssemblies", "Library/PackageCache"):
            for p in glob.glob(os.path.join(ROOT, base, "**", "*.dll"), recursive=True):
                _cache.setdefault(os.path.basename(p), p)
    return _cache.get(basename)


def main():
    proj_path = os.path.join(ROOT, PROJ)
    proj = io.open(proj_path, encoding="utf-8-sig").read()

    refs = re.findall(r"<HintPath>([^<]*)</HintPath>", proj)
    m = re.search(r"<DefineConstants>([^<]*)</DefineConstants>", proj)
    defines = m.group(1) if m else ""

    sep = chr(92)  # backslash, kept out of literals so this file stays paste-safe
    srcs = []
    for p in glob.glob(os.path.join(ROOT, "Assets", "**", "*.cs"), recursive=True):
        parts = p.replace(sep, "/").split("/")
        in_editor = "Editor" in parts
        # Editor-folder scripts compile into Assembly-CSharp-Editor, everything
        # else into Assembly-CSharp. No asmdefs in this project, so that single
        # rule is the whole story.
        if in_editor != EDITOR:
            continue
        srcs.append(os.path.abspath(p))

    if not os.path.isdir(OUT_DIR):
        os.makedirs(OUT_DIR)

    rsp = os.path.join(OUT_DIR, NAME + ".rsp")
    with io.open(rsp, "w", encoding="utf-8") as f:
        f.write("-target:library" + chr(10))
        f.write("-nologo" + chr(10))
        f.write("-nostdlib+" + chr(10))
        f.write("-noconfig" + chr(10))
        f.write("-langversion:9.0" + chr(10))
        f.write("-unsafe" + chr(10))
        f.write('-out:"' + os.path.join(OUT_DIR, NAME + ".dll") + '"' + chr(10))
        if defines:
            f.write("-define:" + defines + chr(10))
        # Everything Unity has already compiled for this project — Netcode,
        # Services, TMP and friends. The csproj can be missing these entirely
        # if it was generated before a package was added.
        seen = set()
        for p in glob.glob(os.path.join(ROOT, "Library", "ScriptAssemblies", "*.dll")):
            name = os.path.basename(p)
            # Skip the project's own assemblies — their types are in `srcs`,
            # and referencing both makes every type ambiguous.
            if name.startswith("Assembly-CSharp"):
                continue
            seen.add(name)
            f.write('-r:"' + p + '"' + chr(10))

        missing = []
        for r in refs:
            if os.path.basename(r) in seen:
                continue
            # Package references are project-relative in the csproj; Unity DLLs
            # are absolute. Resolve the relative ones or csc can't find them.
            if not os.path.isabs(r):
                r = os.path.join(ROOT, r)
            r = os.path.normpath(r)

            # The csproj is only regenerated when the Editor opens, so it can
            # name a package version that has since been upgraded. Fall back to
            # finding the same DLL wherever it actually lives now.
            if not os.path.isfile(r):
                found = resolve_by_name(os.path.basename(r))
                if found:
                    r = found
                else:
                    missing.append(os.path.basename(r))
                    continue

            f.write('-r:"' + r + '"' + chr(10))

        # The editor assembly also references the runtime one.
        if EDITOR:
            f.write('-r:"' + os.path.join(OUT_DIR, "asm.dll") + '"' + chr(10))

        for s in srcs:
            f.write('"' + s + '"' + chr(10))

    if missing:
        print("WARNING: dropped " + str(len(missing)) + " unresolved reference(s): " +
              ", ".join(sorted(set(missing))))

    print("wrote " + rsp)
    print("sources: " + str(len(srcs)) + "   refs: " + str(len(refs)))


main()
