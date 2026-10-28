using System.Diagnostics;
using System.Text;

namespace Scanwise.Core.Processing;

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string exe,
        IEnumerable<string> args,
        IReadOnlyDictionary<string, string>? env,
        TimeSpan timeout,
        CancellationToken cancelToken)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
        timeoutCts.CancelAfter(timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            cancelToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{Path.GetFileName(exe)} did not finish within {timeout.TotalSeconds:0} s.");
        }
    }
}
