using MakeItMeta.Attributes;
namespace MakeItMeta.Tests;

public class TestAttribute : MetaAttribute
{
    public static Dictionary<string, HashSet<string>> MethodsByAssembly { get; set; } = new();

    public override void OnEntry(object? @this, string methodName, object?[]? parameters)
    {
        if (@this is null)
        {
            return;
        }

        var assembly = @this.GetType().Assembly;
        var assemblyFullName = assembly.FullName!;

        if (!MethodsByAssembly.ContainsKey(assemblyFullName))
        {
            MethodsByAssembly.Add(assemblyFullName, new HashSet<string>());
        }
        
        MethodsByAssembly[assemblyFullName].Add(methodName);
    }

    public override void OnExit(object? @this, string methodName)
    {

    }
}