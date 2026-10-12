using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Penelope.Host.Settings;

public class SignInStartup
{
    public const string ValueName = "Penelope";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly bool _memoryOnly;
    private bool _memoryEnabled;

    public SignInStartup()
        : this(memoryOnly: false)
    {
    }

    internal SignInStartup(bool memoryOnly)
    {
        _memoryOnly = memoryOnly;
    }

    public bool IsEnabled()
    {
        if (_memoryOnly)
        {
            return _memoryEnabled;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return ReadValue() is not null;
    }

    public bool Set(bool enabled)
    {
        if (_memoryOnly)
        {
            _memoryEnabled = enabled;
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!enabled)
        {
            DeleteValue();
            return true;
        }

        var command = QuoteHostPath(Environment.ProcessPath, AppContext.BaseDirectory);
        if (command is null)
        {
            return false;
        }

        WriteValue(command);
        return true;
    }

    internal static string? QuoteHostPath(string? processPath, string baseDirectory)
    {
        var path = IsHostExe(processPath) ? processPath! : Path.Combine(baseDirectory, "Penelope.Host.exe");
        if (!File.Exists(path))
        {
            return null;
        }

        return $"\"{path}\"";
    }

    private static bool IsHostExe(string? path)
    {
        return path is not null
            && path.EndsWith($"{Path.DirectorySeparatorChar}Penelope.Host.exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(path);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var value = key?.GetValue(ValueName) as string;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [SupportedOSPlatform("windows")]
    private static void WriteValue(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key?.SetValue(ValueName, command);
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
