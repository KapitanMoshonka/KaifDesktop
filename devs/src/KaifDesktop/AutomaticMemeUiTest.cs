using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using KaifDesktop.Behavior;
using KaifDesktop.Platform;

namespace KaifDesktop
{
    // Uses the production WinForms timer/render/geometry/scheduler path, isolated settings and fake cursor.
    internal sealed class AutomaticMemeUiTest
    {
        private readonly PetApplicationContext app;
        private readonly string report = KaifDesktop.Infrastructure.Diagnostics.TestReport("auto-meme-ui-test.txt");
        private readonly HashSet<string> phases = new HashSet<string>();
        private MemeImageWindow delivered;
        private bool started, outside, finished;
        internal static bool Failed;
        internal AutomaticMemeUiTest(PetApplicationContext context)
        {
            app = context;
            File.WriteAllText(report, "Started: production UI timer, diagnosticMode=False, reproducible queue RNG, isolated 1s calm timer, actual image window, fake cursor.\r\n");
            AlphaTests.Check(!app.DiagnosticMode && app.Activity.ActionQueue.AvailableCount == 7 && app.Activity.ActionQueue.Preview[0] == InteractionKind.Meme, "Startup availability and first fill select Meme with diagnostic RNG only");
        }
        internal void Update(double seconds)
        {
            if (finished) return;
            AlphaTests.Check(seconds < 45, "Automatic UI delivery timeout");
            if (app.Activity.Kind == InteractionKind.Meme)
            {
                AlphaTests.Check(!app.Activity.Manual && app.Activity.AutomaticCount == 1, "Scheduler started Meme automatically");
                started = true; phases.Add(app.Activity.MemeAction.Phase);
                var screen = Screen.PrimaryScreen; double width = screen.WorkingArea.Width - app.MovementBounds.Width;
                outside |= app.X + width < screen.Bounds.Left || app.X > screen.Bounds.Right;
                if (app.TestMemeWindows.Current != null) delivered = app.TestMemeWindows.Current;
            }
            if (started && !app.Activity.Active)
            {
                AlphaTests.Check(app.Activity.MemeAction.Outcome == "completed" && !app.Activity.MemeAction.Faulted, "Normal render/environment/Valid checks did not cancel delivery");
                AlphaTests.Check(outside && new[] { "leave", "prepare", "grab", "pull", "release" }.All(phases.Contains), "Actual UI observed every delivery phase and full offscreen exit");
                AlphaTests.Check(delivered != null && delivered.Visible && Screen.PrimaryScreen.WorkingArea.Contains(delivered.Bounds), "Automatically delivered real window remains visible after release");
                AlphaTests.Check(NativeMethods.GetForegroundWindow() != delivered.Handle && NativeMethods.GetForegroundWindow() != app.PetHandle, "Delivery did not steal focus");
                File.AppendAllText(report, "PASS: startup availability=7 -> first random queue -> real 33ms timer -> auto dequeue/Meme.Start -> leave -> open/prepare -> grab -> pull -> release/completed. Manual=False; AutomaticCount=1; diagnosticMode=False; pet fully outside screen; actual image window visible; EnvironmentChanged/Clamp/Synchronize/Valid/Faulted did not cancel. Elapsed=" + seconds.ToString("0.00") + "s. Normal RNG/probabilities unchanged.\r\n");
                finished = true; app.ControlsWindow.Close();
                AlphaTests.Check(delivered.IsDisposed && app.TestMemeWindows.WindowCount == 0, "Test exit disposes real window");
            }
        }
        internal void Fail(Exception error)
        {
            Failed = true; finished = true; File.AppendAllText(report, "FAIL: " + error + Environment.NewLine); app.ControlsWindow.Close();
        }
    }
}
