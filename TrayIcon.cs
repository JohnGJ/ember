using System.Runtime.InteropServices;

namespace Ember;

// Thin wrapper over Shell_NotifyIcon using NOTIFYICON_VERSION_4, which gives us
// NIN_POPUPOPEN/NIN_POPUPCLOSE so we can draw our own hover window instead of
// the 128-character tooltip.
sealed class TrayIcon : NativeWindow, IDisposable
{
    const uint IconId = 1;
    const int CallbackMessage = 0x8001; // WM_APP + 1

    const int WM_CONTEXTMENU = 0x007B;
    const int NIN_SELECT = 0x0400;
    const int NIN_KEYSELECT = 0x0401;
    const int NIN_POPUPOPEN = 0x0406;
    const int NIN_POPUPCLOSE = 0x0407;

    const uint NIM_ADD = 0;
    const uint NIM_MODIFY = 1;
    const uint NIM_DELETE = 2;
    const uint NIM_SETVERSION = 4;

    const uint NIF_MESSAGE = 0x01;
    const uint NIF_ICON = 0x02;
    const uint NIF_TIP = 0x04;
    const uint NOTIFYICON_VERSION_4 = 4;

    static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    public event Action? PopupOpen;
    public event Action? PopupClose;
    public event Action<Point>? ContextMenu;
    public event Action? Select;

    Bitmap? image;
    string tip = "";
    bool added;

    public TrayIcon()
    {
        CreateHandle(new CreateParams { Caption = "Ember" });
    }

    public static int IconSize => SystemInformation.SmallIconSize.Width;

    public Rectangle Bounds
    {
        get
        {
            var id = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = Handle, uID = IconId };
            return Shell_NotifyIconGetRect(ref id, out var rect) == 0
                ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
                : Rectangle.Empty;
        }
    }

    public void Update(Bitmap icon, string tooltip)
    {
        image?.Dispose();
        image = (Bitmap)icon.Clone();
        tip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        Apply();
    }

    void Apply()
    {
        if (image is null) return;

        var hIcon = image.GetHicon();
        try
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = Handle,
                uID = IconId,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = CallbackMessage,
                hIcon = hIcon,
                szTip = tip,
            };

            if (added && Shell_NotifyIcon(NIM_MODIFY, ref data)) return;

            added = false;
            if (Shell_NotifyIcon(NIM_ADD, ref data))
            {
                added = true;
                data.uVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIcon(NIM_SETVERSION, ref data);
            }
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == CallbackMessage)
        {
            var x = (short)(m.WParam.ToInt64() & 0xFFFF);
            var y = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            switch ((int)(m.LParam.ToInt64() & 0xFFFF))
            {
                case NIN_POPUPOPEN: PopupOpen?.Invoke(); break;
                case NIN_POPUPCLOSE: PopupClose?.Invoke(); break;
                case WM_CONTEXTMENU: ContextMenu?.Invoke(new Point(x, y)); break;
                case NIN_SELECT:
                case NIN_KEYSELECT: Select?.Invoke(); break;
            }
            return;
        }

        if (m.Msg == TaskbarCreated)
        {
            // Explorer restarted and lost every tray icon.
            added = false;
            Apply();
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (added)
        {
            var data = new NOTIFYICONDATA { cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = Handle, uID = IconId };
            Shell_NotifyIcon(NIM_DELETE, ref data);
            added = false;
        }
        image?.Dispose();
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll")]
    static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);
}
