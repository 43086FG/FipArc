// Archive.cs —— 压缩引擎
//
// 原生支持（不依赖任何外部程序）：
//   读+写 : ZIP, TAR, FIP
//   读    : GZ, TAR.GZ / TGZ
//   写    : GZ（单文件）
// 另外如果系统装了 7-Zip / WinRAR，会自动借力处理 7z、rar、iso、cab 等。
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.IO.Compression;

namespace FipArc
{
    public sealed class EntryInfo
    {
        public string Name;
        public long Size;
        public long PackedSize;
        public bool IsDir;
        public override string ToString() { return Name; }
    }

    public static class ArchiveKind
    {
        public const int Zip = 0;
        public const int Tar = 1;
        public const int Gz = 2;
        public const int TarGz = 3;
        public const int Fip = 4;
        public const int SevenZip = 5;
        public const int Rar = 6;
        public const int Other = 99;

        public static int Detect(string path)
        {
            string n = path.ToLowerInvariant();
            if (n.EndsWith(".fip")) return Fip;
            if (n.EndsWith(".tar.gz") || n.EndsWith(".tgz")) return TarGz;
            if (n.EndsWith(".zip") || n.EndsWith(".jar") || n.EndsWith(".cbz")) return Zip;
            if (n.EndsWith(".tar")) return Tar;
            if (n.EndsWith(".gz")) return Gz;
            if (n.EndsWith(".7z")) return SevenZip;
            if (n.EndsWith(".rar")) return Rar;
            return Other;
        }

        public static string Describe(int k)
        {
            switch (k)
            {
                case Zip: return "ZIP";
                case Tar: return "TAR";
                case Gz: return "GZIP";
                case TarGz: return "TAR.GZ";
                case Fip: return "FIP（本软件专有）";
                case SevenZip: return "7Z";
                case Rar: return "RAR";
            }
            return "未知";
        }

        /// <summary>能直接打开（读取/解压）的扩展名</summary>
        public static readonly string[] ReadableExts = {
            ".zip", ".jar", ".cbz", ".tar", ".gz", ".tar.gz", ".tgz",
            ".fip", ".7z", ".rar", ".iso", ".cab", ".xz", ".bz2"
        };

        /// <summary>压缩菜单里列出的格式</summary>
        public static readonly string[] CreateExts = {
            ".fip", ".zip", ".tar", ".tar.gz", ".gz"
        };
    }

    public interface IExtractor
    {
        List<EntryInfo> List();
        void ExtractAll(string destDir, Action<int, string> progress);
        /// <summary>密码保护的文件在解压前调用；不需要密码的实现可以忽略</summary>
        void SetPassword(string pwd);
        /// <summary>
        /// 只解出一个条目到 destDir，返回写出的文件路径（找不到返回 null）。
        /// 从树里拖文件出来时用这个，比整包解压再挑快得多。
        /// </summary>
        string ExtractOne(string entryName, string destDir);
        /// <summary>
        /// 解出一个文件夹的整棵子树，返回解出来的文件夹路径（找不到返回 null）。
        /// 从树/列表里拖文件夹出来时用。
        /// </summary>
        string ExtractFolder(string folderPath, string destDir);
    }


    /// <summary>单条目写盘 + 通用回退实现</summary>
    public static class EntryWrite
    {
        /// <summary>把条目字节写到 destDir 下对应的相对路径；失败返回 null（不抛，由界面统一汇总）</summary>
        public static string One(string destDir, string entryName, byte[] data)
        {
            try
            {
                string target = ZipExtractor.SafeJoin(destDir, entryName);
                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(target, data);
                return target;
            }
            catch (Exception ex)
            {
                Archive.NoteError(entryName, ex);   // 记下来，继续解其它的
                return null;
            }
        }

        /// <summary>通用回退：整包解到临时目录，再把目标文件夹整个搬出来</summary>
        public static string FolderViaExtractAll(IExtractor ex, string folderPath, string destDir)
        {
            string clean = (folderPath ?? "").Replace('\\', '/').TrimEnd('/');
            if (clean.Length == 0) return null;
            string tmp = Path.Combine(Path.GetTempPath(), "FipArcDir_" + Guid.NewGuid().ToString("N"));
            try
            {
                ex.ExtractAll(tmp, null);
                string src = Path.Combine(tmp, clean.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(src)) return null;
                string dest = Path.Combine(destDir, Path.GetFileName(clean));
                int k = 1;
                while (Directory.Exists(dest)) { dest = Path.Combine(destDir, Path.GetFileName(clean) + " (" + k + ")"); k++; }
                CopyDir(src, dest);
                return dest;
            }
            catch { return null; }
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

        /// <summary>不支持随机访问的格式：整包解到临时目录再挑出来</summary>
        public static string ViaExtractAll(IExtractor ex, string entryName, string destDir)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "FipArcOne_" + Guid.NewGuid().ToString("N"));
            try
            {
                ex.ExtractAll(tmp, null);
                string src = Path.Combine(tmp, entryName.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(src)) return null;
                return One(destDir, entryName, File.ReadAllBytes(src));
            }
            catch { return null; }
            finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
        }
    }

    // ---------------- ZIP ----------------
    public sealed class ZipExtractor : IExtractor
    {
        readonly string path;
        public ZipExtractor(string p) { path = p; }
        public void SetPassword(string pwd) { /* zip 不支持密码，忽略 */ }

        public List<EntryInfo> List()
        {
            List<EntryInfo> list = new List<EntryInfo>();
            using (FileStream fs = File.OpenRead(path))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    EntryInfo i = new EntryInfo();
                    i.Name = e.FullName;
                    i.Size = e.Length;
                    i.PackedSize = e.CompressedLength;
                    i.IsDir = e.FullName.EndsWith("/");
                    list.Add(i);
                }
            }
            return list;
        }

        /// <summary>随机访问单个条目（ZIP 天然支持，不用整包解压）</summary>
        /// <summary>只解这个文件夹的子树（随机访问，不碰其它内容）</summary>
        public string ExtractFolder(string folderPath, string destDir)
        {
            string clean = (folderPath ?? "").TrimEnd('/');
            if (clean.Length == 0) return null;
            string outDir = Path.Combine(destDir, Path.GetFileName(clean));
            int k = 1;
            while (Directory.Exists(outDir)) { outDir = Path.Combine(destDir, Path.GetFileName(clean) + " (" + k + ")"); k++; }
            Directory.CreateDirectory(outDir);
            int n = 0;
            foreach (ZipPacked.Item it in ZipPacked.ReadDirectory(path))
            {
                if (it.Name.EndsWith("/")) continue;
                if (!it.Name.StartsWith(clean + "/", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = it.Name.Substring(clean.Length + 1);
                string target = Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar));
                string d = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                File.WriteAllBytes(target, ZipPacked.ReadEntry(path, it, ""));
                n++;
            }
            return n > 0 ? outDir : null;
        }

        public string ExtractOne(string entryName, string destDir)
        {
            foreach (ZipPacked.Item it in ZipPacked.ReadDirectory(path))
            {
                if (!string.Equals(it.Name, entryName, StringComparison.Ordinal)) continue;
                if (it.Name.EndsWith("/")) return null;
                return EntryWrite.One(destDir, it.Name, ZipPacked.ReadEntry(path, it, ""));
            }
            return null;
        }

        public void ExtractAll(string destDir, Action<int, string> progress)
        {
            Directory.CreateDirectory(destDir);
            Archive.ClearErrors();
            using (FileStream fs = File.OpenRead(path))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                int n = 0;
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    n++;
                    string target = SafeJoin(destDir, e.FullName);
                    if (e.FullName.EndsWith("/"))
                    {
                        Directory.CreateDirectory(target);
                    }
                    else
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            e.ExtractToFile(target, true);
                        }
                        catch (Exception exOne) { Archive.NoteError(e.FullName, exOne); }   // 单个失败不中断
                    }
                    if (progress != null) progress(n, e.FullName);
                }
            }
        }

        /// <summary>
        /// 最近一次解压里失败的条目。
        /// 设计意图：单个文件写不进去（只读 / 被占用 / 被杀毒软件拦）时
        /// 不要整包失败，继续解其它的，最后把失败清单交给界面提示。
        /// </summary>
        public static readonly List<string> LastErrors = new List<string>();
        public static void ClearErrors() { LastErrors.Clear(); }
        public static void NoteError(string name, Exception ex)
        {
            string hint = "";
            if (ex is UnauthorizedAccessException)
                hint = "（目标只读 / 被占用 / 或被安全软件拦截）";
            else if (ex is IOException)
                hint = "（磁盘或路径问题，可能同名项已存在）";
            LastErrors.Add(name + hint + "  —  " + ex.Message);
        }

        public static string SafeJoin(string destDir, string entryName)
        {            // 防目录穿越
            string rel = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            rel = rel.TrimStart(Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(destDir, rel));
            string root = Path.GetFullPath(destDir);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("压缩包内含有非法路径: " + entryName);
            return full;
        }
    }

    // ---------------- TAR ----------------
    public sealed class TarExtractor : IExtractor
    {
        // 只在用的时候才打开文件，用完立刻关。
        // 以前构造函数里 File.OpenRead 会一直持有句柄 —— 打开一个 .tar / .tar.gz 之后
        // 这个文件就被本程序锁住，删不掉也改不了名，要等 GC 才释放。
        readonly string srcPath;      // 从文件读：每次操作临时打开、结束就关
        Stream stream;                // 从内存流读（TAR.GZ 解出来的）：直接复用
        public TarExtractor(string p) { srcPath = p; }
        public TarExtractor(Stream s) { stream = s; }
        public void SetPassword(string pwd) { }

        static string ReadString(byte[] b, int off, int len)
        {
            int end = off;
            while (end < off + len && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, off, end - off);
        }

        static long ReadOctal(byte[] b, int off, int len)
        {
            long v = 0; bool any = false;
            for (int i = off; i < off + len; i++)
            {
                byte c = b[i];
                if (c == 0 || c == ' ') { if (any) break; else continue; }
                if (c < '0' || c > '7') break;
                v = v * 8 + (c - '0'); any = true;
            }
            return v;
        }

        /// <summary>打开数据源；返回 true 表示这次是我们自己开的，用完要关</summary>
        bool OpenSource()
        {
            if (stream != null) return false;          // 内存流，不用关
            stream = File.OpenRead(srcPath);
            return true;
        }

        void CloseSource(bool mine)
        {
            if (!mine || stream == null) return;
            try { stream.Dispose(); } catch { }
            stream = null;
        }

        public List<EntryInfo> List()
        {
            bool mine = OpenSource();
            try
            {
            List<EntryInfo> list = new List<EntryInfo>();
            long pos = 0;
            byte[] hdr = new byte[512];
            while (true)
            {
                stream.Position = pos;
                int got = ReadFull(stream, hdr, 512);
                if (got < 512) break;
                bool allZero = true;
                for (int i = 0; i < 512; i++) if (hdr[i] != 0) { allZero = false; break; }
                if (allZero) break;

                string name = ReadString(hdr, 0, 100);
                if (name.Length == 0) break;
                long size = ReadOctal(hdr, 124, 12);
                char type = (char)hdr[156];
                string prefix = ReadString(hdr, 345, 155);
                if (prefix.Length > 0) name = prefix + "/" + name;

                EntryInfo ei = new EntryInfo();
                ei.Name = name;
                ei.Size = size;
                ei.IsDir = (type == '5') || name.EndsWith("/");
                list.Add(ei);

                pos += 512 + ((size + 511) / 512) * 512;
            }
            return list;
            }
            finally { CloseSource(mine); }
        }

        public string ExtractFolder(string folderPath, string destDir)
        {
            return EntryWrite.FolderViaExtractAll(this, folderPath, destDir);
        }

        public string ExtractOne(string entryName, string destDir)
        {
            return EntryWrite.ViaExtractAll(this, entryName, destDir);
        }

        public void ExtractAll(string destDir, Action<int, string> progress)
        {
            bool mine = OpenSource();
            try
            {
            Directory.CreateDirectory(destDir);
            long pos = 0;
            byte[] hdr = new byte[512];
            int n = 0;
            while (true)
            {
                stream.Position = pos;
                int got = ReadFull(stream, hdr, 512);
                if (got < 512) break;
                bool allZero = true;
                for (int i = 0; i < 512; i++) if (hdr[i] != 0) { allZero = false; break; }
                if (allZero) break;

                string name = ReadString(hdr, 0, 100);
                if (name.Length == 0) break;
                long size = ReadOctal(hdr, 124, 12);
                char type = (char)hdr[156];
                string prefix = ReadString(hdr, 345, 155);
                if (prefix.Length > 0) name = prefix + "/" + name;

                long dataPos = pos + 512;
                long next = dataPos + ((size + 511) / 512) * 512;

                if (type == '5' || name.EndsWith("/"))
                {
                    Directory.CreateDirectory(ZipExtractor.SafeJoin(destDir, name));
                }
                else if (type == '0' || type == 0 || type == '7')
                {
                    string target = ZipExtractor.SafeJoin(destDir, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    stream.Position = dataPos;
                    using (FileStream outFs = File.Create(target))
                    {
                        long left = size;
                        byte[] buf = new byte[65536];
                        while (left > 0)
                        {
                            int want = (int)Math.Min(buf.Length, left);
                            int r = stream.Read(buf, 0, want);
                            if (r <= 0) break;
                            outFs.Write(buf, 0, r);
                            left -= r;
                        }
                    }
                    n++;
                    if (progress != null) progress(n, name);
                }
                pos = next;
            }
            }
            finally { CloseSource(mine); }
        }

        static int ReadFull(Stream s, byte[] buf, int count)
        {
            int total = 0;
            while (total < count)
            {
                int r = s.Read(buf, total, count - total);
                if (r <= 0) break;
                total += r;
            }
            return total;
        }
    }

    // ---------------- GZ ----------------
    public sealed class GzExtractor : IExtractor
    {
        readonly string path;
        public GzExtractor(string p) { path = p; }
        public void SetPassword(string pwd) { }

        public string SuggestedName()
        {
            string n = Path.GetFileName(path);
            if (n.ToLowerInvariant().EndsWith(".gz")) return n.Substring(0, n.Length - 3);
            return n + ".out";
        }

        public List<EntryInfo> List()
        {
            List<EntryInfo> list = new List<EntryInfo>();
            EntryInfo i = new EntryInfo();
            i.Name = SuggestedName();
            i.Size = -1;
            list.Add(i);
            return list;
        }

        public string ExtractFolder(string folderPath, string destDir)
        {
            return EntryWrite.FolderViaExtractAll(this, folderPath, destDir);
        }

        public string ExtractOne(string entryName, string destDir)
        {
            return EntryWrite.ViaExtractAll(this, entryName, destDir);
        }

        public void ExtractAll(string destDir, Action<int, string> progress)
        {
            Directory.CreateDirectory(destDir);
            string target = Path.Combine(destDir, SuggestedName());
            using (FileStream inFs = File.OpenRead(path))
            using (GZipStream gz = new GZipStream(inFs, CompressionMode.Decompress))
            using (FileStream outFs = File.Create(target))
            {
                byte[] buf = new byte[65536];
                int r;
                while ((r = gz.Read(buf, 0, buf.Length)) > 0) outFs.Write(buf, 0, r);
            }
            if (progress != null) progress(1, SuggestedName());
        }
    }

    // ---------------- FIP ----------------
    public sealed class FipExtractor : IExtractor
    {
        readonly string path;
        string password;
        byte[] payload;
        byte kind;
        string origName;

        public FipExtractor(string p) { path = p; }
        public void SetPassword(string pwd) { password = pwd; }

        void Ensure()
        {
            if (payload != null) return;
            payload = FipFile.Read(path, out kind, out origName, password);
        }

        public List<EntryInfo> List()
        {
            Ensure();
            if (kind != Kind.Archive)
            {
                List<EntryInfo> one = new List<EntryInfo>();
                EntryInfo i = new EntryInfo();
                i.Name = origName;
                i.Size = payload.Length;
                one.Add(i);
                return one;
            }
            // 压缩包：载荷是 zip
            List<EntryInfo> list = new List<EntryInfo>();
            using (MemoryStream ms = new MemoryStream(payload))
            using (ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    EntryInfo i = new EntryInfo();
                    i.Name = e.FullName;
                    i.Size = e.Length;
                    i.PackedSize = e.CompressedLength;
                    i.IsDir = e.FullName.EndsWith("/");
                    list.Add(i);
                }
            }
            return list;
        }

        /// <summary>FIP 压缩包里是 zip，可以随机访问；单文件种类直接写</summary>
        /// <summary>FIP 压缩包里是 zip，可以随机访问，只解这个子树</summary>
        public string ExtractFolder(string folderPath, string destDir)
        {
            Ensure();   // FIP 的载荷要按需加载
            string clean = (folderPath ?? "").TrimEnd('/');
            if (clean.Length == 0) return null;
            string outDir = Path.Combine(destDir, Path.GetFileName(clean));
            int k = 1;
            while (Directory.Exists(outDir)) { outDir = Path.Combine(destDir, Path.GetFileName(clean) + " (" + k + ")"); k++; }
            Directory.CreateDirectory(outDir);
            int n = 0;
            foreach (ZipPacked.Item it in ZipPacked.ReadDirectory(payload))
            {
                if (it.Name.EndsWith("/")) continue;
                if (!it.Name.StartsWith(clean + "/", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = it.Name.Substring(clean.Length + 1);
                string target = Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar));
                string d = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                File.WriteAllBytes(target, ZipPacked.ReadEntry(payload, it, "", path));
                n++;
            }
            return n > 0 ? outDir : null;
        }

        public string ExtractOne(string entryName, string destDir)
        {
            Ensure();
            if (kind != Kind.Archive)
            {
                if (!string.Equals(entryName, origName, StringComparison.Ordinal)) return null;
                return EntryWrite.One(destDir, origName, payload);
            }
            foreach (ZipPacked.Item it in ZipPacked.ReadDirectory(payload))
            {
                if (!string.Equals(it.Name, entryName, StringComparison.Ordinal)) continue;
                if (it.Name.EndsWith("/")) return null;
                return EntryWrite.One(destDir, it.Name, ZipPacked.ReadEntry(payload, it, "", path));
            }
            return null;
        }

        public void ExtractAll(string destDir, Action<int, string> progress)
        {
            Ensure();
            Directory.CreateDirectory(destDir);
            Archive.ClearErrors();
            if (kind != Kind.Archive)
            {
                // 单文件：还原成原文件名
                string target = ZipExtractor.SafeJoin(destDir, origName);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, payload);
                if (progress != null) progress(1, origName);
                return;
            }
            using (MemoryStream ms = new MemoryStream(payload))
            using (ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                int n = 0;
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    n++;
                    string target = ZipExtractor.SafeJoin(destDir, e.FullName);
                    if (e.FullName.EndsWith("/")) Directory.CreateDirectory(target);
                    else
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            e.ExtractToFile(target, true);
                        }
                        catch (Exception exOne) { Archive.NoteError(e.FullName, exOne); }   // 单个失败不中断
                    }
                    if (progress != null) progress(n, e.FullName);
                }
            }
        }
    }

    // ---------------- 外部 7-Zip / WinRAR 借力 ----------------
    public static class ExternalTools
    {
        public static string FindSevenZip()
        {
            string[] candidates = {
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
            };
            foreach (string c in candidates) if (File.Exists(c)) return c;
            return null;
        }

        public static string FindUnrar()
        {
            string[] candidates = {
                @"C:\Program Files\WinRAR\UnRAR.exe",
                @"C:\Program Files (x86)\WinRAR\UnRAR.exe",
                @"C:\Program Files\WinRAR\WinRAR.exe",
            };
            foreach (string c in candidates) if (File.Exists(c)) return c;
            return null;
        }

        public static bool CanHandle(int kind)
        {
            if (kind == ArchiveKind.SevenZip) return FindSevenZip() != null;
            if (kind == ArchiveKind.Rar) return FindSevenZip() != null || FindUnrar() != null;
            return false;
        }
    }

    // ---------------- 统一入口 ----------------
    public static class Archive
    {
        /// <summary>这个压缩包是否需要密码（FIP 加密 或 加密 ZIP）</summary>
        // ---- 解压失败条目汇总（实现在 ZipExtractor 里，这里转发一下，界面统一用 Archive.*）----
        public static List<string> LastErrors { get { return ZipExtractor.LastErrors; } }
        public static void ClearErrors() { ZipExtractor.ClearErrors(); }
        public static void NoteError(string name, Exception ex) { ZipExtractor.NoteError(name, ex); }

        public static bool NeedsPassword(string path)
        {
            try
            {
                if (FipFile.IsFip(path)) return FipFile.NeedsPassword(path);
                int k = ArchiveKind.Detect(path);
                if (k == ArchiveKind.Zip) return ZipPacked.IsEncrypted(path);
                // 7z / rar / 其它交给 7-Zip 判断（文件名加密的包必须给密码）
                if (k == ArchiveKind.SevenZip || k == ArchiveKind.Rar || k == ArchiveKind.Other)
                    return SevenZip.NeedsPassword(path);
            }
            catch { }
            return false;
        }

        public static IExtractor Open(string path)
        {
            int k = ArchiveKind.Detect(path);
            switch (k)
            {
                case ArchiveKind.Zip:
                    // 带密码的 ZIP 要用自己写的解密器，.NET 的 ZipArchive 不支持加密
                    if (ZipPacked.IsEncrypted(path)) return new EncryptedZipExtractor(path);
                    return new ZipExtractor(path);
                case ArchiveKind.Tar: return new TarExtractor(path);
                case ArchiveKind.Gz: return new GzExtractor(path);
                case ArchiveKind.TarGz:
                    {
                        // 先解 gz 到临时流，再按 tar 读
                        MemoryStream ms = new MemoryStream();
                        using (FileStream inFs = File.OpenRead(path))
                        using (GZipStream gz = new GZipStream(inFs, CompressionMode.Decompress))
                        {
                            byte[] buf = new byte[65536];
                            int r;
                            while ((r = gz.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, r);
                        }
                        ms.Position = 0;
                        return new TarExtractor(ms);
                    }
                case ArchiveKind.Fip: return new FipExtractor(path);
            // 7z / rar / iso / cab / xz / bz2 等：借力本机 7-Zip
            case ArchiveKind.SevenZip:
            case ArchiveKind.Rar:
                return new SevenZipExtractor(path);
            case ArchiveKind.Other:
                // 扩展名不认识也试一下：只要 7-Zip 能识别就行（.xz/.bz2/.cab/.iso/.lzh ...）
                if (SevenZip.Available && System.IO.File.Exists(path)) return new SevenZipExtractor(path);
                break;
            }
            throw new NotSupportedException(
                "不支持的格式: " + Path.GetExtension(path) +
                (k == ArchiveKind.Other ? "" : " (" + ArchiveKind.Describe(k) + ")"));
        }

        // ---------- 打包 ----------
        /// <summary>把若干文件/文件夹打包成 ZIP 字节流（不加密）</summary>
        public static byte[] CreateZip(string[] paths, string baseDir)
        {
            return CreateZip(paths, baseDir, null);
        }

        /// <summary>打包成 ZIP；password 非空则用标准 ZipCrypto 加密</summary>
        public static byte[] CreateZip(string[] paths, string baseDir, string password)
        {
            if (!string.IsNullOrEmpty(password))
                return ZipPacked.Create(paths, baseDir, password);   // 自己实现的加密 ZIP
            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (string p in paths) AddToZip(za, p, baseDir);
                }
                return ms.ToArray();
            }
        }

        static void AddToZip(ZipArchive za, string path, string baseDir)
        {
            if (Directory.Exists(path))
            {
                string[] files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    string rel = MakeRel(f, baseDir);
                    ZipArchiveEntry e = za.CreateEntry(rel.Replace('\\', '/'), CompressionLevel.Optimal);
                    using (Stream es = e.Open())
                    using (FileStream fs = File.OpenRead(f))
                    {
                        byte[] buf = new byte[65536];
                        int r;
                        while ((r = fs.Read(buf, 0, buf.Length)) > 0) es.Write(buf, 0, r);
                    }
                }
            }
            else if (File.Exists(path))
            {
                string rel = MakeRel(path, baseDir);
                ZipArchiveEntry e = za.CreateEntry(rel.Replace('\\', '/'), CompressionLevel.Optimal);
                using (Stream es = e.Open())
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] buf = new byte[65536];
                    int r;
                    while ((r = fs.Read(buf, 0, buf.Length)) > 0) es.Write(buf, 0, r);
                }
            }
        }

        static string MakeRel(string full, string baseDir)
        {
            string f = Path.GetFullPath(full);
            if (string.IsNullOrEmpty(baseDir)) return Path.GetFileName(f);
            string b = Path.GetFullPath(baseDir);
            if (!b.EndsWith(Path.DirectorySeparatorChar.ToString())) b += Path.DirectorySeparatorChar;
            if (f.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return f.Substring(b.Length);
            return Path.GetFileName(f);
        }

        /// <summary>打包成 FIP（内部是 zip，再加密）</summary>
        public static byte[] CreateFip(string[] paths, string baseDir, string displayName)
        {
            return CreateFip(paths, baseDir, displayName, null);
        }

        /// <summary>打包成 FIP，可加密码</summary>
        public static byte[] CreateFip(string[] paths, string baseDir, string displayName, string password)
        {
            byte[] zip = CreateZip(paths, baseDir);
            return FipFile.Write(Kind.Archive, displayName == null ? "archive" : displayName, zip, password);
        }

        /// <summary>打包成 TAR</summary>
        public static byte[] CreateTar(string[] paths, string baseDir)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                List<string> files = new List<string>();
                List<string> rels = new List<string>();
                foreach (string p in paths)
                {
                    if (Directory.Exists(p))
                        foreach (string f in Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                        { files.Add(f); rels.Add(MakeRel(f, baseDir)); }
                    else if (File.Exists(p))
                    { files.Add(p); rels.Add(MakeRel(p, baseDir)); }
                }
                for (int i = 0; i < files.Count; i++)
                    WriteTarEntry(ms, rels[i], File.ReadAllBytes(files[i]));
                // 结尾两个全 0 块
                ms.Write(new byte[1024], 0, 1024);
                return ms.ToArray();
            }
        }

        static void WriteTarEntry(Stream s, string name, byte[] data)
        {
            byte[] hdr = new byte[512];
            byte[] nb = Encoding.UTF8.GetBytes(name.Replace('\\', '/'));
            int copy = Math.Min(nb.Length, 100);
            Buffer.BlockCopy(nb, 0, hdr, 0, copy);

            WriteOctal(hdr, 100, 8, 420);            // mode 0644
            WriteOctal(hdr, 108, 8, 0);              // uid
            WriteOctal(hdr, 116, 8, 0);              // gid
            WriteOctal(hdr, 124, 12, data.Length);   // size
            WriteOctal(hdr, 136, 12, 0);             // mtime
            hdr[156] = (byte)'0';                    // 普通文件
            // checksum 位置先填空格
            for (int i = 148; i < 156; i++) hdr[i] = (byte)' ';
            int sum = 0;
            for (int i = 0; i < 512; i++) sum += hdr[i];
            byte[] cs = Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
            Buffer.BlockCopy(cs, 0, hdr, 148, Math.Min(cs.Length, 8));

            s.Write(hdr, 0, 512);
            s.Write(data, 0, data.Length);
            int pad = (512 - (data.Length % 512)) % 512;
            if (pad > 0) s.Write(new byte[pad], 0, pad);
        }

        static void WriteOctal(byte[] hdr, int off, int len, long value)
        {
            string s = Convert.ToString(value, 8).PadLeft(len - 1, '0');
            byte[] b = Encoding.ASCII.GetBytes(s);
            Buffer.BlockCopy(b, 0, hdr, off, Math.Min(b.Length, len - 1));
        }

        /// <summary>用自带的 7-Zip 打包成 .7z</summary>
        public static byte[] Create7z(string[] paths, string baseDir, string password)
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), "FipArc7z_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            string outFile = Path.Combine(tmpDir, "out.7z");
            try
            {
                StringBuilder args = new StringBuilder();
                args.Append("a -t7z -y -bd \"").Append(outFile).Append("\"");
                // 有密码就 AES-256 加密，并且连文件名一起加密（-mhe=on）
                if (!string.IsNullOrEmpty(password))
                    args.Append(" -p\"").Append(password.Replace("\"", "")).Append("\" -mhe=on");
                foreach (string p in paths) args.Append(" \"").Append(p).Append("\"");
                SevenZip.Run(args.ToString(), 600000);
                if (!File.Exists(outFile))
                    throw new Exception("7-Zip 没有生成压缩包，可能某个文件正被占用。");
                return File.ReadAllBytes(outFile);
            }
            finally { try { Directory.Delete(tmpDir, true); } catch { } }
        }

        /// <summary>单文件压缩成 gz</summary>
        public static byte[] CreateGz(string file)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream gz = new GZipStream(ms, CompressionMode.Compress, true))
                using (FileStream fs = File.OpenRead(file))
                {
                    byte[] buf = new byte[65536];
                    int r;
                    while ((r = fs.Read(buf, 0, buf.Length)) > 0) gz.Write(buf, 0, r);
                }
                return ms.ToArray();
            }
        }
    }
}
