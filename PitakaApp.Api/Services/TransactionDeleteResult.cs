namespace PitakaApp.Api.Services;

public abstract record TransactionDeleteResult;

public sealed record TransactionDeleted : TransactionDeleteResult;

public sealed record TransactionHasLinkedContributions(
    int TransactionId,
    IReadOnlyList<TransactionLinkedContribution> LinkedContributions
) : TransactionDeleteResult;

public sealed record TransactionLinkedContribution(int ContributionId, int GoalId, string GoalName);
