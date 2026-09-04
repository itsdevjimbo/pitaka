using Bogus;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public class AccountFactory
{
    public static Account Make(int userId, string? name = null, AccountType? type = null, decimal? initialBalance = null, bool? isActive = true)
    {
        var faker = new Faker();

        var account = Account.Open(
            userId,
            name ?? faker.Person.FullName,
            type ?? AccountType.Bank,
            initialBalance ?? 0);

        if (!(isActive ?? true))
        {
            account.Deactivate();
        }

        return account;
    }

    public static async Task<Account> CreateAsync(PitakaDbContext context, int userId, string? name = null, AccountType? type = null, decimal? initialBalance = null, bool? isActive = true)
    {
        var account = Make(userId, name, type, initialBalance, isActive);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account;
    }
}