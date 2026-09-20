using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Resources;

public sealed class LinkedContributionSplitConflictProblemDetails : ProblemDetails
{
    public required string Reason { get; init; }
    public bool? Created { get; init; }
    public IReadOnlyList<LinkedContributionSplitFailureResource>? Failures { get; init; }
    public Guid? Key { get; init; }
}

public sealed class LinkedContributionSplitOutcomeUnknownProblemDetails : ProblemDetails
{
    public required string Reason { get; init; }
}

public sealed record LinkedContributionSplitFailureResource(
    string Reason,
    int? RowIndex = null,
    int? GoalId = null,
    string? GoalName = null,
    GoalStatus? CurrentState = null,
    int? AccountId = null,
    string? AccountName = null,
    int? TransactionId = null,
    decimal? TransactionAmount = null,
    TransactionType? Direction = null,
    decimal? LinkedTotal = null,
    decimal? RemainingCapacity = null,
    decimal? CurrentBalance = null,
    decimal? EarmarkedTotal = null,
    decimal? AvailableHeadroom = null,
    decimal? CurrentProgress = null,
    decimal? Target = null,
    decimal? ProposedProgress = null,
    IReadOnlyList<int>? GoalIds = null
);
