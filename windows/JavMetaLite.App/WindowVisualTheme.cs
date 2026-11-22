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

    private static ResourceDictionary? _palette;
    private static bool? _dark;
    public static string Appearance { get; private set; } = "system";

    public static void SetAppearance(string? value)
    {
        Appearance = value is "light" or "dark" ? value : "system";
        Refresh();
    }

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowsSettingsChanged);
            Refresh();
            SetCaption(handle, _dark == true);
        };
    }

    private static IntPtr WindowsSettingsChanged(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x001A or 0x031A && Appearance == "system")
            Application.Current?.Dispatcher.BeginInvoke(Refresh);
        return IntPtr.Zero;
    }

    private static void Refresh()
    {
        var app = Application.Current;
        if (app is null) return;
        bool dark = Appearance switch
        {
            "light" => false,
            "dark" => true,
            _ => Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is 0
        };
        if (_dark == dark && _palette != null && app.Resources.MergedDictionaries.Contains(_palette)) return;
        _dark = dark;
        var next = new ResourceDictionary { Source = new Uri(
            "/JavMetaLite;component/Resources/Palette." + (dark ? "Dark" : "Light") + ".xaml", UriKind.Relative) };
        var dictionaries = app.Resources.MergedDictionaries;
        if (_palette is not null) dictionaries.Remove(_palette);
        dictionaries.Add(next);
        _palette = next;
        foreach (Window window in app.Windows)
            SetCaption(new WindowInteropHelper(window).Handle, dark);
    }

    private static void SetCaption(IntPtr handle, bool dark)
    {
        if (handle == IntPtr.Zero) return;
        try
        {
            var enabled = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
            SetColorAttribute(handle, DwmwaBorderColor, dark ? ToColorRef(57,68,81) : ToColorRef(222,218,211));
            SetColorAttribute(handle, DwmwaCaptionColor, dark ? ToColorRef(26,32,40) : ToColorRef(252,250,246));
            SetColorAttribute(handle, DwmwaTextColor, dark ? ToColorRef(238,241,246) : ToColorRef(35,54,78));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
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
