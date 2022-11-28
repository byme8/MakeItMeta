namespace MakeItMeta.Tests.Cli;

[UsesVerify]
public class AttributeParametersTest : InjectionTest
{
    [Fact]
    public async Task MethodFullName()
    {
        var attribute = """
            using MakeItMeta.Attributes;
            using MakeItMeta.TestAttributes;
            using System.Reflection;

            public class TestAppMetaAttribute : MetaAttribute
            {
                public static Entry? OnEntry(string methodFullName)
                {
                    // place to replace
                    return TestAttribute.OnEntry(null, null, methodFullName, null);
                }

                public static void OnExit(string methodFullName, Entry? entry)
                {
                    TestAttribute.OnExit(null, null, methodFullName, null, entry);
                }
            }            
            """;

        var newConfig = Config
            .Replace("MakeItMeta.TestAttributes.TestAttribute", "TestAppMetaAttribute");
        
        await Execute(attribute, newConfig);
    }
    
    [Fact]
    public async Task This()
    {
        var attribute = """
            using MakeItMeta.Attributes;
            using MakeItMeta.TestAttributes;
            using System.Reflection;

            public class TestAppMetaAttribute : MetaAttribute
            {
                public static Entry? OnEntry(object @this, string methodFullName)
                {
                    // place to replace
                    return TestAttribute.OnEntry(@this, null, methodFullName, null);
                }

                public static void OnExit(object @this, string methodFullName, Entry? entry)
                {
                    TestAttribute.OnExit(@this, null, methodFullName, null, entry);
                }
            }            
            """;

        var newConfig = Config
            .Replace("MakeItMeta.TestAttributes.TestAttribute", "TestAppMetaAttribute");
        
        var replaces = ("public object? Execute()", "public int Value { get; }= 42; public object? Execute()");
        
        await Execute(attribute, newConfig, replaces);
    }
    
    [Fact]
    public async Task Parameters()
    {
        var attribute = """
            using MakeItMeta.Attributes;
            using MakeItMeta.TestAttributes;
            using System.Reflection;

            public class TestAppMetaAttribute : MetaAttribute
            {
                public static Entry? OnEntry(string methodFullName, object[] parameters)
                {
                    // place to replace
                    return TestAttribute.OnEntry(null, null, methodFullName, parameters);
                }

                public static void OnExit(string methodFullName, object[] parameters, Entry? entry)
                {
                    TestAttribute.OnExit(null, null, methodFullName, parameters, entry);
                }
            }            
            """;

        var newConfig = Config
            .Replace("MakeItMeta.TestAttributes.TestAttribute", "TestAppMetaAttribute");

        var replaces = new[]
        {
            ("return new Provider().Provide().Execute(); // place to replace", "return new Provider().Provide(42).Execute();"),
            ("public IExecutor Provide()", "public IExecutor Provide(int value)")
        };
        
        await Execute(attribute, newConfig, replaces);
    }
}