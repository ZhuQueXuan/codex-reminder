using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodexReminder;

class ObserverVerification
{
    static void Assert(bool c, string m) { if (!c) throw new Exception(m); }
    static PendingApprovalSignal Scenario(string fixture, string folder, string mode)
    {
        string receipt = Path.Combine(folder, mode + "-" + Guid.NewGuid().ToString("N") + ".txt");
        ProcessStartInfo start = new ProcessStartInfo(fixture, mode + " \"" + receipt + "\"");
        start.UseShellExecute = false;
        using (Process process = Process.Start(start))
        {
            try
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while (!File.Exists(receipt) && DateTime.UtcNow < deadline) Thread.Sleep(20);
                Assert(File.Exists(receipt), "Fixture did not start");
                IntPtr handle = new IntPtr(Int64.Parse(File.ReadAllText(receipt)));
                Task<PendingApprovalSignal> scan = Task.Factory.StartNew(() => ApprovalObserver.ScanWindow(handle));
                Assert(scan.Wait(8000), "Read-only scan timed out at " + ApprovalObserver.LastReadStage);
                Assert(!File.Exists(receipt + ".clicked"), "Observer clicked a decision button");
                return scan.Result;
            }
            finally { if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(1000)) process.Kill(); } }
        }
    }
    static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(args[1]);
            PendingApprovalSignal first = Scenario(args[0], args[1], "browser");
            Assert(first != null, "Browser confirmation missed");
            ApprovalSignalGate gate = new ApprovalSignalGate();
            Assert(!gate.Observe(first), "A transient decision should not notify immediately");
            Assert(gate.Observe(first), "Sustained request did not notify");
            Assert(!gate.Observe(first), "Waiting request repeatedly notifies");
            gate.Observe(null); gate.Observe(null);
            Assert(!gate.Observe(first) && gate.Observe(first), "Next sustained request after resolution did not notify");
            gate.Reset();
            Assert(!gate.Observe(first), "First transient request notified");
            gate.Observe(null);
            Assert(!gate.Observe(first), "An interrupted request was treated as stable");
            gate.Observe(new PendingApprovalSignal("different-decision"));
            Assert(!gate.Observe(first) && gate.Observe(first), "Changed decision did not restart confirmation");
            Assert(Scenario(args[0], args[1], "chinese") != null, "Chinese approval missed");
            Assert(Scenario(args[0], args[1], "resolved") == null, "Resolved controls falsely trigger");
            Assert(Scenario(args[0], args[1], "settings") == null, "Ordinary settings falsely trigger");
            Console.WriteLine("Cross-process native UIA fixtures, Chinese approval, resolved/ordinary controls and deduplication: passed.");
            Console.WriteLine("No decision button was clicked; no user/Codex window was operated.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
