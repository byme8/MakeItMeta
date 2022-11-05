using Buildalyzer;
using Buildalyzer.Workspaces;
using Microsoft.CodeAnalysis;
namespace MakeItMeta.Tests.Data;

public static class TestProject
{
    public static Project Project { get; }

    static TestProject()
    {
        var manager = new AnalyzerManager();
        manager.GetProject(@"../../../../MakeItMeta.TestApp/MakeItMeta.TestApp.csproj");

        var workspace = manager.GetWorkspace();
        Project = workspace.CurrentSolution.Projects.First(o => o.Name == "MakeItMeta.TestApp");
    }
}