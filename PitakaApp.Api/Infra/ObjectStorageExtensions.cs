using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Options;

namespace PitakaApp.Api.Infra;

public static class ObjectStorageExtensions
{
    public static WebApplicationBuilder AddObjectStorage(this WebApplicationBuilder builder)
    {
        builder
            .Services.AddOptions<ObjectStorageOption>()
            .Bind(builder.Configuration.GetSection(ObjectStorageOption.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options =>
                    Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
                    && (
                        endpoint.Scheme == Uri.UriSchemeHttp
                        || endpoint.Scheme == Uri.UriSchemeHttps
                    ),
                "ObjectStorage:Endpoint must be an absolute HTTP or HTTPS URL."
            )
            .ValidateOnStart();

        builder.Services.AddSingleton<IAmazonS3>(serviceProvider =>
        {
            var storage = serviceProvider.GetRequiredService<IOptions<ObjectStorageOption>>().Value;
            var credentials = new BasicAWSCredentials(storage.AccessKeyId, storage.SecretAccessKey);
            var config = new AmazonS3Config
            {
                ServiceURL = storage.Endpoint,
                AuthenticationRegion = storage.Region,
                ForcePathStyle = true,
            };

            return new AmazonS3Client(credentials, config);
        });

        return builder;
    }
}
