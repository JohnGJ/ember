using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ember;

sealed class TrayApp : ApplicationContext
{
    static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(1);
    static readonly TimeSpan RetryAtMost = TimeSpan.FromMinutes(10);

    readonly TrayIcon tray = new();
    readonly UsagePopup popup = new();
    readonly UsageClient client = new();
    readonly ContextMenuStrip menu = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };

    Usage? usage;
    string? error;
    DateTimeOffset lastFetch = DateTimeOffset.MinValue;
    int failures;
    bool fetching;

    public TrayApp()
    {
        var startup = new ToolStripMenuItem("Start with Windows") { Checked = Startup.Enabled };
        startup.Click += (_, _) =>
        {
            try
            {
                Startup.Enabled = !startup.Checked;
                startup.Checked = !startup.Checked;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        };

        menu.Items.Add("Refresh", null, (_, _) => _ = RefreshAsync());
        menu.Items.Add("Open usage page", null, (_, _) => OpenUsagePage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());

        tray.PopupOpen += () => popup.ShowAt(tray.Bounds);
        tray.PopupClose += popup.Release;
        tray.ContextMenu += ShowMenu;
        tray.Select += () => _ = RefreshAsync();

        timer.Tick += (_, _) => Tick();
        timer.Start();

        Render();
    }

    void Tick()
    {
        popup.Tick();

        var now = DateTimeOffset.Now;
        var interval = failures == 0 ? RefreshEvery : TimeSpan.FromTicks(Math.Min(RefreshEvery.Ticks << failures, RetryAtMost.Ticks));
        var due = now - lastFetch >= interval;
        var reset = usage?.Limits.Any(l => l.ResetsAt > lastFetch && l.ResetsAt <= now) ?? false;
        if (due || reset) _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        if (fetching) return;
        fetching = true;
        lastFetch = DateTimeOffset.Now;
        try
        {
            usage = await client.FetchAsync();
            error = null;
            failures = 0;
        }
        catch (UsageException e)
        {
            Failed(e.Message);
        }
        catch (HttpRequestException)
        {
            Failed("Couldn't reach the usage API.");
        }
        catch (TaskCanceledException)
        {
            Failed("The usage API didn't answer in time.");
        }
        catch (Exception)
        {
            Failed("Something went wrong while fetching usage.");
        }
        finally
        {
            fetching = false;
        }
        Render();
    }

    void Failed(string message)
    {
        error = message;
        failures = Math.Min(failures + 1, 4);
    }

    void Render()
    {
        using var image = GaugeIcon.Render(TrayIcon.IconSize, Fraction(usage?.Week), Fraction(usage?.Session));
        tray.Update(image, Summary());
        popup.SetUsage(usage, error);
    }

    static double Fraction(Limit? limit) => (limit?.Percent ?? 0) / 100;

    string Summary()
    {
        if (usage is null) return error is null ? "Ember" : $"Ember: {error}";
        var parts = usage.Limits.Select(l => $"{l.Name} {l.Percent:0}%");
        return "Ember: " + string.Join(", ", parts);
    }

    void ShowMenu(Point at)
    {
        popup.Hide();
        // Without a foreground window the menu won't close when the user clicks away.
        SetForegroundWindow(tray.Handle);
        menu.Show(at, ToolStripDropDownDirection.AboveLeft);
    }

    static void OpenUsagePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://claude.ai/settings/usage") { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            menu.Dispose();
            popup.Dispose();
            tray.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
}
