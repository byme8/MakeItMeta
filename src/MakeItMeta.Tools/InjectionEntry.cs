
namespace MakeItMeta.Tools;

public record InjectionEntry(string Attribute, InjectionTypeEntry[]? Types, bool All = false);

public record InjectionTypeEntry(string Name, string[]? Methods);
