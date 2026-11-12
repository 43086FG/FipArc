// Playable.cs —— 把 FIP 格式临时还原成可直接播放的普通文件
using System;
using System.IO;

namespace FipArc
{
    public static class Playable
    {
        /// <summary>
        /// 如果 path 是本软件的专有格式，解密还原到临时目录并返回临时文件路径。
        /// 否则原样返回。isTemp 表示调用方用完后应删除。
        /// </summary>
        public static string Resolve(string path, out bool isTemp)
        {
            return Resolve(path, out isTemp, null);
        }

        public static string Resolve(string path, out bool isTemp, string password)
        {
            isTemp = false;
#if SE
            // 精简版不支持自定义 f 格式（.fmp/.fpg/.fjp/.fp4/.fov/.fxt），
            // 直接把原路径交回去，当普通文件处理。
            return path;
#else
            string ext = Path.GetExtension(path).ToLowerInvariant();
            bool isOwn = false;
            foreach (string e in Shell.OwnExts)
                if (ext == e) { isOwn = true; break; }

            if (!isOwn && !FipFile.IsFip(path)) return path;

            byte kind;
            string origName;
            byte[] data = FipFile.Read(path, out kind, out origName, password);

            string srcExt;
            // 归档类不走这里
            if (kind == Kind.Archive)
                throw new InvalidOperationException("这是 FIP 压缩包，不是媒体文件");

            // 依据实际类型推断扩展名
            switch (kind)
            {
                case Kind.AudioFmp: srcExt = ".mp3"; break;
                case Kind.ImagePng: srcExt = ".png"; break;
                case Kind.ImageJpg: srcExt = ".jpg"; break;
                case Kind.VideoMp4: srcExt = ".mp4"; break;
                case Kind.VideoMov: srcExt = ".mov"; break;
                case Kind.TextTxt: srcExt = ".txt"; break;
                default: srcExt = ".bin"; break;
            }

            string tmpDir = Path.Combine(Path.GetTempPath(), "FipArcPlay");
            Directory.CreateDirectory(tmpDir);
            CleanOldTemps(tmpDir);
            // 用原文件名命名临时文件，这样查看器标题里显示的是有意义的名字
            string stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(stem)) stem = "file";
            string tmp = Path.Combine(tmpDir, stem + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + srcExt);
            File.WriteAllBytes(tmp, data);
            isTemp = true;
            return tmp;
#endif
        }

        /// <summary>清掉上次运行遗留的临时文件（超过 1 天的）</summary>
        static void CleanOldTemps(string dir)
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-1);
                foreach (string f in Directory.GetFiles(dir))
                {
                    try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>判断能否用内置播放器/查看器打开</summary>
        public static bool IsMedia(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            // 直接读 Shell.MediaExts，保证"打开方式"和播放器认的格式永远一致
            foreach (string e in Shell.MediaExts) if (ext == e) return true;
            // f 格式媒体（精简版没有）
            foreach (string e in Shell.OwnExts)
                if (ext == e && (e == ".fmp" || e == ".fp4" || e == ".fov")) return true;
            return false;
        }

        public static bool IsImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string[] v = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp",
                           ".fpg", ".fjp" };
            foreach (string e in v) if (ext == e) return true;
            return false;
        }

        public static bool IsText(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string[] v = { ".txt", ".log", ".ini", ".cfg", ".json", ".xml", ".csv", ".md", ".fxt" };
            foreach (string e in v) if (ext == e) return true;
            return false;
        }
    }
}
