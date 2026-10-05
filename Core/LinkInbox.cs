using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Luz;

public sealed class LinkInbox : IDisposable
{
    // Desktop posts callbacks to its dispatcher. Pipe I/O must not capture that
    // dispatcher because startup and shutdown can synchronously wait for it.
    private readonly CancellationTokenSource stop = new();
    private readonly Task server;
    public static string Name(string root) => "luz-nxm-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FileSafety.ResolveFolder(root))))[..24];
    public LinkInbox(string root, Action<string> receive, Action<string> error) => server = Listen(Name(root), receive, error);
    private async Task Listen(string name, Action<string> receive, Action<string> error)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                string text = await Read(pipe, timeout.Token).ConfigureAwait(false);
                LocalImport.ValidateActivation(text);
                receive(text);
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException or DecoderFallbackException) { error("A Nexus link could not be received. Retry the browser download."); }
            catch (Exception) { error("The Nexus link receiver stopped. Restart the terminal to receive browser downloads."); break; }
        }
    }
    private static async Task<string> Read(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > 32768) throw new InvalidDataException("Invalid link message length.");
        byte[] bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return new UTF8Encoding(false, true).GetString(bytes);
    }
    public static async Task<bool> Forward(string root, string address, int milliseconds = 1200)
    {
        LocalImport.ValidateActivation(address);
        using var timeout = new CancellationTokenSource(milliseconds);
        using var pipe = new NamedPipeClientStream(".", Name(root), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            byte[] bytes = Encoding.UTF8.GetBytes(address), header = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
            await pipe.WriteAsync(header, timeout.Token).ConfigureAwait(false); await pipe.WriteAsync(bytes, timeout.Token).ConfigureAwait(false); await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            byte[] ack = new byte[1]; await pipe.ReadExactlyAsync(ack, timeout.Token).ConfigureAwait(false); return ack[0] == 1;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException) { return false; }
    }
    public void Dispose() { stop.Cancel(); try { server.GetAwaiter().GetResult(); } finally { stop.Dispose(); } }
}
