using Microsoft.OpenApi;
using Xunit;

namespace Deimdal.ExtractOpenApi.Tests;

public class ConversionTests
{
    [Theory]
    [InlineData("petstore.json", "petstore_single_method.yaml", "/pet/{petId}/uploadImage", OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Yaml)]
    [InlineData("petstore.json", "petstore_multi_methods.json", "/pet", OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Json)]
    [InlineData("petstore.json", "petstore_multi_methods_filtered.yaml", "/pet/{petId}=get,post", OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Yaml)]
    [InlineData("petstore.json", "petstore_multi_path.yaml", "/user/{username}%%/store/order/{orderId}=get", OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Yaml)]
    [InlineData("anyofapi.yaml", "anyofapi_single_method.yaml", "/process-data", OpenApiSpecVersion.OpenApi3_0, OpenApiFormat.Yaml)]
    public async Task ExtractSuccess(string input, string expected, string pathFilter, OpenApiSpecVersion specVersion, OpenApiFormat format)
    {
        var actualFilePath = Path.Combine(AppContext.BaseDirectory, "Schemas", "actual-" + expected);
        try
        {
            await OpenApiDocumentModifier.ProcessDocument(
                source: Path.Combine(AppContext.BaseDirectory, "Schemas", input),
                destinationFile: new FileInfo(actualFilePath),
                pathFilter: pathFilter.Split("%%"),
                destinationVersion: specVersion,
                destinationFormat: format
            );

            var expectedFile = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schemas", expected));
            var actualFile = await File.ReadAllTextAsync(actualFilePath);
            Assert.Equal(expectedFile, actualFile);
        }
        finally
        {
            if (File.Exists(actualFilePath))
                File.Delete(actualFilePath);
        }
    }
}
