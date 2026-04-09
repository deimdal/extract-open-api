using System.CommandLine;
using System.CommandLine.Help;
using System.IO;
using System.Threading.Tasks;
using Microsoft.OpenApi;

namespace Deimdal.ExtractOpenApi;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        var sourceOption = new Option<string>("--source", "-s")
        {
            Required = true,
            AllowMultipleArgumentsPerToken = false,
            Description = "Source OpenApi specification location (file or URL)."
        };
        var pathsFilterOption = new Option<string[]>("--paths", "-p")
        {
            Required = true,
            AllowMultipleArgumentsPerToken = true,
            Description = "Path selection filter. Format: path1[=operation1[,operation2,...]] path2[=operation3[,operation4,...]]"
        };
        var destinationFileOption = new Option<FileInfo>("--dest-file", "-d")
        {
            Required = true,
            AllowMultipleArgumentsPerToken = false,
            Description = "Destination OpenApi file name."
        };
        var destinationVersionOption = new Option<OpenApiSpecVersion>("--dest-version", "-v")
        {
            Required = false,
            AllowMultipleArgumentsPerToken = false,
            Description = "Destination OpenApi document version.",
            DefaultValueFactory = _ => OpenApiSpecVersion.OpenApi3_0
        };
        var destinationFormatOption = new Option<OpenApiFormat>("--dest-format", "-f")
        {
            Required = false,
            AllowMultipleArgumentsPerToken = false,
            Description = "Destination OpenApi document format.",
            DefaultValueFactory = _ => OpenApiFormat.Yaml
        };

        var rootCommand = new Command("extract-open-api", "Modify OpenApi document to contain only selected paths and operations")
        {
            sourceOption,
            pathsFilterOption,
            destinationFileOption,
            destinationVersionOption,
            destinationFormatOption,
            new HelpOption(),
            new VersionOption()
        };

        rootCommand.SetAction((parseResult, _) => OpenApiDocumentModifier.ProcessDocument(
            parseResult.GetRequiredValue(sourceOption),
            parseResult.GetRequiredValue(destinationFileOption),
            parseResult.GetRequiredValue(pathsFilterOption),
            parseResult.GetValue(destinationVersionOption),
            parseResult.GetValue(destinationFormatOption)
        ));

        var parseResult = rootCommand.Parse(args);
        return parseResult.InvokeAsync();
    }
}
