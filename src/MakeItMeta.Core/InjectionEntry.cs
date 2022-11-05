namespace MakeItMeta.Core;

public class InjectionEntry
{
    public string Attribute { get; set; }

    public string Type { get; set; }

    public string[]? Methods { get; set; }
}