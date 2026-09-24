using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Options;

public class ObjectStorageOption
{
    public const string SectionName = "ObjectStorage";

    [Required(ErrorMessage = "ObjectStorage:Endpoint must be set.")]
    public string Endpoint { get; set; } = string.Empty;

    [Required(ErrorMessage = "ObjectStorage:Region must be set.")]
    public string Region { get; set; } = string.Empty;

    [Required(ErrorMessage = "ObjectStorage:BucketName must be set.")]
    public string BucketName { get; set; } = string.Empty;

    [Required(ErrorMessage = "ObjectStorage:AccessKeyId must be set.")]
    public string AccessKeyId { get; set; } = string.Empty;

    [Required(ErrorMessage = "ObjectStorage:SecretAccessKey must be set.")]
    public string SecretAccessKey { get; set; } = string.Empty;
}
