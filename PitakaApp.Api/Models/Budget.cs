namespace PitakaApp.Api.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PitakaApp.Api.Enums;

public class Budget : BaseEntity
{
    public required int UserId { get; set; }

    public int? CategoryId { get; set; }

    [Column(TypeName = "decimal(14, 2)")]
    public required decimal AmountLimit { get; set; }

    [MaxLength(100)]
    public required BudgetPeriod Period { get; set; }

    public required DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }


    public User User { get; set; } = null!;

    public Category? Category { get; set; }

}