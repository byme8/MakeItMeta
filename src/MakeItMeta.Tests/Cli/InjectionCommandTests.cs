using System.Reflection;
using CliFx.Infrastructure;
using MakeItMeta.Cli.Commands;
using MakeItMeta.Tests.Core;
using MakeItMeta.Tests.Data;
using Microsoft.CodeAnalysis;
namespace MakeItMeta.Tests.Cli;

[UsesVerify]
public class InjectionCommandTests
{
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
                        "name": "MakeItMeta.Tests.TestAttribute"
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
                    "MakeItMeta.Tests.dll"
                ],
                "attributes": 
                [
                    {
                        "name": "MakeItMeta.Tests.TestAttribute"
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
                        "name": "MakeItMeta.Tests.TestAttribute",
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

        var attribute = """
                using MakeItMeta.Attributes;
                using MakeItMeta.Tests;
                
                public class TestAppMetaAttribute : MetaAttribute
                {
                    public static TestAttribute.Entry? OnEntry(object? @this, string assemblyFullName, string methodName, object?[]? parameters)
                    {
                       return TestAttribute.OnEntry(@this, assemblyFullName, methodName, parameters);
                    }

                    public static void OnExit(object? @this, string assemblyFullName, string methodName, TestAttribute.Entry? entry)
                    {
                       TestAttribute.OnExit(@this, assemblyFullName, methodName, entry);
                    }
                }            
                """;

        await Execute(attribute, config, ("public object? Execute()", "[TestAppMeta]public object? Execute()"));
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

        var attribute = """
                using MakeItMeta.Attributes;
                using MakeItMeta.Tests;
                
                public class TestAppMetaAttribute : MetaAttribute
                {
                    public static TestAttribute.Entry? OnEntry(object? @this, string assemblyFullName, string methodName, object?[]? parameters)
                    {   
                       MakeItMeta.TestApp.Log.Write();
                       return TestAttribute.OnEntry(@this, assemblyFullName, methodName, parameters);
                    }

                    public static void OnExit(object? @this, string assemblyFullName, string methodName, TestAttribute.Entry? entry)
                    {
                       TestAttribute.OnExit(@this, assemblyFullName, methodName, entry);
                    }
                }            
                """;

        await Execute(attribute, config);
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

        var attribute = """
                using MakeItMeta.Attributes;
                using MakeItMeta.Tests;

                public class TestAppMetaAttribute : MetaAttribute
                {
                    public static TestAttribute.Entry? OnEntry(object? @this, string assemblyFullName, string methodName, object?[]? parameters)
                    {
                       return TestAttribute.OnEntry(@this, assemblyFullName, methodName, parameters);
                    }

                    public static void OnExit(object? @this, string assemblyFullName, string methodName, TestAttribute.Entry? entry)
                    {
                       TestAttribute.OnExit(@this, assemblyFullName, methodName, entry);
                    }
                }            
                """;

        await Execute(attribute, config);
    }

    private static async Task Execute(string newFile, string config, (string, string) places = default)
    {
        var metaAttributesReference = MetadataReference.CreateFromFile("MakeItMeta.Attributes.dll");
        var metaATestsReference = MetadataReference.CreateFromFile("MakeItMeta.Tests.dll");
        var console = new FakeInMemoryConsole();

        var project = TestProject.Project
            .AddMetadataReference(metaAttributesReference)
            .AddMetadataReference(metaATestsReference)
            .AddDocument("Test.cs", newFile)
            .Project;

        if (places != default)
        {
            project = await project.ReplacePartOfDocumentAsync("Program.cs", places);
        }

        var testAssembly = await project.CompileToRealAssemblyAsBytes();
        var tempTargetAssemblyFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempTargetAssemblyFile, testAssembly);

        config = config.Replace(@"""targetAssemblies"": []", @$"""targetAssemblies"": [""{tempTargetAssemblyFile}""]");
        var tempConfigFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempConfigFile, config);

        var command = new InjectCommand();
        command.Config = tempConfigFile;

        await command.ExecuteAsync(console);

        var modifiesAssemblyBytes = await File.ReadAllBytesAsync(tempTargetAssemblyFile);
        var modifiedAssembly = Assembly.Load(modifiesAssemblyBytes);

        _ = modifiedAssembly.Execute();
        
        var modifiedAssemblyFullName = modifiedAssembly.FullName!;
        var calls = TestAttribute.MethodsByAssembly.GetValueOrDefault(modifiedAssemblyFullName);
        var outputString = console.ReadOutputString();
        var errorString = console.ReadErrorString();
        
        await Verify(new
            {
                calls,
                outputString,
                errorString
            })
            .Track(tempTargetAssemblyFile)
            .Track(tempConfigFile);
    }
}