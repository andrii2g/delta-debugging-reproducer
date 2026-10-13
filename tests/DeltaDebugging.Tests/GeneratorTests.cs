using DeltaDebugging.Core;
using DeltaDebugging.Demo;
using DeltaDebugging.Cli;

namespace DeltaDebugging.Tests;

public class GeneratorTests
{
    [Theory]
    [InlineData("resource-lifecycle", 3)]
    [InlineData("conflicting-config", 3)]
    [InlineData("order-corruption", 4)]
    public void ThousandOperationInputsAreReproducibleAndMinimize(string name, int expected)
    {
        var scenario = Scenarios.Generate(name, 1000, 42);
        scenario.Validate();
        Assert.Equal(scenario.Operations, Scenarios.Generate(name, 1000, 42).Operations);
        Assert.False(scenario.Operations.SequenceEqual(Scenarios.Generate(name, 1000, 43).Operations));
        foreach (var algorithm in Enum.GetValues<Algorithm>())
        {
            Oracle oracle = indices => Executor.Execute(scenario, indices);
            var result = Minimizer.Run(1000, oracle, new(algorithm));
            Assert.Equal(expected, result.Indices.Length);
            Assert.True(Verifier.Verify(result, oracle).OneMinimal);
        }
        Assert.Throws<ArgumentException>(() => Scenarios.Generate(name, expected - 1, 42));
    }

    [Fact]
    public void GeneratorCommandCreatesReplayableFileAndRefusesOverwrite()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ddrepro-generator-" + Guid.NewGuid());
        string input = Path.Combine(directory, "input.json");
        try
        {
            string[] args = ["generate", "resource-lifecycle", "--count", "1000", "--seed", "42", "--output", input];
            Assert.Equal(0, Commands.Run(args, TextWriter.Null, TextWriter.Null));
            Assert.Equal(1000, Scenario.Read(input).Operations.Length);
            Assert.Equal(1, Commands.Run(["replay", input], TextWriter.Null, TextWriter.Null));
            Assert.Equal(2, Commands.Run(args, TextWriter.Null, TextWriter.Null));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
