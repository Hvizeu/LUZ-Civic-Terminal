using Luz;
namespace LuzDesktop;

public sealed class StartupRecoveryWindow : Window
{
    public StartupRecoveryWindow(string root, Exception error, Action? retry = null)
    {
        Background = TerminalTheme.Ink;
        Title = "LUZ Civic Terminal — startup recovery"; Width = 700; Height = 420; MinWidth = 580; MinHeight = 330;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "LUZ could not open its library", FontSize = 24, Foreground = TerminalTheme.Gold });
        panel.Children.Add(new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Data folder: " + root, TextWrapping = TextWrapping.Wrap, Foreground = TerminalTheme.Muted });
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var actions = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        Button ActionButton(string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 8) };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { result.Text = ex.Message; } };
            actions.Children.Add(button); return button;
        }
        var open = new Button { Content = "Open data folder", Margin = new Thickness(0, 0, 8, 8) };
        AutomationProperties.SetName(open, "Open data folder");
        open.Click += async (_, _) => { try { await HostPlatform.Open(root); } catch (Exception ex) { result.Text = ex.Message; } }; actions.Children.Add(open);
        ActionButton("Export startup diagnostics", () => result.Text = "Diagnostics saved to " + RegistryRecovery.ExportDiagnostics(root, error));
        string? backup = null;
        try { backup = RegistryRecovery.ValidBackup(root); } catch (Exception ex) { result.Text = ex.Message; }
        var restore = ActionButton("Restore validated registry backup", () => {
            // Restoration is an explicit user action. The damaged file is copied first.
            result.Text = "Registry restored. Preserved evidence: " + RegistryRecovery.Restore(root);
            if (retry != null) retry();
        });
        restore.IsEnabled = backup != null && error is RegistryRecoveryException;
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = backup == null ? "No valid registry backup was found. Existing library files have been preserved." : "A validated registry backup is available. Restore preserves the failed registry first.", TextWrapping = TextWrapping.Wrap, Foreground = TerminalTheme.Muted });
        panel.Children.Add(result); Content = new ScrollViewer { Content = panel };
    }
}
