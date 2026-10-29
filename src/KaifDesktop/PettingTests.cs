using System;
using System.Drawing;
using System.Linq;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop
{
    internal static class PettingTests
    {
        internal static void Run(AssetCatalog assets)
        {
            var bounds = new RectangleF(0, 0, 1700, 850);
            double duration = assets.Clips["petting"].Duration / 1000.0;
            AlphaTests.Check(duration == .97 && !assets.Clips["petting"].Loop && assets.Clips["petting"].Frames.Count == 4, "Original four-frame finite petting clip");
            foreach (bool walking in new[] { true, false })
            {
                var settings = new PetSettings { BehaviorVersion = 2, TimingVersion = 1 };
                var motion = new BasicBehaviorController(settings, new Random(1), bounds);
                if (!walking) motion.ResumeCalm();
                var control = new InteractionController(motion, assets, settings, new Random(2));
                control.Update(.25, bounds, 1);
                var queue = control.ActionQueue.Preview; double calm = control.CalmElapsed, x = motion.X, y = motion.Y;
                AlphaTests.Check(control.TryPet() && control.Petting && control.Active && control.Kind == null && !motion.Walking, "Calm click stops walking or idle immediately");
                control.Update(.2, bounds, 1); double elapsed = control.AnimationElapsedMs;
                AlphaTests.Check(!control.TryPet() && control.AnimationElapsedMs == elapsed, "Repeated petting never restarts or queues");
                control.Update(0, bounds, 1);
                AlphaTests.Check(control.AnimationElapsedMs == elapsed && control.CalmElapsed == calm && motion.X == x && motion.Y == y, "Petting freezes movement and calm clock, zero delta freezes animation");
                control.Update(duration - .2, bounds, 1);
                AlphaTests.Check(!control.Active && !control.Petting && control.Animation == "idle" && !motion.Walking && control.CalmElapsed == 0 && motion.X == x && motion.Y == y, "One clip finishes into fresh idle with reset calm timer");
                AlphaTests.Check(queue.SequenceEqual(control.ActionQueue.Preview) && control.AutomaticCount == 0, "Petting neither consumes nor reorders interaction queue");
                control.Update(9.999, bounds, 1); AlphaTests.Check(control.AutomaticCount == 0, "New full calm interval after petting");
                control.Update(.001, bounds, 1); AlphaTests.Check(control.AutomaticCount == 1, "Roaming and ordinary scheduler resume");
            }
            foreach (InteractionKind kind in Enum.GetValues(typeof(InteractionKind)))
            {
                var settings = new PetSettings { BehaviorVersion = 2, TimingVersion = 1 };
                var motion = new BasicBehaviorController(settings, new Random(1), bounds);
                var pointer = new FakeCursorAdapter();
                var files = new MemeQueue(new Random(1)); files.SetFiles(new[] { "a", "b" });
                var meme = new MemeInteraction(motion, assets, new FakeMemeWindowAdapter(), files);
                meme.Geometry(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040), new Size(128, 139));
                var control = new InteractionController(motion, assets, settings, new Random(2), new CursorInteraction(motion, assets, settings, pointer, new Random(1)), new WatchInteraction(motion, assets, settings, pointer), meme);
                AlphaTests.Check(control.Start(kind, true), "Scenario starts before petting rejection");
                var queue = control.ActionQueue.Preview; double elapsed = control.AnimationElapsedMs;
                AlphaTests.Check(!control.TryPet() && control.Kind == kind && !control.Petting && control.Active && control.AnimationElapsedMs == elapsed && queue.SequenceEqual(control.ActionQueue.Preview), "Petting cannot interrupt any interactive scenario: " + kind);
                control.CancelAll("test cleanup");
            }
        }
    }
}
