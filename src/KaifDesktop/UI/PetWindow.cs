using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using KaifDesktop.Platform;

namespace KaifDesktop.UI
{
    internal enum PetClickMode { None, Wake, Pet, DragOnly }
    internal sealed class PetWindow : Form
    {
        public event EventHandler EnvironmentChanged;
        public event EventHandler WakeRequested;
        public event EventHandler PetRequested;
        public event Func<bool> DragRequested;
        public event Action<Point> DragMoved;
        public event Action GestureEnded;
        public event Action<Exception> GestureFailed;
        internal Func<bool> GestureAllowed;
        private readonly PetPointerGesture gesture = new PetPointerGesture();
        internal bool GesturePending { get { return gesture.Pressed && !gesture.Dragging; } }
        internal bool GestureCaptured { get { return gesture.Pressed; } }
        internal void CancelPointerGesture()
        {
            bool pending = gesture.Pressed; gesture.Clear();
            try { if (IsHandleCreated && Capture) Capture = false; }
            finally { if (pending) GestureEnded?.Invoke(); }
        }
        private static Point Local(IntPtr packed)
        { long value = packed.ToInt64(); return new Point(unchecked((short)(value & 0xffff)), unchecked((short)((value >> 16) & 0xffff))); }
        private void HandlePointer(ref Message message)
        {
            var local = Local(message.LParam); var screen = PointToScreen(local);
            if (message.Msg == 0x201)
            {
                if ((GestureAllowed == null || GestureAllowed()) && HitAt(local) && gesture.Begin(screen, local, ClickMode, SystemInformation.DragSize))
                {
                    Capture = true;
                    if (!Capture) throw new InvalidOperationException("Не удалось захватить мышь для жеста.");
                }
            }
            else if (message.Msg == 0x200 && gesture.Pressed)
            {
                if (!gesture.Dragging && gesture.BeyondThreshold(screen))
                {
                    gesture.StartDrag();
                    if (DragRequested?.Invoke() != true) { CancelPointerGesture(); return; }
                }
                if (gesture.Dragging) DragMoved?.Invoke(gesture.Position(screen));
            }
            else if (message.Msg == 0x202 && gesture.Pressed)
            {
                if (!gesture.Dragging && gesture.BeyondThreshold(screen))
                {
                    gesture.StartDrag();
                    if (DragRequested?.Invoke() != true) { CancelPointerGesture(); return; }
                    DragMoved?.Invoke(gesture.Position(screen));
                }
                bool dragged = gesture.Dragging;
                var snapshot = gesture.Snapshot;
                bool hit = HitAt(local);
                CancelPointerGesture();
                if (!dragged && hit)
                {
                    if (snapshot == PetClickMode.Wake) WakeRequested?.Invoke(this, EventArgs.Empty);
                    else if (snapshot == PetClickMode.Pet) PetRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        public event EventHandler EscapeRequested;
        private byte[] hitMask;
        private Size hitSize;
        public PetClickMode ClickMode { get; private set; }
        public bool WakeEnabled { get { return ClickMode == PetClickMode.Wake; } }
        public void SetHitMask(byte[] mask, Size size) { hitMask = mask; hitSize = size; }
        internal bool HitAt(Point point)
        {
            return ClickMode != PetClickMode.None && hitMask != null && point.X >= 0 && point.Y >= 0 && point.X < hitSize.Width && point.Y < hitSize.Height && hitMask[point.Y * hitSize.Width + point.X] > 0;
        }
        public void SetClickMode(PetClickMode mode)
        {
            if (ClickMode == mode) return;
            ClickMode = mode;
            if (!IsHandleCreated) return;
            long style = NativeMethods.GetWindowLongPtr(Handle, -20).ToInt64();
            long next = mode != PetClickMode.None ? style & ~0x20L : style | 0x20L;
            if (NativeMethods.SetWindowLongPtr(Handle, -20, new IntPtr(next)) == IntPtr.Zero) throw new Win32Exception();
            if (!NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x37)) throw new Win32Exception();
        }
        public PetWindow() { Text = "Кайфик — альфа 0.1"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual; }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x08000000 | 0x80;
                if (ClickMode == PetClickMode.None) cp.ExStyle |= 0x20;
                return cp;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && m.WParam.ToInt32() == WindowsCursorAdapter.EscapeHotkeyId) { EscapeRequested?.Invoke(this, EventArgs.Empty); return; }
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (m.Msg == 0x84)
            {
                long packed = m.LParam.ToInt64();
                var local = PointToClient(new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff))));
                m.Result = new IntPtr(HitAt(local) ? 1 : -1); return; // HTCLIENT only on currently clickable visible pixels.
            }
            if (m.Msg == 0x201 || m.Msg == 0x202 || m.Msg == 0x200)
            {
                try { HandlePointer(ref m); }
                catch (Exception error)
                {
                    gesture.Clear(); if (Capture) Capture = false;
                    GestureFailed?.Invoke(error);
                }
                return;
            }
            if (m.Msg == 0x215 || m.Msg == 0x1F) { CancelPointerGesture(); base.WndProc(ref m); return; } // capture changed / cancel mode
            if (m.Msg == 0x02E0 || m.Msg == 0x007E || m.Msg == 0x001A)
            {
                base.WndProc(ref m);
                EnvironmentChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }
    }
    internal sealed class LayeredWindowRenderer
    {
        public byte[] Render(IntPtr hwnd, Bitmap source, Point position, Size size)
        {
            using (var scaled = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb))
            {
                using (var graphics = Graphics.FromImage(scaled))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, new Rectangle(Point.Empty, size));
                }
                byte[] mask = new byte[size.Width * size.Height];
                var bits = scaled.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    byte[] row = new byte[size.Width * 4];
                    for (int y = 0; y < size.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, row.Length);
                        for (int x = 0; x < size.Width; x++) mask[y * size.Width + x] = row[x * 4 + 3];
                    }
                }
                finally { scaled.UnlockBits(bits); }
                IntPtr screen = NativeMethods.GetDC(IntPtr.Zero), dc = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
                try
                {
                    if (screen == IntPtr.Zero) throw new Win32Exception("GetDC failed.");
                    dc = NativeMethods.CreateCompatibleDC(screen);
                    if (dc == IntPtr.Zero) throw new Win32Exception();
                    bitmap = scaled.GetHbitmap(Color.FromArgb(0));
                    old = NativeMethods.SelectObject(dc, bitmap);
                    if (old == IntPtr.Zero || old == new IntPtr(-1)) throw new Win32Exception();
                    var point = new NativeMethods.Point(position.X, position.Y);
                    var dimensions = new NativeMethods.Size(size.Width, size.Height);
                    var origin = new NativeMethods.Point(0, 0);
                    var blend = new NativeMethods.Blend { Alpha = 255, Format = 1 };
                    if (!NativeMethods.UpdateLayeredWindow(hwnd, screen, ref point, ref dimensions, dc, ref origin, 0, ref blend, 2)) throw new Win32Exception();
                }
                finally
                {
                    if (old != IntPtr.Zero && old != new IntPtr(-1)) NativeMethods.SelectObject(dc, old);
                    if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
                    if (dc != IntPtr.Zero) NativeMethods.DeleteDC(dc);
                    if (screen != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screen);
                }
                return mask;
            }
        }
    }
}
