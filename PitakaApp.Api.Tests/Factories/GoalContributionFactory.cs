using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public class GoalContributionFactory
{
    public static GoalContribution Make(
        int goalId, int accountId, int? transactionId = null, decimal? amount = null, 
        DateTime? contributionDate = null, string? note = null
    )
    {
        return new GoalContribution
        {
            GoalId = goalId,
            AccountId = accountId,
            TransactionId = transactionId,
            Amount = amount ?? 100,
            ContributionDate = contributionDate ?? DateTime.Now,
            Note = note,
        };
    }

    public static async Task<GoalContribution> CreateAsync(
        PitakaDbContext context, int goalId, int accountId, int? transactionId = null, 
        decimal? amount = null, DateTime? contributionDate = null, string? note = null
    )
    {
        var goalContribution = Make(goalId, accountId, transactionId, amount, contributionDate, note);
        context.GoalContributions.Add(goalContribution);
        await context.SaveChangesAsync();
        return goalContribution;
    }
}