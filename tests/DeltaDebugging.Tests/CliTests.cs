using System.Text.Json;
using System.Xml.Linq;
using DeltaDebugging.Cli;
using DeltaDebugging.Core;
using DeltaDebugging.Demo;

namespace DeltaDebugging.Tests;

public class CliTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ddrepro-tests-" + Guid.NewGuid());
    public CliTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData("minimize")]
    [InlineData("compare")]
    public void ProducesReplayableVerifiedArtifacts(string command)
    {
        string input = Path.Combine(directory, "input.json"), output = Path.Combine(directory, "output");
        Commands.Save(input, Scenarios.Small("order-corruption"));
        Assert.Equal(0, Commands.Run([command, input, "--output", output], TextWriter.Null, TextWriter.Null));
        string run = command == "compare" ? Path.Combine(output, "ddmin") : output;
        var minimized = Scenario.Read(Path.Combine(run, "minimized.json"));
        Assert.Equal(Outcome.TargetFailure, Executor.Execute(minimized, [0, 1, 2, 3]).Outcome);
        var report = JsonSerializer.Deserialize<RunReport>(File.ReadAllText(Path.Combine(run, "report.json")), Scenario.Json)!;
        Assert.True(report.Verification.OneMinimal);
        Assert.Equal(report.Search.OracleExecutions + report.Verification.OracleExecutions, report.TotalOracleExecutions);
        Assert.Equal(1, Commands.Run(["replay", Path.Combine(run, "minimized.json")], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, Commands.Run([command, input, "--output", output], TextWriter.Null, TextWriter.Null));
        Assert.Equal("svg", XDocument.Load(Path.Combine(run, "reduction.svg")).Root!.Name.LocalName);
    }

    [Fact]
    public void BudgetProducesArtifactsButIncompleteExitCode()
    {
        string input = Path.Combine(directory, "input.json"), output = Path.Combine(directory, "output");
        Commands.Save(input, Scenarios.Small("resource-lifecycle"));
        Assert.Equal(3, Commands.Run(["minimize", input, "--max-executions", "1", "--output", output], TextWriter.Null, TextWriter.Null));
        Assert.True(File.Exists(Path.Combine(output, "report.json")));
    }

    [Theory]
    [InlineData("--algorithm", "invalid")]
    [InlineData("--max-executions", "0")]
    [InlineData("--unknown", "value")]
    public void RejectsBadOptions(string option, string value)
    {
        string input = Path.Combine(directory, "input.json");
        Commands.Save(input, Scenarios.Small("resource-lifecycle"));
        Assert.Equal(2, Commands.Run(["minimize", input, option, value], TextWriter.Null, TextWriter.Null));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"name\":\"resource-lifecycle\",\"operations\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"name\":\"resource-lifecycle\",\"operations\":[],\"typo\":1}")]
    public void RejectsMalformedInputs(string json)
    {
        string input = Path.Combine(directory, "input.json");
        File.WriteAllText(input, json);
        Assert.Equal(2, Commands.Run(["replay", input], TextWriter.Null, TextWriter.Null));
    }

    [Fact]
    public void SvgEscapesLabelsPaginatesAndIncludesEveryAttempt()
    {
        var result = Minimizer.Run(80, x => x.Count == 80 ? new(Outcome.TargetFailure, "<bug>&") : new(Outcome.Pass));
        SvgRenderer.Write(result, directory);
        var files = Directory.GetFiles(directory, "*.svg");
        Assert.True(files.Length > 1);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var documents = files.Select(XDocument.Load).ToArray();
        Assert.Equal(result.Trace.Count, documents.Sum(x => x.Descendants(ns + "g").Count()));
        Assert.Contains(documents.SelectMany(x => x.Descendants(ns + "title")), x => x.Value.Contains("<bug>&", StringComparison.Ordinal));
        Assert.All(documents.SelectMany(x => x.Descendants(ns + "a")), link =>
            Assert.True(File.Exists(Path.Combine(directory, link.Attribute("href")!.Value))));
    }
}

