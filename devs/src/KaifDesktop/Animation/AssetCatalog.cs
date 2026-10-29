using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace KaifDesktop.Animation
{
    public sealed class AnimationSpec
    {
        public string[] frames { get; set; }
        public int[] durations_ms { get; set; }
        public bool loop { get; set; }
        public int[][] mouth_anchors_px { get; set; }
        public FrameEvent[] frame_events { get; set; }
    }
    public sealed class FrameEvent { public int frame { get; set; } public string @event { get; set; } }
    public sealed class Manifest
    {
        public int frame_width { get; set; }
        public int frame_height { get; set; }
        public Dictionary<string, AnimationSpec> animations { get; set; }
        public GroundAnchor anchors { get; set; }
        public WatchDirection[] watch_look_directions { get; set; }
    }
    public sealed class WatchDirection { public double degrees_clockwise_from_up { get; set; } public string file { get; set; } }
    public sealed class GroundAnchor { public int ground_y { get; set; } }
    public sealed class Clip
    {
        public readonly List<Bitmap> Frames = new List<Bitmap>();
        public int[] Durations;
        public long Duration;
        public bool Loop = true;
        public int[][] MouthAnchors;
        public FrameEvent[] Events;
        public double EventTime(string eventName)
        {
            var match = Events?.SingleOrDefault(e => e.@event == eventName);
            if (match == null) throw new InvalidDataException("Missing event: " + eventName);
            return Durations.Take(match.frame).Sum(d => (long)d) / 1000.0;
        }
        public int IndexAt(double elapsedMs)
        {
            double time = Loop ? Math.Max(0, elapsedMs) % Duration : Math.Min(Math.Max(0, elapsedMs), Duration - 0.000001);
            for (int i = 0; i < Durations.Length; i++)
            {
                if (time < Durations[i]) return i;
                time -= Durations[i];
            }
            return Durations.Length - 1;
        }
    }
    public sealed class AssetCatalog : IDisposable
    {
        public static readonly string[] RequiredNames = { "idle", "walk-left", "walk-right", "lie-down", "sleep", "wake-up", "chase-tail", "stretch-yawn", "sit-down", "ear-scratch-enter", "ear-scratch-loop", "ear-scratch-exit", "stand-up", "running-left", "running-right", "grab-left", "grab-right", "pull-left", "pull-right", "release-left", "release-right", "watch-idle", "petting", "dragged" };
        public readonly Dictionary<string, Clip> Clips = new Dictionary<string, Clip>();
        public Size FrameSize { get; private set; }
        public int GroundY { get; private set; }
        public AssetCatalog(string directory)
        {
            try
            {
                var manifest = new JavaScriptSerializer().Deserialize<Manifest>(File.ReadAllText(Path.Combine(directory, "animations.json")));
                if (manifest == null || manifest.animations == null || manifest.frame_width < 1 || manifest.frame_height < 1 || manifest.frame_width > 2048 || manifest.frame_height > 2048)
                    throw new InvalidDataException("Некорректный манифест анимаций.");
                FrameSize = new Size(manifest.frame_width, manifest.frame_height);
                GroundY = manifest.anchors == null ? FrameSize.Height : manifest.anchors.ground_y;
                if (GroundY < 0 || GroundY > FrameSize.Height) throw new InvalidDataException("Некорректная точка земли.");
                string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string name in RequiredNames)
                {
                    AnimationSpec spec;
                    if (!manifest.animations.TryGetValue(name, out spec) || spec == null || spec.frames == null || spec.durations_ms == null || spec.frames.Length == 0 || spec.frames.Length > 1000 || spec.frames.Length != spec.durations_ms.Length || spec.durations_ms.Any(d => d <= 0 || d > 60000))
                        throw new InvalidDataException("Некорректная обязательная анимация: " + name);
                    bool expectedLoop = new[] { "idle", "walk-left", "walk-right", "sleep", "chase-tail", "ear-scratch-loop", "running-left", "running-right", "pull-left", "pull-right", "watch-idle", "dragged" }.Contains(name);
                    if (spec.loop != expectedLoop) throw new InvalidDataException("Неверный режим цикла: " + name);
                    if (name.StartsWith("grab-") || name.StartsWith("pull-") || name.StartsWith("release-"))
                    {
                        if (spec.mouth_anchors_px == null || spec.mouth_anchors_px.Length != spec.frames.Length || spec.mouth_anchors_px.Any(a => a == null || a.Length != 2 || a[0] < 0 || a[1] < 0 || a[0] >= FrameSize.Width || a[1] >= FrameSize.Height)) throw new InvalidDataException("Invalid mouth anchors: " + name);
                    }
                    if (spec.frame_events != null && spec.frame_events.Any(e => e == null || e.frame < 0 || e.frame >= spec.frames.Length)) throw new InvalidDataException("Invalid frame event: " + name);
                    var clip = new Clip { Loop = spec.loop, MouthAnchors = spec.mouth_anchors_px, Events = spec.frame_events, Durations = spec.durations_ms, Duration = spec.durations_ms.Sum(d => (long)d) };
                    if (name.StartsWith("grab-")) clip.EventTime("attach_to_mouth");
                    if (name.StartsWith("release-")) clip.EventTime("detach_from_mouth");
                    Clips.Add(name, clip);
                    foreach (string file in spec.frames)
                    {
                        if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file)) throw new InvalidDataException("Некорректный путь кадра.");
                        string path = Path.GetFullPath(Path.Combine(root, file));
                        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Кадр вне Frames.");
                        if (!File.Exists(path)) throw new FileNotFoundException("Отсутствует обязательный кадр: " + file, path);
                        using (var source = new Bitmap(path))
                        {
                            if (source.Size != FrameSize || (source.PixelFormat & PixelFormat.Alpha) == 0) throw new InvalidDataException("Нужен PNG с альфой и размером из манифеста: " + file);
                            var bitmap = new Bitmap(FrameSize.Width, FrameSize.Height, PixelFormat.Format32bppPArgb);
                            try { using (var graphics = Graphics.FromImage(bitmap)) { graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; graphics.DrawImageUnscaled(source, 0, 0); } }
                            catch { bitmap.Dispose(); throw; }
                            clip.Frames.Add(bitmap);
                        }
                    }
                }
                var directions = manifest.watch_look_directions;
                if (directions == null || directions.Length != 8) throw new InvalidDataException("Нужны 8 сидячих направлений взгляда.");
                var look = new Clip { Durations = Enumerable.Repeat(1000, 8).ToArray(), Duration = 8000, Loop = true };
                Clips.Add("watch-look", look);
                for (int i = 0; i < 8; i++)
                {
                    var direction = directions.OrderBy(d => d.degrees_clockwise_from_up).ElementAt(i);
                    if (direction.degrees_clockwise_from_up != i * 45) throw new InvalidDataException("Некорректный угол сидячего взгляда.");
                    string path = Path.GetFullPath(Path.Combine(root, direction.file));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Взгляд вне Frames.");
                    using (var source = new Bitmap(path))
                    {
                        if (source.Size != FrameSize || (source.PixelFormat & PixelFormat.Alpha) == 0) throw new InvalidDataException("Некорректный PNG взгляда.");
                        var copy = new Bitmap(FrameSize.Width, FrameSize.Height, PixelFormat.Format32bppPArgb);
                        look.Frames.Add(copy);
                        using (var g = Graphics.FromImage(copy)) { g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; g.DrawImageUnscaled(source, 0, 0); }
                    }
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (var clip in Clips.Values) foreach (var frame in clip.Frames) frame.Dispose(); Clips.Clear(); }
    }
    public sealed class SpriteAnimator
    {
        private readonly AssetCatalog catalog;
        private string name;
        private double elapsed;
        public SpriteAnimator(AssetCatalog assets) { catalog = assets; name = "idle"; }
        public Bitmap Frame { get { var clip = catalog.Clips[name]; return clip.Frames[clip.IndexAt(elapsed)]; } }
        public void SetTime(string animation, double milliseconds) { name = animation; elapsed = Math.Max(0, milliseconds); }
        public void Update(string animation, double seconds)
        {
            if (name != animation) { name = animation; elapsed = 0; }
            else { var clip = catalog.Clips[name]; elapsed = clip.Loop ? (elapsed + seconds * 1000) % clip.Duration : Math.Min(clip.Duration, elapsed + seconds * 1000); }
        }
    }
}
