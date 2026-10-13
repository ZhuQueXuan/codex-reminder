using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace CodexReminder
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string root = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
                SettingsStore store = new SettingsStore(root);
                if (args.Length == 2 && args[0] == "--export-icon")
                {
                    using (Icon icon = Icons.Make(true)) using (FileStream file = File.Create(args[1])) icon.Save(file);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--diagnose")
                {
                    Dictionary<string, object> result = store.Read();
                    result["tray_running"] = SettingsStore.IsRunning(store.TrayName);
                    File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(result), new UTF8Encoding(false));
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--probe-approval")
                {
                    PendingApprovalSignal signal = ApprovalObserver.Scan();
                    File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                        { "pendingApprovalDetected", signal != null }, { "mode", "read-only" } }), new UTF8Encoding(false));
                    return 0;
                }
                string mode = args.Length > 0 ? args[0] : "";
                if (mode == "--exit") { Signal(store.Key, "Exit"); return 0; }
                if (mode == "--disable") { store.Update("enabled", false); return 0; }
                bool isNew;
                using (Mutex owner = new Mutex(true, store.TrayName, out isNew))
                {
                    if (!isNew)
                    {
                        if (mode == "--enable") store.Update("enabled", true);
                        else Signal(store.Key, "Show");
                        return 0;
                    }
                    try
                    {
                        Dictionary<string, object> changes = new Dictionary<string, object> { { "require_tray", true } };
                        Dictionary<string, object> previous = store.Read();
                        foreach (string category in new string[] { "notify_completed", "notify_approval", "notify_question" })
                            if (!previous.ContainsKey(category)) changes[category] = true;
                        if (mode != "--keep-state") changes["enabled"] = true;
                        store.Update(changes);
                        using (TrayContext tray = new TrayContext(store, mode == "--show")) Application.Run(tray);
                    }
                    finally { owner.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("提醒助手未能完成操作：\r\n" + error.Message, "Codex 提醒助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static void Signal(string key, string action)
        {
            try { using (EventWaitHandle e = EventWaitHandle.OpenExisting("Local\\CodexAttention." + action + "." + key)) e.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
        }
    }

    sealed class SettingsStore
    {
        public readonly string Root, PathName, Key, TrayName;
        public SettingsStore(string root)
        {
            Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            PathName = Path.Combine(Root, "Config", "settings.json");
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(Root.ToUpperInvariant()));
                StringBuilder key = new StringBuilder();
                for (int i = 0; i < 8; i++) key.Append(bytes[i].ToString("x2"));
                Key = key.ToString();
            }
            TrayName = "Local\\CodexAttention.Tray." + Key;
        }
        public Dictionary<string, object> Read()
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(PathName, Encoding.UTF8));
        }
        public static bool Flag(Dictionary<string, object> settings, string name, bool fallback)
        {
            object value;
            return settings.TryGetValue(name, out value) && value is bool ? (bool)value : fallback;
        }
        public static bool IsRunning(string name)
        {
            try { using (Mutex owner = Mutex.OpenExisting(name)) return true; }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }
        public void Update(string name, object value) { Update(new Dictionary<string, object> { { name, value } }); }
        public void Update(Dictionary<string, object> changes)
        {
            using (Mutex gate = new Mutex(false, "Local\\CodexAttention.Settings." + Key))
            {
                bool locked = false;
                try
                {
                    try { locked = gate.WaitOne(3000); } catch (AbandonedMutexException) { locked = true; }
                    if (!locked) throw new IOException("配置正在被其他操作使用，请稍后再试。");
                    Dictionary<string, object> settings = Read();
                    foreach (KeyValuePair<string, object> change in changes) settings[change.Key] = change.Value;
                    string pending = PathName + ".pending";
                    File.WriteAllText(pending, new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
                    File.Replace(pending, PathName, null);
                }
                finally { if (locked) gate.ReleaseMutex(); }
            }
        }
    }

    static class Icons
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static Icon Make(bool active)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Brush fill = new SolidBrush(active ? Color.FromArgb(25, 147, 105) : Color.FromArgb(120, 127, 140)))
            using (Pen white = new Pen(Color.White, 2.4f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.FillEllipse(fill, 1, 1, 30, 30);
                g.DrawArc(white, 8, 7, 16, 18, 48, 264);
                g.DrawLine(white, 22, 10, 24, 9);
                if (!active) g.DrawLine(white, 8, 25, 25, 8);
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon native = Icon.FromHandle(handle)) return (Icon)native.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }

    sealed class TrayContext : ApplicationContext
    {
        readonly SettingsStore store;
        readonly NotifyIcon tray;
        readonly Icon activeIcon, pausedIcon;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem stateItem, reminderItem, soundItem, testItem;
        readonly System.Windows.Forms.Timer timer;
        readonly EventWaitHandle showEvent, exitEvent;
        readonly Control dispatcher;
        StatusForm status;
        volatile bool closing;
        bool enabled, sound, completedEnabled, approvalEnabled, questionEnabled, settingsError, approvalScanInProgress;
        DateTime nextApprovalScan = DateTime.MinValue;
        DateTime lastObserverError = DateTime.MinValue;
        readonly ApprovalSignalGate approvalGate = new ApprovalSignalGate();

        public TrayContext(SettingsStore settings, bool show)
        {
            store = settings;
            dispatcher = new Control();
            dispatcher.CreateControl();
            activeIcon = Icons.Make(true);
            pausedIcon = Icons.Make(false);
            menu = new RoundedReminderMenu();
            menu.Font = new Font("Microsoft YaHei UI", 9);
            menu.Renderer = new ReminderMenuRenderer();
            menu.Padding = new Padding(8);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            stateItem = new ToolStripMenuItem("Codex 提醒助手") { Enabled = false, Font = new Font("Microsoft YaHei UI", 8.5f) };
            reminderItem = new ToolStripMenuItem("弹出提醒", null, delegate { ToggleReminder(); });
            soundItem = new ToolStripMenuItem("声音提醒", null, delegate { ToggleSound(); });
            testItem = new ToolStripMenuItem("测试弹出", null, delegate { TestWindow(); });
            menu.Items.Add(stateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(reminderItem);
            menu.Items.Add(soundItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(testItem);
            menu.Items.Add(new ToolStripMenuItem("设置与状态", null, delegate { ShowStatus(); }));
            menu.Items.Add(new ToolStripMenuItem("打开使用说明", null, delegate { OpenGuide(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("退出程序", null, delegate { Quit(); }) { ForeColor = Color.FromArgb(157, 67, 67) });
            foreach (ToolStripItem item in menu.Items)
                if (!(item is ToolStripSeparator)) item.Padding = new Padding(8, 4, 16, 4);
            menu.Opening += delegate { RefreshSettings(); };
            tray = new NotifyIcon { Icon = activeIcon, Text = "Codex 提醒助手", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowStatus(); };
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexAttention.Show." + store.Key);
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexAttention.Exit." + store.Key);
            timer = new System.Windows.Forms.Timer { Interval = 600 };
            timer.Tick += delegate
            {
                if (exitEvent.WaitOne(0)) { Quit(); return; }
                if (showEvent.WaitOne(0)) ShowStatus();
                RefreshSettings();
                PollApproval();
            };
            RefreshSettings();
            timer.Start();
            if (show) ShowStatus();
            else Balloon(enabled ? "提醒已开启" : "提醒已暂停", "右键图标控制提醒，双击打开设置。找不到图标时，点击右下角的向上箭头。");
        }

        void RefreshSettings()
        {
            try
            {
                Dictionary<string, object> settings = store.Read();
                enabled = SettingsStore.Flag(settings, "enabled", false);
                sound = SettingsStore.Flag(settings, "sound", true);
                completedEnabled = SettingsStore.Flag(settings, "notify_completed", true);
                approvalEnabled = SettingsStore.Flag(settings, "notify_approval", true);
                questionEnabled = SettingsStore.Flag(settings, "notify_question", true);
                settingsError = false;
                tray.Icon = enabled ? activeIcon : pausedIcon;
                tray.Text = enabled ? "Codex 提醒助手 · 提醒已开启" : "Codex 提醒助手 · 提醒已关闭";
                stateItem.Text = "Codex 提醒助手";
                reminderItem.Checked = enabled;
                reminderItem.Enabled = soundItem.Enabled = true;
                soundItem.Checked = sound;
                if (status != null && !status.IsDisposed) status.UpdateState(enabled, sound, false, completedEnabled, approvalEnabled, questionEnabled);
            }
            catch
            {
                settingsError = true;
                tray.Icon = pausedIcon;
                tray.Text = "Codex 提醒助手 · 配置读取失败";
                stateItem.Text = "配置读取失败，请检查配置文件";
                reminderItem.Enabled = soundItem.Enabled = false;
                if (status != null && !status.IsDisposed) status.UpdateState(false, false, true, completedEnabled, approvalEnabled, questionEnabled);
            }
        }

        public void SetEnabled(bool value)
        {
            try { store.Update("enabled", value); approvalGate.Reset(); RefreshSettings(); }
            catch (Exception error) { ShowError(error); }
        }
        public void SetSound(bool value)
        {
            try { store.Update("sound", value); RefreshSettings(); }
            catch (Exception error) { ShowError(error); }
        }
        public void SetNotificationType(string key, bool value)
        {
            try
            {
                if (key != "notify_completed" && key != "notify_approval" && key != "notify_question") throw new ArgumentException("Unknown reminder type");
                store.Update(key, value);
                if (key == "notify_approval") approvalGate.Reset();
                RefreshSettings();
            }
            catch (Exception error) { ShowError(error); }
        }
        public void ToggleReminder()
        {
            try { SetEnabled(!SettingsStore.Flag(store.Read(), "enabled", false)); }
            catch (Exception error) { ShowError(error); }
        }
        public void ToggleSound()
        {
            try { SetSound(!SettingsStore.Flag(store.Read(), "sound", true)); }
            catch (Exception error) { ShowError(error); }
        }
        public void ShowStatus()
        {
            if (status == null || status.IsDisposed) status = new StatusForm(this);
            status.UpdateState(enabled, sound, settingsError, completedEnabled, approvalEnabled, questionEnabled);
            status.Show();
            if (status.WindowState == FormWindowState.Minimized) status.WindowState = FormWindowState.Normal;
            status.Activate();
        }
        void PollApproval()
        {
            if (closing || !enabled || !approvalEnabled || settingsError) { approvalGate.Reset(); return; }
            if (approvalScanInProgress || DateTime.UtcNow < nextApprovalScan) return;
            approvalScanInProgress = true;
            nextApprovalScan = DateTime.UtcNow.AddSeconds(1.2);
            Task.Factory.StartNew(delegate
            {
                PendingApprovalSignal signal = null;
                bool failed = false;
                try { signal = ApprovalObserver.Scan(); }
                catch (Exception error)
                {
                    failed = true;
                    if ((DateTime.UtcNow - lastObserverError).TotalMinutes >= 1)
                    { lastObserverError = DateTime.UtcNow; ObserverLog("observer-error", error.GetType().Name); }
                }
                if (closing || dispatcher.IsDisposed) return;
                try
                {
                    dispatcher.BeginInvoke((Action)delegate
                    {
                        approvalScanInProgress = false;
                        if (!failed && !closing && enabled && approvalEnabled && approvalGate.Observe(signal)) { ObserverLog("approval-detected", null); SendApprovalSignal(); }
                    });
                }
                catch (InvalidOperationException) { }
            });
        }
        void SendApprovalSignal()
        {
            Task.Factory.StartNew(delegate
            {
                if (closing) return;
                try
                {
                    Dictionary<string, object> settings = store.Read();
                    if (!SettingsStore.Flag(settings, "enabled", false) || !SettingsStore.Flag(settings, "notify_approval", true)) return;
                    string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                    ProcessStartInfo start = new ProcessStartInfo(shell,
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(store.Root, "Attention.ps1") + "\" -ApprovalSignal");
                    start.UseShellExecute = false; start.CreateNoWindow = true;
                    start.RedirectStandardOutput = true; start.RedirectStandardError = true;
                    using (Process process = Process.Start(start))
                    {
                        Task<string> output = process.StandardOutput.ReadToEndAsync();
                        Task<string> errors = process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(10000)) process.Kill();
                    }
                }
                catch (Exception error) { ObserverLog("observer-alert-error", error.GetType().Name); }
            });
        }
        void ObserverLog(string result, string errorType)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAttention");
                Directory.CreateDirectory(folder);
                Dictionary<string, object> record = new Dictionary<string, object> {
                    { "at", DateTimeOffset.UtcNow.ToString("o") }, { "result", result }, { "source", "approval-interface-observer" } };
                if (errorType != null) record["errorType"] = errorType;
                File.AppendAllText(Path.Combine(folder, "events.jsonl"), new JavaScriptSerializer().Serialize(record) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }
        public void OpenGuide()
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(store.Root, "Docs", "使用说明.md")) { UseShellExecute = true }); }
            catch (Exception error) { ShowError(error); }
        }
        public void TestWindow()
        {
            if (!testItem.Enabled) return;
            testItem.Enabled = false;
            if (status != null && !status.IsDisposed) status.TestBusy(true);
            Balloon("正在测试", "即使提醒已关闭，也会执行一次手动窗口测试。");
            Task.Factory.StartNew(delegate
            {
                string result;
                try
                {
                    string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                    ProcessStartInfo start = new ProcessStartInfo(shell,
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(store.Root, "Attention.ps1") + "\" -Test");
                    start.UseShellExecute = false; start.CreateNoWindow = true;
                    start.RedirectStandardOutput = true; start.RedirectStandardError = true;
                    start.StandardOutputEncoding = Encoding.UTF8;
                    using (Process process = Process.Start(start))
                    {
                        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                        Task<string> errorTask = process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(10000)) { process.Kill(); throw new IOException("窗口测试超时，请稍后重试。"); }
                        string output = outputTask.Result;
                        string error = errorTask.Result;
                        if (process.ExitCode != 0) throw new IOException("窗口测试未完成，请检查使用说明。");
                        Dictionary<string, object> data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(output.Trim());
                        result = Convert.ToString(data["result"]);
                    }
                }
                catch (Exception error) { result = "测试未完成：" + error.Message; }
                if (closing || dispatcher.IsDisposed) return;
                try { dispatcher.BeginInvoke((Action)delegate { FinishTest(result); }); } catch (InvalidOperationException) { }
            });
        }
        void FinishTest(string result)
        {
            if (closing) return;
            testItem.Enabled = true;
            if (status != null && !status.IsDisposed) status.TestBusy(false);
            string message;
            switch (result)
            {
                case "foreground": message = "Codex 已切到前台。"; break;
                case "already-foreground": message = "Codex 已在前台；切到其他软件后再试。"; break;
                case "window-not-found": message = "未找到 Codex 窗口，请先打开 Codex。"; break;
                case "shown-focus-denied": message = "窗口已显示，但 Windows 没有交出键盘焦点。"; break;
                case "focus-denied": message = "Windows 阻止了本次切换。"; break;
                default: message = result; break;
            }
            Balloon("窗口测试结果", message);
            if (status != null && !status.IsDisposed) status.TestResult(message);
        }
        void Balloon(string title, string text) { if (!closing) tray.ShowBalloonTip(3000, title, text, ToolTipIcon.Info); }
        void ShowError(Exception error) { MessageBox.Show(error.Message, "Codex 提醒助手", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        public void Quit()
        {
            if (closing) return;
            try { store.Update("enabled", false); } catch { /* Ownership gate still stops reminders on process exit. */ }
            closing = true;
            timer.Stop();
            tray.Visible = false;
            if (status != null) status.Close();
            ExitThread();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose(); tray.Dispose(); menu.Dispose();
                activeIcon.Dispose(); pausedIcon.Dispose();
                showEvent.Dispose(); exitEvent.Dispose(); dispatcher.Dispose();
                if (status != null) status.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    static class UiPalette
    {
        public static readonly Color Accent = Color.FromArgb(22, 132, 99);
        public static readonly Color Ink = Color.FromArgb(36, 48, 59);
        public static readonly Color Muted = Color.FromArgb(109, 122, 133);
        public static readonly Color Border = Color.FromArgb(224, 231, 235);
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        public static void Tick(Graphics g, RectangleF r)
        {
            using (Pen pen = new Pen(Color.White, Math.Max(1.8f, r.Width / 11)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawLines(pen, new PointF[] {
                    new PointF(r.X + r.Width * .25f, r.Y + r.Height * .51f),
                    new PointF(r.X + r.Width * .44f, r.Y + r.Height * .70f),
                    new PointF(r.X + r.Width * .76f, r.Y + r.Height * .32f) });
            }
        }
    }

    sealed class RoundedReminderMenu : ContextMenuStrip
    {
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateCorners(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateCorners(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateCorners(); }
        void UpdateCorners()
        {
            if (Width < 24 || Height < 24) return;
            Region previous = Region;
            using (GraphicsPath shape = UiPalette.Round(new RectangleF(0, 0, Width, Height), 10f * DeviceDpi / 96f)) Region = new Region(shape);
            if (previous != null) previous.Dispose();
        }
    }

    sealed class ReminderMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(251, 253, 252));
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = UiPalette.Round(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 10f * e.Graphics.DpiX / 96f))
            using (Pen p = new Pen(UiPalette.Border)) e.Graphics.DrawPath(p, shape);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = UiPalette.Round(new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), 6))
            using (Brush fill = new SolidBrush(Color.FromArgb(232, 245, 239))) e.Graphics.FillPath(fill, shape);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? UiPalette.Muted : e.Item.ForeColor == Color.FromArgb(157, 67, 67) ? e.Item.ForeColor : UiPalette.Ink;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF box = e.ImageRectangle;
            using (GraphicsPath shape = UiPalette.Round(box, 4))
            using (Brush fill = new SolidBrush(UiPalette.Accent)) e.Graphics.FillPath(fill, shape);
            UiPalette.Tick(e.Graphics, box);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (Pen p = new Pen(UiPalette.Border)) e.Graphics.DrawLine(p, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2);
        }
    }

    sealed class SettingsCard : Panel
    {
        public bool ShowDivider = true;
        public SettingsCard() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = UiPalette.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 12))
            using (Brush fill = new SolidBrush(Color.White))
            using (Pen p = new Pen(UiPalette.Border)) { e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(p, shape); }
            if (ShowDivider) using (Pen p = new Pen(UiPalette.Border)) e.Graphics.DrawLine(p, 20, Height / 2, Width - 20, Height / 2);
        }
    }

    sealed class ToggleRow : CheckBox
    {
        public string Description = "";
        public bool Compact;
        bool hovered;
        public ToggleRow()
        {
            AutoSize = false; BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 10);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = g.DpiX / 96f;
            g.Clear(Color.White);
            if (hovered || Focused)
                using (GraphicsPath shape = UiPalette.Round(new RectangleF(0, 0, Width - 1, Height - 1), 8 * scale))
                using (Brush fill = new SolidBrush(Color.FromArgb(246, 250, 248))) g.FillPath(fill, shape);
            float boxSize = (Compact ? 21 : 26) * scale;
            RectangleF box = new RectangleF(Width - 40 * scale, (Height - boxSize) / 2, boxSize, boxSize);
            using (GraphicsPath shape = UiPalette.Round(box, 7 * scale))
            using (Brush fill = new SolidBrush(Checked && Enabled ? UiPalette.Accent : Color.White))
            using (Pen p = new Pen(Checked && Enabled ? UiPalette.Accent : Color.FromArgb(190, 202, 207), 1.4f))
            { g.FillPath(fill, shape); g.DrawPath(p, shape); }
            if (Checked) UiPalette.Tick(g, box);
            Rectangle title = new Rectangle((int)(14 * scale), Compact ? 0 : (int)(15 * scale), (int)(Width - 76 * scale), Compact ? Height : (int)(27 * scale));
            TextRenderer.DrawText(g, Text, Font, title, Enabled ? UiPalette.Ink : UiPalette.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!Compact) using (Font detail = new Font("Microsoft YaHei UI", 9))
                TextRenderer.DrawText(g, Description, detail, new Rectangle(title.X, (int)(44 * scale), title.Width, (int)(24 * scale)), UiPalette.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(3, 3, Width - 6, Height - 6));
        }
    }

    sealed class RoundedButton : Button
    {
        public bool Primary;
        bool hovered;
        public RoundedButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; SetStyle(ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent.BackColor);
            Color fill = !Enabled ? Color.FromArgb(226, 233, 234) : Primary ? (hovered ? Color.FromArgb(17, 114, 85) : UiPalette.Accent) : hovered ? Color.FromArgb(237, 244, 242) : Color.White;
            using (GraphicsPath shape = UiPalette.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9))
            using (Brush b = new SolidBrush(fill))
            using (Pen p = new Pen(Primary ? fill : UiPalette.Border)) { g.FillPath(b, shape); g.DrawPath(p, shape); }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, !Enabled ? UiPalette.Muted : Primary ? Color.White : UiPalette.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(5, 5, Width - 10, Height - 10));
        }
    }

    sealed class StatusForm : Form
    {
        readonly Label state, testResult;
        readonly Button test;
        readonly CheckBox popup, sound, completed, approval, question;
        bool updating;
        public StatusForm(TrayContext owner)
        {
            Text = "Codex 提醒助手";
            SuspendLayout();
            ClientSize = new Size(460, 562);
            Font = new Font("Microsoft YaHei UI", 9);
            BackColor = Color.FromArgb(245, 248, 249);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            using (Icon icon = Icons.Make(true)) Icon = (Icon)icon.Clone();
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Label title = new Label { Text = "Codex 提醒助手", Location = new Point(27, 22), Size = new Size(270, 34), Font = new Font(Font.FontFamily, 16, FontStyle.Bold), ForeColor = UiPalette.Ink };
            state = new Label { Location = new Point(331, 29), Size = new Size(102, 25), TextAlign = ContentAlignment.MiddleRight, Font = new Font(Font.FontFamily, 8.5f) };
            Label subtitle = new Label { Text = "完成工作或需要你时，把 Codex 带回前台。", Location = new Point(29, 68), Size = new Size(402, 25), Font = new Font(Font.FontFamily, 8.5f), ForeColor = UiPalette.Muted };
            SettingsCard card = new SettingsCard { Location = new Point(27, 105), Size = new Size(406, 174), BackColor = BackColor };
            popup = new ToggleRow { Text = "弹出提醒", AccessibleName = "弹出提醒", AccessibleDescription = "所有提醒的总开关", Description = "总开关；只在下方勾选的情况弹回窗口", Location = new Point(12, 4), Size = new Size(382, 77), TabIndex = 0 };
            sound = new ToggleRow { Text = "声音提醒", AccessibleName = "声音提醒", AccessibleDescription = "勾选播放提示音，取消勾选静音", Description = "弹出窗口时播放提示音", Location = new Point(12, 92), Size = new Size(382, 77), TabIndex = 1 };
            popup.CheckedChanged += delegate { if (!updating) owner.SetEnabled(popup.Checked); };
            sound.CheckedChanged += delegate { if (!updating) owner.SetSound(sound.Checked); };
            card.Controls.AddRange(new Control[] { popup, sound });
            card.TabIndex = 0;
            SettingsCard timing = new SettingsCard { Location = new Point(27, 295), Size = new Size(406, 160), BackColor = BackColor, ShowDivider = false, TabIndex = 1 };
            Label timingTitle = new Label { Text = "什么时候弹出（可多选）", Location = new Point(25, 13), Size = new Size(350, 24), ForeColor = UiPalette.Muted, BackColor = Color.White, Font = new Font(Font.FontFamily, 8.5f) };
            completed = new ToggleRow { Text = "回复完成时", AccessibleName = "回复完成时", AccessibleDescription = "一个聊天回合结束时提醒", Compact = true, Font = Font, Location = new Point(12, 40), Size = new Size(382, 34), TabIndex = 0 };
            approval = new ToggleRow { Text = "需要审批时", AccessibleName = "需要审批时", AccessibleDescription = "申请权限或工具确认时提醒", Compact = true, Font = Font, Location = new Point(12, 78), Size = new Size(382, 34), TabIndex = 1 };
            question = new ToggleRow { Text = "需要回答问题时", AccessibleName = "需要回答问题时", AccessibleDescription = "Codex 调用提问工具时提醒", Compact = true, Font = Font, Location = new Point(12, 116), Size = new Size(382, 34), TabIndex = 2 };
            completed.CheckedChanged += delegate { if (!updating) owner.SetNotificationType("notify_completed", completed.Checked); };
            approval.CheckedChanged += delegate { if (!updating) owner.SetNotificationType("notify_approval", approval.Checked); };
            question.CheckedChanged += delegate { if (!updating) owner.SetNotificationType("notify_question", question.Checked); };
            timing.Controls.AddRange(new Control[] { timingTitle, completed, approval, question });
            test = new RoundedButton { Text = "测试弹出", Primary = true, Location = new Point(27, 475), Size = new Size(193, 40), TabIndex = 2 };
            test.Click += delegate { owner.TestWindow(); };
            Button help = new RoundedButton { Text = "使用说明", Location = new Point(240, 475), Size = new Size(193, 40), TabIndex = 3 };
            help.Click += delegate { owner.OpenGuide(); };
            testResult = new Label { Location = new Point(29, 532), Size = new Size(402, 26), ForeColor = UiPalette.Muted, Font = new Font(Font.FontFamily, 8.5f), Text = "设置立即生效。关闭窗口后，程序继续留在托盘。" };
            Controls.AddRange(new Control[] { title, state, subtitle, card, timing, test, help, testResult });
            ResumeLayout(true);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }
        public void UpdateState(bool on, bool audio, bool error, bool onCompleted, bool onApproval, bool onQuestion)
        {
            updating = true;
            state.Text = error ? "配置异常" : !on ? "● 已暂停" : !(onCompleted || onApproval || onQuestion) ? "未选提醒时机" : "● 已开启";
            state.ForeColor = on && !error ? UiPalette.Accent : UiPalette.Muted;
            popup.Checked = on; popup.Enabled = !error;
            sound.Checked = audio; sound.Enabled = !error;
            completed.Checked = onCompleted; approval.Checked = onApproval; question.Checked = onQuestion;
            completed.Enabled = approval.Enabled = question.Enabled = !error;
            ((ToggleRow)sound).Description = on ? "弹出窗口时播放提示音" : "弹出提醒暂停中，声音偏好已保留";
            sound.Invalidate();
            updating = false;
        }
        public void TestBusy(bool busy) { test.Enabled = !busy; if (busy) testResult.Text = "正在测试窗口切换……"; }
        public void TestResult(string message) { testResult.Text = message; }
    }
}
