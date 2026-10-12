using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Penelope.Abstractions;
using Penelope.Host.Agent;
using Penelope.Host.Api;
using Penelope.Host.Plugins;
using Penelope.Host.Security;
using Penelope.Host.Settings;
using Penelope.Host.Tray;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
var testing = builder.Environment.IsEnvironment("Testing")
    || builder.Configuration.GetValue("Penelope:Testing", false);
var noBrowser = builder.Configuration.GetValue("Penelope:NoBrowser", false);
var noTray = builder.Configuration.GetValue("Penelope:NoTray", false);

Mutex? instanceMutex = null;
EventWaitHandle? openEvent = null;
if (!testing)
{
    instanceMutex = new Mutex(true, @"Local\Penelope.Host", out var createdNew);
    if (!createdNew)
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(@"Local\Penelope.Host.Open");
            existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }

        return;
    }

    openEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Penelope.Host.Open");
}

string? dashboardUrl = null;
if (!testing)
{
    var port = PickPort(47321, 20);
    dashboardUrl = $"http://127.0.0.1:{port}";
    builder.WebHost.UseUrls(dashboardUrl);
    WriteInstance(port, dashboardUrl);
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
});

var pluginsPath = ResolvePlugins(builder.Configuration);
var configPath = builder.Configuration["Penelope:ConfigPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Penelope", "config.json");

var catalog = PluginCatalog.Load(pluginsPath, LoggerFactory.Create(logging => logging.AddSimpleConsole()).CreateLogger("plugins"));
builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton<IWebSearch, DuckDuckGoSearch>();
builder.Services.AddSingleton<IAgentToolSource, WebSearchToolSource>();
builder.Services.AddSingleton<AgentToolRegistry>();
builder.Services.AddSingleton<Penelope.Host.Jobs.JobRunner>();
builder.Services.AddSingleton(new AppConfigStore(configPath));
builder.Services.AddSingleton(new SignInStartup(memoryOnly: testing));
builder.Services.AddSingleton<SessionToken>();

var app = builder.Build();
var token = app.Services.GetRequiredService<SessionToken>();
app.UsePenelopeSession(token, testing);

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
});

app.MapGet("/session", (HttpContext context, SessionToken session) =>
{
    var provided = context.Request.Query["token"].ToString();
    if (!session.Matches(provided))
    {
        return Results.NotFound();
    }

    SessionGate.WriteCookie(context, session);
    return Results.Redirect("/");
});

HealthEndpoints.Map(app);
ChatEndpoints.Map(app);
SystemEndpoints.Map(app);
JobEndpoints.Map(app);
HostEndpoints.Map(app);
ModuleEndpoints.Map(app, catalog);

app.MapGet("/plugins/{id}/ui/{**file}", (string id, string file, PluginCatalog plugins) =>
{
    var plugin = plugins.Find(id);
    if (plugin?.UiRoot is null)
    {
        return Results.NotFound();
    }

    var root = Path.GetFullPath(plugin.UiRoot);
    var target = Path.GetFullPath(Path.Combine(root, file));
    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest();
    }

    if (!File.Exists(target))
    {
        return Results.NotFound();
    }

    var contentType = Path.GetExtension(target).ToLowerInvariant() switch
    {
        ".js" => "text/javascript",
        ".css" => "text/css",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };
    return Results.File(target, contentType);
});

app.MapFallbackToFile("index.html");

var sessionUrl = dashboardUrl is null ? null : $"{dashboardUrl}/session?token={token.Value}";
if (!testing && sessionUrl is not null)
{
    var lifetime = app.Lifetime;
    if (openEvent is not null)
    {
        var signal = openEvent;
        var urlToOpen = sessionUrl;
        _ = Task.Run(() =>
        {
            while (!lifetime.ApplicationStopping.WaitHandle.WaitOne(500))
            {
                if (signal.WaitOne(0))
                {
                    Browser.Open(urlToOpen);
                }
            }
        });
    }

    if (!noTray && OperatingSystem.IsWindows())
    {
        TrayIcon.Start(sessionUrl, lifetime, catalog, app.Services.GetRequiredService<Penelope.Host.Jobs.JobRunner>());
    }

    var config = app.Services.GetRequiredService<AppConfigStore>().Get();
    if (!noBrowser && config.OpenBrowserOnLaunch)
    {
        lifetime.ApplicationStarted.Register(() => Browser.Open(sessionUrl));
    }

    app.Logger.LogInformation("Penelope is listening on {Url}", dashboardUrl);
    Console.WriteLine($"Penelope dashboard: {sessionUrl}");
}

        app.Run();
        instanceMutex?.Dispose();
        openEvent?.Dispose();
    }

    static int PickPort(int start, int count)
{
    for (var port = start; port < start + count; port++)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return port;
        }
        catch (SocketException)
        {
        }
    }

    throw new IOException("Penelope couldn't find an open port.");
}

static void WriteInstance(int port, string address)
{
    var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Penelope");
    Directory.CreateDirectory(directory);
    var json = JsonSerializer.Serialize(new { port, address });
    File.WriteAllText(Path.Combine(directory, "instance.json"), json);
}

static string ResolvePlugins(IConfiguration configuration)
{
    return PluginRoots.Resolve(
        configuration["Penelope:Plugins"],
        Environment.ProcessPath,
        AppContext.BaseDirectory,
        Directory.Exists,
        File.Exists);
}
}

