namespace PitakaApp.Api.Models;

using System.ComponentModel.DataAnnotations;
public class Tag : BaseEntity
{
    public int? UserId { get; set; }


    [MaxLength(255)]
    public required string Name { get; set; }

    public User? User { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}