using Luz;
namespace LuzDesktop;
public sealed partial class MainWindow
{
    private Control PlatformSettings()
    {
        var panel = new StackPanel();
        StackPanel Group(string title) { var content = new StackPanel(); var frame = TerminalTheme.Section(title, content); frame.Margin = new Thickness(0, 16, 0, 0); panel.Children.Add(frame); return content; }
        StackPanel section;
        if (!OperatingSystem.IsWindows())
        {
            section = Group("COMPATIBILITY LAYER");
            section.Children.Add(Label("Nivalis is a Windows game. Keep the Windows x64 IL2CPP loader even when this terminal runs on Linux or macOS.", brush: TerminalTheme.Muted));
            section.Children.Add(Label("Proton: set the game's Steam Launch Options to the line below. Merge it with existing options instead of replacing options you need. For Wine/CrossOver, set winhttp to native, then builtin in the game's bottle.", brush: TerminalTheme.Muted));
            section.Children.Add(new TextBox { Text = "WINEDLLOVERRIDES=\"winhttp=n,b\" %command%", IsReadOnly = true });
        }
        section = Group("GAME LAUNCH");
        section.Children.Add(Label(OperatingSystem.IsMacOS() ? "Choose the executable or script you already use to launch Nivalis through Wine/CrossOver. Native macOS Steam cannot run the Windows game." : "Leave the custom launcher empty to launch through Steam. On Linux, enable Proton and configure the DLL override.", brush: TerminalTheme.Muted));
        section.Children.Add(Label("Custom launcher (optional on Windows/Linux)", 13, TerminalTheme.Muted));
        var program = new TextBox { Text = library.State.LaunchProgram }; section.Children.Add(program);
        section.Children.Add(Label("Arguments: one literal argument per line. No shell expansion or surrounding quotes.", 13, TerminalTheme.Muted));
        var arguments = new TextBox { Text = string.Join('\n', library.State.LaunchArguments), AcceptsReturn = true, MinHeight = 90 }; section.Children.Add(arguments);
        var launch = Row(); launch.Children.Add(QuietButton("Browse launcher", async () => { var selected = (await OpenFiles("Choose launcher executable or script", false, "*")).FirstOrDefault(); if (selected != null) program.Text = selected; }));
        launch.Children.Add(Button("Save launch settings", () => { string path = program.Text?.Trim() ?? ""; if (path.Length > 0 && (!Path.IsPathFullyQualified(path) || !File.Exists(path))) throw new InvalidDataException("Choose an existing absolute path to the launcher executable."); library.State.LaunchProgram = path; library.State.LaunchArguments = (arguments.Text ?? "").Replace("\r", "").Split('\n').Where(a => a.Length > 0).ToList(); library.Save(); RefreshProfileStatus(); status.Text = "Launch settings saved. The game starts only when you press Play."; })); section.Children.Add(launch);
        section = Group("NEXUS CONNECTION");
        section.Children.Add(Label("Store your personal API key in " + Credentials.StorageName + ", or use it only for this session. The key is never saved as plain text.", brush: TerminalTheme.Muted));
        var key = new TextBox { PasswordChar = '●', MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Stretch }; section.Children.Add(key);
        var nexus = Row(); nexus.Children.Add(Button("Save key", async () => { await credentials.Save(key.Text ?? ""); key.Text = ""; status.Text = "Nexus key stored securely."; }));
        nexus.Children.Add(QuietButton("Use for session", () => { credentials.UseForSession(key.Text ?? ""); key.Text = ""; status.Text = "Nexus key kept for this session only."; }));
        nexus.Children.Add(QuietButton("Remove key", async () => { await credentials.Remove(); status.Text = "Nexus key removed."; })); nexus.Children.Add(QuietButton("Get API key ↗", () => OpenUrl("https://www.nexusmods.com/users/myaccount?tab=api"))); section.Children.Add(nexus);
        panel.Children.Add(BrowserDownloadSettings());
        return panel;
    }
}
