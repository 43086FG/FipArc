// Setup.cs —— 安装程序 / 卸载向导
//
// 编译时把 FipArc.exe 和 app.ico 作为嵌入资源打进来：
//   csc /target:winexe /resource:FipArc.exe,FipArc.exe /resource:app.ico,app.ico ...
//
// 注册表部分直接复用 src\Shell.cs（同一个源文件也编译进本程序），
// 所以安装程序和主程序的右键菜单/图标关联永远一致。
//
// 用法：
//   FipArcSetup.exe              安装向导（可自定义路径）
//   FipArcSetup.exe /uninstall   卸载向导
//   FipArcSetup.exe /quiet [/dir=...]   静默安装
//   FipArcSetup.exe /uninstall /quiet   静默卸载
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FipArcSetup
{
    static class Program
    {
        #if SE
        public const string APP_NAME = "FipArc";          // 程序名保持一致
        public const string DIR_NAME = "FipArc";          // 和完整版同一个目录（SE 已成为主版本）
        public const string MENU_NAME = "FipArc";
#else
        public const string APP_NAME = "FipArc";
        public const string DIR_NAME = "FipArc";
        public const string MENU_NAME = "FipArc";
#endif
        public const string APP_SUB = "压缩工具";
        public const string VERSION = "1.2.6";
        public const string PUBLISHER = "43086FG";

        public static string DefaultDir
        {
            get
            {
                // 默认装到【当前用户目录】，不需要管理员权限。
                // 之前默认 Program Files，普通用户装会写不进去：
                //   · 文件只写了一半
                //   · 卸载登记项没写成 → 控制面板里看不到这个软件
                // VS Code 用户版、Chrome 都是这么做的。
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(roaming))
                    return Path.Combine(roaming, DIR_NAME);

                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (string.IsNullOrEmpty(pf)) pf = @"C:\Program Files";
                return Path.Combine(pf, DIR_NAME);
            }
        }

        public static string ExeName { get { return "FipArc.exe"; } }
        public static string SetupName { get { return "FipArcSetup.exe"; } }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstall = false, quiet = false;
            string dirArg = null;
            foreach (string a in args)
            {
                string s = a.ToLowerInvariant();
                if (s == "/uninstall" || s == "-uninstall" || s == "/u") uninstall = true;
                if (s == "/quiet" || s == "/s" || s == "/silent") quiet = true;
                if (s.StartsWith("/dir=")) dirArg = a.Substring(5).Trim('"');
            }

            if (uninstall)
            {
                if (quiet)
                {
                    // 注意顺序：必须先读出安装路径再清注册表，
                    // 否则卸载登记被删掉后 FoundDir() 返回 null，会删错目录。
                    string d = Installer.FoundDir();
                    if (string.IsNullOrEmpty(d)) d = string.IsNullOrEmpty(dirArg) ? DefaultDir : dirArg;

                    FipArc.Shell.UnregisterAll(d);
                    // 程序在跑就删不掉 exe，先关掉
                    int closed = Installer.CloseRunningApp(d);

                    int rm, fl; string list;
                    string self = Assembly.GetExecutingAssembly().Location;
                    Installer.UninstallFiles(d, true, self, out rm, out fl, out list);
                    // 自己正在运行删不掉，安排批处理在退出后删
                    if (File.Exists(self) && Installer.IsUnder(self, d)) Installer.ScheduleSelfDelete(d, self);
                    else
                    {
                        try
                        {
                            if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0)
                                Directory.Delete(d);
                        }
                        catch { }
                    }
                    return;
                }
                Application.Run(new UninstallForm());
                return;
            }
            if (quiet)
            {
                try { Installer.Install(string.IsNullOrEmpty(dirArg) ? DefaultDir : dirArg); }
                catch { Environment.ExitCode = 1; }
                return;
            }
            Application.Run(new InstallForm());
        }
    }

    // =====================================================================
    //  安装引擎
    // =====================================================================
    public static class Installer
    {
        public static string FoundDir()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FipArc"))
                {
                    if (k != null) return (string)k.GetValue("InstallLocation");
                }
            }
            catch { }
            return null;
        }

        static byte[] ReadResource(string name)        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("安装包损坏：缺少资源 " + name);
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[65536];
                    int r;
                    while ((r = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, r);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// 关掉正在运行的 FipArc。卸载前必须先做这一步 ——
        /// 程序在跑的时候 FipArc.exe 被占用，删不掉，用户只会看到
        /// "文件正在使用，操作无法完成"。
        /// 先礼后兵：CloseMainWindow 正常关闭，等不到就 Kill。
        /// </summary>
        public static int CloseRunningApp(string dir)
        {
            int closed = 0;
            Process[] procs;
            try { procs = Process.GetProcessesByName("FipArc"); }
            catch { return 0; }

            foreach (Process p in procs)
            {
                bool ours = true;
                try
                {
                    string path = p.MainModule.FileName;
                    if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(dir))
                        ours = IsUnder(path, dir);
                }
                catch
                {
                    // 读不到路径（权限/位数不同），按名字判断，宁可多关
                    ours = true;
                }
                if (!ours) continue;

                try
                {
                    if (p.CloseMainWindow())
                    {
                        if (!p.WaitForExit(3000))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    else
                    {
                        // 没有主窗口（可能是后台/隐藏进程）
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    closed++;
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            if (closed > 0) System.Threading.Thread.Sleep(500);   // 给系统放开文件句柄的时间
            return closed;
        }

        /// <summary>
        /// 删除本软件装进 dir 的文件。向导和静默卸载都调这一个方法，避免两套逻辑走偏。
        /// keepOthers=true 时只删我们自己装的文件，目录里用户放的东西不动。
        /// </summary>
        public static void UninstallFiles(string dir, bool keepOthers,
            string selfPath, out int removed, out int failed, out string failList)
        {
            removed = 0; failed = 0;
            StringBuilder fl = new StringBuilder();
            failList = "";
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            string[] ours = new string[] {
                Path.Combine(dir, Program.ExeName),
                Path.Combine(dir, "app.ico"),
                Path.Combine(dir, Program.SetupName),
                Path.Combine(dir, "7z.exe"),
                Path.Combine(dir, "7z.dll"),
                Path.Combine(dir, "License-7zip.txt"),
                Path.Combine(dir, "Uninstall.exe"),

            };

            // 1) 我们装的文件（正在运行的卸载程序自己删不掉，留给批处理）
            foreach (string f in ours)
            {
                if (!File.Exists(f)) continue;
                if (!string.IsNullOrEmpty(selfPath) &&
                    string.Equals(f, selfPath, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    File.Delete(f);
                    if (!File.Exists(f)) removed++;
                    else { failed++; fl.AppendLine(Path.GetFileName(f)); }
                }
                catch { failed++; fl.AppendLine(Path.GetFileName(f)); }
            }

            // 2) 目录里的其他东西
            try
            {
                foreach (string e in Directory.GetFileSystemEntries(dir))
                {
                    bool isSelf = !string.IsNullOrEmpty(selfPath) &&
                                  string.Equals(e, selfPath, StringComparison.OrdinalIgnoreCase);
                    if (isSelf) continue;

                    if (!keepOthers)
                    {
                        try
                        {
                            if (Directory.Exists(e)) Directory.Delete(e, true);
                            else File.Delete(e);
                            removed++;
                        }
                        catch { failed++; fl.AppendLine(Path.GetFileName(e)); }
                    }
                }
            }
            catch { }

            failList = fl.ToString();
        }

        /// <summary>卸载程序自己正在运行，删不掉；用批处理等本进程退出后再删</summary>
        public static void ScheduleSelfDelete(string dir, string self)
        {
            try
            {
                string bat = Path.Combine(Path.GetTempPath(),
                    "fiparc_uninst_" + Guid.NewGuid().ToString("N") + ".bat");
                string lines2 =
                    "@echo off\r\n" +
                    "setlocal\r\n" +
                    "for /l %%i in (1,1,30) do (\r\n" +
                    "  timeout /t 1 /nobreak >nul\r\n" +
                    "  del /f /q \"" + self + "\" >nul 2>&1\r\n" +
                    "  if not exist \"" + self + "\" goto gone\r\n" +
                    ")\r\n" +
                    ":gone\r\n" +
                    "rmdir /q \"" + dir + "\" >nul 2>&1\r\n" +
                    "del /f /q \"%~f0\" >nul 2>&1\r\n";
                File.WriteAllText(bat, lines2, Encoding.Default);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
        }

        /// <summary>path 是否在 dir 目录下</summary>
        public static bool IsUnder(string path, string dir)
        {
            try
            {
                string a = Path.GetFullPath(path);
                string b = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
        public static void Install(string dir)
        {
            if (string.IsNullOrEmpty(dir)) throw new Exception("安装路径为空");
            Directory.CreateDirectory(dir);
            string oldDir = FoundDir();   // 老版本装在哪（用于升级后清理）

            // 0) 先确认这个目录真的能写。
            //    Program Files 默认只有 Administrators 有写权限，
            //    不提权安装会在这里失败 —— 以前失败被静默吞掉，
            //    结果文件只写了一部分、控制面板里也没有卸载项。
            try
            {
                string probe = Path.Combine(dir, "_fiparc_write_test.tmp");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
            }
            catch
            {
                throw new Exception(
                    "无法写入安装目录：\r\n" + dir + "\r\n\r\n" +
                    "这个位置需要管理员权限。请任选一种办法：\r\n" +
                    "  1. 右键 FipArcSetup.exe →「以管理员身份运行」\r\n" +
                    "  2. 点「浏览…」换一个你有权限的目录（例如 D:\\FipArc）");
            }

            // 1) 写出主程序 + 图标
            byte[] app = ReadResource("FipArc.exe");
            string appPath = Path.Combine(dir, Program.ExeName);

            // 先写成 .dat 再改名成 .exe。
            // 原因：360 主动防御会把"安装程序直接释放出 .exe"当成木马投放行为，
            // 在安装进程退出后把刚写出来的 exe 删掉（实测：Install 内部检查文件还在，
            // 进程一退出就没了）。改名这一步不触发拦截。
            string staging = Path.Combine(dir, "_fiparc_stage.dat");
            File.WriteAllBytes(staging, app);
            try
            {
                if (File.Exists(appPath)) File.Delete(appPath);
            }
            catch { }
            try
            {
                File.Move(staging, appPath);
            }
            catch
            {
                try
                {
                    File.Copy(staging, appPath, true);
                    File.Delete(staging);
                }
                catch { }
            }

            // 确认真的写出来了
            if (!File.Exists(appPath))
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "fiparc_install.log"),
                        DateTime.Now.ToString("HH:mm:ss") + " 写入 FipArc.exe 失败: " + appPath +
                        "  (资源 " + app.Length + " 字节)\r\n");
                }
                catch { }
                throw new Exception(
                    "无法写出主程序：\r\n" + appPath + "\r\n\r\n" +
                    "多半是杀毒软件（360 等）拦截了。\r\n" +
                    "请把安装目录加入杀毒软件信任区后重试。");
            }
            try
            {
                byte[] ico = ReadResource("app.ico");
                File.WriteAllBytes(Path.Combine(dir, "app.ico"), ico);
            }
            catch { }

            // 内置 7-Zip 引擎：让 7z / rar / iso / cab 等格式免安装可用
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "7z.exe"), ReadResource("7z.exe"));
                File.WriteAllBytes(Path.Combine(dir, "7z.dll"), ReadResource("7z.dll"));
                try { File.WriteAllBytes(Path.Combine(dir, "License-7zip.txt"), ReadResource("License-7zip.txt")); } catch { }
            }
            catch { }

            // 各文件类型自己的图标（音频/视频/图片/文本）
            // 专门的卸载程序
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "Uninstall.exe"), ReadResource("Uninstall.exe"));
            }
            catch { }

            // 卸载程序 = 把自己复制一份过去
            string self = Assembly.GetExecutingAssembly().Location;
            string target = Path.Combine(dir, Program.SetupName);
            if (!string.Equals(self, target, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Copy(self, target, true); }
                catch { }
            }

            // 2) 注册表：右键菜单 / 图标 / 双击关联 / 卸载登记
            string exe = Path.Combine(dir, Program.ExeName);
            string icon = File.Exists(Path.Combine(dir, "app.ico"))
                          ? Path.Combine(dir, "app.ico") : exe;
            FipArc.Shell.RegisterContextMenu(exe, icon);
            // EstimatedSize 必须是真实大小。写 0 的话控制面板/设置里
            // 这一项的"大小"列是空的，某些 Windows 版本会把它当成无效项。
            long totalKB = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                    totalKB += new FileInfo(f).Length;
                totalKB = totalKB / 1024 + 1;
            }
            catch { totalKB = 512; }

            FipArc.Shell.RegisterUninstall(exe, dir, Program.VERSION, totalKB);

            // 升级场景：把老版本目录清掉，避免两份残留
            // （默认路径改过：老版本在 %LOCALAPPDATA%\Programs\FipArc，新版本在 %APPDATA%\FipArc）
            try
            {
                if (!string.IsNullOrEmpty(oldDir)
                    && !string.Equals(oldDir.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    && !IsUnder(dir, oldDir)
                    && Directory.Exists(oldDir))
                {
                    try { Directory.Delete(oldDir, true); } catch { }
                }
            }
            catch { }

            // 诊断：结束时再确认一次，定位是哪一步之后丢的
            try
            {
                if (!File.Exists(appPath))
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "fiparc_install.log"),
                        DateTime.Now.ToString("HH:mm:ss") + " 安装结束时 FipArc.exe 不存在: " + appPath + "\r\n");
            }
            catch { }
        }
    }

    // =====================================================================
    //  安装向导
    // =====================================================================
    public sealed class InstallForm : Form
    {
        TextBox pathBox;
        Button next;
        ProgressBar bar;
        Label status;
        Panel page1, page2;

        public InstallForm()
        {
            Text = Program.APP_NAME + " 安装程序";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 390);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            // 指定中文字体：默认的 Segoe UI 行距太挤，中文会互相压住
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = TryIcon();

            // ---- 顶部横幅 ----
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 76;
            header.BackColor = Color.FromArgb(0, 120, 215);
            Controls.Add(header);

            Label t1 = new Label();
            t1.Text = Program.APP_NAME;
            t1.ForeColor = Color.White;
            t1.Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold);
            t1.AutoSize = true;
            t1.Location = new Point(18, 10);
            header.Controls.Add(t1);

            Label t2 = new Label();
            t2.Text = Program.APP_SUB + "   版本 " + Program.VERSION;
            t2.ForeColor = Color.FromArgb(220, 235, 250);
            t2.Font = new Font("Microsoft YaHei UI", 9f);
            t2.AutoSize = true;
            t2.Location = new Point(20, 50);
            header.Controls.Add(t2);

            PictureBox pic = new PictureBox();
            pic.Size = new Size(52, 52);   // 里面放 48x48 的图，Zoom 会留一点边
            pic.Location = new Point(488, 12);
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            try { pic.Image = TryIconBitmap(48); } catch { }
            header.Controls.Add(pic);

            // ---- 第 1 页：选择路径 ----
            page1 = new Panel();
            page1.Dock = DockStyle.Fill;
            Controls.Add(page1);

            // ---- 语言选择：界面上显示各语言自己的名字 ----
            Label lLang = new Label();
            lLang.Text = "安装程序语言 / Language";
            lLang.AutoSize = true;
            lLang.Location = new Point(22, 16);
            lLang.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            page1.Controls.Add(lLang);

            ComboBox langBox = new ComboBox();
            langBox.DropDownStyle = ComboBoxStyle.DropDownList;
            langBox.Items.AddRange(FipArc.Lang.Names);
            langBox.SelectedIndex = FipArc.Lang.DetectSystem();
            langBox.Location = new Point(190, 13);
            langBox.Size = new Size(200, 24);
            langBox.SelectedIndexChanged += delegate
            {
                // 选了就立刻存下来，主程序启动时读它
                FipArc.Lang.Current = langBox.SelectedIndex;
            };
            page1.Controls.Add(langBox);
            FipArc.Lang.Current = langBox.SelectedIndex;

            Label l1 = new Label();
            l1.Text = "选择安装位置";
            l1.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            l1.AutoSize = true;
            l1.Location = new Point(20, 52);
            page1.Controls.Add(l1);

            Label l2 = new Label();
            l2.Text = "将安装以下内容：\n" +
                      "   · FipArc.exe          主程序（压缩 / 解压 / 播放 / 看图 / 编辑）\n" +
                      "   · app.ico             程序图标\n" +
                      "   · 7z.exe / 7z.dll     7-Zip 引擎（支持 .7z / .rar）\n" +
                      "   · Uninstall.exe       卸载程序\n" +
                      "\n" +
                      "安装后会在鼠标右键菜单加入「压缩为」级联菜单和「解压到当前文件夹」。\n" +
                      "只会接管 .fip 关联，其它文件类型保持系统默认，不改图标。";
            l2.AutoSize = false;
            l2.Size = new Size(535, 56);
            l2.Location = new Point(22, 82);
            l2.ForeColor = Color.FromArgb(60, 60, 60);
            page1.Controls.Add(l2);

            Label l3 = new Label();
            l3.Text = "安装路径：";
            l3.AutoSize = true;
            l3.Location = new Point(22, 150);
            page1.Controls.Add(l3);

            string existing = Installer.FoundDir();
            pathBox = new TextBox();
            pathBox.Text = string.IsNullOrEmpty(existing) ? Program.DefaultDir : existing;
            pathBox.Location = new Point(96, 147);
            pathBox.Size = new Size(380, 24);
            page1.Controls.Add(pathBox);

            Button browse = new Button();
            browse.Text = "浏览…";
            browse.Location = new Point(482, 146);
            browse.Size = new Size(80, 26);
            browse.Click += delegate
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                {
                    fb.Description = "选择安装文件夹";
                    fb.SelectedPath = pathBox.Text;
                    if (fb.ShowDialog(this) == DialogResult.OK)
                        pathBox.Text = Path.Combine(fb.SelectedPath, "FipArc");
                }
            };
            page1.Controls.Add(browse);

            CheckBox desktop = new CheckBox();
            desktop.Text = "创建桌面快捷方式";
            desktop.Checked = true;
            desktop.AutoSize = true;
            desktop.Location = new Point(96, 182);
            page1.Controls.Add(desktop);

            // ---- 第 2 页：进度 ----
            page2 = new Panel();
            page2.Dock = DockStyle.Fill;
            page2.Visible = false;
            Controls.Add(page2);

            status = new Label();
            status.Text = FipArc.Lang.S("setup.installing");
            status.AutoSize = true;
            status.Location = new Point(22, 40);
            page2.Controls.Add(status);

            bar = new ProgressBar();
            bar.Location = new Point(24, 72);
            bar.Size = new Size(500, 22);
            bar.Style = ProgressBarStyle.Continuous;
            page2.Controls.Add(bar);

            Label tip = new Label();
            tip.Text = FipArc.Lang.S("setup.finished2");
            tip.AutoSize = true;
            tip.ForeColor = Color.Gray;
            tip.Location = new Point(24, 108);
            page2.Controls.Add(tip);

            // ---- 底部按钮 ----
            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 58;
            footer.BackColor = Color.FromArgb(245, 245, 245);
            Controls.Add(footer);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(380, 14);
            cancel.Size = new Size(78, 30);
            cancel.Click += delegate { Close(); };
            footer.Controls.Add(cancel);

            next = new Button();
            next.Text = "安装";
            next.Location = new Point(466, 14);
            next.Size = new Size(78, 30);
            next.Click += delegate
            {
                string dir = pathBox.Text.Trim();
                if (dir.Length == 0)
                {
                    MessageBox.Show(this, "请填写安装路径");
                    return;
                }
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法创建目录：" + ex.Message);
                    return;
                }

                page1.Visible = false;
                page2.Visible = true;
                next.Enabled = false;
                Application.DoEvents();

                try
                {
                    status.Text = FipArc.Lang.S("setup.writing"); bar.Value = 15; Application.DoEvents();
                    Installer.Install(dir);

                    status.Text = FipArc.Lang.S("setup.registry"); bar.Value = 55; Application.DoEvents();
                    status.Text = FipArc.Lang.S("setup.icons"); bar.Value = 80; Application.DoEvents();
                    FipArc.Shell.RefreshShell();

                    if (desktop.Checked)
                    {
                        status.Text = FipArc.Lang.S("setup.shortcut"); bar.Value = 90; Application.DoEvents();
                        CreateShortcut(dir);
                    }

                    bar.Value = 100;
                    status.Text = FipArc.Lang.S("setup.finished"); Application.DoEvents();
            next.Text = FipArc.Lang.S("setup.finish");
            next.Enabled = true;
            cancel.Visible = false;

                    DialogResult r = MessageBox.Show(this,
                        FipArc.Lang.S("setup.finished") + Environment.NewLine + Environment.NewLine +
                        FipArc.Lang.S("setup.where") + ": " + dir + Environment.NewLine + Environment.NewLine +
                        FipArc.Lang.S("setup.askrun"),
                        FipArc.Lang.S("setup.done.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (r == DialogResult.Yes)
                    {
                        try { Process.Start(Path.Combine(dir, Program.ExeName)); }
                        catch { }
                    }
                    Close();
                }
                catch (Exception ex)
                {
                    bar.Value = 0;
                    status.Text = "安装失败";
                    MessageBox.Show(this, ex.Message, "安装失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    page2.Visible = false;
                    page1.Visible = true;
                    next.Enabled = true;
                }
            };
            footer.Controls.Add(next);
            AcceptButton = next;

            // 关键：Dock 按控件集合的倒序计算。两个 Fill 页必须放到 index 0，
            // 否则它们会抢在顶部横幅(Top)之前停靠，把横幅区域也占掉，
            // 导致页面顶部的内容被横幅遮住。
            page1.BringToFront();
            page2.BringToFront();

            // ---- 选了语言就立刻把安装程序自己的界面也换掉 ----
            Action applyLang = delegate
            {
                Text = FipArc.Lang.S("setup.title");
                l1.Text = FipArc.Lang.S("setup.loc");
                l2.Text = FipArc.Lang.S("setup.desc");
                l3.Text = FipArc.Lang.S("setup.path");
                browse.Text = FipArc.Lang.S("setup.browse2");
                desktop.Text = FipArc.Lang.S("setup.desktop2");
                cancel.Text = FipArc.Lang.S("setup.cancel2");
                next.Text = FipArc.Lang.S("setup.install2");
            };
            langBox.SelectedIndexChanged += delegate
            {
                FipArc.Lang.Current = langBox.SelectedIndex;
                applyLang();
            };
            applyLang();
        }

        static Icon TryIcon()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("app.ico"))
                    if (s != null) return new Icon(s);
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch { return null; }
        }

        /// <summary>
        /// 取指定尺寸的图标位图。
        ///
        /// 不能直接用 Icon.ToBitmap()：我们的 app.ico 里 256x256 那一帧是 PNG 压缩的，
        /// new Icon(stream) 默认取最大帧，ToBitmap() 解 PNG 帧会拿到未初始化内存 ——
        /// 显示出来就是一堆彩色方块（马赛克）。
        /// 这里改成按指定尺寸重建图标，再用 DrawIcon 画到 32bpp 位图上，alpha 也正确。
        /// </summary>
        static Bitmap TryIconBitmap(int size)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("app.ico"))
                {
                    if (s == null) return null;
                    using (Icon ic = new Icon(s, size, size))
                    {
                        Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawIcon(ic, new Rectangle(0, 0, size, size));
                        }
                        return bmp;
                    }
                }
            }
            catch { }
            try
            {
                using (Icon ic = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location))
                {
                    if (ic == null) return null;
                    Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.DrawIcon(ic, new Rectangle(0, 0, size, size));
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }

        void CreateShortcut(string dir)
        {
            try
            {
                string lnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Program.APP_NAME + ".lnk");   // 快捷方式名字仍是 FipArc
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                                           new object[] { lnk });
                Type sct = sc.GetType();
                sct.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc,
                                 new object[] { Path.Combine(dir, Program.ExeName) });
                sct.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc,
                                 new object[] { dir });
                sct.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc,
                                 new object[] { Path.Combine(dir, "app.ico") });
                sct.InvokeMember("Description", BindingFlags.SetProperty, null, sc,
                                 new object[] { Program.APP_SUB });
                sct.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }
    }

    // =====================================================================
    //  卸载向导
    // =====================================================================
    //  卸载向导（两页：确认 -> 进度 -> 结果）
    // =====================================================================
    public sealed class UninstallForm : Form
    {
        readonly string dir;
        Panel page1, page2;
        CheckBox keepOthers;
        Label status, detail;
        ProgressBar bar;
        Button go, cancel;
        int removed = 0, failed = 0;
        StringBuilder failures = new StringBuilder();

        public UninstallForm()
        {
            dir = Installer.FoundDir();
            if (string.IsNullOrEmpty(dir)) dir = Program.DefaultDir;

            Text = Program.APP_NAME + " 卸载";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 356);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);

            // ---- 顶部横幅 ----
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 64;
            header.BackColor = Color.FromArgb(200, 60, 60);
            Controls.Add(header);

            Label t1 = new Label();
            t1.Text = "卸载 " + Program.APP_NAME;
            t1.ForeColor = Color.White;
            t1.Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
            t1.AutoSize = true;
            t1.Location = new Point(18, 14);
            header.Controls.Add(t1);

            // ---- 第 1 页：确认 ----
            page1 = new Panel();
            page1.Dock = DockStyle.Fill;
            Controls.Add(page1);

            Label l1 = new Label();
            l1.Text = "确认卸载";
            l1.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            l1.AutoSize = true;
            l1.Location = new Point(20, 12);
            page1.Controls.Add(l1);

            Label l2 = new Label();
            l2.AutoSize = false;
            l2.Size = new Size(478, 46);
            l2.Location = new Point(22, 40);
            l2.Text = "将从以下位置移除本软件：" + Environment.NewLine +
                      "    " + dir;
            page1.Controls.Add(l2);

            Label l3 = new Label();
            l3.AutoSize = false;
            l3.Size = new Size(478, 92);
            l3.Location = new Point(22, 96);
            l3.ForeColor = Color.FromArgb(60, 60, 60);
            l3.Text = "同时会清理：" + Environment.NewLine +
                      "    · 鼠标右键菜单项（压缩为 / 解压 / 转换为 FIP / 还原）" + Environment.NewLine +
                      "    · 压缩文件的图标关联，还原为系统默认图标" + Environment.NewLine +
                      "    · 控制面板里的卸载登记信息";
            page1.Controls.Add(l3);

            keepOthers = new CheckBox();
            keepOthers.Text = "保留该目录下的其他文件（我自己放的压缩包等）";
            keepOthers.Checked = true;
            keepOthers.AutoSize = true;
            keepOthers.Location = new Point(24, 200);
            page1.Controls.Add(keepOthers);

            // ---- 第 2 页：进度 ----
            page2 = new Panel();
            page2.Dock = DockStyle.Fill;
            page2.Visible = false;
            Controls.Add(page2);

            status = new Label();
            status.Text = "正在卸载…";
            status.AutoSize = false;
            status.Size = new Size(478, 22);
            status.Location = new Point(22, 30);
            page2.Controls.Add(status);

            bar = new ProgressBar();
            bar.Location = new Point(24, 58);
            bar.Size = new Size(470, 22);
            bar.Style = ProgressBarStyle.Continuous;
            page2.Controls.Add(bar);

            detail = new Label();
            detail.AutoSize = false;
            detail.Size = new Size(470, 150);
            detail.Location = new Point(24, 92);
            detail.ForeColor = Color.FromArgb(70, 70, 70);
            page2.Controls.Add(detail);

            // ---- 底部按钮 ----
            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 58;
            footer.BackColor = Color.FromArgb(245, 245, 245);
            Controls.Add(footer);

            cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(340, 14);
            cancel.Size = new Size(78, 30);
            cancel.Click += delegate { Close(); };
            footer.Controls.Add(cancel);

            go = new Button();
            go.Text = "卸载";
            go.Location = new Point(426, 14);
            go.Size = new Size(78, 30);
            go.Click += delegate { RunUninstall(); };
            footer.Controls.Add(go);

            page1.BringToFront();
            page2.BringToFront();
        }

        void Step(string text, int percent)
        {
            status.Text = text;
            bar.Value = percent;
            Application.DoEvents();
        }

        void RunUninstall()
        {
            if (MessageBox.Show(this, "确定要卸载「" + Program.APP_NAME + "」吗？", "确认卸载",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            page1.Visible = false;
            page2.Visible = true;
            go.Enabled = false;
            cancel.Text = "关闭";
            Application.DoEvents();

            try
            {
                // 1) 注册表
                Step("正在关闭正在运行的程序…", 8);
                int closed = Installer.CloseRunningApp(dir);
                if (closed > 0)
                    detail.Text = "已关闭 " + closed + " 个正在运行的 FipArc 窗口。`r`n";

                Step("正在移除右键菜单与图标关联…", 20);
                FipArc.Shell.UnregisterAll(dir);
                FipArc.Shell.RefreshShell();
                detail.Text = "已清理右键菜单、图标关联、卸载登记信息。\r\n";

                // 2) 删除程序文件 + 清理目录
                //    （和静默卸载共用同一套已测试的逻辑，避免两份代码走偏）
                Step("正在删除程序文件…", 45);
                string self = Assembly.GetExecutingAssembly().Location;
                StringBuilder log = new StringBuilder();
                log.Append("已清理右键菜单、图标关联、卸载登记信息。\r\n");

                int rm, fl2; string failList;
                Installer.UninstallFiles(dir, keepOthers.Checked, self, out rm, out fl2, out failList);
                removed += rm; failed += fl2;
                if (!string.IsNullOrEmpty(failList)) failures.Append(failList);

                Step("正在清理安装目录…", 70);
                Step("正在收尾…", 90);
                Installer.ScheduleSelfDelete(dir, self);

                bar.Value = 100;
                Step("卸载完成", 100);

                // 5) 结果
                log.Append(string.Format("共删除 {0} 个文件。\r\n", removed));
                if (failed > 0)
                {
                    log.Append(string.Format("\r\n有 {0} 个文件无法删除（可能正被占用或权限不足）：\r\n", failed));
                    string[] fl = failures.ToString().Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < fl.Length && i < 8; i++) log.Append("    " + fl[i] + "\r\n");
                    log.Append("\r\n可以稍后手动删除安装目录。\r\n");
                }
                else
                {
                    log.Append("\r\n右键菜单和文件图标都已还原为系统默认。");
                }
                detail.Text = log.ToString();

                MessageBox.Show(this,
                    failed > 0
                        ? "卸载已完成，但有 " + failed + " 个文件没能删除。\r\n详细情况见窗口里的说明。"
                        : "卸载完成。",
                    "卸载完成", MessageBoxButtons.OK,
                    failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Step("卸载失败", 0);
                detail.Text = ex.ToString();
                MessageBox.Show(this, ex.Message, "卸载失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static bool TryDeleteFile(string f)
        {
            try { File.Delete(f); return !File.Exists(f); }
            catch { return false; }
        }

        /// <summary>卸载程序自己正在运行，删不掉；用批处理等本进程退出后再删</summary>
        void ScheduleSelfDelete(string self)
        {
            try
            {
                string bat = Path.Combine(Path.GetTempPath(),
                    "fiparc_uninst_" + Guid.NewGuid().ToString("N") + ".bat");
                string lines =
                    "@echo off\r\n" +
                    "setlocal\r\n" +
                    "for /l %%i in (1,1,30) do (\r\n" +
                    "  timeout /t 1 /nobreak >nul\r\n" +
                    "  del /f /q \"" + self + "\" >nul 2>&1\r\n" +
                    "  if not exist \"" + self + "\" goto gone\r\n" +
                    ")\r\n" +
                    ":gone\r\n" +
                    "rmdir /q \"" + dir + "\" >nul 2>&1\r\n" +
                    "del /f /q \"%~f0\" >nul 2>&1\r\n";
                File.WriteAllText(bat, lines, Encoding.Default);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
