using System.Diagnostics;

namespace DeltaDebugging.Core;

public sealed record DeletionCheck(int RemovedIndex, Observation Observation);
public sealed record VerificationResult(bool FailureReproduced, bool OneMinimal, bool Complete,
    int OracleExecutions, double Milliseconds, IReadOnlyList<DeletionCheck> Deletions);

public static class Verifier
{
    // Intentionally bypasses all search caches and checks the exact baseline signature.
    public static VerificationResult Verify(SearchResult result, Oracle oracle,
        CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        int executions = 0;
        bool reproduced = false, complete = false;
        var checks = new List<DeletionCheck>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            executions++;
            reproduced = IsTarget(oracle(Array.AsReadOnly(result.Indices)));
            foreach (int removed in result.Indices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                executions++;
                var observation = oracle(result.Indices.Where(x => x != removed).ToArray());
                checks.Add(new(removed, observation));
            }
            complete = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        clock.Stop();
        return new(reproduced, complete && reproduced && checks.All(x => !IsTarget(x.Observation)),
            complete, executions, clock.Elapsed.TotalMilliseconds, checks.AsReadOnly());

        bool IsTarget(Observation observation) => observation.Outcome == Outcome.TargetFailure
            && observation.Signature == result.Signature;
    }
}
