using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Actions;

[Collection("Database collection")]
public class VerifyBudgetCategoryTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly VerifyBudgetCategory _verifyBudgetCategory;

    public VerifyBudgetCategoryTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _verifyBudgetCategory = _scope.ServiceProvider.GetRequiredService<VerifyBudgetCategory>();
    }

    [Fact]
    public async Task Verify_NoSuchCategory_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);

        var verdict = await _verifyBudgetCategory.VerifyAsync(user, 999999);

        Assert.Equal(BudgetCategoryVerdict.NotFound, verdict);
    }

    [Fact]
    public async Task Verify_AnotherUsersExpenseCategory_ReturnsNotFound()
    {
        var owner = await UserFactory.CreateAsync(_context);
        var other = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, owner.Id, type: CategoryType.Expense);

        var verdict = await _verifyBudgetCategory.VerifyAsync(other, category.Id);

        Assert.Equal(BudgetCategoryVerdict.NotFound, verdict);
    }

    [Fact]
    public async Task Verify_OwnIncomeCategory_ReturnsNotExpense()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Income);

        var verdict = await _verifyBudgetCategory.VerifyAsync(user, category.Id);

        Assert.Equal(BudgetCategoryVerdict.NotExpense, verdict);
    }

    [Fact]
    public async Task Verify_SystemDefaultIncomeCategory_ReturnsNotExpense()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, name: "Salary", type: CategoryType.Income);

        var verdict = await _verifyBudgetCategory.VerifyAsync(user, category.Id);

        Assert.Equal(BudgetCategoryVerdict.NotExpense, verdict);
    }

    [Fact]
    public async Task Verify_OwnExpenseCategory_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);

        var verdict = await _verifyBudgetCategory.VerifyAsync(user, category.Id);

        Assert.Equal(BudgetCategoryVerdict.Ok, verdict);
    }

    [Fact]
    public async Task Verify_SystemDefaultExpenseCategory_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, type: CategoryType.Expense);

        var verdict = await _verifyBudgetCategory.VerifyAsync(user, category.Id);

        Assert.Equal(BudgetCategoryVerdict.Ok, verdict);
    }

    public void Dispose() => _scope.Dispose();
}
