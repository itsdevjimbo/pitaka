using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Requests;

public record CategoryRequest (
    [Required, MaxLength(255)]
    string Name,

    [Required]
    CategoryType Type,

    string? Description,

    [MaxLength(100)]
    string? Icon,

    [MaxLength(100)]
    string? Color
);