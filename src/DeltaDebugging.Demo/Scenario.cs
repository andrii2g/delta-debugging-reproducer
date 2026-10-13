using System.Text.Json;
using System.Text.Json.Serialization;
using DeltaDebugging.Core;

namespace DeltaDebugging.Demo;

public sealed record Operation(string Op, string? Resource = null, string? Value = null);
public sealed record Scenario(int SchemaVersion, string Name, Operation[] Operations)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Scenario Read(string path)
    {
        var scenario = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(path), Json)
            ?? throw new ArgumentException("Input must be a scenario object.");
        scenario.Validate();
        return scenario;
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || !Names.Contains(Name) || Operations is null)
            throw new ArgumentException("Expected schemaVersion 1, a supported name, and operations.");
        foreach (var operation in Operations)
        {
            if (operation is null || !Allowed[Name].Contains(operation.Op))
                throw new ArgumentException("Unknown operation for this scenario.");
            if (operation.Op is "open" or "close" or "send" && Name == "resource-lifecycle"
                && string.IsNullOrWhiteSpace(operation.Resource))
                throw new ArgumentException("Resource operations require a resource name.");
            if (operation.Op == "setValue" && operation.Value is null)
                throw new ArgumentException("setValue requires a value.");
        }
    }

    public static readonly string[] Names = ["resource-lifecycle", "conflicting-config", "order-corruption"];
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        [Names[0]] = ["noop", "open", "close", "send"],
        [Names[1]] = ["noop", "enableEncryption", "enableCompression", "send"],
        [Names[2]] = ["noop", "begin", "setValue", "rollback", "read"]
    };
}
