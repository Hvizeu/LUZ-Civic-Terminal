using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Luz;

public static class UpdateChecks
{
    public static async Task Run(string root, Action<bool,string> check, Action<Action,string> reject)
    {
        const string version="9.8.7", rid="win-x64";
        string asset=$"LUZ-Civic-Terminal-{version}-{rid}.zip";
        string url=$"https://github.com/{LauncherUpdates.Repository}/releases/download/v{version}/{asset}";
        byte[] Package(string runtime=rid,string dllVersion="real",string? extra=null) {
            using var buffer=new MemoryStream();
            using(var z=new ZipArchive(buffer,ZipArchiveMode.Create,true)) {
                string prefix=$"LUZ Civic Terminal {version} {rid}/";
                void Add(string name,byte[] content){var item=z.CreateEntry(prefix+name);item.ExternalAttributes=(0x8000 | (name.EndsWith(".exe")?0x1ED:0x1A4))<<16;using var entry=item.Open();entry.Write(content);}
                Add("LUZ Civic Terminal.exe",Encoding.UTF8.GetBytes("fixture; never execute"));
                Add("Core.dll",File.ReadAllBytes(typeof(LauncherUpdates).Assembly.Location));
                Add("LUZ Civic Terminal.dll",File.ReadAllBytes(dllVersion=="real"?Path.Combine(AppContext.BaseDirectory,"UpdateFixture.dll"):typeof(LauncherUpdates).Assembly.Location));
                Add("LUZ Civic Terminal.deps.json",JsonSerializer.SerializeToUtf8Bytes(new{runtimeTarget=new{name=".NETCoreApp,Version=v10.0/"+runtime}}));
                if(extra!=null)Add(extra,[1]);
            }
            return buffer.ToArray();
        }
        byte[] bytes=Package();string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        byte[] Feed(string? download=null,string digest="",bool draft=false,bool prerelease=false,string? target=null)=>JsonSerializer.SerializeToUtf8Bytes(new[]{new{tag_name="v"+version,draft,prerelease,assets=new[]{new{name=target??asset,browser_download_url=download??url,size=bytes.Length,digest=digest.Length==0?"sha256:"+hash:digest}}}});
        var selected=LauncherUpdates.Select(Feed(),"0.4.0",rid)!;
        check(selected.Version==version&&selected.Sha256==hash,"LUZ selects the matching newer release and GitHub digest");
        check(LauncherUpdates.Select(Feed(),version,rid)==null,"LUZ never reinstalls or downgrades its current version");
        check(LauncherUpdates.Select(Feed(draft:true),"0.4.0",rid)==null&&LauncherUpdates.Select(Feed(prerelease:true),"0.4.0",rid)==null,"Draft and prerelease updates are excluded");
        reject(()=>LauncherUpdates.Select(Feed(target:"wrong-platform.zip"),"0.4.0",rid),"Missing platform package is reported instead of claiming up-to-date");
        reject(()=>LauncherUpdates.Select(Encoding.UTF8.GetBytes("[{}]"),"0.4.0",rid),"Malformed release fields produce a recoverable update error");
        reject(()=>LauncherUpdates.Select(Feed(download:"https://example.com/payload.zip"),"0.4.0",rid),"Foreign repository download is rejected");
        reject(()=>LauncherUpdates.Select(Feed(digest:"sha256:bad"),"0.4.0",rid),"Missing or malformed update checksum is rejected");
        var now=DateTimeOffset.UtcNow;
        check(LauncherUpdates.Due(new(),now)&&!LauncherUpdates.Due(new(false),now)&&!LauncherUpdates.Due(new(true,now),now)&&LauncherUpdates.Due(new(true,now.AddDays(-2)),now),"Startup checks respect opt-out and 24-hour interval");
        using var handler=new UpdateHandler(Feed(),bytes);
        using var client=new HttpClient(handler);var updater=new LauncherUpdates(client);
        var remote=await updater.Check("0.4.0",rid,CancellationToken.None);
        check(remote==selected&&handler.Requests.Count==1,"Public GitHub API update check needs no credentials");
        string data=Path.Combine(root,"update-data");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"registry.json"),"preserve profile");
        string archive=await updater.Download(selected,data,CancellationToken.None);
        string prepared=LauncherUpdates.Prepare(selected,archive);
        string destination=Path.Combine(root,"installed-new");DesktopInstallation.InstallFiles(prepared,destination);
        check(File.ReadAllText(Path.Combine(data,"registry.json"))=="preserve profile"&&FileSafety.Hash(Path.Combine(prepared,"LUZ Civic Terminal.dll"))==FileSafety.Hash(Path.Combine(destination,"LUZ Civic Terminal.dll")),"Full update download, checksum, extraction and installation preserve profile data");
        DesktopInstallation.InstallFiles(prepared,destination);
        check(Directory.Exists(destination),"Interrupted shortcut step can retry an identical installed version");
        File.AppendAllText(Path.Combine(destination,"Core.dll"),"changed");
        reject(()=>DesktopInstallation.InstallFiles(prepared,destination),"Different existing version contents are preserved");
        handler.Asset=[..bytes.Skip(1)];
        await Rejected(()=>updater.Download(selected,data,CancellationToken.None),"Truncated update never installs",check);
        handler.Asset=[..bytes];handler.Asset[^1]^=1;
        await Rejected(()=>updater.Download(selected,data,CancellationToken.None),"Tampered update fails SHA-256 verification",check);
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();await Rejected(()=>updater.Download(selected,data,cancelled.Token),"Cancelled download is discarded",check);}
        check(Directory.GetDirectories(Path.Combine(data,"launcher-updates")).Length==1,"Failed downloads leave no partial update directories");
        foreach(var (payload,label) in new[]{(Package("linux-x64"),"Wrong dependency platform"),(Package(dllVersion:"wrong"),"Wrong application identity"),(Package(extra:"../escape.txt"),"Traversal archive")}) {
            string folder=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string file=Path.Combine(folder,"update.zip");File.WriteAllBytes(file,payload);
            var candidate=selected with {Size=payload.Length,Sha256=FileSafety.Hash(file)};
            reject(()=>LauncherUpdates.Prepare(candidate,file),label+" cannot be installed");
            check(!Directory.Exists(Path.Combine(folder,"unpacked")),label+" clears staging");
        }
        var request=LauncherUpdates.RestartRequest(destination,123,DesktopPlatform.Windows);
        check(request.ArgumentList.SequenceEqual(new[]{"--update-restart","123"})&&!request.UseShellExecute&&!request.FileName.Contains("Nivalis Nights"),"Restart request targets only LUZ and waits for its previous process");
        using(var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"--sleep-for-updater"}})!) {
            reject(()=>LauncherUpdates.WaitForPreviousProcess(child.Id,10),"Restart wait times out without killing an existing process");
            LauncherUpdates.WaitForPreviousProcess(child.Id,5000);check(child.HasExited,"New launcher waits until the old process releases its files and lock");
        }
        string game=Path.Combine(root,"game-update-fixture");Directory.CreateDirectory(Path.Combine(game,"BepInEx/core"));Directory.CreateDirectory(Path.Combine(game,"BepInEx/interop"));Directory.CreateDirectory(Path.Combine(game,"BepInEx/config"));Directory.CreateDirectory(Path.Combine(game,"BepInEx/plugins"));
        foreach(var relative in new[]{"Nivalis Nights.exe","GameAssembly.dll","BepInEx/core/BepInEx.Unity.IL2CPP.dll","BepInEx/interop/Assembly-CSharp.dll","BepInEx/plugins/keep.dll"})File.WriteAllText(Path.Combine(game,relative),"fixture; never execute");
        string cfg=Path.Combine(game,"BepInEx/config/BepInEx.cfg");File.WriteAllText(cfg,"[Other]\nKeep = value\n[IL2CPP]\nUpdateInteropAssemblies = false\nInteropAssemblyPath = {BepInEx}/interop\n");
        string report=GameUpdateRecovery.Inspect(game);check(report.Contains("disabled")&&report.Contains("compatible mod release"),"Diagnostics distinguish disabled binding refresh from incompatible mods");
        var backup=GameUpdateRecovery.Refresh(game);
        check(File.Exists(Path.Combine(backup,"interop/Assembly-CSharp.dll"))&&!Directory.Exists(Path.Combine(game,"BepInEx/interop"))&&File.ReadAllText(cfg).Contains("UpdateInteropAssemblies = true")&&File.ReadAllText(cfg).Contains("Keep = value")&&File.Exists(Path.Combine(game,"BepInEx/plugins/keep.dll")),"Game binding recovery backs up cache, enables regeneration and preserves mods/settings");
        File.WriteAllText(cfg,"[IL2CPP]\nInteropAssemblyPath = /custom/path\n");reject(()=>GameUpdateRecovery.Refresh(game),"Custom interop paths require manual review");
        reject(()=>GameUpdateRecovery.EnableRegeneration("[IL2CPP]\nUpdateInteropAssemblies=false\nUpdateInteropAssemblies=true"),"Ambiguous configuration is not silently rewritten");
    }
    private static async Task Rejected(Func<Task> action,string title,Action<bool,string> check){try{await action();}catch(Exception ex)when(ex is IOException or InvalidDataException or OperationCanceledException){check(true,title);return;}throw new Exception("Accepted: "+title);}
    private sealed class UpdateHandler(byte[] feed,byte[] asset):HttpMessageHandler {
        public byte[] Asset=asset;public List<string> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();if(request.Headers.Authorization!=null||request.Headers.Contains("apikey"))throw new Exception("Credentials leaked to updater");Requests.Add(request.RequestUri!.AbsoluteUri);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(request.RequestUri.Host=="api.github.com"?feed:Asset)});}
    }
}
