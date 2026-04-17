using System.Runtime.InteropServices;
using Ghostty.Bench.Output;
using Ghostty.Bench.Probes;
using Ghostty.Bench.Transports;

namespace Ghostty.Bench;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage(Console.Error);
            return 64;
        }

        string probeName = args[0];
        string? outPath = null;
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--out") outPath = args[i + 1];
        }

        if (!PreflightOk(out string preflightErr))
        {
            Console.Error.WriteLine(preflightErr);
            return 1;
        }

        // Wall-clock watchdog per spec Section 9.3. 30s for round-trip.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Console.Error.WriteLine($"watchdog: {probeName} exceeded 30s deadline");
            Environment.Exit(2);
        });

        try
        {
            string childExe = ResolveChildExe();
            ResultJson? result = probeName switch
            {
                "conpty-roundtrip" => RunRoundTripProbe("conpty_roundtrip", "conpty", childExe),
                "direct-pipe-roundtrip" => RunRoundTripProbe("direct_pipe_roundtrip", "direct_pipe", childExe),
                _ => null,
            };

            if (result is null)
            {
                Console.Error.WriteLine($"unknown probe: {probeName}");
                PrintUsage(Console.Error);
                return 64;
            }

            string json = ResultJson.SerializeIndented(result);
            if (outPath is null)
            {
                Console.Out.WriteLine(json);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                File.WriteAllText(outPath, json);
            }
            return 0;
        }
        catch (TransportException ex)
        {
            Console.Error.WriteLine($"transport error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unhandled: {ex}");
            return 1;
        }
    }

    private static ResultJson RunRoundTripProbe(string probeName, string transportLabel, string childExe)
    {
        using ITransport transport = transportLabel switch
        {
            "conpty" => new ConPtyTransport(childExe),
            "direct_pipe" => new DirectPipeTransport(childExe),
            _ => throw new TransportException($"unknown transport label: {transportLabel}"),
        };

        var probe = new RoundTripProbe(probeName, transportLabel);
        return probe.Run(transport, HostInfo.Capture(), DateTime.UtcNow);
    }

    private static bool PreflightOk(out string err)
    {
        if (!OperatingSystem.IsWindows())
        {
            err = "error: Ghostty.Bench is Windows-only";
            return false;
        }
        var v = Environment.OSVersion.Version;
        if (v.Major < 10 || v.Build < 17763)
        {
            err = $"error: ConPTY requires Windows 10 1809 (build 17763) or newer; detected build {v.Build}";
            return false;
        }
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            err = "error: Ghostty.Bench PR 1 supports x64 only";
            return false;
        }
        err = "";
        return true;
    }

    private static string ResolveChildExe()
    {
        string? baseDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(baseDir, "Ghostty.Bench.EchoChild.exe");
        if (!File.Exists(candidate))
        {
            throw new TransportException($"child binary not found at: {candidate}");
        }
        return candidate;
    }

    private static bool IsHelp(string s) =>
        s is "-h" or "--help" or "help";

    private static void PrintUsage(TextWriter w)
    {
        w.WriteLine("usage: Ghostty.Bench <probe> [--out <path>]");
        w.WriteLine("probes:");
        w.WriteLine("  conpty-roundtrip");
        w.WriteLine("  direct-pipe-roundtrip");
    }
}
