using System.Reflection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PitakaApp.Api.Requests;

namespace PitakaApp.Api.Infra;

public sealed class LinkedContributionOpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.JsonPropertyInfo?.AttributeProvider is not PropertyInfo property)
        {
            return Task.CompletedTask;
        }

        if (
            property.DeclaringType == typeof(CreateLinkedContributionSplitRequest)
            && property.Name == nameof(CreateLinkedContributionSplitRequest.Contributions)
        )
        {
            schema.MinItems = 1;
            schema.Description =
                "One or more ordered rows. Each Goal ID may appear only once in the request.";
        }

        if (
            property.DeclaringType == typeof(CreateLinkedContributionSplitRowRequest)
            && property.Name == nameof(CreateLinkedContributionSplitRowRequest.Amount)
        )
        {
            schema.Minimum = "0.01";
            schema.Maximum = "999999999999.99";
            schema.MultipleOf = 0.01m;
            schema.Description = "A positive, cent-precise amount.";
        }

        return Task.CompletedTask;
    }
}
