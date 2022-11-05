using System.Reflection;
using FluentAssertions;
using MakeItMeta.Attributes;
using MakeItMeta.Core;
using MakeItMeta.Tests.Core;
using MakeItMeta.Tests.Data;

namespace MakeItMeta.Tests;

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
        var resultAssembly = await maker.MakeItMeta(memoryStream);
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
                    Methods = new []{ "Execute" }
                }
            });

        var maker = new MetaMaker();
        var resultAssembly = await maker.MakeItMeta(testAssembly.AsStream(), config);

        var newAssembly = Assembly.Load(resultAssembly.ToArray());

        var result = newAssembly.Execute();
        TestAttribute.Assemblies.Should().Contain(newAssembly.FullName);
    }
}

public class TestAttribute : MetaAttribute
{
    public static HashSet<string> Assemblies { get; set; } = new();
    
    public override void OnEntry(object? @this, string methodName, object?[]? parameters)
    {
        var assembly = @this.GetType().Assembly;
        Assemblies.Add(assembly.FullName);
    }

    public override void OnExit(object? @this, string methodName)
    {
        
    }
}