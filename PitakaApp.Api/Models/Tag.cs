using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Models;

public class Tag : TimestampedEntity
{
    public required int UserId { get; set; }

    [MaxLength(255)]
    public required string Name { get; set; }

    public User User { get; set; } = null!;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}