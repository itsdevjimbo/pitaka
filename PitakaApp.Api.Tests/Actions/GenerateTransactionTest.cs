using PitakaApp.Api.Actions;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Tests.Factories;

namespace PitakaApp.Api.Tests.Actions;
public class GenerateTransactionTest
{
    [Fact]
    public void Generate_TypeIncome_MapsToTransactionTypeIncome()
    {
        var recurringTransaction = RecurringTransactionFactory.Make(1, 2, type: RecurringTransactionType.Income);

        var transactionDate = recurringTransaction.NextRunDate.ToDateTime(TimeOnly.MinValue);
        var transaction = GenerateTransaction.GetTransaction(recurringTransaction, transactionDate);

        Assert.Equal(TransactionType.Income, transaction.Type);
    }

    [Fact]
    public void Generate_TypeExpense_MapsToTransactionTypeExpense()
    {
        var recurringTransaction = RecurringTransactionFactory.Make(1, 2, type: RecurringTransactionType.Expense);

        var transactionDate = recurringTransaction.NextRunDate.ToDateTime(TimeOnly.MinValue);
        var transaction = GenerateTransaction.GetTransaction(recurringTransaction, transactionDate);

        Assert.Equal(TransactionType.Expense, transaction.Type);
    }

    [Fact]
    public void Generate_WithInvalidType_ThrowsInvalidOperationException()
    {
        var recurringTransaction = RecurringTransactionFactory.Make(
            1, 2, type: (RecurringTransactionType)999
        );

        var transactionDate = recurringTransaction.NextRunDate.ToDateTime(TimeOnly.MinValue);
        Assert.Throws<InvalidOperationException>(
            () => GenerateTransaction.GetTransaction(recurringTransaction, transactionDate)
        );
    }

    [Fact]
    public void Generate_ImportantProperties_MapsCorrectly()
    {
        var recurringTransaction = RecurringTransactionFactory.Make(2, 3, categoryId: 4);

        var transactionDate = recurringTransaction.NextRunDate.ToDateTime(TimeOnly.MinValue);
        var transaction = GenerateTransaction.GetTransaction(recurringTransaction, transactionDate);

        Assert.Equal(recurringTransaction.UserId, transaction.UserId);
        Assert.Equal(recurringTransaction.AccountId, transaction.AccountId);
        Assert.Equal(recurringTransaction.CategoryId, transaction.CategoryId);
        Assert.Equal(recurringTransaction.Amount, transaction.Amount);
    }

    [Fact]
    public void Generate_CorrectTransactionDate()
    {
        var nextRunDate = new DateOnly(2026, 08, 23);
        var recurringTransaction = RecurringTransactionFactory.Make(2, 3, nextRunDate: nextRunDate);

        var transactionDate = new DateTime(2026, 08, 26, 0, 0, 0);
        var transaction = GenerateTransaction.GetTransaction(recurringTransaction, transactionDate);

        Assert.Equal(transactionDate, transaction.TransactionDate);
    }

    [Fact]
    public void Generate_CorrectIsRecurringAndTransactionId()
    {
        var recurringTransaction = RecurringTransactionFactory.Make(2, 3);
        recurringTransaction.Id = 5;

        var transactionDate = recurringTransaction.NextRunDate.ToDateTime(TimeOnly.MinValue);
        var transaction = GenerateTransaction.GetTransaction(recurringTransaction, transactionDate);

        Assert.True(transaction.IsRecurring);
        Assert.Equal(5, transaction.RecurringTransactionId);
    }
}