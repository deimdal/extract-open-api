using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Readers;

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

        rootCommand.SetAction((parseResult, _) => ProcessDocument(
            parseResult.GetRequiredValue(sourceOption),
            parseResult.GetRequiredValue(destinationFileOption),
            parseResult.GetRequiredValue(pathsFilterOption),
            parseResult.GetValue(destinationVersionOption),
            parseResult.GetValue(destinationFormatOption)
        ));

        var parseResult = rootCommand.Parse(args);
        return parseResult.InvokeAsync();
    }

    private static async Task ProcessDocument(
        string source,
        FileInfo destinationFile,
        string[] pathFilter,
        OpenApiSpecVersion destinationVersion,
        OpenApiFormat destinationFormat)
    {
        Stream? stream = null;
        try
        {
            var paths = new Dictionary<string, string[]?>();
            foreach (var pathFilterSpec in pathFilter)
            {
                var parts = pathFilterSpec.Split('=');
                if (parts.Length > 2)
                    throw new Exception($"Invalid path filter specification: '{pathFilterSpec}'. Expected: path[=operation1[,operation2,...]]");
                var methods = parts.Length == 2 ? parts[1].Split(',') : null;
                if (paths.ContainsKey(parts[0]))
                    throw new Exception($"Duplicate path filter specification: '{parts[0]}' in '{pathFilterSpec}'.");
                paths.Add(parts[0], methods);
            }

            if (Uri.IsWellFormedUriString(source, UriKind.Absolute))
            {
                Console.WriteLine($"Downloading '{source}'...");
                using var client = new HttpClient();
                stream = await client.GetStreamAsync(source);
            }
            else
            {
                if (!File.Exists(source))
                    throw new FileNotFoundException($"File '{source}' not found.");
                stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            var doc = new OpenApiStreamReader().Read(stream, out var diagnostic);
            await stream.DisposeAsync();
            stream = null;

            if (diagnostic.Errors.Any())
            {
                Console.WriteLine($"Errors in OpenApi document '{source}': {string.Join(", ", diagnostic.Errors)}");
                Console.WriteLine("This is a warning message only. Processing is being continued.");
            }

            OpenApiDocumentModifier.Modify(doc, paths);

            if (destinationFile.Exists)
                destinationFile.Delete();

            await using var fileStream = destinationFile.OpenWrite();
            doc.Serialize(fileStream, destinationVersion, destinationFormat);
            await fileStream.FlushAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        finally
        {
            if (stream != null)
                await stream.DisposeAsync();
        }

        Console.WriteLine("Process is completed.");
    }
}
