using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Models;

public class RecurringTransaction : TimestampedEntity
{
    public required int UserId { get; set; }

    public required int AccountId { get; set; }

    public int? CategoryId { get; set; }
    
    [MaxLength(255)]
    public required string Name { get; set; }

    public required RecurringTransactionType Type { get; set; }

    [Column(TypeName = "decimal(14, 2)")]
    public required decimal Amount { get; set; }

    public string? Description { get; set; }

    public required Frequency Frequency { get; set; }
    
    public required DateOnly StartDate { get; set;}

    public DateOnly? EndDate { get; set; }

    public required DateOnly NextRunDate { get; set; }

    public RecurringTransactionStatus Status { get; set; } = RecurringTransactionStatus.Active;

    
    public User User { get; set;} = null!;

    public Account Account { get; set; } = null!;

    public Category? Category { get; set; }

    public bool CanSetEndDate(DateOnly endDate)
    {
        return endDate > StartDate;
    }
}