using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Luz;
#if PREVIEW_TOOL
using Avalonia.Headless;
#endif
namespace LuzDesktop;
public sealed class App : Application
{
    private Library? library;
    private LinkInbox? inbox;
    private MainWindow? main;
    private static string initialLink = "", startupError = "";
    private static bool installRequested;
    private readonly Queue<string> activations = new();
    public override void Initialize()
    {
        TerminalTheme.Install(this);
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
            lifetime.Activated += (_, e) => { if (e is ProtocolActivatedEventArgs protocol) Dispatcher.UIThread.Post(() => { if (main != null) main.ReceiveLink(protocol.Uri.OriginalString); else if (activations.Count < 20) activations.Enqueue(protocol.Uri.OriginalString); }); };
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (installRequested)
            {
                desktop.MainWindow = new InstallerWindow();
                base.OnFrameworkInitializationCompleted();
                return;
            }
            desktop.Exit += (_, _) => { try { inbox?.Dispose(); } finally { library?.Dispose(); } };
            try
            {
                if (startupError.Length > 0) throw new InvalidDataException(startupError);
                string root = FileSafety.ResolveFolder(HostPlatform.DataRoot());
                try { library = new Library(root); }
                catch (IOException)
                {
                    if (LinkInbox.Forward(root, initialLink, 5000).GetAwaiter().GetResult()) { desktop.Shutdown(); return; }
                    throw new IOException("Another LUZ window may be open. Close older versions normally, then reopen this version. Retry the Nexus download if you opened a link.");
                }
                main = new MainWindow(library); desktop.MainWindow = main;
                inbox = new LinkInbox(root, address => Dispatcher.UIThread.Post(() => main.ReceiveLink(address)), message => Dispatcher.UIThread.Post(() => main.ReceiveLinkError(message)));
                main.Opened += (_, _) => { main.ReceiveLink(initialLink); while (activations.TryDequeue(out string? address)) main.ReceiveLink(address); };
            }
            catch (Exception ex) { desktop.MainWindow = new Window { Title = "LUZ Civic Terminal could not start", Width = 600, Height = 250, Content = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) } }; }
        }
        base.OnFrameworkInitializationCompleted();
    }
    [STAThread] public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--update-restart") {
            try {
                if (!int.TryParse(args[1], out int previousPid)) throw new InvalidDataException("Invalid LUZ update restart request.");
                LauncherUpdates.WaitForPreviousProcess(previousPid);
            } catch (Exception ex) { startupError = ex.Message; }
            args = [];
        }
#if PREVIEW_TOOL
        if (args.Length == 2 && args[0] == "--render-preview")
        {
            Directory.CreateDirectory(args[1]);
            try { AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new() { UseHeadlessDrawing = false }).SetupWithoutStarting(); Preview(args[1]); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "render-error.txt"), ex.ToString()); return 1; }
        }
#endif
        installRequested = args.SequenceEqual(new[] { "--install" }) ||
            (args.Length == 0 && Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "Install LUZ");
        if (installRequested)
            return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
        try
        {
            initialLink = args.Length == 0 ? "" : args.Length == 1 ? args[0] : args.Length == 2 && args[0] == "--nxm" ? args[1] : args.Length == 2 && args[0] == "--import" ? LocalImport.Message(args[1]) : throw new InvalidDataException("Use --import followed by a project ZIP, or --nxm followed by a Nexus link.");
            LocalImport.ValidateActivation(initialLink);
            if (LinkInbox.Forward(HostPlatform.DataRoot(), initialLink, 100).GetAwaiter().GetResult()) return 0;
        }
        catch (Exception ex) { startupError = ex is InvalidDataException ? ex.Message : "The Nexus link could not be opened. Start LUZ normally and paste the link in Maintenance."; }
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
#if PREVIEW_TOOL
    private static void Preview(string output)
    {
        string root = FileSafety.ResolveFolder(Path.Combine(Path.GetTempPath(), "LuzPreview-" + Guid.NewGuid().ToString("N")));
        using (var library = new Library(root))
        {
            foreach (var (name, description) in new[] { ("Better Time", "Set the pace of your days in Nivalis."), ("Furnishings", "Custom furniture packs and blueprint support."), ("Snappable Tables", "Build continuous tables with matching edge connections.") })
            {
                var package = new ModPackage { Name = name, Description = description, Version = "1.0.0", Source = "Thunderstore", SourceId = name }; library.State.Packages.Add(package); library.Active.Mods.Add(new() { PackageId = package.Id });
            }
            var window = new MainWindow(library, true); window.Show();
            foreach (var page in new[] { "Registry", "Catalogue", "Maintenance", "Registry-search", "Registry-empty", "Registry-disabled", "Registry-issues", "Registry-applied" })
            foreach (int width in new[] { 1440, 1100 })
            {
                window.Width = width; window.Height = width == 1100 ? 720 : 900; window.PreviewPage(page); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame."); frame.Save(Path.Combine(output, page.ToLowerInvariant() + "-" + width + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                if (page == "Maintenance")
                {
                    window.PreviewMaintenanceBottom(); Dispatcher.UIThread.RunJobs();
                    using var bottom = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No maintenance frame."); bottom.Save(Path.Combine(output, "maintenance-browser-" + width + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
            }
            window.PreviewCheckInteractions(Path.Combine(output, "presentation-checks.txt")); CivicIcons.Export(Path.Combine(output, "vectors")); window.Close();
            var installer = new InstallerWindow(); installer.Show(); Dispatcher.UIThread.RunJobs();
            using var installerFrame = installer.CaptureRenderedFrame() ?? throw new InvalidOperationException("No installer frame.");
            installerFrame.Save(Path.Combine(output, "installer.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            installer.Close();
        }
        FileSafety.DeleteOwned(FileSafety.ResolveFolder(Path.GetTempPath()), root);
    }
#endif
}
