using System;
using System.Drawing;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;

namespace KaifDesktop
{
    internal static class InteractionTests
    {
        private sealed class FixedSelection : Random
        {
            private readonly int selection;
            internal FixedSelection(int value) { selection = value; }
            public override int Next(int maximum) { return selection % maximum; }
        }
        internal static void Run(AssetCatalog assets)
        {
            var settings = new PetSettings { BehaviorVersion = 2 };
            var invalid = new PetSettings { CalmIntervalSeconds = double.NaN, SleepSeconds = 0, TailCycles = 0, ScratchCycles = 101 };
            invalid.Validate();
            AlphaTests.Check(invalid.CalmIntervalSeconds == 10 && invalid.SleepSeconds == 60 && invalid.TailCycles == 3 && invalid.ScratchCycles == 4, "Interaction setting defaults and validation");
            var bounds = new RectangleF(-100, -100, 900, 700);
            Func<InteractionKind, InteractionController> make = kind =>
            {
                var motion = new BasicBehaviorController(settings, new Random(3), bounds);
                return new InteractionController(motion, assets, settings, new FixedSelection((int)kind));
            };
            Func<string, double> duration = name => assets.Clips[name].Duration / 1000.0;
            foreach (InteractionKind kind in new[] { InteractionKind.Sleep, InteractionKind.ChaseTail, InteractionKind.Stretch, InteractionKind.Scratch })
            {
                var c = make(kind);
                c.Update(9.999, bounds, 1);
                AlphaTests.Check(!c.Active && Math.Abs(c.CalmElapsed - 9.999) < 0.000001, "Calm timer sums Walking and Idle");
                c.Update(0.001, bounds, 1);
                AlphaTests.Check(c.Active && c.Kind == kind && !c.Manual && c.AutomaticCount == 1, "Exactly 10 seconds selects only enabled random interaction");
                double frozen = c.CalmElapsed;
                c.Update(0.1, bounds, 1);
                AlphaTests.Check(c.CalmElapsed == frozen && !c.Start(InteractionKind.Sleep, true), "Calm freezes during scenario; no overlapping manual action");
            }
            var sleep = make(InteractionKind.Sleep);
            AlphaTests.Check(sleep.Start(InteractionKind.Sleep, true) && !sleep.Wake(), "Manual sleep cannot wake during lie-down");
            sleep.Update(duration("lie-down") - 0.001, bounds, 1);
            AlphaTests.Check(sleep.Animation == "lie-down", "All lie-down frames retained");
            sleep.Update(0.001, bounds, 1);
            AlphaTests.Check(sleep.Sleeping && sleep.AnimationElapsedMs < 0.000001, "Sleep timer begins only after lie-down");
            sleep.Update(59.999, bounds, 1);
            AlphaTests.Check(sleep.Sleeping, "Sleep lasts full 60 seconds");
            sleep.Update(0.001, bounds, 1);
            AlphaTests.Check(sleep.Animation == "wake-up" && !sleep.Wake(), "Timed wake-up transitions once");
            sleep.Update(duration("wake-up"), bounds, 1);
            AlphaTests.Check(!sleep.Active && sleep.CompletedCount == 1 && sleep.CalmElapsed < 0.000001 && sleep.Animation == "idle", "Wake-up completes to fresh calm idle");
            sleep.Start(InteractionKind.Sleep, true);
            sleep.Update(duration("lie-down") + 0.7, bounds, 1);
            AlphaTests.Check(sleep.Wake() && sleep.Animation == "wake-up" && !sleep.Wake(), "Early wake is idempotent and plays wake-up");
            sleep.Update(duration("wake-up"), bounds, 1);
            AlphaTests.Check(sleep.AutomaticCount == 0, "Manual runs do not count as background selections");
            foreach (InteractionKind kind in new[] { InteractionKind.ChaseTail, InteractionKind.Stretch, InteractionKind.Scratch })
            {
                var c = make(kind); c.Start(kind, true);
                string[] names = kind == InteractionKind.ChaseTail ? new[] { "chase-tail" } : kind == InteractionKind.Stretch ? new[] { "stretch-yawn" } : new[] { "sit-down", "ear-scratch-enter", "ear-scratch-loop", "ear-scratch-exit", "stand-up" };
                foreach (string name in names)
                {
                    AlphaTests.Check(c.Animation == name, "Explicit scenario order: " + name);
                    int cycles = name == "chase-tail" ? 3 : name == "ear-scratch-loop" ? 4 : 1;
                    for (int cycle = 0; cycle < cycles; cycle++)
                    {
                        c.Update(duration(name) - 0.001, bounds, 1);
                        AlphaTests.Check(c.Animation == name && c.Active, "Full cycle retained for " + name);
                        c.Update(0.001, bounds, 1);
                        if (cycle + 1 < cycles) AlphaTests.Check(c.Animation == name, "Exact configured cycle count");
                    }
                }
                AlphaTests.Check(!c.Active && c.CompletedCount == 1 && c.CalmElapsed < 0.000001 && c.Animation == "idle", "Scenario ends and resets calm timer");
                c.Update(9.999, bounds, 1); AlphaTests.Check(!c.Active, "Full new calm interval after completion");
                c.Update(0.001, bounds, 1); AlphaTests.Check(c.Active && !c.Manual, "Background restarts only after fresh interval");
            }
            var motionCheck = new BasicBehaviorController(settings, new Random(5), bounds);
            var stationary = new InteractionController(motionCheck, assets, settings, new Random(4));
            double x = motionCheck.X, y = motionCheck.Y;
            stationary.Start(InteractionKind.Scratch, true);
            stationary.Update(3, bounds, 1);
            AlphaTests.Check(x == motionCheck.X && y == motionCheck.Y, "No locomotion while interacting");
            double time = stationary.AnimationElapsedMs;
            stationary.Update(0, bounds, 1);
            AlphaTests.Check(time == stationary.AnimationElapsedMs, "Paused zero delta cannot progress scenario");
            settings.CalmIntervalSeconds = 2; settings.SleepSeconds = 1;
            settings.TailCycles = 1; settings.ScratchCycles = 2;
            foreach (InteractionKind kind in new[] { InteractionKind.Sleep, InteractionKind.ChaseTail, InteractionKind.Stretch, InteractionKind.Scratch })
            {
                var c = make(kind);
                c.Update(2000, bounds, 1);
                AlphaTests.Check(c.CompletedCount > 100 && c.AutomaticCount > 100 && c.CalmElapsed <= 2.00001, "Long simulation: no hanging states or immediate retrigger loop");
            }
        }
    }
}
