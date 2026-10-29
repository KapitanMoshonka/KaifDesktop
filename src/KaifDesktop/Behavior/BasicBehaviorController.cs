using System;
using System.Drawing;
using KaifDesktop.Infrastructure;

namespace KaifDesktop.Behavior
{
    public sealed class BasicBehaviorController
    {
        private readonly Random random;
        private readonly PetSettings settings;
        private double remaining, directionX, directionY;
        private bool facingRight = true;
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool Walking { get; private set; }
        public string Animation { get; private set; } = "idle";
        internal double RemainingSeconds { get { return remaining; } }
        internal double PhaseDuration { get; private set; }
        public BasicBehaviorController(PetSettings values, Random rng, RectangleF bounds)
        {
            settings = values; random = rng; ReturnToCenter(bounds);
        }
        private double Pick(double min, double max) { return min + random.NextDouble() * (max - min); }
        private void BeginWalk()
        {
            Walking = true;
            // Eight normalized directions include horizontal, vertical and diagonal travel.
            int direction = random.Next(8);
            double angle = direction * Math.PI / 4;
            directionX = Math.Cos(angle); directionY = Math.Sin(angle);
            if (Math.Abs(directionX) < 0.0001) directionX = 0;
            if (Math.Abs(directionY) < 0.0001) directionY = 0;
            PhaseDuration = remaining = Pick(settings.WalkMinSeconds, settings.WalkMaxSeconds);
            UpdateFacing();
        }
        private void Rest()
        {
            Walking = false; Animation = "idle";
            PhaseDuration = remaining = Pick(settings.IdleMinSeconds, settings.IdleMaxSeconds);
        }
        public void ResumeCalm() { Rest(); }
        public void MoveTo(double x, double y, RectangleF bounds) { X = x; Y = y; Clamp(bounds); }
        private void UpdateFacing()
        {
            if (Math.Abs(directionX) > 0.05) facingRight = directionX > 0;
            Animation = facingRight ? "walk-right" : "walk-left";
        }
        public void ReturnToCenter(RectangleF bounds)
        {
            X = bounds.Left + Math.Max(0, bounds.Width) / 2;
            Y = bounds.Top + Math.Max(0, bounds.Height) / 2;
            BeginWalk();
        }
        public void Clamp(RectangleF bounds)
        {
            X = Math.Max(bounds.Left, Math.Min(bounds.Left + Math.Max(0, bounds.Width), X));
            Y = Math.Max(bounds.Top, Math.Min(bounds.Top + Math.Max(0, bounds.Height), Y));
        }
        private static double Reflect(double position, ref double direction, double distance, double min, double length)
        {
            if (length <= 0) return min;
            double raw = position - min + direction * distance;
            double phase = ((raw % (2 * length)) + 2 * length) % (2 * length);
            if (phase < length) return min + phase;
            direction = -direction;
            return min + 2 * length - phase;
        }
        public void Update(double seconds, RectangleF bounds, double dpiScale)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return;
            Clamp(bounds);
            while (seconds > 0)
            {
                double slice = Math.Min(seconds, remaining);
                if (Walking)
                {
                    double distance = settings.Speed * dpiScale * slice;
                    X = Reflect(X, ref directionX, distance, bounds.Left, Math.Max(0, bounds.Width));
                    Y = Reflect(Y, ref directionY, distance, bounds.Top, Math.Max(0, bounds.Height));
                    UpdateFacing();
                }
                remaining -= slice; seconds -= slice;
                if (remaining <= 0.000000001) { if (Walking) Rest(); else BeginWalk(); }
            }
        }
    }
}
