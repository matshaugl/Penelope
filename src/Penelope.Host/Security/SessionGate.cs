using Penelope.Host.Tray;

namespace Penelope.Host.Security;

public static class SessionGate
{
    public const string CookieName = "penelope_session";

    public static void UsePenelopeSession(this WebApplication app, SessionToken token, bool testing)
    {
        app.Use(async (context, next) =>
        {
            if (!testing && !IsLoopbackHost(context))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (context.Request.Path.StartsWithSegments("/session"))
            {
                await next();
                return;
            }

            var authed = IsAuthed(context, token);
            var isApi = context.Request.Path.StartsWithSegments("/api");
            if (isApi && !authed)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (isApi && IsMutating(context.Request.Method) && !OriginMatches(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!isApi && !authed)
            {
                if (IsBrandAsset(context.Request.Path))
                {
                    await next();
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(LockedPage());
                return;
            }

            await next();
        });
    }

    public static void WriteCookie(HttpContext context, SessionToken token)
    {
        context.Response.Cookies.Append(CookieName, token.Value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = false,
            IsEssential = true,
            Path = "/",
        });
    }

    private static bool IsAuthed(HttpContext context, SessionToken token)
    {
        return context.Request.Cookies.TryGetValue(CookieName, out var cookie) && token.Matches(cookie);
    }

    private static bool IsLoopbackHost(HttpContext context)
    {
        return string.Equals(context.Request.Host.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMutating(string method)
    {
        return HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);
    }

    private static bool OriginMatches(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var origin))
        {
            return false;
        }

        var expected = $"{context.Request.Scheme}://{context.Request.Host.Value}";
        return string.Equals(origin.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBrandAsset(PathString path)
    {
        return path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/favicon.svg", StringComparison.OrdinalIgnoreCase);
    }

    internal static string LockedPage()
    {
        var svg = BrandMark.SourceSvg().Trim();
        var icon = Uri.EscapeDataString(svg);
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <title>Penelope</title>
              <link rel="icon" href="data:image/svg+xml,{{icon}}" type="image/svg+xml">
              <style>
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #282c34; color: #9cdef2; font-family: "Segoe UI", sans-serif; }
                main { max-width: 28rem; padding: 24px; }
                .mark { width: 64px; height: 64px; margin-bottom: 16px; }
                .mark svg { width: 64px; height: 64px; display: block; }
                h1 { font-size: 1.4rem; font-weight: 600; }
              </style>
            </head>
            <body>
              <main>
                <div class="mark">{{svg}}</div>
                <h1>Penelope</h1>
                <p>Open Penelope from its shortcut. This page stays on this computer.</p>
              </main>
            </body>
            </html>
            """;
    }
}
