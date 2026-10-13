using System.Diagnostics;

namespace DeltaDebugging.Core;

public enum Algorithm { Ddmin, Naive }
public enum AttemptKind { Baseline, Empty, Subset, Complement, Deletion }
public sealed record Attempt(int Id, int? ParentId, AttemptKind Kind, int[] Indices,
    Observation Observation, bool CacheHit, bool Accepted);
public sealed record SearchMetrics(int OriginalCount, int MinimizedCount, double ReductionPercentage,
    int OracleExecutions, int CacheHits, int TestedCandidates, int TestedSubsets,
    int TestedComplements, int TestedDeletions, double BaselineMilliseconds, double SearchMilliseconds);
public sealed record SearchResult(int[] Indices, string Signature, bool Complete, string? StopReason,
    SearchMetrics Metrics, IReadOnlyList<Attempt> Trace);
public sealed record SearchOptions(Algorithm Algorithm = Algorithm.Ddmin, int MaxExecutions = int.MaxValue);

public static class Minimizer
{
    public static SearchResult Run(int count, Oracle oracle, SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(oracle);
        options ??= new();
        if (options.MaxExecutions < 1 || !Enum.IsDefined(options.Algorithm))
            throw new ArgumentException("Invalid search options.");
        var session = new Session(oracle, options.MaxExecutions, cancellationToken);
        int[] current = Enumerable.Range(0, count).ToArray();
        var baselineClock = Stopwatch.StartNew();
        var baseline = session.Test(current, null, AttemptKind.Baseline);
        baselineClock.Stop();
        if (baseline.Observation.Outcome != Outcome.TargetFailure || baseline.Observation.Signature is null)
            throw new ArgumentException("The original input must produce a signed target failure.");
        session.Signature = baseline.Observation.Signature;
        int parent = baseline.Id;
        bool complete = true;
        string? stopReason = null;
        var searchClock = Stopwatch.StartNew();
        try
        {
            if (current.Length > 0 && Accept([], AttemptKind.Empty)) { }
            else if (options.Algorithm == Algorithm.Naive)
            {
                int index = 0;
                while (index < current.Length)
                {
                    var candidate = current.Where((_, i) => i != index).ToArray();
                    if (Accept(candidate, AttemptKind.Deletion)) index = 0;
                    else index++;
                }
            }
            else
            {
                int granularity = 2;
                while (current.Length >= 2)
                {
                    int n = Math.Min(granularity, current.Length);
                    bool reduced = false;
                    for (int part = 0; part < n; part++)
                    {
                        int start = (int)((long)part * current.Length / n);
                        int end = (int)((long)(part + 1) * current.Length / n);
                        if (!Accept(current[start..end], AttemptKind.Subset)) continue;
                        granularity = 2;
                        reduced = true;
                        break;
                    }
                    if (reduced) continue;
                    for (int part = 0; part < n; part++)
                    {
                        int start = (int)((long)part * current.Length / n);
                        int end = (int)((long)(part + 1) * current.Length / n);
                        var complement = current[..start].Concat(current[end..]).ToArray();
                        if (!Accept(complement, AttemptKind.Complement)) continue;
                        granularity = Math.Max(n - 1, 2);
                        reduced = true;
                        break;
                    }
                    if (reduced) continue;
                    if (n == current.Length) break;
                    granularity = (int)Math.Min((long)n * 2, current.Length);
                }
            }
        }
        catch (SearchStoppedException exception)
        {
            complete = false;
            stopReason = exception.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            complete = false;
            stopReason = "Cancelled";
        }
        searchClock.Stop();
        return new(current, session.Signature, complete, stopReason,
            new(count, current.Length, count == 0 ? 0 : 100.0 * (count - current.Length) / count,
                session.Executions, session.Hits, session.Trace.Count - 1,
                session.Trace.Count(x => x.Kind == AttemptKind.Subset),
                session.Trace.Count(x => x.Kind == AttemptKind.Complement),
                session.Trace.Count(x => x.Kind == AttemptKind.Deletion),
                baselineClock.Elapsed.TotalMilliseconds, searchClock.Elapsed.TotalMilliseconds),
            session.Trace.AsReadOnly());

        bool Accept(int[] candidate, AttemptKind kind)
        {
            var attempt = session.Test(candidate, parent, kind);
            if (!attempt.Accepted) return false;
            current = candidate;
            parent = attempt.Id;
            return true;
        }
    }

    private sealed class SearchStoppedException(string message) : Exception(message);

    private sealed class Session(Oracle oracle, int limit, CancellationToken token)
    {
        private readonly Dictionary<string, Observation> cache = new(StringComparer.Ordinal);
        public List<Attempt> Trace { get; } = [];
        public string Signature { get; set; } = "";
        public int Executions { get; private set; }
        public int Hits { get; private set; }

        public Attempt Test(int[] indices, int? parent, AttemptKind kind)
        {
            token.ThrowIfCancellationRequested();
            string key = string.Join(',', indices);
            bool hit = cache.TryGetValue(key, out var observation);
            if (hit) Hits++;
            else
            {
                if (Executions >= limit) throw new SearchStoppedException("Execution budget exhausted");
                Executions++;
                observation = oracle(Array.AsReadOnly(indices));
                if (kind != AttemptKind.Baseline && observation.Outcome == Outcome.TargetFailure
                    && observation.Signature != Signature)
                    observation = observation with { Outcome = Outcome.OtherFailure };
                cache.Add(key, observation);
            }
            var attempt = new Attempt(Trace.Count, parent, kind, (int[])indices.Clone(), observation!, hit,
                observation!.Outcome == Outcome.TargetFailure);
            Trace.Add(attempt);
            return attempt;
        }
    }
}
