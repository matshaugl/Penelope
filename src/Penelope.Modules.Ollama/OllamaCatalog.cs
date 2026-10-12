using System.Text.Json;
using Penelope.Abstractions;

namespace Penelope.Modules.Ollama;

public sealed record CatalogEntry(
    string Name,
    string Tag,
    long SizeBytes,
    string Summary,
    string Role,
    int MinRamGb,
    int MinVramGb);

public static class OllamaCatalog
{
    public static string ModelsDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
    }

    public static long? FreeBytes()
    {
        try
        {
            var directory = ModelsDirectory();
            var probe = Directory.Exists(directory) ? directory : Path.GetDirectoryName(directory) ?? directory;
            var root = Path.GetPathRoot(Path.GetFullPath(probe));
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<CatalogEntry> Load()
    {
        var path = Path.Combine(Path.GetDirectoryName(typeof(OllamaCatalog).Assembly.Location) ?? "", "catalog.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "catalog.json");
        }

        if (!File.Exists(path))
        {
            return [];
        }

        var entries = JsonSerializer.Deserialize<List<CatalogEntry>>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        return entries ?? [];
    }

    public static ModuleActionResult? RefuseIfTooLarge(string name, IReadOnlyList<CatalogEntry> entries)
    {
        var known = entries.FirstOrDefault(entry => string.Equals(entry.Tag, name, StringComparison.OrdinalIgnoreCase));
        var free = FreeBytes();
        if (known is null || free is null || free.Value >= known.SizeBytes)
        {
            return null;
        }

        return new ModuleActionResult(false, new FailureCard(
            "There isn't room for this model.",
            "The models disk does not have room for this download.",
            [new FailureFix("disk", "Open the drive", FixKind.OpenDisk, null)]));
    }
}
