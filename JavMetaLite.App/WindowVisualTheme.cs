using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JavMetaLite.App;

internal static class WindowVisualTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private static readonly int BorderColor = ToColorRef(0x2A, 0x35, 0x44);
    private static readonly int CaptionColor = ToColorRef(0x11, 0x18, 0x21);
    private static readonly int TextColor = ToColorRef(0xF1, 0xF5, 0xF9);

    public static void ApplyDarkTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyDarkTitleBar(new WindowInteropHelper(window).Handle);
    }

    private static void ApplyDarkTitleBar(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var enabled = 1;
            var result = DwmSetWindowAttribute(
                handle,
                DwmwaUseImmersiveDarkMode,
                ref enabled,
                Marshal.SizeOf<int>());
            if (result != 0)
            {
                _ = DwmSetWindowAttribute(
                    handle,
                    DwmwaUseImmersiveDarkModeBefore20H1,
                    ref enabled,
                    Marshal.SizeOf<int>());
            }

            SetColorAttribute(handle, DwmwaBorderColor, BorderColor);
            SetColorAttribute(handle, DwmwaCaptionColor, CaptionColor);
            SetColorAttribute(handle, DwmwaTextColor, TextColor);
        }
        catch (DllNotFoundException)
        {
            // Older or non-standard Windows environments keep their native title bar.
        }
        catch (EntryPointNotFoundException)
        {
            // DWM is unavailable; leaving the native title bar is the safe fallback.
        }
    }

    private static void SetColorAttribute(IntPtr handle, int attribute, int color)
    {
        _ = DwmSetWindowAttribute(handle, attribute, ref color, Marshal.SizeOf<int>());
    }

    private static int ToColorRef(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
