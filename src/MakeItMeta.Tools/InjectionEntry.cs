
namespace MakeItMeta.Tools;

public class InjectionEntry
{
    public InjectionEntry(string attribute, InjectionTypeEntry[]? add, InjectionTypeEntry[]? ignore = null)
    {
        Attribute = attribute;
        Add = add;
        Ignore = ignore;
    }

    public string Attribute { get; }
    public InjectionTypeEntry[]? Add { get; }
    public InjectionTypeEntry[]? Ignore { get; }
}

public class InjectionTypeEntry
{
    public InjectionTypeEntry(string name, string[]? methods)
    {
        Name = name;
        Methods = methods;
    }

    public string Name { get; }
    public string[]? Methods { get; }
}
