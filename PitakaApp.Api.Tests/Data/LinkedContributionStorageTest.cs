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

    [Fact]
    public async Task Migration_PreservesHistoricalLinkedContributionAndRestrictiveForeignKey()
    {
        var connection = new MySqlConnectionStringBuilder(
            PitakaWebApplicationFactory.TestConnectionString
        )
        {
            Database = $"ptkmig_{Guid.NewGuid():N}"[..15],
        };
        var options = new DbContextOptionsBuilder<PitakaDbContext>()
            .UseMySql(connection.ConnectionString, ServerVersion.Parse("8.0.0-mysql"))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var context = new PitakaDbContext(options);
        try
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(
                "20260919102048_TrackRecurringTransactionGenerationHistory"
            );

            var user = await UserFactory.CreateAsync(context);
            var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 500);
            var transaction = await TransactionFactory.CreateAsync(
                context,
                user.Id,
                account.Id,
                amount: 500
            );
            var now = DateTime.UtcNow;
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO goals
                    (id, user_id, name, target_amount, target_date, status, created_at, updated_at)
                VALUES
                    (700001, {user.Id}, 'Historical goal', 1000.00, NULL, 'Active', {now}, NULL)
                """
            );
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO goal_contributions
                    (id, goal_id, account_id, transaction_id, amount, contribution_date, note, created_at, updated_at)
                VALUES
                    (700001, 700001, {account.Id}, {transaction.Id}, 125.00, '2026-09-18', 'kept', {now}, NULL)
                """
            );

            context.ChangeTracker.Clear();
            await migrator.MigrateAsync();

            var preserved = await context
                .GoalContributions.AsNoTracking()
                .SingleAsync(gc => gc.Id == 700001);
            Assert.Equal(transaction.Id, preserved.TransactionId);
            Assert.Equal(125m, preserved.Amount);
            Assert.Equal(new DateOnly(2026, 9, 18), preserved.ContributionDate);
            Assert.Equal("kept", preserved.Note);

            context.Transactions.Remove(
                await context.Transactions.SingleAsync(t => t.Id == transaction.Id)
            );
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }
}
