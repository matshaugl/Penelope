using Penelope.Host.SystemInfo;
using Xunit;

namespace Penelope.Host.Tests;

public class MachineReadingsTests
{
    [Fact]
    public void Cpu_and_memory_come_from_this_computer()
    {
        var snapshot = MachineReadings.Read();

        Assert.True(snapshot.Memory.Available);
        Assert.True(snapshot.Memory.CapacityBytes > 1_000_000_000);
        Assert.InRange(snapshot.Memory.Percent!.Value, 0, 100);
        Assert.True(snapshot.Cpu.Available);
        Assert.InRange(snapshot.Cpu.Percent!.Value, 0, 100);
        Assert.NotEmpty(snapshot.Gpus);
        Assert.DoesNotContain("not reading the graphics card yet", snapshot.Gpus[0].Reason ?? "");
        Assert.True(snapshot.Disk.Available);
        Assert.True(snapshot.Disk.CapacityBytes > 1_000_000_000);
    }
}
