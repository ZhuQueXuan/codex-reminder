using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace CodexReminder
{
    public sealed class PendingApprovalSignal
    {
        public readonly string Key;
        public PendingApprovalSignal(string key) { Key = key; }
    }

    public sealed class ApprovalSignalGate
    {
        string lastKey;
        string candidateKey;
        int stableScans;
        int emptyScans;
        public bool Observe(PendingApprovalSignal signal)
        {
            if (signal == null) { candidateKey = null; stableScans = 0; if (++emptyScans >= 2) lastKey = null; return false; }
            emptyScans = 0;
            if (lastKey == signal.Key) return false;
            if (candidateKey != signal.Key) { candidateKey = signal.Key; stableScans = 1; return false; }
            if (++stableScans < 2) return false;
            lastKey = signal.Key;
            candidateKey = null; stableScans = 0;
            return true;
        }
        public void Reset() { lastKey = candidateKey = null; emptyScans = stableScans = 0; }
    }

    public static class ApprovalObserver
    {
        public static volatile string LastReadStage = "idle";
        static readonly Regex Accept = new Regex(@"^(?:approve(?:\s+(?:once|only.*|for.*|this.*))?|allow(?:\s+(?:once|only.*|for.*|this.*))?|accept|confirm|批准|批准一次|本次批准|始终批准|允许|允许一次|本次允许|允许本次|始终允许|允许并记住|接受|同意|确认)(?:\s*\([^)]*\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Reject = new Regex(@"^(?:decline|reject|deny|cancel|拒绝|不允许|不同意|取消)(?:\s*\([^)]*\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Waiting = new Regex(@"^(?:waiting for approval|awaiting approval|approval required|needs approval|等待批准|等待审批|需要批准|需要审批)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static PendingApprovalSignal Scan()
        {
            IntPtr window = CodexAttentionWindow.Find();
            return window == IntPtr.Zero ? null : ScanWindow(window);
        }

        // Read-only: inspect decision controls and status metadata only.
        // No Invoke/Select/Value pattern, keyboard/mouse input or document text.
        public static PendingApprovalSignal ScanWindow(IntPtr handle)
        {
            LastReadStage = "window-lookup";
            AutomationElement root = AutomationElement.FromHandle(handle);
            if (root == null) return null;
            LastReadStage = "window-identity";
            string rootKey = Identity(root);
            System.Windows.Automation.Condition condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            Dictionary<string, int> decisions = new Dictionary<string, int>();
            CacheRequest cache = new CacheRequest();
            {
                cache.TreeScope = TreeScope.Element;
                cache.Add(AutomationElement.NameProperty);
                cache.Add(AutomationElement.ControlTypeProperty);
                cache.Add(AutomationElement.IsEnabledProperty);
                cache.Add(AutomationElement.ItemStatusProperty);
                cache.Add(AutomationElement.HelpTextProperty);
                using (cache.Activate())
                {
                    LastReadStage = "find-controls";
                    AutomationElementCollection controls = root.FindAll(TreeScope.Descendants, condition);
                    for (int i = 0; i < controls.Count; i++)
                    {
                        AutomationElement element = controls[i];
                        LastReadStage = "inspect-controls";
                        try
                        {
                            if (!element.Cached.IsEnabled) continue;
                            string name = (element.Cached.Name ?? "").Trim().Replace("&", "");
                            string state = (element.Cached.ItemStatus ?? "").Trim();
                            string help = (element.Cached.HelpText ?? "").Trim();
                            if (Waiting.IsMatch(state) || Waiting.IsMatch(help)) return new PendingApprovalSignal("status:" + Identity(element));
                            if (element.Cached.ControlType != ControlType.Button) continue;
                            int bit = Accept.IsMatch(name) ? 1 : Reject.IsMatch(name) ? 2 : 0;
                            if (bit == 0) continue;
                            AutomationElement parent = element;
                            for (int level = 0; level < 3; level++)
                            {
                                parent = TreeWalker.ControlViewWalker.GetParent(parent);
                                if (parent == null) break;
                                // Do not combine decision buttons across the entire Codex window.
                                string key = Identity(parent);
                                bool isRoot = key == rootKey;
                                if (parent.Current.ControlType == ControlType.Window && !isRoot) break;
                                if (isRoot && level > 0) break;
                                int seen; decisions.TryGetValue(key, out seen);
                                seen |= bit; decisions[key] = seen;
                                if (seen == 3) return new PendingApprovalSignal("decision:" + key);
                                if (isRoot) break;
                            }
                        }
                        catch (ElementNotAvailableException) { }
                        catch (COMException) { /* Ignore a control that vanished during the scan. */ }
                    }
                }
            }
            return null;
        }

        static string Identity(AutomationElement element) { return String.Join("-", element.GetRuntimeId()); }
    }
}
