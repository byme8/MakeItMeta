using CliFx;
using MakeItMeta.Cli.Commands;

await new CliApplicationBuilder()
    .AddCommand<InjectCommand>()
    .Build()
    .RunAsync();