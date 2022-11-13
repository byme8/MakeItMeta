namespace MakeItMeta.Tools;

public class InjectionConfig
{
    public InjectionConfig(Stream[]? additionalAssemblies, InjectionEntry[]? entries)
    {
        AdditionalAssemblies = additionalAssemblies;
        Entries = entries;
    }

    public Stream[]? AdditionalAssemblies { get; }

    public InjectionEntry[]? Entries { get; }
}