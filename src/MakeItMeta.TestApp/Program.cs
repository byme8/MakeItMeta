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

public class Executor
{
    public object? Execute()
    {
        return null; // place to replace
    }
}

public class Provider
{
    public Executor Provide()
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