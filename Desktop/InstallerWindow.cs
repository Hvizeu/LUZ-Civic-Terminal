using Luz;
using Avalonia.Platform;

namespace LuzDesktop;

public sealed class InstallerWindow : Window
{
    public InstallerWindow()
    {
        Title = "Install LUZ Civic Terminal"; Width = 630; Height = 400; CanResize = false;
        Background = TerminalTheme.Ink; Foreground = TerminalTheme.Text;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LUZ Civic Terminal/Assets/luz-icon.png")));
        string version = typeof(App).Assembly.GetName().Version!.ToString(3);
        string source = Path.GetFullPath(AppContext.BaseDirectory);
        string destination = DesktopInstallation.Destination(version);
        if (OperatingSystem.IsMacOS()) source = Path.GetFullPath(Path.Combine(source, "../.."));
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (desktop.Length == 0) desktop = Path.Combine(HostPlatform.Home, "Desktop");
        var content = new StackPanel { Margin = new Thickness(30), Spacing = 16 };
        content.Children.Add(new TextBlock { Text = "LUZ CIVIC TERMINAL", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = TerminalTheme.Gold });
        content.Children.Add(new TextBlock { Text = "Install " + version + " for your account and create a desktop shortcut.", TextWrapping = TextWrapping.Wrap, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = destination, TextWrapping = TextWrapping.Wrap, Foreground = TerminalTheme.Muted });
        var status = new TextBlock { Text = "Existing profiles and game files are preserved.", TextWrapping = TextWrapping.Wrap };
        content.Children.Add(status);
        var install = new Button { Content = "Install and create shortcut", HorizontalAlignment = HorizontalAlignment.Left };
        bool busy = false;
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        install.Click += async (_, _) =>
        {
            if ((string?)install.Tag == "complete") { Close(); return; }
            busy = true; install.IsEnabled = false; status.Text = "Copying application files…";
            try
            {
                await Task.Run(() => DesktopInstallation.InstallFiles(source, destination));
                // Windows shortcut COM runs on this STA UI thread.
                string shortcut = DesktopInstallation.CreateShortcut(desktop, destination, HostPlatform.Current);
                NxmRegistration.RefreshExistingWindowsRegistration(Path.Combine(destination, "LUZ Civic Terminal.exe"));
                status.Text = "Installed. Open LUZ from your desktop shortcut.\n" + shortcut;
                install.Content = "Done"; install.Tag = "complete";
            }
            catch (Exception ex) { status.Text = "Installation could not finish: " + ex.Message; }
            finally { busy = false; install.IsEnabled = true; }
        };
        content.Children.Add(install); Content = content;
    }
}
