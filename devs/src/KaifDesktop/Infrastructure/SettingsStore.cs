using System;
using System.IO;
using System.Web.Script.Serialization;

namespace KaifDesktop.Infrastructure
{
    public sealed class PetSettings
    {
        public double Speed { get; set; } = 55;
        public int BehaviorVersion { get; set; }
        public double WalkMinSeconds { get; set; } = 3;
        public double WalkMaxSeconds { get; set; } = 7;
        public double IdleMinSeconds { get; set; } = 2;
        public double IdleMaxSeconds { get; set; } = 7;
        public double? X { get; set; }
        public double CalmIntervalSeconds { get; set; } = 10;
        public double SleepSeconds { get; set; } = 60;
        public int TailCycles { get; set; } = 3;
        public int ScratchCycles { get; set; } = 4;
        public double CursorChaseSeconds { get; set; } = 8;
        public double CursorPullSeconds { get; set; } = 3;
        public double CursorChaseSpeed { get; set; } = 240;
        public double CursorPullSpeed { get; set; } = 100;
        public double WatchApproachSeconds { get; set; } = 8;
        public double WatchSeconds { get; set; } = 5;
        public double WatchApproachSpeed { get; set; } = 140;
        public double WatchGapPixels { get; set; } = 18;
        public double WatchDeadZonePixels { get; set; } = 12;
        public int TimingVersion { get; set; }
        public double WatchTotalSeconds { get; set; } = 20;
        public double CursorTotalSeconds { get; set; } = 15;
        public bool CursorCaptureEnabled { get; set; } = true;
        public string MemeSourceDirectory { get; set; } = "memes";
        public void Validate()
        {
            if (TimingVersion < 1) { WatchTotalSeconds = 20; CursorTotalSeconds = 15; TimingVersion = 1; }
            if (!Finite(WatchTotalSeconds) || WatchTotalSeconds < 5 || WatchTotalSeconds > 180) WatchTotalSeconds = 20;
            if (!Finite(CursorTotalSeconds) || CursorTotalSeconds < 3 || CursorTotalSeconds > 60) CursorTotalSeconds = 15;
            // Migrate only the former built-in default; preserve explicitly configured paths.
            if (string.IsNullOrWhiteSpace(MemeSourceDirectory) ||
                string.Equals(MemeSourceDirectory.Trim().Replace('/', '\\').TrimEnd('\\'),
                    @"C:\Users\SirxM\KaifDesktop\Frames\memes", StringComparison.OrdinalIgnoreCase))
                MemeSourceDirectory = "memes";
            if (BehaviorVersion < 2)
            {
                WalkMinSeconds = 3; WalkMaxSeconds = 7;
                IdleMinSeconds = 2; IdleMaxSeconds = 7;
                BehaviorVersion = 2;
            }
            if (!Finite(Speed) || Speed < 5 || Speed > 300) Speed = 55;
            if (!Finite(CalmIntervalSeconds) || CalmIntervalSeconds < 1 || CalmIntervalSeconds > 3600) CalmIntervalSeconds = 10;
            if (!Finite(SleepSeconds) || SleepSeconds < 1 || SleepSeconds > 86400) SleepSeconds = 60;
            if (TailCycles < 1 || TailCycles > 100) TailCycles = 3;
            if (ScratchCycles < 1 || ScratchCycles > 100) ScratchCycles = 4;
            if (!Finite(CursorChaseSeconds) || CursorChaseSeconds < 0.5 || CursorChaseSeconds > 30) CursorChaseSeconds = 8;
            if (!Finite(CursorPullSeconds) || CursorPullSeconds < 0.2 || CursorPullSeconds > 10) CursorPullSeconds = 3;
            if (!Finite(CursorChaseSpeed) || CursorChaseSpeed < 20 || CursorChaseSpeed > 800) CursorChaseSpeed = 240;
            if (!Finite(CursorPullSpeed) || CursorPullSpeed < 10 || CursorPullSpeed > 300) CursorPullSpeed = 100;
            if (!Finite(WatchApproachSeconds) || WatchApproachSeconds < 0.5 || WatchApproachSeconds > 30) WatchApproachSeconds = 8;
            if (!Finite(WatchSeconds) || WatchSeconds < 1 || WatchSeconds > 120) WatchSeconds = 5;
            if (!Finite(WatchApproachSpeed) || WatchApproachSpeed < 20 || WatchApproachSpeed > 500) WatchApproachSpeed = 140;
            if (!Finite(WatchGapPixels) || WatchGapPixels < 2 || WatchGapPixels > 100) WatchGapPixels = 18;
            if (!Finite(WatchDeadZonePixels) || WatchDeadZonePixels < 2 || WatchDeadZonePixels > 100) WatchDeadZonePixels = 12;
            if (!Finite(IdleMinSeconds) || IdleMinSeconds < 0.5 || IdleMinSeconds > 120) IdleMinSeconds = 2;
            if (!Finite(IdleMaxSeconds) || IdleMaxSeconds < IdleMinSeconds || IdleMaxSeconds > 120) IdleMaxSeconds = Math.Max(7, IdleMinSeconds);
            if (!Finite(WalkMinSeconds) || WalkMinSeconds < 0.5 || WalkMinSeconds > 120) WalkMinSeconds = 3;
            if (!Finite(WalkMaxSeconds) || WalkMaxSeconds < WalkMinSeconds || WalkMaxSeconds > 120) WalkMaxSeconds = Math.Max(7, WalkMinSeconds);
            if (X.HasValue && (!Finite(X.Value) || Math.Abs(X.Value) > 100000)) X = null;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
    public static class Diagnostics
    {
        public static bool TestMode { get; set; }
        public static string DataDirectory { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TestMode ? "tests/data" : "data"); } }
        public static string TestReport(string name)
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tests");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, name);
        }
        public static bool ActivityEnabled { get; set; }
        private static string activityDirectory;
        public static string ActivityDirectory { get { return activityDirectory ?? Path.Combine(DataDirectory, "logs"); } set { activityDirectory = value; } }
        private static readonly int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        internal static Action<string> EventObserver;
        public static void Event(string message)
        {
            EventObserver?.Invoke(message);
            if (!ActivityEnabled) return;
            try
            {
                var dir = ActivityDirectory; Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "activity.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                { string prior = path + ".previous"; if (File.Exists(prior)) File.Delete(prior); File.Move(path, prior); }
                File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " pid=" + processId + " " + message.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine);
            }
            catch { /* Diagnostics must not stop the pet. */ }
        }
        public static bool Log(Exception error)
        {
            try { var dir = Path.Combine(DataDirectory, "logs"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, "errors.log"), DateTimeOffset.Now.ToString("o") + " " + error + Environment.NewLine); return true; }
            catch { return false; }
        }
    }
    public sealed class SettingsStore
    {
        private readonly string path;
        public string Warning { get; private set; }
        public SettingsStore(string directory) { path = Path.Combine(directory, "settings.json"); }
        public PetSettings Load()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path)) { var defaults = new PetSettings(); defaults.Validate(); return defaults; }
                var result = new JavaScriptSerializer().Deserialize<PetSettings>(File.ReadAllText(path)) ?? new PetSettings();
                result.Validate(); return result;
            }
            catch (Exception ex) { Diagnostics.Log(ex); Warning = "Настройки недоступны или повреждены. Используются значения по умолчанию."; var defaults = new PetSettings(); defaults.Validate(); return defaults; }
        }
        public bool Save(PetSettings settings)
        {
            string temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(settings));
                if (File.Exists(path))
                {
                    try { File.Replace(temp, path, null); }
                    catch (IOException)
                    {
                        // Some writable folders disallow ReplaceFile's delete/metadata operations.
                        // Keep the complete .tmp until the overwrite has succeeded.
                        File.Copy(temp, path, true);
                        File.Delete(temp);
                    }
                }
                else File.Move(temp, path);
                return true;
            }
            catch (Exception ex) { Diagnostics.Log(ex); return false; }
        }
    }
}
