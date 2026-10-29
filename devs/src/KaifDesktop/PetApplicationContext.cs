using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using System.IO;
using KaifDesktop.Animation;
using KaifDesktop.Behavior;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;
using KaifDesktop.UI;

namespace KaifDesktop
{
    internal sealed class PetApplicationContext : ApplicationContext
    {
        private readonly AssetCatalog assets;
        private readonly PetWindow window;
        private readonly ControlWindow controls;
        private readonly DesktopAreaService desktop = new DesktopAreaService();
        private readonly LayeredWindowRenderer renderer = new LayeredWindowRenderer();
        private readonly SpriteAnimator animator;
        private readonly BasicBehaviorController behavior;
        private readonly InteractionController activity;
        private readonly ICursorAdapter cursorAdapter;
        private readonly MemeQueue memes;
        private readonly IMemeWindowAdapter memeWindows;
        private double lastMemeScan;
        private readonly bool diagnosticMode;
        private readonly SettingsStore store;
        private readonly PetSettings settings;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem pauseItem, hideItem;
        private readonly Timer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastTime;
        private bool paused, hidden, closing, dirty = true;
        private Bitmap previousFrame;
        private Point previousPosition;
        private Size previousSize;
        private double scale = 1;
        private readonly SmokeTest smoke;
        private readonly AutomaticMemeUiTest automaticTest;
        private readonly PettingUiTest pettingTest;
        private readonly DragUiTest dragTest;
        internal PetApplicationContext(bool testing, bool automatic = false, bool pettingProbe = false, bool dragProbe = false)
        {
            diagnosticMode = testing;
            try
            {
                store = new SettingsStore(dragProbe ? Path.Combine(Diagnostics.DataDirectory, "drag-test") : pettingProbe ? Path.Combine(Diagnostics.DataDirectory, "petting-test") : automatic ? Path.Combine(Diagnostics.DataDirectory, "auto-meme-test") : testing ? Path.Combine(Diagnostics.DataDirectory, "smoke-test") : Diagnostics.DataDirectory);
                settings = store.Load();
                if (automatic) settings.CalmIntervalSeconds = 1; // Dedicated diagnostic settings only.
                if (pettingProbe) settings.CalmIntervalSeconds = 600;
                if (dragProbe) { settings.CalmIntervalSeconds = 600; settings.CursorTotalSeconds = 3; }
                assets = new AssetCatalog(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Frames"));
                animator = new SpriteAnimator(assets);
                window = new PetWindow();
                var area = desktop.Area;
                window.Location = new Point(area.Left + area.Width / 2, area.Top);
                var handle = window.Handle;
                scale = desktop.Scale(handle);
                var size = desktop.SpriteSize(assets.FrameSize, scale);
                behavior = new BasicBehaviorController(settings, new Random(), DesktopAreaService.MovementBounds(size, area));
                cursorAdapter = testing || automatic || pettingProbe || dragProbe ? (ICursorAdapter)new FakeCursorAdapter() : new WindowsCursorAdapter(handle);
                var cursorAction = new CursorInteraction(behavior, assets, settings, cursorAdapter, new Random());
                var watchAction = new WatchInteraction(behavior, assets, settings, cursorAdapter);
                memes = new MemeQueue(new Random());
                string memeDirectory = Path.IsPathRooted(settings.MemeSourceDirectory) ? settings.MemeSourceDirectory : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settings.MemeSourceDirectory);
                memes.Scan(memeDirectory);
                memeWindows = new MemeWindowHost(MemeScales.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meme-scales.json")));
                var memeAction = new MemeInteraction(behavior, assets, memeWindows, memes);
                SetMemeGeometry(memeAction, size);
                var selection = automatic ? new Random(Enumerable.Range(0, 10000).First(n => new Random(n).Next(7) == (int)InteractionKind.Meme)) : new Random();
                activity = new InteractionController(behavior, assets, settings, selection, cursorAction, watchAction, memeAction);
                activity.SetRenderedSize(size);
                animator.Update(behavior.Animation, 0);
                window.EnvironmentChanged += (s, e) => { Diagnostics.Event("environment.changed active=" + activity.Kind + "; meme.phase=" + activity.MemeAction.Phase); CancelPointerGesture(); CancelCursor(); activity.CancelWatch(); activity.CancelMeme(); dirty = true; };
                window.WakeRequested += (s, e) => Wake();
                window.PetRequested += (s, e) => PetByClick();
                window.GestureAllowed = () => activity.CanDrag && !hidden && !closing;
                window.DragRequested += BeginPointerDrag;
                window.DragMoved += MovePointerDrag;
                window.GestureEnded += EndPointerGesture;
                window.GestureFailed += error => { activity.EndDrag(); Diagnostics.Log(error); };
                window.EscapeRequested += (s, e) => CancelCursor();
                menu = new ContextMenuStrip();
                pauseItem = new ToolStripMenuItem("Пауза", null, (s, e) => TogglePause());
                hideItem = new ToolStripMenuItem("Скрыть", null, (s, e) => ToggleHidden());
                menu.Items.Add(pauseItem); menu.Items.Add(hideItem);
                menu.Items.Add("Вернуть на экран", null, (s, e) => ReturnToScreen());
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Выход", null, (s, e) => ExitThread());
                tray = new NotifyIcon { Text = "Кайфик — альфа 0.1", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
                tray.DoubleClick += (s, e) => ReturnToScreen();
                controls = new ControlWindow(TogglePause, ToggleHidden, ReturnToScreen, ExitThread, StartManual, Wake, SkipUpcoming);
                controls.FormClosed += (s, e) => { if (!closing) ExitThread(); };
                controls.Show();
                RefreshInteractionState();
                window.Show();
                Draw();
                if (store.Warning != null) tray.ShowBalloonTip(5000, "Кайфик", store.Warning, ToolTipIcon.Warning);
                if (testing) smoke = new SmokeTest(this);
                if (automatic) automaticTest = new AutomaticMemeUiTest(this);
                if (pettingProbe) pettingTest = new PettingUiTest(this);
                if (dragProbe) dragTest = new DragUiTest(this);
                Diagnostics.Event("app.context diagnosticMode=" + diagnosticMode + "; automaticProbe=" + automatic);
                timer = new Timer { Interval = 33 };
                timer.Tick += Tick;
                lastTime = clock.Elapsed.TotalSeconds;
                timer.Start();
            }
            catch { Cleanup(false); throw; }
        }
        internal bool Paused { get { return paused; } }
        internal bool Hidden { get { return hidden; } }
        internal bool DiagnosticMode { get { return diagnosticMode; } }
        internal double X { get { return behavior.X; } }
        internal double Y { get { return behavior.Y; } }
        internal bool Walking { get { return !activity.Active && behavior.Walking; } }
        internal InteractionController Activity { get { return activity; } }
        internal PetWindow Pet { get { return window; } }
        internal FakeCursorAdapter TestCursor { get { return cursorAdapter as FakeCursorAdapter; } }
        internal MemeWindowHost TestMemeWindows { get { return (MemeWindowHost)memeWindows; } }
        internal double RemainingSeconds { get { return behavior.RemainingSeconds; } }
        internal RectangleF MovementBounds { get { return DesktopAreaService.MovementBounds(desktop.SpriteSize(assets.FrameSize, desktop.Scale(window.Handle)), desktop.Area); } }
        internal Bitmap Frame { get { return animator.Frame; } }
        internal IntPtr PetHandle { get { return window.Handle; } }
        internal ControlWindow ControlsWindow { get { return controls; } }
        internal void InvokeTrayCommand(int index)
        {
            AlphaTests.Check(tray.Visible && menu.Items.Count == 5, "Tray exists and commands are available");
            ((ToolStripMenuItem)menu.Items[index]).PerformClick();
        }
        internal void TogglePause() { CancelPointerGesture(); CancelCursor(); activity.CancelMeme(); paused = !paused; pauseItem.Text = paused ? "Продолжить" : "Пауза"; RefreshInteractionState(); }
        internal void ToggleHidden()
        {
            CancelPointerGesture();
            CancelCursor();
            activity.CancelMeme();
            hidden = !hidden; hideItem.Text = hidden ? "Показать" : "Скрыть";
            RefreshInteractionState();
            if (hidden) window.Hide(); else { window.Show(); dirty = true; Draw(); }
        }
        internal void ReturnToScreen()
        {
            CancelPointerGesture();
            CancelCursor();
            activity.CancelMeme();
            activity.CancelWatch();
            var area = desktop.Area;
            var size = desktop.SpriteSize(assets.FrameSize, desktop.Scale(window.Handle));
            behavior.ReturnToCenter(DesktopAreaService.MovementBounds(size, area));
            activity.ResetCalmAfterReturn();
            animator.SetTime(activity.Animation, activity.AnimationElapsedMs);
            if (hidden) ToggleHidden();
            dirty = true; Draw();
        }
        private void Tick(object sender, EventArgs args)
        {
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                double delta = ActiveDelta(now - lastTime); lastTime = now;
                scale = desktop.Scale(window.Handle);
                var area = desktop.Area;
                var size = desktop.SpriteSize(assets.FrameSize, scale);
                var bounds = DesktopAreaService.MovementBounds(size, area);
                if (window.GestureCaptured && NativeMethods.GetCapture() != window.Handle) CancelPointerGesture();
                activity.SetRenderedSize(size);
                SetMemeGeometry(activity.MemeAction, size);
                if (!window.GestureCaptured && !activity.Dragging && activity.MemeAction.TryAutomaticRetry(delta)) activity.RefreshMemeAvailability();
                if (!window.GestureCaptured && !activity.Dragging && !diagnosticMode && now - lastMemeScan > 5 && !activity.MemeAction.Active)
                { lastMemeScan = now; memes.Scan(memes.Source); activity.RefreshMemeAvailability(); }
                if (delta == 0) activity.CancelCursor();
                if (!activity.MemeAction.Active) behavior.Clamp(bounds);
                if (!paused && !hidden && !window.GesturePending)
                {
                    activity.Update(delta, bounds, scale);
                    animator.SetTime(activity.Animation, activity.AnimationElapsedMs);
                }
                if (!hidden) Draw();
                activity.SynchronizeCursor();
                activity.SynchronizeMeme(scale);
                RefreshInteractionState();
                smoke?.Update(now);
                automaticTest?.Update(now);
                pettingTest?.Update(now);
                dragTest?.Update(now);
            }
            catch (Exception ex)
            {
                CancelPointerGesture();
                activity.CancelCursor();
                activity.CancelWatch();
                activity.CancelMeme();
                Diagnostics.Log(ex);
                if (smoke != null) smoke.Fail(ex);
                else if (automaticTest != null) automaticTest.Fail(ex);
                else if (pettingTest != null) pettingTest.Fail(ex);
                else if (dragTest != null) dragTest.Fail(ex);
                else MessageBox.Show("Кайфик остановлен: " + ex.Message, "Ошибка Кайфика", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitThread();
            }
        }
        private void Draw()
        {
            var area = desktop.Area;
            double dpiScale = desktop.Scale(window.Handle);
            var size = desktop.SpriteSize(assets.FrameSize, dpiScale);
            bool outside = activity.MemeAction.Active;
            if (!outside) behavior.Clamp(DesktopAreaService.MovementBounds(size, area));
            var position = outside ? new Point((int)Math.Round(behavior.X), (int)Math.Round(behavior.Y)) : DesktopAreaService.SpritePosition(behavior.X, behavior.Y, size, area);
            var frame = animator.Frame;
            if (dirty || frame != previousFrame || position != previousPosition || size != previousSize)
            {
                var mask = renderer.Render(window.Handle, frame, position, size);
                window.SetHitMask(mask, size);
                previousFrame = frame; previousPosition = position; previousSize = size; dirty = false;
            }
        }
        internal void StartManual(InteractionKind kind)
        {
            CancelPointerGesture();
            if (kind == InteractionKind.Meme) { activity.MemeAction.Retry(); activity.RefreshMemeAvailability(); }
            if (kind == InteractionKind.Meme && !activity.MemeAction.Available)
            { MessageBox.Show(activity.MemeAction.Status + "\nИсточник: " + memes.Source, "Мемы", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (paused || hidden || !activity.Start(kind, true)) return;
            animator.SetTime(activity.Animation, activity.AnimationElapsedMs);
            dirty = true; Draw(); RefreshInteractionState();
        }
        internal void Wake()
        {
            CancelPointerGesture();
            if (paused || hidden || !activity.Wake()) return;
            animator.SetTime(activity.Animation, activity.AnimationElapsedMs);
            dirty = true; Draw(); RefreshInteractionState();
        }
        internal void SkipUpcoming() { activity.SkipNext(); RefreshInteractionState(); }
        private bool BeginPointerDrag()
        {
            if (hidden || closing || !activity.BeginDrag()) return false;
            animator.SetTime(activity.Animation, activity.AnimationElapsedMs); dirty = true; Draw(); RefreshInteractionState(); return true;
        }
        private void MovePointerDrag(Point point)
        {
            activity.MoveDrag(point, MovementBounds);
            animator.SetTime(activity.Animation, activity.AnimationElapsedMs); dirty = true; Draw();
        }
        private void EndPointerGesture()
        {
            activity.EndDrag();
            if (!closing) { animator.SetTime(activity.Animation, activity.AnimationElapsedMs); dirty = true; Draw(); RefreshInteractionState(); }
        }
        internal void CancelPointerGesture()
        {
            window?.CancelPointerGesture();
            if (activity != null && activity.Dragging) EndPointerGesture();
        }
        internal void PetByClick()
        {
            if (paused || hidden || !activity.TryPet()) return;
            animator.SetTime(activity.Animation, activity.AnimationElapsedMs); dirty = true; Draw(); RefreshInteractionState();
        }
        private void RefreshInteractionState()
        {
            window.SetClickMode(hidden || !activity.CanDrag ? PetClickMode.None : paused || activity.Dragging ? PetClickMode.DragOnly : activity.Sleeping ? PetClickMode.Wake : !activity.Active ? PetClickMode.Pet : PetClickMode.DragOnly);
            string description = null;
            if (activity.Dragging) description = "Кайфика перетаскивают.";
            else if (activity.Petting) description = "Кайфик радуется поглаживанию.";
            else if (activity.Active)
            {
                string[] names = { "Кайфик укладывается или спит", "Кайфик гоняется за хвостом", "Кайфик потягивается и зевает", "Кайфик чешет за ухом", "Кайфик ловит курсор. Escape — отпустить", "Кайфик подходит, садится и наблюдает", "Кайфик приносит мем" };
                description = names[(int)activity.Kind.Value] + (activity.Manual ? " (вручную)." : " (случайно).");
                if (activity.Kind == InteractionKind.Sleep)
                    description = (activity.Sleeping ? "Кайфик спит. Клик по силуэту будит." : activity.Animation == "wake-up" ? "Кайфик просыпается." : "Кайфик укладывается.") + (activity.Manual ? " (вручную)" : " (случайно)");
            }
            controls?.UpdateState(paused, hidden, activity.Active, activity.Sleeping, description);
            controls?.UpdateQueue(activity.ActionQueue);
            controls?.UpdateMemeStatus(activity.MemeAction.Status, memes.Source, memes.Preview.Take(2).Select(Path.GetFileName).ToArray());
        }
        internal void CancelCursor()
        {
            activity?.CancelCursor();
            if (activity != null) animator.SetTime(activity.Animation, activity.AnimationElapsedMs);
        }
        private void SetMemeGeometry(MemeInteraction meme, Size size)
        {
            var primary = Screen.PrimaryScreen;
            var left = new Rectangle(primary.Bounds.Left - size.Width - 32, primary.Bounds.Top, size.Width + 32, primary.Bounds.Height);
            var right = new Rectangle(primary.Bounds.Right, primary.Bounds.Top, size.Width + 32, primary.Bounds.Height);
            bool leftFree = !Screen.AllScreens.Any(s => s.DeviceName != primary.DeviceName && s.Bounds.IntersectsWith(left));
            bool rightFree = !Screen.AllScreens.Any(s => s.DeviceName != primary.DeviceName && s.Bounds.IntersectsWith(right));
            meme.Geometry(primary.Bounds, primary.WorkingArea, size, diagnosticMode || leftFree, diagnosticMode || rightFree);
        }
        protected override void ExitThreadCore()
        {
            if (closing) return;
            closing = true;
            Cleanup(true);
            base.ExitThreadCore();
        }
        internal static double ActiveDelta(double seconds)
        {
            // Do not catch up across suspension or a long UI interruption. Normal delays retain real elapsed time.
            return double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 2 ? 0 : seconds;
        }
        private void Cleanup(bool save)
        {
            timer?.Stop(); timer?.Dispose();
            CancelPointerGesture();
            activity?.CancelAll("application exit");
            if (save && settings != null && behavior != null)
            {
                if (!store.Save(settings)) MessageBox.Show("Не удалось сохранить настройки рядом с EXE. Настройки текущего сеанса были в памяти.", "Кайфик", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            memeWindows?.Dispose();
            menu?.Dispose(); controls?.Dispose(); window?.Dispose(); assets?.Dispose();
        }
    }
}
