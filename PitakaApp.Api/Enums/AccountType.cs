namespace PitakaApp.Api.Enums;

public enum AccountType
{
    Cash = 0,
    Bank = 1,

    // 2 was CreditCard. Keep the gap so persisted values and numeric API inputs for the
    // surviving types do not silently change meaning.
    Wallet = 3,
    Investment = 4,
}
