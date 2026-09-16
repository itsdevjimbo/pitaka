using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Models;

public class Account : TimestampedEntity
{
    public uint Version { get; set; }
    public required int UserId { get; set; }

    [MaxLength(255)]
    public required string Name { get; set; }

    public required AccountType Type { get; set; }

    [Column(TypeName = "decimal(14, 2)")]
    public decimal InitialBalance { get; init; } = 0;

    [Column(TypeName = "decimal(14, 2)")]
    public decimal CurrentBalance { get; private set; } = 0;

    public bool IsActive { get; set; } = true;

    public User User { get; set; } = null!;

    public ICollection<Transaction> Transactions { get; set; } = [];

    public ICollection<RecurringTransaction> RecurringTransactions { get; set; } = [];

    public static Account Open(int userId, string name, AccountType type, decimal initialBalance) =>
        new()
        {
            UserId = userId,
            Name = name,
            Type = type,
            InitialBalance = initialBalance,
            CurrentBalance = initialBalance,
        };

    public void Increase(decimal amount)
    {
        CurrentBalance += amount;
    }

    public void Decrease(decimal amount)
    {
        CurrentBalance -= amount;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
