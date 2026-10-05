using System.Collections.Concurrent;
using Luz;

internal static class NxmChecks
{
    public static async Task Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        UiThreadLifecycleChecks.Run(root, check);
        string fresh = "nxm://nivalisnights/mods/12/files/34?key=one%2Btwo&expires=4102444800&user_id=5";
        var link = NxmLink.Parse(fresh); link.RequireFresh();
        check(link.ModId == 12 && link.FileId == 34 && link.DownloadQuery == "?key=one%2Btwo&expires=4102444800", "Signed NXM token decoded and safely re-encoded without user_id");
        check(!link.ToString().Contains("one") && !link.ToString().Contains("key="), "NXM display text omits tokens");
        foreach (string value in new[] {
            "nxm://skyrimspecialedition/mods/12/files/34", "nxm://nivalisnights/collections/test", "nxm://user@nivalisnights/mods/12/files/34",
            "nxm://nivalisnights:42/mods/12/files/34", "nxm://nivalisnights/mods/0/files/34", "nxm://nivalisnights/mods/9999999999999999999999/files/34",
            "nxm://nivalisnights/mods/12/files/34#fragment", "nxm://nivalisnights/mods/12/files/34?key=a", "nxm://nivalisnights/mods/12/files/34?key=a&key=b&expires=4102444800",
            "nxm://nivalisnights/mods/12/files/34?key=%0A&expires=4102444800", "nxm://nivalisnights/mods/12/files/34?path=evil", "nxm://nivalisnights/mods/12/files/34\" --launch" })
            reject(() => NxmLink.Parse(value), "Reject unsupported or malformed NXM address");
        reject(() => NxmLink.Parse("nxm://nivalisnights/mods/12/files/34?key=old&expires=123").RequireFresh(), "Expired signed link requires a new browser download");
        var queue = new NxmQueue(); queue.Add(link); queue.Add(NxmLink.Parse(fresh.Replace("one%2Btwo", "new")));
        check(queue.Count == 1 && queue.Peek()!.DownloadQuery.Contains("new"), "Repeated clicks refresh one pending file's token");
        for (int i = 35; i < 54; i++) queue.Add(NxmLink.Parse("nxm://nivalisnights/mods/12/files/" + i));
        reject(() => queue.Add(NxmLink.Parse("nxm://nivalisnights/mods/12/files/99")), "Bounded incoming download queue");
        queue.Remove(); check(queue.Peek()!.FileId == 35 && queue.Count == 19, "Queued downloads remain in arrival order");
        string executable = Path.Combine(root, "Folder with spaces", "LUZ Civic Terminal.exe");
        check(NxmRegistration.WindowsCommand(executable) == "\"" + executable + "\" --nxm \"%1\"", "Windows handler quotes executable and URL independently");
        string desktop = NxmRegistration.DesktopEntry(Path.Combine(root, "Folder%with$spaces", "LUZ Civic Terminal"));
        check(desktop.Contains("%%with\\\\$spaces") && desktop.Contains("--nxm %u") && desktop.Contains("x-scheme-handler/nxm;"), "Linux desktop entry escapes literal field codes and shell characters");
        var delivered = new ConcurrentQueue<string>(); var errors = new ConcurrentQueue<string>();
        using (var inbox = new LinkInbox(root, delivered.Enqueue, errors.Enqueue))
        {
            check(await LinkInbox.Forward(root, fresh, 3000), "Live instance acknowledges browser link over user-only pipe");
            check(await LinkInbox.Forward(root, "", 3000), "Second ordinary launch asks existing instance to focus");
            check(delivered.TryDequeue(out var received) && received == fresh && delivered.TryDequeue(out var focus) && focus == "", "IPC preserves signed link exactly and separates focus request");
            check(errors.IsEmpty, "Successful handoff produces no receiver errors");
            var request = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in new[] { typeof(NxmChecks).Assembly.Location, "--forward-nxm", root, fresh }) request.ArgumentList.Add(arg);
            using var child = System.Diagnostics.Process.Start(request)!;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await child.WaitForExitAsync(deadline.Token);
            check(child.ExitCode == 0 && delivered.TryDequeue(out var external) && external == fresh, "Separate process delivers link to the existing receiver without a second library");
        }
        check(!await LinkInbox.Forward(root, fresh, 50), "Closed receiver does not falsely acknowledge a link");
        using var sources = new Sources(new FakeHandler());
        var mod = new CatalogMod("12", "Fixture", "1", "", "", "", "", [], "Nexus", 35);
        try { await sources.NexusDownload(mod, "fixture-key", fresh); throw new Exception("Mismatched link was accepted"); }
        catch (InvalidDataException) { check(true, "Signed link cannot authorize a different Nexus file"); }
    }
}
