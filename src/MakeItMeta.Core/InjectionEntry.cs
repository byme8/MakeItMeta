
namespace MakeItMeta.Core;

public record InjectionEntry(string Attribute, InjectionTypeEntry[] Types);

public record InjectionTypeEntry(string Name, string[]? Methods);
