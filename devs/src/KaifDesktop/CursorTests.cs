using System;
using System.Drawing;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop
{
    internal static class CursorTests
    {
        private sealed class FifthChoice : Random
        {
            internal int LastMaximum;
            public override int Next(int maxValue) { LastMaximum = maxValue; return 4; }
        }
        internal static void Run(AssetCatalog assets)
        {
            var bounds = new RectangleF(-300, -100, 900, 700);
            foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 }) foreach (bool left in new[] { false, true })
            {
                var settings = new PetSettings { BehaviorVersion = 2 };
                var motion = new BasicBehaviorController(settings, new Random(1), bounds);
                var adapter = new FakeCursorAdapter();
                var size = new Size((int)Math.Round(192 * dpi * 2 / 3), (int)Math.Round(208 * dpi * 2 / 3));
                string suffix = left ? "left" : "right";
                var anchor = assets.Clips["grab-" + suffix].MouthAnchors[0];
                adapter.Position = new Point((int)Math.Round(motion.X) + (int)Math.Round(anchor[0] * size.Width / 192.0), (int)Math.Round(motion.Y) + (int)Math.Round(anchor[1] * size.Height / 208.0));
                var sequence = new CursorInteraction(motion, assets, settings, adapter, new Random(3));
                AlphaTests.Check(sequence.Start(), "Cursor action armed");
                bool grabbed = false, pulling = false, releasing = false, detached = false;
                int atDetachMoves = -1;
                for (int tick = 0; tick < 2000 && sequence.Active; tick++)
                {
                    double oldX = motion.X, oldY = motion.Y;
                    sequence.Update(0.01, bounds, dpi, size);
                    AlphaTests.Check(Math.Sqrt(Math.Pow(motion.X - oldX, 2) + Math.Pow(motion.Y - oldY, 2)) <= settings.CursorChaseSpeed * dpi * 0.01 + 0.01, "No pet teleport during chase/grab/pull");
                    AlphaTests.Check(motion.X >= bounds.Left && motion.X <= bounds.Right && motion.Y >= bounds.Top && motion.Y <= bounds.Bottom, "Cursor scenario stays in work area");
                    if (sequence.Attached)
                    {
                        grabbed = true;
                        sequence.SynchronizeCursor();
                        AlphaTests.Check(adapter.Position == sequence.MouthPoint(), "Cursor follows actual per-frame scaled mouth anchor");
                        var clip = assets.Clips[sequence.Animation]; var a = clip.MouthAnchors[clip.IndexAt(sequence.ElapsedMs)];
                        var expected = new Point((int)Math.Round(motion.X) + (int)Math.Round(a[0] * size.Width / 192.0), (int)Math.Round(motion.Y) + (int)Math.Round(a[1] * size.Height / 208.0));
                        AlphaTests.Check(adapter.Position == expected, "Anchor mapping matches rendered pixel coordinates");
                    }
                    else if (!grabbed) AlphaTests.Check(adapter.Moves == 0, "No cursor writes before attach frame event");
                    pulling |= sequence.Phase == "pull"; releasing |= sequence.Phase == "release";
                    if (grabbed && !sequence.Attached && !detached) { detached = true; atDetachMoves = adapter.Moves; }
                    if (detached) { sequence.SynchronizeCursor(); AlphaTests.Check(adapter.Moves == atDetachMoves, "No writes or old-position snap after detach"); }
                }
                AlphaTests.Check(grabbed && pulling && releasing && detached && !sequence.Active && !adapter.Armed, "Complete grab/pull/release in both directions and DPI");
            }
            foreach (string reason in new[] { "escape", "session", "pause", "hidden", "exit", "error", "geometry" })
            {
                var settings = new PetSettings { BehaviorVersion = 2 };
                var motion = new BasicBehaviorController(settings, new Random(1), bounds);
                var adapter = new FakeCursorAdapter { Position = new Point((int)motion.X + 111, (int)motion.Y + 64) };
                var sequence = new CursorInteraction(motion, assets, settings, adapter, new Random(3));
                var controller = new InteractionController(motion, assets, settings, new Random(1), sequence);
                controller.SetRenderedSize(new Size(128, 139)); controller.Start(InteractionKind.Cursor, true);
                for (int i = 0; i < 200 && !sequence.Attached; i++) controller.Update(0.01, bounds, 1);
                AlphaTests.Check(sequence.Attached, "Attached before cancellation fixture");
                if (reason == "escape") adapter.Escape = true;
                else if (reason == "session") adapter.Session = false;
                else if (reason == "error") adapter.FailMove = true;
                else controller.CancelCursor(); // Same cancellation entry point used by pause/hide/exit/return.
                controller.SynchronizeCursor();
                AlphaTests.Check(!sequence.Attached && !sequence.Active && !adapter.Armed && !controller.Active && controller.CalmElapsed == 0, "Release and reset on " + reason);
                int writes = adapter.Moves;
                controller.Update(0.1, bounds, 1); controller.SynchronizeCursor();
                AlphaTests.Check(adapter.Moves == writes, "No reattachment after " + reason);
            }
            var values = new PetSettings { BehaviorVersion = 2, CursorTotalSeconds = 3, CursorChaseSpeed = 20 };
            var slow = new BasicBehaviorController(values, new Random(1), bounds);
            var runaway = new FakeCursorAdapter { Position = new Point(500, 500) };
            var chase = new CursorInteraction(slow, assets, values, runaway, new Random(3));
            chase.Start();
            for (int i = 0; i < 250; i++) chase.Update(0.01, bounds, 1, new Size(128, 139));
            AlphaTests.Check(!chase.Active && runaway.Moves == 0 && !runaway.Armed, "Chase timeout leaves user cursor free");
            runaway.Position = new Point(-5000, -5000); double x = slow.X, y = slow.Y;
            chase.Start(); chase.Update(0.01, bounds, 1, new Size(128, 139));
            AlphaTests.Check(!chase.Active && slow.X == x && slow.Y == y, "Unreachable cursor cancels without teleport");
            runaway.BeginAllowed = false;
            AlphaTests.Check(!chase.Start() && !runaway.Armed, "No capture when escape registration is unavailable");
            var mobile = new BasicBehaviorController(new PetSettings(), new Random(2), bounds);
            var free = new FakeCursorAdapter { Position = new Point((int)mobile.X + 111, (int)mobile.Y + 64) };
            var attempt = new CursorInteraction(mobile, assets, new PetSettings(), free, new Random(2));
            attempt.Start(); attempt.Update(0.02, bounds, 1, new Size(128, 139));
            free.Position = new Point(free.Position.X + 250, free.Position.Y);
            attempt.Update(0.42, bounds, 1, new Size(128, 139)); attempt.SynchronizeCursor();
            AlphaTests.Check(!attempt.Attached && free.Moves == 0, "User can escape freely during grab before attach event");
            free.Readable = false; attempt.Update(0.01, bounds, 1, new Size(128, 139));
            AlphaTests.Check(!attempt.Active && !free.Armed, "Cursor read failure cancels cleanly");
            var selection = new FifthChoice(); var simulated = new FakeCursorAdapter();
            var fifth = new InteractionController(mobile, assets, new PetSettings(), selection, new CursorInteraction(mobile, assets, new PetSettings(), simulated, new Random(2)));
            fifth.SetRenderedSize(new Size(128, 139)); fifth.Update(10, bounds, 1);
            AlphaTests.Check(fifth.ActionQueue.AvailableCount == 5 && fifth.Kind == InteractionKind.Cursor && !fifth.Manual && fifth.AutomaticCount == 1, "Cursor is fifth available queued action");
            fifth.CancelCursor();
        }
    }
}
