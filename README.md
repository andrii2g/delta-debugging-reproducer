# delta-debugging-reproducer

A .NET 10 command-line tool that shrinks a failing sequence of operations while
preserving the original failure. Includes a manually implemented classic `ddmin`,
a single-element-removal reference algorithm, three deliberately faulty state
machines, and an SVG attempt tree.

## Quick start

Install the .NET 10 SDK, then run from the repository root:

```sh
dotnet build
dotnet test
dotnet run --project src/DeltaDebugging.Cli -- generate resource-lifecycle --count 1000 --seed 42 --output artifacts/input.json
dotnet run --project src/DeltaDebugging.Cli -- compare artifacts/input.json --output artifacts/comparison
```

Open `artifacts/comparison/ddmin/reduction.svg` in a browser. The JSON reports
contain exact measurements, and `minimized.json` can be replayed:

```sh
dotnet run --project src/DeltaDebugging.Cli -- replay artifacts/comparison/ddmin/minimized.json
```

A reproduced failure gives replay exit code **1**, as expected.

## Commands

```text
ddrepro generate <scenario> [--count 1000] [--seed 42] [--output input.json]
ddrepro replay <input.json>
ddrepro minimize <input.json> [--algorithm ddmin|naive] [--output artifacts/run] [--max-executions N]
ddrepro compare <input.json> [--output artifacts/run] [--max-executions N]
```

Use `dotnet run --project src/DeltaDebugging.Cli --` before each command, or publish:

```sh
dotnet publish src/DeltaDebugging.Cli -c Release -o artifacts/publish
dotnet artifacts/publish/ddrepro.dll --help
```

The output directory must be empty; generated input files must not already exist.
Comparison runs use separate caches and produce `ddmin/`, `naive/`, and
`comparison.json`. Exit codes: **0** successful command; **1** replay did not pass;
**2** invalid input or I/O error; **3** incomplete or unverified reduction;
**130** cancellation. Unexpected programming errors are not converted into oracle
failures and may terminate the process with a runtime-specific exit code.

`--max-executions` limits baseline plus search executions for each algorithm.
Independent verification is additional and reported separately. Ctrl+C cancels
between evaluations. If a baseline is available, the current reproducer and an
incomplete report are saved; cancellation before baseline produces no result.

## Scenarios and JSON

| Name | Deliberate defect | Essential sequence |
| --- | --- | --- |
| `resource-lifecycle` | Close leaves a writable handle behind | open → close → send |
| `conflicting-config` | Compression added after encryption creates an invalid pipeline | enableEncryption → enableCompression → send |
| `order-corruption` | Rollback restores the value but leaves its cached representation stale | begin → setValue → rollback → read |

```json
{
  "schemaVersion": 1,
  "name": "resource-lifecycle",
  "operations": [
    { "op": "open", "resource": "r1" },
    { "op": "close", "resource": "r1" },
    { "op": "send", "resource": "r1" }
  ]
}
```

All scenarios accept `noop`. `setValue` requires a string `value`; resource
lifecycle operations require `resource`. Unknown JSON properties, scenario names,
and operations are rejected. The generator spreads the essential operations among
deterministic no-ops using a seeded xorshift32 generator. No-ops are intentionally
simple: they demonstrate reduction without accidentally introducing another bug.
Examples in `examples/` contain 100 operations each.

## Algorithm and correctness

`ddmin` partitions the current ordered candidate into nearly equal contiguous
chunks. It tests chunks first, then their complements. A failing chunk resets
granularity to two; a failing complement decreases it by one (minimum two).
If no reduction succeeds, granularity doubles up to the candidate length.
This follows [Zeller and Hildebrandt's classic algorithm](https://www.st.cs.uni-saarland.de/papers/tse2002/).

The reference minimizer removes one element at a time and restarts after every
successful removal. Both algorithms test the empty input and preserve original
indices, ordering, and duplicate operations. They do not assume monotonicity.

A fresh executor is used for each evaluation. Outcomes are `TargetFailure`,
`Pass`, `OtherFailure`, and `Unresolved`. The baseline's stable signature identifies
the bug; a different signature cannot justify a reduction. Invalid lifecycle or
transaction prerequisites produce `Unresolved`, not a successful reproduction.
Unexpected exceptions propagate. The cache is scoped to a single run and keyed
by the exact ordered list of retained indices.

After search, independent verification bypasses the cache, replays the result,
and tests every single-element deletion. A verified **1-minimal** input reproduces
the original failure, but no single remaining operation can be removed while
preserving it. This does **not** guarantee a globally smallest input, and an
unresolved deletion does not establish that it would pass if made executable.
All guarantees require a deterministic oracle. Independent verification can detect
some instability but does not prove determinism.

`DeltaDebugging.Core` has no demo or CLI dependency. To minimize another domain,
provide an `Oracle` delegate mapping retained original indices to an `Observation`.
Return a stable signature for every failure. The minimizer itself ensures only the
baseline signature is accepted. Oracles must not mutate candidate inputs.

## Artifacts and metrics

- `minimized.json`: replayable scenario containing retained operations.
- `report.json`: failure signature, original indices, completion state, metrics,
  independent verification, and each deletion check.
- `trace.json`: every logical attempt, including cache hits, its parent, retained
  indices, outcome, and acceptance decision.
- `reduction.svg`: first page of the chronological attempt tree; subsequent pages
  are named `reduction-002.svg`, etc., with previous/next links.

SVG pages contain up to 60 attempts. Edges connect candidates to their source;
accepted paths have thicker red edges. Cross-page parents are identified by their
attempt numbers. The 100-bin position strip indicates which regions contain
retained operations; hover for exact indices. Text labels accompany outcome colors.

Search metrics include original/minimized count, reduction percentage (zero for
empty input), actual oracle executions, cache hits, logical candidate attempts
(including the empty candidate, excluding baseline), subsets, complements,
deletions, baseline time, and search time. Verification executions/time are
separate. Total executions/time include baseline, search, and verification but
exclude JSON and SVG rendering. Compare in Release mode; runtime is observational,
not a rigorous benchmark. `ddmin` is not guaranteed to outperform the reference
algorithm for every predicate; its worst case is quadratic in oracle tests.

Full traces store the indices of every attempted candidate, so very large inputs
can consume substantial memory and disk. This MVP uses sequential in-process
oracles, not arbitrary external processes or flaky/distributed test runners.

## Development

```text
src/DeltaDebugging.Core   Algorithms, caching, traces, verification
src/DeltaDebugging.Demo   Validated JSON models and faulty executors
src/DeltaDebugging.Cli    Commands, reports, SVG generation
tests/DeltaDebugging.Tests
examples/
```

Run `dotnet test -c Release`. Tests cover scenarios, non-monotonic predicates,
duplicate positions, ordering, wrong failures, invalid inputs, cache accounting,
budgets, cancellation, replayable artifacts, and SVG pagination/escaping.
