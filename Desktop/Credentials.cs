using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LuzDesktop;
public sealed class Credentials(string root)
{
    private string KeyPath => Path.Combine(root, "nexus-key.dat");
    public string SessionKey { get; private set; } = "";
    public static string StorageName => OperatingSystem.IsWindows() ? "Windows account encryption" : OperatingSystem.IsMacOS() ? "macOS Keychain" : "your desktop Secret Service (requires secret-tool)";
    private static string Valid(string key) => !string.IsNullOrWhiteSpace(key) && !key.Any(char.IsControl) ? key.Trim() : throw new InvalidDataException("Paste a nonempty, single-line Nexus API key.");
    public void UseForSession(string key) => SessionKey = Valid(key);
    public async Task Save(string key)
    {
        key = Valid(key);
        if (OperatingSystem.IsWindows()) File.WriteAllBytes(KeyPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        else if (OperatingSystem.IsMacOS()) await Task.Run(() => MacKeychain.Save(key));
        else await SecretTool("store", key);
        SessionKey = key;
    }
    public async Task<string> Read()
    {
        if (SessionKey.Length > 0) return SessionKey;
        string key;
        if (OperatingSystem.IsWindows()) key = File.Exists(KeyPath) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser)) : "";
        else if (OperatingSystem.IsMacOS()) key = await Task.Run(MacKeychain.Read);
        else key = await SecretTool("lookup");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Add your Nexus API key in Maintenance, or import a downloaded ZIP.");
        SessionKey = key; return key;
    }
    public async Task Remove()
    {
        SessionKey = "";
        if (OperatingSystem.IsWindows()) File.Delete(KeyPath);
        else if (OperatingSystem.IsMacOS()) await Task.Run(MacKeychain.Remove);
        else await SecretTool("clear");
    }
    private static async Task<string> SecretTool(string operation, string? secret = null)
    {
        var request = new ProcessStartInfo("secret-tool") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        request.ArgumentList.Add(operation);
        if (operation == "store") request.ArgumentList.Add("--label=LUZ Civic Terminal Nexus API key");
        foreach (string value in new[] { "application", "LUZCivicTerminal", "service", "nexus-api" }) request.ArgumentList.Add(value);
        Process process;
        try { process = Process.Start(request) ?? throw new IOException("The secret store did not start."); }
        catch (Win32Exception) { throw new IOException("Secret Service is unavailable. Install secret-tool and a desktop keyring, or choose Use for session."); }
        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            if (secret != null) await process.StandardInput.WriteAsync(secret); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(); throw new IOException("The keyring request timed out. Unlock the keyring or choose Use for session."); }
            string error = await errors;
            if (process.ExitCode != 0 && !(operation == "lookup" && error.Length == 0)) throw new IOException("The keyring could not complete the request. Unlock your desktop keyring or choose Use for session.");
            return (await output).TrimEnd('\r', '\n');
        }
    }
}

internal static class MacKeychain
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("LUZCivicTerminal"), Account = Encoding.UTF8.GetBytes("nexus-api");
    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint length, out IntPtr data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint length, byte[] data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);
    [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr item);
    private static int Find(out uint length, out IntPtr data, out IntPtr item) => SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)Account.Length, Account, out length, out data, out item);
    private static void Check(int result) { if (result != 0) throw new IOException("macOS Keychain could not complete the request (status " + result + "). Unlock the keychain or choose Use for session."); }
    public static string Read()
    {
        int result = Find(out uint length, out var data, out var item);
        if (result == -25300) return "";
        try { Check(result); return Marshal.PtrToStringUTF8(data, checked((int)length)) ?? ""; }
        finally { if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data); if (item != IntPtr.Zero) CFRelease(item); }
    }
    public static void Save(string key)
    {
        byte[] value = Encoding.UTF8.GetBytes(key);
        int result = Find(out _, out var data, out var item);
        try
        {
            if (result == -25300) Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)Account.Length, Account, (uint)value.Length, value, out item));
            else { Check(result); Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)value.Length, value)); }
        }
        finally { CryptographicOperations.ZeroMemory(value); if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data); if (item != IntPtr.Zero) CFRelease(item); }
    }
    public static void Remove()
    {
        int result = Find(out _, out var data, out var item);
        if (result == -25300) return;
        try { Check(result); Check(SecKeychainItemDelete(item)); }
        finally { if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data); if (item != IntPtr.Zero) CFRelease(item); }
    }
}
