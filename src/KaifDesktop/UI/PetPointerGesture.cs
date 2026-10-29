using System.Drawing;

namespace KaifDesktop.UI
{
    internal sealed class PetPointerGesture
    {
        internal bool Pressed { get; private set; }
        internal bool Dragging { get; private set; }
        internal PetClickMode Snapshot { get; private set; }
        private Point origin, offset;
        private Size threshold;
        internal bool Begin(Point screen, Point local, PetClickMode mode, Size dragSize)
        {
            if (Pressed || mode == PetClickMode.None) return false;
            origin = screen; offset = local; threshold = dragSize; Snapshot = mode;
            Pressed = true; Dragging = false; return true;
        }
        internal bool BeyondThreshold(Point screen)
        { return Pressed && !new Rectangle(origin.X - threshold.Width / 2, origin.Y - threshold.Height / 2, threshold.Width, threshold.Height).Contains(screen); }
        internal void StartDrag() { Dragging = true; }
        internal Point Position(Point screen) { return new Point(screen.X - offset.X, screen.Y - offset.Y); }
        internal void Clear() { Pressed = Dragging = false; Snapshot = PetClickMode.None; }
    }
}
