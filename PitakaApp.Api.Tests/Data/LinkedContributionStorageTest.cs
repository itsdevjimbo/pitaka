using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using PitakaApp.Api.Data;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Data;

[Collection("Database collection")]
public class LinkedContributionStorageTest(PitakaWebApplicationFactory factory)
{
    [Fact]
    public async Task OneIncomeTransaction_CanHaveSeveralLinkedContributions()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 500);
        var firstGoal = await GoalFactory.CreateAsync(context, user.Id, name: "Emergency fund");
        var secondGoal = await GoalFactory.CreateAsync(context, user.Id, name: "Holiday");
        var transaction = await TransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            amount: 500
        );

        await GoalContributionFactory.CreateAsync(
            context,
            firstGoal.Id,
            account.Id,
            transaction.Id,
            amount: 200
        );
        await GoalContributionFactory.CreateAsync(
            context,
            secondGoal.Id,
            account.Id,
            transaction.Id,
            amount: 300
        );

        var linked = await context
            .GoalContributions.AsNoTracking()
            .Where(gc => gc.TransactionId == transaction.Id)
            .OrderBy(gc => gc.Id)
            .ToListAsync();

        Assert.Equal([200m, 300m], linked.Select(gc => gc.Amount));
    }
}
