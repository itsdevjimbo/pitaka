using System.ComponentModel.DataAnnotations.Schema;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Models;

public class Transaction : TimestampedEntity
{
    public required int UserId { get; set; }

    public required int AccountId { get; init; }

    public int? CategoryId { get; set; }

    public required TransactionType Type { get; init; }
    
    [Column(TypeName = "decimal(14, 2)")]
    public required decimal Amount { get; init; }

    public int? TransferToAccountId { get; init; }

    public string? Description { get; set; }

    public required DateTime TransactionDate { get; set; }

    public int? RecurringTransactionId { get; init; }

    
    public User User { get; set; } = null!;

    public Account Account { get; set; } = null!;

    public Category? Category { get; set; }

    public Account? TransferToAccount { get; set; }

    public RecurringTransaction? RecurringTransaction { get; set; }

    public GoalContribution? GoalContribution { get; set; }

    public ICollection<Tag> Tags {get; set; } = new List<Tag>();
}