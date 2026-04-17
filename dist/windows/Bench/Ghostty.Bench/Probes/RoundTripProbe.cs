using Ghostty.Bench.Harness;
using Ghostty.Bench.Output;
using Ghostty.Bench.Transports;

namespace Ghostty.Bench.Probes;

public sealed class RoundTripProbe : Probe
{
    private const int WarmupIterations = 100;
    private const int MeasureIterations = 1000;

    public RoundTripProbe(string name, string transport)
    {
        Name = name;
        TransportLabel = transport;
    }

    public override string Name { get; }
    public string TransportLabel { get; }

    public override ResultJson Run(ITransport transport, HostInfo host, DateTime timestampUtc)
    {
        long[] ticks = global::Ghostty.Bench.Harness.Harness.RunRoundTrip(
            transport,
            warmup: WarmupIterations,
            samples: MeasureIterations);

        Array.Sort(ticks);

        double p50 = global::Ghostty.Bench.Harness.Harness.TicksToMicroseconds(Percentiles.Of(ticks, 50));
        double p95 = global::Ghostty.Bench.Harness.Harness.TicksToMicroseconds(Percentiles.Of(ticks, 95));
        double p99 = global::Ghostty.Bench.Harness.Harness.TicksToMicroseconds(Percentiles.Of(ticks, 99));

        return ResultJson.RoundTrip(
            probe: Name,
            transport: TransportLabel,
            p50Us: Math.Round(p50, 2),
            p95Us: Math.Round(p95, 2),
            p99Us: Math.Round(p99, 2),
            samples: MeasureIterations,
            warmup: WarmupIterations,
            host: host,
            timestampUtc: timestampUtc);
    }
}
