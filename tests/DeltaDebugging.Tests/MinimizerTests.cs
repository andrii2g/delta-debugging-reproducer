using DeltaDebugging.Core;
using DeltaDebugging.Demo;

namespace DeltaDebugging.Tests;

public class MinimizerTests
{
    private static Observation Target => new(Outcome.TargetFailure, "bug");
    private static Observation Pass => new(Outcome.Pass);

    [Theory]
    [InlineData("resource-lifecycle", 3)]
    [InlineData("conflicting-config", 3)]
    [InlineData("order-corruption", 4)]
    public void ScenariosReduceToTheirEssentialOperations(string name, int expected)
    {
        var small = Scenarios.Small(name);
        var operations = Enumerable.Repeat(new Operation("noop"), 17)
            .Concat(small.Operations.SelectMany(x => new[] { x, new Operation("noop") }))
            .Concat(Enumerable.Repeat(new Operation("noop"), 23)).ToArray();
        var scenario = small with { Operations = operations };
        scenario.Validate();
        var baseline = Executor.Execute(scenario, Enumerable.Range(0, operations.Length).ToArray());
        foreach (var algorithm in Enum.GetValues<Algorithm>())
        {
            var oracle = Executor.Match(scenario, baseline.Signature!);
            var result = Minimizer.Run(operations.Length, oracle, new(algorithm));
            Assert.True(result.Complete);
            Assert.Equal(expected, result.Indices.Length);
            Assert.Equal(small.Operations, result.Indices.Select(x => operations[x]));
            var verification = Verifier.Verify(result, oracle);
            Assert.True(verification.FailureReproduced);
            Assert.True(verification.OneMinimal);
            Assert.Equal(expected + 1, verification.OracleExecutions);
        }
    }

    [Theory]
    [InlineData(Algorithm.Ddmin)]
    [InlineData(Algorithm.Naive)]
    public void HandlesEmptyAndSingletonFailures(Algorithm algorithm)
    {
        Assert.Empty(Minimizer.Run(5, _ => Target, new(algorithm)).Indices);
        Assert.Empty(Minimizer.Run(0, _ => Target, new(algorithm)).Indices);
        Assert.Equal(new[] { 0 }, Minimizer.Run(1, x => x.Count == 1 ? Target : Pass, new(algorithm)).Indices);
        Assert.Throws<ArgumentException>(() => Minimizer.Run(1, _ => Pass, new(algorithm)));
    }

    [Theory]
    [InlineData(Algorithm.Ddmin)]
    [InlineData(Algorithm.Naive)]
    public void KeepsDuplicatePositionsAndRelativeOrder(Algorithm algorithm)
    {
        Oracle oracle = x => x.Contains(1) && x.Contains(3) ? Target : Pass;
        var result = Minimizer.Run(5, oracle, new(algorithm));
        Assert.Equal(new[] { 1, 3 }, result.Indices);
        Assert.All(result.Trace, x => Assert.Equal(x.Indices.Order(), x.Indices));
    }

    [Fact]
    public void NaiveRestartsAfterSuccessfulDeletion()
    {
        Oracle oracle = x => x.SequenceEqual(new[] { 0, 1, 2 }) || x.SequenceEqual(new[] { 0, 1 })
            || x.SequenceEqual(new[] { 1 }) ? Target : Pass;
        Assert.Equal(new[] { 1 }, Minimizer.Run(3, oracle, new(Algorithm.Naive)).Indices);
    }

    [Fact]
    public void CacheCountsExecutionsAndStillRecordsAttempts()
    {
        int calls = 0;
        var result = Minimizer.Run(2, x => { calls++; return x.Count == 2 ? Target : Pass; });
        Assert.Equal(4, calls);
        Assert.Equal(calls, result.Metrics.OracleExecutions);
        Assert.Equal(2, result.Metrics.CacheHits);
        Assert.Equal(result.Trace.Count, calls + result.Metrics.CacheHits);
        Assert.Contains(result.Trace, x => x.Kind == AttemptKind.Complement && x.CacheHit);
        Assert.All(result.Trace.Skip(1), x => Assert.Equal(0, x.ParentId));
    }

    [Fact]
    public void DoesNotAcceptDifferentFailuresOrUnresolvedCandidates()
    {
        var result = Minimizer.Run(3, x => x.Count == 3 ? Target : x.Count == 0
            ? new(Outcome.Unresolved) : new(Outcome.TargetFailure, "different-bug"));
        Assert.Equal(3, result.Indices.Length);
        Assert.Contains(result.Trace, x => x.Observation.Outcome == Outcome.OtherFailure);
    }

    [Fact]
    public void VerificationDetectsFlakyOracleWithoutUsingCache()
    {
        var result = Minimizer.Run(1, x => x.Count == 1 ? Target : Pass);
        var verification = Verifier.Verify(result, _ => Pass);
        Assert.False(verification.FailureReproduced);
        Assert.False(verification.OneMinimal);
    }

    [Fact]
    public void BudgetAndCancellationReturnIncompleteSearch()
    {
        var budget = Minimizer.Run(5, _ => Target, new(MaxExecutions: 1));
        Assert.False(budget.Complete);
        Assert.Equal(5, budget.Indices.Length);
        using var cancellation = new CancellationTokenSource();
        var result = Minimizer.Run(3, _ => { cancellation.Cancel(); return Target; },
            cancellationToken: cancellation.Token);
        Assert.False(result.Complete);
        var verification = Verifier.Verify(result, _ => Target, cancellation.Token);
        Assert.False(verification.Complete);
        Assert.False(verification.OneMinimal);
    }

    [Fact]
    public void ExhaustiveSmallNonMonotonicPredicatesProduceOneMinimalResults()
    {
        foreach (var algorithm in Enum.GetValues<Algorithm>())
        for (int truthTable = 128; truthTable < 256; truthTable++)
        {
            int table = truthTable;
            Oracle oracle = indices => (table & (1 << indices.Aggregate(0, (mask, i) => mask | (1 << i)))) != 0
                ? Target : Pass;
            var result = Minimizer.Run(3, oracle, new(algorithm));
            Assert.True(Verifier.Verify(result, oracle).OneMinimal);
        }
    }

    [Fact]
    public void ExecutorUsesFreshStateAndMatchesResourceIdentity()
    {
        var scenario = Scenarios.Small("resource-lifecycle");
        Assert.Equal(Executor.Execute(scenario, [0, 1, 2]), Executor.Execute(scenario, [0, 1, 2]));
        Assert.Equal(Outcome.Unresolved, Executor.Execute(scenario, [2]).Outcome);
        Assert.Equal(Outcome.OtherFailure, Executor.Match(scenario, "RESOURCE_USE_AFTER_CLOSE:other")([0, 1, 2]).Outcome);
    }

    [Fact]
    public void ValidatesMalformedOperations()
    {
        Assert.Throws<ArgumentException>(() => new Scenario(1, "resource-lifecycle", [new("open")]).Validate());
        Assert.Throws<ArgumentException>(() => new Scenario(1, "order-corruption", [new("send")]).Validate());
        Assert.Throws<ArgumentException>(() => new Scenario(2, "order-corruption", []).Validate());
    }
}
