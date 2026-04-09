using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Models;

namespace Deimdal.ExtractOpenApi;

public static class OpenApiDocumentModifier
{
    private static readonly Lazy<HttpClient> Client = new(() => new());

    /// <summary>
    /// Process local or remote document
    /// </summary>
    /// <param name="source">Source document URL or filename</param>
    /// <param name="destinationFile">Destination file</param>
    /// <param name="pathFilter">Path to extract</param>
    /// <param name="destinationVersion">Target document version</param>
    /// <param name="destinationFormat">Target document format</param>
    public static async Task ProcessDocument(
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
                stream = await Client.Value.GetStreamAsync(source);
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

            Modify(doc, paths);

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

    /// <summary>
    /// Filter OpenApi document operations and their schemes
    /// </summary>
    private static void Modify(OpenApiDocument doc, Dictionary<string, string[]?> paths)
    {
        var notFound = paths.Where(x => !doc.Paths.ContainsKey(x.Key)).ToList();
        if (notFound.Count > 0)
            throw new Exception("Path was not found in target specification file: " + string.Join(", ", notFound));

        FilterOperations(doc, paths);
        ShakeOperationsSchemaTree(doc);
    }

    /// <summary>
    /// Remove all operations, except filtered
    /// </summary>
    private static void FilterOperations(OpenApiDocument doc, Dictionary<string, string[]?> paths)
    {
        foreach (var pathKey in doc.Paths.Keys)
        {
            if (paths.TryGetValue(pathKey, out var operationFilter))
            {
                if (operationFilter == null)
                    continue;
                foreach (var operationType in doc.Paths[pathKey].Operations.Keys)
                {
                    if (!operationFilter.Contains(operationType.GetDisplayName()) && !doc.Paths[pathKey].Operations.Remove(operationType))
                        throw new Exception($"Can't remove operation '{operationType.GetDisplayName()}' for path '{pathKey}'");
                }
            }
            else if (!doc.Paths.Remove(pathKey))
                throw new Exception($"Can't remove path '{pathKey}'");
        }

        var tags = doc.Paths
            .SelectMany(x => x.Value.Operations)
            .SelectMany(x => x.Value.Tags)
            .Distinct()
            .ToList();

        for (var i = doc.Tags.Count - 1; i >= 0; i--)
            if (!tags.Contains(doc.Tags[i]))
                doc.Tags.RemoveAt(i);
    }

    /// <summary>
    /// Remove all schemas, except used in operations
    /// </summary>
    private static void ShakeOperationsSchemaTree(OpenApiDocument doc)
    {
        var persistSchemas = new HashSet<OpenApiSchema>();
        var persistResponses = new HashSet<OpenApiResponse>();
        foreach (var (_, pathItem) in doc.Paths)
        {
            foreach (var (_, operation) in pathItem.Operations)
            {
                foreach (var parameter in operation.Parameters)
                {
                    CheckSchemaExtractable(parameter.Schema, persistSchemas);
                    foreach (var (_, contentValue) in parameter.Content)
                        CheckSchemaExtractable(contentValue.Schema, persistSchemas);
                }

                if (operation.RequestBody?.Content != null)
                    foreach (var (_, contentValue) in operation.RequestBody.Content)
                        CheckSchemaExtractable(contentValue.Schema, persistSchemas);

                foreach (var (_, openApiResponse) in operation.Responses)
                {
                    persistResponses.Add(openApiResponse);
                    if (openApiResponse.Content != null)
                        foreach (var (_, contentValue) in openApiResponse.Content)
                        {
                            CheckSchemaExtractable(contentValue.Schema, persistSchemas);
                        }
                }
            }
        }

        foreach (var (key, value) in doc.Components.Schemas.ToList())
        {
            if (persistSchemas.Contains(value))
                continue;
            doc.Components.Schemas.Remove(key);
        }

        foreach (var (key, value) in doc.Components.Responses.ToList())
        {
            if (persistResponses.Contains(value))
                continue;
            doc.Components.Responses.Remove(key);
        }
    }

    private static void CheckSchemaExtractable(OpenApiSchema? schema, HashSet<OpenApiSchema> rootSchemas)
    {
        if (schema is { Reference.Id: not null })
        {
            ProcessPropertiesIfNew(schema, rootSchemas);
            if (schema is { Items.Reference: not null })
                ProcessPropertiesIfNew(schema.Items, rootSchemas);
        }
        else if (schema is { Items.Reference: not null })
            ProcessPropertiesIfNew(schema.Items, rootSchemas);
        else
        {
            if (schema is { AllOf: not null })
                foreach (var subschema in schema.AllOf.Where(s => s.Reference is not null))
                    ProcessPropertiesIfNew(subschema, rootSchemas);

            if (schema is { OneOf: not null })
                foreach (var subschema in schema.OneOf.Where(s => s.Reference is not null))
                    ProcessPropertiesIfNew(subschema, rootSchemas);

            if (schema is { AnyOf: not null })
                foreach (var subschema in schema.AnyOf.Where(s => s.Reference is not null))
                    ProcessPropertiesIfNew(subschema, rootSchemas);

            if (schema is { Not.Reference: not null })
                ProcessPropertiesIfNew(schema.Not, rootSchemas);
        }
    }

    private static void ProcessPropertiesIfNew(OpenApiSchema schema, HashSet<OpenApiSchema> schemas)
    {
        var added = schemas.Add(schema);
        if (added)
            ProcessProperties(schema, schemas);
    }

    private static void ProcessProperties(OpenApiSchema schema, HashSet<OpenApiSchema> schemas)
    {
        foreach (var (_, contentValue) in schema.Properties)
            CheckSchemaExtractable(contentValue, schemas);
    }
}
