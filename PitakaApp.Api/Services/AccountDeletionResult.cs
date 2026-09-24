namespace PitakaApp.Api.Services;

public enum AccountDeletionResult
{
    Deleted,
    NotFound,
    HasTransactionHistory,
    HasGoalContributions,
    HasGeneratedRecurringTransactions,
}
