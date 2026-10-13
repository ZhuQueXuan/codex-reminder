using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

class ApprovalFixture
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        using (Form form = new Form { Text = "Approval test fixture", StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false, ClientSize = new Size(350, 120) })
        using (Timer lifetime = new Timer { Interval = 15000 })
        {
            Button accept = new Button { Text = args[0] == "chinese" ? "允许一次" : args[0] == "settings" ? "Save" : "Accept", Location = new Point(20, 30), Enabled = args[0] != "resolved" };
            Button reject = new Button { Text = args[0] == "chinese" ? "拒绝" : args[0] == "settings" ? "Cancel" : "Decline", Location = new Point(150, 30), Enabled = args[0] != "resolved" };
            accept.Click += delegate { File.WriteAllText(args[1] + ".clicked", "clicked"); };
            reject.Click += delegate { File.WriteAllText(args[1] + ".clicked", "clicked"); };
            form.Controls.Add(accept); form.Controls.Add(reject);
            form.Shown += delegate { File.WriteAllText(args[1], form.Handle.ToInt64().ToString()); };
            lifetime.Tick += delegate { form.Close(); };
            lifetime.Start();
            Application.Run(form);
        }
    }
}
