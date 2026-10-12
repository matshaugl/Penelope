using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Penelope.Host.SystemInfo;

static partial class GraphicsCards
{
    private const uint Nvidia = 0x10DE;
    private const uint Amd = 0x1002;
    private const uint Intel = 0x8086;
    private static readonly Guid FactoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");

    private static readonly object CacheGate = new();
    private static IReadOnlyList<Reading>? _cached;
    private static DateTimeOffset _cachedAt;

    public static IReadOnlyList<Reading> Read()
    {
        lock (CacheGate)
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromSeconds(4))
            {
                return _cached;
            }

            var next = ReadFresh();
            _cached = next;
            _cachedAt = DateTimeOffset.UtcNow;
            return next;
        }
    }

    private static IReadOnlyList<Reading> ReadFresh()
    {
        var adapters = ListAdapters();
        var nvidia = QueryNvidiaSmi();
        if (adapters.Count == 0 && nvidia.Count == 0)
        {
            return [new Reading(false, "This computer has no separate graphics card.", null, null)];
        }

        var readings = new List<Reading>();
        var nvidiaIndex = 0;
        if (adapters.Count == 0)
        {
            readings.AddRange(nvidia.Select(ToNvidiaReading));
            return readings;
        }

        var engines = adapters.Any(adapter => adapter.VendorId != Nvidia) ? SampleEngines() : [];
        foreach (var adapter in adapters)
        {
            if (adapter.VendorId == Nvidia && nvidiaIndex < nvidia.Count)
            {
                readings.Add(ToNvidiaReading(nvidia[nvidiaIndex]));
                nvidiaIndex++;
                continue;
            }

            readings.Add(ToAdapterReading(adapter, engines));
        }

        while (nvidiaIndex < nvidia.Count)
        {
            readings.Add(ToNvidiaReading(nvidia[nvidiaIndex]));
            nvidiaIndex++;
        }

        return readings;
    }

    internal static IReadOnlyList<NvidiaGpu> ParseNvidiaSmi(string text)
    {
        var list = new List<NvidiaGpu>();
        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(',');
            if (parts.Length < 4)
            {
                continue;
            }

            if (!double.TryParse(parts[^3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var usedMib)
                || !double.TryParse(parts[^2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var totalMib)
                || !double.TryParse(parts[^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var util))
            {
                continue;
            }

            var name = string.Join(',', parts[..^3]).Trim();
            if (name.Length == 0 || totalMib <= 0)
            {
                continue;
            }

            list.Add(new NvidiaGpu(
                name,
                (long)(usedMib * 1024 * 1024),
                (long)(totalMib * 1024 * 1024),
                Math.Clamp(util, 0, 100)));
        }

        return list;
    }

    private static Reading ToNvidiaReading(NvidiaGpu gpu)
    {
        var used = gpu.UsedBytes / 1_073_741_824d;
        var total = gpu.TotalBytes / 1_073_741_824d;
        return new Reading(
            true,
            null,
            gpu.Utilization,
            $"{gpu.Name} · {used:0.0} GB of {total:0.0} GB video memory",
            gpu.TotalBytes);
    }

    private static Reading ToAdapterReading(Adapter adapter, IReadOnlyList<EngineSample> engines)
    {
        var used = DedicatedUsage(adapter);
        var total = adapter.DedicatedVideoMemory > 0 ? adapter.DedicatedVideoMemory : adapter.SharedSystemMemory;
        if (total <= 0 && used is null)
        {
            var page = DriverPage(adapter.VendorId);
            return new Reading(
                false,
                $"{adapter.Name} is present. Penelope could not confirm its driver, so models will use system memory.",
                null,
                null,
                null,
                page is null ? null : "Open the driver page",
                page);
        }

        var engine = EngineUtilization(engines, adapter.LuidLow, adapter.LuidHigh);
        var memoryPercent = used is > 0 && total > 0 ? Math.Clamp(used.Value * 100d / total, 0, 100) : (double?)null;
        var usedText = used is null ? "usage unknown" : $"{used.Value / 1_073_741_824d:0.0} GB in use";
        var kind = adapter.DedicatedVideoMemory > 0 ? "video memory" : "shared memory";
        var approximate = engine is null ? "about " : "";
        return new Reading(
            true,
            null,
            engine ?? memoryPercent,
            $"{adapter.Name} · {approximate}{usedText} of {total / 1_073_741_824d:0.0} GB {kind}",
            (long)total);
    }

    internal static double? EngineUtilization(IEnumerable<EngineSample> samples, uint luidLow, int luidHigh)
    {
        double threeD = 0;
        double compute = 0;
        var matched = false;
        var high = unchecked((uint)luidHigh);
        foreach (var sample in samples)
        {
            if (!TryParseEngine(sample.Instance, out var low, out var sampleHigh, out var kind))
            {
                continue;
            }

            if (low != luidLow || sampleHigh != high)
            {
                continue;
            }

            if (kind.Equals("3D", StringComparison.OrdinalIgnoreCase))
            {
                threeD += sample.Percent;
                matched = true;
            }
            else if (kind.Equals("Compute", StringComparison.OrdinalIgnoreCase))
            {
                compute += sample.Percent;
                matched = true;
            }
        }

        return matched ? Math.Clamp(Math.Max(threeD, compute), 0, 100) : null;
    }

    internal static bool TryParseEngine(string instance, out uint luidLow, out uint luidHigh, out string kind)
    {
        luidLow = 0;
        luidHigh = 0;
        kind = "";
        var match = EngineInstance().Match(instance);
        if (!match.Success
            || !uint.TryParse(match.Groups["high"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out luidHigh)
            || !uint.TryParse(match.Groups["low"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out luidLow))
        {
            return false;
        }

        kind = match.Groups["kind"].Value;
        return kind.Length > 0;
    }

    [System.Text.RegularExpressions.GeneratedRegex("luid_0x(?<high>[0-9a-fA-F]+)_0x(?<low>[0-9a-fA-F]+)_phys_\\d+_eng_\\d+_engtype_(?<kind>[A-Za-z0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex EngineInstance();

    private static IReadOnlyList<EngineSample> SampleEngines()
    {
        var counters = new List<PerformanceCounter>();
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                return [];
            }

            var names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
            foreach (var name in names)
            {
                if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    counters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", name, readOnly: true));
                }
                catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                }
            }

            if (counters.Count == 0)
            {
                return [];
            }

            foreach (var counter in counters)
            {
                _ = counter.NextValue();
            }

            Thread.Sleep(200);
            return counters
                .Select(counter => new EngineSample(counter.InstanceName, counter.NextValue()))
                .ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
        finally
        {
            foreach (var counter in counters)
            {
                counter.Dispose();
            }
        }
    }

    private static long? DedicatedUsage(Adapter adapter)
    {
        var instance = $"luid_0x{adapter.LuidHigh:x8}_0x{adapter.LuidLow:x8}_phys_0";
        try
        {
            using var counter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instance, true);
            var value = counter.NextValue();
            return value < 0 ? null : (long)value;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<NvidiaGpu> QueryNvidiaSmi()
    {
        var file = FindNvidiaSmi();
        if (file is null)
        {
            return [];
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = "--query-gpu=name,memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2000))
            {
                process.Kill(entireProcessTree: true);
                return [];
            }

            return process.ExitCode == 0 ? ParseNvidiaSmi(output) : [];
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    private static string? FindNvidiaSmi()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), "nvidia-smi.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static string? DriverPage(uint vendorId)
    {
        return vendorId switch
        {
            Nvidia => "https://www.nvidia.com/Download/index.aspx",
            Amd => "https://www.amd.com/en/support",
            Intel => "https://www.intel.com/content/www/us/en/download-center/home.html",
            _ => null,
        };
    }

    private static List<Adapter> ListAdapters()
    {
        var id = FactoryId;
        if (CreateDXGIFactory1(ref id, out var factory) != 0 || factory == 0)
        {
            return [];
        }

        var adapters = new List<Adapter>();
        try
        {
            for (uint index = 0; index < 16; index++)
            {
                if (EnumAdapters1(factory, index, out var adapter) != 0 || adapter == 0)
                {
                    break;
                }

                try
                {
                    var desc = new Desc1();
                    if (GetDesc1(adapter, ref desc) != 0)
                    {
                        continue;
                    }

                    var name = desc.Description?.Trim() ?? "";
                    if ((desc.Flags & 2) != 0 || name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    adapters.Add(new Adapter(name, desc.VendorId, desc.DedicatedVideoMemory, desc.SharedSystemMemory, desc.LuidLow, desc.LuidHigh));
                }
                finally
                {
                    _ = Release(adapter);
                }
            }
        }
        finally
        {
            _ = Release(factory);
        }

        return adapters;
    }

    private static int EnumAdapters1(nint factory, uint index, out nint adapter)
    {
        var method = ReadMethod<EnumAdapters1Delegate>(factory, 11);
        return method(factory, index, out adapter);
    }

    private static int GetDesc1(nint adapter, ref Desc1 desc)
    {
        var method = ReadMethod<GetDesc1Delegate>(adapter, 10);
        return method(adapter, ref desc);
    }

    private static uint Release(nint unknown)
    {
        var method = ReadMethod<ReleaseDelegate>(unknown, 2);
        return method(unknown);
    }

    private static T ReadMethod<T>(nint comObject, int slot) where T : Delegate
    {
        var table = Marshal.ReadIntPtr(comObject);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, slot * IntPtr.Size));
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out nint factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(nint factory, uint index, out nint adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(nint adapter, ref Desc1 desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(nint unknown);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Desc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public ulong DedicatedVideoMemory;
        public ulong DedicatedSystemMemory;
        public ulong SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    private sealed record Adapter(string Name, uint VendorId, ulong DedicatedVideoMemory, ulong SharedSystemMemory, uint LuidLow, int LuidHigh);

    internal sealed record NvidiaGpu(string Name, long UsedBytes, long TotalBytes, double Utilization);

    internal readonly record struct EngineSample(string Instance, double Percent);
}
