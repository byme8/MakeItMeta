using System.Reflection;
using CliFx.Infrastructure;
using MakeItMeta.Cli.Commands;
using MakeItMeta.TestAttributes;
using MakeItMeta.Tests.Core;
using MakeItMeta.Tests.Data;
using Microsoft.CodeAnalysis;
namespace MakeItMeta.Tests.Cli;

[UsesVerify]
public class InjectionCommandTests
{
    public const string Attribute =
                """
                using MakeItMeta.Attributes;
                using MakeItMeta.TestAttributes;

                public class TestAppMetaAttribute : MetaAttribute
                {
                    public static Entry? OnEntry(object? @this, string assemblyFullName, string methodName, object?[]? parameters)
                    {
                        // place to replace
                        return TestAttribute.OnEntry(@this, assemblyFullName, methodName, parameters);
                    }

                    public static void OnExit(object? @this, string assemblyFullName, string methodName, Entry? entry)
                    {
                        TestAttribute.OnExit(@this, assemblyFullName, methodName, entry);
                    }
                }            
                """;

                public const string Config = 
                """
                {
                    "targetAssemblies": [],
                    "additionalAssemblies": 
                    [
                        "MakeItMeta.Attributes.dll",
                        "MakeItMeta.TestAttributes.dll"
                    ],
                    "attributes": 
                    [
                        {
                            "name": "MakeItMeta.TestAttributes.TestAttribute",
                        }
                    ]
                }
                """;

    public static IEnumerable<object[]> BrokenConfigs = new[]
    {
        new[] { "EmptyConfig", "" },
        new[]
        {
            "WithoutTargetAssemblies",
            """
            {
                "additionalAssemblies": 
                [
                    "MakeItMeta.Attributes.dll",
                    "MakeItMeta.Tests.dll"
                ],
                "attributes": 
                [
                    {
                        "name": "MakeItMeta.TestAttributes.TestAttribute"
                    }
                ]
            }
            """
        },
        new[]
        {
            "AttributeWithoutTypesButWithAll",
            """
            {
                "targetAssemblies": [],
                "additionalAssemblies": 
                [
                    "MakeItMeta.Attributes.dll",
                    "MakeItMeta.TestAttributes.dll"
                ],
                "attributes": 
                [
                    {
                        "name": "MakeItMeta.TestAttributes.TestAttribute"
                    }
                ]
            }
            """
        },
        new[]
        {
            "TypeWithoutName",
            """
            {
                "targetAssemblies": [],
                "additionalAssemblies": 
                [
                    "MakeItMeta.Attributes.dll",
                    "MakeItMeta.Tests.dll"
                ],
                "attributes": 
                [
                    {
                        "name": "MakeItMeta.TestAttributes.TestAttribute",
                        "add": [
                            {
                            }
                        ]
                    }
                ]
            }
            """
        }
    };

    [Theory]
    [MemberData(nameof(BrokenConfigs))]
    public async Task InjectionFailsWhenConfigIsBroken(string userCase, string config)
    {
        var testAssembly = await TestProject.Project.CompileToRealAssemblyAsBytes();
        var tempTargetAssemblyFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempTargetAssemblyFile, testAssembly);

        config = config.Replace(@"""targetAssemblies"": [],", @$"""targetAssemblies"": [""{tempTargetAssemblyFile}""],");
        var tempConfigFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempConfigFile, config);

        var command = new InjectCommand();
        var console = new FakeInMemoryConsole();
        command.Config = tempConfigFile;

        await command.ExecuteAsync(console);

        var output = console.ReadOutputString();
        var error = console.ReadErrorString();
        await Verify(new
        {
            outputString = output,
            errorString = error
        })
            .UseParameters(userCase)
            .Track(tempTargetAssemblyFile)
            .Track(tempConfigFile);
    }

    [Fact]
    public async Task CanWorkWithoutInjection()
    {
        var config = """
            {
                "targetAssemblies": []
            }
            """;

        await Execute(Attribute, config, ("public object? Execute()", "[TestAppMeta]public object? Execute()"));
    }

    [Fact]
    public async Task CanIgnoreTypeFromInjection()
    {
        var config = """
            {
                "targetAssemblies": [],
                "attributes": 
                [
                    {
                        "name": "TestAppMetaAttribute",
                        "ignore":
                        [
                            {
                                "name": "MakeItMeta.TestApp.Log",
                                "methods": [
                                    "Write"
                                ]
                            }
                        ]
                    }
                ]
            }
            """;

        var attribute = Attribute.Replace("// place to replace", "MakeItMeta.TestApp.Log.Write();");
        await Execute(attribute, config);
    }

    [Fact]
    public async Task CanInjectionFromDifferentAssembly()
    {
        var config = """
            {
                "targetAssemblies": [],
                "additionalAssemblies": 
                [
                    "MakeItMeta.Attributes.dll",
                    "MakeItMeta.TestAttributes.dll"
                ],
                "attributes": 
                [
                    {
                        "name": "MakeItMeta.TestAttributes.TestAttribute",
                        "ignore":
                        [
                            {
                                "name": "MakeItMeta.TestApp.Log",
                                "methods": [
                                    "Write"
                                ]
                            }
                        ]
                    }
                ]
            }
            """;

        await Execute(string.Empty, config);
    }

    [Fact]
    public async Task OverrideAndVirtual()
    {
        var newFile = """
            public class ExecutorVirtual
            {
                public virtual object? Execute()
                {
                    return null; // place to replace
                }
            }

            public class ExecutorOverride : ExecutorVirtual
            {
                public override object? Execute()
                {
                    return null; // place to replace
                }
            }
            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = """
                    new ExecutorVirtual().Execute();
                    return new ExecutorOverride().Execute();
        """;

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task MultipleReturnsWith1()
    {
        var newFile = """ 
            public struct MultiplrReturns 
            {
                public int DoIt(int value)
                {
                    var result = 0;
                    if(value == 2)
                    {
                        result = 20;
                        return result;
                    }

                    return result + value;
                }
            }
            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = $"return new MultiplrReturns().DoIt(1);";

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task MultipleReturnsWith2()
    {
        var newFile = """ 
            public struct MultiplrReturns 
            {
                public int DoIt(int value)
                {
                    var result = 0;
                    if(value == 2)
                    {
                        result = 20;
                        return result;
                    }

                    return result + value;
                }
            }
            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = $"return new MultiplrReturns().DoIt(2);";

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task ToUInt32()
    {
        var newFile = """
            public struct Color 
            {
                public int R { get; set; }
                public int G { get; set; }
                public int B { get; set; }
                public int A { get; set; }

                public uint ToUint32()
                {
                    return ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | (uint)B;
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = "return new Color().ToUint32();";

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task Generics()
    {
        var newFile = """
            public class Container<TValue>
            {
                public TValue Get()
                {
                    return default;
                }

                public TAsValue GetAs<TAsValue>()
                {
                    return default;
                }

                public TValue Execute()
                {
                    GetAs<string>();
                    return Get();
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = """
            return new Container<int>().Execute();
            """;

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task MetaAttributesAreIgnored()
    {
        var config = """
            {
                "targetAssemblies": [],
                "attributes": 
                [
                    {
                        "name": "TestAppMetaAttribute"
                    }
                ]
            }
            """;

        await Execute(Attribute, config);
    }

    [Fact]
    public async Task MultipleTargetAssembliesAreSupported()
    {
        var firstDoer = @"public static class FirstDoer { public static string DoIt() => ""first""; }";
        var secondDoer = @"public static class SecondDoer { public static string DoIt() => ""second""; }";
        
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var firstMain = "return FirstDoer.DoIt();";
        var secondMain = "return SecondDoer.DoIt();";
        
        var firstAssemblyFile = await PrepareTestAssemblyFile(firstDoer, (replace, firstMain));
        var secondAssemblyFile = await PrepareTestAssemblyFile(secondDoer, (replace, secondMain));
        
        var config = $$"""
            {
                "targetAssemblies": ["{{firstAssemblyFile}}", "{{secondAssemblyFile}}"],
                "additionalAssemblies": 
                [
                    "MakeItMeta.Attributes.dll",
                    "MakeItMeta.TestAttributes.dll"
                ],
                "attributes": 
                [
                    {
                        "name": "MakeItMeta.TestAttributes.TestAttribute",
                        "add": [
                            {
                                "name": "FirstDoer"
                            },
                            {
                                "name": "SecondDoer"
                            }
                        ]
                    }
                ]
            }
            """;
        
        var configFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(configFile, config);

        var console = new FakeInMemoryConsole();

        var command = new InjectCommand();
        command.Config = configFile;

        await command.ExecuteAsync(console);
     
        var firstAssembly = await LoadAssembly(firstAssemblyFile);
        var secondAssembly = await LoadAssembly(secondAssemblyFile);

        var firstResult = firstAssembly.Execute();
        var secondResult = secondAssembly.Execute();
        
        
        var firstAssemblyFullName = firstAssembly.FullName!;
        var secondAssemblyFullName = secondAssembly.FullName!;
        var firstCalls = TestAttribute.MethodsByAssembly.GetValueOrDefault(firstAssemblyFullName);
        var secondCalls = TestAttribute.MethodsByAssembly.GetValueOrDefault(secondAssemblyFullName);
        var outputString = console.ReadOutputString();
        var errorString = console.ReadErrorString();

        await Verify(new
            {
                firstResult,
                secondResult,
                firstCalls,
                secondCalls,
                outputString,
                errorString
            })
            .Track(firstAssemblyFile)
            .Track(secondAssemblyFile)
            .Track(configFile);
    }

    private static async Task Execute(string newFile, string config, (string, string) places = default)
    {
        var tempTargetAssemblyFile = await PrepareTestAssemblyFile(newFile, places);

        config = config.Replace(@"""targetAssemblies"": []", @$"""targetAssemblies"": [""{tempTargetAssemblyFile}""]");
        var tempConfigFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempConfigFile, config);

        var console = new FakeInMemoryConsole();

        var command = new InjectCommand();
        command.Config = tempConfigFile;

        await command.ExecuteAsync(console);

        var modifiedAssembly = await LoadAssembly(tempTargetAssemblyFile);

        var result = modifiedAssembly.Execute();

        var modifiedAssemblyFullName = modifiedAssembly.FullName!;
        var calls = TestAttribute.MethodsByAssembly.GetValueOrDefault(modifiedAssemblyFullName);
        var outputString = console.ReadOutputString();
        var errorString = console.ReadErrorString();

        await Verify(new
        {
            result,
            calls,
            outputString,
            errorString
        })
            .Track(tempTargetAssemblyFile)
            .Track(tempConfigFile);
    }

    private static async Task<Assembly> LoadAssembly(string tempTargetAssemblyFile)
    {

        var modifiesAssemblyBytes = await File.ReadAllBytesAsync(tempTargetAssemblyFile);
        var modifiedAssembly = Assembly.Load(modifiesAssemblyBytes);
        return modifiedAssembly;
    }

    private static async Task<string> PrepareTestAssemblyFile(string? additionalFile = null, (string, string) places = default)
    {
        var metaAttributesReference = MetadataReference.CreateFromFile("MakeItMeta.Attributes.dll");
        var testAttributesReference = MetadataReference.CreateFromFile("MakeItMeta.TestAttributes.dll");
        var project = TestProject.Project;

        if (!string.IsNullOrEmpty(additionalFile))
        {
            project = project.AddDocument("AdditionalFile.cs", additionalFile)
                .Project;
        }

        if (!string.IsNullOrEmpty(additionalFile))
        {
            project = project
                .AddMetadataReference(testAttributesReference)
                .AddMetadataReference(metaAttributesReference);
        }

        if (places != default)
        {
            project = await project.ReplacePartOfDocumentAsync("Program.cs", places);
        }

        var testAssembly = await project.CompileToRealAssemblyAsBytes();
        var tempTargetAssemblyFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempTargetAssemblyFile, testAssembly);
        return tempTargetAssemblyFile;
    }
}