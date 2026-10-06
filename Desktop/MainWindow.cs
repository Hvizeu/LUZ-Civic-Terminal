using System.Diagnostics;
using System.IO;
using Luz;

namespace LuzDesktop;
public sealed partial class MainWindow : Window
{
    private readonly Library library;
    private readonly Sources sources = new();
    private readonly bool preview;
    private readonly Grid shell = new(), body = new();
    private readonly StackPanel sidebar = new(), details = new();
    private readonly TextBlock status = new(), profileLabel = new(), summary = new();
    private readonly ListBox mods = new(), catalogList = new();
    private readonly TextBox search = new() { [ToolTip.TipProperty] = "Find installed mods by name or description" }, catalogSearch = new() { [ToolTip.TipProperty] = "Search the loaded catalogue" };
    private readonly ComboBox profiles = new() { ItemTemplate = new FuncDataTemplate<Profile>((p, _) => new TextBlock { Text = p?.Name }) };
    private readonly Button cancel = new() { Content = "Cancel", IsVisible = false };
    private readonly List<CatalogMod> catalog = [];
    private CancellationTokenSource? operation;
    private string page = "Registry", nexusAddress = "", lastError = "";
    private bool refreshing;
    private string? selectedId;
    private Point dragStart;
    private bool dragArmed;
    private PointerPressedEventArgs? dragPress;
    private readonly Credentials credentials;
    private readonly Dictionary<string, (DateTime Stamp, Avalonia.Media.Imaging.Bitmap Image)> iconCache = new(HostPlatform.PathComparer);
#if PREVIEW_TOOL
    public void PreviewPage(string value)
    {
        if (!preview) throw new InvalidOperationException();
        library.State.GameFolder = value == "Maintenance" ? "" : Path.Combine(library.Root, "PreviewGame");
        status.Text = library.State.GameFolder.Length == 0 ? "Select your game folder in Maintenance to begin." : "Ready.";
        foreach (var entry in library.Active.Mods) entry.Enabled = true;
        foreach (var package in library.State.Packages) package.Plugins.RemoveAll(p => p.Guid.StartsWith("preview.", StringComparison.Ordinal));
        if (value == "Registry-disabled") library.Active.Mods[1].Enabled = false;
        if (value == "Registry-issues") library.State.Packages[0].Plugins.Add(new("preview.plugin", "Better Time", "1.0.0", [new("required.clock.library", "1.0.0", false)], []));
        library.State.AppliedProfileId = value == "Registry-applied" ? library.Active.Id : "";
        library.State.AppliedFingerprint = value == "Registry-applied" ? library.Fingerprint(library.Active) : "";
        search.Text = value == "Registry-search" ? "table" : value == "Registry-empty" ? "no matching permit" : "";
        PreviewRecoveryState(value);
        page = value.StartsWith("Registry", StringComparison.Ordinal) ? "Registry" : value.StartsWith("Maintenance", StringComparison.Ordinal) ? "Maintenance" : value; RefreshPage();

    }
#endif
    public MainWindow(Library library, bool preview = false)
    {
        this.library = library; this.preview = preview; credentials = new Credentials(library.Root);
        Opened += async (_, _) => await CheckLauncherUpdate(false);
        Closed += (_, _) => { updateLifetime.Cancel(); updateHttp.Dispose(); };
        Title = "LUZ Civic Terminal · Nivalis Nights"; Width = 1440; Height = 900; MinWidth = 1100; MinHeight = 720;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://LUZ Civic Terminal/Assets/luz-icon.png")));
        Background = TerminalTheme.Ink; Foreground = TerminalTheme.Text; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        shell.Background = TerminalTheme.Ink;
        shell.RowDefinitions.Add(new() { Height = new GridLength(88) }); shell.RowDefinitions.Add(new()); shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 1) }; header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var identity = new DockPanel { Margin = new Thickness(22, 12, 16, 8) };
        var emblem = new CutBorder { Cut = 12, Width = 52, Height = 52, BorderBrush = TerminalTheme.Gold, BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 14, 0), Child = new TextBlock { Text = "LUZ", FontFamily = FontFamily.Default, FontWeight = FontWeight.Bold, FontSize = 20, Foreground = TerminalTheme.Gold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; DockPanel.SetDock(emblem, Dock.Left); identity.Children.Add(emblem);
        var brand = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; brand.Children.Add(Label("CIVIC TERMINAL", 26, TerminalTheme.Gold, true)); brand.Children.Add(Label("NIVALIS NIGHTS  /  MOD REGISTRY", 11, TerminalTheme.Muted)); identity.Children.Add(brand); header.Children.Add(identity);
        var headerActions = Row(); headerActions.Margin = new Thickness(10, 20, 20, 10);
        applyButton = Button("Apply profile", Apply, icon: CivicSymbol.Apply); playButton = Button("Play Nivalis", Play, icon: CivicSymbol.Play); headerActions.Children.Add(applyButton); headerActions.Children.Add(playButton); Grid.SetColumn(headerActions, 1); header.Children.Add(headerActions); shell.Children.Add(new CutBorder { Cut = 20, Background = TerminalTheme.Panel, BorderBrush = TerminalTheme.Edge, BorderThickness = new Thickness(1), Child = header, Margin = new Thickness(10, 6, 10, 6) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(220) }); body.ColumnDefinitions.Add(new()); Grid.SetRow(body, 1); shell.Children.Add(body);
        sidebar.Margin = new Thickness(0, 0, 1, 0); sidebar.Children.Add(Label("Nivalis Nights", 15, TerminalTheme.Muted, margin: new Thickness(22, 22, 10, 16)));
        foreach (var title in new[] { "Registry", "Catalogue", "Maintenance" }) { string target = title; var b = QuietButton(title, () => { page = target; RefreshPage(); }, title == "Registry" ? "Manage the selected profile" : title); b.Tag = title; b.Content = CivicIcons.Label(Enum.Parse<CivicSymbol>(title), title, controlContent: true); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Margin = new Thickness(14, 0, 14, 10); sidebar.Children.Add(b); }
        sidebar.Children.Add(Label("ACTIVE PROFILE", 11, TerminalTheme.Gold, margin: new Thickness(22, 24, 10, 0)));
        profiles.Margin = new Thickness(14, 8, 14, 12); profiles.SelectionChanged += (_, _) => { if (!refreshing && profiles.SelectedItem is Profile p) { library.State.ActiveProfileId = p.Id; library.Save(); RefreshPage(); } }; sidebar.Children.Add(profiles);
        var profileButtons = Row(); profileButtons.Margin = new Thickness(14, 0, 0, 0); profileButtons.Children.Add(QuietButton("New", () => NewProfile(false))); profileButtons.Children.Add(QuietButton("Copy", () => NewProfile(true))); sidebar.Children.Add(profileButtons);
        var more = new ComboBox { Margin = new Thickness(14, 0, 14, 12), ItemsSource = new[] { "Profile actions…", "Rename", "Delete", "Export profile", "Import profile" }, SelectedIndex = 0 };
        more.SelectionChanged += (_, _) => { if (more.SelectedIndex > 0) { int action = more.SelectedIndex; more.SelectedIndex = 0; Run(() => ProfileAction(action)); } }; sidebar.Children.Add(more);
        profileLabel.TextWrapping = TextWrapping.Wrap; profileLabel.Foreground = TerminalTheme.Muted; profileLabel.Margin = new Thickness(22, 18, 18, 20); sidebar.Children.Add(profileLabel);
        body.Children.Add(new CutBorder { Cut = 18, Background = TerminalTheme.Panel, BorderBrush = TerminalTheme.Edge, BorderThickness = new Thickness(1), Child = sidebar, Margin = new Thickness(10, 12, 0, 12) });
        var footer = new DockPanel { Background = TerminalTheme.Panel, Margin = new Thickness(0, 1, 0, 0), LastChildFill = true }; Grid.SetRow(footer, 2);
        cancel.Click += (_, _) => operation?.Cancel(); DockPanel.SetDock(cancel, Dock.Right); cancel.Margin = new Thickness(8); footer.Children.Add(cancel);
        status.Text = library.State.GameFolder.Length == 0 ? "Select your game folder in Maintenance to begin." : "Ready."; status.TextWrapping = TextWrapping.Wrap; status.Foreground = TerminalTheme.Muted; status.Margin = new Thickness(22, 12, 22, 12); footer.Children.Add(status); shell.Children.Add(footer); Content = shell;
        search.TextChanged += (_, _) => RefreshMods(); catalogSearch.TextChanged += (_, _) => RefreshCatalog();
        mods.Background = Brushes.Transparent; mods.BorderThickness = new Thickness(0); ScrollViewer.SetHorizontalScrollBarVisibility(mods, ScrollBarVisibility.Disabled); mods.SelectionChanged += (_, _) => { if (mods.SelectedItem is ListBoxItem { Tag: string id }) { selectedId = id; ShowDetails(); } };
        DragDrop.SetAllowDrop(mods, true); mods.AddHandler(DragDrop.DropEvent, DropMod);
        mods.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.TryGetText() is { } text && text.StartsWith("LuzMod:", StringComparison.Ordinal) && library.Active.Mods.Any(m => m.PackageId == text[7..]) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; });
        mods.AddHandler(PointerPressedEvent, (_, e) => { dragStart = e.GetPosition(mods); dragArmed = e.Source is TextBlock { Tag: "drag" } && e.GetCurrentPoint(mods).Properties.IsLeftButtonPressed; dragPress = e; }, RoutingStrategies.Tunnel);
        mods.PointerMoved += async (_, e) => { if (dragArmed && dragPress != null && e.GetCurrentPoint(mods).Properties.IsLeftButtonPressed && Math.Sqrt(Math.Pow(e.GetPosition(mods).X - dragStart.X, 2) + Math.Pow(e.GetPosition(mods).Y - dragStart.Y, 2)) > 7 && mods.SelectedItem is ListBoxItem { Tag: string id }) { dragArmed = false; var data = new DataTransfer(); data.Add(DataTransferItem.CreateText("LuzMod:" + id)); try { await DragDrop.DoDragDropAsync(dragPress, data, DragDropEffects.Move); } catch (Exception ex) { Error(ex); } } };
        catalogList.Background = Brushes.Transparent; catalogList.BorderThickness = new Thickness(0); ScrollViewer.SetHorizontalScrollBarVisibility(catalogList, ScrollBarVisibility.Disabled);
        Closing += (_, e) => { if (operation != null) { e.Cancel = true; status.Text = "Finish or cancel the current operation before closing."; } }; Closed += (_, _) => { sources.Dispose(); foreach (var cached in iconCache.Values) cached.Image.Dispose(); iconCache.Clear(); };
        RefreshProfiles(); RefreshPage();
        if (!preview && library.State.GameFolder.Length == 0) DetectGame();
        var recovery = OperationRecovery.Inspect(library.Root);
        if (recovery.Count > 0) status.Text = OperationRecovery.Summary(recovery) + " Open Maintenance before continuing.";
    }
    private static TextBlock Label(string text, double size = 14, IBrush? brush = null, bool bold = false, Thickness? margin = null) => new() { Text = text, FontSize = size, Foreground = brush ?? TerminalTheme.Text, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, FontFamily = FontFamily.Default, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 0, 0, 8) };
    private static WrapPanel Row() => new() { Orientation = Orientation.Horizontal };
    private Button Button(string text, Action action, string? tip = null, bool primary = false, CivicSymbol? icon = null)
    {
        var b = new Button { Content = text, [ToolTip.TipProperty] = tip ?? text, Background = primary ? TerminalTheme.Gold : TerminalTheme.Edge, Foreground = primary ? TerminalTheme.Ink : TerminalTheme.Text };
        if (icon is { } symbol) b.Content = CivicIcons.Label(symbol, text, controlContent: true);
        AutomationProperties.SetName(b, text);
         b.Click += (_, _) => Run(action); return b;
    }
    private void Run(Action action) { try { action(); } catch (Exception ex) { Error(ex); } }
    private async Task Busy(string message, Func<CancellationToken, Task> action)
    {
        if (operation != null) return;
        operation = new(); body.IsEnabled = false; ((Control)shell.Children[0]).IsEnabled = false; cancel.IsVisible = true; status.Text = message;
        try { await action(operation.Token); status.Text = OperationRecovery.CompletionMessage(library); }
        catch (OperationCanceledException) { status.Text = "Cancelled. Any completed library imports remain available; game files were not partially deployed."; }
        catch (Exception ex) { Error(ex); }
        finally { operation.Dispose(); operation = null; body.IsEnabled = true; ((Control)shell.Children[0]).IsEnabled = true; cancel.IsVisible = false; RefreshProfiles(); RefreshPage(); if (!processingLinks && incomingLinks.Count > 0) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ProcessLinks()); }
    }
    private void Error(Exception ex) { lastError = ex.ToString(); status.Text = ex.Message; if (!preview) _ = Notice("Action could not finish", ex.Message); }
    private void RefreshProfiles() { refreshing = true; profiles.ItemsSource = null; profiles.ItemsSource = library.State.Profiles; profiles.SelectedItem = library.Active; refreshing = false; }
    private void RefreshPage()
    {
        foreach (var b in sidebar.Children.OfType<Button>()) { bool active = b.Tag as string == page; b.Background = active ? TerminalTheme.Gold : Brushes.Transparent; b.Foreground = active ? TerminalTheme.Ink : TerminalTheme.Muted; }
        while (body.Children.Count > 1) body.Children.RemoveAt(1);
        RefreshProfileStatus();
        Control view = page switch { "Catalogue" => CataloguePage(), "Maintenance" => MaintenancePage(), _ => RegistryPage() }; Grid.SetColumn(view, 1); body.Children.Add(view);
    }
    private Grid RegistryPage()
    {
        Detach(search); Detach(mods); Detach(details); Detach(summary);
        var grid = new Grid { Margin = new Thickness(26, 18, 24, 18) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(Label(library.Active.Name, 28, TerminalTheme.Text, bold: true)); grid.Children.Add(heading);
        var toolbar = new StackPanel { Margin = new Thickness(0, 0, 0, 8) }; toolbar.Children.Add(Label("Search mods", 13, TerminalTheme.Muted)); toolbar.Children.Add(search); var actions = Row();
        actions.Children.Add(Button("Import ZIP / DLL", ImportMods, "Add downloaded mods to the library", true, CivicSymbol.Import)); actions.Children.Add(QuietButton("Auto-sort", () => { library.Active.Mods = ModPlanner.Sort(library.Active, library.State.Packages); library.Save(); RefreshMods(); }, "Put dependencies before dependent mods. BepInEx controls runtime startup order."));
        actions.Children.Add(QuietButton("Check updates", () => _ = CheckUpdates())); actions.Children.Add(ActionsMenu("Profile tools", ("Add from library", AddFromLibrary), ("Validate profile", ValidateProfile))); toolbar.Children.Add(actions); Grid.SetRow(toolbar, 1); grid.Children.Add(toolbar);
        var content = new Grid(); content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = new GridLength(290) });
        content.Children.Add(TerminalTheme.Section("Mods / deployment order", mods)); details.Margin = new Thickness(4, 0, 4, 0); var scroller = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; var detailFrame = (Control)TerminalTheme.Section("Mod details", scroller); detailFrame.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(detailFrame, 1); content.Children.Add(detailFrame); Grid.SetRow(content, 2); grid.Children.Add(content);
        summary.Foreground = TerminalTheme.Muted; summary.TextWrapping = TextWrapping.Wrap; summary.Margin = new Thickness(0, 16, 0, 0); Grid.SetRow(summary, 3); grid.Children.Add(summary); RefreshMods(); return grid;
    }
    private void RefreshMods()
    {
        mods.Items.Clear(); int index = 0;
        foreach (var entry in library.Active.Mods)
        {
            index++; var package = library.State.Packages.FirstOrDefault(p => p.Id == entry.PackageId); if (package == null) continue;
            if (!(package.Name + " " + package.Description).Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)) continue;
            var row = new Grid { Margin = new Thickness(4, 3, 4, 3), MinHeight = 44 };
            foreach (double w in new[] { 26d, 26d, 54d }) row.ColumnDefinitions.Add(new() { Width = new GridLength(w) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(72) });
            var handle = Label("⠿", 23, TerminalTheme.Muted); handle.Tag = "drag"; ToolTip.SetTip(handle, "Drag to change deployment priority"); row.Children.Add(handle);
            var enabled = new CheckBox { IsChecked = entry.Enabled, [ToolTip.TipProperty] = "Enable " + package.Name }; enabled.Click += (_, _) => { entry.Enabled = enabled.IsChecked == true; library.Save(); RefreshMods(); }; Grid.SetColumn(enabled, 1); row.Children.Add(enabled);
            var icon = ModIcon(package.Icon.Length > 0 ? FileSafety.Under(library.PackageRoot(package.Id), package.Icon) : null, package.Name); Grid.SetColumn(icon, 2); row.Children.Add(icon);
            var name = new StackPanel { Margin = new Thickness(8, 2, 8, 0) }; name.Children.Add(Label(package.Name, 16, bold: true, margin: new Thickness(0, 0, 0, 4))); name.Children.Add(Label((entry.Enabled ? "" : "Disabled  ·  ") + package.Source + (entry.Pinned ? "  /  PINNED" : "") + "  ·  " + package.Version, 12, TerminalTheme.Muted, margin: new Thickness(0))); Grid.SetColumn(name, 3); row.Children.Add(name);
            var order = Label(index.ToString("00"), 22, TerminalTheme.Gold); order.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(order, 4); row.Children.Add(order);
            var item = new ListBoxItem { Content = row, Tag = package.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = entry.Enabled ? TerminalTheme.Panel : TerminalTheme.Ink, Foreground = TerminalTheme.Text, BorderBrush = TerminalTheme.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 4) }; mods.Items.Add(item); if (selectedId == package.Id) mods.SelectedItem = item;
        }
        if (mods.Items.Count == 0) mods.Items.Add(new ListBoxItem { IsEnabled = false, Content = Label(library.Active.Mods.Count == 0 ? "No mods in this profile.\n\nImport a ZIP or choose a mod from the Catalogue. To keep mods already installed in the game, use Maintenance → Import existing installation." : "No mods match this search.", 17, TerminalTheme.Muted, margin: new Thickness(20)), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        if (mods.SelectedItem == null && mods.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag != null) is { } first) mods.SelectedItem = first;
        if (mods.SelectedItem == null) selectedId = null;
        summary.Text = "Drag the handle to reorder. Lower rows win shared files."; ToolTip.SetTip(summary, "BepInEx controls plugin startup order using dependencies. This list controls file deployment priority."); RefreshProfileStatus(); ShowDetails();
    }
    private Control ModIcon(string? path, string name)
    {
        if (path != null && File.Exists(path))
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                if (!iconCache.TryGetValue(path, out var cached) || cached.Stamp != stamp)
                {
                    var bitmap = new Avalonia.Media.Imaging.Bitmap(path);
                    cached.Image?.Dispose(); cached = (stamp, bitmap); iconCache[path] = cached;
                }
                return new Image { Source = cached.Image, Width = 44, Height = 44 };
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { }

        }
        return new CutBorder { Cut = 9, Width = 44, Height = 44, Background = TerminalTheme.Ink, BorderBrush = TerminalTheme.Gold, BorderThickness = new Thickness(1), Child = new TextBlock { Text = new string(name.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant(), Foreground = TerminalTheme.Gold, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
    }
    private void ShowDetails()
    {
        details.Children.Clear(); var entry = library.Active.Mods.FirstOrDefault(m => m.PackageId == selectedId); var p = library.State.Packages.FirstOrDefault(p => p.Id == selectedId);
        if (p == null || entry == null) { details.Children.Add(Label("Select a mod to see its source and dependencies.", brush: TerminalTheme.Muted)); return; }
        details.Children.Add(Label(p.Name, 22, bold: true));
        details.Children.Add(Label(p.Version + "  ·  " + p.Source + "  ·  " + (entry.Enabled ? "Enabled" : "Disabled"), 13, entry.Enabled ? TerminalTheme.Teal : TerminalTheme.Muted));
        var description = Label(p.Description.Length == 0 ? "No description included." : p.Description, brush: TerminalTheme.Muted);
        description.MaxHeight = 100; description.TextTrimming = TextTrimming.WordEllipsis; ToolTip.SetTip(description, p.Description); details.Children.Add(description);
        if (p.PageUrl.Length > 0) details.Children.Add(QuietButton("Open mod page ↗", () => OpenUrl(p.PageUrl)));
        var issues = ModPlanner.Check(library.Active, library.State.Packages);
        if (issues.Count > 0)
        {
            details.Children.Add(Label("Profile issues", 15, TerminalTheme.Gold, true, new Thickness(0, 12, 0, 8)));
            foreach (var issue in issues.Take(3)) details.Children.Add(Label(issue.Severity + ": " + issue.Message, 13, issue.Severity == "Error" ? TerminalTheme.Brush("#FFA788") : TerminalTheme.Gold));
            if (issues.Count > 3) details.Children.Add(QuietButton("View all " + issues.Count + " issues", ValidateProfile));
        }
        var dependencyHeading = CivicIcons.Label(CivicSymbol.Dependencies, "Dependencies", 18); dependencyHeading.Margin = new Thickness(0, 12, 0, 8); Avalonia.Controls.Documents.TextElement.SetFontSize(dependencyHeading, 15); details.Children.Add(dependencyHeading);
        foreach (var dep in p.Dependencies) details.Children.Add(Label(dep, 13, TerminalTheme.Muted));
        foreach (var dep in p.Plugins.SelectMany(plugin => plugin.Dependencies).Concat(p.RequiredPlugins)) details.Children.Add(Label(dep.Guid + (dep.MinimumVersion.Length>0?" ≥ "+dep.MinimumVersion:"") + (dep.Optional ? " (optional)" : ""), 13, TerminalTheme.Muted));
        if (p.Dependencies.Count == 0 && p.RequiredPlugins.Count==0 && p.Plugins.All(plugin => plugin.Dependencies.Count == 0)) details.Children.Add(Label("None declared.", 13, TerminalTheme.Muted));
        var pin = new CheckBox { Content = CivicIcons.Label(CivicSymbol.Pinned, "Pin this version", 16, controlContent: true), IsChecked = entry.Pinned, Margin = new Thickness(0, 16, 0, 12), [ToolTip.TipProperty] = "Skip this version when checking for updates." };
        AutomationProperties.SetName(pin, "Pin this version");
        pin.Click += (_, _) => Run(() => { entry.Pinned = pin.IsChecked == true; library.Save(); RefreshMods(); }); details.Children.Add(pin);
        var order = Row(); var up = QuietButton("Move up", () => Move(-1)); var down = QuietButton("Move down", () => Move(1));
        int index = library.Active.Mods.IndexOf(entry); up.IsEnabled = index > 0; down.IsEnabled = index < library.Active.Mods.Count - 1; order.Children.Add(up); order.Children.Add(down); details.Children.Add(order);
        details.Children.Add(ActionsMenu("Edit mod",
            ("Set source link", async () => { var url = await Prompt("Mod page URL", p.PageUrl); if (url != null) { SetSourceLink(p, url); library.Save(); RefreshMods(); } }),
            ("Edit notes", async () => { var note = await Prompt("Private profile note", entry.Note); if (note != null) { entry.Note = note; library.Save(); ShowDetails(); } }),
            ("Choose icon", async () => { var file = (await OpenFiles("Choose icon", false, "*.png", "*.jpg", "*.jpeg")).FirstOrDefault(); if (file != null) { if (new FileInfo(file).Length > 5_242_880) throw new InvalidDataException("Choose an image smaller than 5 MB."); string filename = "custom-icon" + Path.GetExtension(file).ToLowerInvariant(); File.Copy(file, FileSafety.Under(library.PackageRoot(p.Id), filename), true); p.Icon = filename; library.Save(); RefreshMods(); } }),
            ("Remove from profile", async () => { if (await Confirm("Remove this mod from the profile? Its files stay in the library. The game changes only after Apply.")) { library.Active.Mods.Remove(entry); library.Save(); selectedId = null; RefreshMods(); } })));
        if (entry.Note.Length > 0) details.Children.Add(Label(entry.Note, 13, TerminalTheme.Muted));
        details.Children.Add(Label(p.Files.Count + " package files", 12, TerminalTheme.Muted, margin: new Thickness(0, 12, 0, 0)));
    }
    private void Move(int offset)
    {
        int index = library.Active.Mods.FindIndex(m => m.PackageId == selectedId), target = index + offset; if (index < 0 || target < 0 || target >= library.Active.Mods.Count) return;
        var entry = library.Active.Mods[index]; library.Active.Mods.RemoveAt(index); library.Active.Mods.Insert(target, entry); library.Save(); RefreshMods();
    }
    private void DropMod(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() is not string raw || !raw.StartsWith("LuzMod:", StringComparison.Ordinal)) return;
        string id = raw[7..];
        var node = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (node is not ListBoxItem { Tag: string targetId } || targetId == id) return;
        Run(() => { var list = library.Active.Mods; int from = list.FindIndex(m => m.PackageId == id), to = list.FindIndex(m => m.PackageId == targetId); if (from < 0 || to < 0) return; var entry = list[from]; list.RemoveAt(from); list.Insert(to, entry); library.Save(); selectedId = id; RefreshMods(); });
    }
    private async Task NewProfile(bool duplicate) { var name = await Prompt(duplicate ? "Name this profile copy" : "New profile name", duplicate ? library.Active.Name + " copy" : ""); if (name != null) { library.CreateProfile(name, duplicate); RefreshProfiles(); RefreshPage(); } }
    private async Task ProfileAction(int action)
    {
        if (action == 1) { var name = await Prompt("Rename profile", library.Active.Name); if (!string.IsNullOrWhiteSpace(name) && name.Length <= 80) { library.Active.Name = name; library.Save(); } }
        if (action == 2 && await Confirm("Delete this profile? Library packages and saved configuration backups are retained.")) library.DeleteProfile();
        if (action == 3 && await SaveFile("Export profile", "profile.luzprofile", "*.luzprofile") is { } export) library.ExportProfile(export);
        if (action == 4 && (await OpenFiles("Import profile", false, "*.luzprofile")).FirstOrDefault() is { } import) library.ImportProfile(import);
        RefreshProfiles(); RefreshPage();
    }
    private async Task ImportMods()
    {
        var files = await OpenFiles("Import mods", true, "*.zip", "*.dll");
        if (files.Count > 0) await Busy("Reading packages…", ct => Task.Run(() => { foreach (var file in files) { ct.ThrowIfCancellationRequested(); library.Import(file); } }, ct));
    }
    private async Task AddFromLibrary()
    {
        var selected = await Pick("Stored versions", library.State.Packages.Select(p => new Choice(p.Id, p.Name + " · " + p.Version + " · " + p.Source)).ToList());
        if (selected != null) { library.AddToProfile(library.State.Packages.First(p => p.Id == selected)); RefreshMods(); }
    }
    private Task ValidateProfile() { var issues = ModPlanner.Check(library.Active, library.State.Packages); return Notice("Profile checks", issues.Count == 0 ? "No declared dependency, duplicate plugin or shared-file conflicts found. Gameplay compatibility still needs testing." : string.Join("\n\n", issues.Select(i => i.Severity + ": " + i.Message))); }
    private static void ValidateWeb(string url) { if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new InvalidDataException("Use a valid HTTPS page address."); }
    private static Task OpenUrl(string url) { ValidateWeb(url); return HostPlatform.Open(url); }
    private static void SetSourceLink(ModPackage package, string url)
    {
        ValidateWeb(url); var uri = new Uri(url);
        if (uri.Host is "nexusmods.com" or "www.nexusmods.com") { var parsed = Sources.NexusAddress(url); package.Source = "Nexus"; package.SourceId = parsed.Mod.ToString(); }
        if (uri.Host == "thunderstore.io")
        {
            var match = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, @"^/(?:c/nivalis-nights/p|package)/([^/]+)/([^/]+)/?$");
            if (!match.Success) throw new InvalidDataException("Use the Thunderstore package page, without a version suffix.");
            package.Source = "Thunderstore"; package.SourceId = match.Groups[1].Value + "-" + match.Groups[2].Value;
        }
        package.PageUrl = url;
    }
}
