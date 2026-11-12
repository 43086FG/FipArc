// SevenZip.cs —— 借力本机的 7-Zip 打开 7z / rar / iso / cab / xz / bz2 等格式
//
// 为什么不自己实现：
//   7z 需要 LZMA/LZMA2/PPMd + BCJ 过滤器，rar 是私有压缩算法（还要授权），
//   自己写不现实。业界做法都是调用 7-Zip。
//
// 行为：
//   · 系统装了 7-Zip  → 这些格式都能在树里浏览、双击打开、拖出来
//   · 没装           → 明确告诉用户去装，而不是抛个看不懂的异常
//
// 调用方式（7-Zip 官方命令行）：
//   列目录： 7z l -slt "archive"
//   全解压： 7z x -y -o"dir" "archive"
//   解一个： 7z e -y -o"dir" "archive" "内路径"
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Forms;

namespace FipArc
{
    public static class SevenZip
    {
        /// <summary>找一个可用的 7-Zip 命令行</summary>
        public static string FindExe()
        {
            // 优先用【安装目录里自带的】7-Zip（安装包会一起装进去），
            // 这样用户不用自己装就能开 7z / rar。
            try
            {
                string own = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.exe");
                if (File.Exists(own)) return own;
            }
            catch { }

            string[] candidates = {
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
                @"C:\Program Files\7-Zip\7za.exe",
                @"C:\Program Files (x86)\7-Zip\7za.exe",
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return c;

            // 再扫一遍 PATH
            try
            {
                string pathVar = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathVar))
                {
                    foreach (string dir in pathVar.Split(';'))
                    {
                        if (dir.Trim().Length == 0) continue;
                        foreach (string name in new string[] { "7z.exe", "7za.exe" })
                        {
                            try
                            {
                                string full = Path.Combine(dir.Trim(), name);
                                if (File.Exists(full)) return full;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool Available { get { return FindExe() != null; } }

        /// <summary>跑一次 7z，返回 stdout（stderr 也一起收，7-Zip 有时把提示写到 stderr）</summary>
        public static string Run(string args, int timeoutMs)
        {
            string exe = FindExe();
            if (exe == null) throw new NotSupportedException(
                "打开这个格式需要 7-Zip。\r\n\r\n" +
                "请先安装 7-Zip（免费）：https://www.7-zip.org/\r\n" +
                "装完不用重启，本软件会自动使用它。");

            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            using (Process p = Process.Start(psi))
            {
                StringBuilder sb = new StringBuilder();
                StringBuilder err = new StringBuilder();
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                { if (e.Data != null) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                { if (e.Data != null) err.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException("7-Zip 操作超时");
                }
                p.WaitForExit();
                return sb.ToString() + err.ToString();
            }
        }

        /// <summary>列出压缩包内容（解析 7z l -slt 的键值块）</summary>
        /// <summary>列目录（不带密码；加密包会返回空并靠 NeedsPassword 判断）</summary>
        public static List<EntryInfo> List(string archive)
        {
            return ParseList(Run("l -slt -p\"" + "__FipArc_NoPassword__" + "\" \"" + archive + "\"", 60000));
        }

        /// <summary>这个 7z/rar 是否需要密码（文件名加密的包必须给密码才能列目录）</summary>
        public static bool NeedsPassword(string archive)
        {
            try
            {
                string text = Run("l -slt -p\"" + "__FipArc_NoPassword__" + "\" \"" + archive + "\"", 60000);
                return text.IndexOf("Wrong password", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("Cannot open encrypted archive", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public static List<EntryInfo> ParseList(string text)
        {
            List<EntryInfo> result = new List<EntryInfo>();

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            Dictionary<string, string> block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inItems = false;

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Trim() == "----------") { inItems = true; block.Clear(); continue; }
                if (!inItems) continue;

                if (line.Length == 0)
                {
                    Flush(block, result);
                    block.Clear();
                    continue;
                }
                int eq = line.IndexOf(" = ");
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 3);
                if (!block.ContainsKey(key)) block[key] = val;
            }
            Flush(block, result);
            return result;
        }

        static void Flush(Dictionary<string, string> block, List<EntryInfo> result)
        {
            string path;
            if (!block.TryGetValue("Path", out path)) return;
            if (string.IsNullOrEmpty(path)) return;

            EntryInfo e = new EntryInfo();
            e.Name = path.Replace('\\', '/');

            string attr;
            bool isDir = false;
            if (block.TryGetValue("Attributes", out attr) && attr.Length > 0 && attr[0] == 'D') isDir = true;
            string folder;
            if (block.TryGetValue("Folder", out folder) && folder.Trim() == "+") isDir = true;
            e.IsDir = isDir;

            string size;
            long sz;
            if (block.TryGetValue("Size", out size) && long.TryParse(size, out sz)) e.Size = sz;
            else e.Size = isDir ? 0 : -1;

            string packed;
            long pk;
            if (block.TryGetValue("Packed Size", out packed) && long.TryParse(packed, out pk)) e.PackedSize = pk;
            else e.PackedSize = 0;

            result.Add(e);
        }
    }

    /// <summary>用 7-Zip 读写任意格式的解压器</summary>
    public sealed class SevenZipExtractor : IExtractor
    {
        readonly string path;
        string password;

        public SevenZipExtractor(string p) { path = p; }
        public void SetPassword(string pwd) { password = pwd; }

        // 没有密码时用的占位串。
        // 关键：7z 不带 -p 时，遇到加密包会在控制台等你输入密码，把整个程序卡死；
        // 带一个不可能匹配的密码，它会立刻报错，我们就能转成"需要密码"去弹框。
        const string NoPwd = "__FipArc_NoPassword__";

        string PwdArg
        {
            get
            {
                string p = string.IsNullOrEmpty(password) ? NoPwd : password;
                return " -p\"" + p.Replace("\"", "") + "\"";
            }
        }

        /// <summary>7z 的密码错误输出 -> 换成我们的异常，交给上层弹框重试</summary>
        void CheckPwdError(string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            bool bad = output.IndexOf("Wrong password", StringComparison.OrdinalIgnoreCase) >= 0
                    || output.IndexOf("Cannot open encrypted archive", StringComparison.OrdinalIgnoreCase) >= 0
                    || output.IndexOf("Headers Error", StringComparison.OrdinalIgnoreCase) >= 0
                    || output.IndexOf("Data Error in encrypted file", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!bad) return;
            if (string.IsNullOrEmpty(password)) throw new PasswordRequiredException(path);
            throw new WrongPasswordException();
        }

        public List<EntryInfo> List()
        {
            string dummy = SevenZip.Run("l -slt " + PwdArg + " \"" + path + "\"", 60000);
            CheckPwdError(dummy);
            return SevenZip.ParseList(dummy);
        }

        public void ExtractAll(string destDir, Action<int, string> progress)
        {
            Directory.CreateDirectory(destDir);
            string o = SevenZip.Run("x -y -bd -o\"" + destDir + "\"" + PwdArg + " \"" + path + "\"", 600000);
            CheckPwdError(o);

            if (progress != null)
            {
                int n = 0;
                try
                {
                    foreach (string f in Directory.GetFiles(destDir, "*", SearchOption.AllDirectories))
                    {
                        n++;
                        progress(n, f.Substring(destDir.Length).TrimStart('\\'));
                    }
                }
                catch { }
            }
        }

        /// <summary>只解这个文件夹的子树（7z 支持按路径过滤）</summary>
        public string ExtractFolder(string folderPath, string destDir)
        {
            string clean = (folderPath ?? "").Replace('/', '\\').TrimEnd('\\');
            if (clean.Length == 0) return null;
            string name = Path.GetFileName(clean);
            if (string.IsNullOrEmpty(name)) return null;

            // 7z x 会把【完整内部路径】解到 -o 目录下，
            // 所以先解到临时目录的 <clean> 那一层，再整个搬到 destDir。
            string tmp = Path.Combine(Path.GetTempPath(), "FipArc7zDir_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                string o2 = SevenZip.Run("x -y -bd -o\"" + tmp + "\"" + PwdArg + " \"" + path + "\" \"" + clean + "\\*\"", 600000);
                CheckPwdError(o2);

                string src = Path.Combine(tmp, clean);
                if (!Directory.Exists(src)) return null;

                string dest = Path.Combine(destDir, name);
                int k = 1;
                while (Directory.Exists(dest)) { dest = Path.Combine(destDir, name + " (" + k + ")"); k++; }
                CopyDir(src, dest);
                return dest;
            }
            finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
        }

        static void CopyDir(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
        }

        /// <summary>只解一个条目（7z 支持按路径提取）</summary>
        public string ExtractOne(string entryName, string destDir)
        {
            Directory.CreateDirectory(destDir);
            string flat = Path.Combine(destDir, Path.GetFileName(entryName));
            string tmp = Path.Combine(destDir, "_7z_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);

            // e = 不带路径, 全部解到 -o 目录；只解指定文件
            string o3 = SevenZip.Run("e -y -bd -o\"" + tmp + "\"" + PwdArg + " \"" + path + "\" \"" +
                         entryName.Replace('/', '\\') + "\"", 300000);
            CheckPwdError(o3);

            string produced = Path.Combine(tmp, Path.GetFileName(entryName));
            if (!File.Exists(produced)) return null;
            try
            {
                File.Copy(produced, flat, true);
                Directory.Delete(tmp, true);
            }
            catch { }
            return flat;
        }
    }
}
