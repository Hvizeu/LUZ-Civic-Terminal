using Luz;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NivalisRecovery;

internal static class ContractEdgeChecks
{
    internal static void Run(string root,Action<bool,string> check,Action<Action,string> reject)
    {
        string game=Path.Combine(root,"EdgeGame"),bindings=Path.Combine(game,"BepInEx/interop");Directory.CreateDirectory(bindings);
        File.WriteAllText(Path.Combine(game,"Nivalis Nights.exe"),"fixture");File.WriteAllText(Path.Combine(game,"GameAssembly.dll"),"fixture");
        using var dependency=AssemblyDefinition.CreateAssembly(new("ContractDependency",new(1,0)),"ContractDependency",ModuleKind.Dll);
        var m=dependency.MainModule;
        var parent=new TypeDefinition("Contract","Base",TypeAttributes.Public|TypeAttributes.Class,m.TypeSystem.Object);m.Types.Add(parent);
        var parameter=new GenericParameter("T",parent);parent.GenericParameters.Add(parameter);
        var inherited=new MethodDefinition("Inherited",MethodAttributes.Public,m.TypeSystem.Void);inherited.Parameters.Add(new(parameter));inherited.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));parent.Methods.Add(inherited);
        parent.Fields.Add(new("Value",FieldAttributes.Public,parameter));
        parent.Fields.Add(new("Plain",FieldAttributes.Public,m.TypeSystem.Boolean));
        var plain=new MethodDefinition("PlainMethod",MethodAttributes.Public,m.TypeSystem.Void);plain.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));parent.Methods.Add(plain);
        var concrete=new GenericInstanceType(parent);concrete.GenericArguments.Add(m.TypeSystem.Int32);
        var child=new TypeDefinition("Contract","Child",TypeAttributes.Public|TypeAttributes.Class,concrete);m.Types.Add(child);
        dependency.Write(Path.Combine(bindings,"ContractDependency.dll"));
        using(var facade=AssemblyDefinition.CreateAssembly(new("ContractFacade",new(1,0)),"ContractFacade",ModuleKind.Dll))
        {
            var dependencyScope=new AssemblyNameReference("ContractDependency",new(1,0));facade.MainModule.AssemblyReferences.Add(dependencyScope);
            facade.MainModule.ExportedTypes.Add(new ExportedType("Contract","Child",facade.MainModule,dependencyScope){Attributes=TypeAttributes.Public|TypeAttributes.Forwarder});
            facade.Write(Path.Combine(bindings,"ContractFacade.dll"));
        }
        string callerPath=Path.Combine(root,"EdgeCaller.dll");
        using(var caller=AssemblyDefinition.CreateAssembly(new("EdgeCaller",new(1,0)),"EdgeCaller",ModuleKind.Dll))
        {
            var c=caller.MainModule;var scope=new AssemblyNameReference("ContractFacade",new(1,0));c.AssemblyReferences.Add(scope);
            var targetType=new TypeReference("Contract","Child",c,scope);
            var owner=new TypeDefinition("Fixture","EdgeCaller",TypeAttributes.Public|TypeAttributes.Class,c.TypeSystem.Object);c.Types.Add(owner);
            var invoke=new MethodDefinition("Invoke",MethodAttributes.Public|MethodAttributes.Static,c.TypeSystem.Void);owner.Methods.Add(invoke);
            var importedBase=c.ImportReference(parent);var generic=new GenericInstanceType(importedBase);generic.GenericArguments.Add(c.TypeSystem.Int32);
            var method=new MethodReference("Inherited",c.TypeSystem.Void,generic){HasThis=true};method.Parameters.Add(new(importedBase.GenericParameters[0]));
            invoke.Body.Instructions.Add(Instruction.Create(OpCodes.Call,method));invoke.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld,new FieldReference("Value",importedBase.GenericParameters[0],generic)));
            invoke.Body.Instructions.Add(Instruction.Create(OpCodes.Call,new MethodReference("PlainMethod",c.TypeSystem.Void,targetType){HasThis=true}));invoke.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld,new FieldReference("Plain",c.TypeSystem.Boolean,targetType)));invoke.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            caller.Write(callerPath);
        }
        var selected=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase){["plugins/EdgeCaller.dll"]=callerPath};var snapshot=new CompatibilitySnapshot(game,"edges",false,selected);
        var report=Compatibility.InspectSnapshot(snapshot);
        try{CompatibilityWorker.Run(snapshot,CancellationToken.None,TimeSpan.FromMilliseconds(1));throw new InvalidOperationException("worker timeout ignored");}catch(TimeoutException){check(true,"independent scanner timeout cancels its disposable worker");}
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();try{CompatibilityWorker.Run(snapshot,canceled.Token);throw new InvalidOperationException("worker cancellation ignored");}catch(OperationCanceledException){check(true,"scanner admission honors cancellation without launching a worker");}}
        if(report.Findings.Any(f=>f.Code=="MissingMember"))Console.WriteLine(report.Describe());
        check(!report.Findings.Any(f=>f.Code=="MissingMember"),"inherited generic method and field references resolve without executing assemblies");
        check(report.Identities?.Any(i=>i.Identity.StartsWith("EdgeCaller,"))==true,"reports retain inspected assembly identity and digest");
        string config=Path.Combine(root,"Edge.cfg");File.WriteAllText(config,"before");selected["config/Edge.cfg"]=config;var before=Compatibility.InspectSnapshot(snapshot);File.WriteAllText(config,"after");var after=Compatibility.InspectSnapshot(snapshot);
        reject(()=>Compatibility.Authorize(after,Compatibility.Acknowledge(before)),"actual configuration changes expire the reviewed fingerprint");
        selected["plugins/Duplicate.dll"]=callerPath;report=Compatibility.InspectSnapshot(snapshot);
        check(report.Findings.Any(f=>f.Code=="AmbiguousAssembly"&&!f.CanOverride),"duplicate effective assembly identity cannot be overridden");selected.Remove("plugins/Duplicate.dll");
        string malformed=Path.Combine(root,"Malformed.dll");File.WriteAllText(malformed,"MZ broken metadata");selected["plugins/Malformed.dll"]=malformed;report=Compatibility.InspectSnapshot(snapshot);
        check(report.Findings.Any(f=>f.Code=="NativeContractUnverified"),"malformed metadata remains visibly unverified");selected.Remove("plugins/Malformed.dll");
        File.Delete(Path.Combine(bindings,"ContractDependency.dll"));report=Compatibility.InspectSnapshot(snapshot);
        check(report.Findings.Any(f=>f.Code=="MissingMember"&&f.Expected.Contains("ContractDependency")),"missing selected dependency is reported before activation");
        using(var library=new Library(Path.Combine(root,"EffectiveLibrary")))
        {
            library.State.GameFolder=game;
            var first=new ModPackage{Files=["plugins/Overlap.dll","config/options.cfg"]};var last=new ModPackage{Files=["plugins/Overlap.dll","config/options.cfg"]};
            foreach(var package in new[]{first,last})
            {
                library.State.Packages.Add(package);library.Active.Mods.Add(new(){PackageId=package.Id});
                foreach(string relative in package.Files){string path=Path.Combine(library.PackageRoot(package.Id),"payload",relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,package.Id);}
            }
            var effective=Compatibility.EffectiveFiles(library,false);
            check(File.ReadAllText(effective["plugins/Overlap.dll"])==last.Id&&File.ReadAllText(effective["config/options.cfg"])==first.Id,"effective manifest uses profile order and default configuration precedence");
            string settings=Path.Combine(library.ConfigRoot(library.Active.Id),"options.cfg");Directory.CreateDirectory(Path.GetDirectoryName(settings)!);File.WriteAllText(settings,"profile");
            check(File.ReadAllText(Compatibility.EffectiveFiles(library,false)["config/options.cfg"])=="profile","profile configuration overrides every packaged default");
            library.State.AppliedProfileId=library.Active.Id;Directory.CreateDirectory(Path.Combine(game,"BepInEx/config"));File.WriteAllText(Path.Combine(game,"BepInEx/config/options.cfg"),"installed");
            check(File.ReadAllText(Compatibility.EffectiveFiles(library,false)["config/options.cfg"])=="installed","applied profile inspects actual live configuration");
            File.WriteAllText(Path.Combine(library.Root,"loader-pending.json"),"pending");
            reject(()=>GameFiles.Play(library).GetAwaiter().GetResult(),"Core Play refuses interrupted operations before any launch");
        }
        var workers=new Dictionary<string,string>{{"owned","template"}};
        check(!RecoveryRecordRules.Retired(workers,"vanilla","vanilla")&&RecoveryRecordRules.Retired(workers,"owned","owned"),"worker retirement preserves unrelated native people");
        reject(()=>RecoveryRecordRules.Retired(workers,"owned","different"),"ambiguous person ownership stops retirement");
        RecoveryRecordRules.PayloadBoundary(12,64,32);reject(()=>RecoveryRecordRules.PayloadBoundary(12,64,65),"ghost skip cannot escape its native object");reject(()=>RecoveryRecordRules.PayloadBoundary(12,64,8),"ghost skip cannot move backwards");
        RecoveryRecordRules.Table("table",true,true,true,"valid",new HashSet<string>{"retired"});check(true,"surviving table ghost admits native contents and relationship preservation");
        reject(()=>RecoveryRecordRules.Table("table",true,true,false,null,new HashSet<string>()),"missing table ghost refuses invented contents and relationships");
        var source=new HashSet<int>{1,2,3};var target=new HashSet<int>();int saves=0;
        int count=RecoveryTransfer.Run(source.ToArray(),i=>target.Count<2,source.Remove,target.Add,source.Add,target.Remove,()=>saves++);
        check(count==2&&source.Count==1&&target.Count==2&&saves==1,"native transfer respects capacity and retains overflow");
        var receipt=source.ToArray();source=new HashSet<int>(receipt);count=RecoveryTransfer.Run(source.ToArray(),_=>false,source.Remove,target.Add,source.Add,target.Remove,()=>saves++);
        check(count==0&&source.SetEquals(receipt)&&saves==1,"reload and full-capacity repeat cannot duplicate goods or publish an empty claim");
        source=new HashSet<int>{4,5};target.Clear();int calls=0;
        reject(()=>RecoveryTransfer.Run(source.ToArray(),_=>true,source.Remove,i=>++calls==2?throw new IOException("add failed"):target.Add(i),source.Add,i=>!target.Contains(i)||target.Remove(i),()=>{}),"callback failure preserves its first cause");
        check(source.SetEquals(new[]{4,5})&&target.Count==0,"pre-publication callback failure rolls back all owned transfers");
        source=new HashSet<int>{6};target.Clear();
        reject(()=>RecoveryTransfer.Run(source.ToArray(),_=>true,source.Remove,target.Add,source.Add,target.Remove,()=>throw new IOException("save failed")),"failed publication remains unresolved");
        check(target.Contains(6)&&source.Count==0,"after-publication failure never fabricates a memory rollback contradicting disk");
    }
}
