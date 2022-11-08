#pragma warning disable CS8618
namespace MakeItMeta.Cli.Data;

public class InjectionConfigInput
{
    public string[]? TargetAssemblies { get; set; }

    public string[]? AdditionalAssemblies { get; set; }

    public InjectionAttributeInput[]? Attributes { get; set; }
}

public class InjectionAttributeInput
{
    public string? Name { get; set; }

    public InjectionTypeEntryInput[]? Types { get; set; }
}

public class InjectionTypeEntryInput
{
    public string? Name { get; set; }

    public string[]? Methods { get; set; }
}
