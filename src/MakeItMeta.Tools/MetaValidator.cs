using MakeItMeta.Tools.Results;
using Mono.Cecil;
using Mono.Cecil.Rocks;
namespace MakeItMeta.Tools;

public static class MetaValidator
{
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

            var parameters = new[]
            {
                (Name: "this", Type: "System.Object"),
                (Name: "assemblyFullName", Type: "System.String"),
                (Name: "methodName", Type: "System.String"),
            };

            for (int i = 0; i < parameters.Length; i++)
            {
                var onEntryArgument = parameters[i];
                if (onEntry.Parameters[i].Name != onEntryArgument.Name ||
                    onEntry.Parameters[i].ParameterType.FullName != onEntryArgument.Type)
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnEntry '{i}' parameter has to be '{onEntryArgument.Type} {onEntryArgument.Name}'");
                }
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                var onExitParameter = parameters[i];
                if (onExit.Parameters[i].Name != onExitParameter.Name ||
                    onExit.Parameters[i].ParameterType.FullName != onExitParameter.Type)
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnExit '{i}' parameter has to be '{onExitParameter.Type} {onExitParameter.Name}'");
                }
            }

            if (onEntry.ReturnType.FullName != "System.Void")
            {
                if (onExit.Parameters.Count != 4)
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnEnter returns '{onEntry.ReturnType.FullName}'. The OnExit has to accept is as last parameter.");

                }

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