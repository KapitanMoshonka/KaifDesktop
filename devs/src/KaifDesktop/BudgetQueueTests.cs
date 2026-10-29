using System;
using System.Drawing;
using System.Linq;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop
{
    internal static class BudgetQueueTests
    {
        private sealed class LastChoiceRandom : Random
        {
            internal readonly System.Collections.Generic.List<int> Calls = new System.Collections.Generic.List<int>();
            public override int Next(int maxValue) { Calls.Add(maxValue); return maxValue - 1; }
        }
        private sealed class BridgeRandom : Random
        {
            private int calls;
            public override int Next(int maxValue) { return calls++ == 0 ? maxValue - 1 : 0; }
        }
        internal static void Run(AssetCatalog assets)
        {
            var bounds = new RectangleF(-300, -100, 900, 700);
            var size = new Size(128, 139);
            foreach (double dt in new[] { 0.007, 0.033, 0.19, 0.75, 1.37 })
            foreach (bool late in new[] { false, true })
            {
                var values = new PetSettings { BehaviorVersion = 2, TimingVersion = 1, WatchApproachSpeed = late ? 60 : 140 };
                var motion = new BasicBehaviorController(values, new Random(1), bounds);
                var fake = new FakeCursorAdapter { Position = new Point((int)motion.X + (late ? 250 : 146), (int)motion.Y + 53) };
                var watch = new WatchInteraction(motion, assets, values, fake); watch.Start();
                bool stood = false;
                for (int tick = 0; tick < 10000 && watch.Active; tick++)
                {
                    double before = watch.TotalElapsed;
                    if (tick % 7 == 0) { watch.Update(0, bounds, 1, size); AlphaTests.Check(before == watch.TotalElapsed, "Watch pause excludes budget"); }
                    watch.Update(dt, bounds, 1, size);
                    if (watch.Phase == "watch") AlphaTests.Check(watch.Animation == "watch-look" && !watch.Blinking, "No frontal substitution at any watch time");
                    if (watch.Phase == "stand") { stood = true; AlphaTests.Check(watch.TotalElapsed >= 20 - assets.Clips["stand-up"].Duration / 1000.0 - 0.000001, "Stand reserves exact end of total budget"); }
                }
                AlphaTests.Check(!watch.Active && Math.Abs(watch.TotalElapsed - 20) < 0.000001, "Whole watch budget exactly 20 for varied dt/approach");
                AlphaTests.Check(stood || dt > 1, "Stand-up not omitted");
                motion = new BasicBehaviorController(values, new Random(1), bounds);
                fake = new FakeCursorAdapter { Position = new Point((int)motion.X + (late ? 250 : 111), (int)motion.Y + 64) };
                var cursor = new CursorInteraction(motion, assets, values, fake, new Random(2)); cursor.Start();
                bool attached = false;
                for (int tick = 0; tick < 10000 && cursor.Active; tick++)
                {
                    if (tick % 7 == 0) { double before = cursor.TotalElapsed; cursor.Update(0, bounds, 1, size); AlphaTests.Check(before == cursor.TotalElapsed, "Zero active delta does not consume cursor budget"); }
                    cursor.Update(dt, bounds, 1, size); attached |= cursor.Attached; cursor.SynchronizeCursor();
                }
                AlphaTests.Check(attached && !cursor.Active && !fake.Armed && Math.Abs(cursor.TotalElapsed - 15) < 0.000001, "Whole capture budget exactly 15 for varied dt and chase");
            }
            var old = new PetSettings { Speed = 87, BehaviorVersion = 2, WatchSeconds = 5, CursorPullSeconds = 3, WatchTotalSeconds = 99, CursorTotalSeconds = 99 };
            old.Validate();
            AlphaTests.Check(old.WatchTotalSeconds == 20 && old.CursorTotalSeconds == 15 && old.Speed == 87 && old.TimingVersion == 1, "One-time duration migration preserves other settings");
            old.WatchTotalSeconds = 25; old.CursorTotalSeconds = 18; old.Validate();
            AlphaTests.Check(old.WatchTotalSeconds == 25 && old.CursorTotalSeconds == 18, "Later custom total budgets survive validation");

            var kinds = (InteractionKind[])Enum.GetValues(typeof(InteractionKind));
            var controlled = new LastChoiceRandom();
            var initialValues = new PetSettings { BehaviorVersion = 2, TimingVersion = 1 };
            var initialMotion = new BasicBehaviorController(initialValues, new Random(1), bounds);
            var initialPointer = new FakeCursorAdapter();
            var initialFiles = new MemeQueue(new Random(1)); initialFiles.SetFiles(new[] { "first", "second" });
            var initialMeme = new MemeInteraction(initialMotion, assets, new FakeMemeWindowAdapter(), initialFiles);
            initialMeme.Geometry(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040), size);
            var initialController = new InteractionController(initialMotion, assets, initialValues, controlled,
                new CursorInteraction(initialMotion, assets, initialValues, initialPointer, new Random(1)),
                new WatchInteraction(initialMotion, assets, initialValues, initialPointer), initialMeme);
            AlphaTests.Check(controlled.Calls[0] == 7 && initialController.ActionQueue.AvailableCount == 7 && initialController.ActionQueue.Preview[0] == InteractionKind.Meme, "Very first draw includes seven kinds after geometry is set; controlled RNG, no probability assertion");
            var sameTail = initialController.ActionQueue.Preview; int sameCalls = controlled.Calls.Count;
            initialController.RefreshMemeAvailability();
            AlphaTests.Check(controlled.Calls.Count == sameCalls && sameTail.SequenceEqual(initialController.ActionQueue.Preview), "Unchanged availability never reshuffles preview");
            controlled = new LastChoiceRandom();
            var changed = new InteractionQueue(controlled, kinds.Where(k => k != InteractionKind.Meme));
            changed.RecordStart(InteractionKind.Stretch, true); changed.RecordStart(InteractionKind.Stretch, true);
            int beforeAddition = controlled.Calls.Count;
            changed.SetAvailable(kinds);
            AlphaTests.Check(controlled.Calls.Count == beforeAddition + 12 && changed.Preview[0] == InteractionKind.Meme && !changed.CanStart(InteractionKind.Stretch), "Pool addition rebuilds all twelve slots using new action, preserving actual last/run");
            for (int n = 0; n < 40; n++) { InteractionKind next; AlphaTests.Check(changed.TryPeek(out next), "Refill available"); changed.RecordStart(next, false); }
            AlphaTests.Check(changed.Preview.Contains(InteractionKind.Meme), "Controlled new action remains eligible after refills");
            var queue = new InteractionQueue(new Random(123), kinds);
            var skipQueue = new InteractionQueue(new LastChoiceRandom(), kinds);
            var originalTail = skipQueue.Preview; InteractionKind skipped;
            AlphaTests.Check(skipQueue.TrySkip(out skipped) && skipped == originalTail[0] && skipQueue.Preview.Length == originalTail.Length && skipQueue.Preview.Take(11).SequenceEqual(originalTail.Skip(1)), "Skip drops first future action, preserves valid tail, refills one slot");
            var bridge = new InteractionQueue(new BridgeRandom(), new[] { InteractionKind.Sleep, InteractionKind.ChaseTail });
            bridge.RecordStart(InteractionKind.Sleep, true);
            AlphaTests.Check(bridge.Preview.Take(3).SequenceEqual(new[] { InteractionKind.ChaseTail, InteractionKind.Sleep, InteractionKind.Sleep }), "Prepared history bridge");
            bridge.TrySkip(out skipped);
            AlphaTests.Check(skipped == InteractionKind.ChaseTail && bridge.Preview.Take(2).SequenceEqual(new[] { InteractionKind.Sleep, InteractionKind.ChaseTail }), "Skip repairs new head against actual last/run");
            bridge.RecordStart(InteractionKind.Sleep, true);
            AlphaTests.Check(!bridge.CanStart(InteractionKind.Sleep), "Skipped action never resets actual repeat history");
            var singleSkip = new InteractionQueue(new Random(1), new[] { InteractionKind.Sleep });
            AlphaTests.Check(singleSkip.TrySkip(out skipped) && singleSkip.Preview.Length == 2, "Single available action can refill permitted future pair");
            singleSkip.RecordStart(InteractionKind.Sleep, true); singleSkip.RecordStart(InteractionKind.Sleep, true);
            AlphaTests.Check(!singleSkip.CanSkip && !singleSkip.TrySkip(out skipped), "Blocked singleton has no skip and cannot bypass repetition rule");
            singleSkip.SetAvailable(new InteractionKind[0]);
            AlphaTests.Check(!singleSkip.CanSkip && !singleSkip.TrySkip(out skipped), "Empty pool skip disabled");
            var activeSkip = new InteractionController(initialMotion, assets, initialValues, new LastChoiceRandom());
            activeSkip.Start(InteractionKind.Stretch, true); double elapsedBeforeSkip = activeSkip.AnimationElapsedMs;
            int completedBeforeSkip = activeSkip.CompletedCount;
            AlphaTests.Check(activeSkip.SkipNext() && activeSkip.Active && activeSkip.Kind == InteractionKind.Stretch && activeSkip.Manual && activeSkip.AnimationElapsedMs == elapsedBeforeSkip && activeSkip.CompletedCount == completedBeforeSkip, "Skipping upcoming action never interrupts or advances active scenario");
            InteractionKind? previous = null; int run = 0; bool doubleSeen = false, repeatedOutsideBag = false;
            for (int i = 0; i < 20000; i++)
            {
                InteractionKind kind;
                if (i % 11 == 0)
                {
                    var manualOptions = kinds.Where(queue.CanStart).ToArray(); kind = manualOptions[i % manualOptions.Length];
                    queue.RecordStart(kind, true);
                }
                else { AlphaTests.Check(queue.TryPeek(out kind), "Queue contains a valid next action"); queue.RecordStart(kind, false); }
                run = previous == kind ? run + 1 : 1; previous = kind;
                AlphaTests.Check(run <= 2, "Actual manual+automatic history never has triple repeat");
                doubleSeen |= run == 2;
                var tailPrevious = previous; int tailRun = run;
                foreach (var pending in queue.Preview)
                {
                    tailRun = tailPrevious == pending ? tailRun + 1 : 1; tailPrevious = pending;
                    AlphaTests.Check(tailRun <= 2, "Buffered tail is valid across refill/manual boundary");
                }
                AlphaTests.Check(queue.Preview.Length == 12, "Queue populated ahead of use");
                repeatedOutsideBag |= queue.Preview.Distinct().Count() < 6;
            }
            AlphaTests.Check(doubleSeen && repeatedOutsideBag, "Single/double repeats permitted; not a shuffled bag");
            queue.SetAvailable(new[] { InteractionKind.Sleep });
            if (queue.CanStart(InteractionKind.Sleep)) queue.RecordStart(InteractionKind.Sleep, true);
            if (queue.CanStart(InteractionKind.Sleep)) queue.RecordStart(InteractionKind.Sleep, true);
            InteractionKind ignored;
            AlphaTests.Check(!queue.CanStart(InteractionKind.Sleep) && !queue.TryPeek(out ignored), "Only one action after two repeats must wait");
            queue.SetAvailable(new[] { InteractionKind.Sleep, InteractionKind.Stretch });
            AlphaTests.Check(queue.TryPeek(out ignored) && ignored == InteractionKind.Stretch, "Availability change repairs blocked tail");
            var valuesForWait = new PetSettings { BehaviorVersion = 2 };
            var motionForWait = new BasicBehaviorController(valuesForWait, new Random(1), bounds);
            var controller = new InteractionController(motionForWait, assets, valuesForWait, new Random(1));
            controller.ActionQueue.SetAvailable(new[] { InteractionKind.Stretch });
            for (int n = 0; n < 2; n++) { AlphaTests.Check(controller.Start(InteractionKind.Stretch, true), "Manual first/second repeat allowed"); controller.Update(assets.Clips["stretch-yawn"].Duration / 1000.0, bounds, 1); }
            AlphaTests.Check(!controller.Start(InteractionKind.Stretch, true), "Manual third repeat refused");
            controller.Update(100, bounds, 1);
            AlphaTests.Check(!controller.Active && controller.AutomaticCount == 0, "Blocked queue remains calm without hang or violation");
        }
    }
}
