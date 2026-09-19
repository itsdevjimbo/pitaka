using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Models;

// No foreign keys to historical resources: replay survives their deletion.
public class LinkedContributionOperation : TimestampedEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [MaxLength(36)]
    public required string Key { get; set; }

    [MaxLength(64)]
    public required string Fingerprint { get; set; }
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
}
