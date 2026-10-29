using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace KaifDesktop.Platform
{
    public interface IMemeWindowAdapter : IDisposable
    {
        void Open(string file, Rectangle area, Point outside);
        bool Valid { get; }
        Rectangle Bounds { get; }
        void Move(Point position);
        void RestoreVisible(Rectangle area);
        void Relinquish();
    }

    public sealed class MemeScales
    {
        public Dictionary<string, double> scales { get; set; }
        public static MemeScales Load(string path)
        {
            var config = new JavaScriptSerializer().Deserialize<MemeScales>(File.ReadAllText(path));
            if (config == null || config.scales == null) throw new InvalidDataException("Нет таблицы масштабов мемов.");
            config.scales = new Dictionary<string, double>(config.scales, StringComparer.OrdinalIgnoreCase);
            foreach (var value in config.scales.Values)
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 10) throw new InvalidDataException("Некорректный масштаб мема.");
            return config;
        }
        public double For(string file) { double value; return scales.TryGetValue(Path.GetFileName(file), out value) ? value : 1; }
        public static Size ClientSize(Size original, double requested, Rectangle area)
        {
            // Exactly the accepted standalone test: original pixels * multiplier, no extra DPI multiplier.
            double factor = Math.Min(requested, Math.Min((area.Width - 40.0) / original.Width, (area.Height - 80.0) / original.Height));
            return new Size(Math.Max(1, (int)(original.Width * factor)), Math.Max(1, (int)(original.Height * factor)));
        }
    }

    internal sealed class MemeImageWindow : Form
    {
        internal readonly PictureBox Picture;
        internal MemeImageWindow(string file, MemeScales scales, Rectangle area)
        {
            Bitmap bitmap;
            using (var source = Image.FromFile(file)) bitmap = new Bitmap(source);
            try
            {
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual;
                Text = "Мем — " + Path.GetFileName(file);
                BackColor = Color.FromArgb(30, 30, 30);
                MinimumSize = new Size(60, 60);
                ClientSize = MemeScales.ClientSize(bitmap.Size, scales.For(file), area);
                Picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = bitmap, BackColor = BackColor };
                Controls.Add(Picture);
            }
            catch { bitmap.Dispose(); throw; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void Dispose(bool disposing)
        {
            if (disposing && Picture != null) { var image = Picture.Image; Picture.Image = null; image?.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class MemeWindowHost : IMemeWindowAdapter
    {
        private readonly MemeScales scales;
        private readonly List<MemeImageWindow> windows = new List<MemeImageWindow>();
        private MemeImageWindow current;
        internal MemeWindowHost(MemeScales config) { scales = config; }
        internal MemeImageWindow Current { get { return current; } }
        internal int WindowCount { get { return windows.Count; } }
        public bool Valid { get { return current != null && !current.IsDisposed && current.Visible; } }
        public Rectangle Bounds { get { if (!Valid) throw new InvalidOperationException("Окно мема закрыто."); return current.Bounds; } }
        public void Open(string file, Rectangle area, Point outside)
        {
            if (current != null) throw new InvalidOperationException("Предыдущее окно ещё переносится.");
            var form = new MemeImageWindow(file, scales, area);
            try
            {
                // Create the HWND before positioning so nonclient DPI/minimum sizes have already settled.
                var handle = form.Handle;
                form.Location = new Point(outside.X < area.Left ? outside.X - form.Width : outside.X, outside.Y);
                windows.Add(form);
                form.FormClosed += (s, e) => windows.Remove(form);
                current = form;
                form.Show();
            }
            catch { current = null; windows.Remove(form); form.Dispose(); throw; }
        }
        public void Move(Point position)
        {
            if (!Valid) throw new InvalidOperationException("Окно мема закрыто.");
            // Only our own Form reference is touched; foreign HWNDs are never enumerated.
            current.Location = position;
        }
        public void RestoreVisible(Rectangle area)
        {
            if (!Valid) return;
            var r = current.Bounds;
            current.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - r.Width, r.Left)), Math.Max(area.Top, Math.Min(area.Bottom - r.Height, r.Top)));
        }
        public void Relinquish() { current = null; }
        internal void CloseReleased()
        {
            foreach (var window in windows.Where(w => w != current).ToArray()) { window.Close(); window.Dispose(); }
        }
        public void Dispose()
        {
            current = null;
            foreach (var window in windows.ToArray()) { window.Close(); window.Dispose(); }
            windows.Clear();
        }
    }

    internal sealed class FakeMemeWindowAdapter : IMemeWindowAdapter
    {
        internal bool Own = true, FailOpen, FailMove;
        internal int Moves, Opens, Restores;
        internal Rectangle Window = new Rectangle(100, 100, 600, 500);
        public bool Valid { get { return Own && Opens > 0; } }
        public Rectangle Bounds { get { return Window; } }
        public void Open(string file, Rectangle area, Point outside) { if (FailOpen) throw new IOException("Test open failure"); Opens++; Window.Location = outside; }
        public void Move(Point position) { if (!Own || FailMove) throw new IOException("Test move failure"); Window.Location = position; Moves++; }
        public void RestoreVisible(Rectangle area) { Restores++; if (Own) Window.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Window.Width, Window.Left)), Math.Max(area.Top, Math.Min(area.Bottom - Window.Height, Window.Top))); }
        public void Relinquish() { }
        public void Dispose() { }
    }
}
