using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

static class MenuVerification
{
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(object target, string name) { return target.GetType().GetField(name, Fields).GetValue(target); }
    static void Call(object target, string name, params object[] args) { target.GetType().GetMethod(name).Invoke(target, args); }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Dictionary<string, object> Read(string root) { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "Config", "settings.json"))); }
    static void Save(Control control, string path)
    {
        Form form = control as Form;
        if (form != null)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
        }
        control.CreateControl();
        using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
        {
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            if (form == null) bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            else
            {
                Point origin = form.PointToScreen(Point.Empty);
                Rectangle client = new Rectangle(origin.X - form.Left, origin.Y - form.Top, form.ClientSize.Width, form.ClientSize.Height);
                using (Bitmap cropped = bitmap.Clone(client, bitmap.PixelFormat)) cropped.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                form.Hide();
            }
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string assemblyPath = args[0], scratch = args[1], docs = args[2];
            Directory.CreateDirectory(Path.Combine(scratch, "Config"));
            File.WriteAllText(Path.Combine(scratch, "Config", "settings.json"), "{\"enabled\":true,\"sound\":true,\"marker\":\"keep\"}");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Assembly app = Assembly.LoadFrom(assemblyPath);
            object store = Activator.CreateInstance(app.GetType("CodexReminder.SettingsStore"), new object[] { scratch });
            object context = Activator.CreateInstance(app.GetType("CodexReminder.TrayContext"), new object[] { store, false });
            using ((IDisposable)context)
            {
                ((System.Windows.Forms.Timer)Field(context, "timer")).Stop();
                ContextMenuStrip menu = (ContextMenuStrip)Field(context, "menu");
                ToolStripMenuItem popup = (ToolStripMenuItem)Field(context, "reminderItem");
                ToolStripMenuItem sound = (ToolStripMenuItem)Field(context, "soundItem");
                int popupCount = 0;
                foreach (ToolStripItem item in menu.Items)
                {
                    Assert(item.Text != "开启提醒" && item.Text != "关闭提醒", "Duplicate enable/disable controls still exist");
                    if (item.Text == "弹出提醒") popupCount++;
                }
                Assert(popupCount == 1 && popup.Checked && sound.Checked, "Initial check states incorrect");
                popup.PerformClick();
                Assert(!(bool)Read(scratch)["enabled"] && popup.Checked == false && sound.Checked, "Popup toggle did not preserve sound preference");
                popup.PerformClick();
                Assert((bool)Read(scratch)["enabled"] && popup.Checked, "Popup re-enable failed");
                sound.PerformClick();
                Assert(!(bool)Read(scratch)["sound"] && sound.Checked == false && popup.Checked, "Sound toggle affected popup setting");
                sound.PerformClick();
                Form form = (Form)Activator.CreateInstance(app.GetType("CodexReminder.StatusForm"), new object[] { context });
                context.GetType().GetField("status", Fields).SetValue(context, form);
                Call(form, "UpdateState", true, true, false, true, true, true);
                CheckBox formPopup = (CheckBox)Field(form, "popup");
                CheckBox formSound = (CheckBox)Field(form, "sound");
                CheckBox completed = (CheckBox)Field(form, "completed");
                CheckBox approval = (CheckBox)Field(form, "approval");
                CheckBox question = (CheckBox)Field(form, "question");
                Assert(completed.Checked && approval.Checked && question.Checked, "Old settings should enable all reminder types");
                Assert(formPopup.Checked && formSound.Checked, "Form and menu states differ");
                formPopup.Checked = false;
                Assert(!(bool)Read(scratch)["enabled"] && !popup.Checked && formSound.Checked, "Form toggle did not update menu/config");
                formPopup.Checked = true;
                Assert((bool)Read(scratch)["enabled"] && popup.Checked, "Form re-enable failed");
                completed.Checked = false;
                approval.Checked = false;
                Assert(!(bool)Read(scratch)["notify_completed"] && !(bool)Read(scratch)["notify_approval"] && question.Checked, "Independent type selection failed");
                Assert(popup.Checked && sound.Checked, "Type selection changed global or sound setting");
                formPopup.Checked = false; formPopup.Checked = true;
                Assert(!completed.Checked && !approval.Checked && question.Checked, "Pause/resume reset type preferences");
                completed.Checked = true; question.Checked = false;
                Assert(completed.Checked && !approval.Checked && !question.Checked, "Completion-only selection failed");
                Call(context, "SetNotificationType", "notify_approval", true);
                Assert(completed.Checked && approval.Checked && !question.Checked, "External update did not synchronize controls");
                completed.Checked = false; approval.Checked = false;
                Assert(((Label)Field(form, "state")).Text == "未选提醒时机", "Empty selection needs a visible explanation");
                completed.Checked = true; approval.Checked = true; question.Checked = true;
                Assert((string)Read(scratch)["marker"] == "keep", "Unrelated settings were lost");
                Call(form, "UpdateState", true, true, false, true, true, true);
                menu.PerformLayout();
                menu.Size = menu.GetPreferredSize(Size.Empty);
                Assert(menu.Font.Size <= 9.1f, "Menu font is still oversized");
                Assert(menu.Region != null && !menu.Region.IsVisible(0, 0), "Menu outer corners are not clipped");
                Save(menu, Path.Combine(docs, "菜单预览.png"));
                Save(form, Path.Combine(docs, "设置预览.png"));
                Console.WriteLine("Single popup toggle, two-way checks, menu/form sync, sound preference and unrelated fields: passed.");
                Console.WriteLine("Three independent event toggles, all/one/none selections and pause/resume preference preservation: passed.");
                Console.WriteLine("Offscreen previews rendered from the actual controls; no user windows operated.");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
