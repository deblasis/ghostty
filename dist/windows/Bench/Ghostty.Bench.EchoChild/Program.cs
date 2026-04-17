// Copies every byte arriving on stdin to stdout.
// When stdin is a ConPTY terminal, switches to raw mode first so bytes
// are delivered immediately without line-buffering or local echo.
// Writes 0x01 (ready sentinel) before entering the echo loop so the
// parent knows raw-mode is active and VT init noise is done.
// Spawned by Ghostty.Bench to measure transport cost independent of any real shell.
using System.Runtime.InteropServices;

const uint STD_INPUT_HANDLE = unchecked((uint)-10);
const uint ENABLE_PROCESSED_INPUT = 0x0001;
const uint ENABLE_LINE_INPUT = 0x0002;
const uint ENABLE_ECHO_INPUT = 0x0004;
const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

IntPtr hStdin = GetStdHandle(STD_INPUT_HANDLE);
if (GetConsoleMode(hStdin, out uint mode))
{
    // stdin is a console (ConPTY attached). Switch to raw mode:
    // - Remove ENABLE_LINE_INPUT  -> bytes delivered immediately, no newline needed
    // - Remove ENABLE_ECHO_INPUT  -> terminal does not echo our input back
    // - Remove ENABLE_PROCESSED_INPUT -> Ctrl+C etc. not intercepted by terminal
    // Keep ENABLE_VIRTUAL_TERMINAL_INPUT so VT sequences pass through
    uint rawMode = (mode & ~(ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT | ENABLE_PROCESSED_INPUT))
                   | ENABLE_VIRTUAL_TERMINAL_INPUT;
    SetConsoleMode(hStdin, rawMode);
}

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();

// Signal parent: raw mode is active, ready to echo.
stdout.WriteByte(0x01);
stdout.Flush();

try
{
    stdin.CopyTo(stdout);
}
catch (IOException)
{
    // Parent closed the pipe. Normal shutdown.
}

[DllImport("kernel32.dll", SetLastError = true)]
static extern IntPtr GetStdHandle(uint nStdHandle);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
