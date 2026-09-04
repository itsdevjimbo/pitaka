using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace PitakaApp.Api.Models;

// IdentityUser<int> keeps the int primary key, so every existing foreign key and
// migration is untouched. It brings Email and PasswordHash (both now inherited), plus
// UserName, the Normalized* lookup columns, EmailConfirmed, SecurityStamp,
// ConcurrencyStamp, lockout and two-factor columns. ITimestamped is implemented
// directly, not through TimestampedEntity — C# has no multiple inheritance and
// IdentityUser<int> occupies the base-class slot. See
// .scratch/auth-identity/issues/02-identity-store-swap.md.
public class User : IdentityUser<int>, ITimestamped
{
    [MaxLength(255)]
    public required string Name { get; set; }

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
