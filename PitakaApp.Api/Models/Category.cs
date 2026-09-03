using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Models;

public class Category : TimestampedEntity
{
    public int? UserId { get; set; }

    [MaxLength(255)]
    public required string Name { get; set;}

    public required CategoryType Type { get; init; }

    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Icon { get; set; }

    [MaxLength(100)]
    public string? Color { get; set; }

    public bool IsDefault { get; set; } = false;


    public User? User { get; set; }
}