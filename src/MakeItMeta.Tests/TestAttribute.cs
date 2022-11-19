using MakeItMeta.Attributes;
namespace MakeItMeta.Tests;

public class TestAttribute : MetaAttribute
{
    public static Dictionary<string, List<Entry>> MethodsByAssembly { get; set; } = new();

    public static Entry? OnEntry(object? @this, string assemblyFullName, string methodName, object[]? parameters)
    {
        if (!MethodsByAssembly.ContainsKey(assemblyFullName))
        {
            MethodsByAssembly.Add(assemblyFullName, new List<Entry>());
        }

        var entry = new Entry
        {
            Id = Guid.NewGuid(),
            Message = $"OnEnter: {methodName}"
        };

        MethodsByAssembly[assemblyFullName].Add(entry);

        return entry;
    }

    public static void OnExit(object? @this, string assemblyFullName, string methodName, Entry? entry)
    {
        if (!MethodsByAssembly.ContainsKey(assemblyFullName))
        {
            MethodsByAssembly.Add(assemblyFullName, new List<Entry>());
        }

        var exitEntry = new Entry()
        {
            Id = entry!.Id,
            Message = $"OnExit: {methodName}"
        };

        MethodsByAssembly[assemblyFullName].Add(exitEntry);
    }

    public class Entry
    {
        public Guid Id { get; set; }
        public string Message { get; set; }
    }
}