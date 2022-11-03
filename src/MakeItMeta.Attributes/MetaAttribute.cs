namespace MakeItMeta.Attributes;

public abstract class MetaAttribute : Attribute
{
    public virtual void OnEntry(object? @this, string methodName, object?[]? parameters)
    {
    }

    public virtual void OnExit(object? @this, string methodName)
    {
    }
}