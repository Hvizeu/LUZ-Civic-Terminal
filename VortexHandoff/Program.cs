using System.Diagnostics;
using Luz;

try
{
    if (args.Length != 2 || args[0] != "--vortex-sync") throw new InvalidDataException("Use --vortex-sync followed by an absolute snapshot path.");
    string message = VortexBridge.Message(args[1]);
    string root = HostPlatform.DataRoot();
    if (await LinkInbox.Forward(root, message, 500)) return 0;
    string executable = Path.Combine(AppContext.BaseDirectory, "LUZ Civic Terminal.exe");
    if (!File.Exists(executable)) throw new FileNotFoundException("The LUZ application beside the handoff launcher is missing.", executable);
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
    // Send once after startup: the helper never opens the registry or owns its lock.
    using var child = Process.Start(start) ?? throw new IOException("LUZ could not be started.");
    var deadline = DateTime.UtcNow.AddSeconds(60);
    while (DateTime.UtcNow < deadline)
    {
        if (await LinkInbox.Forward(root, message, 1000)) return 0;
        if (child.HasExited && child.ExitCode != 0) throw new IOException("LUZ exited before receiving the Vortex request.");
        await Task.Delay(150);
    }
    throw new IOException("LUZ has not acknowledged this request. Retry from Vortex after reviewing LUZ startup.");
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
