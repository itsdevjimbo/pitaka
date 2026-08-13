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

        return new Account
        {
            UserId = userId,
            Name = name ?? faker.Person.FullName,
            Type =  type ?? AccountType.Bank,
            InitialBalance = initialBalance ?? 0,
            CurrentBalance = initialBalance ?? 0,
            IsActive = isActive ?? true
        };
    }

    public static async Task<Account> CreateAsync(PitakaDbContext context, int userId, string? name = null, AccountType? type = null, decimal? initialBalance = null, bool? isActive = true)
    {
        var account = Make(userId, name, type, initialBalance, isActive);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account;
    }
}