using System;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using KaifDesktop.Behavior;

namespace KaifDesktop.UI
{
    // An ordinary unowned window supplies the taskbar button. The pet stays a separate no-activate overlay.
    internal sealed class ControlWindow : Form
    {
        private readonly Button pause = new Button();
        private readonly Button visibility = new Button();
        private readonly Label status = new Label();
        internal readonly Button ReturnButton = new Button();
        internal readonly Button ExitButton = new Button();
        internal readonly Button SleepButton = new Button(), TailButton = new Button(), StretchButton = new Button(), ScratchButton = new Button(), WakeButton = new Button();
        internal readonly Button CursorButton = new Button();
        internal readonly CheckBox CursorCaptureCheckBox = new CheckBox();
        private bool updatingCursorCapture;
        internal readonly Button WatchButton = new Button();
        internal readonly Button MemeButton = new Button();
        internal readonly Button SkipButton = new Button();
        private readonly Label memeStatus = new Label();
        internal readonly ListBox QueueList = new ListBox();
        private string queueSignature;
        internal Button PauseButton { get { return pause; } }
        internal Button VisibilityButton { get { return visibility; } }
        internal ControlWindow(Action togglePause, Action toggleHidden, Action returnPet, Action exit, Action<InteractionKind> start, Action wake, Action skip, Action<bool> setCursorCapture)
        {
            Text = "Кайфик — управление";
            Icon = SystemIcons.Application;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 672);
            WindowState = FormWindowState.Minimized;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 12, AutoScroll = true, AutoScrollMinSize = new Size(0, 712) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Insert(6, new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 152));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(status, 0, 0); layout.SetColumnSpan(status, 2);
            Configure(pause, "Пауза", togglePause);
            Configure(visibility, "Скрыть", toggleHidden);
            Configure(ReturnButton, "Вернуть Кайфика", returnPet);
            Configure(ExitButton, "Выход", exit);
            layout.Controls.Add(pause, 0, 1); layout.Controls.Add(visibility, 1, 1);
            layout.Controls.Add(ReturnButton, 0, 2); layout.Controls.Add(ExitButton, 1, 2);
            Configure(SleepButton, "Уложиться спать", () => start(InteractionKind.Sleep));
            Configure(TailButton, "Крутись", () => start(InteractionKind.ChaseTail));
            Configure(StretchButton, "Потянуться и зевнуть", () => start(InteractionKind.Stretch));
            Configure(ScratchButton, "Почесать за ухом", () => start(InteractionKind.Scratch));
            Configure(WakeButton, "Разбудить", wake);
            layout.Controls.Add(SleepButton, 0, 3); layout.Controls.Add(TailButton, 1, 3);
            layout.Controls.Add(StretchButton, 0, 4); layout.Controls.Add(ScratchButton, 1, 4);
            Configure(CursorButton, "Поймать курсор (Esc)", () => start(InteractionKind.Cursor));
            layout.Controls.Add(WakeButton, 0, 5); layout.Controls.Add(CursorButton, 1, 5);
            CursorCaptureCheckBox.Text = "Включить захват мышки";
            CursorCaptureCheckBox.Dock = DockStyle.Fill;
            CursorCaptureCheckBox.CheckedChanged += (s, e) => { if (!updatingCursorCapture) setCursorCapture(CursorCaptureCheckBox.Checked); };
            layout.Controls.Add(CursorCaptureCheckBox, 0, 6); layout.SetColumnSpan(CursorCaptureCheckBox, 2);
            Configure(WatchButton, "Сесть рядом и наблюдать", () => start(InteractionKind.Watch));
            layout.Controls.Add(WatchButton, 0, 7); layout.SetColumnSpan(WatchButton, 2);
            var help = new Label { Text = "Крестик закрывает приложение. Трей тоже доступен.", Dock = DockStyle.Fill, AutoSize = false };
            var queuePanel = new Panel { Dock = DockStyle.Fill };
            var queueLabel = new Label { Text = "Ближайшие действия (до 2 повторов подряд):", Dock = DockStyle.Top, Height = 24 };
            QueueList.Dock = DockStyle.Fill; QueueList.IntegralHeight = false;
            Configure(SkipButton, "Пропустить действие", skip); SkipButton.Dock = DockStyle.Bottom; SkipButton.Height = 32;
            queuePanel.Controls.Add(QueueList); queuePanel.Controls.Add(queueLabel);
            queuePanel.Controls.Add(SkipButton);
            layout.Controls.Add(queuePanel, 0, 8); layout.SetColumnSpan(queuePanel, 2);
            Configure(MemeButton, "Принести мем", () => start(InteractionKind.Meme));
            layout.Controls.Add(MemeButton, 0, 9); layout.SetColumnSpan(MemeButton, 2);
            memeStatus.Dock = DockStyle.Fill; memeStatus.AutoEllipsis = true;
            layout.Controls.Add(memeStatus, 0, 10); layout.SetColumnSpan(memeStatus, 2);
            layout.Controls.Add(help, 0, 11); layout.SetColumnSpan(help, 2);
            Controls.Add(layout);
            UpdateState(false, false);
        }
        // Initial Show must not steal focus; restoring via the taskbar is a deliberate user action.
        protected override bool ShowWithoutActivation { get { return true; } }
        private static void Configure(Button button, string text, Action action)
        {
            button.Text = text; button.Dock = DockStyle.Fill; button.Margin = new Padding(3);
            button.Click += (sender, args) => action();
        }
        internal void UpdateState(bool paused, bool hidden, bool active = false, bool sleeping = false, string activity = null)
        {
            pause.Text = paused ? "Продолжить" : "Пауза";
            visibility.Text = hidden ? "Показать" : "Скрыть";
            status.Text = hidden ? (paused ? "Кайфик скрыт. Пауза включена." : "Кайфик скрыт.") : (paused ? "Кайфик на паузе." : (activity ?? "Кайфик гуляет на рабочем столе."));
            foreach (var button in new[] { SleepButton, TailButton, StretchButton, ScratchButton, CursorButton, WatchButton }) button.Enabled = !active && !paused && !hidden;
            WakeButton.Enabled = sleeping && !paused && !hidden;
            MemeButton.Enabled = !active && !paused && !hidden;
        }
        internal void UpdateQueue(InteractionQueue queue)
        {
            SkipButton.Enabled = queue.CanSkip;
            string[] names = { "Сон", "Крутись", "Потягивание и зевок", "Чесание за ухом", "Захват курсора", "Наблюдение сидя", "Принести мем" };
            var preview = queue.Preview.Take(6).Select((kind, index) => (index + 1) + ". " + names[(int)kind]).ToArray();
            string signature = string.Join("|", preview);
            if (signature != queueSignature)
            {
                queueSignature = signature; QueueList.Items.Clear();
                if (preview.Length == 0) QueueList.Items.Add("Ожидание другого доступного действия"); else QueueList.Items.AddRange(preview);
            }
            Button[] buttons = { SleepButton, TailButton, StretchButton, ScratchButton, CursorButton, WatchButton };
            for (int i = 0; i < buttons.Length; i++) buttons[i].Enabled &= queue.CanStart((InteractionKind)i);
            MemeButton.Enabled &= !queue.IsAvailable(InteractionKind.Meme) || queue.CanStart(InteractionKind.Meme);
        }
        internal void UpdateCursorCapture(bool enabled)
        {
            updatingCursorCapture = true;
            try { CursorCaptureCheckBox.Checked = enabled; }
            finally { updatingCursorCapture = false; }
        }
        internal void UpdateMemeStatus(string text, string source, string[] next)
        { memeStatus.Text = text + "\nИсточник: " + source + "\nДалее: " + string.Join(" → ", next); }
    }
}
