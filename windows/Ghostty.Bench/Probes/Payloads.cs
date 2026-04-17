namespace Ghostty.Bench.Probes;

// Payload factories for throughput probes. Each returns exactly
// Payloads.TargetBytes (100 MB) of content.
//
// INVARIANT (enforced by PayloadsTests.Payloads_DoNotContainTerminatorPrefix):
// no payload may contain the literal substring "~ENDOFBURST_". The throughput
// probe appends "\r\n~ENDOFBURST_<nonce>~" after each payload and scans the
// return stream for that pattern; any in-payload occurrence of the prefix
// would risk a false match and cut the measurement short. The current three
// factories satisfy this trivially (ASCII 'A', SGR+short text, DCS/OSC/APC
// sequences interleaved with " printable " markers, none contain the
// prefix). New payloads must preserve the invariant.
//
// INVARIANT (implicit, not pinned by a test): payloads must end in a
// cleanly-parsed VT state. No unterminated DCS / OSC / APC / SOS / PM
// sequence may straddle the payload -> terminator boundary, or the parser
// (conhost) could consume the terminator's leading "\r\n" as part of the
// lingering state.
public static class Payloads
{
    public const int TargetBytes = 100 * 1024 * 1024;

    // Lazy so a process that only runs round-trip probes never pays the
    // 100 MB alloc, and throughput probes that call these repeatedly
    // share a single buffer instead of churning 100 MB per sample set.
    private static readonly Lazy<ReadOnlyMemory<byte>> _ascii = new(BuildAscii, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<ReadOnlyMemory<byte>> _sgr = new(BuildSgr, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<ReadOnlyMemory<byte>> _stress = new(BuildStress, LazyThreadSafetyMode.ExecutionAndPublication);

    // 100 MB of uppercase 'A' (0x41). ConPTY's parser sees printables
    // only and takes the fast path; useful as a floor measurement.
    public static ReadOnlyMemory<byte> Ascii100Mb() => _ascii.Value;

    // 100 MB of "\x1b[31mhello\x1b[0m " repeated. Tests SGR parser
    // cost under realistic shell output.
    public static ReadOnlyMemory<byte> Sgr100Mb() => _sgr.Value;

    // 100 MB of dense DCS/OSC/APC sequences interleaved with short
    // printable runs. Pathological ConPTY parser load.
    public static ReadOnlyMemory<byte> Stress100Mb() => _stress.Value;

    private static ReadOnlyMemory<byte> BuildAscii()
    {
        byte[] buf = new byte[TargetBytes];
        Array.Fill(buf, (byte)'A');
        return buf;
    }

    private static ReadOnlyMemory<byte> BuildSgr()
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

    private static ReadOnlyMemory<byte> BuildStress()
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
