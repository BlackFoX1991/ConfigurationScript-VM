# CFGS benchmarks

A small suite that measures how fast the CFGS VM executes representative
workloads: loops, recursion, collections, strings, closures, OOP dispatch,
higher-order functions, and floating-point math.

## Running

```powershell
# From the repository root. Builds Release, then runs every case.
.\benchmarks\run_benchmarks.ps1

# Faster iteration: reuse an existing build, fewer runs, one workload.
.\benchmarks\run_benchmarks.ps1 -SkipBuild -Runs 3 -Filter loop

# Machine-readable output.
.\benchmarks\run_benchmarks.ps1 -Json > results.json
```

Options: `-Configuration Release|Debug`, `-Runs <n>`, `-Warmup <n>`,
`-Filter <substring>`, `-SkipBuild`, `-Json`.

Always benchmark in **Release**. Debug is roughly four to five times slower and
is not representative.

## How it works

Each case under `cases/` is a self-contained CFGS program sized to run for
about one to three seconds. The runner:

1. Runs `_baseline.cfs`, an almost-empty script, and takes its fastest run as
   the fixed overhead (spawning the process, starting .NET, loading the VM,
   compiling a tiny script).
2. For every case, does warmup runs (discarded), then `-Runs` measured runs.
3. Reports the **min** and **median** wall time, plus **net ms** = min minus
   the baseline, an estimate of pure VM execution time with startup removed.
4. Derives a **throughput** figure from the work units declared in each case
   header, over the net time.

Wall time is measured per process, so each run pays the ~150 ms baseline once.
That is why the cases do millions of operations: it keeps startup below ~10% of
each measurement, and net-ms subtraction removes most of the rest.

## Reading the numbers

- **Min ms** is the headline: the fastest observed run, least polluted by GC
  pauses and OS scheduling.
- **Net ms** approximates time spent in the VM itself.
- **Throughput** is `units / net_seconds`, where each case declares its own
  unit (iterations, calls, elements, keys, objects, pixels, ...). Compare a
  case against itself across code changes; do not compare throughput *between*
  cases, because a "unit" means something different in each.

These numbers describe this machine and this build only. To compare against
another language, you must run an equivalent workload there the same way
(process per run, same iteration counts); the harness here does not do that.

## Adding a case

Drop a new `NN_name.cfs` into `cases/` with a header the runner reads:

```
# name: Human readable name
# units: 1000000
# unit-label: iterations
```

Size the workload so a Release run takes roughly one to three seconds, keep the
program self-contained (no imports, no file or network I/O), and `print` a
result so the run is verifiable and the work cannot be optimized away.
