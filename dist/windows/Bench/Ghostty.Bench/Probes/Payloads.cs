namespace Ghostty.Bench.Probes;

public static class Payloads
{
    public const int TargetBytes = 100 * 1024 * 1024;

    // 100 MB of uppercase 'A' (0x41). ConPTY's parser sees printables
    // only and takes the fast path; useful as a floor measurement.
    public static ReadOnlyMemory<byte> Ascii100Mb()
    {
        byte[] buf = new byte[TargetBytes];
        Array.Fill(buf, (byte)'A');
        return buf;
    }

    // 100 MB of "\x1b[31mhello\x1b[0m " repeated. Tests SGR parser
    // cost under realistic shell output.
    public static ReadOnlyMemory<byte> Sgr100Mb()
    {
        ReadOnlySpan<byte> unit = "\x1b[31mhello\x1b[0m "u8;
        byte[] buf = new byte[TargetBytes];
        int written = 0;
        while (written + unit.Length <= TargetBytes)
        {
            unit.CopyTo(buf.AsSpan(written));
            written += unit.Length;
        }
        // Pad remainder with 'A' so every byte is defined and the
        // total is exactly 100 MB.
        while (written < TargetBytes)
        {
            buf[written++] = (byte)'A';
        }
        return buf;
    }

    // 100 MB of dense DCS/OSC/APC sequences interleaved with short
    // printable runs. Pathological ConPTY parser load.
    public static ReadOnlyMemory<byte> Stress100Mb()
    {
        // One repeating chunk contains each introducer at a known offset.
        // DCS: ESC P ... ESC \
        // OSC: ESC ] 0 ; title BEL
        // APC: ESC _ ... ESC \
        ReadOnlySpan<byte> chunk = "\x1bPtest\x1b\\\x1b]0;title\x07\x1b_data\x1b\\printable "u8;
        byte[] buf = new byte[TargetBytes];
        int written = 0;
        while (written + chunk.Length <= TargetBytes)
        {
            chunk.CopyTo(buf.AsSpan(written));
            written += chunk.Length;
        }
        while (written < TargetBytes)
        {
            buf[written++] = (byte)' ';
        }
        return buf;
    }
}
