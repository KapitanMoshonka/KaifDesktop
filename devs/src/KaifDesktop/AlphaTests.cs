using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop
{
    internal static class AlphaTests
    {
        internal static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private sealed class DirectionRandom : Random
        {
            private readonly int[] directions;
            private int index;
            internal DirectionRandom(params int[] values) { directions = values; }
            public override int Next(int maxValue) { return directions[index++ % directions.Length] % maxValue; }
            public override double NextDouble() { return 0; }
        }
        private static void CheckExactMovementTiming()
        {
            var values = new PetSettings { BehaviorVersion = 2, WalkMinSeconds = 3, WalkMaxSeconds = 3, IdleMinSeconds = 2, IdleMaxSeconds = 2 };
            var bounds = new RectangleF(0, 0, 10, 10);
            var pet = new BasicBehaviorController(values, new DirectionRandom(0), bounds);
            pet.Update(0.75, bounds, 1);
            Check(pet.Walking && Math.Abs(pet.RemainingSeconds - 2.25) < 0.000001 && Math.Abs(pet.X - 6.25) < 0.000001, "Multiple edge reflections preserve time and traveled distance");
            pet.Update(2.25, bounds, 1);
            Check(!pet.Walking && pet.RemainingSeconds == 2, "Walk ends at exactly 3 active seconds");
            double x = pet.X, y = pet.Y;
            pet.Update(1.999, bounds, 1);
            Check(!pet.Walking && pet.X == x && pet.Y == y, "Idle remains motionless until 2 seconds");
            pet.Update(0.001, bounds, 1);
            Check(pet.Walking && Math.Abs(pet.RemainingSeconds - 3) < 0.000001, "Walk restarts at exact idle boundary");
            var large = new RectangleF(-1000, -1000, 2000, 2000);
            for (int direction = 0; direction < 8; direction++)
            {
                var example = new BasicBehaviorController(values, new DirectionRandom(direction), large);
                example.Update(0.5, large, 1);
                Check(Math.Abs(Math.Sqrt(example.X * example.X + example.Y * example.Y) - values.Speed * 0.5) < 0.00001, "Equal speed in all eight directions");
            }
            var vertical = new BasicBehaviorController(values, new DirectionRandom(4, 6), large);
            Check(vertical.Animation == "walk-left", "Initial left facing");
            vertical.Update(5, large, 1);
            Check(vertical.Walking && vertical.Animation == "walk-left", "Vertical movement retains last facing");
            x = vertical.X; y = vertical.Y;
            vertical.Update(0.5, large, 1);
            Check(vertical.X == x && vertical.Y < y && vertical.Animation == "walk-left", "Pure vertical motion with preserved sprite direction");
        }
        public static int Run()
        {
            string report = KaifDesktop.Infrastructure.Diagnostics.TestReport("self-test.txt");
            try
            {
                using (var assets = new AssetCatalog(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Frames")))
                {
                    Check(assets.Clips.Count == AssetCatalog.RequiredNames.Length + 1 && assets.Clips.Values.All(c => c.Frames.Count > 0), "Required animations and seated directions");
                    foreach (var clip in assets.Clips.Values)
                    {
                        long elapsed = 0;
                        for (int i = 0; i < clip.Durations.Length; i++)
                        {
                            Check(clip.IndexAt(elapsed) == i, "Frame boundary start");
                            Check(clip.IndexAt(elapsed + clip.Durations[i] - 0.1) == i, "Frame boundary end");
                            elapsed += clip.Durations[i];
                        }
                        Check(clip.IndexAt(elapsed) == (clip.Loop ? 0 : clip.Frames.Count - 1), "Loop or non-loop completion boundary");
                        Check(clip.IndexAt(elapsed * 1000) == (clip.Loop ? 0 : clip.Frames.Count - 1), "Finite clips never loop at large elapsed time");
                        for (int cycle = 0; cycle < (clip.Loop ? 3 : 1); cycle++)
                        {
                            long boundary = cycle * clip.Duration;
                            for (int index = 0; index < clip.Durations.Length; index++)
                            {
                                Check(clip.IndexAt(boundary) == index, "Exact frame order over three cycles");
                                boundary += clip.Durations[index];
                            }
                        }
                    }
                    foreach (string direction in new[] { "walk-left", "walk-right" })
                    {
                        var clip = assets.Clips[direction];
                        var player = new SpriteAnimator(assets);
                        player.Update(direction, 0);
                        double timeline = 0;
                        int previousIndex = 0, transitions = 0;
                        int tickMs = Math.Min(33, clip.Durations.Min());
                        while (timeline < clip.Duration * 3)
                        {
                            player.Update(direction, tickMs / 1000.0); timeline += tickMs;
                            int actual = clip.Frames.IndexOf(player.Frame);
                            Check(actual == clip.IndexAt(timeline), "Player follows manifest time without reordering");
                            if (actual != previousIndex)
                            {
                                Check(actual == (previousIndex + 1) % clip.Frames.Count, "No skipped/repeated transition");
                                transitions++; previousIndex = actual;
                            }
                        }
                        Check(transitions >= clip.Frames.Count * 3, "Three complete walk cycles");
                    }
                    var sprite = new SpriteAnimator(assets);
                    sprite.Update("walk-right", 0.1);
                    Check(ReferenceEquals(sprite.Frame, assets.Clips["walk-right"].Frames[0]), "Animation switch resets timeline");
                    InteractionTests.Run(assets);
                    CursorTests.Run(assets);
                    WatchTests.Run(assets);
                    BudgetQueueTests.Run(assets);
                    MemeTests.Run(assets);
                    PettingTests.Run(assets);
                    DragTests.Run(assets);
                    CursorSettingTests.Run(assets);
                }
                var original = new Size(192, 208);
                var area = new Rectangle(-200, 0, 1920, 1080);
                foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    var size = DesktopAreaService.SpriteSize(original, dpi, area);
                    Check(Math.Abs(size.Width - 192 * dpi / 1.5) <= 0.5 && Math.Abs(size.Height - 208 * dpi / 1.5) <= 0.5, "Two-thirds display size at each DPI");
                    var position = DesktopAreaService.SpritePosition(100000, 100000, size, area);
                    Check(area.Contains(new Rectangle(position, size)), "Scaled window remains in work area");
                    var bounds = DesktopAreaService.MovementBounds(size, area);
                    var centered = new BasicBehaviorController(new PetSettings(), new Random(2), bounds);
                    Check(Math.Abs(centered.X + size.Width / 2.0 - (area.Left + area.Width / 2.0)) < 0.001 && Math.Abs(centered.Y + size.Height / 2.0 - (area.Top + area.Height / 2.0)) < 0.001, "Centered spawn in both axes at each DPI");
                    Check(centered.Walking && centered.Animation.StartsWith("walk-"), "Immediate walk at spawn");
                }
                var tiny = new Rectangle(0, 0, 50, 40);
                var tinySize = DesktopAreaService.SpriteSize(original, 2, tiny);
                Check(tiny.Contains(new Rectangle(DesktopAreaService.SpritePosition(-100, -100, tinySize, tiny), tinySize)), "Scaled sprite fits tiny area");
                var settings = new PetSettings { Speed = double.NaN, IdleMinSeconds = -1, IdleMaxSeconds = double.PositiveInfinity, X = double.NaN };
                settings.Validate();
                Check(settings.Speed == 55 && settings.IdleMinSeconds == 2 && settings.IdleMaxSeconds == 7 && settings.WalkMinSeconds == 3 && settings.WalkMaxSeconds == 7 && !settings.X.HasValue, "Settings validation and migration");
                settings = new PetSettings { BehaviorVersion = 2, Speed = 55, X = 99999 };
                var movementBounds = new RectangleF(-100, -80, 230, 190);
                var controller = new BasicBehaviorController(settings, new Random(12), movementBounds);
                Check(controller.X == 15 && controller.Y == 15 && controller.Walking, "Saved X is ignored at centered spawn");
                bool left = false, right = false, idle = false, walk = false, horizontal = false, vertical = false, diagonal = false;
                double previousX = controller.X, previousY = controller.Y;
                for (int i = 0; i < 20000; i++)
                {
                    bool wasWalking = controller.Walking;
                    double beforeRemaining = controller.RemainingSeconds;
                    controller.Update(0.033, movementBounds, 1.5);
                    double dx = controller.X - previousX, dy = controller.Y - previousY;
                    Check(controller.X >= -100 && controller.X <= 130 && controller.Y >= -80 && controller.Y <= 110, "2D movement boundaries");
                    Check(Math.Sqrt(dx * dx + dy * dy) <= 55 * 1.5 * 0.033 + 0.001, "Normalized 2D movement speed");
                    if (wasWalking == controller.Walking && beforeRemaining > 0.033)
                        Check(Math.Abs(controller.RemainingSeconds - (beforeRemaining - 0.033)) < 0.0000001, "Real active elapsed time and no early idle at edges");
                    Check(controller.PhaseDuration >= (controller.Walking ? 3 : 2) && controller.PhaseDuration <= 7, "Walk 3-7 and idle 2-7 phase duration");
                    horizontal |= Math.Abs(dx) > 0.001 && Math.Abs(dy) < 0.00001;
                    vertical |= Math.Abs(dy) > 0.001 && Math.Abs(dx) < 0.00001;
                    diagonal |= Math.Abs(dx) > 0.001 && Math.Abs(dy) > 0.001;
                    left |= controller.Animation == "walk-left"; right |= controller.Animation == "walk-right";
                    idle |= !controller.Walking; walk |= controller.Walking; previousX = controller.X; previousY = controller.Y;
                }
                Check(left && right && idle && walk && horizontal && vertical && diagonal, "All movement directions and phases");
                Check(PetApplicationContext.ActiveDelta(0.75) == 0.75 && PetApplicationContext.ActiveDelta(3600) == 0, "Normal active delays retained; suspension excluded");
                double beforeSleep = controller.RemainingSeconds;
                controller.Update(PetApplicationContext.ActiveDelta(3600), movementBounds, 1);
                Check(controller.X == previousX && controller.Y == previousY && beforeSleep == controller.RemainingSeconds, "Resume has no jump or phase advance");
                controller.Clamp(new RectangleF(20, 30, 0, 0)); Check(controller.X == 20 && controller.Y == 30, "Collapsed work area");
                controller.Update(0.75, new RectangleF(20, 30, 0, 0), 1);
                Check(controller.X == 20 && controller.Y == 30, "Collapsed axes remain inside bounds");
                controller.ReturnToCenter(movementBounds); Check(controller.X == 15 && controller.Y == 15 && controller.Walking, "Return restarts centered walk");
                CheckExactMovementTiming();
                string sandbox = Path.Combine(Path.GetTempPath(), "KaifDesktop-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(sandbox);
                try
                {
                    var store = new SettingsStore(sandbox);
                    Check(store.Load().Speed == 55, "Missing settings defaults");
                    Check(store.Load().MemeSourceDirectory == "memes", "Portable default meme directory");
                    foreach (string legacy in new[] { @"C:\Users\SirxM\KaifDesktop\Frames\memes", "c:/users/sirxm/kaifdesktop/frames/memes/" })
                    {
                        var legacySettings = new PetSettings { MemeSourceDirectory = legacy, Speed = 83 };
                        Check(store.Save(legacySettings), "Legacy meme path fixture saved");
                        var loaded = store.Load();
                        Check(loaded.MemeSourceDirectory == "memes" && loaded.Speed == 83, "Only old default meme path migrates; other settings survive");
                    }
                    foreach (string custom in new[] { @"D:\My memes", "custom/memes", "Frames/memes" })
                    {
                        Check(store.Save(new PetSettings { MemeSourceDirectory = custom }), "Custom meme path fixture saved");
                        Check(store.Load().MemeSourceDirectory == custom, "Custom absolute and relative paths survive");
                    }
                    File.WriteAllText(Path.Combine(sandbox, "settings.json"), "{broken");
                    Check(store.Load().Speed == 55 && store.Warning != null, "Malformed settings recovery");
                    File.WriteAllText(Path.Combine(sandbox, "settings.json"), "{\"Speed\":83,\"IdleMinSeconds\":3,\"IdleMaxSeconds\":8,\"X\":9999}");
                    var migrated = store.Load();
                    Check(migrated.Speed == 83 && migrated.X == 9999 && migrated.IdleMinSeconds == 2 && migrated.IdleMaxSeconds == 7 && migrated.WalkMinSeconds == 3 && migrated.WalkMaxSeconds == 7 && migrated.BehaviorVersion == 2, "Legacy settings migrate without resetting speed");
                    migrated.IdleMinSeconds = 4; migrated.WalkMinSeconds = 5;
                    Check(store.Save(migrated) && store.Load().IdleMinSeconds == 4 && store.Load().WalkMinSeconds == 5, "Migration is one-time, later custom timing survives");
                    Check(store.Save(new PetSettings { Speed = 80, X = -20 }), "Settings write");
                    Check(store.Load().Speed == 80 && store.Load().X == -20, "Settings round trip");
                    Check(store.Save(new PetSettings { Speed = 90 }), "Settings replacement");
                    Check(store.Load().Speed == 90, "Settings replacement readable");
                    string manifestPath = Path.Combine(sandbox, "animations.json");
                    File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Frames", "animations.json"), manifestPath);
                    bool rejected = false;
                    try { using (var invalid = new AssetCatalog(sandbox)) { } } catch (ArgumentException) { rejected = true; }
                    catch (FileNotFoundException) { rejected = true; }
                    Check(rejected, "Missing required PNG rejected");
                    string json = File.ReadAllText(manifestPath);
                    File.WriteAllText(manifestPath, json.Replace("idle/00.png", "../../escape.png"));
                    rejected = false;
                    try { using (var invalid = new AssetCatalog(sandbox)) { } } catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "Path traversal rejected");
                }
                finally { Directory.Delete(sandbox, true); }
                File.WriteAllText(report, "PASS: drag: threshold/snapshot/offset, cancellation of allowed actions, forbidden Cursor/Meme phases, no queue/history mutation, bounds, looping held clip and idle reset on release. Petting: original finite 4-frame/970ms clip, calm Idle/Walking stop, no displacement/restart/queue changes, zero-delta freeze, fresh Idle and full calm reset, rejection during all seven active scenarios. Skip preserves active action/history and repairs queue boundary. First queue draws from all seven enabled kinds; unchanged availability retains preview; changed pool rebuilds 12 slots preserving last/run; deterministic new-action/refill tests. Bounded meme retries 30/60/120 seconds, exhaustion after three rechecks, recovery and diagnostic events passed. Memes: 5000 selections without adjacent repeats, singleton waiting/replacement, image decoding and nonrecursive scan, both edges and fake meme windows pull/release/error/cancel restoration. Whole watch=20s and whole capture=15s at dt .007/.033/.19/.75/1.37 with early/late approach, pause exclusion and reserved exits; no frontal watch-idle substitution; timing migration preserves other settings. Buffered queue=12, 20000 mixed manual/automatic starts and refill boundaries, no triple repeat, doubles permitted, availability repair and single-action waiting. Previous watch/cursor/scenario/resource/20000 movement regression passed. No real cursor moved.\r\n");
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); return 1; }
        }
    }
    // Explicit diagnostic mode only; runs the normal UI and exits itself after 75 seconds.
    internal sealed class SmokeTest
    {
        internal static bool Failed;
        private readonly PetApplicationContext app;
        private readonly string report = KaifDesktop.Infrastructure.Diagnostics.TestReport("smoke-test.txt");
        private int stage;
        private double savedX;
        private double savedY, savedRemaining;
        private Bitmap savedFrame;
        private uint baselineGdi, maximumGdi, baselineUser;
        private bool moved;
        private double lastX;
        private double lastY;
        private readonly IntPtr foreground;
        private int interactionStage;
        private double interactionCheckTime, savedInteractionTime, savedCalm;
        private int watchCursorMoves, watchCursorBegins, watchCursorReleases;
        private string testedMeme;
        internal SmokeTest(PetApplicationContext context)
        {
            app = context; foreground = NativeMethods.GetForegroundWindow();
            File.WriteAllText(report, "Normal UI smoke started.\r\n");
            AlphaTests.Check(foreground != app.PetHandle, "Pet stole foreground at startup");
            AlphaTests.Check(foreground != app.ControlsWindow.Handle, "Controls stole foreground at startup");
            AlphaTests.Check(app.ControlsWindow.ShowInTaskbar && NativeMethods.IsWindowVisible(app.ControlsWindow.Handle), "Taskbar controller is visible");
            AlphaTests.Check(app.ControlsWindow.WindowState == System.Windows.Forms.FormWindowState.Minimized, "Controls start minimized");
            long extendedStyle = NativeMethods.GetWindowLongPtr(app.ControlsWindow.Handle, -20).ToInt64();
            AlphaTests.Check((extendedStyle & 0x40000) != 0 && (extendedStyle & 0x80) == 0, "APPWINDOW without TOOLWINDOW");
            AlphaTests.Check(NativeMethods.GetWindow(app.ControlsWindow.Handle, 4) == IntPtr.Zero, "Taskbar window is unowned");
            AlphaTests.Check(app.ControlsWindow.QueueList.Items.Count == 6, "Upcoming queue visible at startup");
            var bounds = app.MovementBounds;
            AlphaTests.Check(app.Walking && Math.Abs(app.X - (bounds.Left + bounds.Width / 2)) < 0.001 && Math.Abs(app.Y - (bounds.Top + bounds.Height / 2)) < 0.001, "UI starts walking at exact work-area center");
            lastX = app.X; lastY = app.Y;
        }
        internal void Update(double time)
        {
            AlphaTests.Check(NativeMethods.GetForegroundWindow() != app.PetHandle, "Pet activated");
            var bounds = app.MovementBounds;
            AlphaTests.Check(app.Activity.MemeAction.Active || (app.X >= bounds.Left - 0.001 && app.X <= bounds.Right + 0.001 && app.Y >= bounds.Top - 0.001 && app.Y <= bounds.Bottom + 0.001), "UI stays in bounds except explicit meme exit phase");
            var process = Process.GetCurrentProcess();
            uint gdi = NativeMethods.GetGuiResources(process.Handle, 0);
            if (stage == 0 && time > 2)
            {
                baselineGdi = gdi; maximumGdi = gdi; baselineUser = NativeMethods.GetGuiResources(process.Handle, 1);
                app.InvokeTrayCommand(0); savedX = app.X; savedY = app.Y; savedRemaining = app.RemainingSeconds; savedFrame = app.Frame; stage++;
            }
            if (stage == 1 && time > 4)
            {
                AlphaTests.Check(app.Paused && savedX == app.X && savedY == app.Y && savedRemaining == app.RemainingSeconds && ReferenceEquals(savedFrame, app.Frame), "Pause freezes both axes, active phase timer and animation");
                app.ControlsWindow.WindowState = System.Windows.Forms.FormWindowState.Normal;
                app.ControlsWindow.VisibilityButton.PerformClick(); stage++;
            }
            if (stage == 2 && time > 5)
            {
                AlphaTests.Check(app.Hidden && app.Paused, "Hidden preserves pause");
                app.ControlsWindow.VisibilityButton.PerformClick(); AlphaTests.Check(app.Paused, "Show preserves pause");
                AlphaTests.Check(app.ControlsWindow.PauseButton.Text == "Продолжить", "Control reflects tray pause");
                app.ControlsWindow.PauseButton.PerformClick();
                app.InvokeTrayCommand(1); savedX = app.X; savedY = app.Y; savedRemaining = app.RemainingSeconds; savedFrame = app.Frame; stage++;
            }
            if (stage == 3 && time > 7)
            {
                AlphaTests.Check(app.Hidden && savedX == app.X && savedY == app.Y && savedRemaining == app.RemainingSeconds && ReferenceEquals(savedFrame, app.Frame), "Hide freezes both axes, active phase timer and animation");
                AlphaTests.Check(app.ControlsWindow.VisibilityButton.Text == "Показать", "Control reflects tray hide");
                app.ControlsWindow.ReturnButton.PerformClick(); AlphaTests.Check(!app.Hidden && !app.Paused, "Return shows and preserves running state");
                AlphaTests.Check(app.Walking && Math.Abs(app.X - (bounds.Left + bounds.Width / 2)) < 0.001 && Math.Abs(app.Y - (bounds.Top + bounds.Height / 2)) < 0.001, "Return centers both axes and starts walk");
                AlphaTests.Check(NativeMethods.SendMessage(app.PetHandle, 0x84, IntPtr.Zero, IntPtr.Zero) == new IntPtr(-1), "Transparent hit test");
                AlphaTests.Check(NativeMethods.SendMessage(app.PetHandle, 0x21, IntPtr.Zero, IntPtr.Zero) == new IntPtr(3), "No activate response");
                // Normalizing a minimized WinForms window creates child HWNDs lazily.
                // Measure steady-state resources after the controller and its commands have been used.
                baselineGdi = NativeMethods.GetGuiResources(process.Handle, 0);
                maximumGdi = baselineGdi;
                baselineUser = NativeMethods.GetGuiResources(process.Handle, 1);
                stage++;
                app.ControlsWindow.SleepButton.PerformClick();
                AlphaTests.Check(app.Activity.Active && app.Activity.Manual && app.Activity.Kind == InteractionKind.Sleep, "Manual sleep button starts explicit scenario");
                var beforeSkip = app.Activity.ActionQueue.Preview;
                double activeElapsed = app.Activity.AnimationElapsedMs;
                AlphaTests.Check(app.ControlsWindow.SkipButton.Text == "Пропустить действие" && app.ControlsWindow.SkipButton.Enabled, "Exact skip button present and enabled");
                app.ControlsWindow.SkipButton.PerformClick();
                AlphaTests.Check(app.Activity.Kind == InteractionKind.Sleep && app.Activity.Active && app.Activity.AnimationElapsedMs == activeElapsed, "UI skip preserves current sleep");
                string[] queueNames = { "Сон", "Крутись", "Потягивание и зевок", "Чесание за ухом", "Захват курсора", "Наблюдение сидя", "Принести мем" };
                var displayed = app.Activity.ActionQueue.Preview.Take(6).Select((kind, index) => (index + 1) + ". " + queueNames[(int)kind]);
                AlphaTests.Check(app.Activity.ActionQueue.Preview[0] == beforeSkip[1] && app.ControlsWindow.QueueList.Items.Cast<string>().SequenceEqual(displayed), "UI skip updates preview immediately");
            }
            if (stage >= 4)
            {
                maximumGdi = Math.Max(maximumGdi, NativeMethods.GetGuiResources(process.Handle, 0));
                moved |= Math.Abs(app.X - lastX) > 0.1 || Math.Abs(app.Y - lastY) > 0.1;
                lastX = app.X; lastY = app.Y;
                TestInteractions(time);
            }
            if (time > 75)
            {
                AlphaTests.Check(moved, "Walking occurred");
                AlphaTests.Check(interactionStage == 20, "Scenes, queue, meme leave/pull/release and background restart verified");
                // A random post-regression Meme may leave another legitimate image window open.
                // Compare resources after disposing those owned diagnostic windows, like the manual one.
                int imageWindows = app.TestMemeWindows.WindowCount;
                if (app.Activity.MemeAction.Active) app.Activity.CancelMeme();
                app.TestMemeWindows.CloseReleased();
                AlphaTests.Check(app.TestMemeWindows.WindowCount == 0, "All manual/automatic diagnostic images disposed before resource comparison");
                gdi = NativeMethods.GetGuiResources(process.Handle, 0);
                AlphaTests.Check(gdi <= baselineGdi + 5 && maximumGdi <= baselineGdi + 8, "GDI growth");
                uint user = NativeMethods.GetGuiResources(process.Handle, 1);
                AlphaTests.Check(user <= baselineUser + 5, "USER growth: baseline=" + baselineUser + ", final=" + user + ", disposed images=" + imageWindows);
                File.AppendAllText(report, "Resource checkpoint: disposed remaining automatic image windows=" + imageWindows + ".\r\n");
                File.AppendAllText(report, "PASS: 75 seconds; previous budgets/scenes/queue/cursor-release regression; meme manual button left screen and completed real owned image window pull/release, fixed image scales, no size fields, seventh queue entry integrated. Windows cursor remains fake in smoke. GDI baseline=" + baselineGdi + " max=" + maximumGdi + " final=" + gdi + "; USER baseline=" + baselineUser + " final=" + user + ". Own delivered image closed and disposed; clean Close exit.\r\n");
                app.ControlsWindow.Close();
            }
        }
        private static IntPtr PackedPoint(Point p) { return new IntPtr(unchecked((int)((ushort)p.X | ((uint)(ushort)p.Y << 16)))); }
        private void TestInteractions(double time)
        {
            if (interactionStage == 0 && app.Activity.Sleeping)
            {
                AlphaTests.Check(app.Pet.WakeEnabled && (NativeMethods.GetWindowLongPtr(app.PetHandle, -20).ToInt64() & 0x20) == 0, "Sleep receives silhouette clicks");
                AlphaTests.Check(!app.Pet.HitAt(Point.Empty), "Transparent corner does not hit");
                NativeMethods.SendMessage(app.PetHandle, 0x202, IntPtr.Zero, PackedPoint(Point.Empty));
                AlphaTests.Check(app.Activity.Sleeping, "Transparent click cannot wake");
                savedInteractionTime = app.Activity.AnimationElapsedMs; savedCalm = app.Activity.CalmElapsed;
                app.TogglePause(); interactionCheckTime = time; interactionStage = 1;
            }
            else if (interactionStage == 1 && time > interactionCheckTime + 1)
            {
                AlphaTests.Check(app.Activity.Sleeping && app.Activity.AnimationElapsedMs == savedInteractionTime && app.Activity.CalmElapsed == savedCalm && !app.Pet.WakeEnabled, "Pause freezes sleep and disables input");
                app.TogglePause(); app.ToggleHidden(); interactionCheckTime = time; interactionStage = 2;
            }
            else if (interactionStage == 2 && time > interactionCheckTime + 1)
            {
                AlphaTests.Check(app.Activity.AnimationElapsedMs == savedInteractionTime && app.Activity.CalmElapsed == savedCalm && !app.Pet.WakeEnabled, "Hide freezes sleep and disables input");
                app.ToggleHidden();
                Point hit = new Point(-1, -1);
                for (int y = 0; y < app.Pet.ClientSize.Height && hit.X < 0; y++)
                    for (int x = 0; x < app.Pet.ClientSize.Width; x++) if (app.Pet.HitAt(new Point(x, y))) { hit = new Point(x, y); break; }
                AlphaTests.Check(hit.X >= 0, "Visible sleep pixel found at rendered DPI");
                AlphaTests.Check(NativeMethods.SendMessage(app.PetHandle, 0x84, IntPtr.Zero, PackedPoint(app.Pet.PointToScreen(hit))) == new IntPtr(1), "Visible sleep pixel returns HTCLIENT");
                AlphaTests.Check(NativeMethods.SendMessage(app.PetHandle, 0x84, IntPtr.Zero, PackedPoint(app.Pet.PointToScreen(Point.Empty))) == new IntPtr(-1), "Transparent sleep pixel returns HTTRANSPARENT");
                NativeMethods.SendMessage(app.PetHandle, 0x201, new IntPtr(1), PackedPoint(hit));
                AlphaTests.Check(NativeMethods.GetCapture() == app.PetHandle, "Sleep captures only the pending click gesture");
                NativeMethods.SendMessage(app.PetHandle, 0x202, IntPtr.Zero, PackedPoint(hit));
                AlphaTests.Check(NativeMethods.GetCapture() != app.PetHandle, "Wake click leaves no mouse capture");
                AlphaTests.Check(app.Activity.Animation == "wake-up" && !app.Pet.WakeEnabled && app.Pet.ClickMode == KaifDesktop.UI.PetClickMode.DragOnly, "Silhouette click starts wake-up and restores click-through");
                interactionStage = 3;
            }
            else if (interactionStage == 3 && !app.Activity.Active)
            {
                app.ControlsWindow.TailButton.PerformClick();
                AlphaTests.Check(app.Activity.Kind == InteractionKind.ChaseTail && app.Activity.Manual, "Manual tail button"); interactionStage = 4;
            }
            else if (interactionStage == 4 && !app.Activity.Active)
            {
                app.ControlsWindow.StretchButton.PerformClick();
                AlphaTests.Check(app.Activity.Kind == InteractionKind.Stretch && app.Activity.Manual, "Manual stretch button"); interactionStage = 5;
            }
            else if (interactionStage == 5 && !app.Activity.Active)
            {
                app.ControlsWindow.ScratchButton.PerformClick();
                AlphaTests.Check(app.Activity.Kind == InteractionKind.Scratch && app.Activity.Manual, "Manual scratch button"); interactionStage = 6;
            }
            else if (interactionStage == 6 && !app.Activity.Active)
            {
                AlphaTests.Check(app.Activity.CompletedCount >= 4, "Four manual actions completed");
                app.TestCursor.Position = app.Pet.PointToScreen(new Point((int)Math.Round(166.0 * app.Pet.ClientSize.Width / 192), (int)Math.Round(96.0 * app.Pet.ClientSize.Height / 208)));
                app.ControlsWindow.CursorButton.PerformClick();
                AlphaTests.Check(app.Activity.Kind == InteractionKind.Cursor && app.Activity.Manual, "Manual cursor button uses test adapter"); interactionStage = 7;
            }
            else if (interactionStage == 7 && app.Activity.CursorAction.Attached)
            {
                AlphaTests.Check(app.TestCursor.Moves > 0, "Fake cursor follows mouth in UI");
                NativeMethods.SendMessage(app.PetHandle, 0x312, new IntPtr(WindowsCursorAdapter.EscapeHotkeyId), IntPtr.Zero);
                AlphaTests.Check(!app.Activity.Active && !app.TestCursor.Armed && !app.Activity.CursorAction.Attached, "Escape message releases simulated cursor");
                app.TestCursor.Position = new Point(-5000, -5000); interactionStage = 8;
            }
            else if (interactionStage == 8 && !app.Activity.Active)
            {
                app.TestCursor.Position = app.Pet.PointToScreen(new Point(app.Pet.ClientSize.Width + (int)Math.Round(18.0 * app.Pet.ClientSize.Width / 128), (int)Math.Round(app.Pet.ClientSize.Height * 0.38)));
                watchCursorMoves = app.TestCursor.Moves; watchCursorBegins = app.TestCursor.Begins; watchCursorReleases = app.TestCursor.Releases;
                app.ControlsWindow.WatchButton.PerformClick();
                AlphaTests.Check(app.Activity.Kind == InteractionKind.Watch && app.Activity.Manual, "Manual watch button starts read-only scenario");
                interactionStage = 9;
            }
            else if (interactionStage == 9 && app.Activity.WatchAction.Phase == "watch")
            {
                savedX = app.X; savedY = app.Y; savedInteractionTime = app.Activity.WatchAction.PhaseElapsed;
                app.TogglePause(); interactionCheckTime = time; interactionStage = 10;
            }
            else if (interactionStage == 10 && time > interactionCheckTime + 1)
            {
                AlphaTests.Check(app.Activity.Kind == InteractionKind.Watch && app.Activity.WatchAction.PhaseElapsed == savedInteractionTime && app.X == savedX && app.Y == savedY, "Pause freezes watch without cancel");
                app.TogglePause(); app.ToggleHidden(); interactionCheckTime = time; interactionStage = 11;
            }
            else if (interactionStage == 11 && time > interactionCheckTime + 1)
            {
                AlphaTests.Check(app.Activity.Kind == InteractionKind.Watch && app.Activity.WatchAction.PhaseElapsed == savedInteractionTime && app.X == savedX && app.Y == savedY, "Hide freezes watch without cancel");
                app.ToggleHidden();
                app.TestCursor.Position = app.Pet.PointToScreen(new Point(app.Pet.ClientSize.Width / 2, (int)Math.Round(app.Pet.ClientSize.Height * 0.38) - 100));
                interactionCheckTime = time; interactionStage = 12;
            }
            else if (interactionStage == 12 && time > interactionCheckTime + 0.3)
            {
                AlphaTests.Check(app.Activity.WatchAction.LookIndex == 0 && app.X == savedX && app.Y == savedY, "Seated gaze tracks up without moving");
                app.TestCursor.Position = app.Pet.PointToScreen(new Point(app.Pet.ClientSize.Width / 2 + 100, (int)Math.Round(app.Pet.ClientSize.Height * 0.38)));
                interactionCheckTime = time; interactionStage = 13;
            }
            else if (interactionStage == 13 && time > interactionCheckTime + 0.3)
            {
                AlphaTests.Check(app.Activity.WatchAction.LookIndex == 2 && app.X == savedX && app.Y == savedY, "Seated gaze tracks right without moving");
                interactionStage = 14;
            }
            else if (interactionStage == 14 && !app.Activity.Active)
            {
                AlphaTests.Check(app.Activity.CompletedCount >= 6 && app.TestCursor.Moves == watchCursorMoves && app.TestCursor.Begins == watchCursorBegins && app.TestCursor.Releases == watchCursorReleases, "Watch completed with no cursor-side calls");
                app.ControlsWindow.StretchButton.PerformClick(); interactionStage = 15;
            }
            else if (interactionStage == 15 && !app.Activity.Active)
            {
                app.ControlsWindow.StretchButton.PerformClick(); interactionStage = 16;
            }
            else if (interactionStage == 16 && !app.Activity.Active)
            {
                AlphaTests.Check(!app.ControlsWindow.StretchButton.Enabled && !app.Activity.Start(InteractionKind.Stretch, true), "Third consecutive manual action disabled and rejected");
                AlphaTests.Check(app.ControlsWindow.QueueList.Items.Count == 6 && app.Activity.ActionQueue.Preview[0] != InteractionKind.Stretch, "Queue visible and revalidated against manual history");
                app.TestCursor.Position = new Point(-5000, -5000); interactionStage = 17;
            }
            else if (interactionStage == 17 && !app.Activity.Active)
            {
                testedMeme = app.Activity.MemeAction.Memes.Preview[0];
                app.ControlsWindow.MemeButton.PerformClick();
                AlphaTests.Check(app.Activity.Kind == InteractionKind.Meme && app.Activity.Manual, "Manual meme command"); interactionStage = 18;
            }
            else if (interactionStage == 18 && !app.Activity.Active)
            {
                AlphaTests.Check(!app.Activity.MemeAction.Faulted && app.Activity.MemeAction.Memes.Preview[0] != testedMeme, "Meme completed and advanced its separate queue");
                AlphaTests.Check(app.TestMemeWindows.WindowCount > 0, "Delivered real window remains open");
                app.TestMemeWindows.CloseReleased();
                AlphaTests.Check(app.TestMemeWindows.WindowCount == 0, "Own diagnostic image window closes and releases resources");
                interactionStage = 19;
            }
            else if (interactionStage == 19 && app.Activity.AutomaticCount > 0)
            {
                interactionStage = 20;
            }
        }
        internal void Fail(Exception error) { Failed = true; File.AppendAllText(report, "FAIL: " + error); }
    }
}
