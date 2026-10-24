using System.Globalization;

namespace Penelope.Abstractions;

/// <summary>
/// The plugin contract version this host implements. Compatibility rules are in docs/PLUGIN_SYSTEM.md.
/// </summary>
public static class HostApi
{
    public const int Major = 1;

    public const int Minor = 0;

    public static HostApiVersion Current { get; } = new(Major, Minor);
}

public enum HostApiCompatibility
{
    Compatible,
    NeedsNewerHost,
    BuiltForOlderHost,
}

/// <summary>
/// A <c>MAJOR.MINOR</c> plugin contract version, written as <c>"hostApi": "1.0"</c> in manifest.json.
/// </summary>
public readonly record struct HostApiVersion(int Major, int Minor)
{
    public static bool TryParse(string? text, out HostApiVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split('.');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        version = new HostApiVersion(major, minor);
        return true;
    }

    /// <summary>
    /// Whether a host implementing <paramref name="host"/> can load a plugin that declares this version.
    /// </summary>
    public HostApiCompatibility CheckAgainst(HostApiVersion host)
    {
        if (Major > host.Major || (Major == host.Major && Minor > host.Minor))
        {
            return HostApiCompatibility.NeedsNewerHost;
        }

        return Major < host.Major ? HostApiCompatibility.BuiltForOlderHost : HostApiCompatibility.Compatible;
    }

    public override string ToString() => $"{Major}.{Minor}";
}
