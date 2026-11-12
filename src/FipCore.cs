// FipCore.cs —— FIP 专有容器格式（支持密码）
//
// 设计目标：只有本软件能解开，其他任何压缩软件都读不出来。
//   · 魔数 FIPK + 版本号，不是 zip/rar/7z 任何一种公开格式
//   · 载荷先 Deflate 压缩，再加密
//   · 文件头带 CRC32 和原始大小，用于完整性校验
//
// 两代格式：
//   版本 1（无密码）：RC4，密钥 = 内置密文 + 文件名。老文件仍然能读。
//   版本 2（有密码）：AES-256-CBC，密钥 = PBKDF2(密码, 随机盐, 20000 轮)
//                    盐和 IV 都存在文件头里，所以同一密码每次加密结果都不同。
//
// 同一个容器承载多种"种类"(Kind)，实现 mp3<->fmp 这类互转：
//   0 = fip 压缩包（载荷是一个 zip）      1 = fmp 音频
//   2 = fpg 图片(PNG)                     3 = fjp 图片(JPG)
//   4 = fp4 视频(MP4)                     5 = fov 视频(MOV)
//   6 = fxt 文本
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.IO.Compression;

namespace FipArc
{
    public static class Kind
    {
        public const byte Archive = 0;
        public const byte AudioFmp = 1;
        public const byte ImagePng = 2;
        public const byte ImageJpg = 3;
        public const byte VideoMp4 = 4;
        public const byte VideoMov = 5;
        public const byte TextTxt = 6;

        public static bool TryFromSourceExt(string srcExt, out byte kind, out string fExt)
        {
            kind = 0; fExt = null;
            switch (srcExt.ToLowerInvariant())
            {
                case ".mp3": kind = AudioFmp; fExt = ".fmp"; return true;
                case ".png": kind = ImagePng; fExt = ".fpg"; return true;
                case ".jpg":
                case ".jpeg": kind = ImageJpg; fExt = ".fjp"; return true;
                case ".mp4": kind = VideoMp4; fExt = ".fp4"; return true;
                case ".mov": kind = VideoMov; fExt = ".fov"; return true;
                case ".txt": kind = TextTxt; fExt = ".fxt"; return true;
            }
            return false;
        }

        public static bool TryFromFipExt(string fExt, out byte kind, out string srcExt)
        {
            kind = 0; srcExt = null;
            switch (fExt.ToLowerInvariant())
            {
                case ".fmp": kind = AudioFmp; srcExt = ".mp3"; return true;
                case ".fpg": kind = ImagePng; srcExt = ".png"; return true;
                case ".fjp": kind = ImageJpg; srcExt = ".jpg"; return true;
                case ".fp4": kind = VideoMp4; srcExt = ".mp4"; return true;
                case ".fov": kind = VideoMov; srcExt = ".mov"; return true;
                case ".fxt": kind = TextTxt; srcExt = ".txt"; return true;
            }
            return false;
        }

        public static string Describe(byte kind)
        {
            switch (kind)
            {
                case Archive: return "FIP 压缩包";
                case AudioFmp: return "FIP 音频";
                case ImagePng: return "FIP 图片(PNG)";
                case ImageJpg: return "FIP 图片(JPG)";
                case VideoMp4: return "FIP 视频(MP4)";
                case VideoMov: return "FIP 视频(MOV)";
                case TextTxt: return "FIP 文本";
            }
            return "未知";
        }
    }

    public static class Crc32
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
        public static uint Compute(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
                crc = Table[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
        public static uint Compute(byte[] data) { return Compute(data, 0, data.Length); }
    }

    /// <summary>RC4 —— 版本 1 用的轻量流密码</summary>
    public sealed class Rc4
    {
        readonly byte[] s = new byte[256];
        int i, j;

        public Rc4(byte[] key)
        {
            for (int k = 0; k < 256; k++) s[k] = (byte)k;
            int jj = 0;
            for (int k = 0; k < 256; k++)
            {
                jj = (jj + s[k] + key[k % key.Length]) & 0xFF;
                byte tmp = s[k]; s[k] = s[jj]; s[jj] = tmp;
            }
            i = 0; j = 0;
        }

        public byte NextByte()
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            byte tmp = s[i]; s[i] = s[j]; s[j] = tmp;
            return s[(s[i] + s[j]) & 0xFF];
        }

        public void Process(byte[] buf, int offset, int count)
        {
            for (int k = 0; k < count; k++) buf[offset + k] ^= NextByte();
        }

        public byte[] Process(byte[] buf)
        {
            byte[] o = new byte[buf.Length];
            Buffer.BlockCopy(buf, 0, o, 0, buf.Length);
            Process(o, 0, o.Length);
            return o;
        }
    }

    /// <summary>需要密码但没提供</summary>
    public sealed class PasswordRequiredException : Exception
    {
        public PasswordRequiredException(string file)
            : base("这个文件设置了密码，需要输入密码才能打开。")
        {
            FilePath = file;
        }
        public string FilePath;
    }

    /// <summary>密码不对</summary>
    public sealed class WrongPasswordException : Exception
    {
        public WrongPasswordException()
            : base("密码不正确（或文件已损坏）。") { }
    }

    public sealed class FipHeader
    {
        public ushort Version;
        public byte Kind;
        public byte Flags;
        public uint Crc;
        public int OrigSize;
        public string Name;
        public byte[] Salt;
        public byte[] Iv;
        public int PayloadOffset;
        public int PayloadSize;
        public bool NeedsPassword { get { return (Flags & FipFile.FlagPassword) != 0; } }
    }

    public sealed class FipFile
    {
        static readonly byte[] Secret = new byte[]
        {
            0x46,0x69,0x70,0x41,0x72,0x63,0x2D,0x50,
            0x76,0x5A,0x2D,0x43,0x4E,0x2D,0x32,0x30,
            0x32,0x36,0x2D,0x31,0x30,0x2D,0x32,0x34,
            0x9E,0x3B,0x71,0xC4,0x5A,0x08,0xD2,0x66
        };

        public const uint Magic = 0x4B504946;      // "FIPK" 小端
        public const ushort VersionPlain = 1;       // 无密码
        public const ushort VersionPwd = 2;         // 有密码
        public const int HeaderFixed = 4 + 2 + 1 + 1 + 4 + 4 + 2;   // 18
        public const int SaltLen = 16;
        public const int IvLen = 16;
        public const int Pbkdf2Rounds = 20000;

        public const byte FlagEncrypted = 1;
        public const byte FlagDeflated = 2;
        public const byte FlagPassword = 4;

        static byte[] DeriveLegacyKey(string name)
        {
            byte[] nb = Encoding.UTF8.GetBytes(name == null ? "" : name);
            byte[] key = new byte[Secret.Length + nb.Length];
            Buffer.BlockCopy(Secret, 0, key, 0, Secret.Length);
            Buffer.BlockCopy(nb, 0, key, Secret.Length, nb.Length);
            return key;
        }

        static byte[] DerivePwdKey(string password, byte[] salt)
        {
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(password, salt, Pbkdf2Rounds))
                return kdf.GetBytes(32);
        }

        static byte[] AesRun(byte[] data, byte[] key, byte[] iv, bool encrypt)
        {
            using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (ICryptoTransform t = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor())
                    return t.TransformFinalBlock(data, 0, data.Length);
            }
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

        /// <summary>打包（无密码，版本 1）</summary>
        public static byte[] Write(byte kind, string name, byte[] data)
        {
            return Write(kind, name, data, null);
        }

        /// <summary>打包；password 非空则用 AES-256 加密（版本 2）</summary>
        public static byte[] Write(byte kind, string name, byte[] data, string password)
        {
            if (data == null) data = new byte[0];
            if (name == null) name = "";

            // 只有 .fip 压缩包才做压缩。
            // fmp/fpg/fjp/fp4/fov/fxt 是"格式转换"，不是压缩 ——
            // 它们只是换了容器封装，体积应该跟原文件基本一致。
            bool useDeflate = false;
            byte[] payload = data;
            if (kind == Kind.Archive)
            {
                byte[] deflated = Deflate(data);
                if (deflated.Length < data.Length)
                {
                    payload = deflated;
                    useDeflate = true;
                }
            }

            bool usePwd = !string.IsNullOrEmpty(password);
            byte flags = useDeflate ? FlagDeflated : (byte)0;
            byte[] salt = null, iv = null;

            if (usePwd)
            {
                // 随机盐 + 随机 IV：同一密码每次加密结果都不同
                RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
                salt = new byte[SaltLen]; rng.GetBytes(salt);
                iv = new byte[IvLen]; rng.GetBytes(iv);
                byte[] key = DerivePwdKey(password, salt);
                payload = AesRun(payload, key, iv, true);
                flags |= FlagPassword | FlagEncrypted;
            }
            else
            {
                Rc4 rc = new Rc4(DeriveLegacyKey(name));
                payload = rc.Process(payload);
                flags |= FlagEncrypted;
            }

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms, Encoding.UTF8))
            {
                bw.Write(Magic);
                bw.Write(usePwd ? VersionPwd : VersionPlain);
                bw.Write(kind);
                bw.Write(flags);
                bw.Write(Crc32.Compute(data));
                bw.Write(data.Length);
                bw.Write((ushort)name.Length);
                bw.Write(Encoding.Unicode.GetBytes(name));
                if (usePwd)
                {
                    bw.Write(salt);
                    bw.Write(iv);
                }
                bw.Write(payload);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static bool IsFip(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] h = new byte[4];
                    if (fs.Read(h, 0, 4) != 4) return false;
                    return BitConverter.ToUInt32(h, 0) == Magic;
                }
            }
            catch { return false; }
        }

        /// <summary>这个文件是否需要密码（只读头部，不碰载荷）</summary>
        public static bool NeedsPassword(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    FipHeader h = ReadHeader(br, fs.Length);
                    return h.NeedsPassword;
                }
            }
            catch { return false; }
        }

        public static FipHeader ReadHeader(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (BinaryReader br = new BinaryReader(fs))
                return ReadHeader(br, fs.Length);
        }

        public static FipHeader ReadHeader(BinaryReader br, long fileLength)
        {
            uint magic = br.ReadUInt32();
            if (magic != Magic) throw new InvalidDataException("不是 FIP 文件");
            FipHeader h = new FipHeader();
            h.Version = br.ReadUInt16();
            if (h.Version != VersionPlain && h.Version != VersionPwd)
                throw new InvalidDataException("FIP 版本不支持: " + h.Version);
            h.Kind = br.ReadByte();
            h.Flags = br.ReadByte();
            h.Crc = br.ReadUInt32();
            h.OrigSize = br.ReadInt32();
            int nameLen = br.ReadUInt16();
            h.Name = Encoding.Unicode.GetString(br.ReadBytes(nameLen * 2));
            h.PayloadOffset = HeaderFixed + nameLen * 2;
            if (h.NeedsPassword)
            {
                h.Salt = br.ReadBytes(SaltLen);
                h.Iv = br.ReadBytes(IvLen);
                h.PayloadOffset += SaltLen + IvLen;
            }
            h.PayloadSize = (int)(fileLength - h.PayloadOffset);
            if (h.PayloadSize < 0) throw new InvalidDataException("FIP 文件被截断");
            return h;
        }

        /// <summary>解开（无密码）</summary>
        public static byte[] Read(string path, out byte kind, out string name)
        {
            return Read(path, out kind, out name, null);
        }

        /// <summary>解开；密码保护的文件必须传 password</summary>
        public static byte[] Read(string path, out byte kind, out string name, string password)
        {
            using (FileStream fs = File.OpenRead(path))
            using (BinaryReader br = new BinaryReader(fs))
            {
                FipHeader h = ReadHeader(br, fs.Length);
                if (h.NeedsPassword && string.IsNullOrEmpty(password))
                    throw new PasswordRequiredException(path);

                byte[] payload = br.ReadBytes(h.PayloadSize);

                if (h.NeedsPassword)
                {
                    byte[] key = DerivePwdKey(password, h.Salt);
                    try { payload = AesRun(payload, key, h.Iv, false); }
                    catch { throw new WrongPasswordException(); }
                }
                else if ((h.Flags & FlagEncrypted) != 0)
                {
                    Rc4 rc = new Rc4(DeriveLegacyKey(h.Name));
                    payload = rc.Process(payload);
                }

                byte[] data;
                if ((h.Flags & FlagDeflated) != 0)
                {
                    try { data = Inflate(payload); }
                    catch { throw new WrongPasswordException(); }
                }
                else data = payload;

                if (data.Length != h.OrigSize) throw new WrongPasswordException();
                if (Crc32.Compute(data) != h.Crc) throw new WrongPasswordException();

                kind = h.Kind;
                name = h.Name;
                return data;
            }
        }
    }
}
