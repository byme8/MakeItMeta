using Newtonsoft.Json;
#pragma warning disable CS8618
namespace MakeItMeta.Cli.Data;

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
