// Copies every byte arriving on stdin to stdout.
// When stdin is a ConPTY terminal, switches to raw mode first so bytes
// arrive immediately without conhost's line-buffering, then writes the
// "RDY" ready sentinel so the parent (Ghostty.Bench) knows conhost's
// VT preamble has been routed and raw mode is active before starting
// to measure. When stdin is a direct pipe (DirectPipeTransport),
// GetConsoleMode fails cleanly so we skip both steps and behave as a
// plain byte-for-byte echo.
//
// Spec: docs/superpowers/specs/2026-04-17-conpty-bench-probe-protocol-design.md
using System.Runtime.InteropServices;

const uint STD_INPUT_HANDLE = unchecked((uint)-10);
const uint ENABLE_PROCESSED_INPUT = 0x0001;
const uint ENABLE_LINE_INPUT = 0x0002;
const uint ENABLE_ECHO_INPUT = 0x0004;
const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

IntPtr hStdin = GetStdHandle(STD_INPUT_HANDLE);
bool isConPty = GetConsoleMode(hStdin, out uint mode);

if (isConPty)
{
    // Raw stdin: drop line-buffering and local-echo so a single non-newline
    // byte is delivered immediately to our Read. Keep VT input processing
    // on; parents may feed VT sequences as payload in future throughput work.
    uint rawMode = (mode & ~(ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT | ENABLE_PROCESSED_INPUT))
                   | ENABLE_VIRTUAL_TERMINAL_INPUT;
    if (!SetConsoleMode(hStdin, rawMode))
    {
        // Hang-fast: if we cannot switch to raw mode the parent would never
        // see writes echoed, so signal failure distinctly instead of entering
        // the copy loop and letting WaitReady hang until timeout.
        using var errStream = Console.OpenStandardOutput();
        ReadOnlySpan<byte> err = "ERR"u8;
        errStream.Write(err);
        errStream.Flush();
        Environment.Exit(3);
    }

    // RDY sentinel: 3 printable ASCII bytes. Conhost renders printable ASCII
    // literally in its VT output stream, so the parent's scan for "RDY"
    // reliably finds these bytes after the preamble. Non-printable C0 bytes
    // (e.g., 0x01) are not guaranteed to survive conhost's screen rendering.
    using (var readyStream = Console.OpenStandardOutput())
    {
        ReadOnlySpan<byte> ready = "RDY"u8;
        readyStream.Write(ready);
        readyStream.Flush();
    }
}

try
{
    using var stdin = Console.OpenStandardInput();
    using var stdout = Console.OpenStandardOutput();
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
