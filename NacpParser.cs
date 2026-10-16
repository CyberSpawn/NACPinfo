using System.Buffers.Binary;
using System.Text;

namespace NACPinfo;

public enum Kind { Str, U8, U16, U32, U32Hex, U64Hex, Size, Enum, Flags, Hex, U64List, U16List, Ratings, Jit, Titles }

public sealed record Field(int Offset, int Size, string Name, Kind Kind, Dictionary<int, string>? Map = null, string Since = "");

public sealed record FieldResult(Field Field, object? Json, string Text);

public static class NacpParser
{
    public const int Size = 0x4000;

    // Indices 0-15: original languages. 16-30: extended languages, present only in the
    // compressed title block format (TitlesDataFormat = 1).
    static readonly string[] Langs =
    {
        "AmericanEnglish", "BritishEnglish", "Japanese", "French", "German",
        "LatinAmericanSpanish", "Spanish", "Italian", "Dutch", "CanadianFrench",
        "Portuguese", "Russian", "Korean", "TraditionalChinese",
        "SimplifiedChinese", "BrazilianPortuguese",
        "Polish", "Thai", "Indonesian", "Romanian", "Vietnamese", "Arabic",
        "Ukrainian", "Czech", "Slovak", "Greek", "Hungarian", "Norwegian",
        "Finnish", "Swedish", "Danish",
    };

    static readonly string[] RatingOrgs =
    {
        "CERO", "GRACGCRB", "GSRMR", "ESRB", "ClassInd", "USK", "PEGI",
        "PEGIPortugal", "PEGIBBFC", "Russian", "ACB", "OFLC",
    };

    static Dictionary<int, string> Map(params string[] names) =>
        names.Select((n, i) => (n, i)).ToDictionary(x => x.i, x => x.n);

    static readonly Dictionary<int, string> LangBits = Map(Langs);
    static readonly Dictionary<int, string> Attr = Map("Demo", "RetailInteractiveDisplay");
    static readonly Dictionary<int, string> Parental = Map("FreeCommunication");
    static readonly Dictionary<int, string> StartupOpt = Map("IsOptional");
    static readonly Dictionary<int, string> Repair = Map("SuppressGameCardAccess");
    static readonly Dictionary<int, string> ReqNet = Map("Common");

    static readonly Dictionary<int, string> EStartup = Map("None", "Required", "RequiredWithNetworkServiceAccountAvailable");
    static readonly Dictionary<int, string> EDisableEnable = Map("Disable", "Enable");
    static readonly Dictionary<int, string> EAddOnReg = Map("AllOnLaunch", "OnDemand");
    static readonly Dictionary<int, string> EShot = Map("Allow", "Deny");
    static readonly Dictionary<int, string> EVideo = Map("Disable", "Manual", "Enable");
    static readonly Dictionary<int, string> ELoss = Map("None", "Required");
    static readonly Dictionary<int, string> EPlayLog = Map("All", "LogOnly", "None");
    static readonly Dictionary<int, string> ELogo = Map("LicensedByNintendo", "DistributedByNintendo", "Nintendo");
    static readonly Dictionary<int, string> ELogoHandling = Map("Auto", "Manual");
    static readonly Dictionary<int, string> ERtAddOn = Map("Deny", "AllowAppend", "AllowAppendButDontDownloadWhenUsingNetwork");
    static readonly Dictionary<int, string> ERtParam = Map("Always", "AlwaysIfUserStateMatched", "OnRestart");
    static readonly Dictionary<int, string> ECrash = Map("Deny", "Allow");
    static readonly Dictionary<int, string> EHdcp = Map("None", "Required");
    static readonly Dictionary<int, string> EPlq = Map("None", "WhiteList", "All");
    static readonly Dictionary<int, string> EPrp = Map("None", "TargetMarketing");
    static readonly Dictionary<int, string> ECat = Map("NoPolicy", "Stable", "Changeable");

    public static readonly Field[] Fields =
    {
        new(0x0000, 0x3000, "Title", Kind.Titles),
        new(0x3000, 0x25, "Isbn", Kind.Str),
        new(0x3025, 1, "StartupUserAccount", Kind.Enum, EStartup),
        new(0x3026, 1, "UserAccountSwitchLock", Kind.Enum, EDisableEnable),
        new(0x3027, 1, "AddOnContentRegistrationType", Kind.Enum, EAddOnReg),
        new(0x3028, 4, "AttributeFlag", Kind.Flags, Attr),
        new(0x302C, 4, "SupportedLanguageFlag", Kind.Flags, LangBits),
        new(0x3030, 4, "ParentalControlFlag", Kind.Flags, Parental),
        new(0x3034, 1, "Screenshot", Kind.Enum, EShot),
        new(0x3035, 1, "VideoCapture", Kind.Enum, EVideo),
        new(0x3036, 1, "DataLossConfirmation", Kind.Enum, ELoss),
        new(0x3037, 1, "PlayLogPolicy", Kind.Enum, EPlayLog),
        new(0x3038, 8, "PresenceGroupId", Kind.U64Hex),
        new(0x3040, 0x20, "RatingAge", Kind.Ratings),
        new(0x3060, 0x10, "DisplayVersion", Kind.Str),
        new(0x3070, 8, "AddOnContentBaseId", Kind.U64Hex),
        new(0x3078, 8, "SaveDataOwnerId", Kind.U64Hex),
        new(0x3080, 8, "UserAccountSaveDataSize", Kind.Size),
        new(0x3088, 8, "UserAccountSaveDataJournalSize", Kind.Size),
        new(0x3090, 8, "DeviceSaveDataSize", Kind.Size),
        new(0x3098, 8, "DeviceSaveDataJournalSize", Kind.Size),
        new(0x30A0, 8, "BcatDeliveryCacheStorageSize", Kind.Size),
        new(0x30A8, 8, "ApplicationErrorCodeCategory", Kind.Str),
        new(0x30B0, 0x40, "LocalCommunicationId", Kind.U64List),
        new(0x30F0, 1, "LogoType", Kind.Enum, ELogo),
        new(0x30F1, 1, "LogoHandling", Kind.Enum, ELogoHandling),
        new(0x30F2, 1, "RuntimeAddOnContentInstall", Kind.Enum, ERtAddOn),
        new(0x30F3, 1, "RuntimeParameterDelivery", Kind.Enum, ERtParam),
        new(0x30F4, 1, "AppropriateAgeForChina", Kind.U8),
        new(0x30F5, 1, "Reserved", Kind.Hex),
        new(0x30F6, 1, "CrashReport", Kind.Enum, ECrash),
        new(0x30F7, 1, "Hdcp", Kind.Enum, EHdcp),
        new(0x30F8, 8, "SeedForPseudoDeviceId", Kind.U64Hex),
        new(0x3100, 0x41, "BcatPassphrase", Kind.Str),
        new(0x3141, 1, "StartupUserAccountOption", Kind.Flags, StartupOpt),
        new(0x3142, 6, "ReservedForUserAccountSaveDataOperation", Kind.Hex),
        new(0x3148, 8, "UserAccountSaveDataSizeMax", Kind.Size),
        new(0x3150, 8, "UserAccountSaveDataJournalSizeMax", Kind.Size),
        new(0x3158, 8, "DeviceSaveDataSizeMax", Kind.Size),
        new(0x3160, 8, "DeviceSaveDataJournalSizeMax", Kind.Size),
        new(0x3168, 8, "TemporaryStorageSize", Kind.Size),
        new(0x3170, 8, "CacheStorageSize", Kind.Size),
        new(0x3178, 8, "CacheStorageJournalSize", Kind.Size),
        new(0x3180, 8, "CacheStorageDataAndJournalSizeMax", Kind.Size),
        new(0x3188, 2, "CacheStorageIndexMax", Kind.U16),
        new(0x318A, 1, "Reserved", Kind.Hex),
        new(0x318B, 1, "RuntimeUpgrade", Kind.U8),
        new(0x318C, 4, "SupportingLimitedApplicationLicenses", Kind.U32),
        new(0x3190, 0x80, "PlayLogQueryableApplicationId", Kind.U64List),
        new(0x3210, 1, "PlayLogQueryCapability", Kind.Enum, EPlq),
        new(0x3211, 1, "RepairFlag", Kind.Flags, Repair),
        new(0x3212, 1, "ProgramIndex", Kind.U8),
        new(0x3213, 1, "RequiredNetworkServiceLicenseOnLaunchFlag", Kind.Flags, ReqNet),
        new(0x3214, 1, "ApplicationErrorCodePrefix", Kind.U8, null, "20.0.0+"),
        new(0x3215, 1, "TitlesDataFormat", Kind.U8, null, "21.0.0+"),
        new(0x3216, 1, "AcdIndex", Kind.U8, null, "20.0.0+"),
        new(0x3217, 1, "ApparentPlatform", Kind.U8, null, "20.0.0+"),
        new(0x3218, 0x198, "NeighborDetectionClientConfiguration", Kind.Hex),
        new(0x33B0, 0x10, "JitConfiguration", Kind.Jit),
        new(0x33C0, 0x40, "RequiredAddOnContentsSetBinaryDescriptor", Kind.U16List),
        new(0x3400, 1, "PlayReportPermission", Kind.Enum, EPrp),
        new(0x3401, 1, "CrashScreenshotForProd", Kind.Enum, ECrash),
        new(0x3402, 1, "CrashScreenshotForDev", Kind.Enum, ECrash),
        new(0x3403, 1, "ContentsAvailabilityTransitionPolicy", Kind.Enum, ECat),
        new(0x3404, 4, "SupportedLanguageFlagForNxAddon", Kind.Flags, LangBits, "21.0.0+"),
        new(0x3408, 0x40, "AccessibleLaunchRequiredVersion", Kind.U64List),
        new(0x3448, 0x89, "ApplicationControlDataCondition", Kind.Hex, null, "20.0.0+"),
        new(0x34D1, 1, "InitialProgramIndex", Kind.U8, null, "20.0.0+"),
        new(0x34D2, 2, "Reserved", Kind.Hex),
        new(0x34D4, 4, "AccessibleProgramIndexFlags", Kind.U32Hex, null, "20.0.0+"),
        new(0x34D8, 1, "AlbumFileExport", Kind.U8, null, "20.0.0+"),
        new(0x34D9, 1, "SuppressForceConsoleMode", Kind.U8, null, "23.0.0+"),
        new(0x34DA, 6, "Reserved", Kind.Hex),
        new(0x34E0, 0x80, "SaveDataCertificateBytes", Kind.Hex, null, "20.0.0+"),
        new(0x3560, 1, "HasInGameVoiceChat", Kind.U8, null, "20.0.0+"),
        new(0x3561, 3, "Reserved", Kind.Hex),
        new(0x3564, 4, "SupportedExtraAddOnContentFlag", Kind.U32Hex, null, "20.0.0+"),
        new(0x3568, 1, "HasKaraokeFeature", Kind.U8, null, "21.0.0+"),
        new(0x3569, 1, "StreamPlaySaveDataKeyType", Kind.U8, null, "23.0.0+"),
        new(0x356A, 0x696, "Reserved", Kind.Hex),
        new(0x3C00, 0x400, "PlatformSpecificRegion", Kind.Hex, null, "20.0.0+"),
    };

    // ------------------------------------------------------------------ helpers

    static string LangName(int i) => i < Langs.Length ? Langs[i] : $"Language{i}";

    static string CStr(ReadOnlySpan<byte> b)
    {
        int n = b.IndexOf((byte)0);
        if (n < 0) n = b.Length;
        return Encoding.UTF8.GetString(b[..n]);
    }

    static ulong ReadLe(byte[] raw)
    {
        ulong v = 0;
        for (int i = raw.Length - 1; i >= 0; i--) v = (v << 8) | raw[i];
        return v;
    }

    static string HSize(ulong n)
    {
        if (n == 0) return "0";
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };
        double v = n;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {units[i]}";
    }

    static string HexDump(byte[] raw, int width = 32)
    {
        if (raw.All(b => b == 0)) return "(all zero)";
        var sb = new StringBuilder();
        for (int i = 0; i < raw.Length; i += width)
        {
            sb.Append('\n').Append("      ");
            sb.Append(string.Join(' ', raw.Skip(i).Take(width).Select(b => b.ToString("x2"))));
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ decode

    static (object? Json, string Text) Decode(Field f, byte[] raw)
    {
        switch (f.Kind)
        {
            case Kind.Str:
            {
                string s = CStr(raw);
                return (s, s.Length == 0 ? "(empty)" : $"\"{s}\"");
            }
            case Kind.U8:
            case Kind.U16:
            case Kind.U32:
            {
                ulong v = ReadLe(raw);
                return (v, $"{v} (0x{v:X})");
            }
            case Kind.U32Hex:
            {
                ulong v = ReadLe(raw);
                return (v, $"0x{v:X8}");
            }
            case Kind.U64Hex:
            {
                ulong v = ReadLe(raw);
                return (v, $"0x{v:X16}");
            }
            case Kind.Size:
            {
                ulong v = ReadLe(raw);
                return (v, $"0x{v:X} ({v} bytes, {HSize(v)})");
            }
            case Kind.Enum:
            {
                int v = raw[0];
                string name = f.Map!.TryGetValue(v, out var n) ? n : "Unknown";
                return (v, $"{v} ({name})");
            }
            case Kind.Flags:
            {
                ulong v = ReadLe(raw);
                var names = new List<string>();
                for (int b = 0; b < raw.Length * 8; b++)
                    if ((v >> b & 1) != 0)
                        names.Add(f.Map!.TryGetValue(b, out var n) ? n : $"Bit{b}");
                var json = new Dictionary<string, object> { ["value"] = v, ["set"] = names };
                string txt = $"0x{v.ToString("X").PadLeft(raw.Length * 2, '0')}  [{(names.Count > 0 ? string.Join(", ", names) : "none")}]";
                return (json, txt);
            }
            case Kind.Hex:
                return (Convert.ToHexString(raw).ToLowerInvariant(),
                        raw.Length > 8 ? HexDump(raw) : Convert.ToHexString(raw).ToLowerInvariant());
            case Kind.U64List:
            {
                var vals = new List<ulong>();
                var sb = new StringBuilder();
                for (int i = 0; i < raw.Length / 8; i++)
                {
                    ulong v = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(i * 8));
                    vals.Add(v);
                    sb.Append($"\n      [{i}] 0x{v:X16}");
                }
                return (vals, sb.ToString());
            }
            case Kind.U16List:
            {
                var vals = new List<ushort>();
                var sb = new StringBuilder();
                for (int i = 0; i < raw.Length / 2; i++)
                {
                    ushort v = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i * 2));
                    vals.Add(v);
                    sb.Append($"\n      [{i}] 0x{v:X4}");
                }
                return (vals, sb.ToString());
            }
            case Kind.Ratings:
            {
                var json = new Dictionary<string, int>();
                var sb = new StringBuilder();
                for (int i = 0; i < raw.Length; i++)
                {
                    int v = (sbyte)raw[i];
                    string org = i < RatingOrgs.Length ? RatingOrgs[i] : $"Org{i}";
                    json[org] = v;
                    sb.Append($"\n      [{i,2}] {org,-13} {(v < 0 ? "not rated" : v.ToString())}");
                }
                return (json, sb.ToString());
            }
            case Kind.Jit:
            {
                ulong flags = BinaryPrimitives.ReadUInt64LittleEndian(raw);
                ulong mem = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(8));
                var json = new Dictionary<string, object>
                {
                    ["flags"] = flags, ["enabled"] = (flags & 1) != 0, ["memory_size"] = mem,
                };
                return (json, $"\n      Flags      = 0x{flags:X} (Enabled={(flags & 1) != 0})" +
                              $"\n      MemorySize = 0x{mem:X} ({HSize(mem)})");
            }
            case Kind.Titles:
            {
                var list = new List<Dictionary<string, string>>();
                var sb = new StringBuilder();
                for (int i = 0; i < raw.Length / 0x300; i++)
                {
                    var e = raw.AsSpan(i * 0x300, 0x300);
                    string name = CStr(e[..0x200]);
                    string pub = CStr(e[0x200..]);
                    list.Add(new() { ["language"] = LangName(i), ["name"] = name, ["publisher"] = pub });
                    if (name.Length > 0 || pub.Length > 0)
                        sb.Append($"\n      [{i,2}] {LangName(i)}\n           Name:      {name}\n           Publisher: {pub}");
                    else
                        sb.Append($"\n      [{i,2}] {LangName(i)}: (empty)");
                }
                return (list, sb.ToString());
            }
        }
        throw new InvalidOperationException($"Unhandled kind {f.Kind}");
    }

    // ------------------------------------------------------------ compressed titles
    // TitlesDataFormat (0x3215): 0 = 16 raw 0x300-byte entries in the first 0x3000 bytes.
    //                            1 = raw deflate stream starting at offset 2 of that block,
    //                                expanding to a Title array with more languages.

    static (byte[] Raw, string Note) ResolveTitles(byte[] buf)
    {
        byte[] block = buf[..0x3000];
        int format = buf[0x3215];
        if (format == 0) return (block, "");

        if (format != 1)
            return (block, $"WARNING: unknown TitlesDataFormat={format}; showing raw bytes");

        try
        {
            using var input = new MemoryStream(block, 2, block.Length - 2);
            using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var ms = new MemoryStream();
            deflate.CopyTo(ms);
            byte[] data = ms.ToArray();

            if (data.Length == 0 || data.Length % 0x300 != 0)
                return (block, $"WARNING: TitlesDataFormat=1 but decompressed size 0x{data.Length:X} is not a multiple of 0x300; showing raw bytes");

            return (data, $"TitlesDataFormat=1: decompressed -> {data.Length / 0x300} languages");
        }
        catch (InvalidDataException ex)
        {
            return (block, $"WARNING: TitlesDataFormat=1 but decompression failed ({ex.Message}); showing raw bytes");
        }
    }

    public static List<FieldResult> Parse(byte[] buf)
    {
        if (buf.Length < Size) Array.Resize(ref buf, Size);
        var results = new List<FieldResult>(Fields.Length);
        foreach (var f in Fields)
        {
            byte[] slice;
            string note = "";
            if (f.Kind == Kind.Titles) (slice, note) = ResolveTitles(buf);
            else slice = buf[f.Offset..(f.Offset + f.Size)];

            var (json, text) = Decode(f, slice);
            results.Add(new FieldResult(f, json, f.Kind == Kind.Titles ? note : text));
        }
        return results;
    }
}
