namespace PitakaApp.Api.Data;

using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Models;
using PitakaApp.Api.Enums;

public static class AccountSeeder
{
    public static void Seed(DbContext context)
    {
        if (context.Set<Account>().Any())
        {
            return;
        }

        var user = SeedHelper.ExtractTransactionUser(context);

        var accounts = new List<Account>
        {
            new Account { UserId = 0, User = user, Name = "Checking Account", Type = AccountType.Bank, InitialBalance = 1000, CurrentBalance = 1000 },
            new Account { UserId = 0, User = user, Name = "Cash Wallet", Type = AccountType.Cash, InitialBalance = 100, CurrentBalance = 100 },
        };

        context.Set<Account>().AddRange(accounts);
    }
}