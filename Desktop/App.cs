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
            catch (Exception ex)
            {
                desktop.MainWindow = new StartupRecoveryWindow(HostPlatform.DataRoot(), ex, () => {
                    var previous = desktop.MainWindow;
                    library = new Library(HostPlatform.DataRoot());
                    main = new MainWindow(library); desktop.MainWindow = main;
                    inbox = new LinkInbox(library.Root, address => Dispatcher.UIThread.Post(() => main.ReceiveLink(address)), message => Dispatcher.UIThread.Post(() => main.ReceiveLinkError(message)));
                    main.Show(); previous?.Close();
                });
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
    [STAThread] public static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--compatibility-worker" })) return CompatibilityWorker.RunChild();
        if (args.Length >= 2 && args[0] == "--corpsec") return CorpSecCommand.Run(args, HostPlatform.DataRoot());
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
            string builds = Path.Combine(root, "PreviewBuilds"), projects = Path.Combine(root, "authoring", "projects"); Directory.CreateDirectory(Path.Combine(builds, "Better Time")); Directory.CreateDirectory(Path.Combine(builds, "City Life"));
            var betterTime = new CorpSecProject { Id = new string('A', 64), Input = Path.Combine(builds, "Better Time"), ProjectRoot = builds, Workspace = Path.Combine(root, "authoring", "workspaces", new string('A', 64)), Owner = "hvizeu.nivalis.slowdays", DisplayName = "Better Time", Version = "2.2.8", Author = "Hvizeu", Description = "Time controls and safe waiting.", SupportContact = "Nexus Better Time page", PluginGuid = "hvizeu.nivalis.slowdays", PluginAssembly = "NivalisSlowDays", PluginFile = "NivalisSlowDays.dll", PackageFolder = "better-time", ThunderstoreName = "Better_Time", ProjectFile = Path.Combine(projects, new string('A', 64) + ".json"), Capabilities = [new("module", "native", [], [])], PackageOutput = Path.Combine(root, "PreviewPackages") };
            var cityLife = new CorpSecProject { Id = new string('B', 64), Input = Path.Combine(builds, "City Life"), ProjectRoot = builds, Workspace = Path.Combine(root, "authoring", "workspaces", new string('B', 64)), Owner = "hvizeu.nivalis.citylife", DisplayName = "City Life", Version = "1.1.3", Author = "Hvizeu", Description = "City activities and citizen life.", SupportContact = "Nexus City Life page", PluginGuid = "hvizeu.nivalis.citylife", PluginAssembly = "NivalisCityLife", PluginFile = "NivalisCityLife.dll", PackageFolder = "city-life", ThunderstoreName = "City_Life", ProjectFile = Path.Combine(projects, new string('B', 64) + ".json"), Capabilities = [new("module", "native", [], [])] };
            CorpSec.Save(betterTime); CorpSec.Save(cityLife);
            string[] extraProjects = ["Behavior Inspector", "Furniture Lab", "Greenhouse", "Night Market Workshop", "Object Framework", "Object Framework Interaction Example", "Performance Fix", "Tracked Quests HUD", "Unofficial Patch", "Use Oldest First"];
            for (int index = 0; index < extraProjects.Length; index++)
            {
                string name = extraProjects[index], id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("preview." + name))), input = Path.Combine(builds, name); Directory.CreateDirectory(input);
                CorpSec.Save(new() { Id = id, Input = input, ProjectRoot = builds, Workspace = Path.Combine(root, "authoring", "workspaces", id), Owner = "preview.mod." + index, DisplayName = name, Version = "1.0." + index, Author = "Preview Author", PluginGuid = "preview.mod." + index, PluginAssembly = "PreviewMod" + index, PluginFile = "PreviewMod" + index + ".dll", PackageFolder = "preview-mod-" + index, ThunderstoreName = "Preview_Mod_" + index, ProjectFile = Path.Combine(projects, id + ".json"), Capabilities = [new("module", "native", [], [])] });
            }
            CorpSec.RememberSelection(library.Root, betterTime.Id);
            var window = new MainWindow(library, true); window.Show();
            foreach (var page in new[] { "Registry", "Catalogue", "Maintenance", "Registry-search", "Registry-empty", "Registry-disabled", "Registry-only-disabled", "Registry-only-enabled", "Registry-issues", "Registry-applied", "Maintenance-recovery-loader", "Maintenance-recovery-profile", "Maintenance-recovery-both", "Maintenance-recovery-missing", "CorpSec-Project", "CorpSec-Support", "CorpSec-Validation", "CorpSec-Package" })
            foreach (int width in new[] { 1440, 1100 })
            {
                window.Width = width; window.Height = width == 1100 ? 720 : 900; window.PreviewPage(page); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame."); frame.Save(Path.Combine(output, page.ToLowerInvariant() + "-" + width + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                if (page == "Maintenance")
                {
                    window.PreviewMaintenanceBottom(); Dispatcher.UIThread.RunJobs();
                    using var bottom = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No maintenance frame."); bottom.Save(Path.Combine(output, "maintenance-browser-" + width + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                if (page == "CorpSec-Project")
                {
                    window.PreviewCorpProjectBottom(); Dispatcher.UIThread.RunJobs();
                    using var bottom = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No CorpSec project frame."); bottom.Save(Path.Combine(output, "corpsec-project-bottom-" + width + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
            }
            window.PreviewCheckInteractions(Path.Combine(output, "presentation-checks.txt")); CivicIcons.Export(Path.Combine(output, "vectors")); window.Close();
            var greenhouse = new ModPackage { Name = "Nivalis Greenhouse", Version = "2.0.2", Files = ["plugins/NivalisGreenhouse/NivalisGreenhouse.dll"] };
            library.State.Packages.Add(greenhouse); library.Active.Mods.Add(new() { PackageId = greenhouse.Id });
            CompatibilityReport warning = new(1, "Play", new string('A', 64), CompatibilityStatus.Unverified,
                [new("DynamicContractsUnverified", "Warning", greenhouse.Files[0], "Declared native/reflection capabilities", "No maintained compatibility metadata; only compiled references checked", true)]);
            var warningOwner = new MainWindow(library, true); warningOwner.Show();
            var warningWindow = warningOwner.PreviewCompatibilityPrompt(CompatibilityGuidance.Create(library, warning)); Dispatcher.UIThread.RunJobs();
            using var warningFrame = warningWindow.CaptureRenderedFrame() ?? throw new InvalidOperationException("No compatibility warning frame.");
            warningFrame.Save(Path.Combine(output, "compatibility-warning.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            warningWindow.Close(); warningOwner.Close();
            var installer = new InstallerWindow(); installer.Show(); Dispatcher.UIThread.RunJobs();
            using var installerFrame = installer.CaptureRenderedFrame() ?? throw new InvalidOperationException("No installer frame.");
            installerFrame.Save(Path.Combine(output, "installer.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            installer.Close();
            var recoveryError = new RegistryRecoveryException(RegistryFailure.EmptyProfiles, Path.Combine(root, "registry.json"), "The registry has no usable profiles.");
            var recoveryWindow = new StartupRecoveryWindow(root, recoveryError); recoveryWindow.Show(); Dispatcher.UIThread.RunJobs();
            using var recoveryFrame = recoveryWindow.CaptureRenderedFrame() ?? throw new InvalidOperationException("No recovery frame.");
            recoveryFrame.Save(Path.Combine(output, "startup-recovery.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            recoveryWindow.Close();
        }
        FileSafety.DeleteOwned(FileSafety.ResolveFolder(Path.GetTempPath()), root);
    }
#endif
}
