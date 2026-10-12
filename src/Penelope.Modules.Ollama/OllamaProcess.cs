using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

[assembly: SupportedOSPlatform("windows")]

namespace Penelope.Modules.Ollama;

// Spike recorded on 12 October 2026 against the Ollama 0.35.1 already installed on this PC.
// The installer uses these notes. See OllamaSetup.
//
// 1. The live installer is per-user and does not ask for administrator rights.
//    This copy is in %LOCALAPPDATA%\Programs\Ollama and is registered only under
//    HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall. The official install
//    script runs OllamaSetup.exe /VERYSILENT /NORESTART /SUPPRESSMSGBOXES.
//    Quiet removal is unins000.exe /SILENT.
//
// 2. The program that opens port 11434 is ollama.exe. Its command line was
//    "ollama.exe serve", and its parent was "ollama app.exe". The socket was
//    127.0.0.1:11434. Start launches ollama app.exe when that file is beside the
//    server, because the tray is what starts serve. If the tray program is missing,
//    Start launches "ollama.exe serve" itself.
//
// 3. The tray passes its environment to that child. The app setting
//    "Expose Ollama to the network" overrides OLLAMA_HOST (ollama/ollama issue 15186).
//    This PC had no OLLAMA_HOST value, and the listener was loopback, so that
//    override was not changed here. A later listen-address change has to stop the
//    tray and start "ollama.exe serve" with OLLAMA_HOST set on that process.
//    The user variable does not always win while the tray is the parent.
//
// 4. The GitHub release publishes sha256sum.txt next to OllamaSetup.exe.
//    v0.40.3 names the file "./OllamaSetup.exe" (two spaces, then a ./ prefix).
//    The release API also puts sha256:<hex> on the asset as digest. Those two
//    values matched for OllamaSetup.exe on 12 October 2026. The official
//    install.ps1 checks the installer's Authenticode signature and does not
//    compare that file.

public sealed record OllamaLocation(string? ExecutablePath, string? AppPath, string? Version)
{
    public bool IsPresent => ExecutablePath is not null || Version is not null;
}

public class OllamaProcess
{
    private string? _programVersion;

    public static int ReadConfiguredPort()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Penelope",
                "config.json");
            if (!File.Exists(path))
            {
                return 11434;
            }

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("ollamaPort", out var port)
                && port.TryGetInt32(out var value)
                && value is > 0 and <= 65535)
            {
                return value;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
        }

        return 11434;
    }

    public virtual OllamaLocation Locate()
    {
        var executable = FindExecutable();
        var app = Sibling(executable, "ollama app.exe");
        var version = ReadRegistryVersion() ?? ReadFileVersion(executable) ?? _programVersion;
        if (version is null && executable is not null)
        {
            version = ReadProgramVersion(executable);
            _programVersion = version;
        }

        return new OllamaLocation(executable, app, version);
    }

    public virtual int? ListenerPid(int port)
    {
        return TcpListeners.OwningProcessId(port);
    }

    public virtual int? ParentPid(int processId)
    {
        return ProcessTree.ParentOf(processId);
    }

    protected string? RequestedHost { get; private set; }

    public int LaunchServing(string path, string ollamaHost)
    {
        RequestedHost = ollamaHost;
        try
        {
            return Launch(path, serve: true);
        }
        finally
        {
            RequestedHost = null;
        }
    }

    public virtual int Launch(string path, bool serve)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = !serve,
        };
        if (serve)
        {
            start.ArgumentList.Add("serve");
            start.CreateNoWindow = true;
            if (RequestedHost is not null)
            {
                start.Environment["OLLAMA_HOST"] = RequestedHost;
            }
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("Ollama didn't start.");
        return process.Id;
    }

    public virtual void Stop(IReadOnlyCollection<int> processIds, OllamaLocation location)
    {
        var ordered = new List<int>();
        foreach (var processId in processIds.Distinct())
        {
            var parent = ParentPid(processId);
            if (parent is int parentId && IsOllamaImage(ImagePath(parentId), location))
            {
                ordered.Add(parentId);
            }

            ordered.Add(processId);
        }

        foreach (var processId in ordered.Distinct())
        {
            TryClose(processId, location);
        }
    }

    internal static bool IsOllamaImage(string? imagePath, OllamaLocation location)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        if (location.ExecutablePath is not null
            && string.Equals(imagePath, location.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return location.AppPath is not null
            && string.Equals(imagePath, location.AppPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryClose(int processId, OllamaLocation location)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!IsOllamaImage(SafeImage(process), location))
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
        }
    }

    private static string? ImagePath(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return SafeImage(process);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static string? SafeImage(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static string? FindExecutable()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Ollama",
            "ollama.exe");
        if (File.Exists(local))
        {
            return local;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "ollama.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
            }
        }

        return null;
    }

    private static string? Sibling(string? executable, string fileName)
    {
        if (executable is null)
        {
            return null;
        }

        var candidate = Path.Combine(Path.GetDirectoryName(executable) ?? "", fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ReadRegistryVersion()
    {
        try
        {
            using var uninstall = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                return null;
            }

            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(name);
                var display = key?.GetValue("DisplayName") as string;
                if (display is null || !display.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var version = key?.GetValue("DisplayVersion") as string;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version.Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }

        return null;
    }

    private static string? ReadFileVersion(string? executable)
    {
        if (executable is null || !File.Exists(executable))
        {
            return null;
        }

        var info = FileVersionInfo.GetVersionInfo(executable);
        var version = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
        return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
    }

    private static string? ReadProgramVersion(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null || !process.WaitForExit(3000))
            {
                return null;
            }

            var text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            var marker = "version is ";
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            var version = text[(index + marker.Length)..].Trim().Split(' ', '\r', '\n')[0];
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            return null;
        }
    }
}

internal static class TcpListeners
{
    public static IReadOnlyList<string> ListenAddresses(int port)
    {
        var found = new List<string>();
        Walk(port, (address, _) => found.Add(address));
        return found;
    }

    public static int? OwningProcessId(int port)
    {
        int? owner = null;
        Walk(port, (_, pid) => owner ??= pid);
        return owner;
    }

    private static void Walk(int port, Action<string, int> visit)
    {
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, 3, 0);
        if (result != 0 && result != 122)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(buffer, ref size, true, 2, 3, 0);
            if (result != 0)
            {
                return;
            }

            var count = Marshal.ReadInt32(buffer);
            var row = buffer + 4;
            var rowSize = Marshal.SizeOf<TcpRow>();
            for (var index = 0; index < count; index++)
            {
                var entry = Marshal.PtrToStructure<TcpRow>(row + (index * rowSize));
                var localPort = (entry.LocalPort & 0xFF) << 8 | (entry.LocalPort >> 8) & 0xFF;
                if (localPort == port)
                {
                    var bytes = BitConverter.GetBytes(entry.LocalAddr);
                    visit(new System.Net.IPAddress(bytes).ToString(), entry.OwningPid);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public int OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        bool order,
        int addressFamily,
        int tableClass,
        uint reserved);
}

internal static class ProcessTree
{
    public static int? ParentOf(int processId)
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return null;
            }

            do
            {
                if (entry.ProcessId == (uint)processId)
                {
                    return (int)entry.ParentProcessId;
                }
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint ThreadCount;
        public uint ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
