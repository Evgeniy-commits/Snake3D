using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace Snake3D
{
    public static class DarkTitle
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void Apply(Window w)
        {
            w.SourceInitialized += (s, e) =>
            {
                try
                {
                    var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                    int on = 1;
                    // 20 — тёмная шапка (Win10 1809+/Win11), 19 — старые сборки Win10
                    if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0)
                        DwmSetWindowAttribute(h, 19, ref on, 4);

                    // Win11: точные цвета шапки и её текста, формат BGR
                    int cap = 0x2A1512;    // наш #12152A в BGR
                    int txt = 0xF5E9E6;    // наш #E6E9F5 в BGR
                    DwmSetWindowAttribute(h, 35, ref cap, 4);   // цвет шапки
                    DwmSetWindowAttribute(h, 36, ref txt, 4);   // цвет текста шапки
                }
                catch { /* старая ОС — останется системная шапка, не страшно */ }
            };
        }
    }
}
