using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Models;

public class Budget : TimestampedEntity
{
    public required int UserId { get; set; }

    [MaxLength(255)]
    public required string Name { get; set; }

    public int? CategoryId { get; set; }

    [Column(TypeName = "decimal(14, 2)")]
    public required decimal AmountLimit { get; set; }

    [MaxLength(100)]
    public required BudgetPeriod Period { get; set; }

    public required DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string? Description { get; set; }


    public User User { get; set; } = null!;

    public Category? Category { get; set; }

}