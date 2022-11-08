using CliFx.Infrastructure;
using MakeItMeta.Cli.Commands;
using MakeItMeta.Tests.Core;
using MakeItMeta.Tests.Data;
namespace MakeItMeta.Tests.Cli;

[UsesVerify]
public class InjectionCommandTests
{
    public static IEnumerable<object[]> BrokenConfigs = new[]
    {
        new[] { "EmptyConfig", "" },
        new[] { "WithoutTargetAssemblies", 
            $$"""
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
        new[] { "AttributeWithoutTypes", 
            $$"""
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

        await Verify(console.ReadErrorString())
            .UseParameters(userCase)
            .Track(tempTargetAssemblyFile)
            .Track(tempConfigFile);
    }
}