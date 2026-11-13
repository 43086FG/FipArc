// Cache.cs —— 缓存 / 临时文件清理
//
// 本软件运行时会在 %TEMP% 下产生这些东西：
//   FipArcDrag\        从压缩包拖出去时解出来的临时文件
//   FipArcPlay\        播放器把 .fmp/.fp4 解出来的临时文件
//   FipArcPreview\     双击压缩包内文件时的临时解压
//   FipArcOne_xxx\     拖单个文件时的临时目录
//   FipArcDir_xxx\     拖文件夹时的临时目录
//   fiparc_install.log 安装诊断日志
//   fiparc_uninst_*.bat 卸载收尾批处理
//
// 正常退出时大部分会被自己删掉，但进程被强杀、崩溃、或文件被杀毒软件占用时会残留。
// 这个模块负责把它们清干净，并报告释放了多少空间。
using System;
using System.IO;
using System.Collections.Generic;

namespace FipArc
{
    public sealed class CacheInfo
    {
        public int FileCount;
        public int DirCount;
        public long Bytes;
        public List<string> Paths = new List<string>();

        public string Describe()
        {
            return string.Format(Lang.S("cache.summary"), FileCount, DirCount, Cache.Size(Bytes));
        }
    }

    public static class Cache
    {
        /// <summary>需要整个删掉的临时目录名</summary>
        static readonly string[] TempDirs = { "FipArcDrag", "FipArcPlay", "FipArcPreview" };

        /// <summary>需要按前缀删的临时文件/目录</summary>
        static readonly string[] TempPrefixes = {
            "FipArcOne_", "FipArcDir_", "fiparc_uninst_", "fiparc_install", "FipArcAdd_"
        };

        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double v = bytes; string[] u = { "KB", "MB", "GB" };
            int i = -1;
            do { v /= 1024.0; i++; } while (v >= 1024 && i < u.Length - 1);
            return string.Format("{0:0.##} {1}", v, u[i]);
        }

        /// <summary>列出将要清理的目标并统计大小（不删除）</summary>
        public static CacheInfo Scan()
        {
            CacheInfo info = new CacheInfo();
            string temp = Path.GetTempPath();

            foreach (string name in TempDirs)
            {
                string p = Path.Combine(temp, name);
                if (!Directory.Exists(p)) continue;
                info.Paths.Add(p);
                MeasureDir(p, info);
            }

            try
            {
                foreach (string entry in Directory.GetFileSystemEntries(temp))
                {
                    string leaf = Path.GetFileName(entry);
                    bool match = false;
                    foreach (string pre in TempPrefixes)
                        if (leaf.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                    if (!match) continue;
                    if (info.Paths.Contains(entry)) continue;

                    info.Paths.Add(entry);
                    if (Directory.Exists(entry)) MeasureDir(entry, info);
                    else
                    {
                        try { info.Bytes += new FileInfo(entry).Length; info.FileCount++; }
                        catch { }
                    }
                }
            }
            catch { }

            return info;
        }

        static void MeasureDir(string dir, CacheInfo info)
        {
            info.DirCount++;
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    try { info.Bytes += new FileInfo(f).Length; info.FileCount++; }
                    catch { }
                }
                foreach (string d in Directory.GetDirectories(dir))
                    MeasureDir(d, info);
            }
            catch { }
        }

        /// <summary>真正删除。返回删除失败的项数。</summary>
        public static int Clear(Action<string> log)
        {
            CacheInfo info = Scan();
            int failed = 0;

            foreach (string p in info.Paths)
            {
                try
                {
                    if (Directory.Exists(p)) Directory.Delete(p, true);
                    else if (File.Exists(p)) File.Delete(p);
                    if (log != null) log("已删除  " + p);
                }
                catch
                {
                    failed++;
                    // 删不掉（被占用）就只清里面的内容，目录留着
                    try
                    {
                        foreach (string f in Directory.GetFiles(p))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                    catch { }
                    if (log != null) log("占用中  " + p + "（已尽力清理内部文件）");
                }
            }
            return failed;
        }
    }
}
