using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Data;

[Collection("Database collection")]
public class GoalConcurrencyTest(PitakaWebApplicationFactory factory)
{
    [Fact]
    public async Task ConcurrentGoalChanges_SecondSaveIsRejected()
    {
        using var seedScope = factory.Services.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seedContext);
        var goal = await GoalFactory.CreateAsync(seedContext, user.Id);

        using var scopeA = factory.Services.CreateScope();
        using var scopeB = factory.Services.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<PitakaDbContext>();

        var goalA = await contextA.Goals.SingleAsync(g => g.Id == goal.Id);
        var goalB = await contextB.Goals.SingleAsync(g => g.Id == goal.Id);

        goalA.TargetAmount += 100;
        await contextA.SaveChangesAsync();

        goalB.TargetAmount += 50;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextB.SaveChangesAsync());
    }
}
