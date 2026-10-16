using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NACPinfo;

const string Usage = """
    NACPinfo - print every field of the NACP in an NSP, XCI, or raw control.nacp

    Usage:
      NACPinfo <input> [--json] [--no-color] [--no-txt] [--extract <file>] [--pause]
      NACPinfo --drop [<input>] [--json] [--no-color] [--no-txt]

    Options:
      --drop     Interactive mode: drag files into the window and press Enter,
                 repeat as often as you like. Empty line or 'q' quits.
      --no-txt   Don't write the <input>.txt report next to the input file
      --pause    Wait for a keypress before exiting (auto-enabled when the
                 window was opened by drag-and-drop / double-click)

    Notes:
      prod.keys must be in the current working directory.

    Examples:
      NACPinfo game.nsp
      NACPinfo game.xci
      NACPinfo control.nacp --json
      NACPinfo game.nsp --extract control.nacp
      NACPinfo --drop

    Shortcut: point a shortcut at NACPinfo.exe --drop and set "Start in" to the
    folder containing prod.keys.
    """;

Console.OutputEncoding = Encoding.UTF8;

string? input = null, extractPath = null;
bool json = false, noColor = false, noTxt = false, forcePause = false, dropMode = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--json": json = true; break;
        case "--no-color": noColor = true; break;
        case "--no-txt": noTxt = true; break;
        case "--drop": dropMode = true; break;
        case "--pause": forcePause = true; break;
        case "--extract": extractPath = i + 1 < args.Length ? args[++i] : null; break;
        case "-h" or "--help": Console.WriteLine(Usage); return 0;
        default: input ??= args[i]; break;
    }
}

// If we own our console window (launched from Explorer / drag-and-drop), it will
// vanish on exit, so wait for a keypress. Drop mode has its own prompt loop.
bool pause = !dropMode && (forcePause || OwnsConsoleWindow());

try
{
    return dropMode ? DropLoop() : Run(input);
}
finally
{
    if (pause && !Console.IsInputRedirected)
    {
        Console.WriteLine();
        Console.Write("Press any key to exit...");
        Console.ReadKey(true);
    }
}

// ---------------------------------------------------------------------------
int DropLoop()
{
    Console.WriteLine("Drag XCI/NCA files into this window and press Enter.");
    Console.WriteLine("Enter 'q' or an empty line to quit.");

    // A path given on the command line is processed first.
    if (input is not null) RunSafe(input);

    while (true)
    {
        Console.WriteLine();
        Console.Write("> ");
        string? line = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(line) || line.Equals("q", StringComparison.OrdinalIgnoreCase))
            return 0;

        foreach (var p in SplitPaths(line))
            RunSafe(p);
    }
}

void RunSafe(string path)
{
    Console.WriteLine();
    Console.WriteLine($"=== {Path.GetFileName(path)} ===");
    try { Run(path); }
    catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); }
}

/// <summary>Splits a dropped line like  "C:\a b\x.nsp" "C:\y.xci"  into paths.</summary>
static List<string> SplitPaths(string line)
{
    // Whole line is a single existing file (covers unquoted paths with spaces).
    string whole = line.Trim('"');
    if (File.Exists(whole)) return new() { whole };

    var list = new List<string>();
    var sb = new StringBuilder();
    bool inQuotes = false;

    void Flush() { if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); } }

    foreach (char c in line)
    {
        if (c == '"') { inQuotes = !inQuotes; if (!inQuotes) Flush(); continue; }
        if (char.IsWhiteSpace(c) && !inQuotes) { Flush(); continue; }
        sb.Append(c);
    }
    Flush();
    return list;
}

// ---------------------------------------------------------------------------
int Run(string? path)
{
    if (path is null) { Console.Error.WriteLine(Usage); return 1; }
    if (!File.Exists(path)) { Console.Error.WriteLine($"File not found: {path}"); return 1; }

    var nacps = new List<(string Label, byte[] Data)>();

    try
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        bool isContainer = false;
        if (fs.Length >= 4)
        {
            string head = Encoding.ASCII.GetString(Containers.ReadAt(fs, 0, 4));
            isContainer = head is "PFS0" or "HFS0"
                || (fs.Length > 0x104 && Encoding.ASCII.GetString(Containers.ReadAt(fs, 0x100, 4)) == "HEAD");
        }

        if (!isContainer && (fs.Length == NacpParser.Size || path.EndsWith(".nacp", StringComparison.OrdinalIgnoreCase)))
        {
            nacps.Add((Path.GetFileName(path), Containers.ReadAt(fs, 0, (int)fs.Length)));
        }
        else
        {
            var keys = Keys.Find();
            if (keys is null)
            {
                Console.Error.WriteLine("Could not find prod.keys in the working directory.");
                return 1;
            }

            var (files, kind) = Containers.Collect(fs);

            var tickets = new Dictionary<string, byte[]>();
            foreach (var f in files.Where(f => f.Name.EndsWith(".tik", StringComparison.OrdinalIgnoreCase)))
                if (Containers.ParseTicket(Containers.ReadAt(fs, f.Offset, (int)f.Size)) is { } t)
                    tickets[t.RightsId] = t.EncTitleKey;

            var ncas = files.Where(f => f.Name.EndsWith(".nca", StringComparison.OrdinalIgnoreCase)).ToList();
            if (ncas.Count == 0)
            {
                Console.Error.WriteLine("No .nca files found (compressed .ncz is not supported).");
                return 1;
            }

            var errors = new List<string>();
            foreach (var nca in ncas)
            {
                try
                {
                    if (Nca.ExtractNacp(fs, nca, keys, tickets) is { } data)
                        nacps.Add(($"{kind}: {nca.Name}", data));
                }
                catch (Exception ex)
                {
                    errors.Add($"{nca.Name}: {ex.Message}");
                }
            }

            if (nacps.Count == 0)
            {
                Console.Error.WriteLine("No control NCA with a readable NACP found. Possibly DLC.");
                foreach (var e in errors.Take(5)) Console.Error.WriteLine("  " + e);
                return 1;
            }
        }
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }

    // In drop mode several files are processed, so a single --extract target is ignored.
    if (extractPath is not null && !dropMode)
        File.WriteAllBytes(extractPath, nacps[0].Data);

    string report;   // plain-text copy of everything shown, for the .txt file

    if (json)
    {
        var doc = nacps.Select(n => new Dictionary<string, object?>
        {
            ["source"] = n.Label,
            ["fields"] = NacpParser.Parse(n.Data).Select(r => new Dictionary<string, object?>
            {
                ["offset"] = $"0x{r.Field.Offset:X4}",
                ["size"] = $"0x{r.Field.Size:X}",
                ["name"] = r.Field.Name,
                ["since"] = r.Field.Since.Length > 0 ? r.Field.Since : null,
                ["value"] = r.Json,
            }).ToList(),
        }).ToList();

        var opts = new JsonSerializerOptions { WriteIndented = true };
        report = doc.Count == 1 ? JsonSerializer.Serialize(doc[0], opts) : JsonSerializer.Serialize(doc, opts);
        Console.WriteLine(report);
    }
    else
    {
        bool color = !noColor && !Console.IsOutputRedirected
                     && Environment.GetEnvironmentVariable("NO_COLOR") is null;

        var printer = new Printer(color);
        foreach (var (label, data) in nacps)
            printer.Print(NacpParser.Parse(data), label);

        // Re-render without colour codes into a buffer for the txt file.
        var sw = new StringWriter();
        var original = Console.Out;
        Console.SetOut(sw);
        try
        {
            var plain = new Printer(false);
            foreach (var (label, data) in nacps)
                plain.Print(NacpParser.Parse(data), label);
        }
        finally { Console.SetOut(original); }
        report = sw.ToString();
    }

    if (!noTxt)
    {
        string txtPath = Path.ChangeExtension(Path.GetFullPath(path), ".txt");
        try
        {
            File.WriteAllText(txtPath, report, new UTF8Encoding(true));
            // keep stdout clean when emitting JSON
            (json ? Console.Error : Console.Out).WriteLine($"Saved report: {txtPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write {txtPath}: {ex.Message}");
        }
    }
    return 0;
}

static bool OwnsConsoleWindow()
{
    if (!OperatingSystem.IsWindows()) return false;
    try
    {
        // Only our own process attached => console was created for us (Explorer launch / drop).
        var list = new uint[4];
        return Native.GetConsoleProcessList(list, (uint)list.Length) == 1;
    }
    catch { return false; }
}

static class Native
{
    [DllImport("kernel32.dll")]
    public static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
}
