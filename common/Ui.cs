using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using Microsoft.Win32;

namespace PhoneShare
{
    // Shared look and feel for the app and the setup: loads XAML from embedded
    // resources and applies light or dark colours that follow the Windows setting.
    static class Ui
    {
        public static bool Dark { get; private set; }

        static readonly string[,] LightPalette =
        {
            { "BG", "#F4F5F7" }, { "CARD", "#FFFFFF" }, { "TEXT", "#111827" }, { "MUTED", "#6B7280" },
            { "LINE", "#E2E5EA" }, { "ACCENT", "#2563EB" }, { "ACCENTSOFT", "#EAF1FF" }, { "HOVER", "#F1F3F6" },
            { "OK", "#16A34A" }, { "DANGER", "#DC2626" }
        };

        static readonly string[,] DarkPalette =
        {
            { "BG", "#111318" }, { "CARD", "#1A1D24" }, { "TEXT", "#ECEEF2" }, { "MUTED", "#9AA3B2" },
            { "LINE", "#2B303B" }, { "ACCENT", "#4C8DFF" }, { "ACCENTSOFT", "#1B2A47" }, { "HOVER", "#232833" },
            { "OK", "#22C55E" }, { "DANGER", "#F87171" }
        };

        public static string Resource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new FileNotFoundException("Missing embedded resource: " + name);
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        public static Window LoadWindow(string resourceName)
        {
            Dark = ReadDarkSetting();
            string xaml = Resource(resourceName).Replace("$STYLES$", Resource("Styles.xaml"));
            var palette = Dark ? DarkPalette : LightPalette;
            for (int i = 0; i < palette.GetLength(0); i++)
                xaml = xaml.Replace("$" + palette[i, 0] + "$", palette[i, 1]);
            var window = (Window)XamlReader.Parse(xaml);
            window.SourceInitialized += delegate { SetDarkTitleBar(window, Dark); };
            return window;
        }

        static bool ReadDarkSetting()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch { return false; }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        static void SetDarkTitleBar(Window window, bool dark)
        {
            int on = dark ? 1 : 0;
            try { DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref on, sizeof(int)); }
            catch { }
        }
    }
}
