using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace KaifDesktop.Platform
{
    public interface ICursorReader
    {
        bool IsSessionActive { get; }
        bool TryRead(out Point position);
    }
    public interface ICursorAdapter : ICursorReader
    {
        bool Begin();
        bool EscapePressed { get; }
        void Move(Point position);
        void Release();
    }
    internal sealed class WindowsCursorAdapter : ICursorAdapter
    {
        internal const int EscapeHotkeyId = 0x4B46;
        private readonly IntPtr window;
        private bool armed;
        internal WindowsCursorAdapter(IntPtr hwnd) { window = hwnd; }
        public bool Begin()
        {
            if (!IsSessionActive) return false;
            armed = RegisterHotKey(window, EscapeHotkeyId, 0x4000, 0x1B);
            return armed; // If Escape cannot be reserved, do not start capture.
        }
        public bool EscapePressed { get { return (GetAsyncKeyState(0x1B) & 0x8000) != 0; } }
        public bool IsSessionActive
        {
            get
            {
                uint session;
                IntPtr info = IntPtr.Zero, desktop = IntPtr.Zero;
                try
                {
                    if (!ProcessIdToSessionId((uint)System.Diagnostics.Process.GetCurrentProcess().Id, out session)) return false;
                    int bytes;
                    if (!WTSQuerySessionInformation(IntPtr.Zero, session, 8, out info, out bytes) || bytes < 4 || Marshal.ReadInt32(info) != 0) return false;
                    desktop = OpenInputDesktop(0, false, 1);
                    if (desktop == IntPtr.Zero) return false;
                    return DesktopName(desktop) == DesktopName(GetThreadDesktop(GetCurrentThreadId()));
                }
                finally { if (info != IntPtr.Zero) WTSFreeMemory(info); if (desktop != IntPtr.Zero) CloseDesktop(desktop); }
            }
        }
        private static string DesktopName(IntPtr desktop)
        {
            var name = new StringBuilder(256); uint needed;
            if (!GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out needed)) throw new System.ComponentModel.Win32Exception();
            return name.ToString();
        }
        public bool TryRead(out Point position)
        {
            NativeMethods.Point p; bool ok = GetCursorPos(out p); position = new Point(p.X, p.Y); return ok;
        }
        public void Move(Point position)
        {
            if (!armed || !IsSessionActive || EscapePressed) throw new InvalidOperationException("Cursor action interrupted.");
            if (!SetCursorPos(position.X, position.Y)) throw new System.ComponentModel.Win32Exception();
        }
        public void Release() { if (armed) { UnregisterHotKey(window, EscapeHotkeyId); armed = false; } }
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativeMethods.Point point);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint process, out uint session);
        [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")] private static extern bool WTSQuerySessionInformation(IntPtr server, uint session, int type, out IntPtr buffer, out int size);
        [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
        [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder value, int size, out uint needed);
    }
    // Used exclusively by diagnostic runs; never forwards input to Windows.
    internal sealed class FakeCursorAdapter : ICursorAdapter
    {
        internal Point Position = new Point(-5000, -5000);
        internal bool Session = true, Escape, FailMove, Readable = true, BeginAllowed = true;
        internal int Moves, Releases, Begins;
        internal bool Armed;
        public bool Begin() { Begins++; Armed = BeginAllowed; return Armed; }
        public bool IsSessionActive { get { return Session; } }
        public bool EscapePressed { get { return Escape; } }
        public bool TryRead(out Point position) { position = Position; return Readable; }
        public void Move(Point position) { if (FailMove) throw new InvalidOperationException("Injected cursor failure"); Position = position; Moves++; }
        public void Release() { Releases++; Armed = false; }
    }
}
