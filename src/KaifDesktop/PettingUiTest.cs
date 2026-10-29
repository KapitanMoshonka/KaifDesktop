using System;
using System.Drawing;
using System.IO;
using System.Linq;
using KaifDesktop.Behavior;
using KaifDesktop.Platform;
using KaifDesktop.UI;

namespace KaifDesktop
{
    internal sealed class PettingUiTest
    {
        private readonly PetApplicationContext app;
        private readonly string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "petting-ui-test.txt");
        private InteractionKind[] queue;
        private int stage;
        private double at, elapsed, x, y;
        private Point hit;
        private bool finished;
        internal static bool Failed;
        internal PettingUiTest(PetApplicationContext context) { app = context; File.WriteAllText(report, "Started: real UI/hit mask/native messages; isolated calm interval, fake cursor.\r\n"); }
        private static IntPtr Packed(Point p) { return new IntPtr(unchecked((int)((ushort)p.X | ((uint)(ushort)p.Y << 16)))); }
        private void Click(Point p)
        {
            bool clickable = app.Pet.HitAt(p);
            NativeMethods.SendMessage(app.PetHandle, 0x201, new IntPtr(1), Packed(p));
            AlphaTests.Check((NativeMethods.GetCapture() == app.PetHandle) == clickable, "Capture exists only for a visible pending gesture");
            NativeMethods.SendMessage(app.PetHandle, 0x202, IntPtr.Zero, Packed(p));
            AlphaTests.Check(NativeMethods.GetCapture() != app.PetHandle, "Pet leaves no mouse capture after click");
        }
        private Point FindHit()
        {
            for (int py = 0; py < app.Pet.ClientSize.Height; py++)
                for (int px = 0; px < app.Pet.ClientSize.Width; px++) if (app.Pet.HitAt(new Point(px, py))) return new Point(px, py);
            throw new InvalidOperationException("No clickable silhouette");
        }
        private void StartByNativeClick()
        {
            AlphaTests.Check(app.Pet.ClickMode == PetClickMode.Pet, "Calm silhouette accepts petting");
            hit = FindHit();
            AlphaTests.Check(NativeMethods.SendMessage(app.PetHandle, 0x84, IntPtr.Zero, Packed(app.Pet.PointToScreen(hit))) == new IntPtr(1), "Silhouette native HTCLIENT");
            AlphaTests.Check(NativeMethods.SendMessage(app.PetHandle, 0x84, IntPtr.Zero, Packed(app.Pet.PointToScreen(Point.Empty))) == new IntPtr(-1), "Transparent native HTTRANSPARENT");
            Click(Point.Empty); AlphaTests.Check(!app.Activity.Active, "Transparent message does not pet");
            queue = app.Activity.ActionQueue.Preview; x = app.X; y = app.Y;
            Click(hit);
            AlphaTests.Check(app.Activity.Petting && !app.Walking && app.Pet.ClickMode == PetClickMode.DragOnly, "Native visible click starts one petting and restores click-through");
        }
        internal void Update(double time)
        {
            if (finished) return;
            AlphaTests.Check(time < 30 && NativeMethods.GetForegroundWindow() != app.PetHandle, "UI petting does not hang or activate");
            if (stage > 0 && stage < 7) AlphaTests.Check(app.X == x && app.Y == y && queue.SequenceEqual(app.Activity.ActionQueue.Preview), "Petting/pause/hidden preserves position and queue");
            if (stage == 0 && time > .3)
            {
                AlphaTests.Check(app.Walking, "Starts from walking"); StartByNativeClick(); at = time; stage = 1;
            }
            else if (stage == 1 && time > at + .15)
            {
                elapsed = app.Activity.AnimationElapsedMs; Click(hit); app.PetByClick();
                AlphaTests.Check(app.Activity.AnimationElapsedMs == elapsed, "Repeated click does not restart");
                app.TogglePause(); at = time; stage = 2;
            }
            else if (stage == 2 && time > at + .6)
            {
                AlphaTests.Check(app.Activity.Petting && app.Activity.AnimationElapsedMs == elapsed && app.Pet.ClickMode == PetClickMode.DragOnly, "Pause freezes petting and disables clicks");
                app.ToggleHidden(); at = time; stage = 3;
            }
            else if (stage == 3 && time > at + .6)
            {
                AlphaTests.Check(app.Activity.AnimationElapsedMs == elapsed, "Paused hide freezes petting"); app.TogglePause(); at = time; stage = 4;
            }
            else if (stage == 4 && time > at + .6)
            {
                AlphaTests.Check(app.Activity.AnimationElapsedMs == elapsed && app.Hidden, "Hidden running state still freezes petting"); app.ToggleHidden(); stage = 5;
            }
            else if (stage == 5 && !app.Activity.Active)
            {
                AlphaTests.Check(app.Activity.Animation == "idle" && app.Activity.CalmElapsed < .1, "Petting finishes into fresh idle"); StartByNativeClick(); stage = 6;
            }
            else if (stage == 6 && !app.Activity.Active)
            {
                AlphaTests.Check(app.Activity.Animation == "idle" && app.Activity.CalmElapsed < .1, "Idle click also finishes one clip");
                app.ControlsWindow.StretchButton.PerformClick();
                Click(hit); app.PetByClick();
                AlphaTests.Check(app.Activity.Kind == InteractionKind.Stretch && !app.Activity.Petting && app.Pet.ClickMode == PetClickMode.DragOnly, "Active interaction ignores both click and pet request");
                stage = 7;
                File.AppendAllText(report, "PASS: native WM_NCHITTEST/WM_LBUTTONUP on rendered silhouette in Walking and Idle, transparent click ignored, movement stops, no repeat restart, queue unchanged, pause/hide freeze and resume, fresh Idle/calm reset, active Stretch rejects petting, no activation. Physical hardware mouse clicks/routing to other apps not performed.\r\n");
                finished = true; app.ControlsWindow.Close();
            }
        }
        internal void Fail(Exception error) { Failed = true; finished = true; File.AppendAllText(report, "FAIL: " + error); app.ControlsWindow.Close(); }
    }
}
