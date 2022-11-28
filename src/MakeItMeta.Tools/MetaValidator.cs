using MakeItMeta.Tools.Results;
using Mono.Cecil;
using Mono.Cecil.Rocks;
namespace MakeItMeta.Tools;

public static class MetaValidator
{
    public static HashSet<(string, string)> Parameters = new[]
    {
        (Name: "this", Type: "System.Object"),
        (Name: "assemblyFullName", Type: "System.String"),
        (Name: "methodFullName", Type: "System.String"),
        (Name: "parameters", Type: "System.Object[]"),
    }.ToHashSet();
    
    public static Result ValidateConfig(TypeDefinition[] types, InjectionConfig? config)
    {
        if (config?.Entries is null)
        {
            return Result.Success();
        }

        var missingTypes = new HashSet<string>();
        var typesByName = types
            .GroupBy(o => o.FullName)
            .ToDictionary(o => o.Key, o => o.First());
        
        foreach (var entry in config.Entries)
        {
            if (!typesByName.ContainsKey(entry.Attribute))
            {
                missingTypes.Add(entry.Attribute);
                continue;
            }

            if (entry.Add is not null)
            {
                ValidateEntries(entry.Add, typesByName, missingTypes);
            }
            
            if (entry.Ignore is not null)
            {
                ValidateEntries(entry.Ignore, typesByName, missingTypes);
            }
        }

        if (missingTypes.Any())
        {
            return missingTypes
                .Select(o => new Error("MISSING_SYMBOL", $"Failed to find symbol '{o}'"))
                .ToArray();
        }

        return Result.Success();
    }
    
    public static Result ValidateAttributes(CustomAttribute[] metaAttributes)
    {
        foreach (var metaAttribute in metaAttributes)
        {
            var onEntry = metaAttribute.AttributeType.Resolve()
                .GetMethods()
                .First(o => o.Name == "OnEntry");

            var onExit = metaAttribute.AttributeType.Resolve()
                .GetMethods()
                .First(o => o.Name == "OnExit");

            foreach (var onEntryParameter in onEntry.Parameters)
            {
                if (!Parameters.Contains((onEntryParameter.Name, onEntryParameter.ParameterType.FullName)))
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnEntry '{onEntryParameter.ParameterType.FullName} {onEntryParameter.Name}' is unknown");
                }
            }

            var onExitParametersToValidate = onEntry.ReturnType.FullName != "System.Void" 
                ? onExit.Parameters.Count - 1 
                : onExit.Parameters.Count;
            
            foreach (var onExitParameter in onExit.Parameters.Take(onExitParametersToValidate))
            {
                if (!Parameters.Contains((onExitParameter.Name, onExitParameter.ParameterType.FullName)))
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnExit '{onExitParameter.ParameterType.FullName} {onExitParameter.Name}' is unknown");
                }
            }

            if (onEntry.ReturnType.FullName != "System.Void")
            {
                var last = onExit.Parameters.Last();
                if (last.ParameterType.FullName != onEntry.ReturnType.FullName)
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnEnter returns '{onEntry.ReturnType.FullName}'. The OnExit has to accept is as last parameter.");

                }
            }
        }

        return Result.Success();
    }

    private static void ValidateEntries(InjectionTypeEntry[] entries, Dictionary<string, TypeDefinition> typesByName, HashSet<string> missingTypes)
    {
        foreach (var addEntry in entries)
        {
            if (!typesByName.ContainsKey(addEntry.Name))
            {
                missingTypes.Add(addEntry.Name);
                continue;
            }

            var type = typesByName[addEntry.Name];
            var methods = type.Methods
                .Select(o => o.Name)
                .ToArray();

            if (addEntry.Methods is not null)
            {
                foreach (var method in addEntry.Methods)
                {
                    if (!methods.Contains(method))
                    {
                        missingTypes.Add($"{addEntry.Name}.{method}");
                    }
                }
            }
        }
    }
}