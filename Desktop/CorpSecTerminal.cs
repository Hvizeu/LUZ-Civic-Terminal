using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Luz;

namespace LuzDesktop;

public sealed partial class MainWindow
{
    private string corpSection = "Project", corpSourceProject = "";
    private CorpSecProject? corpProject;
    private CorpSecReport? corpReport;
    private readonly TextBox corpName = new(), corpAuthor = new(), corpDescription = new() { AcceptsReturn = true, Height = 80 }, corpWebsite = new(), corpContact = new();
    private readonly TextBox corpStoreName = new(), corpDependencies = new() { AcceptsReturn = true, Height = 100 };
    private readonly ListBox corpProjects = new(), corpCompanionList = new(), corpCapabilityList = new(), corpAdapterList = new(), corpFindingList = new(), corpFiles = new();
    private readonly StackPanel corpMessages = new();
    private bool corpProjectEvents, corpProjectListRefreshing;

    private Control CorpSecPage()
    {
        EnsureCorpProjects(); Detach(corpProjects);
        var page = new Grid { Margin = new Thickness(18, 14, 22, 18), ColumnDefinitions = new ColumnDefinitions("270,*") };
        page.Children.Add(CorpProjectRail());
        var main = new Grid { Margin = new Thickness(20, 0, 0, 0), RowDefinitions = new RowDefinitions("Auto,*") };
        var heading = new StackPanel();
        heading.Children.Add(Label(corpProject?.DisplayName ?? "Choose a mod project", 26, TerminalTheme.Text, true));
        heading.Children.Add(Label(corpProject == null ? "Add the folder where your release builds are written. LUZ will find the mods and remember them." : corpProject.PluginGuid + "  ·  " + corpProject.Version, 12, TerminalTheme.Muted));
        var tabs = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 12) };
        foreach (string name in new[] { "Project", "LUZ Support", "Validation", "Package" })
        {
            string target = name; var button = QuietButton(name, () => { SaveCorpProject(); corpSection = target; RefreshPage(); }); button.Tag = name; button.Background = corpSection == name ? TerminalTheme.Gold : TerminalTheme.Edge; button.Foreground = corpSection == name ? TerminalTheme.Ink : TerminalTheme.Text; button.Margin = new Thickness(0, 0, 8, 6); button.MinWidth = 112; button.IsEnabled = corpProject != null; tabs.Children.Add(button);
        }
        heading.Children.Add(tabs); main.Children.Add(heading);
        Control content = corpProject == null ? CorpEmptyState() : corpSection switch { "LUZ Support" => CorpSupportSection(), "Validation" => CorpValidationSection(), "Package" => CorpPackageSection(), _ => CorpProjectSection() };
        Grid.SetRow(content, 1); main.Children.Add(content); Grid.SetColumn(main, 1); page.Children.Add(main); return page;
    }

    private Control CorpProjectRail()
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        panel.Children.Add(Label("MY MODS", 13, TerminalTheme.Gold, true, new Thickness(4, 2, 0, 4)));
        var actions = new StackPanel { Spacing = 8 };
        actions.Children.Add(Button("Add mods folder", AddCorpFolder, "Choose the folder that receives your DLLs or release ZIPs", primary: true));
        actions.Children.Add(QuietButton("Scan for new builds", ScanCorpFolders, "Find new versions in every saved mods folder"));
        actions.Children.Add(QuietButton("Add one ZIP or DLL", OpenCorpPackage, "Add a one-off package; use a mods folder for repeat releases"));
        Grid.SetRow(actions, 1); panel.Children.Add(actions);
        corpProjects.MinHeight = 0; corpProjects.Margin = new Thickness(0, 10, 0, 0); corpProjects.Background = Brushes.Transparent; corpProjects.BorderThickness = new Thickness(0);
        ScrollViewer.SetHorizontalScrollBarVisibility(corpProjects, ScrollBarVisibility.Disabled); ScrollViewer.SetVerticalScrollBarVisibility(corpProjects, ScrollBarVisibility.Auto); AutomationProperties.SetName(corpProjects, "Saved mod projects");
        Grid.SetRow(corpProjects, 2); panel.Children.Add(corpProjects);
        return new CutBorder { Cut = 14, Background = TerminalTheme.Panel, BorderBrush = TerminalTheme.Edge, BorderThickness = new Thickness(1), Padding = new Thickness(10), ClipToBounds = true, Child = panel };
    }

    private Control CorpEmptyState()
    {
        var panel = CorpPanel(); panel.Children.Add(Label("Start with your builds folder", 22, TerminalTheme.Gold, true));
        panel.Children.Add(Label("Choose one parent folder once. It can contain several mods, versioned release folders, built DLLs or release ZIPs. CorpSec keeps a project for every detected plugin and shows them in My mods.", 15));
        panel.Children.Add(Button("Add mods folder", AddCorpFolder, primary: true));
        panel.Children.Add(Label("Use Add one ZIP or DLL only when you do not have a reusable build or release folder.", 12, TerminalTheme.Muted, margin: new Thickness(0, 8, 0, 0))); return Scroll(panel);
    }

    private Control CorpProjectSection()
    {
        Detach(corpName); Detach(corpAuthor); Detach(corpDescription); Detach(corpWebsite); Detach(corpContact);
        var panel = CorpPanel(); panel.Children.Add(Label("Project details", 21, TerminalTheme.Gold, true));
        if (corpProject == null) return Scroll(panel);
        string watched = corpProject.ProjectRoot.Length == 0 ? "One-off package" : corpProject.ProjectRoot;
        panel.Children.Add(Label("Watched folder", 12, TerminalTheme.Muted)); panel.Children.Add(CorpPath(watched, corpProject.ProjectRoot.Length == 0 ? TerminalTheme.Danger : TerminalTheme.Text));
        panel.Children.Add(Label("Current build", 12, TerminalTheme.Muted, margin: new Thickness(0, 4, 0, 0))); panel.Children.Add(CorpPath(corpProject.Input, TerminalTheme.Text));
        if (corpProject.BuildChoices.Count > 1)
        {
            var edition = new ComboBox(); bool ready = false;
            foreach (var choice in corpProject.BuildChoices) edition.Items.Add(new ComboBoxItem { Content = choice.Label + " · " + choice.Version, Tag = choice.Edition, [ToolTip.TipProperty] = choice.Input + Environment.NewLine + choice.Evidence });
            edition.SelectedItem = edition.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == corpProject.BuildEdition); ready = true;
            edition.SelectionChanged += (_, _) => { if (ready && edition.SelectedItem is ComboBoxItem { Tag: string selected }) SelectCorpEdition(selected); };
            panel.Children.Add(Field("Build edition to follow", edition));
        }
        var buildActions = Row(); buildActions.Children.Add(Button("Check for a new build", RefreshCorpProject, primary: true)); buildActions.Children.Add(QuietButton("Choose source folder", ChooseCorpSource)); panel.Children.Add(buildActions);
        if (corpProject.ProjectRoot.Length == 0) panel.Children.Add(Label("Add the folder that receives this mod's releases to make future versions automatic.", 12, TerminalTheme.Muted));
        panel.Children.Add(Field("Display name", corpName)); panel.Children.Add(Field("Author", corpAuthor)); panel.Children.Add(Field("Description", corpDescription)); panel.Children.Add(Field("Website", corpWebsite)); panel.Children.Add(Field("Support contact", corpContact));
        panel.Children.Add(Label("Changes save automatically when you switch mods or sections. Source folder: " + (corpProject.SourceProject.Length == 0 ? "not selected" : corpProject.SourceProject), 12, TerminalTheme.Muted, margin: new Thickness(0, 10, 0, 0)));
        panel.Children.Add(new Border { Height = 1, Background = TerminalTheme.Edge, Margin = new Thickness(0, 14, 0, 12) });
        panel.Children.Add(Label("Remove saved project", 14, TerminalTheme.Danger, true));
        panel.Children.Add(Label("Removes this entry and its private CorpSec settings. Your build folder, source and exported packages stay where they are.", 12, TerminalTheme.Muted));
        var remove = QuietButton("Remove from My mods", ForgetCorpProject, "Forget this author project without deleting the mod's files"); remove.Foreground = TerminalTheme.Danger; panel.Children.Add(remove); return Scroll(panel);
    }

    private Control CorpSupportSection()
    {
        Detach(corpCompanionList); Detach(corpCapabilityList); Detach(corpAdapterList); Detach(corpFindingList);
        var panel = CorpPanel(); panel.Children.Add(Label("LUZ support", 21, TerminalTheme.Gold, true));
        var supportIntro = Label("CorpSec inspects the built DLL and optional source without executing either one. Review only the questions it cannot answer safely.", brush: TerminalTheme.Muted); supportIntro.TextWrapping = Avalonia.Media.TextWrapping.Wrap; panel.Children.Add(supportIntro);
        panel.Children.Add(Button("Analyze this build", AnalyzeCorpProject, "Refresh detected support, persistence evidence and required actions", primary: true));
        var intent = new ComboBox { ItemsSource = new[] { "I don't know yet", "No extra persistent state", "Separate save or configuration files", "State stored inside the game save", "Custom recovery adapter" } };
        intent.SelectedIndex = corpProject?.RecoveryIntent switch { "stateless" => 1, "files" => 2, "embedded" => 3, "custom" => 4, _ => 0 }; bool intentReady = true;
        intent.SelectionChanged += (_, _) => { if (intentReady) MarkCorpRecovery(intent.SelectedIndex switch { 1 => "stateless", 2 => "files", 3 => "embedded", 4 => "custom", _ => "unknown" }); };
        panel.Children.Add(Field("Where does this mod keep persistent state?", intent));
        RefreshFindings(); corpFindingList.MinHeight = 110; panel.Children.Add(Field("Detected information and next actions", corpFindingList));
        panel.Children.Add(QuietButton("Review selected file suggestion", ReviewSelectedFinding));
        panel.Children.Add(Label("Owned save and configuration files", 14, TerminalTheme.Gold, true, new Thickness(0, 10, 0, 0)));
        corpCompanionList.MinHeight = 100; RefreshCompanions(); panel.Children.Add(corpCompanionList);
        var fileActions = Row(); fileActions.Children.Add(Button("Add owned file", () => EditCorpCompanion(null), primary: true)); fileActions.Children.Add(QuietButton("Edit selected", EditSelectedCorpCompanion)); fileActions.Children.Add(QuietButton("Remove selected", RemoveSelectedCorpCompanion)); panel.Children.Add(fileActions);
        var advanced = new StackPanel { Spacing = 6 };
        advanced.Children.Add(Label("Most mods keep the detected native implementation. Add these only when the mod supplies custom recovery code or fallbacks.", 12, TerminalTheme.Muted));
        RefreshCapabilities(); corpCapabilityList.MinHeight = 90; advanced.Children.Add(Field("Capabilities", corpCapabilityList));
        var capabilityActions = Row(); capabilityActions.Children.Add(QuietButton("Add capability", () => EditCorpCapability(null))); capabilityActions.Children.Add(QuietButton("Edit selected", EditSelectedCorpCapability)); capabilityActions.Children.Add(QuietButton("Remove selected", RemoveSelectedCorpCapability)); advanced.Children.Add(capabilityActions);
        RefreshAdapters(); corpAdapterList.MinHeight = 90; advanced.Children.Add(Field("Recovery adapters", corpAdapterList));
        var adapterActions = Row(); adapterActions.Children.Add(QuietButton("Add adapter DLL", () => EditCorpAdapter(null))); adapterActions.Children.Add(QuietButton("Edit selected", EditSelectedCorpAdapter)); adapterActions.Children.Add(QuietButton("Remove selected", RemoveSelectedCorpAdapter)); advanced.Children.Add(adapterActions);
        panel.Children.Add(new Expander { Header = Label("Advanced recovery implementations", 13, TerminalTheme.Gold, true, new Thickness(0)), Content = advanced, Margin = new Thickness(0, 10, 0, 4) });
        var actions = Row(); actions.Children.Add(Button("Generate support files", GenerateCorpSupport, primary: true)); actions.Children.Add(QuietButton("Generate build integration", GenerateCorpIntegration)); panel.Children.Add(actions);
        panel.Children.Add(Label("Generation reads the newest saved build, creates the package's LUZ folder and leaves the original DLL unchanged.", 12, TerminalTheme.Muted, margin: new Thickness(0, 8, 0, 0))); return Scroll(panel);
    }

    private Control CorpValidationSection()
    {
        var panel = CorpPanel(); panel.Children.Add(Label("Validation", 21, TerminalTheme.Gold, true));
        panel.Children.Add(Label("Package checks cover archive safety. Recovery declaration checks cover support records and implementation selection. In-game recovery remains a separate gameplay test.", brush: TerminalTheme.Muted));
        panel.Children.Add(Button("Validate current generated files", ValidateCorpProject, primary: true)); AddCorpResults(panel); return Scroll(panel);
    }

    private Control CorpPackageSection()
    {
        Detach(corpStoreName); Detach(corpDependencies); Detach(corpFiles);
        var panel = CorpPanel(); panel.Children.Add(Label("Package", 21, TerminalTheme.Gold, true));
        if (corpProject == null) return Scroll(panel);
        var packageIntro = Label("Each build rescans the watched folder, regenerates the selected package type, validates it, and writes the release ZIP. Choose the output folder once.", brush: TerminalTheme.Muted); packageIntro.TextWrapping = Avalonia.Media.TextWrapping.Wrap; panel.Children.Add(packageIntro);
        var mode = new ComboBox { ItemsSource = new[] { "Normal package", "Package with LUZ recovery support" }, SelectedIndex = corpProject.PackageMode == "recovery" ? 1 : 0 }; bool modeReady = true;
        mode.SelectionChanged += (_, _) => { if (modeReady && corpProject != null) { corpProject.PackageMode = mode.SelectedIndex == 1 ? "recovery" : "normal"; CorpSec.Save(corpProject); } };
        panel.Children.Add(Field("Package type", mode));
        panel.Children.Add(Label("Output folder", 12, TerminalTheme.Muted)); panel.Children.Add(CorpPath(corpProject.PackageOutput.Length == 0 ? "Choose an output folder before the first build." : corpProject.PackageOutput, corpProject.PackageOutput.Length == 0 ? TerminalTheme.Danger : TerminalTheme.Text)); panel.Children.Add(QuietButton(corpProject.PackageOutput.Length == 0 ? "Choose output folder" : "Change output folder", ChooseCorpOutput));
        panel.Children.Add(Field("Thunderstore package name", corpStoreName));
        panel.Children.Add(Field("Thunderstore dependency mappings — plugin GUID | Author-Package-Version", corpDependencies));
        var actions = Row(); actions.Children.Add(Button("Build Nexus ZIP", () => BuildCorpPackage("nexus"), primary: true)); actions.Children.Add(QuietButton("Build Thunderstore ZIP", () => BuildCorpPackage("thunderstore"))); actions.Children.Add(QuietButton("Build both", () => BuildCorpPackage("all"))); panel.Children.Add(actions);
        panel.Children.Add(Label("Package file list", 14, TerminalTheme.Gold, true, new Thickness(0, 14, 0, 4))); corpFiles.MinHeight = 180; panel.Children.Add(corpFiles); AddCorpResults(panel); return Scroll(panel);
    }

    private static StackPanel CorpPanel() => new() { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static ScrollViewer Scroll(Control content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static Control Field(string title, Control control) { var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 7) }; panel.Children.Add(Label(title, 12, TerminalTheme.Muted)); AutomationProperties.SetName(control, title); control.HorizontalAlignment = HorizontalAlignment.Stretch; panel.Children.Add(control); return panel; }
    private static TextBlock CorpPath(string value, Avalonia.Media.IBrush brush) { var text = Label(value, 13, brush); text.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis; ToolTip.SetTip(text, value); return text; }

    private void EnsureCorpProjects()
    {
        if (!corpProjectEvents) { corpProjectEvents = true; corpProjects.SelectionChanged += (_, _) => { if (!corpProjectListRefreshing && corpProjects.SelectedItem is ListBoxItem { Tag: string id }) Avalonia.Threading.Dispatcher.UIThread.Post(() => SelectCorpProject(id)); }; }
        RefreshCorpProjectList(corpProject?.Id);
    }

    private void RefreshCorpProjectList(string? selectId = null)
    {
        var projects = CorpSec.SavedProjects(library.Root).OrderBy(project => project.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(project => project.PluginGuid, StringComparer.OrdinalIgnoreCase).ToArray(); string wanted = selectId ?? corpProject?.Id ?? CorpSec.Workspace(library.Root).SelectedProjectId;
        if (corpProject == null && projects.Length != 0) { corpProject = projects.FirstOrDefault(p => p.Id == wanted) ?? projects[0]; LoadCorpProject(); CorpSec.RememberSelection(library.Root, corpProject.Id); wanted = corpProject.Id; }
        corpProjectListRefreshing = true; corpProjects.SelectedIndex = -1; corpProjects.Items.Clear();
        foreach (var project in projects)
        {
            bool available = File.Exists(project.Input) || Directory.Exists(project.Input); var text = new StackPanel(); text.Children.Add(Label(project.DisplayName, 14, TerminalTheme.Text, true, new Thickness(0, 0, 0, 2))); text.Children.Add(Label((available ? project.Version : "Build missing") + (project.ProjectRoot.Length == 0 ? "  ·  ONE-OFF" : ""), 11, available ? TerminalTheme.Muted : TerminalTheme.Danger, margin: new Thickness(0)));
            var item = new ListBoxItem { Content = text, Tag = project.Id, Padding = new Thickness(9, 7), Margin = new Thickness(0, 0, 0, 4), HorizontalContentAlignment = HorizontalAlignment.Stretch, [ToolTip.TipProperty] = project.Input }; corpProjects.Items.Add(item); if (project.Id == wanted) corpProjects.SelectedItem = item;
        }
        if (projects.Length == 0) corpProjects.Items.Add(new ListBoxItem { IsEnabled = false, Content = Label("No saved mods yet.", 13, TerminalTheme.Muted, margin: new Thickness(8)) }); corpProjectListRefreshing = false;
    }

    private void SelectCorpProject(string id)
    {
        if (corpProject?.Id == id) return; SaveCorpProject(); corpProject = CorpSec.SavedProjects(library.Root).Single(p => p.Id == id); corpReport = null; LoadCorpProject(); CorpSec.RememberSelection(library.Root, id); status.Text = corpProject.DisplayName + " selected."; RefreshPage();
    }

    private async Task AddCorpFolder()
    {
        string? root = await OpenFolder("Select the folder that receives your mod builds or release ZIPs"); if (root == null) return; CorpSecFolderScan? scan = null;
        await Busy("Scanning the folder for BepInEx mods…", ct => Task.Run(() => scan = CorpSec.ScanFolder(library.Root, root, ct), ct));
        if (scan != null) { string selected = scan.Projects.First().Id; corpProject = null; RefreshCorpProjectList(selected); corpProject = CorpSec.SavedProjects(library.Root).Single(p => p.Id == selected); LoadCorpProject(); CorpSec.RememberSelection(library.Root, selected); status.Text = "Found " + scan.Projects.Count + " mod" + (scan.Projects.Count == 1 ? "" : "s") + ". The folder is now saved in My mods."; RefreshPage(); }
    }

    private async Task ScanCorpFolders()
    {
        if (CorpSec.Workspace(library.Root).Roots.Count == 0) { status.Text = "Add a mods folder first."; return; }
        IReadOnlyList<CorpSecProject>? projects = null; SaveCorpProject(); string? selected = corpProject?.Id;
        await Busy("Scanning saved folders for new builds…", ct => Task.Run(() => projects = CorpSec.RefreshFolders(library.Root, ct), ct));
        if (projects != null) { corpProject = null; RefreshCorpProjectList(selected); status.Text = "Build scan complete. " + projects.Count + " saved mod" + (projects.Count == 1 ? " was" : "s were") + " refreshed."; RefreshPage(); }
    }

    private async Task OpenCorpPackage()
    {
        string? input = (await OpenFiles("Select a mod ZIP or DLL", false, "*.zip", "*.dll")).FirstOrDefault(); if (input == null) return; CorpSecProject? opened = null;
        await Busy("Reading the mod package…", ct => Task.Run(() => { ct.ThrowIfCancellationRequested(); opened = CorpSec.Inspect(library.Root, input, corpSourceProject); }, ct));
        if (opened != null) { corpProject = opened; corpReport = null; LoadCorpProject(); CorpSec.RememberSelection(library.Root, opened.Id); status.Text = "One-off project saved. Add its release folder later to make build discovery automatic."; RefreshPage(); }
    }

    private async Task RefreshCorpProject()
    {
        SaveCorpProject(); if (corpProject == null) return; CorpSecProject? refreshed = null;
        await Busy("Checking for the newest build…", ct => Task.Run(() => refreshed = CorpSec.Refresh(library.Root, corpProject, ct), ct));
        if (refreshed != null) { corpProject = refreshed; LoadCorpProject(); status.Text = "Current build: " + refreshed.Version + "."; RefreshPage(); }
    }

    private async Task ChooseCorpSource() { string? source = await OpenFolder("Select the optional source project"); if (source != null && corpProject != null) { corpSourceProject = source; corpProject.SourceProject = source; CorpSec.Save(corpProject); _ = CorpSec.Analyze(library.Root, corpProject); status.Text = "Source folder saved and analyzed."; RefreshPage(); } }
    private async Task ChooseCorpOutput() { string? output = await OpenFolder("Choose the folder for release ZIPs"); if (output != null && corpProject != null) { corpProject.PackageOutput = Path.GetFullPath(output); CorpSec.Save(corpProject); status.Text = "Release output folder saved."; RefreshPage(); } }

    private async Task ForgetCorpProject()
    {
        SaveCorpProject(); if (corpProject == null) return; var removed = corpProject;
        bool confirmed = await Confirm("Remove from My mods", "Remove “" + removed.DisplayName + "” from My mods?\n\nLUZ will forget its CorpSec settings and generated authoring workspace. The original build folder, source files and exported packages will not be deleted.\n\nScan for new builds will keep it hidden. Add its builds folder again if you want to restore it.", "Remove from My mods", 450, 680);
        if (!confirmed) return;
        CorpSec.Forget(library.Root, removed); corpProject = null; corpReport = null; corpSection = "Project"; RefreshCorpProjectList(); status.Text = removed.DisplayName + " was removed from My mods."; RefreshPage();
    }

    private void LoadCorpProject()
    {
        if (corpProject == null) return; corpSourceProject = corpProject.SourceProject;
        corpName.Text = corpProject.DisplayName; corpAuthor.Text = corpProject.Author; corpDescription.Text = corpProject.Description; corpWebsite.Text = corpProject.Website; corpContact.Text = corpProject.SupportContact;
        corpStoreName.Text = corpProject.ThunderstoreName; corpDependencies.Text = string.Join(Environment.NewLine, corpProject.ThunderstoreDependencies); RefreshCompanions(); RefreshCapabilities(); RefreshAdapters(); RefreshFindings();
    }

    private void SaveCorpProject()
    {
        if (corpProject == null) return;
        corpProject.DisplayName = corpName.Text?.Trim() ?? corpProject.DisplayName; corpProject.Author = corpAuthor.Text?.Trim() ?? ""; corpProject.Description = corpDescription.Text?.Trim() ?? ""; corpProject.Website = corpWebsite.Text?.Trim() ?? ""; corpProject.SupportContact = corpContact.Text?.Trim() ?? "";
        corpProject.ThunderstoreName = corpStoreName.Text?.Trim() ?? corpProject.ThunderstoreName; corpProject.ThunderstoreDependencies = Lines(corpDependencies.Text).ToList(); CorpSec.Save(corpProject);
    }

    private void RefreshCompanions()
    {
        corpCompanionList.Items.Clear(); if (corpProject == null || corpProject.Companions.Count == 0) { corpCompanionList.Items.Add(new ListBoxItem { IsEnabled = false, Content = Label("No owned save or configuration files declared.", 13, TerminalTheme.Muted, margin: new Thickness(8)) }); return; }
        for (int i = 0; i < corpProject.Companions.Count; i++) { var file = corpProject.Companions[i]; var content = new StackPanel(); content.Children.Add(Label(file.Pattern, 13, TerminalTheme.Text, true, new Thickness(0, 0, 0, 2))); content.Children.Add(Label((file.Root == "config" ? "Configuration" : "Save data") + "  ·  " + (file.Required ? "Required" : "Optional") + "  ·  " + file.Validation, 11, TerminalTheme.Muted, margin: new Thickness(0))); corpCompanionList.Items.Add(new ListBoxItem { Tag = i, Content = content, Padding = new Thickness(8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch }); }
    }

    private async Task EditSelectedCorpCompanion() { if (corpCompanionList.SelectedItem is ListBoxItem { Tag: int index } && corpProject != null) await EditCorpCompanion(corpProject.Companions[index], index); else status.Text = "Select an owned file first."; }
    private void RemoveSelectedCorpCompanion() { if (corpCompanionList.SelectedItem is ListBoxItem { Tag: int index } && corpProject != null) { corpProject.Companions.RemoveAt(index); corpProject.SupportSource = "author"; CorpSec.Save(corpProject); _ = CorpSec.Analyze(library.Root, corpProject); RefreshCompanions(); RefreshFindings(); status.Text = "Owned file removed from this project."; } else status.Text = "Select an owned file first."; }
    private Task EditCorpCompanion(CorpSecCompanion? existing) => EditCorpCompanion(existing, null);
    private async Task EditCorpCompanion(CorpSecCompanion? existing, int? index)
    {
        if (preview || corpProject == null) return;
        var root = new ComboBox { ItemsSource = new[] { "config", "saves" }, SelectedItem = existing?.Root ?? "config" };
        var pattern = new TextBox { Text = existing?.Pattern ?? "MyMod/{slot}.json" }; var slot = new ComboBox { ItemsSource = new[] { "raw", "lower", "upper", "sha256", "sha256-upper" }, SelectedItem = existing?.Slot ?? "raw" };
        var required = new CheckBox { Content = "This file is required to restore the mod", IsChecked = existing?.Required ?? false }; var validation = new ComboBox { ItemsSource = new[] { "opaque", "json-revision" }, SelectedItem = existing?.Validation ?? "opaque" };
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 6 }; content.Children.Add(Field("Location", root)); content.Children.Add(Field("Relative path — use {slot} where the save slot belongs", pattern)); content.Children.Add(Field("Slot naming", slot)); content.Children.Add(Field("Validation", validation)); content.Children.Add(required);
        var window = Dialog(existing == null ? "Add owned file" : "Edit owned file", content, 520); CorpSecCompanion? result = null; var buttons = Row(); buttons.Children.Add(Button("Save file rule", () => { string value = pattern.Text?.Trim() ?? ""; if (value.Length == 0 || Path.IsPathFullyQualified(value) || value.Split('/', '\\').Any(p => p == "..")) { status.Text = "Use a safe path relative to the selected location."; return; } result = new((string)root.SelectedItem!, value.Replace('\\', '/'), (string)slot.SelectedItem!, required.IsChecked == true, (string)validation.SelectedItem!); window.Close(); }, primary: true)); buttons.Children.Add(Button("Cancel", () => window.Close())); content.Children.Add(buttons); await window.ShowDialog(this);
        if (result != null) { if (index.HasValue) corpProject.Companions[index.Value] = result; else corpProject.Companions.Add(result); corpProject.RecoveryIntent = "files"; corpProject.RecoveryReviewed = true; corpProject.SupportSource = "author"; CorpSec.Save(corpProject); _ = CorpSec.Analyze(library.Root, corpProject); RefreshCompanions(); RefreshFindings(); status.Text = "Owned file saved."; }
    }

    private async Task GenerateCorpSupport()
    {
        SaveCorpProject(); if (corpProject == null) return; CorpSecProject? refreshed = null; CorpSecReport? report = null;
        await Busy("Finding the newest build and generating LUZ support…", ct => Task.Run(() => { refreshed = CorpSec.Refresh(library.Root, corpProject, ct); report = CorpSec.Generate(library.Root, refreshed, ct, true); }, ct));
        if (refreshed != null) corpProject = refreshed; if (report != null) { LoadCorpProject(); corpReport = report; corpSection = "Validation"; status.Text = report.RecoveryPassed ? "Support files generated from " + corpProject!.Version + "." : "Support files generated; recovery needs the listed author action."; RefreshPage(); }
    }

    private void GenerateCorpIntegration() { SaveCorpProject(); if (corpProject == null) return; string target = CorpSec.GenerateBuildIntegration(corpProject, Environment.ProcessPath ?? throw new InvalidOperationException("LUZ executable path is unavailable.")); status.Text = "Build integration generated: " + target; }
    private async Task ValidateCorpProject() { SaveCorpProject(); if (corpProject == null) return; CorpSecReport? report = null; bool recovery = corpProject.PackageMode == "recovery"; await Busy("Validating package and recovery contracts…", ct => Task.Run(() => report = CorpSec.Validate(corpProject, ct, recovery), ct)); if (report != null) { corpReport = report; status.Text = report.PackagePassed && (!recovery || report.RecoveryPassed) ? recovery ? "Package checks passed. Recovery declaration checks passed." : "Normal package checks passed. Recovery is not included." : "Validation found required author actions."; RefreshPage(); } }

    private async Task BuildCorpPackage(string preset)
    {
        SaveCorpProject(); if (corpProject == null) return; if (corpProject.PackageOutput.Length == 0) { await ChooseCorpOutput(); if (corpProject.PackageOutput.Length == 0) return; }
        CorpSecRelease? release = null;
        bool recovery = corpProject.PackageMode == "recovery";
        await Busy("Refreshing the build, generating support and validating the package…", ct => Task.Run(() => release = CorpSec.BuildRelease(library.Root, corpProject, corpProject.PackageOutput, preset, ct, recovery), ct));
        if (release != null) { corpProject = release.Project; LoadCorpProject(); corpReport = release.Report; status.Text = "Built " + string.Join(", ", release.Packages.Select(p => Path.GetFileName(p.Archive))) + ". Checksums are beside the ZIPs."; RefreshPage(); }
    }

    private void AddCorpResults(Panel panel)
    {
        Detach(corpMessages); corpMessages.Children.Clear(); if (corpReport == null) corpMessages.Children.Add(Label("No validation result yet.", 13, TerminalTheme.Muted));
        else foreach (var result in corpReport.Messages) { var summary = Label(result.Severity + " · " + result.Message, 13, result.Severity == "Error" ? TerminalTheme.Danger : result.Severity == "Success" ? TerminalTheme.Success : TerminalTheme.Muted); corpMessages.Children.Add(result.Detail == null ? summary : new Expander { Header = summary, Content = Label(result.Detail, 12, TerminalTheme.Muted, margin: new Thickness(18, 2, 0, 8)), IsExpanded = result.Severity == "Error" }); }
        panel.Children.Add(corpMessages); corpFiles.ItemsSource = corpReport?.Files ?? new[] { "No generated package tree yet." };
    }

    private static IEnumerable<string> Lines(string? text) => (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string[] Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
