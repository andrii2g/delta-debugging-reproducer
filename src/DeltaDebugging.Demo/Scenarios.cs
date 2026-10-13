namespace DeltaDebugging.Demo;

public static class Scenarios
{
    public static Scenario Generate(string name, int count, int seed)
    {
        var small = Small(name);
        if (count < small.Operations.Length)
            throw new ArgumentException($"This scenario requires at least {small.Operations.Length} operations.");
        // Explicit PRNG makes generated inputs stable across runtime versions.
        uint state = unchecked((uint)seed) ^ 0x9e3779b9u;
        if (state == 0) state = 1;
        var positions = new HashSet<int>();
        while (positions.Count < small.Operations.Length)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            positions.Add((int)(state % (uint)count));
        }
        var operations = Enumerable.Range(0, count).Select(i => new Operation("noop", Value: $"noise-{i}")).ToArray();
        int critical = 0;
        foreach (int position in positions.Order()) operations[position] = small.Operations[critical++];
        return small with { Operations = operations };
    }

    public static Scenario Small(string name) => new(1, name, name switch
    {
        "resource-lifecycle" => [new("open", "r1"), new("close", "r1"), new("send", "r1")],
        "conflicting-config" => [new("enableEncryption"), new("enableCompression"), new("send")],
        "order-corruption" => [new("begin"), new("setValue", Value: "changed"), new("rollback"), new("read")],
        _ => throw new ArgumentException("Unknown scenario name.")
    });
}
