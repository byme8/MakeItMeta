using MakeItMeta.Cli.Data;
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
        var schema = NJsonSchema.JsonSchema.FromType<InjectionConfigInput>(new JsonSchemaGeneratorSettings()
        {
            SerializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            }
        });

        await Verify(schema.ToJson(), "json")
            .UseFileName("schema")
            .UseDirectory("../../..");
    }
}