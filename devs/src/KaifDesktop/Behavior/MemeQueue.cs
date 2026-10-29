using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace KaifDesktop.Behavior
{
    public sealed class MemeQueue
    {
        private readonly Random random;
        private readonly List<string> buffer = new List<string>();
        private string last;
        private string lastSignature;
        public string[] Files { get; private set; } = new string[0];
        public string Status { get; private set; }
        public string Source { get; private set; }
        public string[] Preview { get { return buffer.ToArray(); } }
        public MemeQueue(Random rng) { random = rng; }
        public void Scan(string directory)
        {
            Source = Path.GetFullPath(directory);
            var files = new List<string>();
            try
            {
                foreach (string path in Directory.EnumerateFiles(Source, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        using (var image = Image.FromFile(path))
                        using (var sample = new Bitmap(1, 1))
                        using (var graphics = Graphics.FromImage(sample))
                        { if (image.Width < 1 || image.Height < 1 || (long)image.Width * image.Height > 100000000) continue; graphics.DrawImage(image, new Rectangle(0, 0, 1, 1)); }
                        files.Add(Path.GetFullPath(path));
                    }
                    catch { /* Non-images and unsupported/corrupt encodings are excluded, source is never edited. */ }
                }
                if (last != null && File.Exists(last) && lastSignature != Signature(last)) last = null;
                SetFiles(files);
            }
            catch (Exception ex) { SetFiles(new string[0]); Status = "Папка мемов недоступна: " + ex.Message; }
        }
        internal void SetFiles(IEnumerable<string> files)
        {
            Files = files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            ValidateTail();
        }
        private void ValidateTail()
        {
            string previous = last; var kept = new List<string>();
            foreach (string file in buffer)
                if (Files.Contains(file, StringComparer.OrdinalIgnoreCase) && !string.Equals(file, previous, StringComparison.OrdinalIgnoreCase)) { kept.Add(file); previous = file; }
            buffer.Clear(); buffer.AddRange(kept);
            while (buffer.Count < 8)
            {
                var choices = Files.Where(f => !string.Equals(f, previous, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (choices.Length == 0) break;
                string file = choices[random.Next(choices.Length) % choices.Length]; buffer.Add(file); previous = file;
            }
            Status = Files.Length == 0 ? "Нет декодируемых изображений в папке мемов." : buffer.Count == 0 ? "Один мем уже показан: повтор запрещён, добавьте другой файл." : "Мемов: " + Files.Length;
        }
        public bool TryPeek(out string file) { ValidateTail(); file = buffer.FirstOrDefault(); return file != null; }
        public void RecordShown(string file)
        {
            if (string.Equals(file, last, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Соседний повтор мема запрещён.");
            last = file; lastSignature = Signature(file); if (buffer.Count > 0 && string.Equals(buffer[0], file, StringComparison.OrdinalIgnoreCase)) buffer.RemoveAt(0); ValidateTail();
        }
        private static string Signature(string path)
        { try { using (var hash = SHA256.Create()) using (var input = File.OpenRead(path)) return Convert.ToBase64String(hash.ComputeHash(input)); } catch { return path; } }
    }
}
