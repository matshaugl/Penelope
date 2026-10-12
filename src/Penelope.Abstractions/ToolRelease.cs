namespace Penelope.Abstractions;

public sealed record ModuleNavItem(string Label, string Path);

public sealed record ToolStatus(ToolRunState State, string Headline);

public sealed record ToolRelease(
    string? InstalledVersion,
    string? LatestVersion,
    DateTimeOffset? CheckedAt,
    bool UpdateAvailable,
    string? CheckError);
