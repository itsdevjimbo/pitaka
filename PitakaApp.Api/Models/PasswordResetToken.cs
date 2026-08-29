using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Models;

public class PasswordResetToken : TimestampedEntity
{
    public required int UserId { get; set; }

    // The hex-encoded SHA-256 digest of the emailed token — 64 characters. Only the
    // hash is ever persisted; the plaintext lives in the email and the response path
    // and nowhere else, so a leaked backup contains no usable reset links.
    [MaxLength(64)]
    public required string TokenHash { get; set; }

    public required DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public User User { get; set; } = null!;
}
