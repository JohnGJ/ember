using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Ember;

sealed class UsagePopup : Form
{
    const int WS_EX_TOPMOST = 0x00000008;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWCP_ROUNDSMALL = 3;

    const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top;

    // Layout in 96-dpi pixels; see Scale().
    const int PopupWidth = 290;
    const int Pad = 14;
    const int BarGap = 6;
    const int BarThickness = 4;
    const int RowGap = 12;

    readonly Font body = new("Segoe UI", 9.75f);
    readonly Font strong = new("Segoe UI Semibold", 9.75f);
    readonly Font small = new("Segoe UI", 8.5f);
    readonly System.Windows.Forms.Timer linger = new() { Interval = 250 };

    Theme theme = Theme.Dark;
    Usage? usage;
    string? error;
    Rectangle anchor;

    public UsagePopup()
    {
        Text = "Ember usage";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;

        linger.Tick += (_, _) =>
        {
            if (Bounds.Contains(Cursor.Position)) return;
            linger.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var preference = DWMWCP_ROUNDSMALL;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    public void SetUsage(Usage? usage, string? error)
    {
        this.usage = usage;
        this.error = error;
        if (Visible) Arrange();
    }

    public void ShowAt(Rectangle anchor)
    {
        this.anchor = anchor;
        linger.Stop();
        theme = Theme.Current;
        BackColor = theme.Background;
        Arrange();
        Show();
    }

    // Called when the cursor leaves the tray icon. Keep the popup while the
    // cursor is over it so the text can be read without it vanishing.
    public void Release() => linger.Start();

    public void Tick()
    {
        if (Visible) Invalidate();
    }

    void Arrange()
    {
        Size = Measure();
        Location = Place(anchor, Size);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var x = Scale(Pad);
        var y = Scale(Pad);
        var w = Width - Scale(Pad) * 2;

        if (usage is not null)
        {
            foreach (var limit in usage.Limits)
            {
                var percent = $"{limit.Percent:0.0}%";
                var percentWidth = TextRenderer.MeasureText(g, percent, strong, Size.Empty, Flags).Width;
                TextRenderer.DrawText(g, limit.Name, body, new Point(x, y), theme.Text, Flags);
                TextRenderer.DrawText(g, percent, strong, new Point(x + w - percentWidth, y), theme.Text, Flags);
                y += body.Height + Scale(BarGap);

                var bar = new Rectangle(x, y, w, Scale(BarThickness));
                FillRounded(g, bar, theme.Track);
                var filled = (int)Math.Round(w * Math.Clamp(limit.Percent, 0, 100) / 100);
                if (filled > 0) FillRounded(g, bar with { Width = Math.Max(filled, bar.Height) }, Palette.Orange);
                y += bar.Height + Scale(BarGap);

                TextRenderer.DrawText(g, ResetLine(limit.ResetsAt), small, new Point(x, y), theme.Muted, Flags);
                y += small.Height + Scale(RowGap);
            }
        }

        var footerColour = error is null ? theme.Muted : Palette.Orange;
        TextRenderer.DrawText(g, FooterText(), small, new Rectangle(x, y, w, Height - y), footerColour, Flags | TextFormatFlags.WordBreak);
    }

    int Scale(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    int RowHeight => body.Height + Scale(BarGap) + Scale(BarThickness) + Scale(BarGap) + small.Height + Scale(RowGap);

    Size Measure()
    {
        var width = Scale(PopupWidth);
        var inner = width - Scale(Pad) * 2;
        var rows = usage?.Limits.Count() ?? 0;
        var footer = TextRenderer.MeasureText(FooterText(), small, new Size(inner, 0), Flags | TextFormatFlags.WordBreak).Height;
        return new Size(width, Scale(Pad) + rows * RowHeight + footer + Scale(Pad));
    }

    string FooterText()
    {
        var updated = usage is null ? null : $"Updated {usage.FetchedAt.LocalDateTime:t}";
        return (error, updated) switch
        {
            (null, null) => "Fetching usage",
            (null, _) => updated,
            (_, null) => error,
            _ => $"{error}\nLast {updated.ToLowerInvariant()}",
        };
    }

    static string ResetLine(DateTimeOffset resetsAt)
    {
        var local = resetsAt.LocalDateTime;
        var today = DateTime.Today;
        var day = local.Date == today ? "today"
            : local.Date == today.AddDays(1) ? "tomorrow"
            : local.ToString("ddd d MMM");
        return $"Resets {day} {local:t} · {Countdown(resetsAt - DateTimeOffset.Now)}";
    }

    static string Countdown(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "any moment";
        if (left.TotalDays >= 1) return $"in {left.Days}d {left.Hours}h";
        if (left.TotalHours >= 1) return $"in {left.Hours}h {left.Minutes}m";
        return $"in {left.Minutes}m {left.Seconds}s";
    }

    static Point Place(Rectangle anchor, Size size)
    {
        if (anchor.IsEmpty) anchor = new Rectangle(Cursor.Position, new Size(1, 1));
        var screen = Screen.FromRectangle(anchor);
        var area = screen.WorkingArea;
        const int gap = 8;

        int x, y;
        if (anchor.Right <= area.Left || anchor.Left >= area.Right)
        {
            // Vertical taskbar: sit beside the icon.
            x = anchor.Right <= area.Left ? anchor.Right + gap : anchor.Left - size.Width - gap;
            y = anchor.Top + anchor.Height / 2 - size.Height / 2;
        }
        else
        {
            // Bottom taskbar, or the overflow flyout just above it: sit above the icon.
            x = anchor.Left + anchor.Width / 2 - size.Width / 2;
            var lowerHalf = anchor.Top + anchor.Height / 2 > screen.Bounds.Top + screen.Bounds.Height / 2;
            y = lowerHalf ? anchor.Top - size.Height - gap : anchor.Bottom + gap;
        }

        x = Math.Clamp(x, area.Left + gap, Math.Max(area.Left + gap, area.Right - size.Width - gap));
        y = Math.Clamp(y, area.Top + gap, Math.Max(area.Top + gap, area.Bottom - size.Height - gap));
        return new Point(x, y);
    }

    static void FillRounded(Graphics g, Rectangle r, Color colour)
    {
        using var brush = new SolidBrush(colour);
        using var path = new GraphicsPath();
        var d = r.Height;
        path.AddArc(r.Left, r.Top, d, d, 90, 180);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 180);
        path.CloseFigure();
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(brush, path);
        g.SmoothingMode = previous;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            linger.Dispose();
            body.Dispose();
            strong.Dispose();
            small.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
