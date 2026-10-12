namespace Penelope.Host.Plugins;

static class PluginRoots
{
    public static string Resolve(
        string? configured,
        string? processPath,
        string baseDirectory,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var exeDirectory = string.IsNullOrWhiteSpace(processPath) ? null : Path.GetDirectoryName(processPath);
        if (exeDirectory is not null && directoryExists(Path.Combine(exeDirectory, "plugins")))
        {
            return Path.Combine(exeDirectory, "plugins");
        }

        var besideBase = Path.Combine(baseDirectory, "plugins");
        if (directoryExists(besideBase))
        {
            return besideBase;
        }

        var dir = new DirectoryInfo(baseDirectory);
        while (dir is not null)
        {
            if (fileExists(Path.Combine(dir.FullName, "Penelope.sln")))
            {
                return Path.Combine(dir.FullName, "plugins");
            }

            dir = dir.Parent;
        }

        return besideBase;
    }
}
