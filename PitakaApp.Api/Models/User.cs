using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace PitakaApp.Api.Models;

// IdentityUser<int> keeps the int primary key, so every existing foreign key and
// migration is untouched. It brings Email and PasswordHash (both now inherited), plus
// UserName, the Normalized* lookup columns, EmailConfirmed, SecurityStamp,
// ConcurrencyStamp, lockout and two-factor columns. ITimestamped is implemented
// directly, not through TimestampedEntity — C# has no multiple inheritance and
// IdentityUser<int> occupies the base-class slot. See ADR 0011.
public class User : IdentityUser<int>, ITimestamped
{
    [MaxLength(255)]
    public required string Name { get; set; }

    // The address this Profile has asked to move to but not yet proven control of (ADR
    // 0014). Never the live address: Email keeps working while this is set, and a value
    // whose PendingEmailExpiresAt is in the past is treated as absent — not returned,
    // not blocking a fresh request, not redeemable. Cleared in place on redemption or
    // cancel; overwritten when a new request supersedes an earlier one.
    [MaxLength(255)]
    public string? PendingEmail { get; set; }

    // When the pending email stops counting. Set from EmailChangeOption.TokenLifespan so
    // the stored state and the confirmation token die together.
    public DateTime? PendingEmailExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<Account> Accounts { get; set; } = new List<Account>();

    public ICollection<Category> Categories { get; set; } = new List<Category>();

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public ICollection<RecurringTransaction> RecurringTransactions { get; set; } = new List<RecurringTransaction>();

    public ICollection<Goal> Goals { get; set; } = new List<Goal>();

    public ICollection<Budget> Budgets { get; set; } = new List<Budget>();

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
}
