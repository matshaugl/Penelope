using System.Drawing;
using System.Runtime.InteropServices;

namespace Penelope.Host.Tray;

static class LogoIcon
{
    private static readonly BrandMark.Spec Mark = BrandMark.Parse(BrandMark.SourceSvg());
    public static string Caption(LogoTone tone)
    {
        return tone switch
        {
            LogoTone.Running => "Penelope — service is running",
            LogoTone.Busy => "Penelope — service is working",
            _ => "Penelope — service is stopped",
        };
    }

    public static Icon Create(LogoTone tone)
    {
        using var bitmap = BrandMark.Draw(Mark, 64, Dot(tone));
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Color Dot(LogoTone tone)
    {
        return tone switch
        {
            LogoTone.Running => Color.FromArgb(0x22, 0xc5, 0x5e),
            LogoTone.Busy => Color.FromArgb(0xea, 0xb3, 0x08),
            _ => Color.FromArgb(0xef, 0x44, 0x44),
        };
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
