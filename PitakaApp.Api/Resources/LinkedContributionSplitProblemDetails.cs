using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Services;

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RowIndex = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? GoalId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GoalName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        GoalStatus? CurrentState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AccountId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? AccountName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? TransactionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        decimal? TransactionAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        TransactionType? Direction = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        decimal? LinkedTotal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        decimal? RemainingCapacity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        decimal? CurrentBalance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        decimal? EarmarkedTotal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        decimal? AvailableHeadroom = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        decimal? CurrentProgress = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Target = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        decimal? ProposedProgress = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<int>? GoalIds = null
)
{
    public static LinkedContributionSplitFailureResource FromFailure(SplitFailure failure) =>
        new(
            failure.Reason,
            failure.RowIndex,
            failure.GoalId,
            ReferenceFact<string>(failure, "goalName"),
            ValueFact<GoalStatus>(failure, "currentState"),
            ValueFact<int>(failure, "accountId"),
            ReferenceFact<string>(failure, "accountName"),
            ValueFact<int>(failure, "transactionId"),
            ValueFact<decimal>(failure, "transactionAmount"),
            ValueFact<TransactionType>(failure, "direction"),
            ValueFact<decimal>(failure, "linkedTotal"),
            ValueFact<decimal>(failure, "remainingCapacity"),
            ValueFact<decimal>(failure, "currentBalance"),
            ValueFact<decimal>(failure, "earmarkedTotal"),
            ValueFact<decimal>(failure, "availableHeadroom"),
            ValueFact<decimal>(failure, "currentProgress"),
            ValueFact<decimal>(failure, "target"),
            ValueFact<decimal>(failure, "proposedProgress"),
            ReferenceFact<IReadOnlyList<int>>(failure, "goalIds")
        );

    private static TValue? ValueFact<TValue>(SplitFailure failure, string name)
        where TValue : struct =>
        failure.Facts.TryGetValue(name, out var value) && value is TValue fact ? fact : null;

    private static TValue? ReferenceFact<TValue>(SplitFailure failure, string name)
        where TValue : class =>
        failure.Facts.TryGetValue(name, out var value) ? value as TValue : null;
}
