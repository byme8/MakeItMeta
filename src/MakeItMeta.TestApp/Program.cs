namespace MakeItMeta.TestApp;

public static class Program
{
    public static void Main(string[] args)
    {
        Execute();
    }

    public static object? Execute()
    {
        return new Executor().Execute(); // place to replace
    }
}

public class Executor
{
    public object? Execute()
    {
        return null; // place to replace
    }
}