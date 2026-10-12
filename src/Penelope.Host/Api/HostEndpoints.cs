using Penelope.Abstractions;
using Penelope.Host.Settings;
using Penelope.Host.Tray;

namespace Penelope.Host.Api;

static class HostEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/settings", (AppConfigStore store, SignInStartup startup) => Results.Json(Describe(store.Get(), startup), ApiJson.Options));

        app.MapPut("/api/settings", (SettingsBody body, AppConfigStore store, SignInStartup startup) =>
        {
            if (body.Theme is not null)
            {
                if (body.Theme is not ("dark" or "light"))
                {
                    return Results.BadRequest(new { message = "Theme must be dark or light." });
                }

                store.UpdateTheme(body.Theme);
            }

            if (body.SetupComplete is bool complete)
            {
                store.SetSetupComplete(complete);
            }

            if (body.StartOnSignIn is bool start && !startup.Set(start))
            {
                return Results.Json(new
                {
                    failure = new FailureCard(
                        "Penelope can't start at sign-in yet.",
                        "This copy of Penelope doesn't have a program file that Windows can open at sign-in.",
                        []),
                }, ApiJson.Options, statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Json(Describe(store.Get(), startup), ApiJson.Options);
        });

        app.MapPost("/api/host/quit", (IHostApplicationLifetime lifetime) =>
        {
            lifetime.StopApplication();
            return Results.Ok(new { closing = true });
        });

        app.MapPost("/api/host/open-firewall", () =>
        {
            WindowsSurfaces.OpenFirewall();
            return Results.Ok(new { opened = true });
        });

        app.MapPost("/api/host/open-url", (UrlBody body) =>
        {
            if (!AllowedDriverPage(body.Url))
            {
                return Results.Json(new
                {
                    failure = new FailureCard(
                        "That page can't be opened.",
                        "Penelope only opens the graphics driver pages it knows.",
                        []),
                }, ApiJson.Options, statusCode: StatusCodes.Status409Conflict);
            }

            Browser.Open(body.Url!);
            return Results.Ok(new { opened = true });
        });

        app.MapPost("/api/host/open-disk", () =>
        {
            if (!WindowsSurfaces.OpenModelsFolder())
            {
                return Results.Json(new
                {
                    failure = new FailureCard(
                        "Penelope can't open that drive yet.",
                        "The models folder is not on this computer.",
                        []),
                }, statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Ok(new { opened = true });
        });
    }

    private static object Describe(AppConfig config, SignInStartup startup)
    {
        return new
        {
            exposeOllamaOnLan = config.ExposeOllamaOnLan,
            ollamaPort = config.OllamaPort,
            setupComplete = config.SetupComplete,
            openBrowserOnLaunch = config.OpenBrowserOnLaunch,
            theme = config.Theme,
            startOnSignIn = startup.IsEnabled(),
        };
    }

    private static bool AllowedDriverPage(string? url)
    {
        return url is "https://www.nvidia.com/Download/index.aspx"
            or "https://www.amd.com/en/support"
            or "https://www.intel.com/content/www/us/en/download-center/home.html";
    }
}

sealed record SettingsBody(string? Theme, bool? SetupComplete, bool? StartOnSignIn);

sealed record UrlBody(string? Url);
