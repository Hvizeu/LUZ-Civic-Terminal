using Luz;

public static class PluginDependencyChecks
{
    public static void RunRangeChecks(Action<bool,string> check, Action<Action,string> reject)
    {
        foreach (var (version, range, expected) in new[] {
            ("1.03", ">=1.03", true), ("1.02", ">=1.03", false), ("1.04", ">=1.03", true),
            ("1.3.0", "1.3.0", true), ("1.4.0", "1.3.0", false),
            ("1.3.0", ">=1.2.0 <2.0.0", true), ("2.0.0", ">=1.2.0 <2.0.0", false),
            ("2.1.0", "^1.3.0 || ~2.1.0", true), ("2.2.0", "^1.3.0 || ~2.1.0", false),
            ("1.3.9", "1.3.*", true), ("1.4.0", "1.3.*", false),
            ("1.3.0-beta.2", ">=1.3.0-beta.1 <1.3.0", true), ("1.3.0-beta.2", ">=1.3.0", false),
            ("1.3.0.9", "1.3.0", true)
        }) check(ModPlanner.MatchesPluginDependency(version, range) == expected, $"Plugin range {version} / {range}: {expected}");
        reject(() => ModPlanner.MatchesPluginDependency("banana", ">=1.3"), "Invalid plugin version is rejected");
        reject(() => ModPlanner.MatchesPluginDependency("1.3.0", ">=banana"), "Invalid plugin range is rejected");
    }
    public static void Run(string root, Action<bool,string> check, Action<Action,string> reject)
    {
        RunRangeChecks(check, reject);
        string data = Path.Combine(root, "range-library"), game = Path.Combine(root, "range-game");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx/core"));
        foreach (string name in new[] { "Nivalis Nights.exe", "GameAssembly.dll", "BepInEx/core/BepInEx.Unity.IL2CPP.dll" }) File.WriteAllText(Path.Combine(game, name), "fixture; never execute");
        using (var library = new Library(data))
        {
            var dependency = PluginMetadata.Read(typeof(TestPlugin).Assembly.Location).Single().Dependencies.Single(d => d.Guid == "fixture.ranged");
            check(dependency.MinimumVersion == ">=1.03", "Metadata import preserves the BepInDependency range");
            var provider = new ModPackage { Name = "HUD dependency fixture", Plugins = [new("fixture.ranged", "HUD dependency fixture", "1.03", [], [])] };
            var consumer = new ModPackage { Name = "HUD range fixture", Plugins = [new("fixture.consumer", "HUD range fixture", "1.0.0", [dependency], [])] };
            library.State.Packages.AddRange([provider, consumer]);
            library.Active.Mods.AddRange([new() { PackageId = consumer.Id }, new() { PackageId = provider.Id }]);
            library.State.GameFolder = game; library.Save();
            check(!ModPlanner.Check(library.Active, library.State.Packages).Any(i => i.Severity == "Error"), "Reported 1.03 / >=1.03 profile passes validation");
            check(ModPlanner.Sort(library.Active, library.State.Packages)[0].PackageId == provider.Id, "Ranged dependencies still sort before their consumers");
            new Deployment(library).Apply();
            check(library.State.AppliedProfileId == library.Active.Id && library.State.AppliedFingerprint == library.Fingerprint(library.Active), "Ranged profile applies and satisfies the Play fingerprint gate without launching");
            provider.Plugins[0] = provider.Plugins[0] with { Version = "1.02" };
            check(ModPlanner.Check(library.Active, library.State.Packages).Any(i => i.Severity == "Error" && i.Message.Contains(">=1.03") && i.Message.Contains("1.02")), "Too-old dependency retains a useful blocking error");
            provider.Plugins[0] = provider.Plugins[0] with { Version = "1.03" };
            consumer.RequiredPlugins.Add(new("fixture.ranged", "1.2.0", false));
            check(!ModPlanner.Check(library.Active, library.State.Packages).Any(i => i.Severity == "Error"), "Object-pack minimum requirements keep their existing semantics");
            consumer.Plugins[0].Dependencies.Add(new("fixture.optional", ">=1.0.0", true));
            check(!ModPlanner.Check(library.Active, library.State.Packages).Any(i => i.Severity == "Error"), "Absent optional dependency does not block the profile");
            library.Save();
        }
        using var reopened = new Library(data);
        check(!ModPlanner.Check(reopened.Active, reopened.State.Packages).Any(i => i.Severity == "Error"), "Stored range metadata works after reopening an existing library");
    }
}
