using System.Reflection;
using FluentAssertions;
using MakeItMeta.Core;
using MakeItMeta.Core.Results;
using MakeItMeta.Tests.Core;
using MakeItMeta.Tests.Data;

namespace MakeItMeta.Tests;

[UsesVerify]
public class GenerationTests
{
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
        var (resultAssembly, error) = maker.MakeItMeta(memoryStream).Unwrap();
        var newAssembly = Assembly.Load(resultAssembly.ToArray());

        var result = newAssembly.Execute();
        result.Should().BeNull();
    }

    [Fact]
    public async Task CanInjectAssembly()
    {
        var testAssembly = await TestProject.Project.CompileToRealAssemblyAsBytes();
        var coreAssembly = File.OpenRead("MakeItMeta.Attributes.dll");
        var thisAssembly = File.OpenRead(GetType().Assembly.Location);

        var config = new InjectionConfig(
            new Stream[]
            {
                coreAssembly,
                thisAssembly
            },
            new[]
            {
                new InjectionEntry()
                {
                    Attribute = "MakeItMeta.Tests.TestAttribute",
                    Type = "MakeItMeta.TestApp.Executor",
                    Methods = new[] { "Execute" }
                }
            });

        var maker = new MetaMaker();
        var (resultAssembly, _) = maker.MakeItMeta(testAssembly.AsStream(), config).Unwrap();

        var newAssembly = Assembly.Load(resultAssembly.ToArray());

        var result = newAssembly.Execute();
        var newAssemblyFullName = newAssembly.FullName!;
        TestAttribute.MethodsByAssembly.Should().ContainKey(newAssemblyFullName);

        var calls = TestAttribute.MethodsByAssembly[newAssemblyFullName];

        await Verify(calls);
    }
    
    [Fact]
    public async Task CanInjectMultipleEntriesInAssembly()
    {
        var testAssembly = await TestProject.Project.CompileToRealAssemblyAsBytes();
        var coreAssembly = File.OpenRead("MakeItMeta.Attributes.dll");
        var thisAssembly = File.OpenRead(GetType().Assembly.Location);

        var config = new InjectionConfig(
            new Stream[]
            {
                coreAssembly,
                thisAssembly
            },
            new[]
            {
                new InjectionEntry()
                {
                    Attribute = "MakeItMeta.Tests.TestAttribute",
                    Type = "MakeItMeta.TestApp.Executor",
                    Methods = new[] { "Execute" }
                },
                new InjectionEntry()
                {
                    Attribute = "MakeItMeta.Tests.TestAttribute",
                    Type = "MakeItMeta.TestApp.Provider",
                    Methods = new[] { "Provide" }
                }
            });

        var maker = new MetaMaker();
        var (resultAssembly, _) = maker.MakeItMeta(testAssembly.AsStream(), config).Unwrap();

        var newAssembly = Assembly.Load(resultAssembly.ToArray());

        var result = newAssembly.Execute();
        var newAssemblyFullName = newAssembly.FullName!;
        TestAttribute.MethodsByAssembly.Should().ContainKey(newAssemblyFullName);

        var calls = TestAttribute.MethodsByAssembly[newAssemblyFullName];

        await Verify(calls);
    }
    
    [Fact]
    public async Task MissingAttributeIsReported()
    {
        var testAssembly = await TestProject.Project.CompileToRealAssemblyAsBytes();
        var coreAssembly = File.OpenRead("MakeItMeta.Attributes.dll");
        var thisAssembly = File.OpenRead(GetType().Assembly.Location);

        var config = new InjectionConfig(
            new Stream[]
            {
                coreAssembly,
                thisAssembly
            },
            new[]
            {
                new InjectionEntry
                {
                    Attribute = "MakeItMeta.Tests.MissingAttribute",
                    Type = "MakeItMeta.TestApp.Executor",
                    Methods = new[] { "Execute" }
                }
            });

        var maker = new MetaMaker();
        var (_, error) = maker.MakeItMeta(testAssembly.AsStream(), config).Unwrap();

        await Verify(error);
    }
    
    [Fact]
    public async Task MissingMethodIsReported()
    {
        var testAssembly = await TestProject.Project.CompileToRealAssemblyAsBytes();
        var coreAssembly = File.OpenRead("MakeItMeta.Attributes.dll");
        var thisAssembly = File.OpenRead(GetType().Assembly.Location);

        var config = new InjectionConfig(
            new Stream[]
            {
                coreAssembly,
                thisAssembly
            },
            new[]
            {
                new InjectionEntry
                {
                    Attribute = "MakeItMeta.Tests.TestAttribute",
                    Type = "MakeItMeta.TestApp.Executor",
                    Methods = new[] { "MissingMethod" }
                }
            });

        var maker = new MetaMaker();
        var (_, error) = maker.MakeItMeta(testAssembly.AsStream(), config).Unwrap();

        await Verify(error);
    }
    
    [Fact]
    public async Task MissingTypeIsReported()
    {
        var testAssembly = await TestProject.Project.CompileToRealAssemblyAsBytes();
        var coreAssembly = File.OpenRead("MakeItMeta.Attributes.dll");
        var thisAssembly = File.OpenRead(GetType().Assembly.Location);

        var config = new InjectionConfig(
            new Stream[]
            {
                coreAssembly,
                thisAssembly
            },
            new[]
            {
                new InjectionEntry
                {
                    Attribute = "MakeItMeta.Tests.TestAttribute",
                    Type = "MakeItMeta.TestApp.MissingExecutor",
                    Methods = new[] { "Execute" }
                }
            });

        var maker = new MetaMaker();
        var (_, error) = maker.MakeItMeta(testAssembly.AsStream(), config).Unwrap();

        await Verify(error);
    }
}