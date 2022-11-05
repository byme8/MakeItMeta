using MakeItMeta.Attributes;
namespace MakeItMeta.Tests;

public class TestAttribute : MetaAttribute
{
    public static HashSet<string> Assemblies { get; set; } = new();

    public override void OnEntry(object? @this, string methodName, object?[]? parameters)
    {
        if (@this is null)
        {
            return;
        }

        var assembly = @this.GetType().Assembly;
        Assemblies.Add(assembly.FullName!);
    }

    public override void OnExit(object? @this, string methodName)
    {

    }
}