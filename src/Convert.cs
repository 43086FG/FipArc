// Convert.cs —— 互转：mp3<->fmp, png<->fpg, jpg<->fjp, mp4<->fp4, mov<->fov, txt<->fxt
using System;
using System.IO;

namespace FipArc
{
    public static class Convert2
    {
        /// <summary>判断这个文件能不能转成 f 格式</summary>
        public static bool CanConvertToFip(string path)
        {
#if SE
            return false;       // 精简版没有 f 格式转换
#else
            byte kind; string fExt;
            return Kind.TryFromSourceExt(Path.GetExtension(path), out kind, out fExt);
#endif
        }

        /// <summary>判断这个文件能不能从 f 格式还原</summary>
        public static bool CanConvertFromFip(string path)
        {
#if SE
            return false;       // 精简版没有 f 格式还原
#else
            byte kind; string srcExt;
            return Kind.TryFromFipExt(Path.GetExtension(path), out kind, out srcExt);
#endif
        }

        /// <summary>源文件 -> f 格式。返回输出路径</summary>
        public static string ToFip(string srcPath)
        {
            return ToFip(srcPath, null);
        }

        /// <summary>源文件 -> f 格式（可加密码）</summary>
        public static string ToFip(string srcPath, string password)
        {
            byte kind; string fExt;
            if (!Kind.TryFromSourceExt(Path.GetExtension(srcPath), out kind, out fExt))
                throw new NotSupportedException(
                    "这个类型不支持转换: " + Path.GetExtension(srcPath) + "\n" +
                    "支持: mp3->fmp, png->fpg, jpg->fjp, mp4->fp4, mov->fov, txt->fxt");

            string name = Path.GetFileName(srcPath);
            byte[] data = File.ReadAllBytes(srcPath);
            byte[] outBytes = FipFile.Write(kind, name, data, password);

            string dest = Path.Combine(Path.GetDirectoryName(srcPath),
                                       Path.GetFileNameWithoutExtension(srcPath) + fExt);
            dest = UniquePath(dest);
            File.WriteAllBytes(dest, outBytes);
            return dest;
        }

        /// <summary>f 格式 -> 还原成原始类型。返回输出路径</summary>
        public static string FromFip(string fipPath)
        {
            return FromFip(fipPath, null);
        }

        /// <summary>f 格式 -> 还原（需要密码时传入）</summary>
        public static string FromFip(string fipPath, string password)
        {
            byte kind; string srcExt;
            if (!Kind.TryFromFipExt(Path.GetExtension(fipPath), out kind, out srcExt))
                throw new NotSupportedException("不是可还原的 FIP 文件类型: " + Path.GetExtension(fipPath));

            byte realKind; string origName;
            byte[] data = FipFile.Read(fipPath, out realKind, out origName, password);
            if (realKind != kind)
                throw new InvalidDataException(
                    string.Format("文件类型不符：扩展名是 {0}，内容却是 {1}", Path.GetExtension(fipPath), Kind.Describe(realKind)));

            string baseName = Path.GetFileNameWithoutExtension(fipPath);
            string outName = string.IsNullOrEmpty(origName) ? baseName + srcExt : origName;
            // 保证扩展名正确
            string curExt = Path.GetExtension(outName);
            if (!string.Equals(curExt, srcExt, StringComparison.OrdinalIgnoreCase))
                outName = Path.GetFileNameWithoutExtension(outName) + srcExt;

            string dest = UniquePath(Path.Combine(Path.GetDirectoryName(fipPath), outName));
            File.WriteAllBytes(dest, data);
            return dest;
        }

        /// <summary>如果目标已存在，自动加 (1) (2)</summary>
        public static string UniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 10000; i++)
            {
                string p = Path.Combine(dir, string.Format("{0} ({1}){2}", name, i, ext));
                if (!File.Exists(p) && !Directory.Exists(p)) return p;
            }
            return path;
        }
    }
}
