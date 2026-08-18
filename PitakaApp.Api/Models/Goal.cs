namespace PitakaApp.Api.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PitakaApp.Api.Enums;

public class Goal : TimestampedEntity
{
    public required int UserId { get; set; }

    [MaxLength(255)]
    public required string Name { get; set; }

    [Column(TypeName = "decimal(14, 2)")]
    public required decimal TargetAmount { get; set; }

    public DateOnly? TargetDate { get; set; }

    public GoalStatus Status { get; set; } = GoalStatus.Active;

    public User User { get; set; } = null!;

    public ICollection<GoalContribution> Contributions { get; set; } = new List<GoalContribution>();
}