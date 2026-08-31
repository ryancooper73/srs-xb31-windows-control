using Xb31.Core;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        bool verifyFrame = args.Contains("--verify-frame", StringComparer.OrdinalIgnoreCase);
        bool probe = args.Contains("--probe", StringComparer.OrdinalIgnoreCase);
        bool verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        if (args.Any(arg => !new[] { "--verify-frame", "--probe", "--verbose" }.Contains(arg, StringComparer.OrdinalIgnoreCase)) ||
            (verifyFrame && probe))
        {
            Console.Error.WriteLine("XB31: invalid arguments");
            return 14;
        }

        if (verifyFrame)
        {
            Console.WriteLine(Convert.ToHexString(Xb31Commands.PowerOffFrame()).ToLowerInvariant());
            return 0;
        }

        var client = new Xb31Client(Console.WriteLine);
        Xb31Result result = probe
            ? await client.ProbeAsync()
            : await client.PowerOffAsync();

        if (result.Status is Xb31Status.Success or Xb31Status.CleanupFailed)
        {
            Console.WriteLine(probe
                ? "XB31: probe complete; no data sent"
                : "XB31: power-off frame sent");
        }

        if (!result.IsSuccess)
        {
            Console.Error.WriteLine(result.Status switch
            {
                Xb31Status.Unavailable => "XB31: unavailable",
                Xb31Status.ConnectionFailed => "XB31: RFCOMM connection failed",
                Xb31Status.WriteFailed => "XB31: write failed",
                Xb31Status.Timeout => "XB31: RFCOMM timeout",
                Xb31Status.CleanupFailed => "XB31: cleanup failed",
                _ => "XB31: unexpected helper failure"
            });

            if (verbose && result.Diagnostic is not null)
            {
                Console.Error.WriteLine(result.Diagnostic);
            }
        }

        return ExitCode(result.Status);
    }

    private static int ExitCode(Xb31Status status) => status switch
    {
        Xb31Status.Success => 0,
        Xb31Status.Unavailable => 10,
        Xb31Status.ConnectionFailed => 11,
        Xb31Status.WriteFailed => 12,
        Xb31Status.Timeout => 13,
        Xb31Status.CleanupFailed => 14,
        _ => 14
    };
}
