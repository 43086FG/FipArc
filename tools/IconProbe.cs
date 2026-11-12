// IconProbe.cs —— 探测 Windows 对某个文件【实际显示】的图标
// 用法: IconProbe.exe <文件路径> <输出png>
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

static class IconProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);

    const uint SHGFI_ICON = 0x000000100;
    const uint SHGFI_LARGEICON = 0x000000000;
    const uint SHGFI_SMALLICON = 0x000000001;
    const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: IconProbe.exe <文件路径> <输出png> [small]");
            return 2;
        }
        string path = args[0];
        string outFile = args[1];
        bool small = args.Length > 2 && args[2] == "small";

        SHFILEINFO fi = new SHFILEINFO();
        uint flags = SHGFI_ICON | (small ? SHGFI_SMALLICON : SHGFI_LARGEICON);
        IntPtr r = SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf(fi), flags);
        if (r == IntPtr.Zero || fi.hIcon == IntPtr.Zero)
        {
            Console.WriteLine("SHGetFileInfo 失败");
            return 1;
        }

        try
        {
            using (Icon ic = Icon.FromHandle(fi.hIcon))
            {
                int size = small ? 64 : 256;
                using (Bitmap bmp = new Bitmap(size, size))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawIcon(ic, new Rectangle(0, 0, size, size));
                    }
                    bmp.Save(outFile, ImageFormat.Png);
                }
                Console.WriteLine("图标已保存: " + outFile);
                Console.WriteLine("图标原始尺寸: " + ic.Width + "x" + ic.Height);
            }
        }
        finally { DestroyIcon(fi.hIcon); }

        // 同时打印注册表解析链，便于对照
        Console.WriteLine();
        Console.WriteLine("--- 注册表解析链 ---");
        Dump(@"\", path);
        return 0;
    }

    static void Dump(string hive, string path)
    {
        string ext = System.IO.Path.GetExtension(path);
        Microsoft.Win32.RegistryKey hkcr = Microsoft.Win32.Registry.ClassesRoot;
        Microsoft.Win32.RegistryKey k = hkcr.OpenSubKey(ext);
        string progid = k == null ? "<无 .ext 键>" : (string)k.GetValue("");
        if (k != null) k.Close();
        Console.WriteLine("HKCR\\" + ext + " 默认值(ProgID) = [" + progid + "]");

        Microsoft.Win32.RegistryKey ic = hkcr.OpenSubKey(ext + @"\DefaultIcon");
        Console.WriteLine("HKCR\\" + ext + "\\DefaultIcon = [" + (ic == null ? "<无>" : (string)ic.GetValue("")) + "]");
        if (ic != null) ic.Close();

        if (!string.IsNullOrEmpty(progid))
        {
            Microsoft.Win32.RegistryKey pi = hkcr.OpenSubKey(progid + @"\DefaultIcon");
            Console.WriteLine("HKCR\\" + progid + "\\DefaultIcon = [" + (pi == null ? "<无>" : (string)pi.GetValue("")) + "]");
            if (pi != null) pi.Close();
        }

        Microsoft.Win32.RegistryKey sfa = hkcr.OpenSubKey(@"SystemFileAssociations\" + ext + @"\DefaultIcon");
        Console.WriteLine("HKCR\\SystemFileAssociations" + ext + "\\DefaultIcon = [" + (sfa == null ? "<无>" : (string)sfa.GetValue("")) + "]");
        if (sfa != null) sfa.Close();
    }
}
