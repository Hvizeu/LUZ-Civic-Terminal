using Mono.Cecil;
namespace Luz;

internal static class MetadataContracts
{
    internal static bool Resolves(MemberReference reference,Action check)
    {
        // MemberRef can legally name a derived/forwarded type. Cecil's normal
        // lookup does not cover every closed-generic inheritance combination.
        var pending=new Queue<TypeReference>();pending.Enqueue(reference.DeclaringType);var seen=new HashSet<string>();int count=0;
        while(pending.Count!=0)
        {
            check();if(++count>128)throw new InvalidDataException("Metadata inheritance exceeds its inspection limit.");
            var type=pending.Dequeue();if(!seen.Add(type.FullName+"@"+type.Scope))continue;
            if(!Compatibility.GameScope(type))continue;
            var definition=type.Resolve();if(definition==null)continue;
            var args=type is GenericInstanceType generic?generic.GenericArguments.ToArray():Array.Empty<TypeReference>();
            var originalArgs=reference.DeclaringType is GenericInstanceType original?original.GenericArguments.ToArray():Array.Empty<TypeReference>();
            if(reference is FieldReference f&&definition.Fields.Any(candidate=>candidate.Name==f.Name&&Signature(candidate.FieldType,args)==Signature(f.FieldType,originalArgs)))return true;
            if(reference is MethodReference m)
            {
                var method=m is GenericInstanceMethod instance?instance.ElementMethod:m;
                if(definition.Methods.Any(candidate=>candidate.Name==method.Name&&candidate.HasThis==method.HasThis&&candidate.ExplicitThis==method.ExplicitThis&&candidate.CallingConvention==method.CallingConvention&&candidate.GenericParameters.Count==method.GenericParameters.Count&&candidate.Parameters.Count==method.Parameters.Count&&Signature(candidate.ReturnType,args)==Signature(method.ReturnType,originalArgs)&&candidate.Parameters.Select(p=>Signature(p.ParameterType,args)).SequenceEqual(method.Parameters.Select(p=>Signature(p.ParameterType,originalArgs)))))return true;
                if(method.Name is ".ctor" or ".cctor")return false;
            }
            if(definition.BaseType!=null)pending.Enqueue(Substitute(definition.BaseType,args));
            foreach(var implemented in definition.Interfaces)pending.Enqueue(Substitute(implemented.InterfaceType,args));
        }
        return false;
    }
    private static string Signature(TypeReference type,TypeReference[] arguments)
    {
        if(type is GenericParameter parameter)return parameter.Type==GenericParameterType.Type&&parameter.Position<arguments.Length?Signature(arguments[parameter.Position],[]):(parameter.Type==GenericParameterType.Type?"!":"!!")+parameter.Position;
        if(type is GenericInstanceType instance)return Signature(instance.ElementType,[])+"<"+string.Join(',',instance.GenericArguments.Select(t=>Signature(t,arguments)))+">";
        if(type is ArrayType array)return Signature(array.ElementType,arguments)+"["+new string(',',array.Rank-1)+"]";
        if(type is ByReferenceType byref)return Signature(byref.ElementType,arguments)+"&";
        if(type is PointerType pointer)return Signature(pointer.ElementType,arguments)+"*";
        if(type is RequiredModifierType required)return Signature(required.ElementType,arguments)+" modreq("+Signature(required.ModifierType,arguments)+")";
        if(type is OptionalModifierType optional)return Signature(optional.ElementType,arguments)+" modopt("+Signature(optional.ModifierType,arguments)+")";
        if(!Compatibility.GameScope(type))return type.FullName;
        var definition=type.Resolve();
        return type.FullName+"@"+(definition?.Module.Assembly?.Name.FullName??type.Scope.ToString());
    }
    internal static string DeclarationName(TypeReference type)=>type switch
    {
        GenericParameter p=>(p.Type==GenericParameterType.Type?"!":"!!")+p.Position,
        GenericInstanceType g=>g.ElementType.FullName+"<"+string.Join(',',g.GenericArguments.Select(DeclarationName))+">",
        ArrayType a=>DeclarationName(a.ElementType)+"["+new string(',',a.Rank-1)+"]",
        ByReferenceType b=>DeclarationName(b.ElementType)+"&",PointerType p=>DeclarationName(p.ElementType)+"*",
        _=>type.FullName
    };
    private static TypeReference Substitute(TypeReference type,TypeReference[] arguments)
    {
        if(type is GenericParameter p&&p.Type==GenericParameterType.Type&&p.Position<arguments.Length)return arguments[p.Position];
        if(type is GenericInstanceType generic){var result=new GenericInstanceType(generic.ElementType);foreach(var arg in generic.GenericArguments)result.GenericArguments.Add(Substitute(arg,arguments));return result;}
        if(type is ArrayType array)return new ArrayType(Substitute(array.ElementType,arguments),array.Rank);
        if(type is ByReferenceType byref)return new ByReferenceType(Substitute(byref.ElementType,arguments));
        if(type is PointerType pointer)return new PointerType(Substitute(pointer.ElementType,arguments));
        if(type is RequiredModifierType required)return new RequiredModifierType(Substitute(required.ModifierType,arguments),Substitute(required.ElementType,arguments));
        if(type is OptionalModifierType optional)return new OptionalModifierType(Substitute(optional.ModifierType,arguments),Substitute(optional.ElementType,arguments));
        return type;
    }
}
