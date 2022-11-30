using MakeItMeta.Cli.Data;
namespace MakeItMeta.Tests.Cli;

[UsesVerify]
public class JsonSchema
{
    [Fact]
    public async Task GenerateSchema()
    {
        var schema = NJsonSchema.JsonSchema.FromType<InjectionConfigInput>();

        await Verify(schema.ToJson(), "json")
            .UseFileName("schema")
            .UseDirectory("../../..");
    }
}