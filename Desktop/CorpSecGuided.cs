using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Luz;

namespace LuzDesktop;

public sealed partial class MainWindow
{
    private async void SelectCorpEdition(string edition)
    {
        if (corpProject == null || corpProject.BuildEdition == edition) return;
        SaveCorpProject(); CorpSecProject? selected = null;
        await Busy("Switching the followed build edition…", ct => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested(); selected = CorpSec.SelectBuild(corpProject, edition); _ = CorpSec.Analyze(library.Root, selected, ct);
        }, ct));
        if (selected == null) return;
        corpProject = selected; corpReport = null; LoadCorpProject(); status.Text = "Following the " + edition + " build."; RefreshPage();
    }

    private async Task AnalyzeCorpProject()
    {
        SaveCorpProject(); if (corpProject == null) return; CorpSecAnalysisReport? report = null;
        await Busy("Inspecting the final DLL and optional source…", ct => Task.Run(() => report = CorpSec.Analyze(library.Root, corpProject, ct), ct));
        if (report == null) return;
        LoadCorpProject(); status.Text = report.RecoveryReady ? "Recovery declaration is complete. In-game recovery is not tested." : "Analysis complete. Review the listed actions."; RefreshPage();
    }

    private void MarkCorpRecovery(string intent)
    {
        if (corpProject == null || corpProject.RecoveryIntent == intent) return;
        corpProject.RecoveryIntent = intent; corpProject.RecoveryReviewed = intent != "unknown"; corpProject.SupportSource = "author";
        CorpSec.Save(corpProject); _ = CorpSec.Analyze(library.Root, corpProject); corpReport = null;
        status.Text = intent == "unknown" ? "Recovery remains incomplete until this question is answered." : "Recovery storage answer saved.";
        RefreshFindings(); RefreshPage();
    }

    private void RefreshFindings()
    {
        corpFindingList.Items.Clear();
        if (corpProject == null || corpProject.Findings.Count == 0)
        {
            bool unanalyzed = corpProject == null || corpProject.RecoveryIntent == "unknown" || !corpProject.RecoveryReviewed;
            corpFindingList.Items.Add(new ListBoxItem { IsEnabled = false, Content = Label(unanalyzed ? "Choose Analyze this build to detect facts and required author actions." : "No unresolved author questions were found.", 13, unanalyzed ? TerminalTheme.Gold : TerminalTheme.Success, margin: new Thickness(8)) }); return;
        }
        foreach (var finding in corpProject.Findings.OrderBy(f => FindingOrder(f.Severity)).ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase))
        {
            var content = new StackPanel();
            var brush = finding.Severity == "Action" ? TerminalTheme.Danger : finding.Severity == "Success" ? TerminalTheme.Success : TerminalTheme.Text;
            content.Children.Add(Label(finding.Severity + " · " + finding.Title, 13, brush, true, new Thickness(0, 0, 0, 2)));
            content.Children.Add(Label(finding.Detail, 11, TerminalTheme.Muted, margin: new Thickness(0)));
            corpFindingList.Items.Add(new ListBoxItem { Tag = finding.Id, Content = content, Padding = new Thickness(8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch, [ToolTip.TipProperty] = "Source: " + finding.Source });
        }
    }

    private static int FindingOrder(string severity) => severity switch { "Action" => 0, "Suggestion" => 1, "Warning" => 2, _ => 3 };

    private async Task ReviewSelectedFinding()
    {
        if (corpProject == null || corpFindingList.SelectedItem is not ListBoxItem { Tag: string id }) { status.Text = "Select a detected file suggestion first."; return; }
        var finding = corpProject.Findings.Single(f => f.Id == id);
        if (finding.CandidateRoot.Length == 0 || finding.CandidatePattern.Length == 0) { status.Text = "This item has no file rule to review. Follow the action shown in its details."; return; }
        await EditCorpCompanion(new(finding.CandidateRoot, SuggestedPattern(finding.CandidatePattern), "raw", false, "opaque", Provenance: finding.Source, Confirmed: true));
    }

    private static string SuggestedPattern(string value)
    {
        string path = value.Replace('\\', '/').TrimStart('/');
        if (path.Contains("{slot}", StringComparison.Ordinal)) return path;
        string extension = Path.GetExtension(path); return extension.Length == 0 ? path.TrimEnd('/') + "/{slot}.json" : path[..^extension.Length] + "-{slot}" + extension;
    }

    private void RefreshCapabilities()
    {
        corpCapabilityList.Items.Clear();
        if (corpProject == null || corpProject.Capabilities.Count == 0) { corpCapabilityList.Items.Add(new ListBoxItem { IsEnabled = false, Content = Label("No recovery capabilities declared.", 13, TerminalTheme.Muted, margin: new Thickness(8)) }); return; }
        for (int i = 0; i < corpProject.Capabilities.Count; i++)
        {
            var capability = corpProject.Capabilities[i]; var content = new StackPanel();
            content.Children.Add(Label(capability.Name, 13, TerminalTheme.Text, true, new Thickness(0, 0, 0, 2)));
            content.Children.Add(Label("Preferred: " + capability.Preferred + (capability.Fallbacks.Length == 0 ? "" : "  ·  fallbacks: " + string.Join(", ", capability.Fallbacks)) + "  ·  " + capability.Provenance, 11, TerminalTheme.Muted, margin: new Thickness(0)));
            corpCapabilityList.Items.Add(new ListBoxItem { Tag = i, Content = content, Padding = new Thickness(8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
    }

    private async Task EditSelectedCorpCapability() { if (corpCapabilityList.SelectedItem is ListBoxItem { Tag: int index } && corpProject != null) await EditCorpCapability(corpProject.Capabilities[index], index); else status.Text = "Select a capability first."; }
    private void RemoveSelectedCorpCapability()
    {
        if (corpCapabilityList.SelectedItem is not ListBoxItem { Tag: int index } || corpProject == null) { status.Text = "Select a capability first."; return; }
        string name = corpProject.Capabilities[index].Name; corpProject.Capabilities.RemoveAt(index);
        corpProject.Adapters = corpProject.Adapters.Select(a => a with { Capabilities = a.Capabilities.Where(c => c != name).ToArray(), Provenance = "author" }).ToList();
        corpProject.SupportSource = "author"; CorpSec.Save(corpProject); _ = CorpSec.Analyze(library.Root, corpProject); RefreshCapabilities(); RefreshAdapters(); RefreshFindings(); status.Text = "Capability removed.";
    }

    private Task EditCorpCapability(CorpSecCapability? existing) => EditCorpCapability(existing, null);
    private async Task EditCorpCapability(CorpSecCapability? existing, int? index)
    {
        if (preview || corpProject == null) return;
        var name = new TextBox { Text = existing?.Name ?? "module" };
        string[] implementations = new[] { "native", "dormant" }.Concat(corpProject.Adapters.Select(a => a.Id)).Distinct(StringComparer.Ordinal).ToArray();
        var preferred = new ComboBox { ItemsSource = implementations, SelectedItem = implementations.Contains(existing?.Preferred) ? existing!.Preferred : implementations[0] };
        var fallbackChecks = implementations.Select(value => new CheckBox { Content = value, IsChecked = existing?.Fallbacks.Contains(value) == true }).ToArray();
        var dependencyChecks = corpProject.Capabilities.Where(c => c.Name != existing?.Name).Select(value => new CheckBox { Content = value.Name, IsChecked = existing?.Dependencies.Contains(value.Name) == true }).ToArray();
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 6 }; content.Children.Add(Field("Capability name", name)); content.Children.Add(Field("Preferred implementation", preferred)); content.Children.Add(Field("Fallbacks", Checks(fallbackChecks))); content.Children.Add(Field("Depends on", Checks(dependencyChecks)));
        var window = Dialog(existing == null ? "Add capability" : "Edit capability", content, 540); CorpSecCapability? result = null; var buttons = Row();
        buttons.Children.Add(Button("Save capability", () =>
        {
            string token = name.Text?.Trim() ?? ""; if (!AuthorToken(token)) { status.Text = "Use lowercase letters, numbers, dots, underscores or dashes for the capability name."; return; }
            string choice = preferred.SelectedItem as string ?? "native"; result = new(token, choice, fallbackChecks.Where(c => c.IsChecked == true && (string?)c.Content != choice).Select(c => (string)c.Content!).ToArray(), dependencyChecks.Where(c => c.IsChecked == true).Select(c => (string)c.Content!).ToArray(), "author"); window.Close();
        }, primary: true)); buttons.Children.Add(Button("Cancel", () => window.Close())); content.Children.Add(buttons); await window.ShowDialog(this);
        if (result == null) return;
        if (corpProject.Capabilities.Where((_, i) => i != index).Any(c => c.Name == result.Name)) { status.Text = "That capability already exists."; return; }
        if (index.HasValue) corpProject.Capabilities[index.Value] = result; else corpProject.Capabilities.Add(result);
        corpProject.SupportSource = "author"; CorpSec.Save(corpProject); _ = CorpSec.Analyze(library.Root, corpProject); RefreshCapabilities(); RefreshFindings(); status.Text = "Capability saved.";
    }

    private void RefreshAdapters()
    {
        corpAdapterList.Items.Clear();
        if (corpProject == null || corpProject.Adapters.Count == 0) { corpAdapterList.Items.Add(new ListBoxItem { IsEnabled = false, Content = Label("No custom adapter DLLs registered.", 13, TerminalTheme.Muted, margin: new Thickness(8)) }); return; }
        for (int i = 0; i < corpProject.Adapters.Count; i++)
        {
            var adapter = corpProject.Adapters[i]; var content = new StackPanel(); content.Children.Add(Label(adapter.Id, 13, TerminalTheme.Text, true, new Thickness(0, 0, 0, 2))); content.Children.Add(Label(adapter.Type + "  ·  " + string.Join(", ", adapter.Capabilities) + "  ·  " + adapter.Provenance, 11, TerminalTheme.Muted, margin: new Thickness(0)));
            corpAdapterList.Items.Add(new ListBoxItem { Tag = i, Content = content, Padding = new Thickness(8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch, [ToolTip.TipProperty] = adapter.Source });
        }
    }

    private async Task EditSelectedCorpAdapter() { if (corpAdapterList.SelectedItem is ListBoxItem { Tag: int index } && corpProject != null) await EditCorpAdapter(corpProject.Adapters[index], index); else status.Text = "Select an adapter first."; }
    private void RemoveSelectedCorpAdapter()
    {
        if (corpAdapterList.SelectedItem is not ListBoxItem { Tag: int index } || corpProject == null) { status.Text = "Select an adapter first."; return; }
        string id = corpProject.Adapters[index].Id; corpProject.Adapters.RemoveAt(index);
        corpProject.Capabilities = corpProject.Capabilities.Select(c => c with { Preferred = c.Preferred == id ? "native" : c.Preferred, Fallbacks = c.Fallbacks.Where(f => f != id).ToArray(), Provenance = "author" }).ToList();
        corpProject.SupportSource = "author"; CorpSec.Save(corpProject); _ = CorpSec.Analyze(library.Root, corpProject); RefreshAdapters(); RefreshCapabilities(); RefreshFindings(); status.Text = "Adapter removed.";
    }

    private Task EditCorpAdapter(CorpSecAdapter? existing) => EditCorpAdapter(existing, null);
    private async Task EditCorpAdapter(CorpSecAdapter? existing, int? index)
    {
        if (preview || corpProject == null) return;
        string? source = existing?.Source ?? (await OpenFiles("Select the recovery adapter DLL", false, "*.dll")).FirstOrDefault(); if (source == null) return;
        var id = new TextBox { Text = existing?.Id ?? Path.GetFileNameWithoutExtension(source).ToLowerInvariant().Replace('_', '-') };
        var type = new TextBox { Text = existing?.Type ?? "MyMod.LuzRecoveryAdapterV1" };
        var capabilityChecks = corpProject.Capabilities.Select(value => new CheckBox { Content = value.Name, IsChecked = existing?.Capabilities.Contains(value.Name) == true }).ToArray();
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 6 }; content.Children.Add(Label(source, 11, TerminalTheme.Muted)); content.Children.Add(Field("Adapter ID", id)); content.Children.Add(Field("Public adapter type", type)); content.Children.Add(Field("Capabilities implemented", Checks(capabilityChecks)));
        var window = Dialog(existing == null ? "Add recovery adapter" : "Edit recovery adapter", content, 560); CorpSecAdapter? result = null; var buttons = Row();
        buttons.Children.Add(Button("Save adapter", () =>
        {
            string token = id.Text?.Trim() ?? "", className = type.Text?.Trim() ?? ""; if (!AuthorToken(token) || className.Length == 0) { status.Text = "Enter a valid adapter ID and public type name."; return; }
            string[] selected = capabilityChecks.Where(c => c.IsChecked == true).Select(c => (string)c.Content!).ToArray(); if (selected.Length == 0) { status.Text = "Choose at least one implemented capability."; return; }
            result = new(token, Path.GetFullPath(source), className, selected, "author"); window.Close();
        }, primary: true)); buttons.Children.Add(Button("Cancel", () => window.Close())); content.Children.Add(buttons); await window.ShowDialog(this);
        if (result == null) return;
        if (corpProject.Adapters.Where((_, i) => i != index).Any(a => a.Id == result.Id)) { status.Text = "That adapter ID already exists."; return; }
        if (index.HasValue) corpProject.Adapters[index.Value] = result; else corpProject.Adapters.Add(result);
        corpProject.RecoveryIntent = "custom"; corpProject.RecoveryReviewed = true; corpProject.SupportSource = "author"; CorpSec.Save(corpProject); _ = CorpSec.Analyze(library.Root, corpProject); RefreshAdapters(); RefreshFindings(); status.Text = "Recovery adapter saved.";
    }

    private static StackPanel Checks(IEnumerable<CheckBox> checks) { var panel = new StackPanel { Spacing = 3 }; foreach (var check in checks) panel.Children.Add(check); return panel; }
    private static bool AuthorToken(string value) => Regex.IsMatch(value, "^[a-z0-9][a-z0-9._-]{0,95}$", RegexOptions.CultureInvariant);
}
