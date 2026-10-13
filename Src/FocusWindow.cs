using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

public static class CodexAttentionWindow
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);

    public static bool IsForeground(IntPtr h)
    {
        return h != IntPtr.Zero && GetForegroundWindow() == h && !IsIconic(h);
    }

    public static int ProcessId(IntPtr h)
    {
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        return (int)pid;
    }

    public static void Restore(IntPtr h)
    {
        if (h != IntPtr.Zero && IsIconic(h)) { ShowWindowAsync(h, 9); Thread.Sleep(80); }
    }

    public static IntPtr Find()
    {
        IntPtr candidate = IntPtr.Zero;
        DateTime newest = DateTime.MinValue;
        foreach (Process p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    string name = p.ProcessName;
                    if (!name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("Codex", StringComparison.OrdinalIgnoreCase)) continue;
                    IntPtr h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;
                    string path = p.MainModule.FileName;
                    bool codexApp = path.IndexOf("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        path.EndsWith("\\Codex.exe", StringComparison.OrdinalIgnoreCase);
                    if (!codexApp) continue;
                    if (h == GetForegroundWindow()) return h;
                    DateTime started = p.StartTime;
                    if (started > newest) { newest = started; candidate = h; }
                }
                catch { /* Ignore inaccessible and exited processes. */ }
            }
        }
        return candidate;
    }

    // Result distinguishes actual focus from merely showing the window above others.
    public static string Reveal(IntPtr h, int holdMilliseconds)
    {
        if (h == IntPtr.Zero) return "window-not-found";
        if (GetForegroundWindow() == h && !IsIconic(h)) return "already-foreground";
        bool wasTopmost = (GetWindowLong(h, -20) & 0x8) != 0;
        bool promoted = false;
        uint current = GetCurrentThreadId();
        uint unused;
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out unused);
        uint target = GetWindowThreadProcessId(h, out unused);
        bool attachedForeground = false, attachedTarget = false;
        try
        {
            ShowWindowAsync(h, IsIconic(h) ? 9 : 5);
            Thread.Sleep(80);
            SetForegroundWindow(h);
            if (GetForegroundWindow() != h)
            {
                if (foreground != 0 && foreground != current)
                    attachedForeground = AttachThreadInput(current, foreground, true);
                if (target != 0 && target != current && target != foreground)
                    attachedTarget = AttachThreadInput(current, target, true);
                SetForegroundWindow(h);
            }
        }
        finally
        {
            if (attachedTarget) AttachThreadInput(current, target, false);
            if (attachedForeground) AttachThreadInput(current, foreground, false);
        }
        try
        {
            if (!wasTopmost)
                promoted = SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x40);
            SetForegroundWindow(h);
            Thread.Sleep(Math.Max(0, Math.Min(holdMilliseconds, 2000)));
            if (GetForegroundWindow() == h) return "foreground";
            return IsWindowVisible(h) && (promoted || wasTopmost) ? "shown-focus-denied" : "focus-denied";
        }
        finally
        {
            // Preserve topmost state that existed before this tool ran.
            if (promoted && !wasTopmost)
                SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }
    }
}
