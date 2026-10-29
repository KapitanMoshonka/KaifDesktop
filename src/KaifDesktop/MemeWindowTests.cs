using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop
{
    internal static class MemeWindowTests
    {
        internal static int Run()
        {
            string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meme-window-test.txt");
            var report = new StringBuilder();
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string source = new SettingsStore(Diagnostics.DataDirectory).Load().MemeSourceDirectory;
                if (!Path.IsPathRooted(source)) source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, source);
                var files = new MemeQueue(new Random(1)); files.Scan(source);
                AlphaTests.Check(files.Files.Length > 0, "Real images available");
                var scales = MemeScales.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meme-scales.json"));
                var screen = Screen.PrimaryScreen.Bounds; var area = Screen.PrimaryScreen.WorkingArea;
                var foreground = NativeMethods.GetForegroundWindow();
                using (var host = new MemeWindowHost(scales))
                using (var assets = new AssetCatalog(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Frames")))
                using (var process = Process.GetCurrentProcess())
                {
                    uint initialGdi = 0, initialUser = 0;
                    int cycles = 0;
                    foreach (string file in files.Files)
                    foreach (bool left in new[] { true, false })
                    {
                        var size = new Size(160, 173); // actual pet size at 120 DPI; image size is independent.
                        var bounds = DesktopAreaService.MovementBounds(size, area);
                        var motion = new BasicBehaviorController(new PetSettings(), new Random(1), bounds);
                        motion.MoveTo(left ? bounds.Left : bounds.Right, bounds.Top + 80, bounds);
                        var queue = new MemeQueue(new Random(1)); queue.SetFiles(new[] { file });
                        var scene = new MemeInteraction(motion, assets, host, queue); scene.Geometry(screen, area, size);
                        AlphaTests.Check(scene.Start(), "Real scene starts");
                        MemeImageWindow form = null; bool outside = false, pulled = false;
                        for (int i = 0; i < 5000 && scene.Active; i++)
                        {
                            scene.Update(.02, 1.25); scene.SynchronizeWindow(1.25); Application.DoEvents();
                            outside |= motion.X + size.Width < screen.Left || motion.X > screen.Right;
                            pulled |= scene.Phase == "pull";
                            if (form == null && host.Current != null)
                            {
                                form = host.Current;
                                using (var image = Image.FromFile(file))
                                    AlphaTests.Check(form.ClientSize == MemeScales.ClientSize(image.Size, scales.For(file), area), "Client pixels match accepted standalone scale: " + file);
                                using (var unlocked = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                                AlphaTests.Check(!screen.IntersectsWith(form.Bounds), "Image initially opens entirely offscreen");
                            }
                            AlphaTests.Check(NativeMethods.GetForegroundWindow() == foreground, "Delivery never activates meme window");
                        }
                        AlphaTests.Check(outside && pulled && !scene.Active && !scene.Faulted && form != null && form.Visible && area.Contains(form.Bounds), "Actual window fully delivered from both edges");
                        AlphaTests.Check(form.Picture.SizeMode == PictureBoxSizeMode.Zoom, "Zoom picture box");
                        string next; AlphaTests.Check(!queue.TryPeek(out next), "Actually shown singleton is not repeated");
                        report.AppendLine(Path.GetFileName(file) + " scale=" + scales.For(file) + " client=" + form.ClientSize + " side=" + (left ? "left" : "right"));
                        var deliveredSize = form.Size; form.Location = new Point(area.Left + 40, area.Top + 40); form.Size = new Size(deliveredSize.Width + 10, deliveredSize.Height + 10);
                        Application.DoEvents(); AlphaTests.Check(form.Visible, "Released window remains movable/resizable");
                        host.CloseReleased(); AlphaTests.Check(form.IsDisposed && form.Picture.Image == null && host.WindowCount == 0, "Closed image resources released");
                        if (cycles++ == 0) { initialGdi = NativeMethods.GetGuiResources(process.Handle, 0); initialUser = NativeMethods.GetGuiResources(process.Handle, 1); }
                    }
                    foreach (string phase in new[] { "prepare", "grab", "pull", "release", "close-pull", "exit" })
                    {
                        var size = new Size(160, 173); var bounds = DesktopAreaService.MovementBounds(size, area);
                        var motion = new BasicBehaviorController(new PetSettings(), new Random(1), bounds);
                        var queue = new MemeQueue(new Random(1)); queue.SetFiles(files.Files);
                        var scene = new MemeInteraction(motion, assets, host, queue); scene.Geometry(screen, area, size); scene.Start();
                        string target = phase == "close-pull" ? "pull" : phase == "exit" ? "grab" : phase;
                        for (int i = 0; i < 5000 && scene.Active && scene.Phase != target; i++) { scene.Update(.02, 1.25); scene.SynchronizeWindow(1.25); Application.DoEvents(); }
                        AlphaTests.Check(scene.Phase == target && host.Valid, "Cancellation phase reached " + phase);
                        var form = host.Current;
                        if (phase == "close-pull") { form.Close(); Application.DoEvents(); scene.Update(.02, 1.25); AlphaTests.Check(!scene.Active && !scene.Faulted, "Closing while carrying finishes normally"); }
                        else { scene.Cancel(); AlphaTests.Check(form.Visible && area.Contains(form.Bounds), "Cancel restores owned window " + phase); }
                        if (phase == "exit") host.Dispose(); else host.CloseReleased();
                        AlphaTests.Check(host.WindowCount == 0 && form.IsDisposed && form.Picture.Image == null, "Cancel/exit leaves no hidden resources");
                    }
                    // Exercise the real automatic scheduler, not Start(kind, false) called by a test.
                    // Select a reproducible seed only in this diagnostic; production still uses Random().
                    int seed = Enumerable.Range(0, 10000).First(n => new Random(n).Next(7) == (int)InteractionKind.Meme);
                    var autoValues = new PetSettings { BehaviorVersion = 2, TimingVersion = 1 };
                    var autoSize = new Size(160, 173); var autoBounds = DesktopAreaService.MovementBounds(autoSize, area);
                    var autoMotion = new BasicBehaviorController(autoValues, new Random(1), autoBounds);
                    var autoFiles = new MemeQueue(new Random(1)); autoFiles.SetFiles(files.Files);
                    var autoMeme = new MemeInteraction(autoMotion, assets, host, autoFiles); autoMeme.Geometry(screen, area, autoSize);
                    var pointer = new FakeCursorAdapter();
                    var automatic = new InteractionController(autoMotion, assets, autoValues, new Random(seed),
                        new CursorInteraction(autoMotion, assets, autoValues, pointer, new Random(1)),
                        new WatchInteraction(autoMotion, assets, autoValues, pointer), autoMeme);
                    automatic.SetRenderedSize(autoSize);
                    AlphaTests.Check(automatic.ActionQueue.AvailableCount == 7 && automatic.ActionQueue.Preview[0] == InteractionKind.Meme, "Normal seven-kind queue with reproducible diagnostic seed");
                    automatic.Update(autoValues.CalmIntervalSeconds - .01, autoBounds, 1.25);
                    AlphaTests.Check(!automatic.Active && automatic.AutomaticCount == 0, "No early auto start");
                    automatic.Update(.01, autoBounds, 1.25);
                    AlphaTests.Check(automatic.Kind == InteractionKind.Meme && !automatic.Manual && automatic.AutomaticCount == 1, "Idle timer itself selected and started Meme");
                    MemeImageWindow autoForm = null;
                    for (int tick = 0; tick < 5000 && automatic.Active; tick++)
                    {
                        automatic.Update(.02, autoBounds, 1.25); automatic.SynchronizeMeme(1.25); Application.DoEvents();
                        if (host.Current != null) autoForm = host.Current;
                    }
                    AlphaTests.Check(!automatic.Active && autoMeme.Outcome == "completed" && !autoMeme.Faulted && autoForm != null && autoForm.Visible && area.Contains(autoForm.Bounds), "Automatically selected meme delivered a real visible image window");
                    AlphaTests.Check(NativeMethods.GetForegroundWindow() == foreground, "Automatic delivery preserves foreground");
                    host.CloseReleased();
                    report.AppendLine("PASS: actual automatic path: idle timer -> seven-kind random queue (diagnostic seed=" + seed + ") -> Meme, Manual=False, AutomaticCount=1 -> real image window -> completed/released/visible. No direct Start call, no production probability change.");
                    uint finalGdi = NativeMethods.GetGuiResources(process.Handle, 0), finalUser = NativeMethods.GetGuiResources(process.Handle, 1);
                    AlphaTests.Check(finalGdi <= initialGdi + 3 && finalUser <= initialUser + 3, "No native resource growth after real windows");
                    report.AppendLine("PASS: " + cycles + " real deliveries, fixed client scales, source files unlocked, no activation, release/manual resize, cancel in all phases, close during pull, exit disposal. GDI " + initialGdi + "->" + finalGdi + "; USER " + initialUser + "->" + finalUser);
                }
                File.WriteAllText(output, report.ToString()); return 0;
            }
            catch (Exception error) { File.WriteAllText(output, report + "FAIL: " + error); return 1; }
        }
    }
}
