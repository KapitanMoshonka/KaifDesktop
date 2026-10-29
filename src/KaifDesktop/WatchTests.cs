using System;
using System.Drawing;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop
{
    internal static class WatchTests
    {
        private sealed class SixthChoice : Random
        {
            internal int Maximum;
            public override int Next(int maximum) { Maximum = maximum; return maximum - 1; }
        }
        internal static void Run(AssetCatalog assets)
        {
            var settings = new PetSettings { BehaviorVersion = 2 };
            var bounds = new RectangleF(-300, -100, 900, 700);
            foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                var size = new Size((int)Math.Round(128 * dpi), (int)Math.Round(208 * 2 / 3.0 * dpi));
                var motion = new BasicBehaviorController(settings, new Random(1), bounds);
                var pointer = new FakeCursorAdapter { BeginAllowed = false, FailMove = true };
                pointer.Position = new Point((int)Math.Round(motion.X + size.Width + 18 * dpi), (int)Math.Round(motion.Y + size.Height * 0.38));
                var watch = new WatchInteraction(motion, assets, settings, pointer);
                AlphaTests.Check(watch.Start(), "Read-only watch does not need capture permission");
                watch.Update(0.01, bounds, dpi, size);
                AlphaTests.Check(watch.Phase == "sit" && watch.Animation == "sit-down", "Approach arrives beside pointer");
                watch.Update(assets.Clips["sit-down"].Duration / 1000.0, bounds, dpi, size);
                AlphaTests.Check(watch.Phase == "watch", "Full sit-down leads to watch");
                double x = motion.X, y = motion.Y;
                bool blink = false, look = false;
                for (int i = 0; i < 400; i++)
                {
                    pointer.Position = new Point((int)(x + size.Width / 2.0 + 200 * Math.Sin(i / 10.0)), (int)(y + size.Height * 0.38 - 200 * Math.Cos(i / 10.0)));
                    watch.Update(0.01, bounds, dpi, size);
                    AlphaTests.Check(motion.X == x && motion.Y == y, "Seated watcher never pursues moved pointer");
                    blink |= watch.Blinking; look |= watch.Animation == "watch-look";
                    AlphaTests.Check(watch.LookIndex >= 0 && watch.LookIndex < 8, "Valid seated look index");
                }
                AlphaTests.Check(!blink && look && watch.Phase == "watch", "Only directional poses, never frontal blink substitution");
                double before = watch.PhaseElapsed;
                watch.Update(0, bounds, dpi, size);
                AlphaTests.Check(watch.PhaseElapsed == before, "No elapsed progress while paused");
                double untilStand = settings.WatchTotalSeconds - assets.Clips["stand-up"].Duration / 1000.0 - watch.TotalElapsed;
                watch.Update(untilStand - 0.001, bounds, dpi, size);
                AlphaTests.Check(watch.Phase == "watch", "Watch fills remaining total budget");
                watch.Update(0.001, bounds, dpi, size);
                AlphaTests.Check(watch.Phase == "stand" && watch.Animation == "stand-up", "Stand-up starts with reserved completion time");
                watch.Update(assets.Clips["stand-up"].Duration / 1000.0, bounds, dpi, size);
                AlphaTests.Check(!watch.Active && Math.Abs(watch.TotalElapsed - 20) < 0.000001, "Whole watch scenario is exactly twenty active seconds");
                AlphaTests.Check(pointer.Begins == 0 && pointer.Moves == 0 && pointer.Releases == 0 && !pointer.Armed, "Watch never arms, moves or releases cursor");
            }
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                AlphaTests.Check(WatchInteraction.Direction(Math.Sin(angle) * 100, -Math.Cos(angle) * 100, 4, 12) == i, "All eight seated look directions");
            }
            AlphaTests.Check(WatchInteraction.Direction(1, 1, 3, 12) == 3, "Dead zone retains gaze near coincident centers");
            Func<double, int, int> directionAt = (angle, previous) => WatchInteraction.Direction(Math.Sin(angle * Math.PI / 180) * 100, -Math.Cos(angle * Math.PI / 180) * 100, previous, 12);
            int chosen = 0;
            for (int i = 0; i < 100; i++) chosen = directionAt(i % 2 == 0 ? 22.4 : 22.6, chosen);
            AlphaTests.Check(chosen == 0 && directionAt(32, chosen) == 1 && directionAt(350, 0) == 0, "Sector hysteresis prevents jitter and handles wrap");
            var slowSettings = new PetSettings { BehaviorVersion = 2, WatchApproachSeconds = 0.5, WatchApproachSpeed = 20 };
            var moving = new BasicBehaviorController(slowSettings, new Random(1), bounds);
            var reader = new FakeCursorAdapter { Position = new Point(550, 500) };
            var timeout = new WatchInteraction(moving, assets, slowSettings, reader);
            timeout.Start(); timeout.Update(0.5, bounds, 1, new Size(128, 139));
            AlphaTests.Check(!timeout.Active, "Approach timeout cancels");
            reader.Position = new Point(-10000, -10000);
            double oldX = moving.X, oldY = moving.Y;
            timeout.Start(); timeout.Update(0.01, bounds, 1, new Size(128, 139));
            AlphaTests.Check(!timeout.Active && moving.X == oldX && moving.Y == oldY, "Unreachable pointer cancels without teleport");
            foreach (Point pointer in new[] { new Point(-299, -99), new Point(726, -99), new Point(-299, 738), new Point(726, 738) })
            {
                var motion = new BasicBehaviorController(settings, new Random(1), bounds);
                reader.Position = pointer;
                var edge = new WatchInteraction(motion, assets, settings, reader); edge.Start();
                for (int tick = 0; tick < 800 && edge.Active && edge.Phase == "approach"; tick++)
                {
                    edge.Update(0.01, bounds, 1, new Size(128, 139));
                    AlphaTests.Check(motion.X >= bounds.Left && motion.X <= bounds.Right && motion.Y >= bounds.Top && motion.Y <= bounds.Bottom, "Approach remains in bounds near corners");
                }
                AlphaTests.Check(edge.Active && edge.Phase == "sit", "Corner has reachable sitting position"); edge.Cancel();
            }
            var motionForPool = new BasicBehaviorController(settings, new Random(1), bounds);
            reader.Position = new Point(300, 300);
            var watchForPool = new WatchInteraction(motionForPool, assets, settings, reader);
            var selection = new SixthChoice();
            var controller = new InteractionController(motionForPool, assets, settings, selection, null, watchForPool);
            controller.SetRenderedSize(new Size(128, 139)); controller.Update(10, bounds, 1);
            AlphaTests.Check(controller.ActionQueue.AvailableCount == 5 && controller.Kind == InteractionKind.Watch && !controller.Manual, "Watch is available queued choice");
            for (int tick = 0; tick < 4000 && controller.Active; tick++) controller.Update(0.01, bounds, 1);
            AlphaTests.Check(!controller.Active && controller.Animation == "idle" && controller.CalmElapsed < 0.001, "Watch returns to calm and resets interval");
            controller.Update(1, bounds, 1); AlphaTests.Check(!controller.Active, "No immediate action after watch completion");
            controller.Start(InteractionKind.Watch, true); controller.CancelWatch();
            AlphaTests.Check(!controller.Active && reader.Moves == 0 && reader.Begins == 0, "Cancellation has no cursor side effects");
        }
    }
}
