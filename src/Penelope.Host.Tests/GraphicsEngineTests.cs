using Penelope.Host.SystemInfo;
using Xunit;

namespace Penelope.Host.Tests;

public class GraphicsEngineTests
{
    private const string Amd =
        "pid_100_luid_0x00000000_0x0001ABCD_phys_0_eng_0_engtype_3D";

    [Fact]
    public void Three_d_use_from_several_processes_is_added_and_capped()
    {
        var percent = GraphicsCards.EngineUtilization(
        [
            new GraphicsCards.EngineSample(Amd, 40),
            new GraphicsCards.EngineSample("pid_200_luid_0x00000000_0x0001ABCD_phys_0_eng_1_engtype_3D", 70),
            new GraphicsCards.EngineSample("pid_100_luid_0x00000000_0x00009999_phys_0_eng_0_engtype_3D", 90),
        ],
        luidLow: 0x0001ABCD,
        luidHigh: 0);

        Assert.Equal(100, percent);
    }

    [Fact]
    public void Compute_is_used_when_it_is_busier_than_3d()
    {
        var percent = GraphicsCards.EngineUtilization(
        [
            new GraphicsCards.EngineSample(Amd, 5),
            new GraphicsCards.EngineSample("pid_100_luid_0x00000000_0x0001ABCD_phys_0_eng_2_engtype_Compute", 62),
            new GraphicsCards.EngineSample("pid_100_luid_0x00000000_0x0001ABCD_phys_0_eng_3_engtype_Copy", 99),
        ],
        luidLow: 0x0001ABCD,
        luidHigh: 0);

        Assert.Equal(62, percent);
    }

    [Fact]
    public void A_quiet_card_is_zero_and_an_unknown_card_is_missing()
    {
        var quiet = GraphicsCards.EngineUtilization(
            [new GraphicsCards.EngineSample(Amd, 0)],
            luidLow: 0x0001ABCD,
            luidHigh: 0);
        var missing = GraphicsCards.EngineUtilization([], luidLow: 1, luidHigh: 0);

        Assert.Equal(0, quiet);
        Assert.Null(missing);
    }

    [Fact]
    public void The_engine_instance_name_exposes_the_adapter()
    {
        Assert.True(GraphicsCards.TryParseEngine(Amd, out var low, out var high, out var kind));
        Assert.Equal(0x0001ABCDu, low);
        Assert.Equal(0u, high);
        Assert.Equal("3D", kind);
        Assert.False(GraphicsCards.TryParseEngine("not-an-engine", out _, out _, out _));
    }
}
