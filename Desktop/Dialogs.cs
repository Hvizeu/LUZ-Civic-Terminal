using Luz;
namespace LuzDesktop;
public sealed partial class MainWindow
{
    private Button Button(string text, Func<Task> action, string? tip = null, bool primary = false, CivicSymbol? icon = null) => Button(text, () => Run(action), tip, primary, icon);
    private Button QuietButton(string text, Func<Task> action, string? tip = null) => QuietButton(text, () => Run(action), tip);
    private async void Run(Func<Task> action) { try { await action(); } catch (Exception ex) { Error(ex); } }
    private Window Dialog(string title, Control content, double height = 240) => new() { Title = title, Width = 620, Height = height, MinWidth = 420, MinHeight = 200, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = TerminalTheme.Ink, Foreground = TerminalTheme.Text, Content = content };
    private async Task<bool> Confirm(string message)
    {
        if (preview) return false;
        var panel = new DockPanel { Margin = new Thickness(22) }; var buttons = Row(); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var window = Dialog("Confirm action", panel, 410);
        buttons.Children.Add(Button("Continue", () => window.Close(true), primary: true)); buttons.Children.Add(Button("Cancel", () => window.Close(false)));
        panel.Children.Add(new ScrollViewer { Content = Label(message), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return await window.ShowDialog<bool>(this);
    }
    private async Task Notice(string title, string message)
    {
        if (preview) return;
        var panel = new DockPanel { Margin = new Thickness(22) }; var window = Dialog(title, panel, 360);
        var button = Button("Close", () => window.Close()); DockPanel.SetDock(button, Dock.Bottom); panel.Children.Add(button);
        panel.Children.Add(new ScrollViewer { Content = Label(message), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); await window.ShowDialog(this);
    }
    private async Task<string?> Prompt(string title, string initial = "")
    {
        if (preview) return null;
        var panel = new StackPanel { Margin = new Thickness(22) }; panel.Children.Add(Label(title)); var input = new TextBox { Text = initial }; panel.Children.Add(input);
        var window = Dialog(title, panel); var buttons = Row(); buttons.Children.Add(Button("Continue", () => window.Close(input.Text?.Trim()), primary: true)); buttons.Children.Add(Button("Cancel", () => window.Close())); panel.Children.Add(buttons);
        window.Opened += (_, _) => input.Focus(); return await window.ShowDialog<string?>(this);
    }
    private sealed record Choice(string Id, string Label);
    private async Task<string?> Pick(string title, List<Choice> choices)
    {
        if (preview) return null;
        var panel = new DockPanel { Margin = new Thickness(22) }; var window = Dialog(title, panel, 430);
        var list = new ListBox { ItemsSource = choices, ItemTemplate = new FuncDataTemplate<Choice>((p, _) => Label(p?.Label ?? "")), SelectedIndex = 0 };
        var buttons = Row(); buttons.Children.Add(Button("Select", () => window.Close((list.SelectedItem as Choice)?.Id), primary: true)); buttons.Children.Add(Button("Cancel", () => window.Close())); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(list);
        return await window.ShowDialog<string?>(this);
    }
    private async Task<List<string>> OpenFiles(string title, bool multiple, params string[] patterns)
    {
        if (preview) return [];
        var selected = await StorageProvider.OpenFilePickerAsync(new() { Title = title, AllowMultiple = multiple, FileTypeFilter = [new FilePickerFileType("Supported files") { Patterns = patterns }] });
        return selected.Select(p => p.TryGetLocalPath() ?? throw new IOException("Select a local file; remote storage items must be downloaded first.")).ToList();
    }
    private async Task<string?> SaveFile(string title, string name, string pattern)
    {
        if (preview) return null;
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = title, SuggestedFileName = name, ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType("Output file") { Patterns = [pattern] }] });
        return file == null ? null : file.TryGetLocalPath() ?? throw new IOException("Select a local output file.");
    }
    private async Task ChooseGame()
    {
        try
        {
            var selection = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Select Nivalis Nights" });
            var folder = selection.FirstOrDefault()?.TryGetLocalPath(); if (folder == null) return;
            await SelectGame(folder);
        }
        catch (Exception ex) { Error(ex); }
    }
    private async Task SelectGame(string folder)
    {
        folder = GameInstallation.Resolve(folder);
        GameFiles.ValidateIdentity(folder);
        bool reconnect = GameInstallation.RequiresReconnect(library, folder);
        if (reconnect && !await Confirm(GameInstallation.ReconnectPrompt(library, folder))) return;
        GameInstallation.Select(library, folder, reconnect);
        status.Text = reconnect ? "Installation reconnected. Review the profile, then apply it to the moved game." : "Game location selected.";
        RefreshPage();
    }
}
