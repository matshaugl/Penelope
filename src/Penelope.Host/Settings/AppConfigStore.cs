using System.Text.Json;

namespace Penelope.Host.Settings;

public sealed class AppConfig
{
    public bool ExposeOllamaOnLan { get; set; }

    public int OllamaPort { get; set; } = 11434;

    public bool SetupComplete { get; set; }

    public bool OpenBrowserOnLaunch { get; set; } = true;

    public string Theme { get; set; } = "dark";
}

public sealed class AppConfigStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private AppConfig _config;

    public AppConfigStore(string path)
    {
        _path = path;
        _config = Read(path);
    }

    public AppConfig Get()
    {
        lock (_gate)
        {
            return Clone(_config);
        }
    }

    public AppConfig UpdateTheme(string theme)
    {
        if (theme is not ("dark" or "light"))
        {
            throw new ArgumentException("Theme must be dark or light.", nameof(theme));
        }

        lock (_gate)
        {
            _config.Theme = theme;
            Write(_path, _config);
            return Clone(_config);
        }
    }

    public AppConfig SetSetupComplete(bool complete)
    {
        lock (_gate)
        {
            _config.SetupComplete = complete;
            Write(_path, _config);
            return Clone(_config);
        }
    }

    private static AppConfig Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new AppConfig();
        }
    }

    private static void Write(string path, AppConfig config)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, Options);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    private static AppConfig Clone(AppConfig config)
    {
        return new AppConfig
        {
            ExposeOllamaOnLan = config.ExposeOllamaOnLan,
            OllamaPort = config.OllamaPort,
            SetupComplete = config.SetupComplete,
            OpenBrowserOnLaunch = config.OpenBrowserOnLaunch,
            Theme = config.Theme,
        };
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
