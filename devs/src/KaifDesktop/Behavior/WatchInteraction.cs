using System;
using System.Drawing;
using KaifDesktop.Animation;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop.Behavior
{
    // Read-only by construction: cannot arm a hotkey, write or restrict the cursor.
    public sealed class WatchInteraction
    {
        private readonly BasicBehaviorController motion;
        private readonly AssetCatalog assets;
        private readonly PetSettings settings;
        private readonly ICursorReader reader;
        private double elapsed;
        public double TotalElapsed { get; private set; }
        private bool preferLeft;
        private string facing = "right";
        public bool Active { get; private set; }
        public string Phase { get; private set; }
        public string Animation { get; private set; } = "idle";
        public double AnimationElapsedMs { get; private set; }
        public double PhaseElapsed { get { return elapsed; } }
        public int LookIndex { get; private set; } = 4;
        public bool Blinking { get; private set; }
        public WatchInteraction(BasicBehaviorController movement, AssetCatalog catalog, PetSettings values, ICursorReader cursor)
        { motion = movement; assets = catalog; settings = values; reader = cursor; }
        public bool Start()
        {
            Point pointer;
            if (!reader.IsSessionActive || !reader.TryRead(out pointer)) return false;
            preferLeft = pointer.X >= motion.X;
            Active = true; TotalElapsed = 0; LookIndex = 4; Enter("approach"); return true;
        }
        public void Cancel() { Active = false; Phase = "done"; Blinking = false; }
        private void Enter(string phase)
        {
            Phase = phase; elapsed = 0; Blinking = false;
            Animation = phase == "approach" ? "walk-" + facing : phase == "sit" ? "sit-down" : phase == "stand" ? "stand-up" : "watch-look";
            AnimationElapsedMs = phase == "watch" ? LookIndex * 1000 : 0;
        }
        private bool Target(Point pointer, RectangleF bounds, Size size, double dpi, out PointF target)
        {
            target = PointF.Empty;
            if (pointer.X < bounds.Left || pointer.X >= bounds.Right + size.Width || pointer.Y < bounds.Top || pointer.Y >= bounds.Bottom + size.Height) return false;
            double gap = settings.WatchGapPixels * dpi;
            var candidates = new[] {
                new PointF((float)(pointer.X - gap - size.Width), (float)(pointer.Y - size.Height * 0.38)),
                new PointF((float)(pointer.X + gap), (float)(pointer.Y - size.Height * 0.38)),
                new PointF(pointer.X - size.Width / 2f, (float)(pointer.Y + gap)),
                new PointF(pointer.X - size.Width / 2f, (float)(pointer.Y - gap - size.Height)) };
            foreach (int index in preferLeft ? new[] { 0, 1, 2, 3 } : new[] { 1, 0, 2, 3 })
            {
                var p = candidates[index];
                p.X = Math.Max(bounds.Left, Math.Min(bounds.Right, p.X));
                p.Y = Math.Max(bounds.Top, Math.Min(bounds.Bottom, p.Y));
                double dx = Math.Max(p.X - pointer.X, Math.Max(0, pointer.X - p.X - size.Width));
                double dy = Math.Max(p.Y - pointer.Y, Math.Max(0, pointer.Y - p.Y - size.Height));
                if (Math.Sqrt(dx * dx + dy * dy) >= gap - 0.1) { target = p; return true; }
            }
            return false;
        }
        internal static int Direction(double dx, double dy, int previous, double deadZone)
        {
            if (dx * dx + dy * dy < deadZone * deadZone) return previous;
            double angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
            double difference = Math.Abs(angle - previous * 45);
            difference = Math.Min(difference, 360 - difference);
            if (difference <= 30.5) return previous; // 8-degree hysteresis beyond the usual 22.5-degree sector.
            return ((int)Math.Floor((angle + 22.5) / 45)) % 8;
        }
        public void Update(double seconds, RectangleF bounds, double dpi, Size size)
        {
            if (!Active || seconds <= 0) return;
            try
            {
                Point pointer;
                if (!reader.IsSessionActive || !reader.TryRead(out pointer)) { Cancel(); return; }
                while (seconds > 0.000000001 && Active)
                {
                    if (Phase == "approach")
                    {
                        double approachLimit = Math.Min(settings.WatchApproachSeconds, settings.WatchTotalSeconds - (assets.Clips["sit-down"].Duration + assets.Clips["stand-up"].Duration) / 1000.0);
                        double dt = Math.Min(seconds, Math.Min(0.01, approachLimit - elapsed));
                        elapsed += dt; TotalElapsed += dt; seconds -= dt;
                        PointF target;
                        if (!Target(pointer, bounds, size, dpi, out target)) { Cancel(); return; }
                        double dx = target.X - motion.X, dy = target.Y - motion.Y, length = Math.Sqrt(dx * dx + dy * dy);
                        if (Math.Abs(dx) > 1) facing = dx > 0 ? "right" : "left";
                        double step = Math.Min(length, settings.WatchApproachSpeed * dpi * dt);
                        if (length > 0) motion.MoveTo(motion.X + dx * step / length, motion.Y + dy * step / length, bounds);
                        Animation = "walk-" + facing; AnimationElapsedMs = elapsed * 1000;
                        if (length <= step + 1) Enter("sit");
                        else if (elapsed >= approachLimit - 0.000000001) Cancel();
                        continue;
                    }
                    double standDuration = assets.Clips["stand-up"].Duration / 1000.0;
                    double remaining = Phase == "watch" ? settings.WatchTotalSeconds - standDuration - TotalElapsed : (Phase == "sit" ? assets.Clips["sit-down"].Duration / 1000.0 : standDuration) - elapsed;
                    double slice = Math.Min(seconds, Math.Max(0, remaining));
                    elapsed += slice; TotalElapsed += slice; seconds -= slice;
                    if (Phase == "watch")
                    {
                        LookIndex = Direction(pointer.X - motion.X - size.Width / 2.0, pointer.Y - motion.Y - size.Height * 0.38, LookIndex, settings.WatchDeadZonePixels * dpi);
                        Blinking = false; // No directional blink artwork exists: never substitute a frontal pose.
                        Animation = "watch-look";
                        AnimationElapsedMs = LookIndex * 1000;
                    }
                    else AnimationElapsedMs = elapsed * 1000;
                    if (remaining <= slice + 0.000000001)
                    {
                        if (Phase == "sit")
                        {
                            LookIndex = Direction(pointer.X - motion.X - size.Width / 2.0, pointer.Y - motion.Y - size.Height * 0.38, LookIndex, settings.WatchDeadZonePixels * dpi);
                            Enter("watch");
                        }
                        else if (Phase == "watch") Enter("stand");
                        else Cancel();
                    }
                }
            }
            catch { Cancel(); throw; }
        }
    }
}
