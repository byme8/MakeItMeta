using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using MakeItMeta.Tools;
using MakeItMeta.Tools.Results;

#pragma warning disable CS8618

namespace MakeItMeta.Cli.Commands;

[Command("inject")]
public class InjectCommand : ICommand
{
    [CommandOption("config", 'c', Description = "The injection configuration file.")]
    public string? Config { get; set; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        if (!File.Exists(Config))
        {
            await console.Error.WriteLineAsync("Failed to find config file at " + Config);
            return;
        }

        var json = await File.ReadAllTextAsync(Config);
        var (inputConfig, error) = MetaValidator.ReadConfig(json).Unwrap();
        if (error)
        {
            await WriteError(console, error);
            return;
        }
        await console.Output.WriteLineAsync("Config is validated");

        var maker = new MetaMaker();
        var (resultAssemblies, metaMakingError) =
            maker.MakeItMeta(inputConfig.TargetAssemblies, inputConfig.InjectionConfig, inputConfig.SearchFolders).Unwrap();
        if (metaMakingError)
        {
            await console.Error.WriteLineAsync(metaMakingError.Errors.First().Message);
            return;
        }

        foreach (var targetAssembly in inputConfig.TargetAssemblies)
        {
            await targetAssembly.DisposeAsync();
        }

        var pathAndAssembly = inputConfig.TargetAssembliesPath
            .Zip(resultAssemblies, (path, stream) => new { Path = path, Stream = stream })
            .ToArray();

        foreach (var pathAndAssemblyStream in pathAndAssembly)
        {
            try
            {
                File.Delete(pathAndAssemblyStream.Path);
                await using var file = File.Open(pathAndAssemblyStream.Path, FileMode.CreateNew);
                await pathAndAssemblyStream.Stream.CopyToAsync(file);
                await console.Output.WriteLineAsync($"Modified: {pathAndAssemblyStream.Path}");
            }
            catch (Exception e)
            {
                await console.Error.WriteLineAsync($"Failed to modify: {pathAndAssemblyStream.Path}");
                await console.Error.WriteLineAsync(e.ToString());
                return;
            }
        }

        await console.Output.WriteLineAsync("Done!");
    }

    private async Task WriteError(IConsole console, UnwrapErrors errorWrap)
    {
        foreach (var error in errorWrap.Errors)
        {
            await console.Error.WriteLineAsync(error.Message);
        }
    }
}