using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Web.Script.Serialization;

internal static class MemeWindowTest
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [STAThread]
    private static void Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var windows = new List<Form>();
        var config = new JavaScriptSerializer().Deserialize<ScaleConfig>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meme-scales.json")));
        var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Frames", "memes");
        if (!Directory.Exists(root)) { MessageBox.Show("Не найдена папка: " + root); return; }
        foreach (var file in Directory.GetFiles(root))
        {
            Bitmap image;
            try { using (var source = Image.FromFile(file)) image = new Bitmap(source); }
            catch (ArgumentException) { continue; }
            catch (OutOfMemoryException) { continue; }
            var form = new Form();
            form.AutoScaleMode = AutoScaleMode.None;
            double requested;
            if (!config.scales.TryGetValue(Path.GetFileName(file), out requested) || requested <= 0) requested = 1.0;
            form.Text = "Мем — " + Path.GetFileName(file) + " — " + requested.ToString("0.##") + "x";
            form.StartPosition = FormStartPosition.Manual;
            form.BackColor = Color.FromArgb(30, 30, 30);
            var area = Screen.PrimaryScreen.WorkingArea;
            double scale = Math.Min(requested, Math.Min((area.Width - 40.0) / image.Width, (area.Height - 80.0) / image.Height));
            form.ClientSize = new Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
            form.MinimumSize = new Size(60, 60);
            int offset = windows.Count * 32;
            form.Location = new Point(area.Left + 20 + offset % Math.Max(32, area.Width - form.Width - 40), area.Top + 20 + offset % Math.Max(32, area.Height - form.Height - 40));
            var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image, BackColor = form.BackColor };
            form.Controls.Add(picture);
            form.FormClosed += delegate { picture.Image = null; image.Dispose(); windows.Remove(form); if (windows.Count == 0) Application.ExitThread(); };
            windows.Add(form);
        }
        if (windows.Count == 0) { MessageBox.Show("В папке нет поддерживаемых изображений."); return; }
        foreach (var window in windows.ToArray()) window.Show();
        Application.Run();
    }
    public sealed class ScaleConfig { public Dictionary<string, double> scales { get; set; } }
}
