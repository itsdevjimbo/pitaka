using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Requests;

public record GoalStatusPatchRequest (
    [Required]
    GoalStatus Status
);