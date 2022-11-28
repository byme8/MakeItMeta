using System.Reflection;
using CliFx.Infrastructure;
using MakeItMeta.Cli.Commands;
using MakeItMeta.TestAttributes;
using MakeItMeta.Tests.Core;
using MakeItMeta.Tests.Data;
using Microsoft.CodeAnalysis;
namespace MakeItMeta.Tests.Cli;

public class InjectionTest
{
    public const string Attribute =
        """
                using MakeItMeta.Attributes;
                using MakeItMeta.TestAttributes;

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
    
    public static async Task Execute(string newFile, string config, params (string, string)[] places)
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
        var calls = TestAttribute.MethodsByAssembly
            .GetValueOrDefault(modifiedAssemblyFullName)?
            .ToArray();
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
            .Track(modifiedAssemblyFullName)
            .Track(tempConfigFile);
    }

    public static async Task<Assembly> LoadAssembly(string tempTargetAssemblyFile)
    {

        var modifiesAssemblyBytes = await File.ReadAllBytesAsync(tempTargetAssemblyFile);
        var modifiedAssembly = Assembly.Load(modifiesAssemblyBytes);
        return modifiedAssembly;
    }

    public static async Task<string> PrepareTestAssemblyFile(string? additionalFile = null, params (string, string)[] places)
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

        if (places.Any())
        {
            project = await project.ReplacePartOfDocumentAsync("Program.cs", places);
        }

        var testAssembly = await project.CompileToRealAssemblyAsBytes();
        var tempTargetAssemblyFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempTargetAssemblyFile, testAssembly);
        return tempTargetAssemblyFile;
    }
}

[UsesVerify]
public class InjectionCommandTests : InjectionTest
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
            .Track(firstAssemblyFullName)
            .Track(secondAssemblyFile)
            .Track(secondAssemblyFullName)
            .Track(configFile);
    }
}