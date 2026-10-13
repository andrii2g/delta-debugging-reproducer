using System.Text.Json;
using DeltaDebugging.Core;
using DeltaDebugging.Demo;
using DeltaDebugging.Cli;

return Commands.Run(args, Console.Out, Console.Error);

namespace DeltaDebugging.Cli
{
    public static class Commands
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                if (args.Length == 0 || args is ["--help"] or ["help"])
                {
                    output.WriteLine("""
                        ddrepro replay <input.json>
                        ddrepro minimize <input.json> [--algorithm ddmin|naive] [--output directory] [--max-executions N]
                        ddrepro compare <input.json> [--output directory] [--max-executions N]
                        Exit codes: 0 success; 1 replay failure; 2 invalid input/tool error; 3 incomplete or unverified reduction; 130 cancelled.
                        """);
                    return 0;
                }
                if (args.Length < 2) throw new ArgumentException("A command and input path are required.");
                string command = args[0];
                if (command is not ("replay" or "minimize" or "compare"))
                    throw new ArgumentException("Unknown command. Use --help.");
                var flags = ParseFlags(args[2..]);
                var allowed = command == "replay" ? Array.Empty<string>() : command == "compare"
                    ? ["--output", "--max-executions"] : new[] { "--output", "--max-executions", "--algorithm" };
                if (flags.Keys.Except(allowed).Any()) throw new ArgumentException("Unsupported option for this command.");
                var scenario = Scenario.Read(args[1]);
                if (command == "replay")
                {
                    var observation = Executor.Execute(scenario, Enumerable.Range(0, scenario.Operations.Length).ToArray());
                    output.WriteLine(JsonSerializer.Serialize(observation, Scenario.Json));
                    return observation.Outcome == Outcome.Pass ? 0 : 1;
                }
                int budget = int.MaxValue;
                if (flags.TryGetValue("--max-executions", out var value)
                    && (!int.TryParse(value, out budget) || budget < 1))
                    throw new ArgumentException("--max-executions must be a positive integer.");
                var algorithm = flags.GetValueOrDefault("--algorithm", "ddmin") switch
                {
                    "ddmin" => Algorithm.Ddmin, "naive" => Algorithm.Naive,
                    _ => throw new ArgumentException("--algorithm must be ddmin or naive.")
                };
                string directory = flags.GetValueOrDefault("--output", "artifacts/run");
                if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                    throw new ArgumentException("Output directory must be empty; choose a new --output path.");
                var algorithms = command == "compare" ? new[] { Algorithm.Ddmin, Algorithm.Naive } : [algorithm];
                var reports = new List<RunReport>();
                foreach (var selected in algorithms)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    Oracle oracle = indices => Executor.Execute(scenario, indices);
                    var search = Minimizer.Run(scenario.Operations.Length, oracle, new(selected, budget), cancellation.Token);
                    var verification = Verifier.Verify(search, oracle, cancellation.Token);
                    string destination = command == "compare" ? Path.Combine(directory, selected.ToString().ToLowerInvariant()) : directory;
                    Directory.CreateDirectory(destination);
                    var report = new RunReport(selected, search.Signature, search.Complete, search.StopReason,
                        search.Indices, search.Metrics, verification,
                        search.Metrics.OracleExecutions + verification.OracleExecutions,
                        search.Metrics.BaselineMilliseconds + search.Metrics.SearchMilliseconds + verification.Milliseconds);
                    reports.Add(report);
                    Save(Path.Combine(destination, "minimized.json"), scenario with
                    { Operations = search.Indices.Select(i => scenario.Operations[i]).ToArray() });
                    Save(Path.Combine(destination, "report.json"), report);
                    Save(Path.Combine(destination, "trace.json"), search.Trace);
                    SvgRenderer.Write(search, destination);
                    output.WriteLine($"{selected}: {search.Metrics.OriginalCount} -> {search.Metrics.MinimizedCount} operations " +
                        $"({search.Metrics.ReductionPercentage:F1}% reduction), {report.TotalOracleExecutions} oracle executions, " +
                        $"{search.Metrics.CacheHits} cache hits; reproduced={verification.FailureReproduced}, " +
                        $"1-minimal={verification.OneMinimal}, complete={search.Complete}. Artifacts: {Path.GetFullPath(destination)}");
                    if (cancellation.IsCancellationRequested) return 130;
                }
                if (command == "compare") Save(Path.Combine(directory, "comparison.json"), reports);
                return reports.All(x => x.SearchComplete && x.Verification.OneMinimal) ? 0 : 3;
            }
            catch (OperationCanceledException) { error.WriteLine("Cancelled."); return 130; }
            catch (Exception exception) when (exception is ArgumentException or JsonException or IOException or UnauthorizedAccessException)
            {
                error.WriteLine(exception.Message);
                return 2;
            }
            finally { Console.CancelKeyPress -= handler; }
        }

        private static Dictionary<string, string> ParseFlags(string[] args)
        {
            var flags = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i += 2)
                if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal)
                    || !flags.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("Options require one value each and cannot be repeated.");
            return flags;
        }

        public static void Save<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Scenario.Json) + Environment.NewLine);
    }

    public sealed record RunReport(Algorithm Algorithm, string Signature, bool SearchComplete, string? StopReason,
        int[] RetainedOriginalIndices, SearchMetrics Search, VerificationResult Verification,
        int TotalOracleExecutions, double TotalMilliseconds);
}
