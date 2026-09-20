using PitakaApp.Api.Models;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Resources;

public sealed record LinkedContributionAccountResource(
    int Id,
    string Name,
    decimal CurrentBalance,
    decimal EarmarkedTotal,
    decimal AvailableHeadroom,
    bool Active
);

public sealed record LinkedContributionResource(
    int Id,
    int GoalId,
    int AccountId,
    int? TransactionId,
    decimal Amount,
    DateOnly ContributionDate,
    string? Note,
    string GoalName
)
{
    public static LinkedContributionResource FromModel(GoalContribution contribution) =>
        new(
            contribution.Id,
            contribution.GoalId,
            contribution.AccountId,
            contribution.TransactionId,
            contribution.Amount,
            contribution.ContributionDate,
            contribution.Note,
            contribution.Goal.Name
        );
}

public sealed record LinkedContributionSnapshotResource(
    int TransactionId,
    decimal TransactionAmount,
    decimal LinkedTotal,
    decimal RemainingCapacity,
    LinkedContributionAccountResource Account,
    IReadOnlyList<LinkedContributionResource> LinkedContributions
)
{
    public static LinkedContributionSnapshotResource FromSnapshot(
        LinkedContributionSnapshot snapshot
    )
    {
        var transaction = snapshot.Transaction;
        var account = transaction.Account;

        return new(
            transaction.Id,
            transaction.Amount,
            snapshot.LinkedTotal,
            transaction.Amount - snapshot.LinkedTotal,
            new(
                account.Id,
                account.Name,
                account.CurrentBalance,
                snapshot.EarmarkedTotal,
                account.CurrentBalance - snapshot.EarmarkedTotal,
                account.IsActive
            ),
            [.. snapshot.LinkedContributions.Select(LinkedContributionResource.FromModel)]
        );
    }
}
