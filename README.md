# NACPinfo
A .NET 10 console tool that prints every field of the control.nacp (metadata/configuration file) from a Nintendo Switch NSP, XCI, or a raw control.nacp file.
Output is sectioned and color-aware, and a plain-text report is saved next to each input file.

## Requirements

- .NET 10 SDK to build (`dotnet build -c Release`)
- `prod.keys` in the **current working directory** (needed for NSP/XCI; not needed for a raw `.nacp`)

## Usage

```
NACPinfo <input> [--json] [--no-color] [--no-txt] [--extract <file>] [--pause]
NACPinfo --drop [<input>] [--json] [--no-color] [--no-txt]
```

| Option | Description |
|---|---|
| `--drop` | Interactive mode (see below). |
| `--json` | Emit JSON instead of the formatted view. |
| `--no-color` | Disable ANSI colours (`NO_COLOR` env var is also honoured). |
| `--no-txt` | Don't write the `<input>.txt` report. |
| `--extract <file>` | Save the raw `control.nacp` to `<file>`. Ignored in `--drop` mode. |
| `--pause` | Wait for a keypress before exiting. Auto-enabled when the console window was created for the process (e.g. double-click). |
| `-h`, `--help` | Show usage. |

### Examples

```
NACPinfo game.nsp
NACPinfo game.xci
NACPinfo control.nacp --json
NACPinfo game.nsp --extract control.nacp
NACPinfo --drop
```

## Drop mode (`--drop`)

Run with `--drop` and the program prompts:

```
Drag XCI/NCA files into this window and press Enter.
Enter 'q' or an empty line to quit.

>
```

Drag one or more files into the window, press **Enter**, and each file is processed in
turn. Then you get a fresh prompt. An empty line or `q` exits.

- Dropped paths arrive as a list of quoted paths, e.g. `"C:\Games\A.nsp" "C:\Games\B.xci"`.
  Quoted paths with spaces are handled. If the whole line is a single existing file
  (quoted or not), it is used as-is.
- An error on one file (missing keys, bad file, unsupported format) is reported and the
  loop continues with the next.
- A path given on the command line together with `--drop` is processed first, then the
  prompt appears.
- Each file writes its own `.txt` report next to itself (unless `--no-txt`).

### Shortcut setup (Windows)

1. Right-click `NACPinfo.exe` → **Create shortcut**.
2. Shortcut properties:
   - **Target:** `C:\path\to\NACPinfo.exe --drop`
   - **Start in:** the folder that contains `prod.keys`
3. Double-click the shortcut, drag files into the window, press Enter.

> **Note:** the parser understands the double-quoted paths produced by `cmd.exe` and the
> classic console host. PowerShell drops paths as `& 'path'` (single quotes), which are
> not parsed. A single dropped file still works if it exists as typed.

## Notes

- Compressed `.ncz` content is not supported.
- Titles stored with `TitlesDataFormat = 1` (compressed title block) are decompressed
  automatically; unknown formats fall back to the raw bytes with a warning.
- Offsets in the output are relative to the start of the 0x4000-byte NACP.

## Project layout

| File | Purpose |
|---|---|
| `Program.cs` | CLI, argument parsing, drop-mode loop, report output |
| `Containers.cs` | Key loading, crypto, PFS0/HFS0/XCI parsing, NCA → `control.nacp` |
| `NacpParser.cs` | NACP field table and decoding |
| `Printer.cs` | Sectioned console rendering |
