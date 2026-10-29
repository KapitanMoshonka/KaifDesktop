using System;
using System.Drawing;
using KaifDesktop.Animation;
using KaifDesktop.Infrastructure;
using KaifDesktop.Platform;

namespace KaifDesktop.Behavior
{
    public sealed class MemeInteraction
    {
        private readonly BasicBehaviorController motion;
        private readonly AssetCatalog assets;
        
        private readonly IMemeWindowAdapter windows;
        public MemeQueue Memes { get; private set; }
        private Rectangle screen, area;
        private Size size;
        private RectangleF extended;
        private bool left, attached, shown;
        private bool leftFree = true, rightFree = true;
        public bool Faulted { get; private set; }
        private int failures, automaticRetries;
        private double retryRemaining;
        public string Outcome { get; private set; }
        private double elapsed, total, targetX, targetY, gripY;
        private string file;
        public bool Active { get; private set; }
        public string Phase { get; private set; }
        public string Animation { get; private set; } = "idle";
        public double ElapsedMs { get { return elapsed * 1000; } }
        public string Status { get; private set; } = "Мемы: готовность не проверена";
        public MemeInteraction(BasicBehaviorController moving, AssetCatalog catalog, IMemeWindowAdapter adapter, MemeQueue queue)
        { motion = moving; assets = catalog; windows = adapter; Memes = queue; }
        public void Geometry(Rectangle mainScreen, Rectangle work, Size rendered, bool freeLeft = true, bool freeRight = true) { screen = mainScreen; area = work; size = rendered; leftFree = freeLeft; rightFree = freeRight; }
        public void Retry() { Faulted = false; failures = automaticRetries = 0; retryRemaining = 0; Diagnostics.Event("meme.retry manual"); }
        public bool TryAutomaticRetry(double seconds)
        {
            if (!Faulted || Active || automaticRetries >= 3 || seconds <= 0) return false;
            retryRemaining -= seconds;
            if (retryRemaining > 0) return false;
            Faulted = false; automaticRetries++;
            Diagnostics.Event("meme.retry automatic=" + automaticRetries + "/3");
            return true;
        }
        private void Fail(Exception error)
        {
            Faulted = true; Outcome = "faulted"; failures++;
            retryRemaining = 30 * Math.Pow(2, Math.Min(failures - 1, 2));
            Status = error.Message + (automaticRetries < 3 ? " Повторная проверка через " + retryRemaining + " с." : " Автопроверки исчерпаны; повторите ручной командой.");
            Diagnostics.Event("meme.failure reason=" + Status); Diagnostics.Log(error); Cancel();
        }
        public bool Available
        {
            get
            {
                if (Faulted) return false;
                if (!leftFree && !rightFree) { Status = "Нет свободного бокового края основного экрана."; return false; }
                string next; bool ok = Memes.TryPeek(out next);
                if (!ok) Status = Memes.Status; else if (!Active && !Status.StartsWith("Мем:")) Status = Memes.Status;
                return ok;
            }
        }
        public bool Start()
        {
            if (!Available || !Memes.TryPeek(out file)) return false;
            left = leftFree && (!rightFree || motion.X + size.Width / 2.0 < screen.Left + screen.Width / 2.0);
            extended = new RectangleF(screen.Left - size.Width - 32, area.Top, screen.Width + size.Width * 2 + 64, Math.Max(0, area.Height - size.Height));
            targetX = left ? screen.Left - size.Width - 16 : screen.Right + 16;
            targetY = Math.Max(area.Top, Math.Min(area.Bottom - size.Height, motion.Y));
            Outcome = "running"; Active = true; attached = false; shown = false; total = 0; Enter("leave"); return true;
        }
        private void Enter(string phase)
        {
            Diagnostics.Event("meme.phase " + phase + "; file=" + System.IO.Path.GetFileName(file));
            Phase = phase; elapsed = 0;
            string side = left ? "right" : "left";
            Animation = phase == "leave" ? "running-" + (left ? "left" : "right") : phase == "open" || phase == "prepare" ? "idle" : phase + "-" + side;
        }
        private bool Step(double x, double y, double distance)
        {
            double dx = x - motion.X, dy = y - motion.Y, length = Math.Sqrt(dx * dx + dy * dy);
            double scale = length > 0 ? Math.Min(1, distance / length) : 0;
            motion.MoveTo(motion.X + dx * scale, motion.Y + dy * scale, extended);
            return length <= distance + 0.01;
        }
        private Point Mouth()
        {
            var clip = assets.Clips[Animation]; var anchor = clip.MouthAnchors[clip.IndexAt(ElapsedMs)];
            return new Point((int)Math.Round(motion.X) + (int)Math.Round(anchor[0] * size.Width / (double)assets.FrameSize.Width), (int)Math.Round(motion.Y) + (int)Math.Round(anchor[1] * size.Height / (double)assets.FrameSize.Height));
        }
        public void Update(double seconds, double dpi)
        {
            if (!Active || seconds <= 0) return;
            try
            {
                if (Phase != "leave" && !windows.Valid) { Status = "Окно мема закрыто пользователем."; Cancel(); return; }
                elapsed += seconds; total += seconds;
                if (total > 40) throw new InvalidOperationException("Таймаут принесения мема.");
                if (Phase == "leave")
                {
                    if (Step(targetX, targetY, 260 * dpi * seconds)) { windows.Open(file, area, new Point((int)targetX, (int)targetY)); Enter("prepare"); }
                }
                else if (Phase == "prepare")
                {
                    if (elapsed < 0.05) return;
                    var actual = windows.Bounds;
                    if (actual.Width > area.Width || actual.Height > area.Height) throw new InvalidOperationException("Размер мема больше рабочей области: " + actual.Size);
                    Status = "Мем: " + System.IO.Path.GetFileName(file) + ", окно " + actual.Width + "×" + actual.Height + " px";
                    double margin = 8 * dpi;
                    if (actual.Height + 2 * margin > area.Height) throw new InvalidOperationException("Окно мема слишком высокое для безопасного перетягивания в рабочей области.");
                    gripY = assets.Clips["pull-" + (left ? "right" : "left")].MouthAnchors[0][1] * size.Height / (double)assets.FrameSize.Height;
                    targetY = Math.Max(area.Top + margin, Math.Min(area.Bottom - actual.Height - margin, targetY));
                    motion.MoveTo(motion.X, targetY, extended);
                    windows.Move(new Point(left ? screen.Left - actual.Width + 1 : screen.Right - 1, (int)Math.Round(targetY)));
                    Enter("grab");
                }
                else if (Phase == "grab")
                {
                    var clip = assets.Clips[Animation];
                    if (elapsed >= clip.EventTime("attach_to_mouth")) attached = true;
                    if (elapsed >= clip.Duration / 1000.0)
                    {
                        var actual = windows.Bounds;
                        var pull = assets.Clips["pull-" + (left ? "right" : "left")];
                        double anchor = left ? double.MaxValue : double.MinValue;
                        foreach (var a in pull.MouthAnchors) anchor = left ? Math.Min(anchor, a[0] * size.Width / (double)assets.FrameSize.Width) : Math.Max(anchor, a[0] * size.Width / (double)assets.FrameSize.Width);
                        targetX = left ? area.Left + actual.Width - anchor + 12 * dpi : area.Right - actual.Width - anchor - 12 * dpi;
                        targetX = Math.Max(area.Left, Math.Min(area.Right - size.Width, targetX));
                        targetY = Math.Max(area.Top, Math.Min(area.Bottom - size.Height, motion.Y));
                        Enter("pull");
                    }
                }
                else if (Phase == "pull")
                {
                    if (Step(targetX, targetY, 160 * dpi * seconds)) Enter("release");
                }
                else if (Phase == "release")
                {
                    var clip = assets.Clips[Animation];
                    if (elapsed >= clip.EventTime("detach_from_mouth")) attached = false;
                    if (elapsed >= clip.Duration / 1000.0)
                    {
                        windows.RestoreVisible(area);
                        if (!windows.Valid || !area.Contains(windows.Bounds)) throw new InvalidOperationException("Не удалось целиком показать мем в рабочей области.");
                        RecordShown(); windows.Relinquish(); Active = false; Phase = "done"; Outcome = "completed"; failures = automaticRetries = 0;
                        Diagnostics.Event("meme.released outcome=completed; file=" + System.IO.Path.GetFileName(file));
                    }
                }
            }
            catch (Exception ex) { Fail(ex); }
        }
        public void SynchronizeWindow(double dpi)
        {
            if (!Active || !attached) return;
            try
            {
                var mouth = Mouth(); var window = windows.Bounds;
                // Left arrival grips the right window edge; right arrival grips its left edge.
                int x = left ? mouth.X - window.Width : mouth.X;
                int y = (int)Math.Round(mouth.Y - gripY);
                windows.Move(new Point(x, y));
                if (windows.Bounds.IntersectsWith(area) && Rectangle.Intersect(windows.Bounds, area).Width > 24 * dpi) RecordShown();
            }
            catch (Exception ex) { Fail(ex); }
        }
        private void RecordShown() { if (!shown) { Memes.RecordShown(file); shown = true; } }
        public void Cancel()
        {
            if (Active) { if (Outcome != "faulted") Outcome = "cancelled"; Diagnostics.Event("meme.cancel phase=" + Phase + "; outcome=" + Outcome + "; reason=" + Status); }
            attached = false; Active = false; Phase = "done";
            try { windows.RestoreVisible(area); if (windows.Valid) RecordShown(); } finally { windows.Relinquish(); motion.Clamp(DesktopAreaService.MovementBounds(size, area)); }
        }
    }
}
