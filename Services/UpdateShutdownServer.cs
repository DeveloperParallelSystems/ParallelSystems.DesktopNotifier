using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace ParallelSystems.DesktopNotifier.Services;

// Protocol v1: prepare:<start UTC ticks> -> ready/busy/unable -> commit -> exiting.
// This source is duplicated intentionally so each product can build without a sibling repository.
public sealed class UpdateShutdownServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    public Task Completion => _loop;
    public UpdateShutdownServer(string prefix, Func<CancellationToken, Task<string>> prepare,
        Func<Task> commit, Func<Task> abort)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (string.IsNullOrEmpty(prefix) || prefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.')) throw new ArgumentException("Invalid pipe prefix.");
        using var process = Process.GetCurrentProcess();
        var ticks = process.StartTime.ToUniversalTime().Ticks;
        var name = prefix + ".Update.v1." + process.Id;
        _loop = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                var prepared = false; var committed = false;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                try
                {
                    using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    var request = await ReadLineAsync(pipe, deadline.Token);
                    if (request != "prepare:" + ticks.ToString(CultureInfo.InvariantCulture))
                    { await WriteLineAsync(pipe, "wrong-process", deadline.Token); continue; }
                    // The callback must honor cancellation while queued to its UI dispatcher.
                    prepared = true; // Abort even if the prepare callback times out after being queued.
                    var response = await prepare(deadline.Token).WaitAsync(deadline.Token);
                    if (response is not ("ready" or "busy" or "unable")) response = "unable";
                    await WriteLineAsync(pipe, response, deadline.Token);
                    if (response != "ready") continue;
                    if (await ReadLineAsync(pipe, deadline.Token) != "commit") continue;
                    committed = true;
                    // Once commit is received, loss of the acknowledgement does not revoke the request.
                    await WriteLineAsync(pipe, "exiting", deadline.Token);
                }
                catch (Exception)
                {
                    // A broken/local request must not stop the application or cause a tight retry loop.
                    if (!_stop.IsCancellationRequested) await Task.Delay(100);
                }
                finally
                {
                    deadline.Cancel();
                    if (committed) await commit();
                    else if (prepared) await abort();
                }
                if (committed) return;
            }
        });
    }
    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var bytes = new List<byte>(); var next = new byte[1];
        while (bytes.Count < 96)
        {
            if (await stream.ReadAsync(next, token) == 0) throw new IOException("Shutdown peer disconnected.");
            if (next[0] == 10) return Encoding.ASCII.GetString(bytes.ToArray());
            if (next[0] is < 32 or > 126) throw new IOException("Invalid shutdown request.");
            bytes.Add(next[0]);
        }
        throw new IOException("Shutdown request is too long.");
    }
    private static async Task WriteLineAsync(Stream stream, string value, CancellationToken token)
    { await stream.WriteAsync(Encoding.ASCII.GetBytes(value + "\n"), token); await stream.FlushAsync(token); }
    public void Stop() => _stop.Cancel();
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _loop; } finally { _stop.Dispose(); }
    }
}
