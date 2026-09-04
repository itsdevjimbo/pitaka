using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Actions;

[Collection("Database collection")]
public class VerifyTransactionCategoryTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly VerifyTransactionCategory _verifyTransactionCategory;

    public VerifyTransactionCategoryTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _verifyTransactionCategory = _scope.ServiceProvider.GetRequiredService<VerifyTransactionCategory>();
    }

    [Fact]
    public async Task Verify_NoSuchCategory_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);

        var verdict = await _verifyTransactionCategory.VerifyAsync(user, 999999, CategoryType.Income);

        Assert.Equal(TransactionCategoryVerdict.NotFound, verdict);
    }

    [Fact]
    public async Task Verify_AnotherUsersCategoryOfTheRightType_ReturnsNotFound()
    {
        var owner = await UserFactory.CreateAsync(_context);
        var other = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, owner.Id, type: CategoryType.Expense);

        var verdict = await _verifyTransactionCategory.VerifyAsync(other, category.Id, CategoryType.Expense);

        Assert.Equal(TransactionCategoryVerdict.NotFound, verdict);
    }

    [Fact]
    public async Task Verify_OwnCategoryOfTheWrongType_ReturnsTypeMismatch()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Income);

        var verdict = await _verifyTransactionCategory.VerifyAsync(user, category.Id, CategoryType.Expense);

        Assert.Equal(TransactionCategoryVerdict.TypeMismatch, verdict);
    }

    [Fact]
    public async Task Verify_SystemDefaultCategoryOfTheWrongType_ReturnsTypeMismatch()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, name: "Salary", type: CategoryType.Income);

        var verdict = await _verifyTransactionCategory.VerifyAsync(user, category.Id, CategoryType.Expense);

        Assert.Equal(TransactionCategoryVerdict.TypeMismatch, verdict);
    }

    [Fact]
    public async Task Verify_OwnCategoryOfTheExpectedType_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);

        var verdict = await _verifyTransactionCategory.VerifyAsync(user, category.Id, CategoryType.Expense);

        Assert.Equal(TransactionCategoryVerdict.Ok, verdict);
    }

    [Fact]
    public async Task Verify_SystemDefaultCategoryOfTheExpectedType_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, type: CategoryType.Income);

        var verdict = await _verifyTransactionCategory.VerifyAsync(user, category.Id, CategoryType.Income);

        Assert.Equal(TransactionCategoryVerdict.Ok, verdict);
    }

    public void Dispose() => _scope.Dispose();
}
