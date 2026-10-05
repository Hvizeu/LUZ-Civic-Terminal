using System.IO;
using System.Security.Cryptography;
using System.Text;
using Luz;

namespace LuzDesktop;
public sealed partial class MainWindow
{
    private Task<string> NexusKey() => credentials.Read();
    private Grid CataloguePage()
    {
        Detach(catalogSearch); Detach(catalogList);
        var grid = new Grid { Margin = new Thickness(26, 24, 24, 18) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new());
        var top = new StackPanel(); top.Children.Add(Label("Catalogue", 28, TerminalTheme.Text, bold: true)); top.Children.Add(Label("Download mods to this profile, then Apply when ready.", brush: TerminalTheme.Muted));
        var actions = Row(); actions.Children.Add(Button("Load Thunderstore", () => _ = Busy("Loading the Nivalis Nights catalogue…", async ct => { var result = await sources.Thunderstore(ct); catalog.Clear(); catalog.AddRange(result.Where(p => !Sources.IsLoader(p.Id))); await CacheIcons(ct); }), primary: true));
        actions.Children.Add(QuietButton("Find Nexus mod", async () => { var address = await Prompt("Nexus page, mod number or NXM link", nexusAddress); if (address != null) { nexusAddress = address; if (address.StartsWith("nxm:", StringComparison.OrdinalIgnoreCase)) { var link = NxmLink.Parse(address); link.RequireFresh(); catalogueLinks[link.Identity] = link.Address; } _ = Busy("Looking up Nexus files…", async ct => { var result = await sources.Nexus(address, await NexusKey(), ct); catalog.Clear(); catalog.AddRange(result); await CacheIcons(ct); }); } }));
        actions.Children.Add(QuietButton("Browse Nexus ↗", () => OpenUrl("https://www.nexusmods.com/games/nivalisnights/mods"))); top.Children.Add(actions); top.Children.Add(Label("Search catalogue", 13, TerminalTheme.Muted)); top.Children.Add(catalogSearch); grid.Children.Add(top);
        var frame = TerminalTheme.Section("Available modifications", catalogList); Grid.SetRow(frame, 1); grid.Children.Add(frame); RefreshCatalog(); return grid;
    }
    private async Task CacheIcons(CancellationToken ct)
    {
        foreach (var p in catalog.Take(150))
        {
            string path = Path.Combine(library.Root, "icons", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Id + p.Source))) + ".png");
            if (File.Exists(path) || string.IsNullOrWhiteSpace(p.IconUrl)) continue;
            try { await sources.Download(p.IconUrl, path, ct, 5_242_880); }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or UriFormatException) { /* A monogram remains available when a source image fails. */ }
        }
    }
    private void RefreshCatalog()
    {
        catalogList.Items.Clear();
        foreach (var p in catalog.Where(p => (p.Name + " " + p.Description).Contains(catalogSearch.Text ?? "", StringComparison.OrdinalIgnoreCase)))
        {
            var row = new Grid { Margin = new Thickness(12), MinHeight = 85 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(62) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(126) });
            string iconPath = Path.Combine(library.Root, "icons", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Id + p.Source))) + ".png"); row.Children.Add(ModIcon(iconPath, p.Name));
            var text = new StackPanel { Margin = new Thickness(0, 0, 18, 0) }; text.Children.Add(Label(p.Name, 18, bold: true)); text.Children.Add(Label(p.Source + " · " + p.Version + (p.FileId != 0 ? " · File " + p.FileId : ""), 12, TerminalTheme.Gold)); text.Children.Add(Label(p.Description, 13, TerminalTheme.Muted)); Grid.SetColumn(text, 1); row.Children.Add(text);
            var actions = new StackPanel(); actions.Children.Add(Button("Install", () => InstallCatalog(p), "Download to the current profile. Apply later to change game files.", true)); actions.Children.Add(QuietButton("Mod page ↗", () => OpenUrl(p.PageUrl))); Grid.SetColumn(actions, 2); row.Children.Add(actions);
            catalogList.Items.Add(new ListBoxItem { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = TerminalTheme.Panel, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(0) });
        }
        if (catalogList.Items.Count == 0) catalogList.Items.Add(new ListBoxItem { IsEnabled = false, Content = Label(catalog.Count == 0 ? "Choose Load Thunderstore or Find Nexus mod above.\n\nAlready have a ZIP? Import it from Registry." : "No mods match this search.", 16, TerminalTheme.Muted, margin: new Thickness(18)), HorizontalContentAlignment = HorizontalAlignment.Stretch });
    }
    private async Task InstallCatalog(CatalogMod mod)
    {
        var plan = mod.Source == "Thunderstore" ? Sources.Resolve(mod, catalog) : [mod];
        if (!await Confirm("Add these packages to “" + library.Active.Name + "”?\n\n" + string.Join('\n', plan.Select(p => p.Name + " " + p.Version)) + "\n\nOnly install mods you trust. Mods execute code when the game starts. Game files change only after Apply profile.")) return;
        _ = Busy("Downloading packages and dependencies…", async ct => { foreach (var p in plan) { ct.ThrowIfCancellationRequested(); await DownloadMod(p, ct); } });
    }
    private async Task DownloadMod(CatalogMod mod, CancellationToken ct, string? explicitLink = null)
    {
        var present = library.Active.Mods.Select(e => (Entry: e, Package: library.State.Packages.First(p => p.Id == e.PackageId))).FirstOrDefault(p => p.Package.Source == mod.Source && p.Package.SourceId == mod.Id);
        string? selectedLink = explicitLink ?? catalogueLinks.GetValueOrDefault(mod.Id + "/" + mod.FileId);
        if (present.Package != null && selectedLink == null)
        {
            bool currentEnough = present.Package.Version == mod.Version;
            try { currentEnough |= ModPlanner.CompareVersions(present.Package.Version, mod.Version) >= 0; } catch (InvalidDataException) { }
            if (currentEnough) { present.Entry.Enabled = true; library.Save(); return; }
        }
        if (present.Entry?.Pinned == true) throw new InvalidOperationException(mod.Name + " is pinned. Unpin it before replacing its version.");
        string url = mod.Source == "Nexus" ? await sources.NexusDownload(mod, await NexusKey(), selectedLink ?? mod.Id, ct) : mod.DownloadUrl;
        string zip = FileSafety.Under(library.Root, "downloads/" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await sources.Download(url, zip, ct); ct.ThrowIfCancellationRequested();
            string importedFile = zip;
            using (var input = File.OpenRead(zip))
            {
                int first = input.ReadByte(), second = input.ReadByte();
                if (first == 'M' && second == 'Z') importedFile = Path.ChangeExtension(zip, ".dll");
                else if (first != 'P' || second != 'K') throw new InvalidDataException("This download is not a supported ZIP or DLL. RAR, 7z, collections and scripted installers are not supported.");
            }
            if (importedFile != zip) File.Move(zip, importedFile);
            else
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
                if (archive.Entries.Any(e => e.FullName.Replace('\\', '/').EndsWith("fomod/ModuleConfig.xml", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("This ZIP requires a FOMOD installer, which LUZ does not support. Use a simple BepInEx package instead.");
            }
            ModPackage p;
            try { p = await Task.Run(() => library.Import(importedFile, mod), ct); }
            finally { if (importedFile != zip) File.Delete(importedFile); }
            if (p.Icon.Length == 0 && Uri.TryCreate(mod.IconUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            {
                string icon = Path.Combine(library.PackageRoot(p.Id), "icon.png");
                try { await sources.Download(mod.IconUrl, icon, ct, 5_242_880); p.Icon = "icon.png"; library.Save(); }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException) { status.Text = "Package imported; its icon could not be downloaded."; }
            }
        }
        finally { File.Delete(zip); }
    }
    private async Task CheckUpdates()
    {
        List<CatalogMod> updates = []; List<string> notes = []; bool complete = false;
        await Busy("Checking published versions…", async ct =>
        {
            var all = await sources.Thunderstore(ct);
            foreach (var entry in library.Active.Mods)
            {
                var installed = library.State.Packages.First(p => p.Id == entry.PackageId); if (entry.Pinned) { notes.Add(installed.Name + ": pinned"); continue; }
                CatalogMod? update = null;
                if (installed.Source == "Thunderstore") update = all.FirstOrDefault(p => p.Id == installed.SourceId);
                else if (installed.Source == "Nexus")
                {

                    var files = await sources.Nexus(installed.SourceId, await NexusKey(), ct);
                    update = files.FirstOrDefault(p => p.Name == installed.Name);
                    if (update == null) notes.Add(installed.Name + ": review Nexus files manually; the file name changed");
                }
                else { notes.Add(installed.Name + ": local package, update by importing a new ZIP"); continue; }
                if (update != null)
                {
                    try { if (ModPlanner.CompareVersions(update.Version, installed.Version) > 0) updates.Add(update); }
                    catch (InvalidDataException) { notes.Add(installed.Name + ": nonstandard version, review its mod page manually"); }
                }
            }
            catalog.Clear(); catalog.AddRange(all);
            complete = true;
        });
        if (!complete) return;
        if (updates.Count == 0) { await Notice("Update check", "No newer comparable versions found.\n\n" + string.Join('\n', notes)); return; }
        if (!await Confirm("Download these updates?\n\n" + string.Join('\n', updates.Select(p => p.Name + " → " + p.Version)) + "\n\n" + string.Join('\n', notes) + "\n\nYour old package versions remain in the library. Apply the profile when ready.")) return;
        await Busy("Downloading updates…", async ct => { foreach (var update in updates) foreach (var mod in update.Source == "Thunderstore" ? Sources.Resolve(update, catalog) : [update]) { ct.ThrowIfCancellationRequested(); await DownloadMod(mod, ct); } });
    }
}
