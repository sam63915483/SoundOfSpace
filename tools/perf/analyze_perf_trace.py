"""Analyse PerfTrace CSVs (written by Assets/3 - Scripts/Scripts/Game/Debug/PerfTrace.cs).

    py -3 tools/perf/analyze_perf_trace.py                # newest trace in the default perf folder
    py -3 tools/perf/analyze_perf_trace.py <file.csv|dir> # a specific file, or every csv in a dir

Prints, for the whole run:
  * frame-time distribution and the biggest time buckets
  * cost by SITUATION (looking at the village / the moon / the moon base; visible vs
    hidden behind a planet; on foot vs piloting; per nearest body)
  * the A/B toggle deltas (each bisect key vs the untouched baseline in the SAME
    situation), so "lights off saved 6 ms while looking at the village" is a
    number, not an impression
  * the 3 s before / after every MARK
  * which timing column tracks frame time best (correlation)
stdlib only.
"""
import csv, glob, math, os, statistics, sys
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass
from collections import defaultdict

DEFAULT_DIR = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\DefaultCompany\Solar System 2\perf")
TOGGLES = ["lights OFF", "moon base OFF", "village OFF", "shuttle OFF", "shadows OFF",
           "grass OFF", "dust OFF", "UI OFF", "pixel lights 4"]
TIME_COLS = ["main_ms", "render_ms", "gpu_ms", "update_ms", "lateupdate_ms", "fixed_ms", "canvas_ms",
             "camrender_ms", "culling_ms", "shadowmap_ms", "opaque_ms", "transparent_ms", "imagefx_ms",
             "finishrender_ms", "waitpresent_ms", "waitrender_ms", "grass_ms", "dust_ms", "endless_ms",
             "lensflare_ms", "fish_ms", "fireflies_ms", "cats_ms", "lod_ms", "nbody_ms"]
COUNT_COLS = ["draws", "setpass", "batches", "tris_k", "verts_k", "shadowcasters", "skinned", "instanced_draws", "dyn_batched", "gc_bytes"]


def fnum(s):
    try:
        return float(s)
    except (TypeError, ValueError):
        return float("nan")


def load(path):
    rows = []
    header = None
    with open(path, newline="", encoding="utf-8") as fh:
        for line in fh:
            if line.startswith("#"):
                header = line.strip()
                continue
            break
    with open(path, newline="", encoding="utf-8") as fh:
        lines = [l for l in fh if not l.startswith("#")]
    for r in csv.DictReader(lines):
        d = {}
        for k, v in r.items():
            if k in ("scene", "body"):
                d[k] = v
            else:
                d[k] = fnum(v)
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
        return "at the moon base"
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
    out = "%-44s n=%5d  fps %5.1f  ms %6.2f (p95 %6.2f)" % (label, len(rows), 1000.0 / mean([r["dt_ms"] for r in rows]) if rows else 0, mean([r["dt_ms"] for r in rows]), pct([r["dt_ms"] for r in rows], 0.95))
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
    base = [r for r in rows if r["mask"] == 0 and r["snap"] == 0]
    dts = [r["dt_ms"] for r in rows]
    print("frames %d  duration %.0f s  fps mean %.1f  median %.1f  1%%-low %.1f   frame ms p50 %.2f p95 %.2f p99 %.2f" % (
        len(rows), rows[-1]["t"] - rows[0]["t"], 1000 / mean(dts), 1000 / pct(dts, 0.5), 1000 / pct(dts, 0.99), pct(dts, 0.5), pct(dts, 0.95), pct(dts, 0.99)))
    scenes = defaultdict(int)
    for r in rows: scenes[r["scene"]] += 1
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
        print("   (no data for: %s — marker not present in this build)" % ", ".join(unavailable))

    # ---- by situation
    print("\n-- by situation (baseline frames) --")
    by = defaultdict(list)
    for r in base: by[situation(r)].append(r)
    for k, v in sorted(by.items(), key=lambda kv: -mean([r["dt_ms"] for r in kv[1]])):
        print("  " + fmt_row(k, v, ["main_ms", "render_ms", "gpu_ms", "shadowmap_ms", "opaque_ms", "waitpresent_ms"]))
        print("  %-44s draws %6.0f  setpass %6.0f  casters %5.0f  tris_k %7.0f  skinned %4.0f" % (
            "", mean([r["draws"] for r in v]), mean([r["setpass"] for r in v]), mean([r["shadowcasters"] for r in v]), mean([r["tris_k"] for r in v]), mean([r["skinned"] for r in v])))

    print("\n-- by nearest body (baseline, on foot) --")
    by = defaultdict(list)
    for r in base:
        if r["piloting"] == 0: by[r["body"]].append(r)
    for k, v in sorted(by.items(), key=lambda kv: -len(kv[1])):
        print("  " + fmt_row(k, v, ["main_ms", "gpu_ms", "grass_ms", "dust_ms"]) + "  draws %.0f" % mean([r["draws"] for r in v]))

    # ---- toggles: each single-toggle state vs baseline in the same situation, within ±20 s
    print("\n-- A/B toggles: single toggle vs baseline in the same situation (nearby in time) --")
    any_toggle = False
    for bit, name in enumerate(TOGGLES):
        m = 1 << bit
        on = [r for r in rows if r["mask"] == m and r["snap"] == 0]
        if not on:
            continue
        any_toggle = True
        by_sit = defaultdict(list)
        for r in on: by_sit[situation(r)].append(r)
        for sit, v in by_sit.items():
            t0, t1 = v[0]["t"] - 20, v[-1]["t"] + 20
            ref = [r for r in base if situation(r) == sit and t0 <= r["t"] <= t1]
            if len(ref) < 30 or len(v) < 30:
                continue
            d_ms = mean([r["dt_ms"] for r in ref]) - mean([r["dt_ms"] for r in v])
            d_draw = mean([r["draws"] for r in ref]) - mean([r["draws"] for r in v])
            d_gpu = mean([r["gpu_ms"] for r in ref]) - mean([r["gpu_ms"] for r in v])
            d_main = mean([r["main_ms"] for r in ref]) - mean([r["main_ms"] for r in v])
            print("  %-16s %-42s saves %6.2f ms/frame  (main %+.2f, gpu %+.2f, draws %+.0f)   fps %5.1f -> %5.1f   [n %d vs %d]" % (
                name, sit, d_ms, d_main, d_gpu, d_draw, 1000 / mean([r["dt_ms"] for r in ref]), 1000 / mean([r["dt_ms"] for r in v]), len(ref), len(v)))
    if not any_toggle:
        print("  (no toggle frames in this trace)")
    multi = [r for r in rows if int(r["mask"]) != 0 and (int(r["mask"]) & (int(r["mask"]) - 1)) != 0]
    if multi:
        print("  (%d frames had 2+ toggles on at once — skipped; press one at a time)" % len(multi))

    # ---- marks
    marks = [r for r in rows if r["mark"] > 0]
    if marks:
        print("\n-- marks (3 s before → 3 s after) --")
        for mrow in marks:
            t = mrow["t"]
            before = [r for r in rows if t - 3 <= r["t"] < t]
            after = [r for r in rows if t <= r["t"] < t + 3]
            print("  MARK %d  t=%.1fs  %s  body=%s alt=%.0fm  village ang %.0f° dist %.0f hidden=%d | moon ang %.0f° hidden=%d | base ang %.0f° dist %.0f" % (
                mrow["mark"], t, situation(mrow), mrow["body"], mrow["alt_m"], mrow["village_ang"], mrow["village_dist"], mrow["village_hidden"], mrow["moon_ang"], mrow["moon_hidden"], mrow["moonbase_ang"], mrow["moonbase_dist"]))
            if before: print("     before: " + fmt_row("", before, ["main_ms", "gpu_ms", "shadowmap_ms", "opaque_ms"]).strip() + "  draws %.0f" % mean([r["draws"] for r in before]))
            if after:  print("     after : " + fmt_row("", after, ["main_ms", "gpu_ms", "shadowmap_ms", "opaque_ms"]).strip() + "  draws %.0f" % mean([r["draws"] for r in after]))

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
    for r in worst: by[situation(r)] += 1
    for k, v in sorted(by.items(), key=lambda kv: -kv[1]):
        print("   %-44s %d" % (k, v))
    print("   mean of worst: " + fmt_row("", worst, ["main_ms", "gpu_ms", "fixed_ms", "endless_ms", "shadowmap_ms"]).strip() + "  gc_bytes %.0f" % mean([r["gc_bytes"] for r in worst]))


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
