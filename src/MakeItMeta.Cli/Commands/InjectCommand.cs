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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true
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
            await WriteError(console, error);
            return;
        }

        var validationError = ValidateInputConfig(console, inputConfig).Unwrap();
        if (validationError)
        {
            await WriteError(console, validationError);
            return;
        }

        await console.Output.WriteLineAsync("Config is validated");

        var attributes = inputConfig.Attributes?
            .Select(att =>
            {
                var add = att.Add?
                    .Select(type => new InjectionTypeEntry(type.Name!, type.Methods))
                    .ToArray();
                
                var ignore = att.Ignore?
                    .Select(type => new InjectionTypeEntry(type.Name!, type.Methods))
                    .ToArray();

                return new InjectionEntry(att.Name!, add, ignore);
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

        var searchFolders = inputConfig.TargetAssemblies!
            .Select(o => Path.GetDirectoryName(o)!)
            .Distinct()
            .ToArray();

        var maker = new MetaMaker();
        var (resultAssemblies, metaMakingError) = maker.MakeItMeta(targetAssemblies, injectionConfig, searchFolders).Unwrap();
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
        catch(Exception ex)
        {
            return new[]
            {
                new Error("FAILED_TO_PARSE_CONFIG", "Failed parse config file at " + Config),
                new Error("FAILED_TO_PARSE_CONFIG", ex.Message),
            };
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
        
        var typeWithoutName = inputConfig.Attributes
            .SelectMany(attribute => attribute.Add?
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