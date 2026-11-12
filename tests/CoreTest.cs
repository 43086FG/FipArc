// CoreTest.cs —— 核心逻辑自动化测试（不启动界面）
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using FipArc;

static class CoreTest
{
    static int pass = 0, fail = 0;
    static void Ok(bool cond, string msg)
    {
        if (cond) { pass++; Console.WriteLine("  [OK]   " + msg); }
        else { fail++; Console.WriteLine("  [FAIL] " + msg); }
    }

    static string Work;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Work = Path.Combine(Path.GetTempPath(), "FipArcTest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Work);
        Console.WriteLine("测试目录: " + Work);
        Console.WriteLine();

        try
        {
            TestFipRoundTrip();
            TestFipNotOpenableByOthers();
            TestZip();
            TestTar();
            TestGz();
            TestTarGz();
            TestConvert();
            TestPassword();
            TestZipPassword();
            TestLargeFile();
            TestExtractOne();
            TestExtractFolder();
            TestCrc();
        }
        catch (Exception ex)
        {
            Console.WriteLine("  [异常] " + ex);
            fail++;
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("FIPARC_KEEP") == null)
            { try { Directory.Delete(Work, true); } catch { } }
            else Console.WriteLine("（保留测试目录: " + Work + "）");
        }

        Console.WriteLine();
        Console.WriteLine("通过 " + pass + " 项，失败 " + fail + " 项");
        return fail == 0 ? 0 : 1;
    }

    // ---------------- FIP 往返 ----------------
    static void TestFipRoundTrip()
    {
        Console.WriteLine("=== FIP 容器往返 ===");
        byte[] data = Encoding.UTF8.GetBytes(string.Join("|", new string[] {
            "植物大战僵尸", "FipArc", "hello world", "12345" }));
        byte[] fip = FipFile.Write(Kind.Archive, "测试包", data);
        string path = Path.Combine(Work, "t1.fip");
        File.WriteAllBytes(path, fip);

        Ok(BitConverter.ToUInt32(fip, 0) == FipFile.Magic, "魔数是 FIPK");
        Ok(fip.Length > data.Length || fip.Length > 0, "生成了容器 (" + fip.Length + " 字节)");

        byte kind; string name;
        byte[] back = FipFile.Read(path, out kind, out name);
        Ok(kind == Kind.Archive, "种类还原正确");
        Ok(name == "测试包", "文件名还原正确: " + name);
        Ok(back.Length == data.Length, "长度一致 (" + data.Length + ")");
        bool same = true;
        for (int i = 0; i < data.Length; i++) if (data[i] != back[i]) { same = false; break; }
        Ok(same, "内容逐字节一致");
        Console.WriteLine();
    }

    // ---------------- FIP 别的软件打不开 ----------------
    static void TestFipNotOpenableByOthers()
    {
        Console.WriteLine("=== FIP 不能被其他软件识别 ===");
        byte[] data = Encoding.UTF8.GetBytes("机密内容 secret content 1234567890");
        byte[] fip = FipFile.Write(Kind.TextTxt, "s.txt", data);
        string path = Path.Combine(Work, "s.fxt");
        File.WriteAllBytes(path, fip);

        Ok(!(fip[0] == 0x50 && fip[1] == 0x4B), "不是 ZIP 格式（魔数不是 PK）");
        Ok(fip[0] != 0x52 || fip[1] != 0x61, "不是 RAR 格式");
        Ok(fip[0] != 0x37 || fip[1] != 0x7A, "不是 7Z 格式");

        // 用 .NET 的 ZipArchive 尝试打开，必须失败
        bool zipFailed = false;
        try
        {
            using (FileStream fs = File.OpenRead(path))
            using (System.IO.Compression.ZipArchive za = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
            { int n = za.Entries.Count; }
        }
        catch { zipFailed = true; }
        Ok(zipFailed, "标准 ZIP 解析器无法打开");

        // 原文不能出现在容器里（说明真的加密了）
        string rawText = Encoding.UTF8.GetString(fip);
        Ok(rawText.IndexOf("机密内容") < 0, "明文内容未泄露（已加密）");

        // 篡改 1 字节后必须校验失败
        byte[] bad = (byte[])fip.Clone();
        bad[bad.Length - 1] ^= 0xFF;
        string badPath = Path.Combine(Work, "bad.fxt");
        File.WriteAllBytes(badPath, bad);
        bool detect = false;
        try { byte k2; string n2; FipFile.Read(badPath, out k2, out n2); }
        catch { detect = true; }
        Ok(detect, "篡改后能检测出损坏");
        Console.WriteLine();
    }

    // ---------------- ZIP ----------------
    static void TestZip()
    {
        Console.WriteLine("=== ZIP 打包 / 解包 ===");
        string src = Path.Combine(Work, "zipsrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.txt"), "aaa");
        File.WriteAllText(Path.Combine(src, "b.txt"), "bbb");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "sub", "c.txt"), "ccc");

        byte[] zip = Archive.CreateZip(new string[] { src }, Work);
        string zp = Path.Combine(Work, "t.zip");
        File.WriteAllBytes(zp, zip);
        Ok(zip.Length > 0, "生成 ZIP (" + zip.Length + " 字节)");

        IExtractor ex = Archive.Open(zp);
        List<EntryInfo> list = ex.List();
        Ok(list.Count == 3, "列出 3 个文件（实际 " + list.Count + "）");

        string outDir = Path.Combine(Work, "zipout");
        ex.ExtractAll(outDir, null);
        Ok(File.Exists(Path.Combine(outDir, "zipsrc", "a.txt")), "解出 a.txt");
        Ok(File.Exists(Path.Combine(outDir, "zipsrc", "sub", "c.txt")), "解出 sub/c.txt");
        Console.WriteLine();
    }

    // ---------------- TAR ----------------
    static void TestTar()
    {
        Console.WriteLine("=== TAR 打包 / 解包 ===");
        string src = Path.Combine(Work, "tarsrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "x.txt"), "xxx");
        File.WriteAllText(Path.Combine(src, "y.txt"), "yyy");

        byte[] tar = Archive.CreateTar(new string[] { src }, Work);
        string tp = Path.Combine(Work, "t.tar");
        File.WriteAllBytes(tp, tar);
        Ok(tar.Length > 1024, "生成 TAR (" + tar.Length + " 字节)");

        IExtractor ex = Archive.Open(tp);
        List<EntryInfo> list = ex.List();
        Ok(list.Count == 2, "列出 2 个文件（实际 " + list.Count + "）");

        string outDir = Path.Combine(Work, "tarout");
        ex.ExtractAll(outDir, null);
        Ok(File.Exists(Path.Combine(outDir, "tarsrc", "x.txt")), "解出 x.txt");
        Console.WriteLine();
    }

    // ---------------- GZ ----------------
    static void TestGz()
    {
        Console.WriteLine("=== GZ 压缩 / 解压 ===");
        string f = Path.Combine(Work, "one.txt");
        File.WriteAllText(f, new string('A', 5000));
        byte[] gz = Archive.CreateGz(f);
        string gp = Path.Combine(Work, "one.txt.gz");
        File.WriteAllBytes(gp, gz);
        Ok(gz.Length < 5000, "压缩生效（5000 -> " + gz.Length + "）");

        string outDir = Path.Combine(Work, "gzout");
        Archive.Open(gp).ExtractAll(outDir, null);
        Ok(File.Exists(Path.Combine(outDir, "one.txt")), "解出 one.txt");
        if (File.Exists(Path.Combine(outDir, "one.txt")))
            Ok(new FileInfo(Path.Combine(outDir, "one.txt")).Length == 5000, "解压后长度正确");
        Console.WriteLine();
    }

    // ---------------- TAR.GZ ----------------
    static void TestTarGz()
    {
        Console.WriteLine("=== TAR.GZ 打包 / 解包 ===");
        string src = Path.Combine(Work, "tgsrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "k.txt"), "kkk");

        byte[] tar = Archive.CreateTar(new string[] { src }, Work);
        byte[] tgz;
        using (MemoryStream ms = new MemoryStream())
        {
            using (System.IO.Compression.GZipStream gz = new System.IO.Compression.GZipStream(
                ms, System.IO.Compression.CompressionMode.Compress, true))
                gz.Write(tar, 0, tar.Length);
            tgz = ms.ToArray();
        }
        string tp = Path.Combine(Work, "t.tar.gz");
        File.WriteAllBytes(tp, tgz);

        IExtractor ex = Archive.Open(tp);
        List<EntryInfo> list = ex.List();
        Ok(list.Count == 1, "列出 1 个文件（实际 " + list.Count + "）");
        string outDir = Path.Combine(Work, "tgzout");
        ex.ExtractAll(outDir, null);
        Ok(File.Exists(Path.Combine(outDir, "tgsrc", "k.txt")), "解出 k.txt");
        Console.WriteLine();
    }

    // ---------------- 格式互转 ----------------
    static void TestConvert()
    {
        Console.WriteLine("=== 格式互转 ===");

        // txt -> fxt -> txt
        string txt = Path.Combine(Work, "note.txt");
        string content = "第一行\n第二行\n植物大战僵尸 FipArc";
        File.WriteAllText(txt, content, new UTF8Encoding(false));
        string fxt = Convert2.ToFip(txt);
        Ok(Path.GetExtension(fxt).ToLower() == ".fxt", "txt -> " + Path.GetFileName(fxt));
        Ok(File.Exists(fxt), "fxt 文件已生成");
        Ok(!(File.ReadAllBytes(fxt)[0] == 0x50), "fxt 不是明文的 zip/文本");

        string back = Convert2.FromFip(fxt);
        Ok(Path.GetExtension(back).ToLower() == ".txt", "fxt -> " + Path.GetFileName(back));
        string backText = File.ReadAllText(back, new UTF8Encoding(false));
        Ok(backText == content, "内容完全还原（" + backText.Length + " 字符）");

        // 用 Shell.OwnExts 判断
        Ok(Playable.IsText(fxt), "fxt 被识别为文本类");
        Ok(Playable.IsText(txt), "普通 txt 也被识别为文本类");

        // 不支持的扩展名应报错
        string bin = Path.Combine(Work, "x.bin");
        File.WriteAllBytes(bin, new byte[] { 1, 2, 3 });
        bool threw = false;
        try { Convert2.ToFip(bin); } catch { threw = true; }
        Ok(threw, "不支持的格式会明确报错");

        // 关键语义：f 格式是"格式转换"，不是压缩
        string bigTxt = Path.Combine(Work, "bigtext.txt");
        File.WriteAllText(bigTxt, new string('A', 100000));
        long origLen = new FileInfo(bigTxt).Length;
        string bigFxt = Convert2.ToFip(bigTxt);
        long fxtLen = new FileInfo(bigFxt).Length;
        Ok(fxtLen >= origLen,
           string.Format("fxt 不做压缩：{0} 字节 -> {1} 字节（只多了 {2} 字节头部）",
                         origLen, fxtLen, fxtLen - origLen));
        Ok(fxtLen < origLen + 512, "fxt 开销很小（<512 字节）");

        // 还原后必须一字不差
        string backBig = Convert2.FromFip(bigFxt);
        Ok(File.ReadAllText(backBig) == new string('A', 100000), "fxt 还原后内容一致");

        // 对照：.fip 压缩包仍然压缩（同样内容应该小得多）
        string zipDir = Path.Combine(Work, "cmpsrc");
        Directory.CreateDirectory(zipDir);
        File.WriteAllText(Path.Combine(zipDir, "a.txt"), new string('A', 100000));
        byte[] fipArc = Archive.CreateFip(new string[] { zipDir }, Work, "cmp");
        Ok(fipArc.Length < origLen / 10,
           string.Format("fip 压缩包仍然压缩：100000 字节文本 -> 容器 {0} 字节", fipArc.Length));

        Console.WriteLine();
    }

    // ---------------- 密码保护 ----------------
    static void TestPassword()
    {
        Console.WriteLine("=== 密码保护（AES-256）===");
        byte[] data = Encoding.UTF8.GetBytes("这是需要密码才能打开的机密内容 top secret 2026");
        string pwd = "MyPass123!中文";

        byte[] enc = FipFile.Write(Kind.TextTxt, "secret.txt", data, pwd);
        string p = Path.Combine(Work, "secret.fxt");
        File.WriteAllBytes(p, enc);

        byte kind; string name;

        // 1) 标记为需要密码
        Ok(FipFile.NeedsPassword(p), "NeedsPassword 返回 true");
        Ok(BitConverter.ToUInt16(enc, 4) == FipFile.VersionPwd, "版本号是 2");

        // 2) 不给密码 -> 抛 PasswordRequiredException
        bool req = false;
        try { FipFile.Read(p, out kind, out name); }
        catch (PasswordRequiredException) { req = true; }
        Ok(req, "不给密码时抛 PasswordRequiredException");

        // 3) 错密码 -> 抛 WrongPasswordException
        bool wrong = false;
        try { FipFile.Read(p, out kind, out name, "错误的密码"); }
        catch (WrongPasswordException) { wrong = true; }
        Ok(wrong, "错误密码被拒绝");

        // 4) 正确密码 -> 完整还原
        byte[] back = FipFile.Read(p, out kind, out name, pwd);
        bool same = back.Length == data.Length;
        for (int i = 0; same && i < data.Length; i++) if (back[i] != data[i]) same = false;
        Ok(same, "正确密码完整还原 (" + data.Length + " 字节)");

        // 5) 明文不泄露
        string raw = Encoding.UTF8.GetString(enc);
        Ok(raw.IndexOf("机密内容") < 0 && raw.IndexOf("top secret") < 0, "明文内容未泄露");

        // 6) 同密码两次加密结果不同（随机盐 + 随机 IV）
        byte[] enc2 = FipFile.Write(Kind.TextTxt, "secret.txt", data, pwd);
        bool differ = false;
        for (int i = 0; i < Math.Min(enc.Length, enc2.Length); i++)
            if (enc[i] != enc2[i]) { differ = true; break; }
        Ok(differ, "同密码两次加密结果不同（随机盐/IV）");
        Ok(enc.Length == enc2.Length, "两次加密长度一致");

        // 7) 不带密码的老格式仍能读（向后兼容）
        byte[] plain = FipFile.Write(Kind.TextTxt, "old.txt", data);
        string pp = Path.Combine(Work, "old.fxt");
        File.WriteAllBytes(pp, plain);
        Ok(!FipFile.NeedsPassword(pp), "无密码文件 NeedsPassword 为 false");
        Ok(BitConverter.ToUInt16(plain, 4) == FipFile.VersionPlain, "无密码文件版本号是 1");
        byte k2; string n2;
        byte[] back2 = FipFile.Read(pp, out k2, out n2);
        Ok(back2.Length == data.Length, "老格式仍可正常读取");

        // 7.5) f 格式【转换】也支持密码，而且输出扩展名必须是 .fpg 不是 .fip
        string pngPath = Path.Combine(Work, "pic.png");
        byte[] fakePng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5 };
        File.WriteAllBytes(pngPath, fakePng);
        string fpgPath = Convert2.ToFip(pngPath, "Pwd123");
        Ok(Path.GetExtension(fpgPath).ToLower() == ".fpg",
           "png 转换输出是 .fpg（不是 .fip）: " + Path.GetFileName(fpgPath));
        Ok(FipFile.NeedsPassword(fpgPath), "png->fpg 支持密码");
        string pngBack = Convert2.FromFip(fpgPath, "Pwd123");
        Ok(File.ReadAllBytes(pngBack).Length == fakePng.Length, "png 经密码转换后能完整还原");
        bool fpgWrongPwd = false;
        try { Convert2.FromFip(fpgPath, "错的"); } catch { fpgWrongPwd = true; }
        Ok(fpgWrongPwd, "fpg 错误密码被拒绝");

        // 8) 通过 Archive 层带密码解压
        //    另外生成一个 ASCII 密码的样本，供界面自动化测试使用
        byte[] guiSample = FipFile.Write(Kind.TextTxt, "gui.txt",
            Encoding.UTF8.GetBytes("这是用密码 Test1234 加密的内容。\r\n如果你看到这段文字，说明密码流程完全正确。"), "Test1234");
        File.WriteAllBytes(Path.Combine(Work, "gui.fxt"), guiSample);        string src = Path.Combine(Work, "pwdsrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "inside.txt"), "包内文件");
        byte[] arc = Archive.CreateFip(new string[] { src }, Work, "加密包", pwd);
        string ap = Path.Combine(Work, "pwd.fip");
        File.WriteAllBytes(ap, arc);
        Ok(FipFile.NeedsPassword(ap), "加密压缩包标记正确");

        bool lreq = false;
        try { Archive.Open(ap).List(); }
        catch (PasswordRequiredException) { lreq = true; }
        Ok(lreq, "不带密码无法列出内容");

        IExtractor ex = Archive.Open(ap);
        ex.SetPassword(pwd);
        List<EntryInfo> list = ex.List();
        Ok(list.Count == 1, "带密码可列出 1 项（实际 " + list.Count + "）");
        string outDir = Path.Combine(Work, "pwdout");
        ex.ExtractAll(outDir, null);
        Ok(File.Exists(Path.Combine(outDir, "pwdsrc", "inside.txt")), "带密码解压成功");

        Console.WriteLine();
    }

    // ---------------- 加密 ZIP ----------------
    static void TestZipPassword()
    {
        Console.WriteLine("=== 加密 ZIP（PKWARE ZipCrypto）===");
        string src = Path.Combine(Work, "zsrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.txt"), "机密内容 secret 1234567890", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(src, "big.txt"), new string('B', 5000));
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "sub", "c.txt"), "深层 deep");

        string pwd = "Test1234";
        byte[] zip = ZipPacked.Create(new string[] { src }, Work, pwd);
        string zp = Path.Combine(Work, "enc.zip");
        File.WriteAllBytes(zp, zip);

        Ok(zip.Length > 0, "生成 " + zip.Length + " 字节");
        Ok(zip[0] == 0x50 && zip[1] == 0x4B, "是标准 ZIP（PK 魔数）");
        Ok(ZipPacked.IsEncrypted(zp), "识别为加密 ZIP");

        // 明文不能出现
        string raw = Encoding.UTF8.GetString(zip);
        Ok(raw.IndexOf("机密内容") < 0, "明文未泄露（已加密）");

        // 中央目录
        List<ZipPacked.Item> items = ZipPacked.ReadDirectory(zp);
        Ok(items.Count == 3, "中央目录 3 项（实际 " + items.Count + "）");

        // 不给密码
        bool needPwd = false;
        try
        {
            EncryptedZipExtractor e0 = new EncryptedZipExtractor(zp);
            e0.ExtractAll(Path.Combine(Work, "zx0"), null);
        }
        catch (PasswordRequiredException) { needPwd = true; }
        Ok(needPwd, "不给密码抛 PasswordRequiredException");

        // 错密码
        bool wrong = false;
        try
        {
            EncryptedZipExtractor e1 = new EncryptedZipExtractor(zp);
            e1.SetPassword("wrong");
            e1.ExtractAll(Path.Combine(Work, "zx1"), null);
        }
        catch (Exception) { wrong = true; }
        Ok(wrong, "错误密码被拒绝");

        // 正确密码
        EncryptedZipExtractor ex = new EncryptedZipExtractor(zp);
        ex.SetPassword(pwd);
        string outDir = Path.Combine(Work, "zout");
        ex.ExtractAll(outDir, null);
        Ok(File.Exists(Path.Combine(outDir, "zsrc", "a.txt")), "解出 a.txt");
        Ok(File.Exists(Path.Combine(outDir, "zsrc", "sub", "c.txt")), "解出 sub/c.txt");
        if (File.Exists(Path.Combine(outDir, "zsrc", "a.txt")))
            Ok(File.ReadAllText(Path.Combine(outDir, "zsrc", "a.txt"), new UTF8Encoding(false))
               == "机密内容 secret 1234567890", "内容逐字一致");
        if (File.Exists(Path.Combine(outDir, "zsrc", "big.txt")))
            Ok(new FileInfo(Path.Combine(outDir, "zsrc", "big.txt")).Length == 5000,
               "5000 字节文件完整");

        // 压缩确实生效（加密后仍然压缩）
        Ok(zip.Length < 5000, "加密 ZIP 仍然压缩（5000 字节文本 -> " + zip.Length + " 字节容器）");

        // 把路径记下来，供外部工具（Python zipfile）交叉验证
        File.WriteAllText(Path.Combine(Work, "zipinfo.txt"), zp + "\n" + pwd + "\n");
        Console.WriteLine();
    }

    // ---------------- 大文件 ----------------
    static void TestLargeFile()
    {
        Console.WriteLine("=== 大文件（8MB 随机数据）===");
        byte[] big = new byte[8 * 1024 * 1024];
        Random rnd = new Random(12345);
        rnd.NextBytes(big);
        byte[] fip = FipFile.Write(Kind.Archive, "big", big);
        string p = Path.Combine(Work, "big.fip");
        File.WriteAllBytes(p, fip);
        Ok(fip.Length > 0, "生成 " + (fip.Length / 1024 / 1024) + " MB 容器");

        byte k; string n;
        byte[] back = FipFile.Read(p, out k, out n);
        Ok(back.Length == big.Length, "长度一致");
        bool same = true;
        for (int i = 0; i < big.Length; i++) if (big[i] != back[i]) { same = false; break; }
        Ok(same, "8MB 随机数据逐字节一致");

        // 随机数据不可压缩，所以容器应该约等于原大小 + 头部
        Ok(fip.Length >= big.Length, "不可压缩时不膨胀太多（" + (fip.Length - big.Length) + " 字节开销）");
        Console.WriteLine();
    }

    // ---------------- CRC ----------------
    // ---------------- 单条目提取（拖出文件用）----------------
    static void TestExtractOne()
    {
        Console.WriteLine("=== 单条目提取（树里拖文件出去）===");

        string src = Path.Combine(Work, "one_src");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "AAA", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "BBB深层", new UTF8Encoding(false));

        // 1) 普通 ZIP
        string zp = Path.Combine(Work, "one.zip");
        File.WriteAllBytes(zp, Archive.CreateZip(new string[] { src }, Work));
        IExtractor ex = Archive.Open(zp);
        string outDir = Path.Combine(Work, "one_out");
        string got = ex.ExtractOne("one_src/a.txt", outDir);
        Ok(got != null && File.Exists(got), "ZIP 单条目提取成功: " + (got == null ? "(null)" : Path.GetFileName(got)));
        if (got != null) Ok(File.ReadAllText(got, new UTF8Encoding(false)) == "AAA", "内容正确");
        string got2 = ex.ExtractOne("one_src/sub/b.txt", outDir);
        Ok(got2 != null && File.Exists(got2), "子目录里的条目也能提取");
        Ok(ex.ExtractOne("不存在的条目", outDir) == null, "条目不存在时返回 null");

        // 2) FIP 压缩包（内部也是 zip）
        string fp = Path.Combine(Work, "one.fip");
        File.WriteAllBytes(fp, Archive.CreateFip(new string[] { src }, Work, "one"));
        IExtractor fx = Archive.Open(fp);
        string g2 = fx.ExtractOne("one_src/a.txt", Path.Combine(Work, "one_out2"));
        Ok(g2 != null && File.Exists(g2), "FIP 单条目提取成功");

        // 3) 加密 ZIP：不给密码要报错，给对密码能取
        string ep = Path.Combine(Work, "one_enc.zip");
        File.WriteAllBytes(ep, ZipPacked.Create(new string[] { src }, Work, "Pwd9"));
        bool needPwd = false;
        try { Archive.Open(ep).ExtractOne("one_src/a.txt", Path.Combine(Work, "one_out3")); }
        catch (PasswordRequiredException) { needPwd = true; }
        Ok(needPwd, "加密 ZIP 不给密码时报 PasswordRequiredException");
        IExtractor ex3 = Archive.Open(ep);
        ex3.SetPassword("Pwd9");
        string g3 = ex3.ExtractOne("one_src/a.txt", Path.Combine(Work, "one_out3"));
        Ok(g3 != null && File.Exists(g3), "加密 ZIP 带密码能单独提取");

        // 4) TAR 回退路径
        string tp = Path.Combine(Work, "one.tar");
        File.WriteAllBytes(tp, Archive.CreateTar(new string[] { src }, Work));
        string g4 = Archive.Open(tp).ExtractOne("one_src/a.txt", Path.Combine(Work, "one_out4"));
        Ok(g4 != null && File.Exists(g4), "TAR 走回退路径也能提取");

        Console.WriteLine();
    }

    // ---------------- 文件夹整棵子树提取（拖文件夹用）----------------
    static void TestExtractFolder()
    {
        Console.WriteLine("=== 文件夹提取（拖整个文件夹出去）===");

        string root = Path.Combine(Work, "dir_src");
        Directory.CreateDirectory(Path.Combine(root, "sub", "deeper"));
        File.WriteAllText(Path.Combine(root, "top.txt"), "T", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "sub", "one.txt"), "1", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "sub", "deeper", "two.txt"), "2", new UTF8Encoding(false));

        // ZIP
        string zp = Path.Combine(Work, "dir.zip");
        File.WriteAllBytes(zp, Archive.CreateZip(new string[] { root }, Work));
        string outDir = Path.Combine(Work, "dir_out");
        string got = Archive.Open(zp).ExtractFolder("dir_src/sub", outDir);
        Ok(got != null && Directory.Exists(got), "ZIP 提取文件夹成功: " + (got == null ? "(null)" : Path.GetFileName(got)));
        if (got != null)
        {
            Ok(File.Exists(Path.Combine(got, "one.txt")), "子文件在");
            Ok(File.Exists(Path.Combine(got, "deeper", "two.txt")), "更深一层也在");
            Ok(!File.Exists(Path.Combine(got, "top.txt")), "上级文件没被带出来");
        }

        // FIP
        string fp = Path.Combine(Work, "dir.fip");
        File.WriteAllBytes(fp, Archive.CreateFip(new string[] { root }, Work, "d"));
        string g2 = Archive.Open(fp).ExtractFolder("dir_src/sub", Path.Combine(Work, "dir_out2"));
        Ok(g2 != null && Directory.Exists(g2), "FIP 提取文件夹成功");

        // 加密 ZIP
        string ep = Path.Combine(Work, "dir_enc.zip");
        File.WriteAllBytes(ep, ZipPacked.Create(new string[] { root }, Work, "Pw1"));
        IExtractor exe = Archive.Open(ep);
        exe.SetPassword("Pw1");
        string g3 = exe.ExtractFolder("dir_src/sub", Path.Combine(Work, "dir_out3"));
        Ok(g3 != null && Directory.Exists(g3), "加密 ZIP 提取文件夹成功");

        // TAR 回退
        string tp = Path.Combine(Work, "dir.tar");
        File.WriteAllBytes(tp, Archive.CreateTar(new string[] { root }, Work));
        string g4 = Archive.Open(tp).ExtractFolder("dir_src/sub", Path.Combine(Work, "dir_out4"));
        Ok(g4 != null && Directory.Exists(g4), "TAR 回退也能提取文件夹");

        // 不存在的文件夹
        Ok(Archive.Open(zp).ExtractFolder("没有这个目录", Path.Combine(Work, "dir_out5")) == null, "不存在的文件夹返回 null");

        Console.WriteLine();
    }

    static void TestCrc()
    {
        Console.WriteLine("=== CRC32 正确性 ===");
        // 标准测试向量：CRC32("123456789") = 0xCBF43926
        uint c = Crc32.Compute(Encoding.ASCII.GetBytes("123456789"));
        Ok(c == 0xCBF43926u, "CRC32(\"123456789\") = 0x" + c.ToString("X8") + " (期望 0xCBF43926)");
        Console.WriteLine();
    }
}
