using Luz;
using Microsoft.Win32;

internal static class RegistrationRemovalChecks
{
    public static void Run(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) return;
        string fixture = @"Software\LUZ-RegistrationFixture-" + Guid.NewGuid().ToString("N");
        try
        {
            using var user = Registry.CurrentUser.CreateSubKey(fixture);
            void Write(string path, string name, string value) { using var key = user.CreateSubKey(path); key.SetValue(name, value); }
            string? Read(string path, string name) { using var key = user.OpenSubKey(path); return key?.GetValue(name) as string; }
            const string apps = @"Software\RegisteredApplications", capabilities = @"Software\LUZCivicTerminal\Capabilities";
            const string other = @"Software\Classes\nxm\shell\open\command", choice = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\nxm\UserChoice";
            Write(apps, "LUZ Civic Terminal", capabilities); Write(apps, "Other manager", "Other capabilities");
            Write(capabilities, "ApplicationName", "LUZ Civic Terminal");
            Write(@"Software\Classes\" + NxmRegistration.ProgId + @"\shell\open\command", "", "deleted LUZ executable");
            Write(other, "", "original manager command"); Write(choice, "ProgId", NxmRegistration.ProgId); Write(choice, "Hash", "protected-choice-fixture");
            Write(@"Software\LUZCivicTerminal", "Preserved", "user setting");
            NxmRegistration.RemoveWindowsRegistration(user);
            check(Read(apps, "LUZ Civic Terminal") == null && Read(capabilities, "ApplicationName") == null && Read(@"Software\Classes\" + NxmRegistration.ProgId + @"\shell\open\command", "") == null, "Remove stale LUZ registration even if installed executable is gone");
            check(Read(apps, "Other manager") == "Other capabilities" && Read(other, "") == "original manager command", "Cleanup preserves other managers and shared NXM command");
            check(Read(choice, "ProgId") == NxmRegistration.ProgId && Read(choice, "Hash") == "protected-choice-fixture", "Cleanup leaves Windows protected choice for user selection in Settings");
            check(Read(@"Software\LUZCivicTerminal", "Preserved") == "user setting", "Cleanup preserves unrelated LUZ settings");
            NxmRegistration.RemoveWindowsRegistration(user);
            check(Read(apps, "Other manager") == "Other capabilities", "Repeated registration cleanup is harmless");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(fixture, false); }
    }
}
