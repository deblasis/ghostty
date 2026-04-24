using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ghostty.Core.Profiles;

/// <summary>
/// Production IProcessRunner. Wraps System.Diagnostics.Process with a
/// hard timeout and cancellation. Never shells out via cmd; fileName
/// and args are passed verbatim to ProcessStartInfo. Stdout/stderr are
/// captured as UTF-8 strings; probes that need UTF-16 (wsl --list) read
/// RawStdoutBytes from a dedicated overload via a probe-local path
/// (WslProbe handles its own UTF-16 decoding; see Task 6).
/// </summary>
internal sealed class WindowsProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var sw = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
                return new ProcessResult(-1, "", "", sw.Elapsed);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // File not found / not executable. Match the interface
            // contract: ExitCode = -1 means "did not start".
            return new ProcessResult(-1, "", "", sw.Elapsed);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        // Start reads before WaitForExitAsync; reversing the order can
        // deadlock when the process fills the output pipe before exiting.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            // Drain the stream tasks so they don't run as unobserved continuations
            // against the disposed Process. Kill closes the pipes so these return
            // immediately.
            try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); }
            catch { }
            return new ProcessResult(-1, "", "", sw.Elapsed);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, stdout, stderr, sw.Elapsed);
    }
}
