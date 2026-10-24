using System.Reflection;
using System.Text.Json;
using Penelope.Abstractions;

namespace Penelope.Host.Plugins;

public sealed class PluginCatalog
{
    private readonly List<LoadedPlugin> _plugins;

    internal PluginCatalog(IEnumerable<LoadedPlugin> plugins)
    {
        _plugins = plugins.ToList();
    }

    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    public LoadedPlugin? Find(string id)
    {
        return _plugins.FirstOrDefault(plugin => string.Equals(plugin.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static PluginCatalog Load(string directory, ILogger logger)
    {
        var plugins = new List<LoadedPlugin>();
        if (!Directory.Exists(directory))
        {
            logger.LogInformation("No plugins directory at {Directory}", directory);
            return new PluginCatalog(plugins);
        }

        foreach (var folder in Directory.GetDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                plugins.Add(LoadFolder(folder, manifestPath, logger));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plugin folder {Folder} could not be loaded", folder);
                plugins.Add(LoadedPlugin.Broken(Path.GetFileName(folder), "This tool couldn't be loaded.", folder));
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            if (!seen.Add(plugin.Id))
            {
                logger.LogWarning("Duplicate plugin id {Id}", plugin.Id);
            }
        }

        return new PluginCatalog(plugins);
    }

    private static LoadedPlugin LoadFolder(string folder, string manifestPath, ILogger logger)
    {
        PluginManifest? manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions.Manifest);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Plugin manifest {Path} is not valid JSON", manifestPath);
            return LoadedPlugin.Broken(Path.GetFileName(folder), "This tool couldn't be loaded.", folder);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
        {
            return LoadedPlugin.Broken(Path.GetFileName(folder), "This tool couldn't be loaded.", folder);
        }

        if (!TryReadHostApi(manifest.HostApi, out var hostApi))
        {
            logger.LogWarning("Plugin manifest {Path} has an unreadable hostApi", manifestPath);
            return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool couldn't be loaded.", folder, manifest.Summary);
        }

        switch (hostApi.CheckAgainst(HostApi.Current))
        {
            case HostApiCompatibility.NeedsNewerHost:
                return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool needs a newer Penelope.", folder, manifest.Summary);
            case HostApiCompatibility.BuiltForOlderHost:
                return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool was built for an older Penelope.", folder, manifest.Summary);
        }

        if (string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool couldn't be loaded.", folder, manifest.Summary);
        }

        var assemblyPath = Path.GetFullPath(Path.Combine(folder, manifest.Assembly));
        var root = Path.GetFullPath(folder);
        if (!assemblyPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(assemblyPath))
        {
            logger.LogWarning("Plugin assembly {Path} is missing", assemblyPath);
            return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool couldn't be loaded.", folder, manifest.Summary);
        }

        try
        {
            // Share Penelope.Abstractions with the host so IToolModule is one type.
            var context = new PluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var moduleType = assembly.GetTypes().FirstOrDefault(type =>
                !type.IsAbstract && typeof(IToolModule).IsAssignableFrom(type));
            if (moduleType is null)
            {
                return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool couldn't be loaded.", folder, manifest.Summary);
            }

            if (Activator.CreateInstance(moduleType) is not IToolModule module)
            {
                return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool couldn't be loaded.", folder, manifest.Summary);
            }

            if (!string.Equals(module.Id, manifest.Id, StringComparison.Ordinal))
            {
                logger.LogWarning("Plugin id {ManifestId} does not match module id {ModuleId}", manifest.Id, module.Id);
                return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool couldn't be loaded.", folder, manifest.Summary);
            }

            string? uiRoot = null;
            if (!string.IsNullOrWhiteSpace(manifest.Ui))
            {
                var candidate = Path.GetFullPath(Path.Combine(folder, manifest.Ui));
                if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                {
                    uiRoot = Path.GetDirectoryName(candidate);
                }
            }

            var nav = module.Nav.Count > 0 ? module.Nav : manifest.Nav ?? [];
            return new LoadedPlugin
            {
                Id = module.Id,
                DisplayName = module.DisplayName,
                Summary = string.IsNullOrWhiteSpace(module.Summary) ? manifest.Summary ?? "" : module.Summary,
                Availability = module.Availability,
                Module = module,
                Directory = folder,
                UiRoot = uiRoot,
                HasCustomUi = uiRoot is not null,
                Nav = nav,
            };
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger.LogError(ex, "Plugin {Id} types failed to load", manifest.Id);
            return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool couldn't be loaded.", folder, manifest.Summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin {Id} failed to load", manifest.Id);
            return LoadedPlugin.Broken(manifest.Id, manifest.Name, "This tool couldn't be loaded.", folder, manifest.Summary);
        }
    }

    // A bare integer is the pre-1.0 manifest form and means MAJOR.0. A missing value is treated as 0.0.
    internal static bool TryReadHostApi(JsonElement value, out HostApiVersion version)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                version = new HostApiVersion(0, 0);
                return true;
            case JsonValueKind.Number when value.TryGetInt32(out var major) && major >= 0:
                version = new HostApiVersion(major, 0);
                return true;
            case JsonValueKind.String:
                return HostApiVersion.TryParse(value.GetString(), out version);
            default:
                version = default;
                return false;
        }
    }
}

public sealed class LoadedPlugin
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Summary { get; init; }

    public ToolAvailability Availability { get; init; }

    public IToolModule? Module { get; init; }

    public required string Directory { get; init; }

    public string? UiRoot { get; init; }

    public bool HasCustomUi { get; init; }

    public IReadOnlyList<ModuleNavItem> Nav { get; init; } = [];

    public string? LoadError { get; init; }

    public static LoadedPlugin Broken(string id, string message, string directory)
    {
        return Broken(id, id, message, directory, null);
    }

    public static LoadedPlugin Broken(string id, string name, string message, string directory, string? summary)
    {
        return new LoadedPlugin
        {
            Id = id,
            DisplayName = name,
            Summary = string.IsNullOrWhiteSpace(summary) ? message : summary,
            Availability = ToolAvailability.Error,
            Directory = directory,
            LoadError = message,
            Nav = [],
        };
    }
}

sealed class PluginManifest
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Assembly { get; set; }

    public string? Ui { get; set; }

    public JsonElement HostApi { get; set; }

    public string? Summary { get; set; }

    public List<ModuleNavItem>? Nav { get; set; }
}

static class JsonOptions
{
    public static readonly JsonSerializerOptions Manifest = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
