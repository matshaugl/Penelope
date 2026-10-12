using Penelope.Host.Plugins;
using Xunit;

namespace Penelope.Host.Tests;

public class PluginRootTests
{
    [Fact]
    public void A_configured_folder_wins()
    {
        var path = PluginRoots.Resolve(
            @"C:\configured\plugins",
            @"C:\install\Penelope.Host.exe",
            @"C:\extract",
            _ => true,
            _ => true);

        Assert.Equal(@"C:\configured\plugins", path);
    }

    [Fact]
    public void The_folder_beside_the_exe_wins_over_the_extract_folder()
    {
        var path = PluginRoots.Resolve(
            null,
            @"C:\Users\person\AppData\Local\Programs\Penelope\Penelope.Host.exe",
            @"C:\Users\person\AppData\Local\Temp\extract",
            directory => directory.StartsWith(@"C:\Users\person\AppData\Local\Programs\Penelope", StringComparison.Ordinal),
            _ => false);

        Assert.Equal(@"C:\Users\person\AppData\Local\Programs\Penelope\plugins", path);
    }

    [Fact]
    public void A_development_tree_is_used_when_the_exe_has_no_plugins()
    {
        var path = PluginRoots.Resolve(
            null,
            @"C:\repo\src\Penelope.Host\bin\Penelope.Host.exe",
            @"C:\repo\src\Penelope.Host\bin",
            _ => false,
            file => file == @"C:\repo\Penelope.sln");

        Assert.Equal(@"C:\repo\plugins", path);
    }
}
