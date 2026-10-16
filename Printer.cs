using System.Text;

namespace NACPinfo;

/// <summary>Pretty, sectioned, colour-aware console output for a parsed NACP.</summary>
public sealed class Printer
{
    const int Width = 78;
    const int NameCol = 42;
    const string Indent = "          ";   // lines up under the value column of a row

    readonly bool _color;
    public Printer(bool color) => _color = color;

    // ------------------------------------------------------------ colours
    string C(string code, string s) => _color ? $"\u001b[{code}m{s}\u001b[0m" : s;
    string Dim(string s) => C("90", s);
    string Bold(string s) => C("1", s);
    string Head(string s) => C("1;36", s);
    string Val(string s) => C("32", s);
    string Warn(string s) => C("33", s);

    // ------------------------------------------------------------ layout
    static readonly (string Title, string[] Names)[] Sections =
    {
        ("Title & Publisher", new[] { "Title" }),
        ("General", new[]
        {
            "Isbn", "DisplayVersion", "StartupUserAccount", "StartupUserAccountOption",
            "UserAccountSwitchLock", "AttributeFlag", "ParentalControlFlag",
            "AppropriateAgeForChina", "ProgramIndex", "InitialProgramIndex",
            "AccessibleProgramIndexFlags", "ApparentPlatform", "AcdIndex",
            "LogoType", "LogoHandling", "Hdcp",
        }),
        ("Age Ratings", new[] { "RatingAge" }),
        ("Languages", new[] { "SupportedLanguageFlag", "SupportedLanguageFlagForNxAddon" }),
        ("IDs", new[]
        {
            "PresenceGroupId", "AddOnContentBaseId", "SaveDataOwnerId", "LocalCommunicationId",
            "SeedForPseudoDeviceId", "PlayLogQueryableApplicationId",
            "AccessibleLaunchRequiredVersion", "ApplicationErrorCodeCategory",
            "ApplicationErrorCodePrefix",
        }),
        ("Capture, Logging & Crash Reports", new[]
        {
            "Screenshot", "VideoCapture", "DataLossConfirmation", "PlayLogPolicy",
            "PlayLogQueryCapability", "CrashReport", "CrashScreenshotForProd",
            "CrashScreenshotForDev", "PlayReportPermission", "AlbumFileExport",
        }),
        ("Save Data & Storage", new[]
        {
            "UserAccountSaveDataSize", "UserAccountSaveDataJournalSize", "DeviceSaveDataSize",
            "DeviceSaveDataJournalSize", "UserAccountSaveDataSizeMax",
            "UserAccountSaveDataJournalSizeMax", "DeviceSaveDataSizeMax",
            "DeviceSaveDataJournalSizeMax", "TemporaryStorageSize", "CacheStorageSize",
            "CacheStorageJournalSize", "CacheStorageDataAndJournalSizeMax",
            "CacheStorageIndexMax", "BcatDeliveryCacheStorageSize", "BcatPassphrase",
            "SaveDataCertificateBytes", "StreamPlaySaveDataKeyType",
        }),
        ("Add-On Content & Runtime", new[]
        {
            "AddOnContentRegistrationType", "RuntimeAddOnContentInstall",
            "RuntimeParameterDelivery", "RuntimeUpgrade",
            "RequiredAddOnContentsSetBinaryDescriptor", "SupportedExtraAddOnContentFlag",
            "ContentsAvailabilityTransitionPolicy", "SupportingLimitedApplicationLicenses",
            "RepairFlag", "RequiredNetworkServiceLicenseOnLaunchFlag", "TitlesDataFormat",
            "ApplicationControlDataCondition",
        }),
        ("Features & Platform", new[]
        {
            "HasInGameVoiceChat", "HasKaraokeFeature", "SuppressForceConsoleMode",
            "JitConfiguration", "NeighborDetectionClientConfiguration", "PlatformSpecificRegion",
        }),
        ("Reserved", Array.Empty<string>()),   // filled by name prefix
        ("Other", Array.Empty<string>()),      // safety net: nothing is ever dropped
    };

    int SectionOf(Field f)
    {
        if (f.Name.StartsWith("Reserved")) return Sections.Length - 2;
        for (int i = 0; i < Sections.Length - 2; i++)
            if (Sections[i].Names.Contains(f.Name)) return i;
        return Sections.Length - 1;
    }

    // ------------------------------------------------------------ public entry
    public void Print(IReadOnlyList<FieldResult> results, string source)
    {
        Banner(results, source);

        var groups = results.GroupBy(r => SectionOf(r.Field)).OrderBy(g => g.Key);
        foreach (var g in groups)
        {
            SectionHeader(Sections[g.Key].Title);
            foreach (var r in g.OrderBy(r => r.Field.Offset))
                Row(r);
            Console.WriteLine();
        }
        Console.WriteLine(Dim($"{results.Count} fields shown.  Offsets are relative to the start of the 0x4000-byte NACP."));
        Console.WriteLine();
    }

    void Banner(IReadOnlyList<FieldResult> results, string source)
    {
        string line = new('═', Width);
        Console.WriteLine(Head("╔" + line[1..^1] + "╗"));
        // Console.WriteLine(Head("║ ") + Bold("NACP") + "  " + Dim("·") + "  " + source);
		// Inner width = Width - 2 border characters. Visible text is measured without colour codes.
		string plain = $" NACP  ·  {source}";
		int inner = Width - 2;
		if (plain.Length > inner)   // long names: truncate so the box stays intact
		{
			source = source[..Math.Max(0, source.Length - (plain.Length - inner + 1))] + "…";
			plain = $" NACP  ·  {source}";
		}
		string padding = new(' ', Math.Max(0, inner - plain.Length));

		Console.WriteLine(Head("║") + " " + Bold("NACP") + "  " + Dim("·") + "  " + source + padding + Head("║"));
		//
		
		
        Console.WriteLine(Head("╚" + line[1..^1] + "╝"));

        var titles = (List<Dictionary<string, string>>)results.First(r => r.Field.Name == "Title").Json!;
        var first = titles.FirstOrDefault(t => t["name"].Length > 0);
        string version = (string)results.First(r => r.Field.Name == "DisplayVersion").Json!;
        ulong owner = (ulong)results.First(r => r.Field.Name == "SaveDataOwnerId").Json!;

        Console.WriteLine();
        if (first is not null)
        {
            Console.WriteLine($"  {Dim("Game      ")}  {Bold(first["name"])}");
            Console.WriteLine($"  {Dim("Publisher ")}  {first["publisher"]}");
        }
        if (version.Length > 0)
            Console.WriteLine($"  {Dim("Version   ")}  {version}");
        Console.WriteLine($"  {Dim("Save owner")}  0x{owner:X16}");
        Console.WriteLine();
    }

    void SectionHeader(string title)
    {
        string text = $"── {title} ";
        Console.WriteLine(Head(text + new string('─', Math.Max(0, Width - text.Length))));
    }

    // ------------------------------------------------------------ rows
    void Row(FieldResult r)
    {
        var f = r.Field;
        var (inline, block) = Render(r);

        int pad = NameCol - f.Name.Length;
        string lead = pad > 1 ? " " + Dim(new string('.', pad - 1)) : " ";
        string tag = f.Since.Length > 0 ? "  " + Dim($"[{f.Since}]") : "";

        Console.WriteLine($"  {Dim($"0x{f.Offset:X4}")}  {Bold(f.Name)}{lead} {inline}{tag}");
        foreach (var l in block) Console.WriteLine(Indent + l);
    }

    (string Inline, List<string> Block) Render(FieldResult r)
    {
        var f = r.Field;
        var block = new List<string>();

        switch (f.Kind)
        {
            case Kind.Str:
            {
                var s = (string)r.Json!;
                return (s.Length == 0 ? Dim("(empty)") : Val($"\"{s}\""), block);
            }
            case Kind.U8:
            case Kind.U16:
            case Kind.U32:
            {
                ulong v = (ulong)r.Json!;
                return (v == 0 ? Dim("0") : Val(v.ToString()) + (v > 9 ? Dim($"  (0x{v:X})") : ""), block);
            }
            case Kind.U32Hex:
            {
                ulong v = (ulong)r.Json!;
                return (v == 0 ? Dim($"0x{v:X8}") : Val($"0x{v:X8}"), block);
            }
            case Kind.U64Hex:
            {
                ulong v = (ulong)r.Json!;
                return (v == 0 ? Dim($"0x{v:X16}") : Val($"0x{v:X16}"), block);
            }
            case Kind.Size:
            {
                ulong v = (ulong)r.Json!;
                if (v == 0) return (Dim("0"), block);
                return (Val(HSize(v)) + Dim($"  (0x{v:X})"), block);
            }
            case Kind.Enum:
            {
                int v = (int)r.Json!;
                bool known = f.Map!.TryGetValue(v, out var name);
                return ((known ? Val(name!) : Warn("Unknown")) + Dim($"  ({v})"), block);
            }
            case Kind.Flags:
            {
                var d = (Dictionary<string, object>)r.Json!;
                var set = (List<string>)d["set"];
                ulong v = (ulong)d["value"];
                if (set.Count == 0) return (Dim("none") + Dim($"  (0x{v:X})"), block);
                string joined = string.Join(", ", set);
                if (set.Count <= 3 && joined.Length <= 50)
                    return (Val(joined) + Dim($"  (0x{v:X})"), block);
                foreach (var l in Wrap(set, Width - Indent.Length)) block.Add(Val(l));
                return (Dim($"{set.Count} set  (0x{v:X})"), block);
            }
            case Kind.Hex:
                return HexValue(Convert.FromHexString((string)r.Json!));
            case Kind.U64List:
            {
                var vals = (List<ulong>)r.Json!;
                var used = vals.Select((v, i) => (v, i)).Where(x => x.v != 0).ToList();
                foreach (var (v, i) in used) block.Add($"{Dim($"[{i}]")} {Val($"0x{v:X16}")}");
                return (used.Count == 0 ? Dim("(none)") : Dim($"{used.Count} of {vals.Count} used"), block);
            }
            case Kind.U16List:
            {
                var vals = (List<ushort>)r.Json!;
                var used = vals.Select((v, i) => (v, i)).Where(x => x.v != 0).ToList();
                foreach (var (v, i) in used) block.Add($"{Dim($"[{i}]")} {Val($"0x{v:X4}")}");
                return (used.Count == 0 ? Dim("(none)") : Dim($"{used.Count} of {vals.Count} used"), block);
            }
            case Kind.Ratings:
            {
                var d = (Dictionary<string, int>)r.Json!;
                var rated = d.Where(kv => kv.Value >= 0).ToList();
                foreach (var kv in rated) block.Add($"{kv.Key,-14}{Val(kv.Value.ToString())}");
                return (rated.Count == 0
                    ? Dim("not rated by any organization")
                    : Dim($"{rated.Count} of {d.Count} organizations"), block);
            }
            case Kind.Jit:
            {
                var d = (Dictionary<string, object>)r.Json!;
                bool enabled = (bool)d["enabled"];
                ulong mem = (ulong)d["memory_size"];
                block.Add($"{Dim("Flags      ")} 0x{(ulong)d["flags"]:X}");
                block.Add($"{Dim("MemorySize ")} {(mem == 0 ? Dim("0") : Val(HSize(mem)) + Dim($"  (0x{mem:X})"))}");
                return (enabled ? Val("Enabled") : Dim("Disabled"), block);
            }
            case Kind.Titles:
                return TitlesValue((List<Dictionary<string, string>>)r.Json!, r.Text);
        }
        throw new InvalidOperationException($"Unhandled kind {f.Kind}");
    }

    (string, List<string>) TitlesValue(List<Dictionary<string, string>> titles, string note)
    {
        var block = new List<string>();
        var filled = titles.Where(t => t["name"].Length > 0 || t["publisher"].Length > 0).ToList();

        // Group identical name/publisher pairs so the same title isn't repeated 10 times.
        foreach (var grp in filled.GroupBy(t => (t["name"], t["publisher"])))
        {
            if (block.Count > 0) block.Add("");
            block.Add(Bold(grp.Key.Item1.Length > 0 ? grp.Key.Item1 : "(no name)"));
            block.Add($"{Dim("by")} {(grp.Key.Item2.Length > 0 ? grp.Key.Item2 : "(no publisher)")}");
            foreach (var l in Wrap(grp.Select(t => t["language"]), Width - Indent.Length - 2))
                block.Add(Dim("» " + l));
        }

        var empty = titles.Where(t => t["name"].Length == 0 && t["publisher"].Length == 0)
                          .Select(t => t["language"]).ToList();
        if (empty.Count > 0)
        {
            if (block.Count > 0) block.Add("");
            block.Add(Dim($"No title set for {empty.Count} of {titles.Count} languages:"));
            foreach (var l in Wrap(empty, Width - Indent.Length - 2)) block.Add(Dim("  " + l));
        }
        if (note.Length > 0)
        {
            if (block.Count > 0) block.Add("");
            block.Add(note.StartsWith("WARNING") ? Warn(note) : Dim(note));
        }
        return (Dim($"{filled.Count} of {titles.Count} languages set"), block);
    }

    (string, List<string>) HexValue(byte[] raw)
    {
        var block = new List<string>();
        if (raw.All(b => b == 0))
            return (Dim(raw.Length == 1 ? "0x00" : $"(all zero, {raw.Length} bytes)"), block);
        if (raw.Length <= 8)
            return (Val(string.Join(' ', raw.Select(b => b.ToString("x2")))), block);
        for (int i = 0; i < raw.Length; i += 16)
            block.Add(Val(string.Join(' ', raw.Skip(i).Take(16).Select(b => b.ToString("x2")))));
        return (Dim($"{raw.Length} bytes"), block);
    }

    // ------------------------------------------------------------ helpers
    static IEnumerable<string> Wrap(IEnumerable<string> items, int width)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            if (sb.Length > 0 && sb.Length + item.Length + 2 > width)
            {
                yield return sb.ToString();
                sb.Clear();
            }
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(item);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    static string HSize(ulong n)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };
        double v = n;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {units[i]}";
    }
}
