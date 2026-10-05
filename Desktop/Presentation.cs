using System.IO;

namespace LuzDesktop;
public sealed partial class MainWindow
{

    private Button applyButton = null!, playButton = null!;
    private Button QuietButton(string text, Action action, string? tip = null)
    {
        var button = Button(text, action, tip); button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.Foreground = TerminalTheme.Muted; button.HorizontalAlignment = HorizontalAlignment.Left; button.HorizontalContentAlignment = HorizontalAlignment.Left; return button;
    }
    private Button ActionsMenu(string text, params (string Text, Func<Task> Action)[] actions)
    {
        var menu = new ContextMenu { Background = TerminalTheme.Panel, Foreground = TerminalTheme.Text, BorderBrush = TerminalTheme.Edge };
        foreach (var (label, action) in actions) { var item = new MenuItem { Header = label, Padding = new Thickness(12, 8, 12, 8) }; item.Click += (_, _) => Run(action); menu.Items.Add(item); }
        Button? button = null; button = QuietButton(text + " ▾", () => menu.Open(button!)); button.ContextMenu = menu; return button;
    }
    private void RefreshProfileStatus()
    {
        bool applied = library.State.AppliedProfileId == library.Active.Id && library.State.AppliedFingerprint == library.Fingerprint(library.Active);
        bool located = library.State.GameFolder.Length > 0;
        bool interrupted = File.Exists(Path.Combine(library.Root, "pending.json")) || File.Exists(Path.Combine(library.Root, "loader-pending.json"));
        int errors = Luz.ModPlanner.Check(library.Active, library.State.Packages).Count(i => i.Severity == "Error");
        profileLabel.Text = $"{library.Active.Mods.Count(m => m.Enabled)} of {library.Active.Mods.Count} mods enabled\n\n" + (interrupted ? "Recovery needed.\nOpen Maintenance." : !located ? "Choose your game folder\nin Maintenance." : errors > 0 ? errors + (errors == 1 ? " blocking issue." : " blocking issues.") + "\nReview mod details." : applied ? "Applied to the game." : "Unapplied changes.\nApply before playing.");
        profileLabel.Foreground = interrupted || errors > 0 ? TerminalTheme.Brush("#FFA788") : applied ? TerminalTheme.Teal : TerminalTheme.Muted;
        applyButton.Background = applied ? TerminalTheme.Panel : TerminalTheme.Gold; applyButton.Foreground = applied ? TerminalTheme.Muted : TerminalTheme.Ink;
        applyButton.IsEnabled = located && !applied && !interrupted && errors == 0;
        ToolTip.SetTip(applyButton, !located ? "Choose your game folder in Maintenance first." : interrupted ? "Recover the interrupted installation in Maintenance." : errors > 0 ? "Resolve the blocking issues shown in mod details." : applied ? "The current profile is already applied." : "Back up the current installation and apply this profile.");
        playButton.Background = applied ? TerminalTheme.Gold : TerminalTheme.Panel; playButton.Foreground = applied ? TerminalTheme.Ink : TerminalTheme.Muted;
        bool launchReady = Luz.HostPlatform.Current != Luz.DesktopPlatform.MacOS || library.State.LaunchProgram.Length > 0;
        playButton.IsEnabled = located && applied && !interrupted && errors == 0 && launchReady;
        ToolTip.SetTip(playButton, !launchReady ? "Configure your working Wine/CrossOver launcher in Maintenance." : !located ? "Choose your game folder in Maintenance first." : errors > 0 ? "Resolve the blocking issues shown in mod details." : !applied ? "Apply this profile before playing." : interrupted ? "Recover the interrupted installation first." : "Launch Nivalis through Steam.");
    }
}
