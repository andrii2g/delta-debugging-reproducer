using DeltaDebugging.Core;

namespace DeltaDebugging.Demo;

public static class Executor
{
    // State is deliberately local: no candidate can contaminate the next evaluation.
    public static Observation Execute(Scenario scenario, IReadOnlyList<int> indices)
    {
        var resources = new Dictionary<string, bool>();
        var writableHandles = new HashSet<string>();
        bool encryption = false, invalidPipeline = false, transaction = false;
        string value = "initial", cached = value, saved = value;
        foreach (int index in indices)
        {
            var op = scenario.Operations[index];
            switch (op.Op)
            {
                case "noop": break;
                case "open":
                    resources[op.Resource!] = true;
                    writableHandles.Add(op.Resource!);
                    break;
                case "close":
                    if (!resources.TryGetValue(op.Resource!, out bool opened) || !opened)
                        return new(Outcome.Unresolved);
                    resources[op.Resource!] = false;
                    // BUG: the writable handle survives close.
                    break;
                case "send" when scenario.Name == "resource-lifecycle":
                    if (!resources.TryGetValue(op.Resource!, out bool active))
                        return new(Outcome.Unresolved);
                    if (!active && writableHandles.Contains(op.Resource!))
                        return new(Outcome.TargetFailure, "RESOURCE_USE_AFTER_CLOSE:" + op.Resource);
                    break;
                case "enableEncryption": encryption = true; break;
                case "enableCompression":
                    // BUG: compression is inserted on the wrong side of existing encryption.
                    invalidPipeline = encryption;
                    break;
                case "send":
                    if (invalidPipeline) return new(Outcome.TargetFailure, "INVALID_PIPELINE");
                    break;
                case "begin":
                    if (transaction) return new(Outcome.Unresolved);
                    transaction = true;
                    saved = value;
                    break;
                case "setValue": value = cached = op.Value!; break;
                case "rollback":
                    if (!transaction) return new(Outcome.Unresolved);
                    value = saved;
                    transaction = false;
                    // BUG: cached value is not restored or invalidated.
                    break;
                case "read":
                    if (value != cached) return new(Outcome.TargetFailure, "STALE_READ_AFTER_ROLLBACK");
                    break;
                default: throw new InvalidOperationException("Validate the input before executing it.");
            }
        }
        return new(Outcome.Pass);
    }

    public static Oracle Match(Scenario scenario, string signature) => indices =>
    {
        var observation = Execute(scenario, indices);
        return observation.Outcome == Outcome.TargetFailure && observation.Signature != signature
            ? observation with { Outcome = Outcome.OtherFailure } : observation;
    };
}
