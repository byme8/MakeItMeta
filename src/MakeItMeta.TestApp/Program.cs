namespace MakeItMeta.TestApp;

public static class Program
{
    public static void Main(string[] args)
    {
        Execute();
    }

    public static object? Execute()
    {
        return new Provider().Provide().Execute(); // place to replace
    }
}

public class Executor : IExecutor
{
    public object? Execute()
    {
        return null; // place to replace
    }
}

public class Provider
{
    public IExecutor Provide()
    {
        return new Executor();
    }
}

public class Log
{
    public static void Write()
    {
        Console.WriteLine("log"); 
    }
}

public interface IExecutor
{
    object? Execute();
}