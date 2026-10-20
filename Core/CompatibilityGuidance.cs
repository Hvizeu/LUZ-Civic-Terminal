namespace Luz;

public sealed record CompatibilityPrompt(string Title, string Message, string ContinueLabel);

public static class CompatibilityGuidance
{
    public static CompatibilityPrompt Create(Library library, CompatibilityReport report)
    {
        string operation = report.Action == "Play" ? "launch the game" : "apply the profile";
        string continueLabel = report.Action == "Play" ? "Launch anyway" : "Apply anyway";
        string cancelResult = report.Action == "Play" ? "The game will not launch." : "No game files will be changed.";
        var subject = Subject(library, report);
        bool blocked = report.Findings.Any(f => f.Severity == "Error" && !f.CanOverride);
        bool dynamicOnly = report.Findings.Count > 0 && report.Findings.All(f => f.Code == "DynamicContractsUnverified");
        bool bindingProblem = report.Findings.Any(f => f.Code == "BindingsUnverified");

        string title = blocked ? "Compatibility problem" : "Compatibility not confirmed";
        string explanation;
        if (dynamicOnly)
        {
            explanation = $"LUZ has not verified {subject.Text} for this exact Nivalis setup.\n\n"
                + "What this means\n"
                + $"This warning does not mean {(subject.Plural ? "the mods are" : "the mod is")} broken. LUZ checked the normal game references stored in {(subject.Plural ? "these mods" : "the mod")} and found no known broken reference. {(subject.Plural ? "These mods do" : "The mod does")} not include the extra information LUZ needs to check game calls created while running, so LUZ cannot promise that every feature works.\n\n"
                + "What should I do?\n"
                + $"• If this exact setup is already working, choose \"{continueLabel}\". LUZ will remember the decision and ask again only if the game, mod loader, selected profile, mod files, or relevant settings change.\n"
                + $"• If you do not want to {operation} with an unverified setup, choose \"Cancel\". {cancelResult}\n\n"
                + "How this warning gets removed\n"
                + $"{(subject.Plural ? "The mod authors or the LUZ maintainer" : "The mod author or the LUZ maintainer")} must test {subject.Text} against the current Nivalis build and add it to LUZ's supported list. If you are a player, report this on the mod's page and send the support code shown below.";
        }
        else if (blocked)
        {
            explanation = $"LUZ found a problem that must be fixed before it can {operation}. Nothing has been changed.\n\n"
                + "Use the action named in the support details below. If that does not resolve it, open Maintenance, export diagnostics, and send the archive to the LUZ maintainer.";
        }
        else
        {
            explanation = $"LUZ could not fully verify {subject.Text} for this exact Nivalis setup. This does not prove the mod is broken, but the check found something that needs your decision.\n\n"
                + $"Choose \"{continueLabel}\" only if you want to {operation} with this exact setup. LUZ will remember that choice and ask again only after a relevant file or setting changes.\n\n"
                + $"Choose \"Cancel\" to go back. {cancelResult}\n\n"
                + "If the mod fails in game, open Maintenance, export diagnostics, and send the archive to the LUZ maintainer.";
        }
        if (bindingProblem)
            explanation += "\n\nRecommended action: open Maintenance and choose Refresh game bindings, then try again.";

        string details = string.Join("\n", report.Findings.Select(f => $"Support code: {f.Code}\nFile: {f.Plugin}"));
        return new(title, explanation + (details.Length == 0 ? "" : "\n\nSupport details\n" + details), continueLabel);
    }

    private sealed record AffectedSubject(string Text, bool Plural);
    private static AffectedSubject Subject(Library library, CompatibilityReport report)
    {
        var enabled = library.Active.Mods.Where(m => m.Enabled).Select(m => library.State.Packages.Single(p => p.Id == m.PackageId)).ToArray();
        var packages = report.Findings.Select(f => enabled.FirstOrDefault(p => p.Files.Any(file => file.Equals(f.Plugin, StringComparison.OrdinalIgnoreCase))))
            .Where(p => p != null).DistinctBy(p => p!.Id).Cast<ModPackage>().ToArray();
        if (packages.Length == 1)
        {
            string version = packages[0].Version is "" or "0.0.0" ? "" : " " + packages[0].Version;
            return new(packages[0].Name + version, false);
        }
        if (packages.Length > 1) return new(packages.Length + " enabled mods", true);
        string file = report.Findings.Select(f => Path.GetFileNameWithoutExtension(f.Plugin.Replace('/', Path.DirectorySeparatorChar))).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "an enabled mod";
        return new(file, false);
    }
}
