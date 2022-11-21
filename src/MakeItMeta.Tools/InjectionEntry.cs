
namespace MakeItMeta.Tools;

public record InjectionEntry(string Attribute, InjectionTypeEntry[]? Add, InjectionTypeEntry[]? Ignore = null);

public record InjectionTypeEntry(string Name, string[]? Methods);
