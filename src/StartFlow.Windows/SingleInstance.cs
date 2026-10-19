using System.IO.Pipes;
using System.Security.Principal;

namespace StartFlow.Windows;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipe;
    private readonly CancellationTokenSource stop = new();
    public bool IsFirst { get; }
    public SingleInstance(string? testScope = null)
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        pipe = "StartFlow-" + sid + (testScope == null ? "" : "-" + testScope);
        // The named kernel object is a lifetime lease. No thread-affine ownership is needed.
        mutex = new Mutex(false, @"Local\" + pipe, out var created); IsFirst = created;
    }
    public async Task NotifyAsync(string message)
    {
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(3000);
        await using var writer = new StreamWriter(client); await writer.WriteLineAsync(message); await writer.FlushAsync();
    }
    public async Task ListenAsync(Action<string> received)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync(stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(3), stop.Token);
                if (message is "show" or "test-exit" or "test-close" || message is { Length: < 100 } && message.StartsWith("login:", StringComparison.Ordinal)) received(message);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException) { }
        }
    }
    public void Dispose() { stop.Cancel(); mutex.Dispose(); }
}
