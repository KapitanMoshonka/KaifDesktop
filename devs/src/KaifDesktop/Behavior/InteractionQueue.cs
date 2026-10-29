using System;
using System.Collections.Generic;
using System.Linq;

namespace KaifDesktop.Behavior
{
    public sealed class InteractionQueue
    {
        private readonly Random random;
        private readonly List<InteractionKind> buffer = new List<InteractionKind>();
        private InteractionKind[] available;
        private InteractionKind? last;
        private int run;
        public InteractionQueue(Random rng, IEnumerable<InteractionKind> enabled) { random = rng; SetAvailable(enabled); }
        public int AvailableCount { get { return available.Length; } }
        public bool IsAvailable(InteractionKind kind) { return available.Contains(kind); }
        public InteractionKind[] Preview { get { return buffer.ToArray(); } }
        public bool CanSkip { get { return buffer.Count > 0; } }
        public bool TrySkip(out InteractionKind skipped)
        {
            skipped = default(InteractionKind);
            if (!CanSkip) return false;
            skipped = buffer[0]; buffer.RemoveAt(0);
            ValidateTail(); // Repair the new head against actual last/run, then append random choices.
            return true;
        }
        public bool CanStart(InteractionKind kind) { return available.Contains(kind) && !(last == kind && run >= 2); }
        public void SetAvailable(IEnumerable<InteractionKind> enabled, bool preserveTail = false)
        {
            var next = enabled.Distinct().OrderBy(x => x).ToArray();
            // A tail drawn from a different pool excludes new actions until it drains.
            // Rebuild from the actual history, retaining last/run and its max-two rule.
            if (available == null || (!preserveTail && !available.SequenceEqual(next))) buffer.Clear();
            available = next; ValidateTail();
        }
        public void ValidateTail()
        {
            var valid = new List<InteractionKind>(); var previous = last; int repeats = run;
            foreach (var kind in buffer)
            {
                if (!available.Contains(kind) || (previous == kind && repeats >= 2)) continue;
                valid.Add(kind); repeats = previous == kind ? repeats + 1 : 1; previous = kind;
            }
            buffer.Clear(); buffer.AddRange(valid);
            while (buffer.Count < 12)
            {
                var choices = available.Where(k => previous != k || repeats < 2).ToArray();
                if (choices.Length == 0) break;
                var kind = choices[random.Next(choices.Length) % choices.Length];
                buffer.Add(kind); repeats = previous == kind ? repeats + 1 : 1; previous = kind;
            }
        }
        public bool TryPeek(out InteractionKind kind)
        {
            ValidateTail(); kind = buffer.Count > 0 ? buffer[0] : default(InteractionKind); return buffer.Count > 0;
        }
        public void RecordStart(InteractionKind kind, bool manual)
        {
            if (!CanStart(kind)) throw new InvalidOperationException("Более двух одинаковых действий подряд запрещены.");
            if (!manual && buffer.Count > 0 && buffer[0] == kind) buffer.RemoveAt(0);
            run = last == kind ? run + 1 : 1; last = kind;
            ValidateTail();
        }
    }
}
