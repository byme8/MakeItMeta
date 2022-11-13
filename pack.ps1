param (
    [string]$version = (Get-Date -Format "999.yyMM.ddHH.mmss")
) 

dotnet clean -c Release 
dotnet pack -c Release ./src/MakeItMeta.Attributes/MakeItMeta.Attributes.csproj --verbosity normal /p:Version=$version -o ./nugets
dotnet pack -c Release ./src/MakeItMeta.Tools/MakeItMeta.Tools.csproj --verbosity normal /p:Version=$version -o ./nugets
dotnet pack -c Release ./src/MakeItMeta.Cli/MakeItMeta.Cli.csproj --verbosity normal /p:Version=$version -o ./nugets
dotnet pack -c Release ./src/MakeItMeta/MakeItMeta.csproj --verbosity normal /p:Version=$version -o ./nugets
