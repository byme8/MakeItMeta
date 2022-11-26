using MakeItMeta.Tools.Results;
using Mono.Cecil;
namespace MakeItMeta.Tools;

public static class MetaConfigValidator
{
    public static Result Validate(TypeDefinition[] types, InjectionConfig? config)
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