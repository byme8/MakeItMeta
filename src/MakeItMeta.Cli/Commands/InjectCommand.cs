using System.Text;
using System.Text.Json;
using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using MakeItMeta.Cli.Data;
using MakeItMeta.Tools;
using MakeItMeta.Tools.Results;
#pragma warning disable CS8618

namespace MakeItMeta.Cli.Commands;

[Command("inject")]
public class InjectCommand : ICommand
{
    private JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

        var attributes = inputConfig.Attributes?
            .Select(att =>
            {
                var entries = att.Types?
                    .Select(type => new InjectionTypeEntry(type.Name!, type.Methods))
                    .ToArray();

                return new InjectionEntry(att.Name!, entries, att.All);
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
        var (resultAssemblies, metaMakingError) = maker.MakeItMeta(targetAssemblies, injectionConfig).Unwrap();
        if (metaMakingError)
        {
            await console.Error.WriteLineAsync(metaMakingError.Errors.First().Message);
            return;
        }

        foreach (var targetAssembly in targetAssemblies)
        {
            await targetAssembly.DisposeAsync();
        }
        
        var pathAndAssembly = inputConfig.TargetAssemblies!
            .Zip(resultAssemblies, (path, stream) => new { Path = path, Stream = stream })
            .ToArray();

        foreach (var pathAndAssemblyStream in pathAndAssembly)
        {
            try
            {
                await using var file = File.Open(pathAndAssemblyStream.Path, FileMode.Create);
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
                .WithMessage("Failed to find target assembly at " + target);
        }

        foreach (var target in inputConfig.AdditionalAssemblies ?? Array.Empty<string>())
        {
            if (File.Exists(target))
            {
                continue;
            }

            return configValidationFailed
                .WithMessage("Failed to find target assembly at " + target);
        }

        if (inputConfig.Attributes is null)
        {
            return Result.Success();
        }

        var attributeWithoutType = inputConfig.Attributes
            .Where(x => x.Types is null && !x.All)
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
            .SelectMany(attribute => attribute.Types?
                .Select(type => (Attrbute: attribute, TypeName: type.Name))
                .ToArray() ?? Array.Empty<(InjectionAttributeInput Attrbute, string? TypeName)>())
            .Where(x => x.TypeName is null)
            .ToArray();

        if (typeWithoutName.Any())
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("The types do not contain names inside the attributes");
            foreach (var attribute in typeWithoutName)
            {
                stringBuilder.AppendLine($"- {attribute.Attrbute.Name}");
            }
            
            return configValidationFailed
                .WithMessage(stringBuilder.ToString());
        }

        return Result.Success();
    }
}