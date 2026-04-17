namespace Ghostty.Bench;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: Ghostty.Bench <probe> [--out <path>]");
            Console.Error.WriteLine("probes: (not yet implemented in scaffold)");
            return 64; // EX_USAGE
        }

        Console.Error.WriteLine($"Ghostty.Bench scaffold: received probe '{args[0]}', no probes implemented yet");
        return 0;
    }
}
