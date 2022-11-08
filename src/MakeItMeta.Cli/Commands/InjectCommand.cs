using System.Text;
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

        var validationError = ValidateInputConfig(console, inputConfig).Unwrap();
        if (validationError)
        {
            await console.Error.WriteLineAsync(validationError.Errors.First().Message);
            return;
        }

        await console.Output.WriteLineAsync("Config is validated");

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

    private static Result ValidateInputConfig(IConsole console, InjectionConfigInput inputConfig)
    {
        var configValidationFailed = new Error("CONFIG_VALIDATION_FAILED", string.Empty);
        if (inputConfig.TargetAssemblies is null)
        {
            return configValidationFailed
                .WithMessage("Config file does not contain any target assemblies");
        }

        foreach (var target in inputConfig.TargetAssemblies)
        {
            if (File.Exists(target))
            {
                continue;
            }

            return configValidationFailed
                .WithMessage("failed to find target assembly at " + target);
        }

        foreach (var target in inputConfig.AdditionalAssemblies ?? Array.Empty<string>())
        {
            if (File.Exists(target))
            {
                continue;
            }

            return configValidationFailed
                .WithMessage("failed to find target assembly at " + target);
        }

        if (inputConfig.Attributes is null)
        {
            return configValidationFailed
                .WithMessage("Config file does not contain any attributes");
        }

        var attributeWithoutType = inputConfig.Attributes
            .Where(x => x.Types is null)
            .ToArray();

        if (attributeWithoutType.Any())
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("The following attributes do not contain any types");
            foreach (var attribute in attributeWithoutType)
            {
                stringBuilder.AppendLine($"- {attribute.Name}");
            }
            
            return configValidationFailed
                .WithMessage(stringBuilder.ToString());
        }

        var typeWithoutName = inputConfig.Attributes
            .SelectMany(x => x.Types!)
            .Where(x => x.Name is null)
            .ToArray();

        if (typeWithoutName.Any())
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("The following types do not contain a name");
            foreach (var type in typeWithoutName)
            {
                stringBuilder.AppendLine($"- {type.Name}");
            }
            
            return configValidationFailed
                .WithMessage(stringBuilder.ToString());
        }

        return Result.Success();
    }
}