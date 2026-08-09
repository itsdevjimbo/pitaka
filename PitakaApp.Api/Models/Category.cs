namespace PitakaApp.Api.Models;

using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;

public class Category : TimestampedEntity
{
    public int? UserId { get; set; }

    public int? ParentId { get; set; }

    [MaxLength(255)]
    public required string Name { get; set;}

    public required CategoryType Type { get; set; }

    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Icon { get; set; }

    [MaxLength(100)]
    public string? Color { get; set; }

    public bool IsDefault { get; set; } = false;


    public User? User { get; set; }

    public Category? Parent { get; set;}

    public ICollection<Category> Children { get; set; } = new List<Category>();
    
}