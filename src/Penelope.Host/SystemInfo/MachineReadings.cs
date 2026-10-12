using System.Runtime.InteropServices;
using Penelope.Host.Tray;

namespace Penelope.Host.SystemInfo;

static class MachineReadings
{
    private static readonly object Gate = new();
    private static long _idle;
    private static long _kernel;
    private static long _user;
    private static bool _hasSample;

    public static SystemSnapshot Read()
    {
        lock (Gate)
        {
            return new SystemSnapshot(
                ReadCpu(),
                ReadMemory(),
                GraphicsCards.Read(),
                ReadDisk());
        }
    }

    private static Reading ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return new Reading(false, "Penelope could not read memory.", null, null);
        }

        var freeGb = status.AvailPhys / 1_073_741_824d;
        var totalGb = status.TotalPhys / 1_073_741_824d;
        return new Reading(
            true,
            null,
            status.MemoryLoad,
            $"{freeGb:0.0} GB free of {totalGb:0.0} GB",
            (long)status.TotalPhys);
    }

    private static Reading ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return new Reading(false, "Penelope could not read the processor.", null, null);
        }

        var idleNow = idle.ToTicks();
        var kernelNow = kernel.ToTicks();
        var userNow = user.ToTicks();
        if (!_hasSample)
        {
            _idle = idleNow;
            _kernel = kernelNow;
            _user = userNow;
            _hasSample = true;
            Thread.Sleep(200);
            return ReadCpu();
        }

        var idleDelta = idleNow - _idle;
        var total = (kernelNow - _kernel) + (userNow - _user);
        _idle = idleNow;
        _kernel = kernelNow;
        _user = userNow;
        if (total <= 0)
        {
            return new Reading(false, "Penelope is taking the first processor sample.", null, null);
        }

        var busy = Math.Clamp((total - idleDelta) * 100d / total, 0, 100);
        return new Reading(true, null, busy, $"{Environment.ProcessorCount} processors");
    }

    private static Reading ReadDisk()
    {
        var folder = WindowsSurfaces.ModelsFolder();
        var root = folder is null ? null : Path.GetPathRoot(Path.GetFullPath(folder));
        if (string.IsNullOrWhiteSpace(root))
        {
            return new Reading(false, "Penelope could not find the models drive.", null, null);
        }

        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return new Reading(false, "The models drive is not ready.", null, null);
            }

            var freeGb = drive.AvailableFreeSpace / 1_073_741_824d;
            var totalGb = drive.TotalSize / 1_073_741_824d;
            var used = Math.Clamp((drive.TotalSize - drive.AvailableFreeSpace) * 100d / drive.TotalSize, 0, 100);
            var name = drive.Name.TrimEnd('\\');
            return new Reading(true, null, used, $"{freeGb:0.0} GB free of {totalGb:0.0} GB on {name}", drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Reading(false, "Penelope could not read the models drive.", null, null);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public long ToTicks() => (long)High << 32 | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
