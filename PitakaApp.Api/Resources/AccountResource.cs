using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record AccountResource(int Id, int UserId, string Name, AccountType Type, decimal InitialBalance, decimal CurrentBalance, bool IsActive)
{
    public static AccountResource FromModel(Account account) =>
        new(account.Id, account.UserId, account.Name, account.Type, account.InitialBalance, account.CurrentBalance, account.IsActive);

    public static List<AccountResource> Collection(IEnumerable<Account> accounts) =>
        accounts.Select(FromModel).ToList();
} 