using Mono.Cecil;
namespace Luz;

internal static class MetadataContracts
{
    internal static bool Resolves(MemberReference reference,Action check)
    {
        try{if(reference is MethodReference directMethod&&directMethod.Resolve()!=null||reference is FieldReference directField&&directField.Resolve()!=null)return true;}catch(AssemblyResolutionException){ }
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
                if(definition.Methods.Any(candidate=>candidate.Name==method.Name&&candidate.HasThis==method.HasThis&&candidate.GenericParameters.Count==method.GenericParameters.Count&&candidate.Parameters.Count==method.Parameters.Count&&Signature(candidate.ReturnType,args)==Signature(method.ReturnType,originalArgs)&&candidate.Parameters.Select(p=>Signature(p.ParameterType,args)).SequenceEqual(method.Parameters.Select(p=>Signature(p.ParameterType,originalArgs)))))return true;
            }
            if(definition.BaseType!=null)pending.Enqueue(definition.BaseType);
            foreach(var implemented in definition.Interfaces)pending.Enqueue(implemented.InterfaceType);
        }
        return false;
    }
    private static string Signature(TypeReference type,TypeReference[] arguments)
    {
        if(type is GenericParameter parameter)return parameter.Type==GenericParameterType.Type&&parameter.Position<arguments.Length?Signature(arguments[parameter.Position],[]):(parameter.Type==GenericParameterType.Type?"!":"!!")+parameter.Position;
        if(type is GenericInstanceType instance)return instance.ElementType.FullName+"<"+string.Join(',',instance.GenericArguments.Select(t=>Signature(t,arguments)))+">";
        if(type is ArrayType array)return Signature(array.ElementType,arguments)+"["+new string(',',array.Rank-1)+"]";
        if(type is ByReferenceType byref)return Signature(byref.ElementType,arguments)+"&";
        if(type is PointerType pointer)return Signature(pointer.ElementType,arguments)+"*";
        return type.FullName;
    }
}
