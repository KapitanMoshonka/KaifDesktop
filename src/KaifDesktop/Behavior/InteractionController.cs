using System;
using System.Collections.Generic;
using System.Drawing;
using KaifDesktop.Animation;
using KaifDesktop.Infrastructure;

namespace KaifDesktop.Behavior
{
    public enum InteractionKind { Sleep, ChaseTail, Stretch, Scratch, Cursor, Watch, Meme }
    // Explicit scenarios own their transitions. Manifest next is deliberately never executed.
    public sealed class InteractionController
    {
        private sealed class Stage { internal string Name; internal double Duration; }
        private readonly BasicBehaviorController movement;
        private readonly AssetCatalog assets;
        private readonly PetSettings settings;
        private readonly Random random;
        private readonly CursorInteraction cursorAction;
        private readonly WatchInteraction watchAction;
        private readonly MemeInteraction memeAction;
        public MemeInteraction MemeAction { get { return memeAction; } }
        private Size renderedSize;
        private readonly List<Stage> stages = new List<Stage>();
        private int stageIndex;
        private double stageElapsed, animationElapsed;
        public double CalmElapsed { get; private set; }
        public bool Active { get { return Dragging || stages.Count > 0 || (cursorAction != null && cursorAction.Active) || (watchAction != null && watchAction.Active) || (memeAction != null && memeAction.Active); } }
        public CursorInteraction CursorAction { get { return cursorAction; } }
        public WatchInteraction WatchAction { get { return watchAction; } }
        public bool Sleeping { get { return Active && Animation == "sleep"; } }
        public bool Petting { get; private set; }
        public bool Dragging { get; private set; }
        public bool CanDrag { get { return Kind != InteractionKind.Cursor && Kind != InteractionKind.Meme; } }
        private double dragElapsed;
        public bool BeginDrag()
        {
            if (!CanDrag || Dragging) return false;
            string previous = Petting ? "petting" : Kind?.ToString() ?? "calm";
            CancelAll("drag started"); movement.ResumeCalm();
            Dragging = true; Manual = true; Kind = null; dragElapsed = 0; Animation = "dragged";
            Diagnostics.Event("drag.start from=" + previous); return true;
        }
        public void MoveDrag(Point position, RectangleF bounds) { if (Dragging) movement.MoveTo(position.X, position.Y, bounds); }
        public bool EndDrag()
        {
            if (!Dragging) return false;
            Dragging = false; Diagnostics.Event("drag.end"); Finish(); return true;
        }
        public bool TryPet()
        {
            if (Active) return false;
            movement.ResumeCalm(); Petting = true; Manual = true; Kind = null;
            stageIndex = 0; stageElapsed = 0; Add("petting"); Animation = "petting";
            Diagnostics.Event("petting.start mode=manual"); return true;
        }
        public bool Manual { get; private set; }
        public InteractionKind? Kind { get; private set; }
        public string Animation { get; private set; }
        public double AnimationElapsedMs { get { return Dragging ? dragElapsed : memeAction != null && memeAction.Active ? memeAction.ElapsedMs : watchAction != null && watchAction.Active ? watchAction.AnimationElapsedMs : cursorAction != null && cursorAction.Active ? cursorAction.ElapsedMs : Active ? stageElapsed * 1000 : animationElapsed * 1000; } }
        public int CompletedCount { get; private set; }
        public int AutomaticCount { get; private set; }
        public InteractionQueue ActionQueue { get; private set; }
        private string availabilityLog;
        public InteractionController(BasicBehaviorController motion, AssetCatalog catalog, PetSettings values, Random rng, CursorInteraction pointer = null, WatchInteraction watching = null, MemeInteraction memes = null)
        {
            movement = motion; assets = catalog; settings = values; random = rng; cursorAction = pointer; watchAction = watching; memeAction = memes; Animation = motion.Animation;
            ActionQueue = new InteractionQueue(rng, EnabledKinds());
            RefreshMemeAvailability();
        }
        private List<InteractionKind> EnabledKinds()
        {
            var enabled = new List<InteractionKind> { InteractionKind.Sleep, InteractionKind.ChaseTail, InteractionKind.Stretch, InteractionKind.Scratch };
            if (cursorAction != null) enabled.Add(InteractionKind.Cursor);
            if (watchAction != null) enabled.Add(InteractionKind.Watch);
            if (memeAction != null && memeAction.Available) enabled.Add(InteractionKind.Meme);
            return enabled;
        }
        public void RefreshMemeAvailability()
        {
            var enabled = EnabledKinds();
            ActionQueue.SetAvailable(enabled);
            string state = "available=" + enabled.Contains(InteractionKind.Meme) + "; files=" + (memeAction == null ? 0 : memeAction.Memes.Files.Length) + "; reason=" + (enabled.Contains(InteractionKind.Meme) ? "ready" : memeAction?.Status ?? "no adapter");
            if (state != availabilityLog) { availabilityLog = state; Diagnostics.Event("meme.availability " + state + "; pool=" + string.Join(",", enabled)); LogQueue("availability"); }
        }
        private void LogQueue(string reason) { Diagnostics.Event("queue.preview reason=" + reason + "; next=" + string.Join(",", ActionQueue.Preview)); }
        public bool SkipNext()
        {
            InteractionKind skipped;
            if (!ActionQueue.TrySkip(out skipped)) return false;
            Diagnostics.Event("queue.skip kind=" + skipped + "; active=" + Kind); LogQueue("skip"); return true;
        }
        private void Started(InteractionKind kind, bool manual) { Diagnostics.Event("action.start mode=" + (manual ? "manual" : "auto") + "; kind=" + kind); LogQueue(manual ? "manual start" : "auto dequeue"); }
        public void SetRenderedSize(Size size) { renderedSize = size; }
        private void Add(string name, int cycles = 1, double? duration = null)
        { stages.Add(new Stage { Name = name, Duration = duration ?? assets.Clips[name].Duration / 1000.0 * cycles }); }
        public bool Start(InteractionKind kind, bool manual)
        {
            if (Active || !ActionQueue.CanStart(kind)) return false;
            Kind = kind; Manual = manual; stageIndex = 0; stageElapsed = 0;
            switch (kind)
            {
                case InteractionKind.Sleep: Add("lie-down"); Add("sleep", duration: settings.SleepSeconds); Add("wake-up"); break;
                case InteractionKind.ChaseTail: Add("chase-tail", settings.TailCycles); break;
                case InteractionKind.Stretch: Add("stretch-yawn"); break;
                case InteractionKind.Scratch:
                    Add("sit-down"); Add("ear-scratch-enter"); Add("ear-scratch-loop", settings.ScratchCycles); Add("ear-scratch-exit"); Add("stand-up"); break;
                case InteractionKind.Cursor:
                    try { if (cursorAction == null || !cursorAction.Start()) { Finish("rejected before start"); return false; } }
                    catch (Exception ex) { cursorAction?.Cancel(); Finish("error before start"); Diagnostics.Log(ex); return false; }
                    Animation = cursorAction.Animation;
                    ActionQueue.RecordStart(kind, manual);
                    if (!manual) AutomaticCount++;
                    Started(kind, manual);
                    return true;
                case InteractionKind.Watch:
                    try { if (watchAction == null || !watchAction.Start()) { Finish("rejected before start"); return false; } }
                    catch (Exception ex) { watchAction?.Cancel(); Finish("error before start"); Diagnostics.Log(ex); return false; }
                    Animation = watchAction.Animation;
                    ActionQueue.RecordStart(kind, manual);
                    if (!manual) AutomaticCount++;
                    Started(kind, manual);
                    return true;
                case InteractionKind.Meme:
                    if (memeAction == null || !memeAction.Start()) { Finish("rejected before start"); RefreshMemeAvailability(); return false; }
                    Animation = memeAction.Animation; ActionQueue.RecordStart(kind, manual); if (!manual) AutomaticCount++; Started(kind, manual); return true;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            Animation = stages[0].Name;
            ActionQueue.RecordStart(kind, manual);
            if (!manual) AutomaticCount++;
            Started(kind, manual);
            return true;
        }
        private void Advance()
        {
            stageElapsed = 0; stageIndex++;
            if (stageIndex < stages.Count) { Animation = stages[stageIndex].Name; return; }
            Finish();
        }
        private void Finish(string outcome = "completed")
        {
            if (Petting) Diagnostics.Event("petting.end outcome=" + outcome);
            Petting = false;
            if (Kind.HasValue) Diagnostics.Event("action.end mode=" + (Manual ? "manual" : "auto") + "; kind=" + Kind + "; outcome=" + outcome);
            stages.Clear(); stageIndex = 0; Kind = null; Manual = false;
            CalmElapsed = 0; animationElapsed = 0; CompletedCount++;
            movement.ResumeCalm(); Animation = movement.Animation;
        }
        public void CancelCursor()
        {
            if (cursorAction == null) return;
            bool wasCursor = Kind == InteractionKind.Cursor;
            if (!wasCursor && !cursorAction.Active && !cursorAction.Attached) return;
            cursorAction.Cancel();
            if (wasCursor) Finish("cancelled: requested");
        }
        public void SynchronizeCursor()
        {
            if (Kind != InteractionKind.Cursor) return;
            try { cursorAction.SynchronizeCursor(); }
            catch (Exception ex) { Diagnostics.Log(ex); }
            if (!cursorAction.Active) Finish(cursorAction.TotalElapsed >= settings.CursorTotalSeconds - .000001 ? "completed" : "cancelled: cursor unavailable, timeout, Escape or error");
        }
        public void CancelWatch()
        {
            if (watchAction == null) return;
            bool wasWatch = Kind == InteractionKind.Watch;
            watchAction.Cancel();
            if (wasWatch) Finish("cancelled: requested");
        }
        public void CancelMeme()
        {
            if (memeAction == null || !memeAction.Active) return;
            try { memeAction.Cancel(); } finally { Finish(memeAction.Outcome); RefreshMemeAvailability(); }
        }
        public void CancelAll(string reason)
        {
            if (!Active) return;
            try { cursorAction?.Cancel(); watchAction?.Cancel(); if (memeAction != null && memeAction.Active) memeAction.Cancel(); }
            finally { Dragging = false; Finish("cancelled: " + reason); }
        }
        public void SynchronizeMeme(double dpi)
        {
            if (Kind != InteractionKind.Meme) return;
            memeAction.SynchronizeWindow(dpi);
            if (!memeAction.Active) { Finish(memeAction.Outcome); RefreshMemeAvailability(); }
        }
        public bool Wake()
        {
            if (!Sleeping) return false;
            Advance(); return true;
        }
        public void ResetCalmAfterReturn()
        {
            // Returning a sleeping/sitting pet only moves it; it does not interrupt the scenario.
            if (!Active) { CalmElapsed = 0; animationElapsed = 0; Animation = movement.Animation; }
        }
        public void Update(double seconds, RectangleF bounds, double dpiScale)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return;
            if (Dragging) { dragElapsed += seconds * 1000; return; }
            while (seconds > 0)
            {
                if (memeAction != null && memeAction.Active)
                {
                    memeAction.Update(seconds, dpiScale);
                    if (!memeAction.Active) { Finish(memeAction.Outcome); RefreshMemeAvailability(); } else Animation = memeAction.Animation;
                    return;
                }
                if (watchAction != null && watchAction.Active)
                {
                    try { watchAction.Update(seconds, bounds, dpiScale, renderedSize); }
                    catch (Exception ex) { Diagnostics.Log(ex); }
                    if (!watchAction.Active) Finish(watchAction.TotalElapsed >= settings.WatchTotalSeconds - .000001 ? "completed" : "cancelled: pointer unavailable, timeout or error"); else Animation = watchAction.Animation;
                    return;
                }
                if (cursorAction != null && cursorAction.Active)
                {
                    try { cursorAction.Update(seconds, bounds, dpiScale, renderedSize); }
                    catch (Exception ex) { Diagnostics.Log(ex); }
                    if (!cursorAction.Active) Finish(cursorAction.TotalElapsed >= settings.CursorTotalSeconds - .000001 ? "completed" : "cancelled: cursor unavailable, timeout, Escape or error"); else Animation = cursorAction.Animation;
                    return;
                }
                if (Active)
                {
                    double slice = Math.Min(seconds, Math.Max(0, stages[stageIndex].Duration - stageElapsed));
                    stageElapsed += slice; seconds -= slice;
                    if (stageElapsed >= stages[stageIndex].Duration - 0.000000001) Advance();
                }
                else
                {
                    double slice = Math.Min(seconds, Math.Max(0, settings.CalmIntervalSeconds - CalmElapsed));
                    string before = movement.Animation;
                    movement.Update(slice, bounds, dpiScale);
                    Animation = movement.Animation;
                    animationElapsed = before == Animation ? animationElapsed + slice : 0;
                    CalmElapsed += slice; seconds -= slice;
                    if (CalmElapsed >= settings.CalmIntervalSeconds - 0.000000001)
                    {
                        InteractionKind next;
                        if (ActionQueue.TryPeek(out next)) Start(next, false);
                        else
                        {
                            movement.Update(seconds, bounds, dpiScale); Animation = movement.Animation; animationElapsed += seconds;
                            return; // One available action already ran twice: wait calmly, never spin or violate the limit.
                        }
                    }
                }
            }
        }
    }
}
