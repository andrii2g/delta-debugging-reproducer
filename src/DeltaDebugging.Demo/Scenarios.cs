namespace DeltaDebugging.Demo;

public static class Scenarios
{
    public static Scenario Small(string name) => new(1, name, name switch
    {
        "resource-lifecycle" => [new("open", "r1"), new("close", "r1"), new("send", "r1")],
        "conflicting-config" => [new("enableEncryption"), new("enableCompression"), new("send")],
        "order-corruption" => [new("begin"), new("setValue", Value: "changed"), new("rollback"), new("read")],
        _ => throw new ArgumentException("Unknown scenario name.")
    });
}
