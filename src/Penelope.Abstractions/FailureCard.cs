namespace Penelope.Abstractions;

public enum FixKind
{
    Retry,
    CheckAgain,
    OpenUrl,
    OpenFirewall,
    OpenDisk,
}

public sealed record FailureFix(string Id, string Label, FixKind Kind, string? Url);

public sealed record FailureCard(string Summary, string Detail, IReadOnlyList<FailureFix> Fixes);

public sealed record ModuleActionResult(bool Succeeded, FailureCard? Failure);
