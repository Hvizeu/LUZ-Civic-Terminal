using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("Tests")]

namespace Luz;

public sealed record CompatibilitySnapshot(string Game, string ProfileFingerprint, bool Installed, SortedDictionary<string, string> Selected);

public static class CompatibilityWorker
{
    private static readonly SemaphoreSlim admission = new(1);
    public static int RunChild()
    {
        try
        {
            string input = ReadBounded(Console.In, 4 * 1024 * 1024);
            var snapshot = JsonSerializer.Deserialize<CompatibilitySnapshot>(input) ?? throw new InvalidDataException("Missing compatibility snapshot.");
            Console.Write(JsonSerializer.Serialize(Compatibility.InspectSnapshot(snapshot))); return 0;
        }
        catch (Exception error) { Console.Error.Write(error); return 1; }
    }
    internal static CompatibilityReport Run(CompatibilitySnapshot snapshot, CancellationToken cancellation,TimeSpan? maximum = null)
    {
        TimeSpan limit=maximum??TimeSpan.FromSeconds(65);
        if(limit<=TimeSpan.Zero||limit>TimeSpan.FromSeconds(65))throw new ArgumentOutOfRangeException(nameof(maximum));
        admission.Wait(cancellation);
        try
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Compatibility worker host unavailable.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
            start.ArgumentList.Add("--compatibility-worker");
            start.Environment["DOTNET_PROCESSOR_COUNT"] = "2";
            start.Environment["DOTNET_GCHeapHardLimit"] = "20000000";
            string request = JsonSerializer.Serialize(snapshot);
            if (request.Length > 4 * 1024 * 1024) throw new InvalidDataException("Compatibility request exceeds its size limit.");
            using var child = Process.Start(start) ?? throw new IOException("Could not start metadata worker.");
            var watch = Stopwatch.StartNew();
            Exception? failure=null;
            try
            {
                var output = ReadBoundedAsync(child.StandardOutput, 8 * 1024 * 1024, cancellation); var errors = ReadBoundedAsync(child.StandardError, 1024 * 1024, cancellation);
                child.StandardInput.WriteAsync(request.AsMemory(), cancellation).WaitAsync(TimeSpan.FromSeconds(10), cancellation).GetAwaiter().GetResult();
                child.StandardInput.Close();
                while (!child.WaitForExit(100))
                {
                    cancellation.ThrowIfCancellationRequested(); child.Refresh();
                    if (watch.Elapsed > limit) throw new TimeoutException("Metadata worker exceeded its "+limit.TotalSeconds+" second limit.");
                    try { if (child.WorkingSet64 > 1024L * 1024 * 1024) throw new IOException("Metadata worker exceeded 1 GiB; inspection stopped."); }
                    catch (InvalidOperationException) when (child.HasExited) { break; }
                    if (output.IsFaulted) output.GetAwaiter().GetResult();
                    if (errors.IsFaulted) errors.GetAwaiter().GetResult();
                }
                cancellation.ThrowIfCancellationRequested();
                if(watch.Elapsed>limit)throw new TimeoutException("Metadata worker exceeded its "+limit.TotalSeconds+" second limit.");
                string text = output.GetAwaiter().GetResult(), diagnostic = errors.GetAwaiter().GetResult();
                if (child.ExitCode != 0) throw new IOException("Metadata worker failed: " + diagnostic);
                if (text.Length > 8 * 1024 * 1024) throw new InvalidDataException("Compatibility report exceeds its size limit.");
                return JsonSerializer.Deserialize<CompatibilityReport>(text) ?? throw new InvalidDataException("Metadata worker returned no report.");
            }
            catch(Exception error){failure=error;throw;}
            finally
            {
                try{if(!child.HasExited){try{child.Kill(entireProcessTree:true);}catch(InvalidOperationException)when(child.HasExited){ }if(!child.WaitForExit(5000))throw new IOException("Metadata worker termination remains unresolved.");}}
                catch(Exception cleanup){if(failure!=null)throw new AggregateException("Metadata inspection and owned-worker cleanup failed.",failure,cleanup);throw;}
            }
        }
        finally { admission.Release(); }
    }
    private static string ReadBounded(TextReader reader, int limit)
    {
        var result = new System.Text.StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
        { if (result.Length + count > limit) throw new InvalidDataException("Compatibility worker stream exceeds its size limit."); result.Append(buffer, 0, count); }
        return result.ToString();
    }
    private static async Task<string> ReadBoundedAsync(TextReader reader, int limit, CancellationToken cancellation)
    {
        var result = new System.Text.StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        { if (result.Length + count > limit) throw new InvalidDataException("Compatibility worker stream exceeds its size limit."); result.Append(buffer, 0, count); }
        return result.ToString();
    }
}
