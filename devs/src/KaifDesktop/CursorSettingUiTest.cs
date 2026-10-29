using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop
{
    // Real WinForms controls/rendering/timer, fake cursor only; does not inject hardware input.
    internal sealed class CursorSettingUiTest
    {
        internal static bool Failed;
        private readonly PetApplicationContext app;
        private readonly string report = Diagnostics.TestReport("cursor-setting-ui-test.txt");
        private readonly HashSet<Bitmap> spinFrames = new HashSet<Bitmap>();
        private int stage, writes;
        private double at, x, y;
        private Point released;
        private bool finished;
        internal CursorSettingUiTest(PetApplicationContext context)
        {
            app = context;
            File.WriteAllText(report, "Started: real checkbox HWND/BM_CLICK, UI timer and layered spin rendering. Fake cursor; isolated settings.\r\n");
        }
        private void ClickToggle(bool expected)
        {
            var check = app.ControlsWindow.CursorCaptureCheckBox;
            NativeMethods.SendMessage(check.Handle, 0xF5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
            // WinForms can paint its own check state; BM_GETCHECK is not its managed Checked property.
            AlphaTests.Check(check.Checked == expected && app.Activity.CursorCaptureEnabled == expected, "Native checkbox click and controller stay synchronized");
            AlphaTests.Check(expected || (!app.ControlsWindow.CursorButton.Enabled && !app.Activity.ActionQueue.Preview.Contains(InteractionKind.Cursor)), "Disabled manual button and full queue filter");
            var saved = new SettingsStore(Path.Combine(Diagnostics.DataDirectory, "cursor-setting-test")).Load();
            AlphaTests.Check(saved.CursorCaptureEnabled == expected, "Checkbox change saved immediately");
        }
        internal void Update(double time)
        {
            if (finished) return;
            AlphaTests.Check(time < 20 && app.TestCursor != null, "Safe diagnostic mode and bounded test duration");
            if (stage == 0 && time > .3)
            {
                var controls = app.ControlsWindow;
                controls.WindowState = FormWindowState.Normal;
                AlphaTests.Check(controls.CursorCaptureCheckBox.Text == "Включить захват мышки" && controls.CursorCaptureCheckBox.Checked && controls.TailButton.Text == "Крутись", "Exact visible product labels and default enabled state");
                ClickToggle(false);
                app.StartManual(InteractionKind.Cursor);
                AlphaTests.Check(!app.Activity.Active && app.TestCursor.Begins == 0, "Disabled direct manual request never arms cursor");
                controls.TailButton.PerformClick();
                AlphaTests.Check(app.Activity.Kind == InteractionKind.ChaseTail, "Крутись button starts spin");
                x = app.X; y = app.Y;
                double elapsed = app.Activity.AnimationElapsedMs;
                ClickToggle(true); ClickToggle(false);
                AlphaTests.Check(app.Activity.Kind == InteractionKind.ChaseTail && app.Activity.AnimationElapsedMs == elapsed && app.X == x && app.Y == y, "Checkbox preserves running non-Cursor animation and position");
                using (var bitmap = new Bitmap(controls.Width, controls.Height))
                {
                    controls.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Diagnostics.TestReport("cursor-setting-controls.png"));
                }
                at = time; stage = 1;
            }
            if (stage == 1)
            {
                if (app.Activity.Kind == InteractionKind.ChaseTail) spinFrames.Add(app.Frame);
                else
                {
                    AlphaTests.Check(spinFrames.Count == 10 && time - at >= 3.4 && time - at < 4 && app.X == x && app.Y == y, "All ten spin frames rendered through three cycles, then calm without movement");
                    ClickToggle(true);
                    app.TestCursor.Position = new Point((int)Math.Round(app.X) + (int)Math.Round(166 * app.Pet.Width / 192.0), (int)Math.Round(app.Y) + (int)Math.Round(96 * app.Pet.Height / 208.0));
                    app.ControlsWindow.CursorButton.PerformClick();
                    AlphaTests.Check(app.Activity.Kind == InteractionKind.Cursor, "Re-enabled manual cursor action starts");
                    stage = 2;
                }
            }
            else if (stage == 2 && app.Activity.CursorAction.Attached)
            {
                released = app.TestCursor.Position; writes = app.TestCursor.Moves;
                AlphaTests.Check(writes > 0, "Fake cursor follows attached mouth before cancellation");
                ClickToggle(false);
                AlphaTests.Check(!app.Activity.Active && !app.TestCursor.Armed && !app.Activity.CursorAction.Attached && app.TestCursor.Position == released && app.TestCursor.Moves == writes, "Checkbox releases active capture without snap");
                at = time; stage = 3;
            }
            else if (stage == 3 && time > at + .3)
            {
                AlphaTests.Check(app.TestCursor.Position == released && app.TestCursor.Moves == writes, "No stale cursor writes after disabling");
                ClickToggle(true);
                AlphaTests.Check(app.ControlsWindow.CursorButton.Enabled, "Manual button re-enabled when allowed");
                ClickToggle(false);
                File.AppendAllText(report, "PASS: exact checkbox/Крутись labels; native BM_CLICK synchronizes checked/controller/button/12-item queue and persists true/false; disabled direct start denied; active non-Cursor spin unaffected; all 10 frames rendered over 3 cycles; re-enable starts fake capture; disabling releases attachment without snap or stale writes. Real user cursor never moved. Physical mouse input and artistic acceptance remain manual.\r\n");
                finished = true;
                app.ControlsWindow.Close();
            }
        }
        internal void Fail(Exception error)
        {
            Failed = true; finished = true;
            File.AppendAllText(report, "FAIL: " + error + Environment.NewLine);
            app.ControlsWindow.Close();
        }
    }
}
