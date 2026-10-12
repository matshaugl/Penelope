using Penelope.Abstractions;
using Penelope.Host.Jobs;
using Penelope.Host.Plugins;
using Penelope.Host.Tray;

namespace Penelope.Host.Api;

static class ModuleEndpoints
{
    public static void Map(WebApplication app, PluginCatalog catalog)
    {
        app.MapGet("/api/modules", (JobRunner jobs) =>
        {
            var list = catalog.Plugins.Select(plugin => Describe(plugin, jobs, includePlan: false)).ToList();
            return Results.Json(list, ApiJson.Options);
        });

        app.MapGet("/api/modules/{id}", (string id, JobRunner jobs) =>
        {
            var plugin = catalog.Find(id);
            return plugin is null
                ? Results.NotFound()
                : Results.Json(Describe(plugin, jobs, includePlan: true), ApiJson.Options);
        });

        app.MapPost("/api/modules/{id}/install", (string id, JobRunner jobs) => Begin(catalog, jobs, id, "install"));
        app.MapPost("/api/modules/{id}/upgrade", (string id, JobRunner jobs) => Begin(catalog, jobs, id, "upgrade"));
        app.MapPost("/api/modules/{id}/start", (string id) => RunImmediate(catalog, id, module => module.StartAsync(CancellationToken.None)));
        app.MapPost("/api/modules/{id}/stop", (string id) => RunImmediate(catalog, id, module => module.StopAsync(CancellationToken.None)));

        app.MapPost("/api/modules/{id}/check", async (string id, CancellationToken cancellationToken) =>
        {
            var plugin = catalog.Find(id);
            if (plugin is null)
            {
                return Results.NotFound();
            }

            if (plugin.Module is null)
            {
                return Unavailable(plugin);
            }

            await plugin.Module.CheckForUpdateAsync(cancellationToken);
            return Results.Json(new { release = plugin.Module.GetRelease() }, ApiJson.Options);
        });

        app.MapPost("/api/modules/{id}/fixes", (string id, FixBody body, JobRunner jobs) =>
        {
            var plugin = catalog.Find(id);
            if (plugin is null)
            {
                return Results.NotFound();
            }

            var job = jobs.Find(body.JobId);
            if (job is null || !string.Equals(job.ModuleId, plugin.Id, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound();
            }

            var fix = job.Failure?.Fixes.FirstOrDefault(item => item.Id == body.FixId);
            if (fix is null)
            {
                return Results.NotFound();
            }

            return fix.Kind switch
            {
                FixKind.Retry => Retry(catalog, jobs, job),
                FixKind.CheckAgain when plugin.Module is not null => CheckAndReturn(plugin),
                FixKind.OpenUrl => OpenUrl(fix.Url),
                FixKind.OpenFirewall => OpenFirewall(),
                FixKind.OpenDisk => OpenModelsFolder(),
                _ => Unavailable(plugin),
            };
        });

        var queue = new HostJobQueue(app.Services.GetRequiredService<JobRunner>());
        foreach (var plugin in catalog.Plugins)
        {
            if (plugin.Module is null)
            {
                continue;
            }

            if (plugin.Module is IJobAwareModule aware)
            {
                aware.UseJobs(queue);
            }

            var builder = new ModuleEndpointBuilder();
            plugin.Module.ContributeApi(builder);
            builder.Apply(app.MapGroup($"/api/modules/{plugin.Id}"));
        }
    }

    private static IResult Retry(PluginCatalog catalog, JobRunner jobs, JobSnapshot job)
    {
        var started = jobs.Retry(job.Id);
        if (started is null)
        {
            return Begin(catalog, jobs, job.ModuleId, job.Kind);
        }

        var created = !string.Equals(started.Id, job.Id, StringComparison.Ordinal);
        return Results.Json(
            new { jobId = started.Id },
            ApiJson.Options,
            statusCode: created ? StatusCodes.Status202Accepted : StatusCodes.Status200OK);
    }

    private static IResult Begin(PluginCatalog catalog, JobRunner jobs, string id, string kind)
    {
        var plugin = catalog.Find(id);
        if (plugin is null)
        {
            return Results.NotFound();
        }

        if (plugin.Module is null || plugin.Availability != ToolAvailability.Ready)
        {
            return Unavailable(plugin);
        }

        var existing = jobs.Running(plugin.Id, kind);
        if (existing is not null)
        {
            return Results.Json(new { jobId = existing.Id }, ApiJson.Options);
        }

        var module = plugin.Module;
        var snapshot = jobs.Start(plugin.Id, kind, (log, cancellationToken) => kind switch
        {
            "upgrade" => module.UpgradeAsync(log, cancellationToken),
            _ => module.InstallAsync(log, cancellationToken),
        });
        return Results.Json(new { jobId = snapshot.Id }, ApiJson.Options, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> RunImmediate(PluginCatalog catalog, string id, Func<IToolModule, Task<ModuleActionResult>> action)
    {
        var plugin = catalog.Find(id);
        if (plugin is null)
        {
            return Results.NotFound();
        }

        if (plugin.Module is null || plugin.Availability != ToolAvailability.Ready)
        {
            return Unavailable(plugin);
        }

        var result = await action(plugin.Module);
        if (!result.Succeeded)
        {
            return Results.Json(new { failure = result.Failure }, ApiJson.Options, statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Json(new
        {
            status = plugin.Module.GetStatus(),
            release = plugin.Module.GetRelease(),
        }, ApiJson.Options);
    }

    private static IResult CheckAndReturn(LoadedPlugin plugin)
    {
        plugin.Module!.CheckForUpdateAsync(CancellationToken.None).GetAwaiter().GetResult();
        return Results.Json(new { release = plugin.Module.GetRelease() }, ApiJson.Options);
    }

    private static IResult OpenUrl(string? url)
    {
        if (url is null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new
            {
                failure = new FailureCard(
                    "That page can't be opened.",
                    "Penelope only opens secure web pages from a fix.",
                    []),
            }, ApiJson.Options, statusCode: StatusCodes.Status409Conflict);
        }

        Browser.Open(url);
        return Results.Ok(new { opened = true });
    }

    private static IResult OpenModelsFolder()
    {
        if (!WindowsSurfaces.OpenModelsFolder())
        {
            return Results.Json(new
            {
                failure = new FailureCard(
                    "Penelope can't open that drive yet.",
                    "The models folder is not on this computer.",
                    []),
            }, ApiJson.Options, statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Ok(new { opened = true });
    }

    private static IResult OpenFirewall()
    {
        WindowsSurfaces.OpenFirewall();
        return Results.Ok(new { opened = true });
    }

    private static IResult Unavailable(LoadedPlugin plugin)
    {
        return Results.Json(new
        {
            failure = new FailureCard(
                plugin.LoadError ?? "This tool is not available yet.",
                "Penelope can't run this action until the tool is ready.",
                []),
        }, ApiJson.Options, statusCode: StatusCodes.Status409Conflict);
    }

    private static object Describe(LoadedPlugin plugin, JobRunner jobs, bool includePlan)
    {
        var module = plugin.Module;
        return new
        {
            id = plugin.Id,
            name = plugin.DisplayName,
            summary = plugin.Summary,
            availability = plugin.Availability,
            loadError = plugin.LoadError,
            hasCustomUi = plugin.HasCustomUi,
            nav = plugin.Nav,
            status = module?.GetStatus(),
            release = module?.GetRelease(),
            plan = includePlan ? module?.PlanInstall() : null,
            activeJobId = jobs.ActiveJobId(plugin.Id),
            latestJobId = jobs.LatestJobId(plugin.Id),
        };
    }
}

sealed record FixBody(string JobId, string FixId);
