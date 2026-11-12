// ZipTest.cs —— 加密 ZIP 的自测
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using FipArc;

static class ZipTest
{
    static int pass = 0, fail = 0;
    static void Ok(bool c, string m)
    {
        if (c) { pass++; Console.WriteLine("  [OK]   " + m); }
        else { fail++; Console.WriteLine("  [FAIL] " + m); }
    }

    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        string work = Path.Combine(Path.GetTempPath(), "ZipTest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(work);

        try
        {
            string src = Path.Combine(work, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "这是机密内容 secret content 1234567890", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(src, "b.txt"), new string('B', 5000));
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "sub", "c.txt"), "深层文件 deep file");

            string pwd = "Test1234";

            Console.WriteLine("=== 生成加密 ZIP ===");
            byte[] zip = ZipPacked.Create(new string[] { src }, work, pwd);
            string zp = Path.Combine(work, "enc.zip");
            File.WriteAllBytes(zp, zip);
            Ok(zip.Length > 0, "生成 " + zip.Length + " 字节");
            Ok(zip[0] == 0x50 && zip[1] == 0x4B, "是标准 ZIP（PK 魔数）");
            Ok(ZipPacked.IsEncrypted(zp), "IsEncrypted 识别为加密");

            Console.WriteLine();
            Console.WriteLine("=== 目录与密码校验 ===");
            List<ZipPacked.Item> items = ZipPacked.ReadDirectory(zp);
            Ok(items.Count == 3, "中央目录 3 项（实际 " + items.Count + "）");

            bool needPwd = false;
            try { EncryptedZipExtractor ex0 = new EncryptedZipExtractor(zp); ex0.ExtractAll(Path.Combine(work, "x0"), null); }
            catch (PasswordRequiredException) { needPwd = true; }
            Ok(needPwd, "不给密码抛 PasswordRequiredException");

            bool wrong = false;
            try
            {
                EncryptedZipExtractor ex1 = new EncryptedZipExtractor(zp);
                ex1.SetPassword("错误的密码");
                ex1.ExtractAll(Path.Combine(work, "x1"), null);
            }
            catch (WrongPasswordException) { wrong = true; }
            catch (Exception) { wrong = true; }
            Ok(wrong, "错密码被拒绝");

            Console.WriteLine();
            Console.WriteLine("=== 正确密码解压 ===");
            EncryptedZipExtractor ex = new EncryptedZipExtractor(zp);
            ex.SetPassword(pwd);
            string outDir = Path.Combine(work, "out");
            ex.ExtractAll(outDir, null);
            Ok(File.Exists(Path.Combine(outDir, "src", "a.txt")), "解出 a.txt");
            Ok(File.Exists(Path.Combine(outDir, "src", "sub", "c.txt")), "解出 sub/c.txt");
            if (File.Exists(Path.Combine(outDir, "src", "a.txt")))
            {
                string got = File.ReadAllText(Path.Combine(outDir, "src", "a.txt"), new UTF8Encoding(false));
                Ok(got == "这是机密内容 secret content 1234567890", "内容逐字一致");
            }
            if (File.Exists(Path.Combine(outDir, "src", "b.txt")))
                Ok(new FileInfo(Path.Combine(outDir, "src", "b.txt")).Length == 5000,
                   "大文件长度一致（5000，压缩后应显著变小）");

            Console.WriteLine();
            Console.WriteLine("=== 明文不以原文形式出现在文件里检查 ===");
            string raw = Encoding.UTF8.GetString(zip);
            Ok(raw.IndexOf("这是机密内容") < 0, "明文未泄露（已加密）");

            Console.WriteLine("    （把 ZIP 路径写到 out.txt 供外部工具验证）");
            File.WriteAllText(Path.Combine(work, "path.txt"), zp + "\r\n" + outDir);

            Console.WriteLine();
            Console.WriteLine("通过 " + pass + " 项，失败 " + fail + " 项");
            Console.WriteLine("测试目录: " + work);
            if (Environment.GetEnvironmentVariable("ZIP_KEEP") == null)
            { }
            else Console.WriteLine("（保留）");
            return fail == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  [异常] " + ex);
            return 1;
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("ZIP_KEEP") == null)
            { try { Directory.Delete(work, true); } catch { } }
        }
    }
}
