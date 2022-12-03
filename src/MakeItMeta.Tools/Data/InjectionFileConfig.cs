using Newtonsoft.Json;

#pragma warning disable CS8618
namespace MakeItMeta.Tools.Data;

public class InjectionConfigInput
{
    [JsonProperty("$schema")]
    public string Schema { get; set; }
    
    public string[]? TargetAssemblies { get; set; }

    public string[]? AdditionalAssemblies { get; set; }

    public InjectionAttributeInput[]? Attributes { get; set; }
}

public class InjectionAttributeInput
{
    public string? Name { get; set; }

    public InjectionTypeEntryInput[]? Add { get; set; }

    public InjectionTypeEntryInput[]? Ignore { get; set; }
}

public class InjectionTypeEntryInput
{
    public string? Name { get; set; }

    public string[]? Methods { get; set; }
}

public class InjectionConfigOutput
{
    public string[] TargetAssembliesPath { get; set; }

    public Stream[] TargetAssemblies { get; set; }
    
    public InjectionConfig InjectionConfig { get; set; }

    public string[] SearchFolders { get; set; }
}