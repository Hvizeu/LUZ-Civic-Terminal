using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

namespace Luz;

internal static class BindingProvenance
{
    internal static string Verify(string game, Action check, string? effectiveConfig = null)
    {
        string bep = FileSafety.PortableDestination(game, "BepInEx"), core = Path.Combine(bep, "core"), libs = Path.Combine(bep, "unity-libs"), bindings = Path.Combine(bep, "interop");
        string marker = Path.Combine(bindings, "assembly-hash.txt");
        if (!File.Exists(marker) || !File.Exists(Path.Combine(bindings, "Assembly-CSharp.dll")) || !Directory.Exists(libs)) return "Bindings or generation inputs are missing. Refresh game bindings, then launch to regenerate them.";
        try
        {
            if(!FileSafety.Hash(Path.Combine(core,"BepInEx.Unity.IL2CPP.dll")).Equals("46CF1EF802BDF8CB6587FC1E4D98F7AF1073A60487B39A64E230E6F6E4C23AEC",StringComparison.OrdinalIgnoreCase))return "Loader build is not recognized; its generation recipe must be verified first.";
            using var loader = AssemblyDefinition.ReadAssembly(Path.Combine(core, "BepInEx.Unity.IL2CPP.dll"));
            var manager = loader.MainModule.Types.FirstOrDefault(t => t.Name == "Il2CppInteropManager");
            var compute = manager?.Methods.FirstOrDefault(m => m.Name == "ComputeHash");
            // Recognize the inspected BepInEx 6 recipe by its calls. A changed
            // loader remains unverified rather than being assigned this recipe.
            var calls = compute?.Body?.Instructions.Select(i => i.Operand?.ToString() ?? "").ToArray() ?? [];
            if (!calls.Any(c => c.Contains("MD5::Create")) || !calls.Any(c => c.Contains("EnumerateFiles")) || !calls.Any(c => c.Contains("Cpp2IL"))) return "Loader fingerprint recipe is not recognized.";
            string config = effectiveConfig ?? Path.Combine(bep, "config/BepInEx.cfg");
            string text = File.Exists(config) ? File.ReadAllText(config) : "";
            foreach (System.Text.RegularExpressions.Match setting in System.Text.RegularExpressions.Regex.Matches(text, @"(?im)^\s*(InteropAssemblyPath|IL2CPPInteropAssembliesPath)\s*=([^\r\n]*)"))
                if (setting.Groups[2].Value.Trim() is not ("{BepInEx}" or "{BepInEx}/interop")) return "Custom binding location requires a provenance adapter.";
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            void FileBytes(string path) { check(); FileSafety.NoLinks(path); using var stream = File.OpenRead(path); byte[] buffer = new byte[65536]; int count; while ((count = stream.Read(buffer)) > 0) { check(); md5.AppendData(buffer, 0, count); } }
            FileBytes(Path.Combine(game, "GameAssembly.dll"));
            // Match the loader's native enumeration order, deliberately unsorted.
            foreach (string dll in Directory.EnumerateFiles(libs, "*.dll", SearchOption.TopDirectoryOnly)) { md5.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(dll))); FileBytes(dll); }
            string rename = Path.Combine(bep, "DeobfuscationMap.csv.gz");
            if (File.Exists(rename)) FileBytes(rename);
            foreach (string file in new[] { "Il2CppInterop.Generator.dll", "Cpp2IL.Core.dll" })
            { using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(core, file)); md5.AppendData(Encoding.UTF8.GetBytes(assembly.Name.Version.ToString())); }
            string actual = Convert.ToHexString(md5.GetHashAndReset());
            return actual.Equals(File.ReadAllText(marker).Trim(), StringComparison.OrdinalIgnoreCase) ? "" : "Generation fingerprint differs from the current inputs. Refresh game bindings.";
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException) { return "Cannot verify generation inputs: " + ex.Message; }
    }
}
