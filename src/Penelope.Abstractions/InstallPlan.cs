namespace Penelope.Abstractions;

public sealed record InstallStep(string Label, string Detail);

public sealed record InstallPlan(IReadOnlyList<InstallStep> Steps);
