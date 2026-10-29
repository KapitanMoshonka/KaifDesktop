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
    internal sealed class DragUiTest
    {
        private readonly PetApplicationContext app;
        private readonly string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drag-ui-test.txt");
        private int stage;
        private double at, elapsed, calm, x, y;
        private Point origin, destination;
        private InteractionKind[] queue;
        private readonly HashSet<string> cursorPhases = new HashSet<string>(), memePhases = new HashSet<string>();
        private bool finished;
        internal static bool Failed;
        internal DragUiTest(PetApplicationContext context) { app = context; File.WriteAllText(report, "Started: real HWND, native capture and mouse messages; fake cursor, isolated timers.\r\n"); }
        private static IntPtr Packed(Point p) { return new IntPtr(unchecked((int)((ushort)p.X | ((uint)(ushort)p.Y << 16)))); }
        private void Send(int msg, Point screen) { NativeMethods.SendMessage(app.PetHandle, msg, msg == 0x202 ? IntPtr.Zero : new IntPtr(1), Packed(app.Pet.PointToClient(screen))); }
        private void Down()
        {
            Point local = new Point(-1, -1);
            for (int py = 0; py < app.Pet.ClientSize.Height && local.X < 0; py++)
                for (int px = 0; px < app.Pet.ClientSize.Width; px++) if (app.Pet.HitAt(new Point(px, py))) { local = new Point(px, py); break; }
            AlphaTests.Check(local.X >= 0, "Visible drag pixel");
            origin = app.Pet.PointToScreen(local); queue = app.Activity.ActionQueue.Preview; x = app.X; y = app.Y;
            Send(0x201, origin);
            AlphaTests.Check(app.Pet.GesturePending && !app.Activity.Dragging && NativeMethods.GetCapture() == app.PetHandle, "Down snapshots and captures without starting drag/click");
        }
        private void Cross()
        {
            destination = new Point(origin.X + SystemInformation.DragSize.Width + 20, origin.Y + SystemInformation.DragSize.Height + 20);
            Send(0x200, destination);
            AlphaTests.Check(app.Activity.Dragging && app.Activity.Animation == "dragged" && !app.Activity.Petting && !app.Pet.GesturePending, "Threshold starts drag and cancels old state");
            AlphaTests.Check(queue.SequenceEqual(app.Activity.ActionQueue.Preview), "Native drag preserves pending queue");
        }
        private void Released()
        {
            AlphaTests.Check(!app.Activity.Dragging && !app.Pet.GestureCaptured && NativeMethods.GetCapture() != app.PetHandle && app.Activity.Animation == "idle" && app.Activity.CalmElapsed < .1, "Release/loss returns idle and clears all capture/gesture flags");
            AlphaTests.Check(queue.SequenceEqual(app.Activity.ActionQueue.Preview), "Release preserves queue");
        }
        private void Forbidden()
        {
            var screen = app.Pet.PointToScreen(new Point(app.Pet.Width / 2, app.Pet.Height / 2));
            var kind = app.Activity.Kind; var before = app.Activity.ActionQueue.Preview;
            Send(0x201, screen); Send(0x200, new Point(screen.X + 80, screen.Y + 50)); Send(0x202, screen);
            AlphaTests.Check(!app.Pet.GestureCaptured && !app.Activity.Dragging && NativeMethods.GetCapture() != app.PetHandle && kind == app.Activity.Kind && before.SequenceEqual(app.Activity.ActionQueue.Preview), "Forbidden phase never records pending drag");
        }
        internal void Update(double time)
        {
            if (finished) return;
            AlphaTests.Check(time < 60 && NativeMethods.GetForegroundWindow() != app.PetHandle, "No activation or drag-test hang");
            if (stage == 0 && time > .3)
            { Down(); elapsed = app.Activity.AnimationElapsedMs; calm = app.Activity.CalmElapsed; at = time; stage = 1; }
            else if (stage == 1 && time > at + .4)
            {
                AlphaTests.Check(app.Activity.CalmElapsed == calm && app.Activity.AnimationElapsedMs == elapsed && app.X == x && app.Y == y, "Pending gesture freezes state and auto timer");
                Send(0x200, new Point(origin.X + Math.Max(0, SystemInformation.DragSize.Width / 2 - 1), origin.Y));
                AlphaTests.Check(app.Pet.GesturePending && !app.Activity.Dragging && !app.Activity.Petting, "Below-threshold move remains a potential click");
                Cross(); AlphaTests.Check(Math.Abs(app.X - x - (destination.X - origin.X)) < 1.1 && Math.Abs(app.Y - y - (destination.Y - origin.Y)) < 1.1, "No jump when dragged pose starts"); at = time; stage = 2;
            }
            else if (stage == 2 && time > at + .8)
            {
                AlphaTests.Check(app.Activity.AnimationElapsedMs > 700 && app.Activity.Animation == "dragged", "Held animation loops");
                Send(0x200, new Point(-10000, -10000)); AlphaTests.Check(app.X == app.MovementBounds.Left && app.Y == app.MovementBounds.Top, "Top/left work-area clamp");
                Send(0x200, new Point(10000, 10000)); AlphaTests.Check(app.X == app.MovementBounds.Right && app.Y == app.MovementBounds.Bottom, "Bottom/right work-area clamp");
                Send(0x202, new Point(10000, 10000)); Released();
                app.ReturnToScreen(); Down(); Cross(); app.Pet.Capture = false; Released();
                Send(0x202, destination); AlphaTests.Check(!app.Activity.Petting, "Stale release after capture loss does not click");
                Down(); Cross(); NativeMethods.SendMessage(app.PetHandle, 0x1A, IntPtr.Zero, IntPtr.Zero); Released();
                Down(); Cross(); app.ToggleHidden(); Released(); AlphaTests.Check(app.Hidden, "Hidden state preserved"); app.ToggleHidden();
                Down(); Cross(); app.TogglePause(); Released(); AlphaTests.Check(app.Paused, "Pause toggle ends current gesture");
                Down(); Cross(); elapsed = app.Activity.AnimationElapsedMs; at = time; stage = 3;
            }
            else if (stage == 3 && time > at + .4)
            {
                AlphaTests.Check(app.Paused && app.Activity.AnimationElapsedMs == elapsed, "Paused manual drag preserves pause and freezes clip");
                Send(0x200, new Point(destination.X + 60, destination.Y + 40)); Send(0x202, new Point(destination.X + 60, destination.Y + 40)); Released();
                AlphaTests.Check(app.Paused, "Release does not unpause"); app.TogglePause(); app.StartManual(InteractionKind.Sleep); stage = 4;
            }
            else if (stage == 4 && app.Activity.Sleeping)
            { Down(); AlphaTests.Check(app.Activity.Sleeping, "Sleep is not woken on mouse-down"); at = time; elapsed = app.Activity.AnimationElapsedMs; stage = 5; }
            else if (stage == 5 && time > at + .2)
            {
                AlphaTests.Check(app.Activity.Sleeping && app.Activity.AnimationElapsedMs == elapsed, "Sleep snapshot remains until gesture resolves");
                Cross(); Send(0x202, destination); Released();
                foreach (var kind in new[] { InteractionKind.ChaseTail, InteractionKind.Stretch, InteractionKind.Scratch, InteractionKind.Watch })
                { app.StartManual(kind); Down(); Cross(); Send(0x202, destination); Released(); }
                app.PetByClick(); AlphaTests.Check(app.Activity.Petting, "Petting active before drag"); Down(); Cross(); Send(0x202, destination); Released();
                app.ReturnToScreen();
                double width = Screen.PrimaryScreen.WorkingArea.Width - app.MovementBounds.Width, height = Screen.PrimaryScreen.WorkingArea.Height - app.MovementBounds.Height;
                app.TestCursor.Position = new Point((int)Math.Round(app.X + 166 * width / 192), (int)Math.Round(app.Y + 96 * height / 208));
                app.StartManual(InteractionKind.Cursor); cursorPhases.Add(app.Activity.CursorAction.Phase); Forbidden(); stage = 6;
            }
            else if (stage == 6)
            {
                if (app.Activity.Kind == InteractionKind.Cursor) { cursorPhases.Add(app.Activity.CursorAction.Phase); Forbidden(); }
                else if (!app.Activity.Active)
                { AlphaTests.Check(new[] { "chase", "grab", "pull", "release" }.All(cursorPhases.Contains), "All cursor phases natively reject drag"); app.StartManual(InteractionKind.Meme); memePhases.Add(app.Activity.MemeAction.Phase); Forbidden(); stage = 7; }
            }
            else if (stage == 7)
            {
                if (app.Activity.Kind == InteractionKind.Meme) { memePhases.Add(app.Activity.MemeAction.Phase); Forbidden(); }
                else if (!app.Activity.Active)
                {
                    AlphaTests.Check(new[] { "leave", "prepare", "grab", "pull", "release" }.All(memePhases.Contains) && app.Activity.MemeAction.Outcome == "completed", "All real meme phases reject drag without disrupting delivery");
                    Send(0x202, app.Pet.PointToScreen(Point.Empty)); AlphaTests.Check(!app.Activity.Active && !app.Pet.GestureCaptured, "No delayed gesture/click after forbidden action ends");
                    Down(); Cross();
                    File.AppendAllText(report, "PASS: real capture, threshold/click snapshot, pending freezes scheduler, offset/no jump, looping dragged, bounds, release/capture loss/stale up, environment/hide/pause cleanup, paused manual movement, cancellation of sleep/tail/stretch/scratch/watch/petting, queue unchanged, every Cursor and real Meme phase rejects gestures, no activation. Physical mouse movement/routing not performed.\r\n");
                    IntPtr closingHandle = app.PetHandle;
                    finished = true; app.ControlsWindow.Close();
                    AlphaTests.Check(NativeMethods.GetCapture() != closingHandle && !app.Activity.Dragging && app.TestMemeWindows.WindowCount == 0, "Exit clears drag/capture and owned image windows");
                }
            }
        }
        internal void Fail(Exception error) { Failed = true; finished = true; File.AppendAllText(report, "FAIL: " + error); app.ControlsWindow.Close(); }
    }
}
