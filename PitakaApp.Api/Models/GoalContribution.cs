using System.ComponentModel.DataAnnotations.Schema;

namespace PitakaApp.Api.Models;

public class GoalContribution : BaseEntity
{
    public required int GoalId { get; set; }

    public int? TransactionId { get; set; }

    [Column(TypeName = "decimal(14, 2)")]
    public required decimal Amount { get; set; }

    public required DateTime ContributionDate { get; set; }

    public string? Note { get; set; }



    public Goal Goal { get; set; } = null!;

    public Transaction? Transaction { get; set; }
}