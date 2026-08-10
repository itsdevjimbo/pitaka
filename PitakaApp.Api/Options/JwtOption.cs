namespace PitakaApp.Api.Options;

using System.ComponentModel.DataAnnotations;

public class JwtOption
{
    public const string SectionName = "Jwt";

    [Required, MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int ExpiryMinutes { get; set; }
}