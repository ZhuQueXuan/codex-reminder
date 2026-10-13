using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace CodexReminderInstaller
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool extractOnly = args.Length == 2 && args[0] == "--extract-only";
            try
            {
                if (extractOnly) { Payload.Extract(args[1]); return 0; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length == 2 && args[0] == "--preview")
                {
                    using (InstallerForm form = new InstallerForm())
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000);
                        form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(args[1]); }
                    }
                    return 0;
                }
                Application.Run(new InstallerForm());
                return 0;
            }
            catch (Exception error)
            {
                if (extractOnly)
                    File.WriteAllText(Path.Combine(args[1], "extraction-error.txt"), error.ToString(), new UTF8Encoding(false));
                else MessageBox.Show("安装未完成：\r\n" + error.Message, "Codex 提醒助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    public static class Payload
    {
        static readonly string[] Files = new string[] {
            "Attention.ps1", "App/CodexReminder.exe", "App/CodexReminder.exe.config",
            "Src/Attention.ps1", "Src/Settings.ps1", "Src/FocusWindow.cs", "Src/Manage.ps1", "Src/ReviewHooks.ps1", "Src/Setup.ps1",
            "Config/settings.json", "Docs/使用说明.md", "先看这里.txt", "首次安装并启动.cmd", "启动提醒助手.cmd", "打开事件审阅终端.cmd"
        };

        static string Hash(Stream stream)
        { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""); }

        public static void Extract(string destination)
        {
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            HashSet<string> allowed = new HashSet<string>(Files, StringComparer.Ordinal);
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexReminder.Payload"))
            {
                if (resource == null || Hash(resource) != ReleaseInfo.PayloadHash) throw new InvalidDataException("安装包内容校验失败。");
                resource.Position = 0;
                using (ZipArchive zip = new ZipArchive(resource, ZipArchiveMode.Read))
                {
                    // Validate the complete archive before writing any files.
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (name.EndsWith("/")) continue;
                        const string prefix = "Codex提醒助手/";
                        if (!name.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("安装包目录无效。");
                        string relative = name.Substring(prefix.Length);
                        if (!allowed.Contains(relative) || !found.Add(relative)) throw new InvalidDataException("安装包文件清单无效。");
                        string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装目录无效。");
                    }
                    if (found.Count != allowed.Count) throw new InvalidDataException("安装包缺少必要文件。");
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (name.EndsWith("/")) continue;
                        string relative = name.Substring("Codex提醒助手/".Length);
                        string target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                        // Reinstall preserves the user's switches and any unrelated configuration fields.
                        if (relative == "Config/settings.json" && File.Exists(target)) continue;
                        using (Stream input = entry.Open())
                        using (MemoryStream contents = new MemoryStream())
                        {
                            input.CopyTo(contents);
                            byte[] bytes = contents.ToArray();
                            if (File.Exists(target))
                            {
                                contents.Position = 0;
                                using (FileStream old = File.OpenRead(target))
                                    if (Hash(old) == Hash(contents)) continue;
                            }
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            // Only explicitly allowlisted files are replaced. No removal or cleanup pass.
                            using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                                output.Write(bytes, 0, bytes.Length);
                        }
                    }
                }
            }
        }
    }

    sealed class InstallerForm : Form
    {
        readonly TextBox location;
        readonly CheckBox shortcut;
        readonly Button install, browse;
        readonly Label status;
        bool busy;
        public InstallerForm()
        {
            SuspendLayout();
            Text = "安装 Codex 提醒助手";
            Font = new Font("Microsoft YaHei UI", 9);
            ClientSize = new Size(490, 360);
            BackColor = Color.FromArgb(246, 249, 248);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexReminder.Icon"))
                if (icon != null) Icon = new Icon(icon);
            Label title = new Label { Text = "Codex 提醒助手", Location = new Point(26, 22), Size = new Size(430, 34), Font = new Font(Font.FontFamily, 16, FontStyle.Bold), ForeColor = Color.FromArgb(30, 48, 57) };
            Label intro = new Label { Text = "完成回复、需要审批或回答问题时，让你及时看到 Codex。", Location = new Point(28, 65), Size = new Size(432, 44), ForeColor = Color.FromArgb(91, 112, 121) };
            Label pathTitle = new Label { Text = "安装位置", Location = new Point(28, 119), Size = new Size(400, 24) };
            location = new TextBox { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexReminder", ReleaseInfo.Version), Location = new Point(28, 148), Size = new Size(344, 28), ReadOnly = true, BackColor = Color.White, TabStop = false };
            browse = new Button { Text = "更改…", Location = new Point(382, 146), Size = new Size(80, 30), TabIndex = 1 };
            browse.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择存放提醒助手的文件夹（需要可写）", SelectedPath = location.Text })
                    if (dialog.ShowDialog(this) == DialogResult.OK) location.Text = Path.Combine(dialog.SelectedPath, "CodexReminder-" + ReleaseInfo.Version);
            };
            shortcut = new CheckBox { Text = "创建桌面快捷方式", Checked = true, Location = new Point(28, 193), Size = new Size(400, 25), TabIndex = 2 };
            Label note = new Label { Text = "首次使用还需在 Codex 中信任提醒事件。\r\n提醒类型可在托盘的“设置与状态”中自由选择。", Location = new Point(28, 235), Size = new Size(434, 48), ForeColor = Color.FromArgb(91, 112, 121) };
            status = new Label { Text = "版本 " + ReleaseInfo.Version + " · 无需手动解压", Location = new Point(28, 308), Size = new Size(283, 33), ForeColor = Color.FromArgb(91, 112, 121) };
            install = new Button { Text = "安装并启动", Location = new Point(321, 300), Size = new Size(141, 39), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(25, 147, 105), ForeColor = Color.White, TabIndex = 3 };
            install.FlatAppearance.BorderSize = 0; install.Click += delegate { BeginInstall(); };
            AcceptButton = install;
            Controls.AddRange(new Control[] { title, intro, pathTitle, location, browse, shortcut, note, status, install });
            ResumeLayout(true);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
        }

        async void BeginInstall()
        {
            busy = true; install.Enabled = browse.Enabled = shortcut.Enabled = false;
            status.Text = "正在安装，请稍候…";
            string destination = location.Text;
            bool makeShortcut = shortcut.Checked;
            try
            {
                await Task.Run(delegate { Payload.Extract(destination); });
                if (makeShortcut) CreateShortcut(destination, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                status.Text = "请按安装提示完成 Codex 事件信任。";
                string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                int result = await Task.Run(delegate
                {
                    using (Process process = Process.Start(new ProcessStartInfo(shell,
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Path.Combine(destination, "Src", "Setup.ps1") + "\"")
                        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = destination }))
                    { process.WaitForExit(); return process.ExitCode; }
                });
                if (result != 0) throw new IOException("提醒配置未安装成功，请根据刚才的提示处理后重试。");
                status.Text = "安装完成，程序已在右下角托盘运行。";
                busy = false; Close();
            }
            catch (Exception error)
            {
                status.Text = "安装未完成，可处理后重试。";
                MessageBox.Show(this, error.Message + "\r\n如果程序文件正在使用，请先退出旧版托盘程序。", "Codex 提醒助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { busy = false; if (!IsDisposed) install.Enabled = browse.Enabled = shortcut.Enabled = true; }
        }

        static void CreateShortcut(string root, string desktop)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type), link = null;
            try
            {
                string path = Path.Combine(desktop, "Codex提醒助手.lnk");
                link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Path.Combine(root, "App", "CodexReminder.exe") });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { root });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "启动 Codex 提醒助手" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            finally
            {
                if (link != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
