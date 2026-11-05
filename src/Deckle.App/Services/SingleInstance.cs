using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Deckle.App.Services;

/// <summary>
/// Keeps one Deckle per user. A second launch (a shortcut, the jump list, the dock menu) hands its request to the
/// running instance over a named pipe and exits. The pipe is local and limited to the current user.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string ScanRequest = "scan";
    public const string ShowRequest = "show";

    private readonly CancellationTokenSource _cts = new();
    private readonly string _pipeName;

    /// <param name="pipeName">Only tests pass one; the default is per user.</param>
    public SingleInstance(string? pipeName = null)
    {
        _pipeName = pipeName ?? DefaultPipeName;
    }

    private static string DefaultPipeName
    {
        get
        {
            var user = new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray());
            return "deckle-" + (user.Length > 0 ? user : "user");
        }
    }

    /// <summary>Sends <paramref name="request"/> to a running instance. False when none is running.</summary>
    public static bool TrySend(string request, string? pipeName = null)
    {
        var bytes = Encoding.UTF8.GetBytes(request + "\n");
        // A client can connect just as the running instance swaps listeners and go unheard, so a request only
        // counts once the instance acknowledges it.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName ?? DefaultPipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
                client.Connect(TimeSpan.FromMilliseconds(400));
                if (OperatingSystem.IsWindows())
                {
                    // We were started by the user, so we may bring a window to the front; pass that right on.
                    AllowSetForegroundWindow(-1);
                }
                client.Write(bytes);
                client.Flush();
                var ack = new byte[1];
                var read = client.ReadAsync(ack).AsTask();
                if (read.Wait(TimeSpan.FromSeconds(2)) && read.Result == 1)
                {
                    return true;
                }
            }
            catch (TimeoutException)
            {
                return false; // Nothing is listening: no instance running.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AggregateException)
            {
            }
        }
        return false;
    }

    /// <summary>Accepts requests from later launches; <paramref name="onRequest"/> runs on a background thread.</summary>
    public void Listen(Action<string> onRequest)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                    var line = await reader.ReadLineAsync(_cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), _cts.Token);
                    server.WriteByte(1);
                    server.Flush();
                    if (line is ScanRequest or ShowRequest)
                    {
                        onRequest(line);
                    }
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
                {
                    // Another instance owns the pipe, or a client went away mid-message. Don't spin.
                    try
                    {
                        await Task.Delay(1000, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        });
    }

    public void Dispose() => _cts.Cancel();

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
