using System;
using System.Drawing;
using KaifDesktop.Animation;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop.Behavior
{
    public sealed class CursorInteraction
    {
        private readonly BasicBehaviorController motion;
        private readonly AssetCatalog assets;
        private readonly PetSettings settings;
        private readonly ICursorAdapter cursor;
        private readonly Random random;
        private string side = "right";
        private double elapsed, pursuitTime, pullTime, targetX, targetY;
        private Size renderedSize;
        public double TotalElapsed { get; private set; }
        public bool Active { get; private set; }
        public bool Attached { get; private set; }
        public string Phase { get; private set; }
        public string Animation { get; private set; } = "idle";
        public double ElapsedMs { get { return elapsed * 1000; } }
        public CursorInteraction(BasicBehaviorController movement, AssetCatalog catalog, PetSettings values, ICursorAdapter adapter, Random rng)
        { motion = movement; assets = catalog; settings = values; cursor = adapter; random = rng; }
        public bool Start()
        {
            try { if (!cursor.Begin()) { cursor.Release(); return false; } }
            catch { cursor.Release(); throw; }
            Active = true; TotalElapsed = 0; Attached = false; elapsed = pursuitTime = pullTime = 0; Phase = "chase"; Animation = "running-" + side; return true;
        }
        public void Cancel()
        {
            Active = false; Attached = false; Phase = "done";
            cursor.Release(); // No ClipCursor, capture, or old-position restore exists anywhere.
        }
        private void Enter(string phase)
        { Phase = phase; elapsed = 0; Animation = (phase == "chase" ? "running" : phase) + "-" + side; }
        public Point MouthPoint()
        {
            var clip = assets.Clips[Animation];
            int[] anchor = clip.MouthAnchors[clip.IndexAt(ElapsedMs)];
            return new Point((int)Math.Round(motion.X) + (int)Math.Round(anchor[0] * (double)renderedSize.Width / assets.FrameSize.Width), (int)Math.Round(motion.Y) + (int)Math.Round(anchor[1] * (double)renderedSize.Height / assets.FrameSize.Height));
        }
        private PointF Offset(string name, double ms)
        {
            var clip = assets.Clips[name]; var a = clip.MouthAnchors[clip.IndexAt(ms)];
            return new PointF((float)(a[0] * (double)renderedSize.Width / assets.FrameSize.Width), (float)(a[1] * (double)renderedSize.Height / assets.FrameSize.Height));
        }
        private double Approach(double x, double y, double distance, RectangleF bounds)
        {
            double dx = x - motion.X, dy = y - motion.Y, length = Math.Sqrt(dx * dx + dy * dy);
            double ratio = length > 0 ? Math.Min(1, distance / length) : 0;
            motion.MoveTo(motion.X + dx * ratio, motion.Y + dy * ratio, bounds);
            dx = x - motion.X; dy = y - motion.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
        private bool Reachable(Point p, RectangleF bounds)
        {
            foreach (string direction in new[] { "left", "right" })
            {
                var offset = Offset("grab-" + direction, 0);
                if (p.X - offset.X >= bounds.Left - 8 && p.X - offset.X <= bounds.Right + 8 && p.Y - offset.Y >= bounds.Top - 8 && p.Y - offset.Y <= bounds.Bottom + 8) return true;
            }
            return false;
        }
        public void Update(double seconds, RectangleF bounds, double dpiScale, Size size)
        {
            if (!Active) return;
            renderedSize = size;
            try
            {
                if (!cursor.IsSessionActive || cursor.EscapePressed) { Cancel(); return; }
                // Small steps prevent skipped attach/detach events even after delayed UI updates.
                while (seconds > 0.000000001 && Active)
                {
                    double releaseDuration = assets.Clips["release-" + side].Duration / 1000.0;
                    double boundary = Phase == "release" ? settings.CursorTotalSeconds : settings.CursorTotalSeconds - releaseDuration;
                    double dt = Math.Min(seconds, Math.Min(0.01, Math.Max(0, boundary - TotalElapsed)));
                    seconds -= dt; elapsed += dt; TotalElapsed += dt;
                    if (Phase == "chase" || (Phase == "grab" && !Attached))
                    {
                        pursuitTime += dt;
                        Point p;
                        double chaseLimit = settings.CursorTotalSeconds - releaseDuration - assets.Clips["grab-" + side].Duration / 1000.0;
                        if (pursuitTime >= chaseLimit - 0.000000001 || !cursor.TryRead(out p) || !Reachable(p, bounds)) { Cancel(); return; }
                        if (Phase == "chase")
                        {
                            var right = Offset("grab-right", 0); var left = Offset("grab-left", 0);
                            double rx = p.X - right.X, lx = p.X - left.X;
                            side = rx >= bounds.Left && rx <= bounds.Right && (p.X >= motion.X + size.Width / 2.0 || lx < bounds.Left || lx > bounds.Right) ? "right" : "left";
                            Animation = "running-" + side;
                        }
                        var anchor = Offset("grab-" + side, Phase == "grab" ? ElapsedMs : 0);
                        double distance = Approach(p.X - anchor.X, p.Y - anchor.Y, settings.CursorChaseSpeed * dpiScale * dt, bounds);
                        if (Phase == "chase" && distance <= 3 * dpiScale) Enter("grab");
                        else if (Phase == "grab" && elapsed >= assets.Clips[Animation].EventTime("attach_to_mouth"))
                        {
                            if (distance <= 8 * dpiScale) Attached = true;
                            else Enter("chase"); // User escaped before attachment; continue pursuing within the same timeout.
                        }
                    }
                    if (Phase == "grab" && elapsed >= assets.Clips[Animation].Duration / 1000.0)
                    {
                        if (!Attached) { Enter("chase"); continue; }
                        targetX = side == "right" ? motion.X + random.NextDouble() * (bounds.Right - motion.X) : bounds.Left + random.NextDouble() * (motion.X - bounds.Left);
                        targetY = bounds.Top + random.NextDouble() * bounds.Height;
                        pullTime = 0; Enter("pull");
                    }
                    else if (Phase == "pull")
                    {
                        pullTime += dt;
                        double left = Approach(targetX, targetY, settings.CursorPullSpeed * dpiScale * dt, bounds);
                        if (left < 0.1)
                        {
                            targetX = side == "right" ? motion.X + random.NextDouble() * (bounds.Right - motion.X) : bounds.Left + random.NextDouble() * (motion.X - bounds.Left);
                            targetY = bounds.Top + random.NextDouble() * bounds.Height;
                        }
                    }
                    else if (Phase == "release")
                    {
                        if (Attached && elapsed >= assets.Clips[Animation].EventTime("detach_from_mouth")) { Attached = false; cursor.Release(); }
                        if (TotalElapsed >= settings.CursorTotalSeconds - 0.000000001) Cancel();
                    }
                    if (Active && Phase != "release" && TotalElapsed >= settings.CursorTotalSeconds - releaseDuration - 0.000000001)
                    { if (Attached) Enter("release"); else Cancel(); }
                }
            }
            catch { Cancel(); throw; }
        }
        public void SynchronizeCursor()
        {
            if (!Active || !Attached) return;
            try
            {
                if (!cursor.IsSessionActive || cursor.EscapePressed) { Cancel(); return; }
                cursor.Move(MouthPoint());
            }
            catch { Cancel(); throw; }
        }
    }
}
