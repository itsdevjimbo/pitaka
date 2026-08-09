namespace PitakaApp.Api.Models;

using System.ComponentModel.DataAnnotations.Schema;
using PitakaApp.Api.Enums;
public class Transaction : BaseEntity
{
    public required int UserId { get; set; }

    public required int AccountId { get; set; }

    public int? CategoryId { get; set; }

    public required TransactionType Type { get; set; }
    
    [Column(TypeName = "decimal(14, 2)")]
    public required decimal Amount { get; set; }

    public int? TransferToAccountId { get; set; }

    public string? Description { get; set; }

    public required DateTime TransactionDate { get; set; }

    public bool IsRecurring { get; set; } = false;

    public int? RecurringTransactionId { get; set; }

    
    public User User { get; set; } = null!;

    public Account Account { get; set; } = null!;

    public Category? Category { get; set; }

    public Account? TransferToAccount { get; set; }

    public RecurringTransaction? RecurringTransaction { get; set; }

    public GoalContribution? GoalContribution { get; set; }

    public ICollection<Tag> Tags {get; set; } = new List<Tag>();
}