using System;
using System.Drawing;
using System.Windows.Forms;

namespace KaifDesktop.Platform
{
    internal sealed class DesktopAreaService
    {
        internal const double DisplayScale = 2.0 / 3.0;
        public Rectangle Area { get { return Screen.PrimaryScreen.WorkingArea; } }
        public double Scale(IntPtr hwnd)
        {
            try { uint dpi = NativeMethods.GetDpiForWindow(hwnd); return dpi == 0 ? 1 : dpi / 96.0; }
            catch (EntryPointNotFoundException) { using (var graphics = Graphics.FromHwnd(hwnd)) return graphics.DpiX / 96.0; }
        }
        public Size SpriteSize(Size original, double scale)
        {
            return SpriteSize(original, scale, Area);
        }
        internal static Size SpriteSize(Size original, double dpiScale, Rectangle area)
        {
            double fit = Math.Min(dpiScale * DisplayScale, Math.Min((double)area.Width / original.Width, (double)area.Height / original.Height));
            return new Size(Math.Max(1, (int)Math.Round(original.Width * fit)), Math.Max(1, (int)Math.Round(original.Height * fit)));
        }
        internal static RectangleF MovementBounds(Size size, Rectangle area)
        {
            return new RectangleF(area.Left, area.Top, Math.Max(0, area.Width - size.Width), Math.Max(0, area.Height - size.Height));
        }
        internal static Point SpritePosition(double x, double y, Size size, Rectangle area)
        {
            int top = Math.Max(area.Top, Math.Min(area.Bottom - size.Height, (int)Math.Round(y)));
            int left = Math.Max(area.Left, Math.Min(area.Right - size.Width, (int)Math.Round(x)));
            return new Point(left, top);
        }
    }
}
