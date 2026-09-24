using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Models;

public enum ProfilePictureObjectState
{
    Uploading,
    Current,
    PendingDeletion,
    Deleting,
}

// Durable lifecycle record for one immutable object key. The Profile's reference and
// this state change in one database transaction, so cleanup cannot race a current
// picture or make a losing upload current after cleanup has claimed it.
public class ProfilePictureObject
{
    [MaxLength(128)]
    public required string ObjectKey { get; set; }

    public ProfilePictureObjectState State { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? NextAttemptAt { get; set; }

    public DateTime? DeletionLeaseUntil { get; set; }

    public string? DeletionLeaseToken { get; set; }

    public int DeletionAttempts { get; set; }
}
