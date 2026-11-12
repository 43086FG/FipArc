// ZipCrypto.cs —— 带密码的标准 ZIP
//
// 说明：.NET 自带的 ZipArchive 不支持加密，所以这里自己实现 ZIP 结构。
//
// 加密方式：传统 PKWARE ZipCrypto（ZIP 规范的 0x0001 标志位）
//   · 优点：Windows 资源管理器、macOS、7-Zip、WinRAR、Bandizip 全都能直接打开
//   · 缺点：强度弱（有已知明文攻击），不是用来对抗专业破解的
//   · 需要强加密请用 .fip 格式（AES-256 + PBKDF2）
//
// 只处理两种压缩方法：0 = 存储，8 = deflate（覆盖绝大多数 ZIP）
// AES 加密的 ZIP（method 99）不支持，会明确报错让用户用 7-Zip 打开。
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.IO.Compression;

namespace FipArc
{
    /// <summary>PKWARE 传统 ZIP 加密流密码</summary>
    public sealed class ZipCrypt
    {
        static readonly uint[] Table = BuildTable();
        static uint[] BuildTable()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        uint k0, k1, k2;

        public ZipCrypt(string password)
        {
            k0 = 0x12345678; k1 = 0x23456789; k2 = 0x34567890;
            byte[] pb = Encoding.UTF8.GetBytes(password == null ? "" : password);
            for (int i = 0; i < pb.Length; i++) UpdateKeys(pb[i]);
        }

        static uint Crc32Step(uint crc, byte b)
        {
            return (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        }

        void UpdateKeys(byte b)
        {
            k0 = Crc32Step(k0, b);
            k1 = k1 + (k0 & 0xFF);
            k1 = k1 * 134775813u + 1u;
            k2 = Crc32Step(k2, (byte)(k1 >> 24));
        }

        byte DecryptByte()
        {
            uint temp = (k2 | 2u) & 0xFFFFu;
            return (byte)(((temp * (temp ^ 1u)) >> 8) & 0xFF);
        }

        /// <summary>就地解密</summary>
        public void Decrypt(byte[] buf, int off, int len)
        {
            for (int i = 0; i < len; i++)
            {
                byte c = buf[off + i];
                byte p = (byte)(c ^ DecryptByte());
                UpdateKeys(p);
                buf[off + i] = p;
            }
        }

        /// <summary>就地加密</summary>
        public void Encrypt(byte[] buf, int off, int len)
        {
            for (int i = 0; i < len; i++)
            {
                byte p = buf[off + i];
                buf[off + i] = (byte)(p ^ DecryptByte());
                UpdateKeys(p);
            }
        }
    }

    /// <summary>ZIP 结构读写（含加密）</summary>
    public static class ZipPacked
    {
        const uint SIG_LOCAL = 0x04034b50;
        const uint SIG_CENTRAL = 0x02014b50;
        const uint SIG_EOCD = 0x06054b50;

        // ---------------- 判断是否加密 ----------------
        public static bool IsEncrypted(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] h = new byte[8];
                    if (fs.Read(h, 0, 8) < 8) return false;
                    if (BitConverter.ToUInt32(h, 0) != SIG_LOCAL) return false;
                    ushort flags = BitConverter.ToUInt16(h, 6);
                    return (flags & 0x0001) != 0;
                }
            }
            catch { return false; }
        }

        // =====================================================================
        //  写入
        // =====================================================================
        sealed class Src
        {
            public string Full, Rel;
        }

        /// <summary>把若干文件/文件夹打包成加密 ZIP</summary>
        public static byte[] Create(string[] paths, string baseDir, string password)
        {
            List<Src> files = Collect(paths, baseDir);

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                List<long> offsets = new List<long>();
                List<Src> order = new List<Src>();
                List<uint> crcs = new List<uint>();
                List<int> comps = new List<int>();
                List<int> origs = new List<int>();
                List<ushort> methods = new List<ushort>();
                List<ushort> allFlags = new List<ushort>();
                bool useEnc = !string.IsNullOrEmpty(password);

                ushort dosTime, dosDate;
                ToDosTime(DateTime.Now, out dosTime, out dosDate);

                foreach (Src s in files)
                {
                    byte[] raw = File.ReadAllBytes(s.Full);
                    byte[] deflated = Deflate(raw);
                    bool useDeflate = deflated.Length < raw.Length;
                    byte[] payload = useDeflate ? deflated : raw;
                    ushort method = (ushort)(useDeflate ? 8 : 0);

                    uint crc = Crc32.Compute(raw);
                    byte[] nameBytes = Encoding.UTF8.GetBytes(s.Rel);

                    byte[] block;
                    ushort flags;
                    if (useEnc)
                    {
                        // 加密头 12 字节：前 11 随机，最后 1 字节 = CRC 高字节
                        byte[] enc = new byte[12];
                        new Random(Guid.NewGuid().GetHashCode()).NextBytes(enc);
                        enc[11] = (byte)(crc >> 24);

                        // 合并成一个缓冲区一起加密，保证密钥流连续
                        block = new byte[12 + payload.Length];
                        Buffer.BlockCopy(enc, 0, block, 0, 12);
                        Buffer.BlockCopy(payload, 0, block, 12, payload.Length);
                        new ZipCrypt(password).Encrypt(block, 0, block.Length);
                        flags = 0x0001;            // bit0 = 加密
                    }
                    else
                    {
                        block = payload;           // 不加密，直接放 deflate 数据
                        flags = 0x0000;
                    }

                    offsets.Add(ms.Position);
                    order.Add(s);
                    crcs.Add(crc);
                    comps.Add(block.Length);
                    origs.Add(raw.Length);
                    methods.Add(method);
                    allFlags.Add(flags);

                    // 本地文件头
                    bw.Write(SIG_LOCAL);
                    bw.Write((ushort)20);          // version needed
                    bw.Write(flags);
                    bw.Write(method);
                    bw.Write(dosTime);
                    bw.Write(dosDate);
                    bw.Write(crc);
                    bw.Write((uint)block.Length);  // 加密时含 12 字节头
                    bw.Write((uint)raw.Length);
                    bw.Write((ushort)nameBytes.Length);
                    bw.Write((ushort)0);           // extra len
                    bw.Write(nameBytes);
                    bw.Write(block);
                }

                long cdStart = ms.Position;
                for (int i = 0; i < order.Count; i++)
                {
                    byte[] nameBytes = Encoding.UTF8.GetBytes(order[i].Rel);
                    bw.Write(SIG_CENTRAL);
                    bw.Write((ushort)20);          // version made by
                    bw.Write((ushort)20);          // version needed
                    bw.Write(allFlags[i]);        // flags
                    bw.Write((ushort)(methods[i]));  // method（每个文件各自记录）
                    bw.Write(dosTime);
                    bw.Write(dosDate);
                    bw.Write(crcs[i]);
                    bw.Write((uint)comps[i]);
                    bw.Write((uint)origs[i]);
                    bw.Write((ushort)nameBytes.Length);
                    bw.Write((ushort)0);           // extra
                    bw.Write((ushort)0);           // comment
                    bw.Write((ushort)0);           // disk
                    bw.Write((ushort)0);           // internal attrs
                    bw.Write((uint)0);             // external attrs
                    bw.Write((uint)offsets[i]);
                    bw.Write(nameBytes);
                }
                long cdSize = ms.Position - cdStart;

                bw.Write(SIG_EOCD);
                bw.Write((ushort)0);
                bw.Write((ushort)0);
                bw.Write((ushort)order.Count);
                bw.Write((ushort)order.Count);
                bw.Write((uint)cdSize);
                bw.Write((uint)cdStart);
                bw.Write((ushort)0);
                bw.Flush();
                return ms.ToArray();
            }
        }

        static List<Src> Collect(string[] paths, string baseDir)
        {
            List<Src> list = new List<Src>();
            foreach (string p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (string f in Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                        list.Add(new Src { Full = f, Rel = MakeRel(f, baseDir) });
                }
                else if (File.Exists(p))
                {
                    list.Add(new Src { Full = p, Rel = MakeRel(p, baseDir) });
                }
            }
            return list;
        }

        static string MakeRel(string full, string baseDir)
        {
            string f = Path.GetFullPath(full).Replace('\\', '/');
            if (string.IsNullOrEmpty(baseDir)) return Path.GetFileName(f);
            string b = Path.GetFullPath(baseDir).Replace('\\', '/').TrimEnd('/') + "/";
            if (f.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return f.Substring(b.Length);
            return Path.GetFileName(f);
        }

        static void ToDosTime(DateTime t, out ushort dosTime, out ushort dosDate)
        {
            dosTime = (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
            dosDate = (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
        }

        static byte[] Deflate(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    ds.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        static byte[] Inflate(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream(data))
            using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (MemoryStream outMs = new MemoryStream())
            {
                byte[] buf = new byte[65536];
                int n;
                while ((n = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
                return outMs.ToArray();
            }
        }

        // =====================================================================
        //  读取（走中央目录，最可靠）
        // =====================================================================
        public sealed class Item
        {
            public string Name;
            public ushort Flags;
            public ushort Method;
            public uint Crc;
            public long PackedSize;
            public long Size;
            public long LocalOffset;
        }

        public static List<Item> ReadDirectory(string path)
        {
            return ReadDirectory(File.ReadAllBytes(path));
        }

        /// <summary>从内存里的 ZIP 数据读目录（FIP 内部的 zip 也走这里）</summary>
        public static List<Item> ReadDirectory(byte[] all)
        {
            List<Item> items = new List<Item>();

            // 从尾部往前找 EOCD
            int eocd = -1;
            int stop = Math.Max(0, all.Length - 65557 - 22);
            for (int i = all.Length - 22; i >= stop; i--)
            {
                if (BitConverter.ToUInt32(all, i) == SIG_EOCD) { eocd = i; break; }
            }
            if (eocd < 0) throw new InvalidDataException("ZIP 文件损坏：找不到目录结尾");

            int count = BitConverter.ToUInt16(all, eocd + 10);
            uint cdOff = BitConverter.ToUInt32(all, eocd + 16);
            int p = (int)cdOff;

            for (int n = 0; n < count; n++)
            {
                if (p + 46 > all.Length) break;
                if (BitConverter.ToUInt32(all, p) != SIG_CENTRAL) break;
                Item it = new Item();
                it.Flags = BitConverter.ToUInt16(all, p + 8);
                it.Method = BitConverter.ToUInt16(all, p + 10);
                it.Crc = BitConverter.ToUInt32(all, p + 16);
                it.PackedSize = BitConverter.ToUInt32(all, p + 20);
                it.Size = BitConverter.ToUInt32(all, p + 24);
                int nameLen = BitConverter.ToUInt16(all, p + 28);
                int extraLen = BitConverter.ToUInt16(all, p + 30);
                int commentLen = BitConverter.ToUInt16(all, p + 32);
                it.LocalOffset = BitConverter.ToUInt32(all, p + 42);
                it.Name = Encoding.UTF8.GetString(all, p + 46, nameLen);
                items.Add(it);
                p += 46 + nameLen + extraLen + commentLen;
            }
            return items;
        }

        public static byte[] ReadEntry(string path, Item it, string password)
        {
            return ReadEntry(File.ReadAllBytes(path), it, password, path);
        }

        /// <summary>从内存里的 ZIP 数据读出单个条目</summary>
        public static byte[] ReadEntry(byte[] all, Item it, string password, string displayPath)
        {
            if (it.Method == 99)
                throw new NotSupportedException(
                    "这个 ZIP 用了 AES 加密（method 99），本软件暂不支持。\n请用 7-Zip 或 WinRAR 打开。");

            int lo = (int)it.LocalOffset;
            if (lo + 30 > all.Length) throw new InvalidDataException("ZIP 损坏：本地文件头越界");
            if (BitConverter.ToUInt32(all, lo) != SIG_LOCAL) throw new InvalidDataException("ZIP 损坏：本地文件头签名错误");
            int lnLen = BitConverter.ToUInt16(all, lo + 26);
            int leLen = BitConverter.ToUInt16(all, lo + 28);
            int dataStart = lo + 30 + lnLen + leLen;

            bool enc = (it.Flags & 0x0001) != 0;
            if (enc && string.IsNullOrEmpty(password))
                throw new PasswordRequiredException(displayPath);

            int packLen = (int)it.PackedSize;
            if (dataStart + packLen > all.Length)
                packLen = all.Length - dataStart;
            byte[] data = new byte[packLen];
            Buffer.BlockCopy(all, dataStart, data, 0, packLen);

            if (enc)
            {
                if (data.Length < 12) throw new InvalidDataException("加密数据不完整");
                new ZipCrypt(password).Decrypt(data, 0, data.Length);
                // 校验 12 字节头的最后一字节 = CRC 高字节
                if ((it.Flags & 0x0008) == 0)
                {
                    if (data[11] != (byte)(it.Crc >> 24)) throw new WrongPasswordException();
                }
                byte[] body = new byte[data.Length - 12];
                Buffer.BlockCopy(data, 12, body, 0, body.Length);
                data = body;
            }

            byte[] raw;
            if (it.Method == 8) raw = Inflate(data);
            else if (it.Method == 0) raw = data;
            else throw new NotSupportedException("不支持的 ZIP 压缩方法: " + it.Method);

            if (raw.Length != it.Size) throw new WrongPasswordException();
            if (Crc32.Compute(raw) != it.Crc) throw new WrongPasswordException();
            return raw;
        }
    }

    /// <summary>加密 ZIP 的解压器</summary>
    public sealed class EncryptedZipExtractor : IExtractor
    {
        readonly string path;
        string password;

        public EncryptedZipExtractor(string p) { path = p; }
        public void SetPassword(string pwd) { password = pwd; }

        public List<EntryInfo> List()
        {
            List<EntryInfo> list = new List<EntryInfo>();
            foreach (ZipPacked.Item it in ZipPacked.ReadDirectory(path))
            {
                EntryInfo e = new EntryInfo();
                e.Name = it.Name;
                e.Size = it.Size;
                e.PackedSize = it.PackedSize;
                e.IsDir = it.Name.EndsWith("/");
                list.Add(e);
            }
            return list;
        }


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
                File.WriteAllBytes(target, ZipPacked.ReadEntry(path, it, password));
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
                byte[] data = ZipPacked.ReadEntry(path, it, password);
                string target = ZipExtractor.SafeJoin(destDir, it.Name);
                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(target, data);
                return target;
            }
            return null;
        }
        public void ExtractAll(string destDir, Action<int, string> progress)
        {
            Directory.CreateDirectory(destDir);
            List<ZipPacked.Item> items = ZipPacked.ReadDirectory(path);
            int n = 0;
            foreach (ZipPacked.Item it in items)
            {
                string target = ZipExtractor.SafeJoin(destDir, it.Name);
                if (it.Name.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, ZipPacked.ReadEntry(path, it, password));
                n++;
                if (progress != null) progress(n, it.Name);
            }
        }
    }
}
