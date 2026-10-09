using Microsoft.Win32;

namespace Ember;

sealed record Theme(Color Background, Color Text, Color Muted, Color Track)
{
    public static readonly Theme Dark = new(
        Color.FromArgb(0x20, 0x20, 0x20),
        Color.FromArgb(0xF3, 0xF3, 0xF3),
        Color.FromArgb(0x9E, 0x9E, 0x9E),
        Color.FromArgb(0x3D, 0x3D, 0x3D));

    public static readonly Theme Light = new(
        Color.FromArgb(0xF9, 0xF9, 0xF9),
        Color.FromArgb(0x1B, 0x1B, 0x1B),
        Color.FromArgb(0x6E, 0x6E, 0x6E),
        Color.FromArgb(0xE1, 0xE1, 0xE1));

    public static Theme Current
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var light = key?.GetValue("SystemUsesLightTheme") as int? ?? 1;
            return light != 0 ? Light : Dark;
        }
    }
}
