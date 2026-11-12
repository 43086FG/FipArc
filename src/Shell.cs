// Shell.cs —— Windows Shell 集成
//
//   右键菜单：
//     · 选中文件/文件夹 -> 「压缩为」级联菜单（FIP / ZIP / TAR / TAR.GZ / GZ）
//     · 按文件类型给出专属转换菜单：
//           .mp3 -> 「转换为 FIP 音频 (.fmp)」
//           .png -> 「转换为 FIP 图片 (.fpg)」
//           …
//           .fmp -> 「还原为 MP3」
//           .fxt -> 「还原为 TXT」
//     · 压缩包上 -> 「解压到当前文件夹」「用 FipArc 打开」
//     · 文件夹空白处【不】显示任何菜单
//
//   图标：所有压缩格式的文件图标替换成本软件的图标（卸载时还原）
//
// 说明：这份文件同时被编译进 FipArc.exe 和 FipArcSetup.exe，
//       所以安装程序和主程序的注册表行为永远一致，不会各写一套产生偏差。
using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Win32;

namespace FipArc
{
    public static class Shell
    {
#if SE
        public const string PROGID_ARCHIVE = "FipArc.Archive";
        public const string PROGID_AUDIO = "FipArc.Audio";
        public const string PROGID_VIDEO = "FipArc.Video";
        public const string PROGID_MEDIA = "FipArc.Media";
        public const string PROGID_IMAGE = "FipArc.Image";
        public const string PROGID_TEXT = "FipArc.Text";
        public const string UNINSTALL_KEY = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FipArc";
        const string BACKUP_KEY = @"Software\FipArc\IconBackup";
        const string CAP_KEY = @"Software\FipArc\Capabilities";
        const string REG_APP_NAME = "FipArc";
#else
        public const string PROGID_ARCHIVE = "FipArc.Archive";
        public const string PROGID_AUDIO = "FipArc.Audio";
        public const string PROGID_VIDEO = "FipArc.Video";
        public const string PROGID_MEDIA = "FipArc.Media";
        public const string PROGID_IMAGE = "FipArc.Image";
        public const string PROGID_TEXT = "FipArc.Text";
        public const string UNINSTALL_KEY = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FipArc";
        const string BACKUP_KEY = @"Software\FipArc\IconBackup";
        const string CAP_KEY = @"Software\FipArc\Capabilities";
        const string REG_APP_NAME = "FipArc";
#endif

        /// <summary>安装后图标会被替换的所有压缩格式</summary>
        public static readonly string[] ArchiveExts = {
            // 只保留 .fip。其它压缩包 / 音视频 / 图片 / 文本一律不抢关联、不改图标。
            ".fip"
        };

        /// <summary>本软件自己的格式</summary>
        public static readonly string[] OwnExts = {
#if SE
            ".fip"                       // 精简版：只保留 .fip
#else
            ".fip", ".fmp", ".fpg", ".fjp", ".fp4", ".fov", ".fxt"
#endif
        };

        // ---------------- 转换映射 ----------------
        sealed class Conv
        {
            public string Src, Dest, Desc;
            public Conv(string s, string d, string desc) { Src = s; Dest = d; Desc = desc; }
        }

        // 普通格式 -> f 格式（右键「转换为 FIP XXX」）
        static readonly Conv[] ToFipMap = new Conv[] {
#if SE
            // 精简版不做 f 格式转换
#else
            new Conv(".mp3",  ".fmp", "音频"),
            new Conv(".png",  ".fpg", "图片"),
            new Conv(".jpg",  ".fjp", "图片"),
            new Conv(".jpeg", ".fjp", "图片"),
            new Conv(".mp4",  ".fp4", "视频"),
            new Conv(".mov",  ".fov", "视频"),
            new Conv(".txt",  ".fxt", "文本"),
#endif
        };

        // f 格式 -> 普通格式（右键「还原为 XXX」）
        static readonly Conv[] FromFipMap = new Conv[] {
#if SE
            // 精简版不做 f 格式还原
#else
            new Conv(".fmp", "MP3", null),
            new Conv(".fpg", "PNG", null),
            new Conv(".fjp", "JPG", null),
            new Conv(".fp4", "MP4", null),
            new Conv(".fov", "MOV", null),
            new Conv(".fxt", "TXT", null),
#endif
        };

        // ---------------- 压缩格式 ----------------
        sealed class PackFmt
        {
            public string Key, Ext, Label;
            public PackFmt(string k, string e, string l) { Key = k; Ext = e; Label = l; }
        }

        static readonly PackFmt[] PackFormats = new PackFmt[] {
            new PackFmt("fip", ".fip",    "FIP 格式 (.fip · 可设密码)"),
            new PackFmt("zip", ".zip",    "ZIP 格式 (.zip · 可设密码)"),
            new PackFmt("7z",  ".7z",     "7Z 格式 (.7z · 可设密码)"),
            new PackFmt("tar", ".tar",    "TAR 格式 (.tar)"),
            new PackFmt("tgz", ".tar.gz", "TAR.GZ 格式 (.tar.gz)"),
            new PackFmt("gz",  ".gz",     "GZ 格式 (.gz，仅单文件)"),
        };

        /// <summary>某个压缩格式对应的扩展名</summary>
        public static string ExtForPackKey(string key)
        {
            foreach (PackFmt f in PackFormats)
                if (string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)) return f.Ext;
            return ".fip";
        }

        static string Q(string s) { return "\"" + s + "\""; }

        // =====================================================================
        //  注册
        // =====================================================================
        public static void RegisterContextMenu(string exePath, string iconPath)
        {
            // ---- 压缩：级联菜单（选中文件 / 选中文件夹）----
            CascadePack(@"Software\Classes\*\shell\FipArcPack", "压缩为(&C)", iconPath, exePath);
            CascadePack(@"Software\Classes\Directory\shell\FipArcPack", "压缩为(&C)", iconPath, exePath);
            // 注意：不注册 Directory\Background —— 那样在文件夹空白处也会出现菜单，
            //       那时并没有选中任何文件。

            // ---- 按文件类型的专属转换菜单 ----
            // 注意：必须注册到 SystemFileAssociations 下面！
            //   .zip / .txt 这类扩展名本身已经关联了 ProgID，
            //   Windows 会【忽略】Software\Classes\<ext>\shell 下的菜单项。
            //   实测：.zip\shell\X 不显示，SystemFileAssociations\.zip\shell\X 显示。
            foreach (Conv c in ToFipMap)
            {
                RegMenu(SfaKey(c.Src) + @"\shell\FipArcToFip",
                        "转换为 " + c.Dest + " 格式（" + c.Desc + "）(&F)",
                        iconPath, Q(exePath) + " /tofip \"%1\"");
            }
            foreach (Conv c in FromFipMap)
            {
                RegMenu(SfaKey(c.Src) + @"\shell\FipArcFromFip",
                        "还原为 " + c.Dest + "(&R)",
                        iconPath, Q(exePath) + " /fromfip \"%1\"");
            }

            // ---- 压缩包上：解压 / 打开 ----
            foreach (string ext in ArchiveExts)
            {
                string baseKey = SfaKey(ext);
                RegMenu(baseKey + @"\shell\FipArcExtract", "解压到当前文件夹(&E)",
                        iconPath, Q(exePath) + " /extract \"%1\"");
                RegMenu(baseKey + @"\shell\FipArcOpen", "用 FipArc 打开(&O)",
                        iconPath, Q(exePath) + " /open \"%1\"");
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(baseKey + @"\shell\FipArcExtract"))
                    if (k != null)
                    {
                        k.SetValue("MultiSelectModel", "Player");
                        k.SetValue("Position", "top");
                    }
            }

            // ---- 自己的格式：双击自动打开 ----
            RegisterOwnProgids(exePath, iconPath);

            // ---- 所有压缩格式的图标都换成本软件 ----
            ReplaceIcons(iconPath);
        }

        /// <summary>按文件类型的菜单必须放在这里，放在 &lt;ext&gt;\shell 下会被忽略</summary>
        static string SfaKey(string ext)
        {
            return @"Software\Classes\SystemFileAssociations\" + ext;
        }

        static void CascadePack(string keyPath, string caption, string iconPath, string exePath)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                if (k == null) return;
                k.SetValue("MUIVerb", caption);
                k.SetValue("Icon", Q(iconPath));
                k.SetValue("SubCommands", "");      // 空字符串 = 启用级联子菜单
                using (RegistryKey shell = k.CreateSubKey("shell"))
                {
                    if (shell == null) return;
                    foreach (PackFmt f in PackFormats)
                    {
                        using (RegistryKey c = shell.CreateSubKey(f.Key))
                        {
                            if (c == null) continue;
                            c.SetValue("MUIVerb", f.Label);
                            using (RegistryKey cmd = c.CreateSubKey("command"))
                                if (cmd != null)
                                    cmd.SetValue("", Q(exePath) + " /pack " + f.Key + " \"%1\"");
                        }
                    }
                }
            }
        }

        static void RegMenu(string keyPath, string caption, string iconPath, string command)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                if (k == null) return;
                k.SetValue("", caption);
                k.SetValue("MUIVerb", caption);
                k.SetValue("Icon", Q(iconPath));
                using (RegistryKey cmd = k.CreateSubKey("command"))
                    if (cmd != null) cmd.SetValue("", command);
            }
        }

        /// <summary>常见音视频扩展名：只登记到「打开方式」列表里，不抢默认</summary>
        static string[] mediaExtsCache;

        /// <summary>音视频扩展名并集（惰性初始化，避免静态字段声明顺序问题）</summary>
        public static string[] MediaExts
        {
            get
            {
                if (mediaExtsCache == null)
                {
                    List<string> all = new List<string>();
                    all.AddRange(VideoExts);
                    all.AddRange(AudioExts);
                    mediaExtsCache = all.ToArray();
                }
                return mediaExtsCache;
            }
        }

        /// <summary>视频扩展名</summary>
        public static readonly string[] VideoExts = {
            ".mp4", ".m4v", ".mkv", ".mka", ".avi", ".mov", ".wmv", ".asf",
            ".flv", ".f4v", ".webm", ".ogv", ".mpg", ".mpeg", ".m2v",
            ".ts", ".m2ts", ".mts", ".3gp", ".3g2"
        };

        /// <summary>音频扩展名（含 mp2 / m4a / ape / ogg / opus / aac）</summary>
        public static readonly string[] AudioExts = {
            ".mp3", ".mp2", ".mpa", ".wav", ".flac", ".aac", ".m4a", ".m4b",
            ".ogg", ".oga", ".opus", ".wma", ".ape", ".wv", ".tta", ".aiff", ".aif", ".au", ".amr"
        };

        /// <summary>常见图片扩展名：同样只登记进「打开方式」</summary>
        public static readonly string[] ImageExts = {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico"
        };

        /// <summary>常见文本扩展名：同样只登记进「打开方式」</summary>
        public static readonly string[] TextExts = {
            // 只放"文档类"文本。.bat / .cmd / .ps1 是可执行脚本，
            // 它们原来有自己的图标（齿轮 / 终端），不能换成文本图标。
            ".txt", ".log", ".ini", ".cfg", ".json", ".xml", ".csv", ".md", ".nfo"
        };

        /// <summary>把一个 ProgID 挂到扩展名的「打开方式」候选列表里（不抢默认）</summary>
        static void AddOpenWith(string ext, string progid)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ext))
                {
                    if (k == null) return;
                    using (RegistryKey owp = k.CreateSubKey("OpenWithProgids"))
                        if (owp != null) owp.SetValue(progid, new byte[0], RegistryValueKind.Binary);
                }
                // 再挂一层 Application 关联，让「打开方式 → 更多应用」里也出现
                // FipArc 这个名字（而不是一串 ProgID）
                using (RegistryKey app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + progid))
                {
                    if (app != null)
                        using (RegistryKey list = app.CreateSubKey("SupportedTypes"))
                            if (list != null) list.SetValue(ext, "", RegistryValueKind.String);
                }
            }
            catch { }
        }

        /// <summary>
        /// 清理旧版本乱加的关联和图标：音视频 / 图片 / 文本 / 各类压缩包全部撤销，只保留 .fip。
        /// 默认值指向 FipArc 的整键删掉（回落系统默认）；只被改过图标的，删图标子键。
        /// </summary>
        public static void CleanupLegacy()
        {
            string[] exts = {
                ".zip", ".rar", ".7z", ".tar", ".gz", ".tar.gz", ".tgz", ".bz2", ".xz",
                ".cab", ".iso", ".jar", ".lzh", ".arj", ".z", ".lz",
                ".mp4", ".m4v", ".mkv", ".mka", ".avi", ".mov", ".wmv", ".asf",
                ".flv", ".f4v", ".webm", ".ogv", ".mpg", ".mpeg", ".m2v",
                ".ts", ".m2ts", ".mts", ".3gp", ".3g2",
                ".mp3", ".mp2", ".mpa", ".wav", ".flac", ".aac", ".m4a", ".m4b",
                ".ogg", ".oga", ".opus", ".wma", ".ape", ".wv", ".tta", ".aiff", ".aif", ".au", ".amr",
                ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico",
                ".txt", ".log", ".ini", ".cfg", ".json", ".xml", ".csv", ".md", ".nfo"
            };
            foreach (string ext in exts)
            {
                try
                {
                    string sub = @"Software\Classes\" + ext;
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(sub, true))
                    {
                        if (k == null) continue;
                        string v = k.GetValue("") as string;
                        if (v != null && v.StartsWith("FipArc", StringComparison.OrdinalIgnoreCase))
                        {
                            // 关联是我们写的：整键删掉，交还系统
                            try { k.Close(); } catch { }
                            Del(sub);
                        }
                        else
                        {
                            // 只是被我们改过图标 / 登记过打开方式：只删这两个子键
                            try { k.DeleteSubKeyTree("DefaultIcon", false); } catch { }
                            try { k.DeleteSubKeyTree("OpenWithProgids", false); } catch { }
                        }
                    }
                }
                catch { }
            }

            // 删掉多余的类型登记 —— .txt 会恢复成系统里的「文本文档」
            foreach (string pg in new string[] { PROGID_AUDIO, PROGID_VIDEO, PROGID_MEDIA, PROGID_IMAGE, PROGID_TEXT })
            {
                try { Del(@"Software\Classes\" + pg); } catch { }
            }
            try { Del(@"Software\FipArc\Capabilities"); } catch { }
            try { Del(@"Software\FipArcSE\Capabilities"); } catch { }
            try { Del(@"Software\FipArcSE"); } catch { }

            // 旧版本在「默认应用」列表里注册过自己，一并撤销
            try
            {
                using (RegistryKey ra = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
                {
                    if (ra != null)
                    {
                        try { ra.DeleteValue("FipArc", false); } catch { }
                        try { ra.DeleteValue("FipArcSE", false); } catch { }
                    }
                }
            }
            catch { }

            // 旧版本给 .json 写过错误的内容类型标记
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.json", true))
                {
                    if (k != null && "application/x-fiparc".Equals(k.GetValue("Content Type")))
                        k.DeleteValue("Content Type", false);
                }
            }
            catch { }

            // 还原被改过图标的系统扩展名（.zip/.7z 等回到系统默认图标）
            try { RestoreIcons(); } catch { }
        }
        static void RegisterOwnProgids(string exePath, string iconPath)
        {
            // 每种文件类型用各自的图标，不能全都套一个（看起来会很廉价）
            string dir = Path.GetDirectoryName(iconPath);
            string icoAudio = Path.Combine(dir, "audio.ico");
            string icoVideo = Path.Combine(dir, "video.ico");
            string icoImage = Path.Combine(dir, "image.ico");
            string icoText  = Path.Combine(dir, "text.ico");
            if (!File.Exists(icoAudio)) icoAudio = iconPath;
            if (!File.Exists(icoVideo)) icoVideo = iconPath;
            if (!File.Exists(icoImage)) icoImage = iconPath;
            if (!File.Exists(icoText))  icoText  = iconPath;

            Progid(PROGID_ARCHIVE, "FipArc 压缩包", iconPath, Q(exePath) + " /open \"%1\"");
            BindExt(".fip", PROGID_ARCHIVE, exePath);

            // ---- 关联策略：只保留 .fip ----
            // 其它格式一律不再绑定，并把旧版本写进注册表的关联和图标清理干净。
            CleanupLegacy();
            return;
        }

        /// <summary>
        /// 注册「默认应用」能力表。
        /// Windows 10/11 的「默认应用」页面只认 HKCU\Software\RegisteredApplications
        /// 指向的 Capabilities，没这一步程序根本不会出现在那个列表里，
        /// 用户也就没法把它设成播放器 / 看图 / 文本编辑的默认程序。
        /// </summary>
        static void RegisterCapabilities(string iconPath)
        {
            try
            {
                using (RegistryKey cap = Registry.CurrentUser.CreateSubKey(CAP_KEY))
                {
                    if (cap == null) return;
                    cap.SetValue("ApplicationName", "FipArc");
                    cap.SetValue("ApplicationDescription", "FipArc 压缩工具 · 媒体播放器 · 图片查看器 · 文本编辑器");
                    cap.SetValue("ApplicationIcon", iconPath + ",0");

                    using (RegistryKey fa = cap.CreateSubKey("FileAssociations"))
                    {
                        if (fa != null)
                        {
                            foreach (string ext in ArchiveExts) fa.SetValue(ext, PROGID_ARCHIVE);
                            foreach (string ext in VideoExts) fa.SetValue(ext, PROGID_VIDEO);
                            foreach (string ext in AudioExts) fa.SetValue(ext, PROGID_AUDIO);
#if !SE
                            foreach (string ext in OwnExts)
                                if (ext != ".fip") fa.SetValue(ext, ext == ".fxt" ? PROGID_TEXT : (ext == ".fpg" || ext == ".fjp" ? PROGID_IMAGE : PROGID_MEDIA));
#endif
                        }
                    }
                }

                using (RegistryKey ra = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                    if (ra != null) ra.SetValue(REG_APP_NAME, CAP_KEY);
            }
            catch { }
        }

        /// <summary>
        /// 打开 Windows 的「默认应用」设置页。
        /// Win10/11 不允许程序直接改默认应用（UserChoice 有哈希校验），
        /// 所以只能把用户送到设置页自己点。
        /// </summary>
        public static void OpenDefaultAppsSettings()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); }
            catch { }
        }

        /// <summary>
        /// 删掉 Windows 的「用户选择」记录（UserChoice），让 <ext> -> ProgID 的映射生效。
        /// Win8 以后 UserChoice 带哈希校验，只要它存在，系统就不认我们在 Classes 里写的关联。
        /// 删这个键一般能成功（键本身的 ACL 是可删的），系统随后会按 Classes 重新推导。
        /// </summary>
        public static void DropUserChoice(string ext)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + ext + @"\UserChoice",
                    false);
            }
            catch { }
        }

        static void Progid(string progid, string desc, string iconPath, string command)
        {
            string key = @"Software\Classes\" + progid;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key))
            {
                if (k == null) return;
                k.SetValue("", desc);
                k.SetValue("FriendlyTypeName", desc);
                using (RegistryKey ic = k.CreateSubKey("DefaultIcon"))
                {
                    if (ic != null)
                    {
                        // iconPath 已经是 "文件,索引" 形式（比如 imageres.dll,-102）就直接用
                        ic.SetValue("", iconPath.IndexOf(',') >= 0 ? iconPath : Q(iconPath) + ",0");
                    }
                }
                using (RegistryKey cmd = k.CreateSubKey(@"shell\open\command"))
                    if (cmd != null) cmd.SetValue("", command);
            }
        }

        static void BindExt(string ext, string progid, string exePath)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ext))
            {
                if (k == null) return;
                k.SetValue("", progid);
                k.SetValue("Content Type", "application/x-fiparc");
                k.SetValue("PerceivedType", "document");
                // 让 Windows「打开方式」列表里也能看到本程序
                using (RegistryKey owp = k.CreateSubKey("OpenWithProgids"))
                    if (owp != null) owp.SetValue(progid, new byte[0], RegistryValueKind.Binary);
            }

            // 注册到「应用程序」列表，双击和拖到 exe 上都能识别
            using (RegistryKey app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\FipArc.exe"))
            {
                if (app != null)
                {
                    using (RegistryKey cmd = app.CreateSubKey(@"shell\open\command"))
                        if (cmd != null) cmd.SetValue("", Q(exePath) + " /open \"%1\"");
                }
            }
        }

        // ---------------- 图标替换 ----------------
        //
        // 这里有个非常容易踩的坑，实测踩过两次：
        //
        //  1) 有 ProgID 时，.ext\DefaultIcon 被忽略，必须改 ProgID 的 DefaultIcon。
        //  2) 但如果只在 HKCU\Software\Classes\<ext> 下写 DefaultIcon（不写默认值），
        //     这个键会把 HKLM\Software\Classes\<ext> 的关联【整个遮蔽】掉，
        //     Windows 就认为 .zip 没有关联类型 —— 图标变成通用空白页，比不改还糟。
        //
        // 所以：有 ProgID 时，HKCU 里要把「扩展名 -> ProgID」映射一起写一份，
        //       保证遮蔽前后关联一致，再去改 ProgID 的图标。
        //       没有 ProgID 时，才直接给扩展名写 DefaultIcon。
        public static void ReplaceIcons(string iconPath)
        {
            foreach (string ext in ArchiveExts)
            {
                string progid = EffectiveProgid(ext);

                if (string.IsNullOrEmpty(progid))
                {
                    // 没有 ProgID：直接给扩展名写图标
                    using (RegistryKey ic = Registry.CurrentUser.CreateSubKey(
                        @"Software\Classes\" + ext + @"\DefaultIcon"))
                        if (ic != null) ic.SetValue("", iconPath + ",0");
                    continue;
                }

                if (progid.StartsWith("FipArc."))
                    continue;   // 自己的 ProgID，图标已在 Progid() 里设好

                SaveOriginalIcon(ext, progid);

                // 写一份一致的「扩展名 -> ProgID」映射，避免遮蔽导致关联丢失
                using (RegistryKey ek = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ext))
                    if (ek != null) ek.SetValue("", progid);

                // 清掉以前版本可能留下的扩展名级图标（现在由 ProgID 的图标生效）
                try { Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Classes\" + ext + @"\DefaultIcon", false); }
                catch { }

                using (RegistryKey ic = Registry.CurrentUser.CreateSubKey(
                    @"Software\Classes\" + progid + @"\DefaultIcon"))
                    if (ic != null) ic.SetValue("", iconPath + ",0");
            }
        }

        /// <summary>
        /// 取扩展名在系统里真正生效的 ProgID。
        /// 必须 HKCU -> HKLM 依次显式查：HKCR 合并视图在 .zip 这种情况下会返回空
        /// （HKCU\Software\Classes\.zip 只写了 DefaultIcon，没有默认值，
        ///  但 HKLM 里是 CompressedFolder，图标实际来自那里）。
        /// </summary>
        static string EffectiveProgid(string ext)
        {
            string p = TryProgid(Registry.CurrentUser, @"Software\Classes\" + ext);
            if (!string.IsNullOrEmpty(p)) return p;
            p = TryProgid(Registry.LocalMachine, @"Software\Classes\" + ext);
            if (!string.IsNullOrEmpty(p)) return p;
            return null;
        }

        static string TryProgid(RegistryKey hive, string subPath)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(subPath))
                {
                    if (k == null) return null;
                    object v = k.GetValue("");
                    if (v == null) return null;
                    string s = v.ToString();
                    return s.Length > 0 ? s : null;
                }
            }
            catch { }
            return null;
        }

        static void SaveOriginalIcon(string ext, string progid)
        {
            using (RegistryKey b = Registry.CurrentUser.CreateSubKey(BACKUP_KEY + "\\" + ext.Replace(".", "_")))
            {
                if (b == null) return;
                if (b.GetValue("progid") != null) return;   // 已备份过

                // 记录原始图标值，并记录 HKCU 下这个 ProgID 的 DefaultIcon 原本是否存在，
                // 卸载时若原本不存在就删掉覆盖，让 HKLM 里的系统默认值重新生效。
                bool existedHkcu = false;
                string icon = "";
                using (RegistryKey ic = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\" + progid + @"\DefaultIcon"))
                {
                    if (ic != null)
                    {
                        existedHkcu = true;
                        icon = (string)(ic.GetValue("") ?? "");
                    }
                }
                if (!existedHkcu)
                {
                    // HKCU 没有，读系统的真实原始值
                    try
                    {
                        using (RegistryKey ic2 = Registry.ClassesRoot.OpenSubKey(progid + @"\DefaultIcon"))
                            if (ic2 != null) icon = (string)(ic2.GetValue("") ?? "");
                    }
                    catch { }
                }

                b.SetValue("progid", progid);
                b.SetValue("icon", icon);
                b.SetValue("existedHkcu", existedHkcu ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        // =====================================================================
        //  卸载
        // =====================================================================
        public static void UnregisterAll()
        {
            UnregisterAll(null);
        }

        /// <summary>
        /// 清理注册表。
        /// onlyIfDir 非空时，只有当卸载登记项记录的 InstallLocation 与之一致，
        /// 才删除那条卸载登记 —— 否则卸载 A 目录会把 B 目录的登记也一起清掉
        /// （这会让控制面板里已经安装好的条目凭空消失）。
        /// </summary>
        public static void UnregisterAll(string onlyIfDir)
        {
            // 压缩菜单（含级联子项）
            Del(@"Software\Classes\*\shell\FipArcPack");
            Del(@"Software\Classes\Directory\shell\FipArcPack");

            // 历史遗留（旧版本注册过的）
            Del(@"Software\Classes\Directory\Background\shell\FipArcCompress");
            Del(@"Software\Classes\*\shell\FipArcCompress");
            Del(@"Software\Classes\*\shell\FipArcConvert");
            Del(@"Software\Classes\Directory\shell\FipArcCompress");

            // 转换菜单（新位置 SystemFileAssociations + 旧位置 <ext>\shell 一起清）
            foreach (Conv c in ToFipMap)
            {
                Del(SfaKey(c.Src) + @"\shell\FipArcToFip");
                Del(@"Software\Classes\" + c.Src + @"\shell\FipArcToFip");
            }
            foreach (Conv c in FromFipMap)
            {
                Del(SfaKey(c.Src) + @"\shell\FipArcFromFip");
                Del(@"Software\Classes\" + c.Src + @"\shell\FipArcFromFip");
            }

            // 解压 / 打开
            foreach (string ext in ArchiveExts)
            {
                Del(SfaKey(ext) + @"\shell\FipArcExtract");
                Del(SfaKey(ext) + @"\shell\FipArcOpen");
                Del(@"Software\Classes\" + ext + @"\shell\FipArcExtract");
                Del(@"Software\Classes\" + ext + @"\shell\FipArcOpen");
                Del(@"Software\Classes\" + ext + @"\DefaultIcon");
            }

            // ProgID
            foreach (string p in new string[] { PROGID_ARCHIVE, PROGID_MEDIA, PROGID_IMAGE, PROGID_TEXT })
                Del(@"Software\Classes\" + p);
            Del(@"Software\Classes\Applications\FipArc.exe");

            // 解除扩展名绑定
            foreach (string ext in OwnExts) Del(@"Software\Classes\" + ext);

            RestoreIcons();

            // 卸载登记：只有在卸载的就是登记的那个目录时才删，
            // 避免"卸载临时测试目录"把用户真实安装的条目一起清掉。
            bool dropUninstallKey = true;
            if (!string.IsNullOrEmpty(onlyIfDir))
            {
                try
                {
                    using (RegistryKey uk = Registry.CurrentUser.OpenSubKey(UNINSTALL_KEY))
                    {
                        string loc = uk == null ? null : (string)uk.GetValue("InstallLocation");
                        if (!string.IsNullOrEmpty(loc))
                        {
                            string a = System.IO.Path.GetFullPath(loc).TrimEnd('\\');
                            string b = System.IO.Path.GetFullPath(onlyIfDir).TrimEnd('\\');
                            if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                                dropUninstallKey = false;
                        }
                    }
                }
                catch { }
            }
            if (dropUninstallKey) Del(UNINSTALL_KEY);
            Del(@"Software\FipArc");
        }

        static void RestoreIcons()
        {
            using (RegistryKey b = Registry.CurrentUser.OpenSubKey(BACKUP_KEY))
            {
                if (b == null) return;
                foreach (string sub in b.GetSubKeyNames())
                {
                    using (RegistryKey s = b.OpenSubKey(sub))
                    {
                        if (s == null) continue;
                        string progid = (string)s.GetValue("progid");
                        string icon = (string)s.GetValue("icon");
                        int existed = 0;
                        object ev = s.GetValue("existedHkcu");
                        if (ev is int) existed = (int)ev;
                        if (string.IsNullOrEmpty(progid)) continue;
                        try
                        {
                            // 原来的值是空的（或原本就没有这个键）：
                            // 把整个 DefaultIcon 子键删掉。只删值会留下一个空键，
                            // 空键同样会遮蔽 HKLM 里的系统图标，导致卸载后图标不还原。
                            if (existed == 0 || string.IsNullOrEmpty(icon))
                            {
                                try
                                {
                                    Registry.CurrentUser.DeleteSubKeyTree(
                                        @"Software\Classes\" + progid + @"\DefaultIcon", false);
                                }
                                catch { }
                                continue;
                            }
                            using (RegistryKey ic = Registry.CurrentUser.CreateSubKey(
                                @"Software\Classes\" + progid + @"\DefaultIcon"))
                            {
                                if (ic != null)
                                {
                                    if (string.IsNullOrEmpty(icon)) ic.DeleteValue("", false);
                                    else ic.SetValue("", icon);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        static void Del(string keyPath)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
            catch { }
        }

        // ---------------- 卸载登记 ----------------
        public static void RegisterUninstall(string exePath, string installDir, string version, long sizeKB)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UNINSTALL_KEY))
            {
                if (k == null) return;
                k.SetValue("DisplayName", "FipArc 压缩工具");
                k.SetValue("DisplayIcon", Q(exePath) + ",0");
                k.SetValue("DisplayVersion", version);
                k.SetValue("Publisher", "FipArc");
                k.SetValue("InstallLocation", installDir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)sizeKB, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                string uninst = Path.Combine(installDir, "Uninstall.exe");
                if (!File.Exists(uninst)) uninst = exePath;
                // 指向专门的 Uninstall.exe，不再让用户去点安装程序
                k.SetValue("UninstallString", Q(uninst));
                k.SetValue("QuietUninstallString", Q(uninst) + " /quiet");
            }
        }

        /// <summary>LAV Splitter 的 CLSID，用来判断内置解码器有没有注册</summary>
        public const string LAV_SPLITTER = "{171252A0-8820-4AFE-9DF8-5C92B2D66B04}";

        /// <summary>DirectShow 全能解码器（LAV）是否已注册</summary>
        public static bool LavInstalled()
        {
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + LAV_SPLITTER + @"\InprocServer32"))
                    if (k != null) return true;
            }
            catch { }
            return false;
        }

        /// <summary>通知资源管理器刷新图标缓存</summary>
        public static void RefreshShell()
        {
            try
            {
                NativeMethods.SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern void SHCallNotifyPlaceholder(int eventId, int flags, IntPtr item1, IntPtr item2);

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
    }
}
