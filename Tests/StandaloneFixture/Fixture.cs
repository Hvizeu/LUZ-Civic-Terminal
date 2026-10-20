namespace IndependentAuthorFixture;

[AttributeUsage(AttributeTargets.Class)]
public sealed class BepInPlugin(string guid, string name, string version) : Attribute { public string Identity => guid + name + version; }

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class BepInDependency(string guid, string version) : Attribute { public string Identity => guid + version; }

[BepInPlugin("fixture.plugin", "Fixture", "1.0.0")]
[BepInDependency("hard", "2.0.0")]
[BepInDependency("fixture.ranged", ">=1.03")]
public sealed class FixturePlugin
{
    public static void WriteState(string value) => File.WriteAllText("Fixture/{slot}.json", value);
}
