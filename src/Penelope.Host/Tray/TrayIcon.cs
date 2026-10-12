using System.Windows.Forms;
using Penelope.Host.Jobs;
using Penelope.Host.Plugins;

namespace Penelope.Host.Tray;

static class TrayIcon
{
    public static void Start(string openUrl, IHostApplicationLifetime lifetime, PluginCatalog catalog, JobRunner jobs)
    {
        var thread = new Thread(() => Run(openUrl, lifetime, catalog, jobs))
        {
            IsBackground = true,
            Name = "Penelope tray",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Run(string openUrl, IHostApplicationLifetime lifetime, PluginCatalog catalog, JobRunner jobs)
    {
        ApplicationConfiguration.Initialize();
        var published = new PublishedTone();
        var shown = LogoTone.Busy;
        using var icon = new NotifyIcon
        {
            Icon = LogoIcon.Create(shown),
            Visible = true,
            Text = LogoIcon.Caption(shown),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Penelope", null, (_, _) => Browser.Open(openUrl));
        menu.Items.Add("Quit Penelope", null, (_, _) => lifetime.StopApplication());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => Browser.Open(openUrl);

        _ = Task.Run(() =>
        {
            while (!lifetime.ApplicationStopping.WaitHandle.WaitOne(1500))
            {
                try
                {
                    published.Write(LogoToneMap.ForCatalog(catalog, jobs));
                }
                catch (Exception)
                {
                    published.Write(LogoTone.Stopped);
                }
            }
        });

        using var timer = new System.Windows.Forms.Timer { Interval = 400 };
        timer.Tick += (_, _) =>
        {
            var next = published.Read();
            if (next == shown)
            {
                return;
            }

            shown = next;
            var replacement = LogoIcon.Create(next);
            var previous = icon.Icon;
            icon.Icon = replacement;
            previous?.Dispose();
            icon.Text = LogoIcon.Caption(next);
        };
        timer.Start();

        using var registration = lifetime.ApplicationStopping.Register(() =>
        {
            icon.Visible = false;
            Application.ExitThread();
        });
        Application.Run();
        icon.Visible = false;
        icon.Icon?.Dispose();
    }

    private sealed class PublishedTone
    {
        private int _value = (int)LogoTone.Busy;

        public LogoTone Read() => (LogoTone)Volatile.Read(ref _value);

        public void Write(LogoTone tone) => Volatile.Write(ref _value, (int)tone);
    }
}
