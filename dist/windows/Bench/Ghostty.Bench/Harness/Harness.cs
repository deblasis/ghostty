using System.Diagnostics;
using Ghostty.Bench.Transports;

namespace Ghostty.Bench.Harness;

public static class Harness
{
    // Performs `warmup` untimed single-byte round-trips, then `samples`
    // timed round-trips, and returns the timings array in Stopwatch
    // ticks. Caller converts to microseconds via
    // (ticks * 1_000_000.0 / Stopwatch.Frequency).
    public static long[] RunRoundTrip(ITransport transport, int warmup, int samples)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 1);

        Span<byte> writeBuf = stackalloc byte[1];
        writeBuf[0] = 0x42;
        Span<byte> readBuf = stackalloc byte[1];

        for (int i = 0; i < warmup; i++)
        {
            SingleByteRoundTrip(transport, writeBuf, readBuf);
        }

        long[] timings = new long[samples];
        for (int i = 0; i < samples; i++)
        {
            long start = Stopwatch.GetTimestamp();
            SingleByteRoundTrip(transport, writeBuf, readBuf);
            long end = Stopwatch.GetTimestamp();
            timings[i] = end - start;
        }

        return timings;
    }

    private static void SingleByteRoundTrip(ITransport t, ReadOnlySpan<byte> writeBuf, Span<byte> readBuf)
    {
        t.Input.Write(writeBuf);
        t.Input.Flush();
        int read = t.Output.Read(readBuf);
        if (read != 1)
        {
            throw new IOException($"expected 1 byte on round-trip read, got {read}");
        }
    }

    // Reads exactly `total` bytes into `buffer` from `stream`, blocking.
    // Used by ThroughputProbe; placed here to keep all the "careful IO"
    // helpers in one file.
    public static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer[read..]);
            if (n == 0) throw new EndOfStreamException("peer closed before all bytes read");
            read += n;
        }
    }

    public static double TicksToMicroseconds(long ticks) =>
        ticks * 1_000_000.0 / Stopwatch.Frequency;
}
