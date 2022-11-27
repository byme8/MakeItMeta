using System.Reflection;
using FluentAssertions;
using MakeItMeta.Tools;
using MakeItMeta.Tools.Results;
using MakeItMeta.Tests.Core;
using MakeItMeta.Tests.Data;
using MakeItMeta.TestAttributes;

namespace MakeItMeta.Tests;

[UsesVerify]
public class GenerationTests
{
    public static Stream[] AdditionalAssemblies()
    {
        var coreAssembly = File.OpenRead("MakeItMeta.Attributes.dll");
        var testAttributesAssembly = File.OpenRead("MakeItMeta.TestAttributes.dll");
        return new Stream[]
        {
            coreAssembly,
            testAttributesAssembly
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
                        "MakeItMeta.TestAttributes.TestAttribute",
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
                        "MakeItMeta.TestAttributes.TestAttribute",
                        new[]
                        {
                            new InjectionTypeEntry(
                                "MakeItMeta.TestApp.Executor",
                                new[] { "Execute" })
                        }),
                    new InjectionEntry(
                        "MakeItMeta.TestAttributes.TestAttribute",
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
                    new InjectionEntry("MakeItMeta.TestAttributes.TestAttribute",
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
                        "MakeItMeta.TestAttributes.TestAttribute",
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
        var testAssembly = await TestProject.Project.PrepareTestAssemblyFile();
    
        var maker = new MetaMaker();
        var errors = maker.MakeItMeta(new[] { testAssembly }, config).Unwrap();
        if (errors)
        {
            await Verify(errors)
                .UseParameters(useCase);
            return;
        }
    
        var newAssembly = Assembly.LoadFile(testAssembly);
    
        var result = newAssembly.Execute();
        var newAssemblyFullName = newAssembly.FullName!;
        var calls = TestAttribute.MethodsByAssembly.GetValueOrDefault(newAssemblyFullName);
    
        await Verify(calls)
            .UseParameters(useCase);
    }
    
    [Fact]
    public async Task OnEnterReturnIsPassedToOnExit()
    {
        var config = new InjectionConfig(
            AdditionalAssemblies(),
            new[]
            {
                new InjectionEntry(
                    "MakeItMeta.TestAttributes.TestAttribute",
                    new[]
                    {
                        new InjectionTypeEntry(
                            "MakeItMeta.TestApp.Executor",
                            new[] { "Execute" })
                    })
            });
    
        var testAssembly = await TestProject.Project.PrepareTestAssemblyFile();
    
        var maker = new MetaMaker();
        var errors = maker.MakeItMeta(new[] { testAssembly }, config).Unwrap();
    
        var newAssembly = Assembly.LoadFile(testAssembly);
    
        var result = newAssembly.Execute();
        var newAssemblyFullName = newAssembly.FullName!;
        var calls = TestAttribute.MethodsByAssembly.GetValueOrDefault(newAssemblyFullName);
    
        await Verify(new
        {
            calls,
            errors
        });
    }
    
    [Fact]
    public async Task CanRewriteTheWholeAssembly()
    {
        var config = new InjectionConfig(
            AdditionalAssemblies(),
            new[]
            {
                new InjectionEntry("MakeItMeta.TestAttributes.TestAttribute", null, null)
            });
    
        var testAssembly = await TestProject.Project.PrepareTestAssemblyFile();
    
        var maker = new MetaMaker();
        var errors = maker.MakeItMeta(new[] { testAssembly }, config).Unwrap();
    
        var newAssembly = Assembly.LoadFile(testAssembly);
    
        var result = newAssembly.Execute();
        var newAssemblyFullName = newAssembly.FullName!;
        var calls = TestAttribute.MethodsByAssembly.GetValueOrDefault(newAssemblyFullName);
    
        await Verify(new
        {
            calls,
            errors
        });
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
        var testAssembly = await TestProject.Project.PrepareTestAssemblyFile();
    
        var maker = new MetaMaker();
        var errors = maker.MakeItMeta(new[] { testAssembly }).Unwrap();
    
        var newAssembly = Assembly.LoadFile(testAssembly);
    
        var result = newAssembly.Execute();
        result.Should().BeNull();
    }

}