namespace Luz;

public static class ModPlanner
{
    public static int CompareVersions(string left, string right)
    {
        var a = left.Split('+')[0].Split('-', 2); var b = right.Split('+')[0].Split('-', 2);
        if (!Version.TryParse(a[0], out var av) || !Version.TryParse(b[0], out var bv)) throw new InvalidDataException("Unrecognized version: " + left + " / " + right);
        int diff = new Version(av.Major, av.Minor, Math.Max(0, av.Build), Math.Max(0, av.Revision)).CompareTo(new Version(bv.Major, bv.Minor, Math.Max(0, bv.Build), Math.Max(0, bv.Revision)));
        if (diff != 0) return diff;
        if (a.Length != b.Length) return a.Length == 1 ? 1 : -1;
        if (a.Length == 1) return 0;
        var ap = a[1].Split('.'); var bp = b[1].Split('.');
        for (int i = 0; i < Math.Min(ap.Length, bp.Length); i++)
        {
            bool an = long.TryParse(ap[i], out long ai), bn = long.TryParse(bp[i], out long bi);
            int c = an && bn ? ai.CompareTo(bi) : an != bn ? (an ? -1 : 1) : StringComparer.Ordinal.Compare(ap[i], bp[i]);
            if (c != 0) return c;
        }
        return ap.Length.CompareTo(bp.Length);
    }
    public static (string Id, string Version) ParseDependency(string value)
    {
        int split = value.LastIndexOf('-');
        if (split < 1) throw new InvalidDataException("Invalid package dependency: " + value);
        return (value[..split], value[(split + 1)..]);
    }
    public static List<Issue> Check(Profile profile, IReadOnlyList<ModPackage> library)
    {
        List<Issue> issues = [];
        if (profile.Mods.Select(m => m.PackageId).Distinct().Count() != profile.Mods.Count) issues.Add(new("Error", "This profile contains duplicate package entries."));
        var active = profile.Mods.Where(m => m.Enabled).Select(m => library.FirstOrDefault(p => p.Id == m.PackageId)).ToArray();
        if (active.Any(p => p == null)) { issues.Add(new("Error", "A profile package is missing from the library.")); return issues; }
        var packages = active.Cast<ModPackage>().ToArray();
        var plugins = packages.SelectMany(p => p.Plugins.Select(plugin => (Package: p, Plugin: plugin))).ToArray();
        foreach (var group in plugins.GroupBy(p => p.Plugin.Guid, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) issues.Add(new("Error", "Duplicate plugin ID: " + group.Key));
        foreach (var pair in plugins)
        {
            foreach (var conflict in pair.Plugin.Incompatibilities.Where(c => plugins.Any(p => p.Plugin.Guid.Equals(c, StringComparison.OrdinalIgnoreCase))))
                issues.Add(new("Error", pair.Plugin.Name + " conflicts with " + conflict));
            foreach (var dependency in pair.Plugin.Dependencies)
            {
                var target = plugins.FirstOrDefault(p => p.Plugin.Guid.Equals(dependency.Guid, StringComparison.OrdinalIgnoreCase)).Plugin;
                if (target == null && !dependency.Optional) issues.Add(new("Error", pair.Plugin.Name + " needs " + dependency.Guid));
                else if (target != null && dependency.MinimumVersion.Length > 0)
                {
                    try { if (CompareVersions(target.Version, dependency.MinimumVersion) < 0) issues.Add(new("Error", pair.Plugin.Name + " needs " + dependency.Guid + " ≥ " + dependency.MinimumVersion)); }
                    catch (InvalidDataException ex) { issues.Add(new("Error", ex.Message)); }
                }
            }
        }
        foreach (var package in packages)
        foreach (var dep in package.RequiredPlugins)
        {
            var target = plugins.FirstOrDefault(x => x.Plugin.Guid.Equals(dep.Guid, StringComparison.OrdinalIgnoreCase)).Plugin;
            if (target == null) issues.Add(new("Error", package.Name + " needs " + dep.Guid + " ≥ " + dep.MinimumVersion));
            else if (dep.MinimumVersion.Length > 0)
                try { if (CompareVersions(target.Version, dep.MinimumVersion) < 0) issues.Add(new("Error", package.Name + " needs " + dep.Guid + " ≥ " + dep.MinimumVersion)); }
                catch (InvalidDataException ex) { issues.Add(new("Error", ex.Message)); }
        }
        foreach (var package in packages)
        foreach (var text in package.Dependencies)
        {
            try
            {
                var dep = ParseDependency(text);
                if (dep.Id.Equals("BepInEx-BepInExPack_IL2CPP", StringComparison.OrdinalIgnoreCase)) continue;
                var match = packages.FirstOrDefault(p => p.Source == "Thunderstore" && p.SourceId.Equals(dep.Id, StringComparison.OrdinalIgnoreCase));
                if (match == null) issues.Add(new("Error", package.Name + " needs Thunderstore package " + text));
                else if (CompareVersions(match.Version, dep.Version) < 0) issues.Add(new("Error", package.Name + " needs " + text + " or newer"));
            }
            catch (InvalidDataException ex) { issues.Add(new("Error", package.Name + ": " + ex.Message)); }
        }
        var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in packages)
        foreach (var file in p.Files.Where(f => !f.StartsWith("config/", StringComparison.OrdinalIgnoreCase)))
        {
            if (destinations.TryGetValue(file, out var previous)) issues.Add(new("Warning", $"{p.Name} overrides {previous}: {file}. Lower rows win shared files."));
            destinations[file] = p.Name;
        }
        try { Sort(profile, library); } catch (InvalidDataException ex) { issues.Add(new("Error", ex.Message)); }
        return issues.Distinct().ToList();
    }
    public static List<ModEntry> Sort(Profile profile, IReadOnlyList<ModPackage> library)
    {
        var ordered = new List<ModEntry>(); var visiting = new HashSet<string>(); var visited = new HashSet<string>();
        void Visit(ModEntry entry)
        {
            if (visited.Contains(entry.PackageId)) return;
            if (!visiting.Add(entry.PackageId)) throw new InvalidDataException("Dependency cycle detected. Review the enabled mods before applying.");
            var p = library.FirstOrDefault(p => p.Id == entry.PackageId) ?? throw new InvalidDataException("Missing package in profile.");
            if (entry.Enabled)
            {
                var ids = p.Plugins.SelectMany(p => p.Dependencies).Concat(p.RequiredPlugins).Select(d => d.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var sources = p.Dependencies.Select(d => ParseDependency(d).Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in profile.Mods.Where(m => m.Enabled && m.PackageId != entry.PackageId))
                {
                    var target = library.FirstOrDefault(p => p.Id == candidate.PackageId);
                    if (target != null && (target.Plugins.Any(p => ids.Contains(p.Guid)) || sources.Contains(target.SourceId))) Visit(candidate);
                }
            }
            visiting.Remove(entry.PackageId); visited.Add(entry.PackageId); ordered.Add(entry);
        }
        foreach (var m in profile.Mods) Visit(m);
        return ordered;
    }
}
