using MakeItMeta.Tools.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NJsonSchema.Generation;
namespace MakeItMeta.Tests.Cli;

[UsesVerify]
public class JsonSchema
{
    [Fact]
    public async Task GenerateSchema()
    {
        var jsonSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };
        var jsonSchemaGeneratorSettings = new JsonSchemaGeneratorSettings()
        {
            SerializerSettings = jsonSerializerSettings
        };
        
        var schema = NJsonSchema.JsonSchema.FromType<InjectionConfigInput>(jsonSchemaGeneratorSettings);

        await Verify(schema.ToJson(), "json")
            .UseFileName("schema")
            .UseDirectory("../../..");
    }
}