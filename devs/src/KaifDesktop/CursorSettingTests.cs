using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop
{
    internal static class CursorSettingTests
    {
        private sealed class CursorChoice : Random
        {
            public override int Next(int maxValue) { return Math.Min(4, maxValue - 1); }
        }
        internal static void Run(AssetCatalog assets)
        {
            var json = new JavaScriptSerializer();
            AlphaTests.Check(json.Deserialize<PetSettings>("{}").CursorCaptureEnabled, "Old JSON defaults cursor capture to enabled");
            var saved = json.Deserialize<PetSettings>("{\"CursorCaptureEnabled\":false,\"Speed\":83}");
            saved.Validate();
            AlphaTests.Check(!saved.CursorCaptureEnabled && saved.Speed == 83, "Explicit false survives validation");
            string sandbox = Path.Combine(Path.GetTempPath(), "KaifDesktop-setting-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SettingsStore(sandbox);
                AlphaTests.Check(store.Load().CursorCaptureEnabled, "Missing settings enable cursor capture");
                AlphaTests.Check(store.Save(saved) && !store.Load().CursorCaptureEnabled, "Disabled setting persists to disk");
                saved.CursorCaptureEnabled = true;
                AlphaTests.Check(store.Save(saved) && store.Load().CursorCaptureEnabled, "Re-enabled setting persists to disk");
            }
            finally { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); }

            var bounds = new RectangleF(0, 0, 1700, 850);
            var size = new Size(128, 139);
            foreach (bool enabled in new[] { false, true })
            {
                var values = new PetSettings { CursorCaptureEnabled = enabled };
                var motion = new BasicBehaviorController(values, new Random(1), bounds);
                var pointer = new FakeCursorAdapter();
                var controller = new InteractionController(motion, assets, values, new CursorChoice(), new CursorInteraction(motion, assets, values, pointer, new Random(2)));
                AlphaTests.Check(controller.ActionQueue.IsAvailable(InteractionKind.Cursor) == enabled && (enabled || !controller.ActionQueue.Preview.Contains(InteractionKind.Cursor)), "Initial pool honors saved disabled state before first fill");
                if (!enabled) AlphaTests.Check(!controller.Start(InteractionKind.Cursor, true) && pointer.Begins == 0, "Disabled manual start never arms adapter");
            }
            foreach (string scenario in new[] { "calm", "Sleep", "ChaseTail", "Stretch", "Scratch", "Watch", "Meme", "petting", "drag" })
            {
                var values = new PetSettings();
                var motion = new BasicBehaviorController(values, new Random(1), bounds);
                var pointer = new FakeCursorAdapter { Position = new Point((int)motion.X + 146, (int)motion.Y + 53) };
                var files = new MemeQueue(new Random(1)); files.SetFiles(new[] { "one", "two" });
                var meme = new MemeInteraction(motion, assets, new FakeMemeWindowAdapter(), files);
                meme.Geometry(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040), size);
                var controller = new InteractionController(motion, assets, values, new CursorChoice(),
                    new CursorInteraction(motion, assets, values, pointer, new Random(2)),
                    new WatchInteraction(motion, assets, values, pointer), meme);
                controller.SetRenderedSize(size);
                if (scenario == "petting") controller.TryPet();
                else if (scenario == "drag") controller.BeginDrag();
                else if (scenario != "calm") controller.Start((InteractionKind)Enum.Parse(typeof(InteractionKind), scenario), true);
                controller.Update(.01, bounds, 1);
                var kind = controller.Kind; var name = controller.Animation; double time = controller.AnimationElapsedMs;
                double x = motion.X, y = motion.Y, calm = controller.CalmElapsed;
                int completed = controller.CompletedCount, moves = pointer.Moves, begins = pointer.Begins, releases = pointer.Releases;
                var tail = controller.ActionQueue.Preview;
                AlphaTests.Check(tail.Contains(InteractionKind.Cursor), "Fixture has waiting Cursor items");
                // This fixture collapses several Watch items when Cursor is removed:
                // retain the legal prefix, then repair the newly adjacent triple.
                var retained = tail.Where(k => k != InteractionKind.Cursor).Take(scenario == "Watch" ? 1 : 2).ToArray();
                controller.SetCursorCaptureEnabled(false);
                AlphaTests.Check(controller.ActionQueue.Preview.Length == 12 && !controller.ActionQueue.Preview.Contains(InteractionKind.Cursor) && controller.ActionQueue.Preview.Take(retained.Length).SequenceEqual(retained), "Disable removes every Cursor, retains valid order and refills tail");
                AlphaTests.Check(controller.Kind == kind && controller.Animation == name && controller.AnimationElapsedMs == time && motion.X == x && motion.Y == y && controller.CalmElapsed == calm && controller.CompletedCount == completed, "Non-Cursor action progress preserved: " + scenario);
                AlphaTests.Check(pointer.Moves == moves && pointer.Begins == begins && pointer.Releases == releases, "Unrelated scenarios do not touch cursor adapter");
                controller.RefreshMemeAvailability();
                AlphaTests.Check(!controller.ActionQueue.IsAvailable(InteractionKind.Cursor), "Periodic availability refresh respects disabled setting");
                controller.SetCursorCaptureEnabled(true);
                AlphaTests.Check(controller.ActionQueue.IsAvailable(InteractionKind.Cursor) && controller.ActionQueue.Preview.Contains(InteractionKind.Cursor), "Enable immediately admits Cursor to refilled pool");
                AlphaTests.Check(controller.Kind == kind && controller.AnimationElapsedMs == time, "Enable preserves current action");
                controller.CancelAll("setting test cleanup");
            }
            // Actual attached fake cursor: disabling releases without restoring old coordinates.
            var settings = new PetSettings();
            var moving = new BasicBehaviorController(settings, new Random(1), bounds);
            var fake = new FakeCursorAdapter { Position = new Point((int)moving.X + 111, (int)moving.Y + 64) };
            var action = new CursorInteraction(moving, assets, settings, fake, new Random(3));
            var control = new InteractionController(moving, assets, settings, new CursorChoice(), action);
            control.SetRenderedSize(size);
            AlphaTests.Check(control.Start(InteractionKind.Cursor, true), "Cursor starts before cancellation test");
            for (int i = 0; i < 1000 && !action.Attached; i++) { control.Update(.01, bounds, 1); control.SynchronizeCursor(); }
            AlphaTests.Check(action.Attached && fake.Armed && fake.Moves > 0, "Fixture reaches mouth attachment");
            var position = fake.Position; int writes = fake.Moves;
            control.SetCursorCaptureEnabled(false);
            control.SynchronizeCursor();
            AlphaTests.Check(!control.Active && !action.Attached && !fake.Armed && fake.Position == position && fake.Moves == writes && control.CalmElapsed == 0, "Disable safely releases active capture without cursor snap or further writes");
            AlphaTests.Check(!control.Start(InteractionKind.Cursor, true), "Manual start stays forbidden after cancel");
            control.SetCursorCaptureEnabled(true);
            AlphaTests.Check(control.Start(InteractionKind.Cursor, true), "Second cursor start allowed after enable");
            control.SetCursorCaptureEnabled(false); control.SetCursorCaptureEnabled(true);
            AlphaTests.Check(!control.Start(InteractionKind.Cursor, true), "Toggle retains two-Cursor history and forbids third start");

            var queue = new InteractionQueue(new Random(7), (InteractionKind[])Enum.GetValues(typeof(InteractionKind)));
            InteractionKind? last = null; int run = 0;
            for (int i = 0; i < 5000; i++)
            {
                bool enabled = i % 3 != 0;
                queue.SetAvailable(((InteractionKind[])Enum.GetValues(typeof(InteractionKind))).Where(k => enabled || k != InteractionKind.Cursor), preserveTail: !enabled);
                var previous = last; int series = run;
                foreach (var pending in queue.Preview)
                {
                    series = previous == pending ? series + 1 : 1; previous = pending;
                    AlphaTests.Check(series <= 2 && (enabled || pending != InteractionKind.Cursor), "Toggle/refill obeys max-two across executed history");
                }
                InteractionKind next; AlphaTests.Check(queue.TryPeek(out next), "Queue stays filled");
                queue.RecordStart(next, false); run = last == next ? run + 1 : 1; last = next;
            }
            var spin = assets.Clips["chase-tail"];
            var durations = new[] { 150, 100, 100, 100, 100, 100, 150, 100, 150, 100 };
            AlphaTests.Check(spin.Loop && spin.Frames.Count == 10 && spin.Duration == 1150 && spin.Durations.SequenceEqual(durations), "Approved ten-frame 1150ms spin manifest");
            var animator = new SpriteAnimator(assets);
            double elapsed = 0;
            for (int cycle = 0; cycle < 3; cycle++) for (int index = 0; index < 10; index++)
            {
                animator.SetTime("chase-tail", elapsed);
                AlphaTests.Check(ReferenceEquals(animator.Frame, spin.Frames[index]), "Spin preserves exact frame order over loop boundary");
                elapsed += durations[index];
            }
            var spinControl = new InteractionController(moving, assets, settings, new Random(5));
            spinControl.Start(InteractionKind.ChaseTail, true);
            spinControl.Update(settings.TailCycles * 1.15 - .001, bounds, 1);
            AlphaTests.Check(spinControl.Active, "Configured spin cycles complete in full");
            spinControl.Update(.001, bounds, 1);
            AlphaTests.Check(!spinControl.Active && spinControl.Animation == "idle", "Spin returns to calm after unchanged configured cycle count");
            File.WriteAllText(Diagnostics.TestReport("cursor-setting-self-test.txt"), "PASS: default/legacy JSON true, explicit false, disk persistence, disabled startup/manual start, all waiting Cursor removed with retained tail/refill, non-Cursor progress/Watch/petting/drag unchanged, active attachment released without snap, re-enable, history/max-two through 5000 toggles. Fake cursor only.\r\n");
            File.WriteAllText(Diagnostics.TestReport("spin-self-test.txt"), "PASS: ten frames in manifest order 00..09, durations 150/100/100/100/100/100/150/100/150/100, looping 1150ms, three ordered loops and unchanged TailCycles completion.\r\n");
        }
    }
}
