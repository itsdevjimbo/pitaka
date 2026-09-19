using PitakaApp.Api.Actions;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Jobs;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Infra;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Auth
        services.AddScoped<RegisterUser>();
        services.AddScoped<LoginUser>();
        services.AddScoped<GenerateJwtToken>();
        services.AddScoped<GetCurrentUser>();
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<RequestPasswordReset>();
        services.AddScoped<ResetPassword>();
        services.AddScoped<SendEmailConfirmation>();
        services.AddScoped<ConfirmEmail>();
        services.AddScoped<ResendConfirmation>();
        services.AddScoped<RequestEmailChange>();
        services.AddScoped<RedeemEmailChange>();
        services.AddScoped<CancelEmailChange>();
        services.AddScoped<ChangeProfileName>();
        services.AddScoped<ChangePassword>();

        // Category
        services.AddScoped<CategoryService>();
        services.AddScoped<VerifyBudgetCategory>();
        services.AddScoped<VerifyTransactionCategory>();

        // Account
        services.AddScoped<AccountService>();
        services.AddScoped<UpdateAccountBalance>();
        services.AddScoped<ContributionGuards>();

        // Transaction
        services.AddScoped<TransactionService>();
        services.AddScoped<LinkedContributionSplitService>();

        // Budget
        services.AddScoped<BudgetService>();
        services.AddScoped<GetBudgetCycle>();
        services.AddScoped<GetBudgetAmountSpent>();
        services.AddScoped<GetBudgetWithSpend>();

        // Goal
        services.AddScoped<GoalService>();
        services.AddScoped<GoalContributionService>();
        services.AddScoped<GetGoalCurrentAmount>();

        // Recurring Transaction
        services.AddScoped<RecurringTransactionService>();
        services.AddScoped<GetNextRunDate>();
        services.AddScoped<GetDueRecurringTransactions>();
        services.AddScoped<GenerateDueRecurringTransactions>();

        // Tag
        services.AddScoped<TagService>();

        return services;
    }
}
