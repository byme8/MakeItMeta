using System.Text.Json;
using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using MakeItMeta.Cli.Data;
using MakeItMeta.Core;
using MakeItMeta.Core.Results;
#pragma warning disable CS8618

namespace MakeItMeta.Cli.Commands;

[Command("inject")]
public class InjectCommand : ICommand
{
    private JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [CommandOption("input", 'i', Description = "The input file to inject into.")]
    public string Config { get; set; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        if (!File.Exists(Config))
        {
            await console.Error.WriteLineAsync("Failed to find config file at " + Config);
            return;
        }

        var json = await File.ReadAllTextAsync(Config);
        var (inputConfig, error) = ReadConfig(json).Unwrap();
        if (error)
        {
            await console.Error.WriteLineAsync(error.Errors.First().Message);
            return;
        }

        var failed = await ValidateInputConfig(console, inputConfig);
        if (failed)
        {
            return;
        }

        var attributes = inputConfig.Attributes!
            .Select(att =>
            {
                var entries = att.Types!
                    .Select(type => new InjectionTypeEntry(type.Name!, type.Methods))
                    .ToArray();

                return new InjectionEntry(att.Name!, entries);
            })
            .ToArray();

        var targetAssemblies = inputConfig.TargetAssemblies!
            .Select(File.OpenRead)
            .Cast<Stream>()
            .ToArray();

        var additionalAssemblies = inputConfig.AdditionalAssemblies?
            .Select(File.OpenRead)
            .Cast<Stream>()
            .ToArray();

        var injectionConfig = new InjectionConfig(additionalAssemblies, attributes);

        var maker = new MetaMaker();
        var resultAssemblies = maker.MakeItMeta(targetAssemblies, injectionConfig);
    }

    private Result<InjectionConfigInput> ReadConfig(string json)
    {
        try
        {
            var inputConfig = JsonSerializer.Deserialize<InjectionConfigInput>(json, options);
            if (inputConfig is null)
            {
                return new Error("FAILED_TO_PARSE_CONFIG", "Failed parse config file at " + Config);
            }
            return inputConfig;
        }
        catch
        {
            return new Error("FAILED_TO_PARSE_CONFIG", "Failed parse config file at " + Config);
        }
    }

    private static async Task<bool> ValidateInputConfig(IConsole console, InjectionConfigInput inputConfig)
    {
        if (inputConfig.TargetAssemblies is null)
        {
            await console.Error.WriteLineAsync("Config file does not contain any target assemblies");
            return true;
        }

        foreach (var target in inputConfig.TargetAssemblies)
        {
            if (File.Exists(target))
            {
                continue;
            }

            await console.Error.WriteLineAsync("failed to find target assembly at " + target);
            return true;
        }

        foreach (var target in inputConfig.AdditionalAssemblies ?? Array.Empty<string>())
        {
            if (File.Exists(target))
            {
                continue;
            }

            await console.Error.WriteLineAsync("failed to find target assembly at " + target);
            return true;
        }

        if (inputConfig.Attributes is null)
        {
            await console.Error.WriteLineAsync("Config file does not contain any attributes");
            return true;
        }

        var attributeWithoutType = inputConfig.Attributes
            .Where(x => x.Types is null)
            .ToArray();

        if (attributeWithoutType.Any())
        {
            await console.Error.WriteLineAsync("The following attributes do not contain any types");
            foreach (var attribute in attributeWithoutType)
            {
                await console.Error.WriteLineAsync($"- {attribute.Name}");
            }
            return true;
        }

        var typeWithoutName = inputConfig.Attributes
            .SelectMany(x => x.Types!)
            .Where(x => x.Name is null)
            .ToArray();

        if (typeWithoutName.Any())
        {
            await console.Error.WriteLineAsync("The following types do not contain a name");
            foreach (var type in typeWithoutName)
            {
                await console.Error.WriteLineAsync($"- {type.Name}");
            }
            return true;
        }

        return false;
    }
}