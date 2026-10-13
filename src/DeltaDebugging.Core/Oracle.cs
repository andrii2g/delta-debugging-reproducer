namespace DeltaDebugging.Core;

public enum Outcome { Pass, TargetFailure, OtherFailure, Unresolved }
public sealed record Observation(Outcome Outcome, string? Signature = null);
public delegate Observation Oracle(IReadOnlyList<int> indices);
