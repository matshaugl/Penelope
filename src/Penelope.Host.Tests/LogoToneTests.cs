using Penelope.Abstractions;
using Penelope.Host.SystemInfo;
using Penelope.Host.Tray;
using Xunit;

namespace Penelope.Host.Tests;

public class LogoToneTests
{
    [Fact]
    public void A_running_service_is_green()
    {
        Assert.Equal(LogoTone.Running, LogoToneMap.FromModule(ToolAvailability.Ready, ToolRunState.Running, false));
    }

    [Fact]
    public void Work_in_progress_is_yellow()
    {
        Assert.Equal(LogoTone.Busy, LogoToneMap.FromModule(ToolAvailability.Ready, ToolRunState.Running, true));
        Assert.Equal(LogoTone.Busy, LogoToneMap.FromModule(ToolAvailability.Ready, ToolRunState.Starting, false));
        Assert.Equal(LogoTone.Busy, LogoToneMap.FromModule(ToolAvailability.Ready, ToolRunState.Installing, false));
        Assert.Equal(LogoTone.Busy, LogoToneMap.FromModule(ToolAvailability.Ready, ToolRunState.Stopping, false));
    }

    [Fact]
    public void Stopped_and_unavailable_services_are_red()
    {
        Assert.Equal(LogoTone.Stopped, LogoToneMap.FromModule(ToolAvailability.Ready, ToolRunState.Stopped, false));
        Assert.Equal(LogoTone.Stopped, LogoToneMap.FromModule(ToolAvailability.Ready, ToolRunState.NotInstalled, false));
        Assert.Equal(LogoTone.Stopped, LogoToneMap.FromModule(ToolAvailability.Error, ToolRunState.Running, false));
        Assert.Equal(LogoTone.Stopped, LogoToneMap.FromModule(ToolAvailability.Missing, null, false));
    }

    [Fact]
    public void A_working_tool_wins_over_a_running_one()
    {
        Assert.Equal(LogoTone.Busy, LogoToneMap.Combine([LogoTone.Running, LogoTone.Busy, LogoTone.Stopped]));
        Assert.Equal(LogoTone.Running, LogoToneMap.Combine([LogoTone.Stopped, LogoTone.Running]));
        Assert.Equal(LogoTone.Stopped, LogoToneMap.Combine([LogoTone.Stopped]));
        Assert.Equal(LogoTone.Stopped, LogoToneMap.Combine([]));
    }
}

public class GraphicsCardTests
{
    [Fact]
    public void Nvidia_smi_text_becomes_a_named_card()
    {
        var cards = GraphicsCards.ParseNvidiaSmi("NVIDIA GeForce RTX 4060 Ti, 1845, 8188, 11\n");

        var card = Assert.Single(cards);
        Assert.Equal("NVIDIA GeForce RTX 4060 Ti", card.Name);
        Assert.Equal(11, card.Utilization);
        Assert.True(card.TotalBytes > 7_000_000_000);
        Assert.True(card.UsedBytes > 0);
    }

    [Fact]
    public void Driver_pages_are_the_three_vendors_penelope_will_open()
    {
        Assert.StartsWith("https://www.nvidia.com/", GraphicsCards.DriverPage(0x10DE));
        Assert.StartsWith("https://www.amd.com/", GraphicsCards.DriverPage(0x1002));
        Assert.StartsWith("https://www.intel.com/", GraphicsCards.DriverPage(0x8086));
        Assert.Null(GraphicsCards.DriverPage(0));
    }
}
