using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Models;

public enum StoredFileState
{
    Uploading,
    Available,
    PendingDeletion,
    Deleting,
}

// Metadata and durable cleanup state for one immutable private object-storage key.
// Keep State available while any domain row references this file; transition it to
// PendingDeletion in the same transaction that removes the last reference.
public class StoredFile
{
    public int Id { get; set; }

    [MaxLength(128)]
    public required string ObjectKey { get; set; }

    [MaxLength(32)]
    public required string MediaType { get; set; }

    public StoredFileState State { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? NextAttemptAt { get; set; }

    public DateTime? DeletionLeaseUntil { get; set; }

    public string? DeletionLeaseToken { get; set; }

    public int DeletionAttempts { get; set; }
}
