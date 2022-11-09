using System;
namespace MakeItMeta.Attributes;

public abstract class MetaAttribute : Attribute
{
    public abstract void OnEntry(object? @this, string methodName, object?[]? parameters);

    public abstract void OnExit(object? @this, string methodName);
}