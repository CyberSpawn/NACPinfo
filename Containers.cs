using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NACPinfo;

public sealed record FileEntry(string Name, long Offset, long Size);

// ---------------------------------------------------------------------------
// Keys
// ---------------------------------------------------------------------------
public sealed class Keys
{
    readonly Dictionary<string, byte[]> _k = new();

    public static Keys Load(string path)
    {
        var keys = new Keys();
        foreach (var line in File.ReadLines(path))
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] == '#' || t[0] == ';') continue;
            int eq = t.IndexOf('=');
            if (eq < 0) continue;
            try { keys._k[t[..eq].Trim().ToLowerInvariant()] = Convert.FromHexString(t[(eq + 1)..].Trim()); }
            catch (FormatException) { /* skip non-hex lines */ }
        }
        return keys;
    }

    /// <summary>Loads prod.keys from the current working directory, or null if it is missing.</summary>
    public static Keys? Find()
    {
        const string path = "prod.keys";
        return File.Exists(path) ? Load(path) : null;
    }

    public byte[] Get(string name) =>
        _k.TryGetValue(name, out var v) ? v : throw new KeyNotFoundException($"{name} missing from keys file");
}

// ---------------------------------------------------------------------------
// Crypto
// ---------------------------------------------------------------------------
public static class Crypto
{
    static Aes MakeAes(byte[] key)
    {
        var aes = Aes.Create();
        aes.Key = key;
        return aes;
    }

    public static byte[] EcbDecrypt(byte[] key, byte[] data)
    {
        using var aes = MakeAes(key);
        return aes.DecryptEcb(data, PaddingMode.None);
    }

    static void MulAlpha(byte[] t)
    {
        int carry = 0;
        for (int i = 0; i < 16; i++)
        {
            int next = (t[i] >> 7) & 1;
            t[i] = (byte)((t[i] << 1) | carry);
            carry = next;
        }
        if (carry != 0) t[0] ^= 0x87;
    }

    /// <summary>AES-128-XTS decrypt with Nintendo's big-endian sector tweak. key = 32 bytes.</summary>
    public static byte[] XtsDecrypt(byte[] key, byte[] data, ulong firstSector = 0, int sectorSize = 0x200)
    {
        using var k1 = MakeAes(key[..16]);
        using var k2 = MakeAes(key[16..32]);
        var output = new byte[data.Length];
        var tweakIn = new byte[16];
        var block = new byte[16];

        for (int s = 0; s * sectorSize < data.Length; s++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(tweakIn.AsSpan(8), firstSector + (ulong)s);
            byte[] t = k2.EncryptEcb(tweakIn, PaddingMode.None);

            int sectorEnd = Math.Min(data.Length, (s + 1) * sectorSize);
            for (int off = s * sectorSize; off + 16 <= sectorEnd; off += 16)
            {
                for (int i = 0; i < 16; i++) block[i] = (byte)(data[off + i] ^ t[i]);
                byte[] dec = k1.DecryptEcb(block, PaddingMode.None);
                for (int i = 0; i < 16; i++) output[off + i] = (byte)(dec[i] ^ t[i]);
                MulAlpha(t);
            }
        }
        return output;
    }

    /// <summary>AES-128-CTR. absOffset is the offset of data inside the NCA (16-byte aligned).</summary>
    public static byte[] CtrDecrypt(byte[] key, byte[] sectionCtr8, long absOffset, byte[] data)
    {
        int blocks = (data.Length + 15) / 16;
        var counters = new byte[blocks * 16];
        ulong start = (ulong)absOffset >> 4;
        for (int b = 0; b < blocks; b++)
        {
            for (int i = 0; i < 8; i++) counters[b * 16 + i] = sectionCtr8[7 - i];
            BinaryPrimitives.WriteUInt64BigEndian(counters.AsSpan(b * 16 + 8), start + (ulong)b);
        }
        using var aes = MakeAes(key);
        byte[] stream = aes.EncryptEcb(counters, PaddingMode.None);
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++) result[i] = (byte)(data[i] ^ stream[i]);
        return result;
    }
}

// ---------------------------------------------------------------------------
// Containers (PFS0 / HFS0 / XCI)
// ---------------------------------------------------------------------------
public static class Containers
{
    public static byte[] ReadAt(Stream s, long off, int n)
    {
        s.Seek(off, SeekOrigin.Begin);
        var b = new byte[n];
        s.ReadExactly(b);
        return b;
    }

    static string Ascii4(byte[] b) => Encoding.ASCII.GetString(b, 0, 4);

    /// <summary>Parses a PFS0 (entry size 0x18) or HFS0 (entry size 0x40) table.</summary>
    static List<FileEntry> ParseTable(Stream s, long baseOff, string magic, int entrySize)
    {
        var h = ReadAt(s, baseOff, 16);
        if (Ascii4(h) != magic) throw new InvalidDataException($"Not a {magic}");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4));
        uint strSize = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8));
        var entries = ReadAt(s, baseOff + 0x10, (int)(count * entrySize));
        var strTab = ReadAt(s, baseOff + 0x10 + count * entrySize, (int)strSize);
        long dataStart = baseOff + 0x10 + count * entrySize + strSize;

        var files = new List<FileEntry>();
        for (int i = 0; i < count; i++)
        {
            var e = entries.AsSpan(i * entrySize);
            long off = (long)BinaryPrimitives.ReadUInt64LittleEndian(e);
            long size = (long)BinaryPrimitives.ReadUInt64LittleEndian(e[8..]);
            int nameOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[16..]);
            int end = Array.IndexOf(strTab, (byte)0, nameOff);
            if (end < 0) end = strTab.Length;
            files.Add(new FileEntry(Encoding.UTF8.GetString(strTab, nameOff, end - nameOff), dataStart + off, size));
        }
        return files;
    }

    public static (List<FileEntry> Files, string Kind) Collect(Stream s)
    {
        string head = s.Length >= 4 ? Ascii4(ReadAt(s, 0, 4)) : "";
        if (head == "PFS0") return (ParseTable(s, 0, "PFS0", 0x18), "NSP");

        List<FileEntry> root;
        string kind;
        if (head == "HFS0")
        {
            root = ParseTable(s, 0, "HFS0", 0x40);
            kind = "HFS0";
        }
        else if (s.Length > 0x104 && Ascii4(ReadAt(s, 0x100, 4)) == "HEAD")
        {
            long hfsOff = (long)BinaryPrimitives.ReadUInt64LittleEndian(ReadAt(s, 0x130, 8));
            if (hfsOff <= 0 || hfsOff + 4 > s.Length || Ascii4(ReadAt(s, hfsOff, 4)) != "HFS0")
                hfsOff = 0xF000;
            root = ParseTable(s, hfsOff, "HFS0", 0x40);
            kind = "XCI";
        }
        else throw new InvalidDataException("Unrecognized file (not NSP / XCI / HFS0).");

        var files = new List<FileEntry>();
        foreach (var part in root)   // partitions: secure / normal / update / logo
        {
            try { files.AddRange(ParseTable(s, part.Offset, "HFS0", 0x40)); }
            catch (InvalidDataException) { files.Add(part); }
        }
        return (files, kind);
    }

    // ---- Tickets ----------------------------------------------------------
    static readonly Dictionary<uint, int> SigSizes = new()
    {
        [0x010000] = 0x200 + 0x3C, [0x010001] = 0x100 + 0x3C, [0x010002] = 0x3C + 0x40,
        [0x010003] = 0x200 + 0x3C, [0x010004] = 0x100 + 0x3C, [0x010005] = 0x3C + 0x40,
    };

    public static (string RightsId, byte[] EncTitleKey)? ParseTicket(byte[] data)
    {
        if (data.Length < 8) return null;
        uint sig = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (!SigSizes.TryGetValue(sig, out int sigSize)) return null;
        int body = 4 + sigSize;
        if (data.Length < body + 0x170) return null;
        var tkey = data[(body + 0x40)..(body + 0x50)];
        var rid = data[(body + 0x160)..(body + 0x170)];
        return (Convert.ToHexString(rid).ToLowerInvariant(), tkey);
    }
}

// ---------------------------------------------------------------------------
// NCA -> control.nacp
// ---------------------------------------------------------------------------
public static class Nca
{
    static byte[]? DecryptHeader(Keys keys, byte[] raw)
    {
        byte[] hk = keys.Get("header_key");
        byte[] h = Crypto.XtsDecrypt(hk, raw[..0xC00]);
        string magic = Encoding.ASCII.GetString(h, 0x200, 4);
        if (magic == "NCA3") return h;
        if (magic == "NCA2")
        {
            for (int i = 0; i < 4; i++)
            {
                var seg = Crypto.XtsDecrypt(hk, raw[(0x400 + i * 0x200)..(0x600 + i * 0x200)]);
                seg.CopyTo(h, 0x400 + i * 0x200);
            }
            return h;
        }
        return null;
    }

    static byte[] SectionKey(Keys keys, byte[] hdr, Dictionary<string, byte[]> tickets)
    {
        int kgOld = hdr[0x206], appIdx = hdr[0x207], kgNew = hdr[0x220];
        int kg = Math.Max(kgOld, kgNew);
        kg = kg > 0 ? kg - 1 : 0;

        byte[] rightsId = hdr[0x230..0x240];
        if (rightsId.Any(b => b != 0))
        {
            string rid = Convert.ToHexString(rightsId).ToLowerInvariant();
            if (!tickets.TryGetValue(rid, out var enc))
                throw new InvalidDataException("NCA uses a rights ID but no matching ticket was found");
            return Crypto.EcbDecrypt(keys.Get($"titlekek_{kg:x2}"), enc);
        }
        string area = appIdx switch { 1 => "ocean", 2 => "system", _ => "application" };
        byte[] kak = keys.Get($"key_area_key_{area}_{kg:x2}");
        byte[] keyArea = Crypto.EcbDecrypt(kak, hdr[0x300..0x340]);
        return keyArea[0x20..0x30];   // key index 2 = CTR
    }

    static bool ValidRomFsHeader(byte[] buf, long off)
    {
        if (off < 0 || off + 0x50 > buf.Length) return false;
        ulong headerSize = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan((int)off));
        ulong fileMetaOff = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan((int)off + 0x38));
        ulong dataOff = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan((int)off + 0x48));
        return headerSize == 0x50 && fileMetaOff < (ulong)buf.Length && dataOff < (ulong)buf.Length;
    }

    /// <summary>Returns the raw NACP, or null if this NCA is not a Control NCA.</summary>
    public static byte[]? ExtractNacp(Stream s, FileEntry nca, Keys keys, Dictionary<string, byte[]> tickets)
    {
        byte[] raw = Containers.ReadAt(s, nca.Offset, 0xC00);
        byte[]? hdr = DecryptHeader(keys, raw);
        if (hdr is null) return null;
        if (hdr[0x205] != 2) return null;   // 2 = Control

        long secStart = (long)BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(0x240)) * 0x200;
        long secEnd = (long)BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(0x244)) * 0x200;
        byte[] fsh = hdr[0x400..0x600];
        int fsType = fsh[2], encType = fsh[4];
        if (fsType != 0) throw new InvalidDataException("Control NCA section 0 is not RomFS");

        byte[] data = Containers.ReadAt(s, nca.Offset + secStart, (int)(secEnd - secStart));
        if (encType is 3 or 4)
            data = Crypto.CtrDecrypt(SectionKey(keys, hdr, tickets), fsh[0x140..0x148], secStart, data);
        else if (encType != 1)
            throw new NotSupportedException($"Unsupported section encryption type {encType}");

        // IVFC data level (level 6) offset: FS header 0x08 + 0x10 + 5*0x18
        long romfs = (long)BinaryPrimitives.ReadUInt64LittleEndian(fsh.AsSpan(0x90));
        if (!ValidRomFsHeader(data, romfs))
        {
            romfs = -1;
            for (long c = 0; c < Math.Min(data.Length, 0x100000); c += 0x200)
                if (ValidRomFsHeader(data, c)) { romfs = c; break; }
            if (romfs < 0)
                throw new InvalidDataException(
                    "RomFS header not found - decryption likely failed (wrong/missing keys or personalized ticket)");
        }

        long fileMetaOff = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan((int)romfs + 0x38));
        long fileMetaSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan((int)romfs + 0x40));
        long dataOff = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan((int)romfs + 0x48));

        int pos = 0;
        int tblStart = (int)(romfs + fileMetaOff);
        while (pos + 0x20 <= fileMetaSize)
        {
            var e = data.AsSpan(tblStart + pos);
            long fOff = (long)BinaryPrimitives.ReadUInt64LittleEndian(e[8..]);
            long fSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(e[16..]);
            int nameLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[28..]);
            string name = Encoding.UTF8.GetString(e.Slice(0x20, nameLen));
            if (name == "control.nacp")
                return data[(int)(romfs + dataOff + fOff)..(int)(romfs + dataOff + fOff + fSize)];
            pos += 0x20 + ((nameLen + 3) & ~3);
        }
        throw new InvalidDataException("control.nacp not found in RomFS");
    }
}
