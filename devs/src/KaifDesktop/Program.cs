using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using KaifDesktop.Infrastructure;

namespace KaifDesktop
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool selfTest = Array.IndexOf(args, "--self-test") >= 0;
            Diagnostics.TestMode = Array.Exists(args, a => a == "--self-test" || a == "--meme-window-test" || a == "--smoke-test" || a == "--auto-meme-test" || a == "--petting-test" || a == "--drag-test");
            if (Array.IndexOf(args, "--meme-window-test") >= 0) return MemeWindowTests.Run();
            bool smoke = Array.IndexOf(args, "--smoke-test") >= 0;
            bool automatic = Array.IndexOf(args, "--auto-meme-test") >= 0;
            bool petting = Array.IndexOf(args, "--petting-test") >= 0;
            bool drag = Array.IndexOf(args, "--drag-test") >= 0;
            if (selfTest) return AlphaTests.Run();
            Diagnostics.ActivityEnabled = true;
            if (smoke) Diagnostics.ActivityDirectory = Path.Combine(Diagnostics.DataDirectory, "smoke-test", "logs");
            if (automatic) Diagnostics.ActivityDirectory = Path.Combine(Diagnostics.DataDirectory, "auto-meme-test", "logs");
            if (petting) Diagnostics.ActivityDirectory = Path.Combine(Diagnostics.DataDirectory, "petting-test", "logs");
            if (drag) Diagnostics.ActivityDirectory = Path.Combine(Diagnostics.DataDirectory, "drag-test", "logs");
            bool owned;
            using (var mutex = new Mutex(true, drag ? "Local\\KaifDesktop.Alpha.Drag.0.1" : petting ? "Local\\KaifDesktop.Alpha.Petting.0.1" : automatic ? "Local\\KaifDesktop.Alpha.AutoMeme.0.1" : smoke ? "Local\\KaifDesktop.Alpha.Smoke.0.1" : "Local\\KaifDesktop.Alpha.0.1", out owned))
            {
                if (!owned) return 0;
                try
                {
                    Diagnostics.Event("app.start mode=" + (drag ? "drag-test" : petting ? "petting-test" : automatic ? "auto-meme-test" : smoke ? "smoke" : "normal"));
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    using (var context = new PetApplicationContext(smoke, automatic, petting, drag)) Application.Run(context);
                    return SmokeTest.Failed || AutomaticMemeUiTest.Failed || PettingUiTest.Failed || DragUiTest.Failed ? 1 : 0;
                }
                catch (Exception ex)
                {
                    bool logged = Diagnostics.Log(ex);
                    if (smoke || automatic || petting || drag) File.WriteAllText(Diagnostics.TestReport(drag ? "drag-ui-test.txt" : petting ? "petting-ui-test.txt" : automatic ? "auto-meme-ui-test.txt" : "smoke-test.txt"), "FAIL: " + ex);
                    else MessageBox.Show("Не удалось запустить Кайфика: " + ex.Message + (logged ? "\nПодробности: data\\logs\\errors.log" : "\nЖурнал недоступен для записи."), "Кайфик", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
