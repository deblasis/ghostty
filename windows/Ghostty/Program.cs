using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ghostty;

/// <summary>
/// Custom entry point that wraps the WinUI 3 startup with diagnostic
/// error capture. The XAML-generated Main is suppressed via
/// DISABLE_XAML_GENERATED_MAIN. This is temporary for debugging
/// NativeAOT startup crashes that produce no output.
/// </summary>
public static partial class Program
{
    #region Win32 Console Interop

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_INPUT_HANDLE = -10;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
    private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;
    private const uint ENABLE_WINDOW_INPUT = 0x0008;
    private const uint ENABLE_MOUSE_INPUT = 0x0010;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    private const ushort KEY_EVENT = 0x0001;
    private const ushort MOUSE_EVENT = 0x0002;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint OPEN_EXISTING = 3;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [LibraryImport("kernel32.dll", EntryPoint = "WriteConsoleW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteConsole(
        IntPtr hConsoleOutput, string lpBuffer, uint nNumberOfCharsToWrite,
        out uint lpNumberOfCharsWritten, IntPtr lpReserved);

    [LibraryImport("kernel32.dll", EntryPoint = "ReadConsoleInputW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadConsoleInput(
        IntPtr hConsoleInput, out INPUT_RECORD lpBuffer,
        uint nLength, out uint lpNumberOfEventsRead);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleScreenBufferInfo(
        IntPtr hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
        [FieldOffset(4)] public MOUSE_EVENT_RECORD MouseEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public int bKeyDown;
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_EVENT_RECORD
    {
        public short X, Y;
        public uint dwButtonState;
        public uint dwControlKeyState;
        public uint dwEventFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public short dwSizeX, dwSizeY;
        public short dwCursorX, dwCursorY;
        public ushort wAttributes;
        public short srWindowLeft, srWindowTop, srWindowRight, srWindowBottom;
        public short dwMaximumWindowSizeX, dwMaximumWindowSizeY;
    }

    // Console helpers that bypass .NET Console class entirely.
    private static IntPtr _hOut, _hIn;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteFile(
        IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    private static void ConWrite(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        WriteFile(_hOut, bytes, (uint)bytes.Length, out _, IntPtr.Zero);
    }

    private static void StdWrite(string text) => ConWrite(text);

    private static byte ReadByte()
    {
        var buf = new byte[1];
        ReadFile(_hIn, buf, 1, out _, IntPtr.Zero);
        return buf[0];
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadFile(
        IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    private static (int w, int h) ConSize()
    {
        // Try Win32 API first (works on real consoles).
        if (GetConsoleScreenBufferInfo(_hOut, out var info))
            return (info.srWindowRight - info.srWindowLeft + 1,
                    info.srWindowBottom - info.srWindowTop + 1);
        // Fallback for ConPTY: use environment or default.
        int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var cols);
        int.TryParse(Environment.GetEnvironmentVariable("LINES"), out var rows);
        return (cols > 0 ? cols : 120, rows > 0 ? rows : 30);
    }

    #endregion

    [STAThread]
    static int Main(string[] args)
    {
        // Handle CLI actions before WinUI startup.
        if (args.Length > 0 && args[0].StartsWith('+'))
        {
            // Use the inherited stdio handles directly. When launched
            // from a ConPTY terminal (Ghostty, Windows Terminal), these
            // are VT pipes. AttachConsole would replace them with Win32
            // console handles that don't work inside ConPTY.
            _hOut = GetStdHandle(STD_OUTPUT_HANDLE);
            _hIn = GetStdHandle(STD_INPUT_HANDLE);

            // Try to enable VT processing (works on real consoles,
            // no-op on ConPTY pipes which already support VT).
            if (GetConsoleMode(_hOut, out var outMode))
                SetConsoleMode(_hOut, outMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING | ENABLE_PROCESSED_OUTPUT);

            return args[0] switch
            {
                "+list-themes" => ListThemes(args),
                "+version" => ShowVersion(),
                _ => UnknownAction(args[0]),
            };
        }

        try
        {
            Console.Error.WriteLine("[Ghostty] Program.Main entered");
            Console.Error.Flush();

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Console.Error.WriteLine("[Ghostty] ComWrappers initialized");
            Console.Error.Flush();

            Microsoft.UI.Xaml.Application.Start(p =>
            {
                Console.Error.WriteLine("[Ghostty] Application.Start callback entered");
                Console.Error.Flush();

                var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);

                Console.Error.WriteLine("[Ghostty] Creating App instance");
                Console.Error.Flush();

                new App();

                Console.Error.WriteLine("[Ghostty] App instance created");
                Console.Error.Flush();
            });

            return 0;
        }
        catch (Exception ex)
        {
            var msg = $"[Ghostty] FATAL: {ex}";
            Console.Error.WriteLine(msg);
            Console.Error.Flush();

            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "ghostty-crash.log");
                File.WriteAllText(logPath, msg);
            }
            catch { /* best effort */ }

            return 1;
        }
    }

    #region Theme Discovery

    private static SortedDictionary<string, string> DiscoverThemes()
    {
        var themes = new SortedDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        var themesDir = Path.Combine(appData, "ghostty", "themes");

        if (Directory.Exists(themesDir))
        {
            foreach (var file in Directory.EnumerateFiles(themesDir))
            {
                var name = Path.GetFileName(file);
                if (name is not null) themes.TryAdd(name, file);
            }
        }
        return themes;
    }

    private static string GetConfigFilePath()
    {
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "ghostty", "config");
    }

    private static string? ReadCurrentTheme()
    {
        var configPath = GetConfigFilePath();
        if (!File.Exists(configPath)) return null;
        foreach (var line in File.ReadLines(configPath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) continue;
            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue;
            if (trimmed[..eq].Trim() == "theme")
                return trimmed[(eq + 1)..].Trim();
        }
        return null;
    }

    private static void WriteThemeToConfig(string themeName)
    {
        var configPath = GetConfigFilePath();
        if (!File.Exists(configPath)) return;

        var lines = File.ReadAllLines(configPath);
        var found = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith('#')) continue;
            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue;
            if (trimmed[..eq].Trim() == "theme")
            {
                lines[i] = $"theme = {themeName}";
                found = true;
                break;
            }
        }
        if (!found)
        {
            var list = new List<string>(lines) { $"theme = {themeName}" };
            lines = list.ToArray();
        }
        File.WriteAllLines(configPath, lines);
    }

    private static (uint[] palette, uint fg, uint bg, uint? cursor) ParseThemeColors(string path)
    {
        uint[] palette = new uint[16];
        uint[] defaults = [
            0x000000, 0xCC0000, 0x00CC00, 0xCCCC00,
            0x0000CC, 0xCC00CC, 0x00CCCC, 0xCCCCCC,
            0x666666, 0xFF0000, 0x00FF00, 0xFFFF00,
            0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
        ];
        Array.Copy(defaults, palette, 16);
        uint fg = 0xCCCCCC, bg = 0x000000;
        uint? cursorColor = null;

        if (!File.Exists(path)) return (palette, fg, bg, cursorColor);

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) continue;
            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue;
            var key = trimmed[..eq].Trim();
            var val = trimmed[(eq + 1)..].Trim();

            if (key == "foreground") { if (TryParseHex(val, out var c)) fg = c; }
            else if (key == "background") { if (TryParseHex(val, out var c)) bg = c; }
            else if (key == "cursor-color") { if (TryParseHex(val, out var c)) cursorColor = c; }
            else if (key == "palette")
            {
                var peq = val.IndexOf('=');
                if (peq < 0) continue;
                if (int.TryParse(val[..peq].Trim(), out var idx) && idx is >= 0 and < 16)
                    if (TryParseHex(val[(peq + 1)..].Trim(), out var c)) palette[idx] = c;
            }
        }
        return (palette, fg, bg, cursorColor);
    }

    private static bool TryParseHex(string s, out uint color)
    {
        color = 0;
        if (string.IsNullOrEmpty(s)) return false;
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out color))
            return true;
        return false;
    }

    #endregion

    #region CLI Actions

    private static int ListThemes(string[] args)
    {
        var themes = DiscoverThemes();
        if (themes.Count == 0)
        {
            StdWrite("No themes found.\r\n");
            return 1;
        }

        var plain = args.Contains("--plain");
        var showPath = args.Contains("--path");

        if (plain)
        {
            foreach (var (name, path) in themes)
                StdWrite((showPath ? path : name) + "\r\n");
            return 0;
        }

        return ThemeTui(themes);
    }

    private static int ShowVersion()
    {
        StdWrite("Ghostty (Windows)\r\n");
        return 0;
    }

    private static int UnknownAction(string action)
    {
        StdWrite($"Unknown action: {action}\r\n");
        StdWrite("Available actions: +list-themes, +version\r\n");
        return 1;
    }

    #endregion

    #region Theme TUI

    private static string Fg(uint c) =>
        $"\x1b[38;2;{c >> 16};{(c >> 8) & 0xFF};{c & 0xFF}m";

    private static string Bg(uint c) =>
        $"\x1b[48;2;{c >> 16};{(c >> 8) & 0xFF};{c & 0xFF}m";

    private static int ThemeTui(SortedDictionary<string, string> themes)
    {
        var names = themes.Keys.ToList();
        var paths = themes.Values.ToList();
        var originalTheme = ReadCurrentTheme() ?? "";
        var cursor = 0;
        var scroll = 0;
        var search = "";
        var filtered = Enumerable.Range(0, names.Count).ToList();
        var searching = false;
        var showHelp = false;

        // Start at the current theme.
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], originalTheme, StringComparison.OrdinalIgnoreCase))
            { cursor = i; break; }
        }

        // Alt screen, hide cursor, enable SGR mouse.
        ConWrite("\x1b[?1049h\x1b[?25l\x1b[?1000h\x1b[?1006h");

        try
        {
            var needsRedraw = true;

            while (true)
            {
                if (needsRedraw)
                {
                    DrawTui(names, paths, filtered, cursor, scroll,
                        search, searching, showHelp, originalTheme);
                    needsRedraw = false;
                }

                // Read VT input (works inside ConPTY and real consoles).
                var b = ReadByte();
                string key;

                if (b == 0x1B) // ESC - start of escape sequence
                {
                    var b2 = ReadByte();
                    if (b2 == '[') // CSI
                    {
                        // Read until we get a letter or ~
                        var seq = "";
                        while (true)
                        {
                            var b3 = ReadByte();
                            seq += (char)b3;
                            if ((b3 >= 0x40 && b3 <= 0x7E) && b3 != ';')
                                break;
                        }
                        key = seq switch
                        {
                            "A" => "UP",
                            "B" => "DOWN",
                            "H" => "HOME",
                            "F" => "END",
                            "5~" => "PGUP",
                            "6~" => "PGDN",
                            _ when seq.StartsWith("<") => "MOUSE:" + seq,
                            _ => "CSI:" + seq,
                        };
                    }
                    else if (b2 == 'O') // SS3
                    {
                        var b3 = ReadByte();
                        key = b3 switch
                        {
                            (byte)'P' => "F1",
                            _ => "SS3:" + (char)b3,
                        };
                    }
                    else
                    {
                        key = "ESC";
                    }
                }
                else if (b == 0x0D) key = "ENTER";
                else if (b == 0x7F || b == 0x08) key = "BS";
                else if (b >= 0x20 && b < 0x7F) key = ((char)b).ToString();
                else key = $"CTRL:{b}";

                if (showHelp)
                {
                    showHelp = false;
                    needsRedraw = true;
                    continue;
                }

                if (searching)
                {
                    if (key == "ESC")
                    {
                        searching = false;
                        search = "";
                        Refilter();
                    }
                    else if (key == "ENTER")
                    {
                        searching = false;
                    }
                    else if (key == "BS")
                    {
                        if (search.Length > 0)
                            search = search[..^1];
                        Refilter();
                    }
                    else if (key.Length == 1 && !char.IsControl(key[0]))
                    {
                        search += key;
                        Refilter();
                    }
                    needsRedraw = true;
                    continue;
                }

                var prevCursor = cursor;
                var (_, conH) = ConSize();

                if (key == "ESC" || key == "q")
                {
                    if (!string.IsNullOrEmpty(originalTheme))
                        WriteThemeToConfig(originalTheme);
                    return 0;
                }
                else if (key == "ENTER")
                    return 0;
                else if (key == "UP" || key == "k")
                { if (cursor > 0) cursor--; }
                else if (key == "DOWN" || key == "j")
                { if (cursor < filtered.Count - 1) cursor++; }
                else if (key == "PGUP")
                    cursor = Math.Max(0, cursor - (conH - 4));
                else if (key == "PGDN")
                    cursor = Math.Min(filtered.Count - 1, cursor + (conH - 4));
                else if (key == "HOME")
                    cursor = 0;
                else if (key == "END")
                    cursor = filtered.Count - 1;
                else if (key == "F1" || key == "?")
                    showHelp = true;
                else if (key == "/")
                { searching = true; search = ""; }
                else if (key.StartsWith("MOUSE:"))
                {
                    // SGR mouse: <btn;x;yM or <btn;x;ym
                    var parts = key[7..].TrimEnd('M', 'm').Split(';');
                    if (parts.Length == 3 &&
                        int.TryParse(parts[0], out var btn) &&
                        int.TryParse(parts[1], out var mx) &&
                        int.TryParse(parts[2], out var my))
                    {
                        var (conW, _) = ConSize();
                        var listW2 = Math.Min(35, conW / 3);
                        if (btn == 64 && cursor > 0) cursor--; // scroll up
                        else if (btn == 65 && cursor < filtered.Count - 1) cursor++; // scroll down
                        else if (btn == 0 && mx <= listW2 && my >= 2) // left click
                        {
                            var clickIdx = scroll + my - 2;
                            if (clickIdx >= 0 && clickIdx < filtered.Count)
                                cursor = clickIdx;
                        }
                    }
                }

                if (cursor != prevCursor)
                    ApplyThemeAtCursor();

                needsRedraw = true;
            }
        }
        finally
        {
            // Disable mouse, show cursor, exit alt screen.
            ConWrite("\x1b[?1006l\x1b[?1000l\x1b[?25h\x1b[?1049l");
        }

        void Refilter()
        {
            if (string.IsNullOrEmpty(search))
                filtered = Enumerable.Range(0, names.Count).ToList();
            else
                filtered = Enumerable.Range(0, names.Count)
                    .Where(i => names[i].Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            cursor = 0;
            scroll = 0;
        }

        void ApplyThemeAtCursor()
        {
            if (cursor < 0 || cursor >= filtered.Count) return;
            WriteThemeToConfig(names[filtered[cursor]]);
        }
    }

    private static void DrawTui(
        List<string> names, List<string> paths,
        List<int> filtered, int cursor, int scroll,
        string search, bool searching, bool showHelp,
        string originalTheme)
    {
        var (w, h) = ConSize();
        var listW = Math.Min(35, w / 3);
        var prevL = listW + 3;
        var listH = h - 3;

        // Adjust scroll.
        if (cursor < scroll) scroll = cursor;
        if (cursor >= scroll + listH) scroll = cursor - listH + 1;

        var buf = new System.Text.StringBuilder(4096);
        buf.Append("\x1b[2J\x1b[H"); // clear

        // Header bar.
        string header;
        if (searching)
            header = $" /{search}\x1b[5m_\x1b[25m";
        else
            header = $" Ghostty Themes ({filtered.Count})";
        buf.Append($"\x1b[7m{header}");
        // Pad to width, accounting for escape sequences.
        var visLen = searching ? 2 + search.Length + 1 : header.Length;
        if (visLen < w) buf.Append(new string(' ', w - visLen));
        buf.Append("\x1b[0m");

        if (showHelp)
        {
            DrawHelp(buf, w, h);
            Console.Write(buf.ToString());
            Console.Out.Flush();
            return;
        }

        // Theme list.
        for (int row = 0; row < listH && scroll + row < filtered.Count; row++)
        {
            var idx = filtered[scroll + row];
            var name = names[idx];
            var isCur = (scroll + row) == cursor;
            var isOrig = string.Equals(name, originalTheme, StringComparison.OrdinalIgnoreCase);
            var marker = isOrig ? "*" : " ";
            var display = name.Length > listW - 3
                ? name[..(listW - 6)] + "..."
                : name;

            buf.Append($"\x1b[{row + 2};1H");
            if (isCur)
                buf.Append($"\x1b[7m{marker}{display.PadRight(listW - 2)}\x1b[0m");
            else
                buf.Append($"\x1b[2m{marker}\x1b[22m{display.PadRight(listW - 2)}");
        }

        // Vertical separator.
        for (int row = 0; row < listH + 1; row++)
            buf.Append($"\x1b[{row + 1};{listW + 1}H\x1b[2m|\x1b[22m");

        // Preview.
        if (cursor >= 0 && cursor < filtered.Count)
        {
            var idx = filtered[cursor];
            var (pal, fg, bg, cc) = ParseThemeColors(paths[idx]);
            // color238 is a dark gray for line numbers/borders
            uint c238 = 0x444444;
            var s = $"{Fg(fg)}{Bg(bg)}"; // standard style
            var s238 = $"{Fg(c238)}{Bg(bg)}";
            var s5 = $"{Fg(pal[5])}{Bg(bg)}"; // magenta - keywords
            var s4 = $"{Fg(pal[4])}{Bg(bg)}"; // blue - numbers/fn
            var s2 = $"{Fg(pal[2])}{Bg(bg)}"; // green - strings
            var s10 = $"{Fg(pal[10])}{Bg(bg)}"; // bright green
            var s12 = $"{Fg(pal[12])}{Bg(bg)}"; // bright blue - types
            var s6 = $"{Fg(pal[6])}{Bg(bg)}"; // cyan - functions

            // Theme name centered and bold.
            buf.Append($"\x1b[2;{prevL}H{Bg(bg)}\x1b[1;3m{Fg(fg)}{names[idx]}\x1b[0m");

            // Palette grid: 8 columns, 2 rows of colored blocks.
            int palRow = 4;
            for (int i = 0; i < 16; i++)
            {
                var r = i / 8;
                var c = i % 8;
                buf.Append($"\x1b[{palRow + r * 2};{prevL + c * 6}H");
                buf.Append($"{Fg(pal[i])}{Bg(bg)}");
                buf.Append($"{i,3} ");
                buf.Append($"{Fg(pal[i])}\u2588\u2588");
                buf.Append("\x1b[0m");
                buf.Append($"\x1b[{palRow + r * 2 + 1};{prevL + c * 6 + 4}H");
                buf.Append($"{Fg(pal[i])}{Bg(bg)}\u2588\u2588\x1b[0m");
            }

            // bat-style code sample.
            int codeRow = palRow + 5;
            var sepW = Math.Max(0, w - prevL - 10);
            var sep = new string('\u2500', sepW);

            // -> bat ziggzagg.zig
            buf.Append($"\x1b[{codeRow};{prevL}H{Bg(bg)}");
            buf.Append($"{Fg(pal[2])}\u2192 ");
            buf.Append($"{Fg(pal[4])}bat ");
            buf.Append($"{Fg(pal[6])}\x1b[4mziggzagg.zig\x1b[24m");
            buf.Append("\x1b[0m");

            // Top border
            buf.Append($"\x1b[{codeRow + 1};{prevL}H{s238}\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u252c{sep}\x1b[0m");

            // File header
            buf.Append($"\x1b[{codeRow + 2};{prevL}H{s238}       \u2502 {s}File: \x1b[1m{Fg(fg)}{Bg(bg)}ziggzagg.zig\x1b[22m\x1b[0m");

            // Mid border
            buf.Append($"\x1b[{codeRow + 3};{prevL}H{s238}\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u253c{sep}\x1b[0m");

            // Line 1: const std = @import("std");
            buf.Append($"\x1b[{codeRow + 4};{prevL}H{s238}   1   \u2502 {s5}const{s} std {s5}= @import{s}({s10}\"std\"{s});\x1b[0m");

            // Line 2: (empty)
            buf.Append($"\x1b[{codeRow + 5};{prevL}H{s238}   2   \u2502\x1b[0m");

            // Line 3: pub fn main() !void {
            buf.Append($"\x1b[{codeRow + 6};{prevL}H{s238}   3   \u2502 {s5}pub {s12}fn {s2}main{s}() {s5}!{s12}void{s} {{\x1b[0m");

            // Line 4: const stdout = ...
            buf.Append($"\x1b[{codeRow + 7};{prevL}H{s238}   4   \u2502     {s5}const {s}stdout {s5}={s} std.Io.getStdOut().writer();\x1b[0m");

            // Line 5: var i: usize = 1;
            buf.Append($"\x1b[{codeRow + 8};{prevL}H{s238}   5   \u2502     {s5}var {s}i:{s12} usize{s5} ={s4} 1{s};\x1b[0m");

            // Line 6: while (i <= 16) : (i += 1) {
            buf.Append($"\x1b[{codeRow + 9};{prevL}H{s238}   6   \u2502     {s5}while {s}(i {s5}<= {s4}16{s}) : (i {s5}+= {s4}1{s}) {{\x1b[0m");

            // Line 7: if (i % 15 == 0) {
            buf.Append($"\x1b[{codeRow + 10};{prevL}H{s238}   7   \u2502         {s5}if {s}(i {s5}% {s4}15 {s5}== {s4}0{s}) {{\x1b[0m");

            // Line 8: try stdout.writeAll("ZiggZagg\n");
            buf.Append($"\x1b[{codeRow + 11};{prevL}H{s238}   8   \u2502             {s5}try {s}stdout.writeAll({s10}\"ZiggZagg{s12}\\n{s10}\"{s});\x1b[0m");

            // Line 9: } else if (i % 3 == 0) {
            buf.Append($"\x1b[{codeRow + 12};{prevL}H{s238}   9   \u2502         {s}}} {s5}else if {s}(i {s5}% {s4}3 {s5}== {s4}0{s}) {{\x1b[0m");

            // Line 10: try stdout.writeAll("Zigg\n");
            buf.Append($"\x1b[{codeRow + 13};{prevL}H{s238}  10   \u2502             {s5}try {s}stdout.writeAll({s10}\"Zigg{s12}\\n{s10}\"{s});\x1b[0m");

            // Line 11: } else if (i % 5 == 0) {
            buf.Append($"\x1b[{codeRow + 14};{prevL}H{s238}  11   \u2502         {s}}} {s5}else if {s}(i {s5}% {s4}5 {s5}== {s4}0{s}) {{\x1b[0m");

            // Line 12: try stdout.writeAll("Zagg\n");
            buf.Append($"\x1b[{codeRow + 15};{prevL}H{s238}  12   \u2502             {s5}try {s}stdout.writeAll({s10}\"Zagg{s12}\\n{s10}\"{s});\x1b[0m");

            // Line 13: } else {
            buf.Append($"\x1b[{codeRow + 16};{prevL}H{s238}  13   \u2502         {s}}} {s5}else {s}{{\x1b[0m");

            // Line 14: try stdout.print("{d}\n", .{i});  (with selection highlight)
            var selBg = pal[4]; // selection uses blue
            buf.Append($"\x1b[{codeRow + 17};{prevL}H{s238}  14   \u2502             {s5}try {Fg(fg)}{Bg(selBg)}stdout.print(\"{{d}}\\n\", .{{i}}){Fg(cc ?? fg)}{Bg(bg)};{s}\x1b[0m");

            // Line 15-17
            buf.Append($"\x1b[{codeRow + 18};{prevL}H{s238}  15   \u2502         {s}}}\x1b[0m");
            buf.Append($"\x1b[{codeRow + 19};{prevL}H{s238}  16   \u2502     {s}}}\x1b[0m");
            buf.Append($"\x1b[{codeRow + 20};{prevL}H{s238}  17   \u2502 {s}}}\x1b[0m");

            // Bottom border
            buf.Append($"\x1b[{codeRow + 21};{prevL}H{s238}\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2534{sep}\x1b[0m");
        }

        // Footer.
        buf.Append($"\x1b[{h};1H\x1b[2m");
        buf.Append(" [j/k] navigate  [/] search  [Enter] apply  [Esc] cancel  [F1] help");
        buf.Append("\x1b[0m");

        ConWrite(buf.ToString());
    }

    private static void DrawHelp(System.Text.StringBuilder buf, int w, int h)
    {
        var lines = new[]
        {
            "",
            "  Ghostty Theme Preview - Help",
            "",
            "  Navigation:",
            "    j / Down       Move down",
            "    k / Up         Move up",
            "    Page Down      Jump down",
            "    Page Up        Jump up",
            "    Home           First theme",
            "    End            Last theme",
            "",
            "  Actions:",
            "    Enter          Apply selected theme and exit",
            "    Esc / q        Cancel and restore original theme",
            "    /              Search themes",
            "    F1             Toggle this help",
            "",
            "  The running Ghostty app updates live as you browse.",
            "",
            "  Press any key to close help...",
        };

        for (int i = 0; i < lines.Length && i + 2 < h; i++)
        {
            buf.Append($"\x1b[{i + 3};3H");
            buf.Append(lines[i]);
        }
    }

    #endregion
}
