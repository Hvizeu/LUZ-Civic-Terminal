using Mono.Cecil;
using System.Text.Json;

if (args.Length == 2 && args[0] == "--check-adapters")
{
    using var bindings = AssemblyDefinition.ReadAssembly(args[1]);
    var requirements = new[] {
        ("Nivalis.SerializationManager", "Save", "System.Boolean", new[] { "System.String", "System.Boolean" }),
        ("Nivalis.SerializationManager", "Load", "System.Void", new[] { "System.String" }),
        ("Nivalis.SerializationManager", "AutoSave", "System.Boolean", Array.Empty<string>()),
        ("Nivalis.SerializationManager", "DeleteSaveFiles", "System.Boolean", new[] { "System.String" }),
        ("Nivalis.TimeOfDayManager", "AddTime", "System.Void", new[] { "System.Single" }),
        ("Nivalis.InventorySystem.ItemContainer", "TryAdd", "Nivalis.InventorySystem.ItemCollectionOperationResult", new[] { "Nivalis.InventorySystem.ItemInstanceData" }),
        ("Nivalis.Economy.Vendor", "TryRegisterItem", "System.Void", new[] { "Nivalis.Economy.EconomyManager/VendorItemDefinition", "Nivalis.InventorySystem.ItemType" }) };
    foreach (var (type, method, result, parameters) in requirements)
    {
        var owner = bindings.MainModule.GetType(type) ?? throw new TypeLoadException(type);
        if (!owner.Methods.Any(m => m.Name == method && m.ReturnType.FullName == result && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters))) throw new MissingMethodException(type + "." + method);
        Console.WriteLine("PASS current native adapter target " + type + "." + method);
    }
    return;
}
if (args.Length < 3) throw new ArgumentException("Supply report.json, the pinned BepInEx core directory, then built universal DLLs.");
using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(Path.GetFullPath(args[1]));
var reports = new List<object>();
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Types(t.NestedTypes)));
foreach (string path in args.Skip(2))
{
    resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
    var metadata = assembly.CustomAttributes.Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" && a.ConstructorArguments.Count == 2).ToDictionary(a => (string)a.ConstructorArguments[0].Value, a => (string)a.ConstructorArguments[1].Value);
    bool Game(TypeReference type)
    {
        while (type is TypeSpecification specification) type = specification.ElementType;
        return type.Scope is AssemblyNameReference scope && (scope.Name == "Assembly-CSharp" || scope.Name.StartsWith("UnityEngine.") || scope.Name.StartsWith("Unity.") || scope.Name is "Unity.InputSystem" or "Data.Input" or "MagicaCloth");
    }
    var methods = Types(assembly.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
    var direct = methods.SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MemberReference>().Where(m => m is TypeReference type ? Game(type) : Game(m.DeclaringType)).Select(m => m.FullName).Distinct().Order().ToArray();
    var unsafeOwners = methods.Where(m => m.Body.Instructions.Any(i => i.OpCode.Name.StartsWith("ldind") || i.OpCode.Name.StartsWith("stind") || i.Operand?.ToString()?.Contains("ToPointer") == true || i.Operand?.ToString()?.Contains("Runtime.InteropServices.Marshal") == true)).Select(m => m.DeclaringType.FullName).Distinct().Order().ToArray();
    var mediated = methods.SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>().Where(m => m.DeclaringType.Name == "ContinuityClient").GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.Count());
    var plugins = Types(assembly.MainModule.Types).Where(t => t.CustomAttributes.Any(a => a.AttributeType.Name == "BepInPlugin")).ToArray();
    bool dependency = plugins.All(t => t.CustomAttributes.Any(a => a.AttributeType.Name == "BepInDependency" && a.ConstructorArguments.Count > 0 && a.ConstructorArguments[0].Value as string == "hvizeu.luz.continuity"));
    if (metadata.GetValueOrDefault("Nivalis.Continuity.Api") != "1" || !dependency) throw new InvalidDataException("Embedded Continuity API or optional host dependency is missing: " + assembly.Name.Name);
    reports.Add(new { Assembly = assembly.Name.Name, Version = assembly.Name.Version.ToString(3), Api = 1, OptionalHostDependency = dependency, MediatedCalls = mediated, DirectNativeReferences = direct, UnsafeOwners = unsafeOwners, RequiresExactBuild = direct.Length != 0 || unsafeOwners.Length != 0, NativeHash = metadata.GetValueOrDefault("Nivalis.NativeBuild"), MetadataHash = metadata.GetValueOrDefault("Nivalis.MetadataBuild") });
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new { Schema = 1, Modules = reports, NativeExecutionTested = false }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Coverage written for " + reports.Count + " universal modules. Residual native dependencies retain exact-build requirements.");
