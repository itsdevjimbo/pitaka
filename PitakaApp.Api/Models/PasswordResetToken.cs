using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace PitakaApp.Api.Models;

public class PasswordResetToken : TimestampedEntity
{
    // The plaintext token is hashed this one way — hex SHA-256, 64 chars — by the action
    // that mints it and by the action that spends it, so it lives beside the column it
    // produces rather than as a public static on one of those actions.
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

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
