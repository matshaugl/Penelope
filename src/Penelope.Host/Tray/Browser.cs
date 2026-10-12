namespace Penelope.Host.Tray;

static class Browser
{
    public static void Open(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
        {
            UseShellExecute = true,
        });
    }
}

static class WindowsSurfaces
{
    public static string? ModelsFolder()
    {
        var configured = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
        var folder = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models")
            : configured;
        if (Directory.Exists(folder))
        {
            return folder;
        }

        var parent = Path.GetDirectoryName(folder);
        return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) ? parent : null;
    }

    public static bool OpenModelsFolder()
    {
        var folder = ModelsFolder();
        if (folder is null)
        {
            return false;
        }

        Browser.Open(folder);
        return true;
    }

    public static void OpenFirewall()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "control.exe",
            Arguments = "/name Microsoft.WindowsFirewall",
            UseShellExecute = true,
        });
    }
}
