using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;
using KaifDesktop.UI;

namespace KaifDesktop
{
    internal static class DragTests
    {
        internal static void Run(AssetCatalog assets)
        {
            AlphaTests.Check(assets.Clips["dragged"].Loop && assets.Clips["dragged"].Frames.Count == 4, "Existing dragged is a four-frame loop");
            var gesture = new PetPointerGesture();
            AlphaTests.Check(!gesture.Begin(new Point(100, 100), new Point(25, 30), PetClickMode.None, new Size(8, 8)), "Forbidden down never starts a latent gesture");
            AlphaTests.Check(gesture.Begin(new Point(100, 100), new Point(25, 30), PetClickMode.Wake, new Size(8, 8)) && !gesture.BeyondThreshold(new Point(103, 100)) && gesture.Snapshot == PetClickMode.Wake, "Pending click uses state snapshot and system-size rectangle");
            AlphaTests.Check(gesture.BeyondThreshold(new Point(105, 100)), "Threshold crossing starts drag");
            gesture.StartDrag(); AlphaTests.Check(gesture.Position(new Point(180, 200)) == new Point(155, 170), "Original grab offset preserved across pose change");
            gesture.Clear(); AlphaTests.Check(!gesture.Pressed && !gesture.Dragging && !gesture.BeyondThreshold(new Point(500, 500)), "Capture loss clears all pending state");
            var bounds = new RectangleF(-200, -100, 1600, 800);
            foreach (string scenario in new[] { "calm", "Sleep", "ChaseTail", "Stretch", "Scratch", "Watch", "petting" })
            {
                var settings = new PetSettings { BehaviorVersion = 2, TimingVersion = 1 };
                var motion = new BasicBehaviorController(settings, new Random(1), bounds);
                var cursor = new FakeCursorAdapter();
                var control = new InteractionController(motion, assets, settings, new Random(2), watching: new WatchInteraction(motion, assets, settings, cursor));
                if (scenario == "petting") control.TryPet();
                else if (scenario != "calm") AlphaTests.Check(control.Start((InteractionKind)Enum.Parse(typeof(InteractionKind), scenario), true), "Allowed scenario starts");
                var queue = control.ActionQueue.Preview; double x = motion.X, y = motion.Y;
                AlphaTests.Check(control.BeginDrag() && control.Dragging && control.Animation == "dragged" && !motion.Walking && !control.Petting && motion.X == x && motion.Y == y, "Drag cancels allowed action without initial jump: " + scenario);
                control.Update(2, bounds, 1); AlphaTests.Check(control.AnimationElapsedMs == 2000, "Dragged loop advances while held");
                control.MoveDrag(new Point(-10000, 10000), bounds);
                AlphaTests.Check(motion.X == bounds.Left && motion.Y == bounds.Bottom, "Dragging clamps entire sprite to main work area bounds");
                AlphaTests.Check(control.EndDrag() && !control.Active && control.Animation == "idle" && control.CalmElapsed == 0 && !control.EndDrag() && queue.SequenceEqual(control.ActionQueue.Preview), "Release/capture loss returns fresh idle once, preserving queue");
            }
            foreach (InteractionKind kind in new[] { InteractionKind.Cursor, InteractionKind.Meme })
            {
                var settings = new PetSettings { BehaviorVersion = 2, TimingVersion = 1 };
                var size = new Size(128, 139); var screen = new Rectangle(0, 0, 1920, 1080); var work = new Rectangle(0, 0, 1920, 1040);
                var moving = new BasicBehaviorController(settings, new Random(1), bounds);
                var pointer = new FakeCursorAdapter { Position = new Point((int)moving.X + 146, (int)moving.Y + 64) };
                var files = new MemeQueue(new Random(1)); files.SetFiles(new[] { "a", "b" });
                var meme = new MemeInteraction(moving, assets, new FakeMemeWindowAdapter(), files); meme.Geometry(screen, work, size);
                var cursor = new CursorInteraction(moving, assets, settings, pointer, new Random(1));
                var control = new InteractionController(moving, assets, settings, new Random(2), cursor, memes: meme); control.SetRenderedSize(size);
                control.Start(kind, true); var phases = new HashSet<string>();
                for (int tick = 0; tick < 5000 && control.Active; tick++)
                {
                    string phase = kind == InteractionKind.Meme ? meme.Phase : cursor.Phase; phases.Add(phase);
                    var before = control.ActionQueue.Preview;
                    AlphaTests.Check(!control.CanDrag && !control.BeginDrag() && control.Kind == kind && before.SequenceEqual(control.ActionQueue.Preview), "Drag forbidden throughout " + kind + ":" + phase);
                    control.Update(.01, bounds, 1); control.SynchronizeCursor(); control.SynchronizeMeme(1);
                }
                AlphaTests.Check((kind == InteractionKind.Meme ? new[] { "leave", "prepare", "grab", "pull", "release" } : new[] { "chase", "grab", "pull", "release" }).All(phases.Contains), "Every forbidden phase exercised: " + kind);
                AlphaTests.Check(!control.Dragging && !control.Active, "No delayed drag after forbidden scenario completes");
            }
        }
    }
}
