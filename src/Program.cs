// Program.cs —— 程序入口 / 命令行
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FipArc
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Lang.Load();                      // 上次选的语言

            // 右键菜单的文案是在注册表里写死的，换了语言必须重写一遍，
            // 否则菜单会一直停留在安装时那个语言。
            try { Shell.RegisterContextMenu(Application.ExecutablePath, Path.Combine(Application.StartupPath, "app.ico")); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                if (args.Length == 0)
                {
                    Application.Run(new MainForm(null));
                    return;
                }

                string cmd = args[0].ToLowerInvariant();
                string[] rest = new string[Math.Max(0, args.Length - 1)];
                for (int i = 1; i < args.Length; i++) rest[i - 1] = args[i];

                switch (cmd)
                {
                    case "/pack":
                        // /pack <格式> <文件…>   格式 = fip / zip / tar / tgz / gz
                        {
                            string fmt = rest.Length > 0 ? rest[0].TrimStart('.').ToLowerInvariant() : "fip";
                            if (fmt == "targz") fmt = "tgz";
                            string[] files = new string[Math.Max(0, rest.Length - 1)];
                            for (int i = 1; i < rest.Length; i++) files[i - 1] = rest[i];
                            DoPack(files, fmt);
                        }
                        return;
                    case "/tofip":
                        DoToFip(rest);
                        return;
                    case "/fromfip":
                        DoFromFip(rest);
                        return;
                    case "/extract":
                        DoExtract(rest);
                        return;
                    case "/open":
                        DoOpen(rest);
                        return;
                    case "/uninstall":
                        DoUninstall();
                        return;
                    default:
                        // 直接双击文件：第一个参数就是路径
                        DoOpen(args);
                        return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Lang.S("err.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------- 密码处理 ----------
        /// <summary>密码保护时弹框询问并执行，最多重试 3 次。返回 false 表示取消/密码一直错。</summary>
        static bool UnlockAndRun(string path, Action<string> act)
        {
            string pwd = "";
            bool need = false;
            try { need = Archive.NeedsPassword(path); }
            catch { }

            for (int i = 0; i < 3; i++)
            {
                if (need)
                {
                    pwd = PasswordForm.AskOpen(null, Path.GetFileName(path),
                        i > 0 ? Lang.S("err.cancelpw") : "");
                    if (pwd == null) return false;
                }
                try { act(pwd); return true; }
                catch (WrongPasswordException) { need = true; continue; }
            }
            MessageBox.Show(Lang.S("err.pw3"), Lang.S("err.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // ---------- 右键：压缩（fmt = fip / zip / tar / tgz / gz）----------
        static void DoPack(string[] paths, string fmt)
        {
            if (paths.Length == 0) return;
            string ext = Shell.ExtForPackKey(fmt);

            if (fmt == "gz")
            {
                if (paths.Length > 1)
                {
                    MessageBox.Show(Lang.S("prog.gzone"),
                        Lang.S("prog.cantpack"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (Directory.Exists(paths[0]))
                {
                    MessageBox.Show(Lang.S("prog.gzdir"),
                        Lang.S("prog.cantpack"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // 只有压缩包格式才问密码：.fip（AES-256）和 .zip（标准 ZipCrypto）。
            // f 格式转换（fmp/fpg/fjp/fp4/fov/fxt）是"转码"，不问密码。
            string pwd = "";
            if (fmt == "fip" || fmt == "zip" || fmt == "7z")
            {
                string what = paths.Length == 1
                    ? Path.GetFileName(paths[0].TrimEnd('\\'))
                    : (paths.Length + " 个文件");
                pwd = PasswordForm.AskNew(null, what + " → ." + fmt);
                if (pwd == null) return;      // 用户取消
            }

            using (BusyForm busy = new BusyForm(Lang.S("st.packing")))
            {
                busy.Show();
                Application.DoEvents();
                string last = null;
                int ok = 0;
                string err = null;
                foreach (string p in paths)
                {
                    try
                    {
                        string baseDir = Directory.Exists(p) ? Path.GetDirectoryName(p.TrimEnd('\\')) : Path.GetDirectoryName(p);
                        string nameBase = Directory.Exists(p) ? Path.GetFileName(p.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(p);
                        byte[] data = PackOne(new string[] { p }, baseDir, nameBase, fmt, pwd);
                        string outPath = Convert2.UniquePath(Path.Combine(baseDir, nameBase + ext));
                        File.WriteAllBytes(outPath, data);
                        last = outPath;
                        ok++;
                    }
                    catch (Exception ex) { err = ex.Message; }
                }
                busy.Close();
                string note = (fmt == "fip" && !string.IsNullOrEmpty(pwd))
                    ? Environment.NewLine + Lang.S("prog.pwset") : "";
                if (err != null && ok == 0)
                    MessageBox.Show(err, Lang.S("dlg.packfail"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else if (ok == 1 && last != null)
                    MessageBox.Show(Lang.S("prog.packed") + Environment.NewLine + last + note, Lang.S("dlg.done"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(string.Format(Lang.S("prog.okpack"), ok) + note, Lang.S("dlg.done"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>按指定格式打包</summary>
        static byte[] PackOne(string[] paths, string baseDir, string displayName, string fmt, string pwd)
        {
            switch (fmt)
            {
                case "fip":
                    return Archive.CreateFip(paths, baseDir, displayName, pwd);
                case "zip":
                    return Archive.CreateZip(paths, baseDir, pwd);   // pwd 非空则标准 ZipCrypto 加密
                case "7z":
                    return Archive.Create7z(paths, baseDir, pwd);   // pwd 非空则 AES-256 加密
                case "tar":
                    return Archive.CreateTar(paths, baseDir);
                case "tgz":
                    {
                        byte[] tar = Archive.CreateTar(paths, baseDir);
                        using (MemoryStream ms = new MemoryStream())
                        {
                            using (System.IO.Compression.GZipStream gz =
                                new System.IO.Compression.GZipStream(ms,
                                    System.IO.Compression.CompressionMode.Compress, true))
                                gz.Write(tar, 0, tar.Length);
                            return ms.ToArray();
                        }
                    }
                case "gz":
                    return Archive.CreateGz(paths[0]);
            }
            throw new NotSupportedException("不支持的压缩格式: " + fmt);
        }

        // ---------- 右键：解压到当前文件夹 ----------
        static void DoExtract(string[] paths)
        {
            if (paths.Length == 0) return;
            int ok = 0;
            int failedFiles = 0;
            string err = null;
            string lastDir = null;
            using (BusyForm busy = new BusyForm(Lang.S("st.extracting")))
            {
                busy.Show();
                Application.DoEvents();
                foreach (string p in paths)
                {
                    string path = p;
                    string dest = Convert2.UniquePath(
                        Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path)));
                    bool done = UnlockAndRun(path, delegate(string pw)
                    {
                        Directory.CreateDirectory(dest);
                        IExtractor ex = Archive.Open(path);
                        ex.SetPassword(pw);
                        ex.ExtractAll(dest, null);
                    });
                    if (done)
                    {
                        lastDir = dest;
                        ok++;
                        if (Archive.LastErrors.Count > 0) failedFiles += Archive.LastErrors.Count;
                    }
                    else
                    {
                        try { if (Directory.Exists(dest) && Directory.GetFileSystemEntries(dest).Length == 0) Directory.Delete(dest); }
                        catch { }
                        err = Lang.S("prog.cancelpw");
                    }
                }
                busy.Close();
            }
            if (err != null && ok == 0)
                MessageBox.Show(err, Lang.S("prog.extfail"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (ok == 1 && lastDir != null)
                MessageBox.Show(Lang.S("prog.extracted") + Environment.NewLine + lastDir, Lang.S("dlg.done"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (ok > 0)
                MessageBox.Show(string.Format(Lang.S("prog.okextract"), ok)
                    + (failedFiles > 0 ? Environment.NewLine + Environment.NewLine +
                       string.Format(Lang.S("prog.writefail"), failedFiles) + Environment.NewLine +
                       Lang.S("prog.writehint") : ""), Lang.S("dlg.done"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- 右键：转换为 FIP（按文件类型自动决定目标格式）----------
        static void DoToFip(string[] paths)
        {
            if (paths.Length == 0) return;

            // 先校验：不支持的类型直接说清楚，不要做完一半再报错
            StringBuilder bad = new StringBuilder();
            foreach (string p in paths)
            {
                byte k; string fe;
                if (!Kind.TryFromSourceExt(Path.GetExtension(p), out k, out fe))
                    bad.AppendLine("  " + Path.GetFileName(p));
            }
            if (bad.Length > 0)
            {
                MessageBox.Show(
                    "以下文件不支持转换：\n" + bad +
                    "\n只支持：mp3 / png / jpg / jpeg / mp4 / mov / txt",
                    "无法转换", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string what = paths.Length == 1
                ? Path.GetFileName(paths[0])
                : (paths.Length + " 个文件");
            // 不设密码：f 格式用内置密钥加密，只有本软件能解开
            string pwd = "";

            int ok = 0;
            string err = null;
            string last = null;
            using (BusyForm busy = new BusyForm("正在转换格式…"))
            {
                busy.Show();
                Application.DoEvents();
                foreach (string p in paths)
                {
                    try { last = Convert2.ToFip(p, pwd); ok++; }
                    catch (Exception ex) { err = ex.Message; }
                }
                busy.Close();
            }
            string note = string.IsNullOrEmpty(pwd) ? "" : Environment.NewLine + "（已设置密码保护）";
            if (err != null && ok == 0)
                MessageBox.Show(err, "转换失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (ok == 1 && last != null)
                MessageBox.Show("已转换：" + Environment.NewLine + last + note, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (ok > 0)
                MessageBox.Show(string.Format("成功转换 {0} 个文件。", ok) + note, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- 右键：从 FIP 还原为普通格式 ----------
        static void DoFromFip(string[] paths)
        {
            if (paths.Length == 0) return;
            int ok = 0;
            string last = null;
            string err = null;
            using (BusyForm busy = new BusyForm("正在还原格式…"))
            {
                busy.Show();
                Application.DoEvents();
                foreach (string p in paths)
                {
                    string path = p;
                    byte k; string se;
                    if (!Kind.TryFromFipExt(Path.GetExtension(path), out k, out se))
                    {
                        err = "不是可还原的 FIP 文件类型: " + Path.GetFileName(path);
                        continue;
                    }
                    bool done = UnlockAndRun(path, delegate(string pw) { last = Convert2.FromFip(path, pw); });
                    if (done) ok++;
                    else err = Lang.S("prog.cancelpw");
                }
                busy.Close();
            }
            if (err != null && ok == 0)
                MessageBox.Show(err, "还原未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (ok == 1 && last != null)
                MessageBox.Show("已还原为：" + Environment.NewLine + last, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (ok > 0)
                MessageBox.Show(string.Format("成功还原 {0} 个文件。", ok), "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- 双击 / 右键打开 ----------
        static void DoOpen(string[] paths)
        {
            if (paths.Length == 0) return;
            string p = paths[0];
            if (Directory.Exists(p)) { Application.Run(new MainForm(null)); return; }

            // 先区分：.fip 是压缩包；.fmp/.fpg/.fjp/.fp4/.fov/.fxt 是"f 格式媒体"
            string extLow = Path.GetExtension(p).ToLowerInvariant();
#if SE
            // 精简版不认 f 格式
#else
            byte fKind; string fSrcExt;
#endif
#if SE
            bool isFMedia = false;   // 精简版不认 f 格式，交给系统默认程序
#else
            bool isFMedia = Kind.TryFromFipExt(extLow, out fKind, out fSrcExt);
#endif

            // 压缩包 -> 主窗口（密码在主窗口里问）
            int kind = ArchiveKind.Detect(p);
            if (!isFMedia && (kind != ArchiveKind.Other || FipFile.IsFip(p)))
            {
                Application.Run(new MainForm(new string[] { p }));
                return;
            }

            if (Playable.IsText(p))
            {
                TextViewerForm f = new TextViewerForm();
                f.Show();
                f.LoadFile(p);
                Application.Run();
                return;
            }

            string target = p;
            bool isTemp = false;
            if (FipFile.IsFip(p) && FipFile.NeedsPassword(p))
            {
                string tempPath = null;
                bool ok = UnlockAndRun(p, delegate(string pw)
                {
                    bool dummy;
                    tempPath = Playable.Resolve(p, out dummy, pw);
                });
                if (!ok || tempPath == null) return;
                target = tempPath;
                isTemp = true;
            }
            else
            {
                try { target = Playable.Resolve(p, out isTemp); }
                catch (PasswordRequiredException) { return; }
            }

            if (Playable.IsMedia(p))
            {
                // 本软件不再内置音视频播放，交给系统默认播放器
                try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); } catch { }
                return;
            }
            Application.Run(new MainForm(new string[] { p }));
        }

        // ---------- 卸载 ----------
        static void DoUninstall()
        {
            string setup = Path.Combine(Application.StartupPath, "Uninstall.exe");
            if (File.Exists(setup))
            {
                Process.Start(setup);
                return;
            }
            MessageBox.Show(Lang.S("uninst.missing"),
                Lang.S("pw.hint"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>一个极简的"忙"提示窗，避免大文件操作时界面像卡死</summary>
    sealed class BusyForm : Form
    {
        public BusyForm(string text)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ControlBox = false;
            ShowInTaskbar = false;
            Text = "FipArc";
            ClientSize = new Size(300, 90);
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Dock = DockStyle.Fill;
            l.Font = new Font("Microsoft YaHei UI", 10f);
            Controls.Add(l);
            TopMost = true;
        }
    }
}
