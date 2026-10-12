using System.ComponentModel;
using System.Diagnostics;

namespace Penelope.Modules.Ollama;

enum FirewallStep
{
    Changed,
    Cancelled,
    Failed,
}

class LanFirewall
{
    public const string RuleName = "Penelope Ollama LAN";

    public virtual bool RuleExists()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("netsh", ShowArguments())
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return false;
            }

            var text = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0 && !text.Contains("No rules match", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public virtual FirewallStep Apply(bool allow, string programPath, int port)
    {
        if (!allow && !RuleExists())
        {
            return FirewallStep.Changed;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + (allow ? AddCommand(programPath, port) : DeleteCommand()))
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
            {
                return FirewallStep.Failed;
            }

            if (!process.WaitForExit(120_000))
            {
                return FirewallStep.Failed;
            }

            return process.ExitCode == 0 ? FirewallStep.Changed : FirewallStep.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return FirewallStep.Cancelled;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return FirewallStep.Failed;
        }
    }

    internal static string AddArguments(string programPath, int port)
    {
        return $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port} profile=private program=\"{programPath}\"";
    }

    internal static string DeleteArguments()
    {
        return $"advfirewall firewall delete rule name=\"{RuleName}\"";
    }

    private static string ShowArguments()
    {
        return $"advfirewall firewall show rule name=\"{RuleName}\"";
    }

    private static string AddCommand(string programPath, int port)
    {
        return $"netsh {DeleteArguments()} & netsh {AddArguments(programPath, port)}";
    }

    private static string DeleteCommand()
    {
        return $"netsh {DeleteArguments()}";
    }
}
