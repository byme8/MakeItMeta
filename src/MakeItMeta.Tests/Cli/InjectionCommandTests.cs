using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using CliFx.Infrastructure;
using MakeItMeta.Cli.Commands;
using MakeItMeta.TestAttributes;
using MakeItMeta.Tests.Core;
using MakeItMeta.Tests.Data;
using Microsoft.CodeAnalysis;

namespace MakeItMeta.Tests.Cli;

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

        config = config.Replace(@"""targetAssemblies"": [],",
            @$"""targetAssemblies"": [""{Json.Encode(tempTargetAssemblyFile)}""],");
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

        var firstAssemblyFile = await TestProject.Project.PrepareTestAssemblyFile(firstDoer, (replace, firstMain));
        var secondAssemblyFile = await TestProject.Project.PrepareTestAssemblyFile(secondDoer, (replace, secondMain));

        var config = $$"""
            {
                "targetAssemblies": ["{{Json.Encode(firstAssemblyFile)}}", "{{Json.Encode(secondAssemblyFile)}}"],
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

        var firstAssembly = LoadAssembly(firstAssemblyFile);
        var secondAssembly = LoadAssembly(secondAssemblyFile);

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
}

public class InjectionTest
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

    public InjectionTest()
    {
        Json = JavaScriptEncoder.Create(UnicodeRanges.All);
    }

    public JavaScriptEncoder Json { get; set; }

    public async Task Execute(string newFile, string config, (string, string) places = default)
    {
        var tempTargetAssemblyFile = await TestProject.Project.PrepareTestAssemblyFile(newFile, places);
        config = config.Replace(
            @"""targetAssemblies"": []",
            @$"""targetAssemblies"": [""{Json.Encode(tempTargetAssemblyFile)}""]");

        var tempConfigFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempConfigFile, config);

        var console = new FakeInMemoryConsole();

        var command = new InjectCommand();
        command.Config = tempConfigFile;

        await command.ExecuteAsync(console);

        var modifiedAssembly = LoadAssembly(tempTargetAssemblyFile);

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

    public static Assembly LoadAssembly(string tempTargetAssemblyFile)
    {
        var modifiedAssembly = Assembly.LoadFrom(tempTargetAssemblyFile);
        return modifiedAssembly;
    }
}