using System;
using System.Drawing;
using System.IO;
using System.Linq;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;
namespace KaifDesktop
{
    internal static class MemeTests
    {
        internal static void Run(AssetCatalog assets)
        {
            var queue = new MemeQueue(new Random(12)); queue.SetFiles(new[] { "a", "b", "c" });
            string last = null;
            for (int i = 0; i < 5000; i++)
            {
                string file; AlphaTests.Check(queue.TryPeek(out file) && file != last, "No adjacent meme repeat across refill");
                queue.RecordShown(file); last = file;
                string previous = last; foreach (var pending in queue.Preview) { AlphaTests.Check(previous != pending, "No duplicate in queued tail"); previous = pending; }
            }
            queue = new MemeQueue(new Random(1)); queue.SetFiles(new[] { "only" }); string first;
            AlphaTests.Check(queue.TryPeek(out first), "One file initially allowed"); queue.RecordShown(first);
            AlphaTests.Check(!queue.TryPeek(out first), "One already shown file must wait");
            queue.SetFiles(new[] { "only", "new" }); AlphaTests.Check(queue.TryPeek(out first) && first == "new", "Added file unblocks without repeat");
            queue.SetFiles(new string[0]); AlphaTests.Check(!queue.TryPeek(out first), "Empty source unavailable");
            string directory = Path.Combine(Path.GetTempPath(), "Kaif-meme-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                using (var bitmap = new Bitmap(8, 8)) bitmap.Save(Path.Combine(directory, "valid.png"));
                File.WriteAllText(Path.Combine(directory, "bad.jpg"), "not an image");
                Directory.CreateDirectory(Path.Combine(directory, "nested")); File.Copy(Path.Combine(directory, "valid.png"), Path.Combine(directory, "nested", "hidden.png"));
                queue.Scan(directory); AlphaTests.Check(queue.Files.Length == 1, "Decode validation and no recursive enumeration");
                queue.TryPeek(out first); queue.RecordShown(first); AlphaTests.Check(!queue.TryPeek(out first), "Real singleton waits");
                using (var bitmap = new Bitmap(9, 9)) bitmap.Save(Path.Combine(directory, "valid.png"));
                queue.Scan(directory); AlphaTests.Check(queue.TryPeek(out first), "Changed singleton content is a new available file");
            }
            finally { Directory.Delete(directory, true); }
            var screen = new Rectangle(0, 0, 1920, 1080); var work = new Rectangle(0, 0, 1920, 1040); var size = new Size(128, 139);
            foreach (bool left in new[] { false, true })
            {
                var settings = new PetSettings { BehaviorVersion = 2, TimingVersion = 1 };
                var motion = new BasicBehaviorController(settings, new Random(1), DesktopAreaService.MovementBounds(size, work));
                motion.MoveTo(left ? 100 : 1600, 150, DesktopAreaService.MovementBounds(size, work));
                var windows = new FakeMemeWindowAdapter(); var files = new MemeQueue(new Random(1)); files.SetFiles(new[] { "one", "two" });
                var scene = new MemeInteraction(motion, assets, windows, files); scene.Geometry(screen, work, size);
                scene.Geometry(screen, work, size, false, false);
                AlphaTests.Check(!scene.Available, "No free side edge disables meme scene");
                scene.Geometry(screen, work, size);
                AlphaTests.Check(scene.Start(), "Meme available"); bool outside = false, held = false;
                for (int i = 0; i < 4000 && scene.Active; i++)
                {
                    scene.Update(0.02, 1); scene.SynchronizeWindow(1);
                    outside |= motion.X + size.Width < screen.Left || motion.X > screen.Right;
                    held |= scene.Phase == "pull";
                }
                AlphaTests.Check(outside && held && !scene.Active && !scene.Faulted && windows.Opens == 1 && work.Contains(windows.Bounds) && files.Preview[0] == "two", "Meme exits, pulls owned window, releases visible and advances shown history");
                foreach (string failure in new[] { "open", "move", "pause", "cancel", "closed" })
                {
                    windows = new FakeMemeWindowAdapter { FailOpen = failure == "open", FailMove = failure == "move" };
                    files = new MemeQueue(new Random(1)); files.SetFiles(new[] { "single" });
                    motion.MoveTo(1600, 150, DesktopAreaService.MovementBounds(size, work));
                    scene = new MemeInteraction(motion, assets, windows, files); scene.Geometry(screen, work, size); scene.Start();
                    for (int i = 0; i < 2500 && scene.Active; i++)
                    {
                        scene.Update(0.02, 1); scene.SynchronizeWindow(1);
                        if ((failure == "pause" || failure == "cancel") && scene.Phase == "pull") scene.Cancel();
                        if (failure == "closed" && scene.Phase == "grab") windows.Own = false;
                    }
                    AlphaTests.Check(!scene.Active && scene.Phase == "done" && motion.X >= work.Left && motion.X <= work.Right - size.Width, "Error/cancel returns pet safely: " + failure);
                    string next;
                    bool displayed = failure != "open" && failure != "closed";
                    AlphaTests.Check(files.TryPeek(out next) != displayed, "History includes windows actually shown by completion or restoration");
                    if (windows.Valid) AlphaTests.Check(work.Contains(windows.Bounds), "Created window restored visibly on failure/cancel");
                    if (failure == "closed") AlphaTests.Check(!scene.Faulted, "User closing image is a normal cancellation");
                }
            }
            foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                var expected = MemeScales.ClientSize(new Size(1000, 800), .3, work);
                AlphaTests.Check(expected == new Size(300, 240), "Image pixels are not multiplied by desktop DPI " + dpi);
            }
            AlphaTests.Check(MemeScales.ClientSize(new Size(10000, 8000), 1, work).Height <= work.Height - 80, "Oversized image fits work area proportionally");
            var events = new System.Collections.Generic.List<string>();
            Diagnostics.EventObserver = events.Add;
            try
            {
                var values = new PetSettings { BehaviorVersion = 2, TimingVersion = 1 };
                var bounds = DesktopAreaService.MovementBounds(size, work);
                var moving = new BasicBehaviorController(values, new Random(1), bounds);
                var failing = new FakeMemeWindowAdapter { FailOpen = true };
                var source = new MemeQueue(new Random(1)); source.SetFiles(new[] { "retry-a", "retry-b" });
                var action = new MemeInteraction(moving, assets, failing, source); action.Geometry(screen, work, size);
                var actions = new InteractionController(moving, assets, values, new Random(1), memes: action);
                foreach (double delay in new[] { 30.0, 60.0, 120.0, 120.0 })
                {
                    AlphaTests.Check(action.Start(), "Retry attempt starts");
                    for (int n = 0; n < 3000 && action.Active; n++) action.Update(.02, 1);
                    AlphaTests.Check(action.Faulted && !action.Available, "Failure excludes automatic meme");
                    actions.RefreshMemeAvailability();
                    if (delay == 120 && events.Count(e => e.StartsWith("meme.retry automatic")) == 3)
                    { AlphaTests.Check(!action.TryAutomaticRetry(100000), "Three automatic probes exhaust retry budget"); break; }
                    AlphaTests.Check(!action.TryAutomaticRetry(0) && !action.TryAutomaticRetry(delay - .01) && action.Faulted, "No early or zero-time retry");
                    AlphaTests.Check(action.TryAutomaticRetry(.02) && !action.Faulted, "Retry expires at bounded cooldown");
                    actions.RefreshMemeAvailability();
                    AlphaTests.Check(actions.ActionQueue.IsAvailable(InteractionKind.Meme), "Recovered action rejoins rebuilt automatic pool");
                }
                action.Retry(); failing.FailOpen = false; actions.RefreshMemeAvailability();
                AlphaTests.Check(actions.Start(InteractionKind.Meme, false), "Automatic meme starts after recovery");
                for (int n = 0; n < 3000 && actions.Active; n++) { actions.Update(.02, bounds, 1); actions.SynchronizeMeme(1); }
                AlphaTests.Check(!action.Faulted && action.Outcome == "completed", "Successful recovery completes");
                AlphaTests.Check(events.Any(e => e.Contains("action.start mode=auto; kind=Meme")) && events.Any(e => e.Contains("action.end mode=auto; kind=Meme; outcome=completed")) && events.Any(e => e.Contains("available=False")) && events.Any(e => e.Contains("available=True")), "Logs distinguish auto start, completion and availability reasons");
                AlphaTests.Check(actions.Start(InteractionKind.Sleep, false), "Exit diagnostic action starts"); actions.CancelAll("application exit");
                AlphaTests.Check(!actions.Active && events.Any(e => e.Contains("kind=Sleep; outcome=cancelled: application exit")), "Exit cancels and logs even a stationary scenario");
            }
            finally { Diagnostics.EventObserver = null; }
        }
    }
}
