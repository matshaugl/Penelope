using System.Drawing;
using System.Net;
using Penelope.Host.Security;
using Penelope.Host.Tray;
using Xunit;

namespace Penelope.Host.Tests;

public class BrandMarkTests : IClassFixture<PenelopeFactory>
{
    private readonly PenelopeFactory _factory;

    public BrandMarkTests(PenelopeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void The_logo_source_is_the_clean_mark()
    {
        var svg = File.ReadAllText(Path.Combine(Repo.Root, "branding", "penelope.svg"));
        Assert.DoesNotContain("<circle", svg, StringComparison.OrdinalIgnoreCase);
        var spec = BrandMark.Parse(svg);
        Assert.Equal("P", spec.Letter);
        Assert.Equal(128, spec.Tile);
        Assert.Equal(98, spec.DotX);
        Assert.Equal(BrandMark.SourceSvg().Trim(), svg.Trim());
    }

    [Fact]
    public void The_status_dot_is_drawn_only_when_requested()
    {
        var spec = BrandMark.Parse(File.ReadAllText(Path.Combine(Repo.Root, "branding", "penelope.svg")));
        using var clean = BrandMark.Draw(spec, 128, dot: null);
        using var status = BrandMark.Draw(spec, 128, Color.FromArgb(0xef, 0x44, 0x44));

        Assert.True(clean.GetPixel(104, 28).R < 40);
        Assert.True(status.GetPixel(104, 28).R > 180);
    }

    [Fact]
    public async Task The_locked_page_shows_the_clean_mark()
    {
        var page = SessionGate.LockedPage();
        Assert.Contains("<rect", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<circle", page, StringComparison.OrdinalIgnoreCase);

        var client = _factory.CreateClient();
        var locked = await client.GetStringAsync("/");
        Assert.Contains("Open Penelope from its shortcut", locked);
        Assert.Contains("<rect", locked, StringComparison.OrdinalIgnoreCase);

        var favicon = await client.GetAsync("/favicon.svg");
        var body = await favicon.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Open Penelope from its shortcut", body);
    }

    [Fact]
    public void Icons_and_shortcuts_use_the_penelope_mark()
    {
        var csproj = File.ReadAllText(Path.Combine(Repo.Root, "src", "Penelope.Host", "Penelope.Host.csproj"));
        var iss = File.ReadAllText(Path.Combine(Repo.Root, "installer", "penelope.iss"));
        var html = File.ReadAllText(Path.Combine(Repo.Root, "web", "index.html"));
        Assert.Contains("Assets\\penelope.ico", csproj);
        Assert.Contains("SetupIconFile=..\\src\\Penelope.Host\\Assets\\penelope.ico", iss);
        Assert.Contains("UninstallDisplayIcon={app}\\Penelope.Host.exe", iss);
        Assert.Contains("/favicon.svg", html);

        var icons = Directory.EnumerateFiles(Repo.Root, "*.ico", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(Repo.Root, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(
            [
                Path.Combine("src", "Penelope.Host", "Assets", "penelope.ico"),
                Path.Combine("web", "public", "favicon.ico"),
            ],
            icons);
    }
}
