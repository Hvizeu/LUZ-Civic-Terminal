using Mono.Cecil;
using Mono.Cecil.Cil;
using Luz;

internal static class GenericScopeChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        string folder=Path.Combine(root,"GenericScopes");Directory.CreateDirectory(folder);
        using var a=AssemblyDefinition.CreateAssembly(new("ScopeA",new(1,0)),"ScopeA",ModuleKind.Dll);
        using var b=AssemblyDefinition.CreateAssembly(new("ScopeB",new(1,0)),"ScopeB",ModuleKind.Dll);
        var identityA=new TypeDefinition("Contract","Identity",TypeAttributes.Public,a.MainModule.TypeSystem.Object);a.MainModule.Types.Add(identityA);
        var identityB=new TypeDefinition("Contract","Identity",TypeAttributes.Public,b.MainModule.TypeSystem.Object);b.MainModule.Types.Add(identityB);
        var box=new TypeDefinition("Contract","Box`1",TypeAttributes.Public,a.MainModule.TypeSystem.Object);box.GenericParameters.Add(new("T",box));a.MainModule.Types.Add(box);
        var parent=new TypeDefinition("Contract","Base`1",TypeAttributes.Public,a.MainModule.TypeSystem.Object);var t=new GenericParameter("T",parent);parent.GenericParameters.Add(t);a.MainModule.Types.Add(parent);
        var take=new MethodDefinition("Take",MethodAttributes.Public,a.MainModule.TypeSystem.Void);take.Parameters.Add(new(t));take.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));parent.Methods.Add(take);
        var use=new MethodDefinition("Use",MethodAttributes.Public,a.MainModule.TypeSystem.Void);use.Parameters.Add(new(identityA));use.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));parent.Methods.Add(use);
        var middle=new TypeDefinition("Contract","Middle`1",TypeAttributes.Public,a.MainModule.TypeSystem.Object);var u=new GenericParameter("U",middle);middle.GenericParameters.Add(u);a.MainModule.Types.Add(middle);
        var boxed=new GenericInstanceType(box);boxed.GenericArguments.Add(u);var baseType=new GenericInstanceType(parent);baseType.GenericArguments.Add(boxed);middle.BaseType=baseType;
        var middleInt=new GenericInstanceType(middle);middleInt.GenericArguments.Add(a.MainModule.TypeSystem.Int32);var leaf=new TypeDefinition("Contract","Leaf",TypeAttributes.Public,middleInt);a.MainModule.Types.Add(leaf);
        a.Write(Path.Combine(folder,"ScopeA.dll"));b.Write(Path.Combine(folder,"ScopeB.dll"));
        using(var facade=AssemblyDefinition.CreateAssembly(new("Facade",new(1,0)),"Facade",ModuleKind.Dll))
        {
            var scope=new AssemblyNameReference("ScopeA",new(1,0));facade.MainModule.AssemblyReferences.Add(scope);
            foreach(string name in new[]{"Leaf","Identity"})facade.MainModule.ExportedTypes.Add(new("Contract",name,facade.MainModule,scope){Attributes=TypeAttributes.Public|TypeAttributes.Forwarder});
            facade.Write(Path.Combine(folder,"Facade.dll"));
        }
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(folder);
        using var read=AssemblyDefinition.ReadAssembly(Path.Combine(folder,"ScopeA.dll"),new ReaderParameters{AssemblyResolver=resolver});
        var owner=read.MainModule.GetType("Contract.Leaf");var parameter=new GenericInstanceType(read.MainModule.GetType("Contract.Box`1"));parameter.GenericArguments.Add(read.MainModule.TypeSystem.Int32);
        var inherited=new MethodReference("Take",read.MainModule.TypeSystem.Void,owner){HasThis=true};inherited.Parameters.Add(new(parameter));
        check(MetadataContracts.Resolves(inherited,()=>{}),"transitive generic inheritance substitutes Base<Box<int>> without executing target assemblies");
        var wrong=new TypeReference("Contract","Identity",read.MainModule,new AssemblyNameReference("ScopeB",new(1,0)));
        var mismatched=new MethodReference("Use",read.MainModule.TypeSystem.Void,owner){HasThis=true};mismatched.Parameters.Add(new(wrong));
        check(!MetadataContracts.Resolves(mismatched,()=>{}),"same-named parameter types from different assemblies cannot satisfy a member contract");
        var right=new MethodReference("Use",read.MainModule.TypeSystem.Void,owner){HasThis=true};right.Parameters.Add(new(read.MainModule.GetType("Contract.Identity")));
        check(MetadataContracts.Resolves(right,()=>{}),"assembly-qualified positive contract continues to resolve");
        var forwardedOwner=new TypeReference("Contract","Leaf",read.MainModule,new AssemblyNameReference("Facade",new(1,0)));
        var forwarded=new MethodReference("Take",read.MainModule.TypeSystem.Void,forwardedOwner){HasThis=true};forwarded.Parameters.Add(new(parameter));
        check(MetadataContracts.Resolves(forwarded,()=>{}),"forwarded type retains its closed generic inherited method contract");
        var forwardedParameter=new TypeReference("Contract","Identity",read.MainModule,new AssemblyNameReference("Facade",new(1,0)));
        var forwardedUse=new MethodReference("Use",read.MainModule.TypeSystem.Void,forwardedOwner){HasThis=true};forwardedUse.Parameters.Add(new(forwardedParameter));
        check(MetadataContracts.Resolves(forwardedUse,()=>{}),"forwarded parameter resolves to its actual assembly identity");
    }
}
