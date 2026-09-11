"""Analyse PerfTrace CSVs (written by Assets/3 - Scripts/Scripts/Game/Debug/PerfTrace.cs).

    py -3 tools/perf/analyze_perf_trace.py                # newest trace in the default perf folder
    py -3 tools/perf/analyze_perf_trace.py <file.csv|dir> # a specific file, or every csv in a dir

Prints, for the whole run:
  * frame-time distribution and the biggest time buckets
  * cost by SITUATION (looking at the village / the moon / the moon base; visible vs
    hidden behind a planet; on foot vs piloting; per nearest body)
  * the A/B toggle deltas: each contiguous ON window against the 2 s just before and
    after it (same spot, same view), so "lights off saved 2 ms in the village" is a
    number, not an impression
  * the 3 s before / after every MARK
  * which timing column tracks frame time best (correlation)
stdlib only.
"""
import csv, glob, math, os, statistics, sys
from collections import defaultdict

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

DEFAULT_DIR = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\DefaultCompany\Solar System 2\perf")

# v2 toggle bits (PerfTrace.cs). Run 1 (2026-09-11 19:12) used the v1 set:
# lights OFF, moon base OFF, village OFF, shuttle OFF, shadows OFF, grass OFF, dust OFF, UI OFF, pixel lights 4
TOGGLES = ["lights OFF", "lights vertex", "village OFF", "MSAA OFF", "shadows OFF",
           "grass OFF", "cascades2+dist100", "lights skip planet", "pixel lights 8"]
TIME_COLS = ["main_ms", "gpu_ms", "waitgpu_ms", "update_ms", "lateupdate_ms", "fixed_ms", "canvas_ms",
             "camrender_ms", "culling_ms", "skinfinal_ms", "shadowmap_ms", "opaque_ms", "transparent_ms", "imagefx_ms",
             "finishrender_ms", "renderers_ms", "grass_ms", "dust_ms", "endless_ms",
             "lensflare_ms", "fish_ms", "fireflies_ms", "cats_ms", "uinav_ms"]
COUNT_COLS = ["draws", "setpass", "batches", "tris_k", "verts_k", "shadowcasters", "skinned", "instanced_draws", "gc_bytes"]


def fnum(s):
    try:
        return float(s)
    except (TypeError, ValueError):
        return float("nan")


def load(path):
    header = None
    with open(path, newline="", encoding="utf-8") as fh:
        lines = []
        for line in fh:
            if line.startswith("#"):
                header = line.strip()
            else:
                lines.append(line)
    rows = []
    for r in csv.DictReader(lines):
        d = {}
        for k, v in r.items():
            if k in ("scene", "body"):
                d[k] = v
            else:
                d[k] = fnum(v)
        for c in TIME_COLS + COUNT_COLS:
            d.setdefault(c, float("nan"))
        rows.append(d)
    return header, rows


def mean(xs):
    xs = [x for x in xs if not math.isnan(x)]
    return sum(xs) / len(xs) if xs else float("nan")


def pct(xs, p):
    xs = sorted(x for x in xs if not math.isnan(x))
    if not xs:
        return float("nan")
    return xs[min(len(xs) - 1, int(len(xs) * p))]


def situation(r):
    """Classify a frame by what the camera is doing. Order matters (first match wins)."""
    if r["piloting"] == 1:
        return "piloting"
    if r["moonbase_ang"] >= 0 and r["moonbase_ang"] < 20 and r["moonbase_dist"] < 400:
        return "looking at moon base (<400 m)"
    if r["village_ang"] >= 0 and r["village_ang"] < 25:
        if r["village_dist"] < 120 and r["village_hidden"] == 0:
            return "looking at village (near)"
        return "looking at village (hidden behind planet)" if r["village_hidden"] == 1 else "looking at village (far, visible)"
    if r["moon_ang"] >= 0 and r["moon_ang"] < 12:
        return "looking at moon (hidden)" if r["moon_hidden"] == 1 else "looking at moon"
    if r["shuttle_ang"] >= 0 and r["shuttle_ang"] < 20 and r["shuttle_dist"] < 60:
        return "looking at shuttle (near)"
    return "elsewhere"


def fmt_row(label, rows, cols):
    out = "%-44s n=%5d  fps %5.1f  ms %6.2f (p95 %6.2f)" % (
        label, len(rows), 1000.0 / mean([r["dt_ms"] for r in rows]) if rows else 0,
        mean([r["dt_ms"] for r in rows]), pct([r["dt_ms"] for r in rows], 0.95))
    for c in cols:
        out += "  %s %6.1f" % (c.replace("_ms", ""), mean([r[c] for r in rows]))
    return out


def report(path):
    header, rows = load(path)
    print("=" * 110)
    print(os.path.basename(path))
    print(header or "(no header)")
    if not rows:
        print("empty"); return
    rows = [r for r in rows if r["dt_ms"] < 100]          # drop scene-load hitches
    base = [r for r in rows if r["mask"] == 0 and r["snap"] == 0]
    dts = [r["dt_ms"] for r in rows]
    print("frames %d  duration %.0f s  fps mean %.1f  median %.1f  1%%-low %.1f   frame ms p50 %.2f p95 %.2f p99 %.2f" % (
        len(rows), rows[-1]["t"] - rows[0]["t"], 1000 / mean(dts), 1000 / pct(dts, 0.5), 1000 / pct(dts, 0.99),
        pct(dts, 0.5), pct(dts, 0.95), pct(dts, 0.99)))
    scenes = defaultdict(int)
    for r in rows:
        scenes[r["scene"]] += 1
    print("scenes:", dict(scenes))

    # ---- where does the frame go (baseline frames only)
    print("\n-- time buckets, baseline frames (no toggles), mean ms --")
    for c in TIME_COLS:
        v = mean([r[c] for r in base])
        if not math.isnan(v) and v > 0.02:
            print("   %-16s %7.2f" % (c, v))
    print("   counters: " + "  ".join("%s %.0f" % (c, mean([r[c] for r in base])) for c in COUNT_COLS if not math.isnan(mean([r[c] for r in base]))))
    unavailable = [c for c in TIME_COLS + COUNT_COLS if all(math.isnan(r[c]) for r in rows[:50])]
    if unavailable:
        print("   (no data for: %s)" % ", ".join(unavailable))

    # ---- by situation
    print("\n-- by situation (baseline frames) --")
    by = defaultdict(list)
    for r in base:
        by[situation(r)].append(r)
    for k, v in sorted(by.items(), key=lambda kv: -mean([r["dt_ms"] for r in kv[1]])):
        print("  " + fmt_row(k, v, ["main_ms", "gpu_ms", "camrender_ms", "lateupdate_ms"]))
        print("  %-44s draws %6.0f  setpass %6.0f  casters %5.0f  tris_k %7.0f  skinned %4.0f" % (
            "", mean([r["draws"] for r in v]), mean([r["setpass"] for r in v]), mean([r["shadowcasters"] for r in v]),
            mean([r["tris_k"] for r in v]), mean([r["skinned"] for r in v])))

    print("\n-- by nearest body (baseline, on foot) --")
    by = defaultdict(list)
    for r in base:
        if r["piloting"] == 0:
            by[r["body"]].append(r)
    for k, v in sorted(by.items(), key=lambda kv: -len(kv[1])):
        print("  " + fmt_row(k, v, ["main_ms", "gpu_ms", "grass_ms", "dust_ms"]) + "  draws %.0f" % mean([r["draws"] for r in v]))

    # ---- toggles: each contiguous ON run vs the 2 s immediately before and after it (same spot, same view)
    print("\n-- A/B toggles: each ON window vs the 2 s before + after it --")
    any_toggle = False
    i = 0
    while i < len(rows):
        if rows[i]["mask"] > 0:
            m = int(rows[i]["mask"]); j = i
            while j < len(rows) and rows[j]["mask"] == m:
                j += 1
            on = [r for r in rows[i:j] if r["snap"] == 0]
            t0, t1 = rows[i]["t"], rows[j - 1]["t"]
            ref = [r for r in rows if r["mask"] == 0 and r["snap"] == 0 and (t0 - 2 <= r["t"] < t0 or t1 < r["t"] <= t1 + 2)]
            if len(on) >= 30 and len(ref) >= 30:
                any_toggle = True
                names = "+".join(TOGGLES[b] for b in range(len(TOGGLES)) if m & (1 << b))
                f = lambda c: (mean([r[c] for r in ref]), mean([r[c] for r in on]))
                d = f("dt_ms"); g = f("gpu_ms"); mn = f("main_ms"); dr = f("draws"); tr = f("tris_k")
                print("  %-22s t=%5.0f %4.1fs  %-40s frame %6.2f -> %6.2f ms (%+5.2f)  gpu %5.2f -> %5.2f  main %5.2f -> %5.2f  draws %5.0f -> %5.0f  tris_k %6.0f -> %6.0f  fps %5.1f -> %5.1f" % (
                    names, t0, t1 - t0, situation(rows[i]), d[0], d[1], d[1] - d[0], g[0], g[1], mn[0], mn[1], dr[0], dr[1], tr[0], tr[1], 1000 / d[0], 1000 / d[1]))
            i = j
        else:
            i += 1
    if not any_toggle:
        print("  (no usable toggle windows - hold each toggle >= 1 s)")

    # ---- marks
    marks = [r for r in rows if r["mark"] > 0]
    if marks:
        print("\n-- marks (3 s before -> 3 s after) --")
        for mrow in marks:
            t = mrow["t"]
            before = [r for r in rows if t - 3 <= r["t"] < t]
            after = [r for r in rows if t <= r["t"] < t + 3]
            print("  MARK %d  t=%.1fs  %s  body=%s alt=%.0fm  village ang %.0f dist %.0f hidden=%d | moon ang %.0f hidden=%d | base ang %.0f dist %.0f" % (
                mrow["mark"], t, situation(mrow), mrow["body"], mrow["alt_m"], mrow["village_ang"], mrow["village_dist"],
                mrow["village_hidden"], mrow["moon_ang"], mrow["moon_hidden"], mrow["moonbase_ang"], mrow["moonbase_dist"]))
            if before:
                print("     before: " + fmt_row("", before, ["main_ms", "gpu_ms", "camrender_ms"]).strip() + "  draws %.0f tris_k %.0f" % (mean([r["draws"] for r in before]), mean([r["tris_k"] for r in before])))
            if after:
                print("     after : " + fmt_row("", after, ["main_ms", "gpu_ms", "camrender_ms"]).strip() + "  draws %.0f tris_k %.0f" % (mean([r["draws"] for r in after]), mean([r["tris_k"] for r in after])))

    # ---- what tracks frame time
    print("\n-- correlation with frame time (baseline frames; 1.0 = moves exactly with it) --")
    dt = [r["dt_ms"] for r in base]
    cors = []
    for c in TIME_COLS + COUNT_COLS:
        xs = [r[c] for r in base]
        if any(math.isnan(x) for x in xs) or len(set(xs)) < 3:
            continue
        try:
            cors.append((statistics.correlation(xs, dt), c))
        except Exception:
            pass
    for cval, c in sorted(cors, reverse=True)[:12]:
        print("   %-16s %5.2f" % (c, cval))

    # ---- worst 1% frames: what was going on
    worst = sorted(base, key=lambda r: -r["dt_ms"])[: max(10, len(base) // 100)]
    print("\n-- worst 1%% frames (n=%d): situations --" % len(worst))
    by = defaultdict(int)
    for r in worst:
        by[situation(r)] += 1
    for k, v in sorted(by.items(), key=lambda kv: -kv[1]):
        print("   %-44s %d" % (k, v))
    print("   mean of worst: " + fmt_row("", worst, ["main_ms", "gpu_ms", "fixed_ms", "endless_ms"]).strip() + "  gc_bytes %.0f" % mean([r["gc_bytes"] for r in worst]))


def main():
    arg = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_DIR
    if os.path.isdir(arg):
        files = sorted(glob.glob(os.path.join(arg, "trace_*.csv")), key=os.path.getmtime)
        if not files:
            print("no trace_*.csv in", arg); return
        if len(sys.argv) <= 1:
            files = files[-1:]
    else:
        files = [arg]
    for f in files:
        report(f)


if __name__ == "__main__":
    main()
