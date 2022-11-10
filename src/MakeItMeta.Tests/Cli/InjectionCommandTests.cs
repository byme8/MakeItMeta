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
            "AttributeWithoutTypes",
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
                        "types": [
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

        var errorString = console.ReadErrorString();
        await Verify(errorString)
            .UseParameters(userCase)
            .Track(tempTargetAssemblyFile)
            .Track(tempConfigFile);
    }

    [Fact]
    public async Task InjectionSuccessful()
    {
        var config =
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
                "types": [
                {
                    "name": "MakeItMeta.TestApp.Executor"
                }
                ]
            }
            ]
        }
        """;
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

        var modifiesAssemblyBytes = await File.ReadAllBytesAsync(tempTargetAssemblyFile);
        var modifiedAssembly = Assembly.Load(modifiesAssemblyBytes);

        var types = modifiedAssembly
            .GetTypes()
            .ToArray();

        var executorMethods = types
            .First(o => o.Name == "Executor")?
            .GetMethods()
            .Select(o => o.Name)
            .ToArray();

        var outputString = console.ReadOutputString();
        var errorString = console.ReadErrorString();
        await Verify(new
            {
                outputString,
                errorString,
                executorMethods
            })
            .Track(tempTargetAssemblyFile)
            .Track(tempConfigFile);
    }
    
    [Fact]
    public async Task CanWorkWithoutInjection()
    {
        var config =
            """
        {
            "targetAssemblies": []
        }
        """;

        var metaAttributesReference = MetadataReference.CreateFromFile("MakeItMeta.Attributes.dll") ;
        var projectWithReferenceToMetaReference = await TestProject.Project
            .AddMetadataReference(metaAttributesReference)
            .AddDocument("TestAppMetaAttribute.cs", """
                using MakeItMeta.Attributes;
                
                public class TestAppMetaAttribute : MetaAttribute
                {
                    public override void OnEntry(object? @this, string methodName, object?[]? parameters)
                    {
                       
                    }

                    public override void OnExit(object? @this, string methodName)
                    {

                    }
                }            
                """)
            .Project
            .ReplacePartOfDocumentAsync("Program.cs", ("public object? Execute()", "[TestAppMeta]public object? Execute()"));
        
        var testAssembly = await projectWithReferenceToMetaReference.CompileToRealAssemblyAsBytes();
        var tempTargetAssemblyFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempTargetAssemblyFile, testAssembly);

        config = config.Replace(@"""targetAssemblies"": []", @$"""targetAssemblies"": [""{tempTargetAssemblyFile}""]");
        var tempConfigFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempConfigFile, config);

        var command = new InjectCommand();
        var console = new FakeInMemoryConsole();
        command.Config = tempConfigFile;

        await command.ExecuteAsync(console);

        var modifiesAssemblyBytes = await File.ReadAllBytesAsync(tempTargetAssemblyFile);
        var modifiedAssembly = Assembly.Load(modifiesAssemblyBytes);

        var types = modifiedAssembly
            .GetTypes()
            .ToArray();

        var executorMethods = types
            .First(o => o.Name == "Executor")?
            .GetMethods()
            .Select(o => o.Name)
            .ToArray();

        var outputString = console.ReadOutputString();
        var errorString = console.ReadErrorString();
        await Verify(new
            {
                outputString,
                errorString,
                executorMethods
            })
            .Track(tempTargetAssemblyFile)
            .Track(tempConfigFile);
    }
}