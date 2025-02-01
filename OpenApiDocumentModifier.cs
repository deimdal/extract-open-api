using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;

namespace Deimdal.ExtractOpenApi;

public static class OpenApiDocumentModifier
{
    /// <summary>
    /// Filter OpenApi document operations and their schemes
    /// </summary>
    public static void Modify(OpenApiDocument doc, Dictionary<string, string[]?> paths)
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
                    if (openApiResponse.Content != null)
                        foreach (var (_, contentValue) in openApiResponse.Content)
                            CheckSchemaExtractable(contentValue.Schema, persistSchemas);
            }
        }

        foreach (var (key, value) in doc.Components.Schemas.ToList())
        {
            if (persistSchemas.Contains(value))
                continue;
            doc.Components.Schemas.Remove(key);
        }
    }

    private static void CheckSchemaExtractable(OpenApiSchema? schema, HashSet<OpenApiSchema> rootSchemas)
    {
        if (schema is { Reference.Id: not null })
        {
            var added = rootSchemas.Add(schema);
            if (added)
                ProcessProperties(schema, rootSchemas);
        }
        else if (schema is { Items.Reference: not null })
        {
            var added = rootSchemas.Add(schema.Items);
            if (added)
                ProcessProperties(schema.Items, rootSchemas);
        }
    }

    private static void ProcessProperties(OpenApiSchema schema, HashSet<OpenApiSchema> schemas)
    {
        foreach (var (_, contentValue) in schema.Properties)
            CheckSchemaExtractable(contentValue, schemas);
    }
}
