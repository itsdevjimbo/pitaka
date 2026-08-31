using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class BudgetsControllerTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public BudgetsControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task Get_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/budgets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithLoggedInUser_ReturnsTheirBudgets()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        await BudgetFactory.CreateAsync(_context, userB.Id);
        
        await BudgetFactory.CreateAsync(_context, userA.Id, name: "Test budget 1");
        await BudgetFactory.CreateAsync(_context, userA.Id, name: "Test budget 2");
        await BudgetFactory.CreateAsync(_context, userA.Id, name: "Test budget 3");
        
        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/budgets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<BudgetWithSpendResource>>(TestJsonOptions.Default);
        Assert.Equal(3, body!.Count);
    }

    [Fact]
    public async Task Create_WithNoLoggedInUser_ReturnsUnauthorized()
    {
        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNonExistentCategory_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = 99999
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithOtherUserCategory_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        var category = await CategoryFactory.CreateAsync(_context, userB.Id);
        _client.ActAsUser(userA);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = category.Id,
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await BudgetFactory.CreateAsync(_context, user.Id, "Transpo Budget");
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameDifferentUser_ReturnsCreated()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        await BudgetFactory.CreateAsync(_context, userB.Id, "Transpo Budget");
        _client.ActAsUser(userA);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithLoggedInUser_ReturnsCreatedStatusCode()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BudgetResource>(TestJsonOptions.Default);

        Assert.Equal("Transpo Budget", body!.Name);
        Assert.Equal(5000, body!.AmountLimit);
        Assert.Equal("Weekly", body!.Period.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now).ToString(), body!.StartDate.ToString());
        Assert.Null(body!.EndDate);
        Assert.Null(body!.Description);
        Assert.Null(body!.CategoryId);
    }

    [Fact]
    public async Task Create_UserCategory_ReturnsCreatedStatusCode()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = category.Id,
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_SystemDefaultCategory_ReturnsCreatedStatusCode()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = category.Id,
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Show_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var response = await _client.GetAsync("/api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Show_BudgetBelongsToOtherUser_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(userA);

        var budget = await BudgetFactory.CreateAsync(_context, userB.Id);

        var response = await _client.GetAsync("/api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_BelongsToUser_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var response = await _client.GetAsync("/api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BudgetResource>(TestJsonOptions.Default);

        Assert.Equal("Test budget", body!.Name);
        Assert.Equal(10000, body!.AmountLimit);
        Assert.Equal("Weekly", body!.Period.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now).ToString(), body!.StartDate.ToString());
        Assert.Null(body!.EndDate);
        Assert.Null(body!.Description);
        Assert.Null(body!.CategoryId);
    }

    // --- AmountSpent + cycle (see .scratch/budget-cycle-spend/spec.md) ---

    private static DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateTime UtcMidnightToday => DateTime.UtcNow.Date;

    private async Task<BudgetWithSpendResource> ShowBudget(User user, int budgetId)
    {
        _client.ActAsUser(user);
        var response = await _client.GetAsync("/api/budgets/" + budgetId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BudgetWithSpendResource>(TestJsonOptions.Default))!;
    }

    [Fact]
    public async Task Show_DailyBudget_ReturnsAmountSpentAndTodayCycle()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Daily, amountLimit: 5000,
            startDate: UtcToday.AddDays(-7));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 100, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 50, transactionDate: DateTime.UtcNow);
        // Non-counting: an expense outside the window.
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 999, transactionDate: UtcMidnightToday.AddDays(-1));

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(150m, body.AmountSpent);
        Assert.Equal(UtcToday, body.CycleStart);
        Assert.Equal(UtcToday, body.CycleEnd);
        Assert.Equal(5000m, body.AmountLimit);
    }

    [Fact]
    public async Task Show_AmountSpent_ExcludesIncomeTransferOtherCategoryAndOutOfWindow()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var groceries = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);
        var transport = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Daily, categoryId: groceries.Id,
            startDate: UtcToday.AddDays(-7));

        // Counts.
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 100, categoryId: groceries.Id, transactionDate: DateTime.UtcNow);
        // Excluded.
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Income, amount: 200, categoryId: groceries.Id, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Transfer, amount: 300, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 400, categoryId: transport.Id, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 500, categoryId: groceries.Id, transactionDate: UtcMidnightToday.AddDays(-1));
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 600, categoryId: groceries.Id, transactionDate: UtcMidnightToday.AddDays(1));

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(100m, body.AmountSpent);
    }

    [Fact]
    public async Task Show_AmountSpent_IncludesWindowBoundariesAndRecurringGenerated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurring = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Daily, startDate: UtcToday.AddDays(-7));

        // Exactly on CycleStart (midnight) and the last instant of CycleEnd both count.
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 100, transactionDate: UtcMidnightToday);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 50, transactionDate: UtcMidnightToday.AddDays(1).AddSeconds(-1));
        // Generated by a RecurringTransaction — ordinary for this sum.
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 25, transactionDate: DateTime.UtcNow, recurringTransactionId: recurring.Id);
        // Non-counting: one day before CycleStart, one day after CycleEnd.
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 999, transactionDate: UtcMidnightToday.AddDays(-1));
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 999, transactionDate: UtcMidnightToday.AddDays(1));

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(175m, body.AmountSpent);
    }

    [Fact]
    public async Task Show_NullCategoryBudget_CountsEveryExpenseInWindowIncludingUncategorised()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var a = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);
        var b = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Daily, categoryId: null,
            startDate: UtcToday.AddDays(-7));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 100, categoryId: a.Id, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 30, categoryId: b.Id, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 20, categoryId: null, transactionDate: DateTime.UtcNow);
        // Non-counting.
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Income, amount: 999, categoryId: a.Id, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 999, categoryId: null, transactionDate: UtcMidnightToday.AddDays(-1));

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(150m, body.AmountSpent);
    }

    [Fact]
    public async Task Show_BudgetOnParentCategory_DoesNotCountChildCategoryExpense()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var parent = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);
        var child = CategoryFactory.Make(user.Id, type: CategoryType.Expense);
        child.ParentId = parent.Id;
        _context.Categories.Add(child);
        await _context.SaveChangesAsync();

        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Daily, categoryId: parent.Id,
            startDate: UtcToday.AddDays(-7));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 100, categoryId: parent.Id, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 40, categoryId: child.Id, transactionDate: DateTime.UtcNow);

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(100m, body.AmountSpent);
    }

    [Fact]
    public async Task Show_AmountSpent_IgnoresAnotherUsersExpenseInTheSameWindow()
    {
        var user = await UserFactory.CreateAsync(_context);
        var other = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var otherAccount = await AccountFactory.CreateAsync(_context, other.Id);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Daily, startDate: UtcToday.AddDays(-7));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 100, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, other.Id, otherAccount.Id, TransactionType.Expense, amount: 999, transactionDate: DateTime.UtcNow);

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(100m, body.AmountSpent);
    }

    [Fact]
    public async Task Show_NoTransactionsInCycle_AmountSpentIsZero()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Daily, startDate: UtcToday.AddDays(-7));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 999, transactionDate: UtcMidnightToday.AddDays(-2));

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(0m, body.AmountSpent);
    }

    [Fact]
    public async Task Show_FutureStartDate_DescribesFirstCycleWithZeroSpend()
    {
        var user = await UserFactory.CreateAsync(_context);
        await AccountFactory.CreateAsync(_context, user.Id);
        var firstOfNextMonth = new DateOnly(UtcToday.Year, UtcToday.Month, 1).AddMonths(2);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Monthly, startDate: firstOfNextMonth);

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(firstOfNextMonth, body.CycleStart);
        Assert.Equal(firstOfNextMonth.AddMonths(1).AddDays(-1), body.CycleEnd);
        Assert.Equal(0m, body.AmountSpent);
    }

    [Fact]
    public async Task Show_PastEndDate_ReturnsFinalCycleAndItsTotal()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var start = new DateOnly(UtcToday.Year, UtcToday.Month, 1).AddMonths(-6);
        var end = new DateOnly(UtcToday.Year, UtcToday.Month, 15).AddMonths(-2);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Monthly, startDate: start, endDate: end, amountLimit: 5000);

        var insideFinalCycle = new DateOnly(end.Year, end.Month, 10).ToDateTime(TimeOnly.MinValue);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 100, transactionDate: insideFinalCycle);
        // Today's expense is outside the final (past) cycle.
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 999, transactionDate: DateTime.UtcNow);

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(new DateOnly(end.Year, end.Month, 1), body.CycleStart);
        Assert.Equal(end, body.CycleEnd);
        Assert.Equal(100m, body.AmountSpent);
        Assert.Equal(5000m, body.AmountLimit);
    }

    [Fact]
    public async Task Show_ShortFirstCycle_DoesNotProRateAmountLimit()
    {
        var user = await UserFactory.CreateAsync(_context);
        await AccountFactory.CreateAsync(_context, user.Id);
        var seventeenth = new DateOnly(UtcToday.Year, UtcToday.Month, 17);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Monthly, startDate: seventeenth, amountLimit: 5000);

        var body = await ShowBudget(user, budget.Id);

        Assert.Equal(seventeenth, body.CycleStart);
        Assert.Equal(5000m, body.AmountLimit);
    }

    // --- List: AmountSpent + cycle per Budget (see .scratch/budget-cycle-spend/issues/02) ---

    private async Task<List<BudgetWithSpendResource>> ListBudgets(User user)
    {
        _client.ActAsUser(user);
        var response = await _client.GetAsync("/api/budgets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<BudgetWithSpendResource>>(TestJsonOptions.Default))!;
    }

    [Fact]
    public async Task Get_BudgetsWithDifferentPeriods_EachReportsItsOwnWindow()
    {
        var user = await UserFactory.CreateAsync(_context);
        await AccountFactory.CreateAsync(_context, user.Id);
        var daily = await BudgetFactory.CreateAsync(
            _context, user.Id, name: "Daily", period: BudgetPeriod.Daily, startDate: UtcToday.AddDays(-30));
        var monthly = await BudgetFactory.CreateAsync(
            _context, user.Id, name: "Monthly", period: BudgetPeriod.Monthly, startDate: UtcToday.AddDays(-90));

        var body = await ListBudgets(user);

        var dailyResource = body.Single(b => b.Id == daily.Id);
        Assert.Equal(UtcToday, dailyResource.CycleStart);
        Assert.Equal(UtcToday, dailyResource.CycleEnd);

        var monthlyResource = body.Single(b => b.Id == monthly.Id);
        Assert.Equal(new DateOnly(UtcToday.Year, UtcToday.Month, 1), monthlyResource.CycleStart);
        Assert.Equal(
            new DateOnly(UtcToday.Year, UtcToday.Month, DateTime.DaysInMonth(UtcToday.Year, UtcToday.Month)),
            monthlyResource.CycleEnd);
    }

    [Fact]
    public async Task Get_TwoBudgetsOverlappingOnOneExpense_BothCountIt()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var groceries = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);
        var unnarrowed = await BudgetFactory.CreateAsync(
            _context, user.Id, name: "All spending", period: BudgetPeriod.Daily, categoryId: null,
            startDate: UtcToday.AddDays(-7));
        var categoryBudget = await BudgetFactory.CreateAsync(
            _context, user.Id, name: "Groceries", period: BudgetPeriod.Daily, categoryId: groceries.Id,
            startDate: UtcToday.AddDays(-7));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 120, categoryId: groceries.Id, transactionDate: DateTime.UtcNow);

        var body = await ListBudgets(user);

        Assert.Equal(120m, body.Single(b => b.Id == unnarrowed.Id).AmountSpent);
        Assert.Equal(120m, body.Single(b => b.Id == categoryBudget.Id).AmountSpent);
    }

    [Fact]
    public async Task Get_BudgetWithNoSpending_ReportsZeroBesideASiblingWithARealFigure()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var groceries = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);
        var transport = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);
        var spent = await BudgetFactory.CreateAsync(
            _context, user.Id, name: "Spent", period: BudgetPeriod.Daily, categoryId: groceries.Id,
            startDate: UtcToday.AddDays(-7));
        // Live cycle covering today, but nothing in its category lands in it.
        var quiet = await BudgetFactory.CreateAsync(
            _context, user.Id, name: "Quiet", period: BudgetPeriod.Daily, categoryId: transport.Id,
            startDate: UtcToday.AddDays(-7));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 75, categoryId: groceries.Id, transactionDate: DateTime.UtcNow);

        var body = await ListBudgets(user);

        Assert.Equal(75m, body.Single(b => b.Id == spent.Id).AmountSpent);
        Assert.Equal(0m, body.Single(b => b.Id == quiet.Id).AmountSpent);
    }

    [Fact]
    public async Task Get_FinishedNotYetStartedAndLiveBudgets_EachSitCorrectlyInTheList()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var finishedStart = new DateOnly(UtcToday.Year, UtcToday.Month, 1).AddMonths(-6);
        var finishedEnd = new DateOnly(UtcToday.Year, UtcToday.Month, 15).AddMonths(-2);
        var finished = await BudgetFactory.CreateAsync(
            _context, user.Id, name: "Finished", period: BudgetPeriod.Monthly,
            startDate: finishedStart, endDate: finishedEnd);

        var futureStart = new DateOnly(UtcToday.Year, UtcToday.Month, 1).AddMonths(2);
        var future = await BudgetFactory.CreateAsync(
            _context, user.Id, name: "Future", period: BudgetPeriod.Monthly, startDate: futureStart);

        var live = await BudgetFactory.CreateAsync(
            _context, user.Id, name: "Live", period: BudgetPeriod.Daily, startDate: UtcToday.AddDays(-7));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 40,
            transactionDate: new DateOnly(finishedEnd.Year, finishedEnd.Month, 10).ToDateTime(TimeOnly.MinValue));
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 90, transactionDate: DateTime.UtcNow);

        var body = await ListBudgets(user);

        var finishedResource = body.Single(b => b.Id == finished.Id);
        Assert.Equal(new DateOnly(finishedEnd.Year, finishedEnd.Month, 1), finishedResource.CycleStart);
        Assert.Equal(finishedEnd, finishedResource.CycleEnd);
        Assert.Equal(40m, finishedResource.AmountSpent);

        var futureResource = body.Single(b => b.Id == future.Id);
        Assert.Equal(futureStart, futureResource.CycleStart);
        Assert.Equal(futureStart.AddMonths(1).AddDays(-1), futureResource.CycleEnd);
        Assert.Equal(0m, futureResource.AmountSpent);

        var liveResource = body.Single(b => b.Id == live.Id);
        Assert.Equal(UtcToday, liveResource.CycleStart);
        Assert.Equal(UtcToday, liveResource.CycleEnd);
        Assert.Equal(90m, liveResource.AmountSpent);
    }

    [Fact]
    public async Task Get_SameBudgetFromListAndFromShow_ReturnsIdenticalSpendAndCycle()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Monthly, startDate: UtcToday.AddDays(-40));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 210, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 15, transactionDate: DateTime.UtcNow);

        var fromList = (await ListBudgets(user)).Single(b => b.Id == budget.Id);
        var fromShow = await ShowBudget(user, budget.Id);

        Assert.Equal(fromShow.AmountSpent, fromList.AmountSpent);
        Assert.Equal(fromShow.CycleStart, fromList.CycleStart);
        Assert.Equal(fromShow.CycleEnd, fromList.CycleEnd);
    }

    [Fact]
    public async Task Get_AnotherUsersBudgetsAndExpenses_NeverAppearOrCount()
    {
        var user = await UserFactory.CreateAsync(_context);
        var other = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var otherAccount = await AccountFactory.CreateAsync(_context, other.Id);

        var budget = await BudgetFactory.CreateAsync(
            _context, user.Id, period: BudgetPeriod.Daily, startDate: UtcToday.AddDays(-7));
        await BudgetFactory.CreateAsync(
            _context, other.Id, period: BudgetPeriod.Daily, startDate: UtcToday.AddDays(-7));

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, TransactionType.Expense, amount: 60, transactionDate: DateTime.UtcNow);
        await TransactionFactory.CreateAsync(_context, other.Id, otherAccount.Id, TransactionType.Expense, amount: 999, transactionDate: DateTime.UtcNow);

        var mine = Assert.Single(await ListBudgets(user));
        Assert.Equal(budget.Id, mine.Id);
        Assert.Equal(60m, mine.AmountSpent);
    }

    [Fact]
    public async Task Update_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentBudget_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/99999", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_OtherUsersBudget_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(userA);
        
        var budget = await BudgetFactory.CreateAsync(_context, userB.Id);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    
    [Fact]
    public async Task Update_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await BudgetFactory.CreateAsync(_context, user.Id, name: "Test duplicate");
        _client.ActAsUser(user);

        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "Test duplicate",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentCategory_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = 9999
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithOtherUserCategory_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);
        
        var budget = await BudgetFactory.CreateAsync(_context, userA.Id);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = category.Id
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now.AddDays(3)),
            EndDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
            CategoryId = category.Id,
            Description = "Test update budget"
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<BudgetResource>(TestJsonOptions.Default);
        Assert.Equal("Updated Budget", body!.Name);
        Assert.Equal(5000, body!.AmountLimit);
        Assert.Equal("Weekly", body!.Period.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(3)).ToString(), body!.StartDate.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(7)).ToString(), body!.EndDate.ToString());
        Assert.Equal("Test update budget", body!.Description);
        Assert.Equal(category.Id, body!.CategoryId);
    }

    [Fact]
    public async Task Update_CategoryIdToNull_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id, categoryId: category.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now.AddDays(3)),
            EndDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
            Description = "Test update budget"
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<BudgetResource>(TestJsonOptions.Default);
        Assert.Equal("Updated Budget", body!.Name);
        Assert.Equal(5000, body!.AmountLimit);
        Assert.Equal("Weekly", body!.Period.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(3)).ToString(), body!.StartDate.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(7)).ToString(), body!.EndDate.ToString());
        Assert.Equal("Test update budget", body!.Description);
        Assert.Null(body!.CategoryId);
    }

    [Fact]
    public async Task Update_KeepingSameName_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id, name: "Test same name");

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test same name",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var response = await _client.DeleteAsync("api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithInvalidId_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/budgets/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OtherUsersBudget_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, userB.Id);
        
        _client.ActAsUser(userA);
        

        var response = await _client.DeleteAsync("api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        
        var seededCategory = await BudgetFactory.CreateAsync(_context, user.Id);

        var response = await _client.DeleteAsync("api/budgets/" + seededCategory.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidBudgetRequests))]
    public async Task Create_WithInvalidData_ReturnsBadRequest(
        string? name, 
        decimal amountLimit, 
        BudgetPeriod? period
    )
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { 
            Name = name, 
            AmountLimit = amountLimit, 
            Period = period, 
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };


        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithEndDateBeforeStartDate_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { 
            Name = "Test budget", 
            AmountLimit = 5000, 
            Period = BudgetPeriod.Weekly, 
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            EndDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-3)),
        };


        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithSameStartDateAndEndDate_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { 
            Name = "Test budget", 
            AmountLimit = 5000, 
            Period = BudgetPeriod.Weekly, 
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            EndDate = DateOnly.FromDateTime(DateTime.Now),
        };


        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithEndDateBeforeStartDate_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            EndDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-3)),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static IEnumerable<object?[]> InvalidBudgetRequests()
    {
        // Missing name
        yield return new object?[] { null, 100m, BudgetPeriod.Weekly };
        // AmountLimit <= 0
        yield return new object?[] { "Test Budget", -100m, BudgetPeriod.Weekly} ;
        // // missing Period
        yield return new object?[] { "Test Budget", 100m, null };
    }

    public void Dispose() => _scope.Dispose();
}

