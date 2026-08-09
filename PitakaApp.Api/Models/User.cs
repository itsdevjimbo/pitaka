namespace PitakaApp.Api.Models;

using System.ComponentModel.DataAnnotations;

public class User : BaseEntity
{
    [MaxLength(255)]
    public required string Name { get; set; }

    [MaxLength(255)]
    public required string Email { get; set; }

    [MaxLength(255)]
    public required string PasswordHash { get; set; }


    public ICollection<Account> Accounts { get; set; } = new List<Account>();

    public ICollection<Category> Categories { get; set; } = new List<Category>();

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public ICollection<RecurringTransaction> RecurringTransactions { get; set; } = new List<RecurringTransaction>();

    public ICollection<Goal> Goals { get; set; } = new List<Goal>();
    
    public ICollection<Budget> Budgets { get; set; } = new List<Budget>();

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
}