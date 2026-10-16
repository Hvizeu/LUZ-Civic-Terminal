using System.IO;
using Luz;

namespace LuzDesktop;

public sealed partial class MainWindow
{
    private Control ContinuityPanel()
    {
        var panel = new StackPanel();
        bool hasSession = false;
        string description;
        try { var options = ModContinuity.Options(library); hasSession = options.Enabled && options.SessionId.Length != 0; description = ModContinuity.Describe(library); }
        catch (Exception error) { description = "Continuity requires attention: " + error.Message; }
        panel.Children.Add(Label(description, brush: TerminalTheme.Muted));
        panel.Children.Add(Label("Compatibility is attempted first. Protected saves preserve a separate original checkpoint; unavailable supported features can retain dormant data.", brush: TerminalTheme.Muted));
        var actions = Row();
        actions.Children.Add(Button("Enable protected saves", EnableContinuity));
        actions.Children.Add(QuietButton("Check capabilities", ShowContinuity));
        var recover = QuietButton("Recover interrupted save", RecoverContinuity); recover.IsEnabled = hasSession; actions.Children.Add(recover);
        panel.Children.Add(actions);
        var states = Row();
        var suspend = QuietButton("Suspend supported feature", SuspendContinuity); suspend.IsEnabled = hasSession; states.Children.Add(suspend);
        var restore = QuietButton("Restore feature", RestoreContinuity); restore.IsEnabled = hasSession; states.Children.Add(restore);
        var checkpoint = QuietButton("Restore original checkpoint", RestoreContinuityCheckpoint); checkpoint.IsEnabled = hasSession; states.Children.Add(checkpoint);
        panel.Children.Add(states);
        var timeline = QuietButton("Resume protected timeline", ResumeContinuityTimeline); timeline.IsEnabled = hasSession; panel.Children.Add(timeline);
        panel.Children.Add(Label("Included with LUZ updates from GitHub. Apply installs the bundled runtime; Play verifies the session. Checkpoints are retained until explicitly managed.", 13, TerminalTheme.Muted));
        return panel;
    }
    private async Task EnableContinuity()
    {
        var path = (await OpenFiles("Select the original native save to protect", false, "*.sav")).FirstOrDefault(); if (path == null) return;
        if (!await Confirm("Enable LUZ protected saves for this profile?\n\nLUZ will add its bundled continuity runtime. Apply the profile before Play. A verified checkpoint and a separate working timeline will be created before the protected launch. Existing saves remain available in the checkpoint.")) return;
        await Busy("Configuring protected saves…", ct => Task.Run(() => { ct.ThrowIfCancellationRequested(); ModContinuity.Configure(library, path); }, ct));
    }
    private async Task ShowContinuity()
    {
        var capabilities = ModContinuity.Capabilities(library);
        await Notice("Mod continuity", ModContinuity.Describe(library) + "\n\n" + string.Join("\n", capabilities.Select(c => c.Owner + "/" + c.Name + ": " + c.State + " · " + c.Detail)));
    }
    private async Task RecoverContinuity()
    {
        if (!await Confirm("Restore the unfinished native-save and companion publication together? Externally changed files will be preserved and reported. The immutable original checkpoint is retained.")) return;
        await Busy("Recovering protected save…", ct => Task.Run(() => { ct.ThrowIfCancellationRequested(); ModContinuity.Recover(library); }, ct));
    }
    private async Task RestoreContinuityCheckpoint()
    {
        if (!await Confirm("Create a fresh protected timeline from the original verified checkpoint? Your current progress and externally changed files remain intact. The restored timeline starts at the checkpoint's historical point.")) return;
        await Busy("Restoring original checkpoint into a separate timeline…", ct => Task.Run(() => { ct.ThrowIfCancellationRequested(); ModContinuity.RestoreCheckpoint(library); }, ct));
    }
    private async Task ResumeContinuityTimeline()
    {
        var timelines = ModContinuity.Timelines(library);
        string? choice = await Pick("Resume a retained protected timeline", timelines.Select(t => new Choice(t.Id, t.Label)).ToList());
        if (choice == null) return;
        await Busy("Selecting protected timeline…", ct => Task.Run(() => { ct.ThrowIfCancellationRequested(); ModContinuity.SelectTimeline(library, choice); }, ct));
        await Notice("Timeline selected", "Play will verify the current game and mod payloads before resuming this timeline. A changed compatible payload creates a new revision; retained progress and checkpoints remain intact.");
    }
    private async Task SuspendContinuity()
    {
        string? choice = await Pick("Temporarily unavailable feature", new List<Choice> { new("bettertime/module", "Better Time"), new("citylife/module", "City Life"), new("framework/behaviours", "Object Framework custom behaviors — catalogue remains active") });
        if (choice == null || !await Confirm("Suspend this feature on the protected timeline? Its supported state will be retained dormant. Dependent functionality may be unavailable until restoration.")) return;
        await Busy("Preserving unavailable feature state…", ct => Task.Run(() => { ct.ThrowIfCancellationRequested(); ModContinuity.Suspend(library, choice); }, ct));
        await Notice("Feature preserved", "Apply the profile before playing. Unavailable gameplay modules and required dependants are disabled in this profile; supported native persistence remains installed.");
    }
    private async Task RestoreContinuity()
    {
        string? choice = await Pick("Restore compatible feature", new List<Choice> { new("bettertime", "Better Time"), new("citylife", "City Life"), new("framework", "Object Framework custom behaviors") });
        if (choice == null) return;
        await Busy("Preparing feature restoration…", ct => Task.Run(() => { ct.ThrowIfCancellationRequested(); ModContinuity.RequestRestore(library, choice); }, ct));
        await Notice("Restoration prepared", "Apply the profile, then load the protected timeline. The compatible module must acknowledge its restored state before saving becomes available. Unresolved records and the original checkpoint are retained.");
    }
}
