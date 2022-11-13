using System.Reflection;
using FluentAssertions;
using MakeItMeta.Tools;
using MakeItMeta.Tools.Results;
using MakeItMeta.Tests.Core;
using MakeItMeta.Tests.Data;

namespace MakeItMeta.Tests;

[UsesVerify]
public class GenerationTests
{
    public static Stream[] AdditionalAssemblies()
    {
        var coreAssembly = File.OpenRead("MakeItMeta.Attributes.dll");
        var thisAssembly = File.OpenRead(typeof(GenerationTests).Assembly.Location);
        return new Stream[]
        {
            coreAssembly,
            thisAssembly
        };
    }

    public static IEnumerable<object[]> Data => new[]
    {
        new object[]
        {
            "CanInjectAssembly",
            new InjectionConfig(
                AdditionalAssemblies(),
                new[]
                {
                    new InjectionEntry(
                        "MakeItMeta.Tests.TestAttribute",
                        new[]
                        {
                            new InjectionTypeEntry(
                                "MakeItMeta.TestApp.Executor",
                                new[] { "Execute" })
                        })
                })
        },
        new object[]
        {
            "CanInjectMultipleEntriesInAssembly",
            new InjectionConfig(
                AdditionalAssemblies(),
                new[]
                {
                    new InjectionEntry(
                        "MakeItMeta.Tests.TestAttribute",
                        new[]
                        {
                            new InjectionTypeEntry(
                                "MakeItMeta.TestApp.Executor",
                                new[] { "Execute" })
                        }),
                    new InjectionEntry(
                        "MakeItMeta.Tests.TestAttribute",
                        new[]
                        {
                            new InjectionTypeEntry(
                                "MakeItMeta.TestApp.Provider",
                                new[] { "Provide" })
                        })
                })
        },

        new object[]
        {
            "MissingAttributeIsReported",
            new InjectionConfig(
                AdditionalAssemblies(),
                new[]
                {
                    new InjectionEntry(
                        "MakeItMeta.Tests.MissingAttribute",
                        new[]
                        {
                            new InjectionTypeEntry(
                                "MakeItMeta.TestApp.Executor",
                                new[] { "Execute" })
                        })
                })
        },

        new object[]
        {
            "MissingMethodIsReported",
            new InjectionConfig(
                AdditionalAssemblies(),
                new[]
                {
                    new InjectionEntry("MakeItMeta.Tests.TestAttribute",
                        new[]
                        {
                            new InjectionTypeEntry(
                                "MakeItMeta.TestApp.Executor",
                                new[] { "MissingExecute" })
                        })
                })
        },

        new object[]
        {
            "MissingTypeIsReported",
            new InjectionConfig(
                AdditionalAssemblies(),
                new[]
                {
                    new InjectionEntry(
                        "MakeItMeta.Tests.TestAttribute",
                        new[]
                        {
                            new InjectionTypeEntry(
                                "MakeItMeta.TestApp.MissingExecutor",
                                new[] { "Execute" })
                        })
                })
        },
    };

    [Theory]
    [MemberData(nameof(Data))]
    public async Task CanInjectAssembly(string useCase, InjectionConfig config)
    {
        var testAssembly = await TestProject.Project.CompileToRealAssemblyAsBytes();

        var maker = new MetaMaker();
        var (resultAssembly, errors) = maker.MakeItMeta(new Stream[] { testAssembly.AsStream() }, config).Unwrap();
        if (errors)
        {
            await Verify(errors)
                .UseParameters(useCase);
            return;
        }

        var newAssembly = Assembly.Load(resultAssembly[0].ToArray());

        var result = newAssembly.Execute();
        var newAssemblyFullName = newAssembly.FullName!;
        var calls = TestAttribute.MethodsByAssembly.GetValueOrDefault(newAssemblyFullName);

        await Verify(calls)
            .UseParameters(useCase);
    }

    [Fact]
    public async Task CompilationWorks()
    {
        var assembly = await TestProject.Project.CompileToRealAssembly();
        var result = assembly.Execute();

        result.Should().BeNull();
    }

    [Fact]
    public async Task EmptyMakerWorks()
    {
        var assembly = await TestProject.Project.CompileToRealAssemblyAsBytes();
        var memoryStream = new MemoryStream(assembly);

        var maker = new MetaMaker();
        var (resultAssembly, error) = maker.MakeItMeta(new Stream[] { memoryStream }).Unwrap();
        var newAssembly = Assembly.Load(resultAssembly[0].ToArray());

        var result = newAssembly.Execute();
        result.Should().BeNull();
    }

    [Fact]
    public async Task InjectableMethodIsDuplicated()
    {
        var assembly = await TestProject.Project.CompileToRealAssemblyAsBytes();
        var memoryStream = new MemoryStream(assembly);

        var config = new InjectionConfig(
            AdditionalAssemblies(),
            new[]
            {
                new InjectionEntry(
                    "MakeItMeta.Tests.TestAttribute",
                    new[]
                    {
                        new InjectionTypeEntry("MakeItMeta.TestApp.Executor", new[] { "Execute" })
                    })

            });

        var maker = new MetaMaker();
        var (resultAssembly, error) = maker.MakeItMeta(new Stream[] { memoryStream }, config).Unwrap();
        var newAssembly = Assembly.Load(resultAssembly[0].ToArray());
        var result = newAssembly.Execute();

        await Verify(new[]
        {
            result,
            error,
            newAssembly.GetType("MakeItMeta.TestApp.Executor")?
                .GetMethods()
                .Select(o => o.Name)
                .ToArray()
        });
    }

}