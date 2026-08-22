using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class RecurringTransactionsControllerTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public RecurringTransactionsControllerTest(PitakaWebApplicationFactory factory)
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
    public async Task Get_WithLoggedInUser_ReturnsTheirRecurringTransactions()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(_context, userA.Id, initialBalance: 5000);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id, initialBalance: 5000);

        await RecurringTransactionFactory.CreateAsync(_context, userB.Id, accountB.Id);
        
        await RecurringTransactionFactory.CreateAsync(_context, userA.Id, accountA.Id, name: "Test 1");
        await RecurringTransactionFactory.CreateAsync(_context, userA.Id, accountA.Id, name: "Test 2");
        await RecurringTransactionFactory.CreateAsync(_context, userA.Id, accountA.Id, name: "Test 3");
        
        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/recurringtransactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<RecurringTransactionResource>>(TestJsonOptions.Default);
        Assert.Equal(3, body!.Count);
    }

    [Fact]
    public async Task Create_WithNoLoggedInUser_ReturnsUnauthorized()
    {
        var request = new
        {
            AccountId = 3,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNonExistentAccountId_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            AccountId = 99999,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAccountIdBelongsToOtherUser_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);

        var request = new
        {
            AccountId = account.Id,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInactiveAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, isActive: false);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNonExistentCategory_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            CategoryId = 99999,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithCategoryBelongsToOtherUser_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, userB.Id);
        var account = await AccountFactory.CreateAsync(_context, userA.Id);

        _client.ActAsUser(userA);

        var request = new
        {
            AccountId = account.Id,
            CategoryId = category.Id,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicateName_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id, name: "Test 1");

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Name = "Test 1",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
    public static IEnumerable<object?[]> InvalidRecurringTransactionRequests()
    {
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));

        // Missing name
        yield return new object?[] { null, 500m, RecurringTransactionType.Income, Frequency.Daily,  startDate, null };

        // Negavtive amount
        yield return new object?[] { "Test", -100m, RecurringTransactionType.Income, Frequency.Daily,  startDate, null };

        // 0 amount
        yield return new object?[] { "Test", 0, RecurringTransactionType.Income, Frequency.Daily,  startDate, null };

        // missing type
        yield return new object?[] { "Test", 100m, null, Frequency.Daily,  startDate, null };

        // missing daily
        yield return new object?[] { "Test", 100m, RecurringTransactionType.Income, null,  startDate, null };

        // start date today
        yield return new object?[] { "Test", 100m, RecurringTransactionType.Income, Frequency.Daily, DateOnly.FromDateTime(DateTime.UtcNow) , null };

        // start in the past
        yield return new object?[] { "Test", 100m, RecurringTransactionType.Income, Frequency.Daily, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), null};

        // end date before start date
        yield return new object?[] { "Test", 100m, RecurringTransactionType.Income, Frequency.Daily, startDate, DateOnly.FromDateTime(DateTime.UtcNow)};

        // end date same with start date
        yield return new object?[] { "Test", 100m, RecurringTransactionType.Income, Frequency.Daily, startDate, startDate};
    }

    [Theory]
    [MemberData(nameof(InvalidRecurringTransactionRequests))]
    public async Task Create_WithInvalidData_ReturnsBadRequest(
        string? name,
        decimal amount,
        RecurringTransactionType? type,
        Frequency? frequency,
        DateOnly? startDate,
        DateOnly? endDate
    )
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Name = name,
            Amount = amount,
            Type = type,
            Frequency = frequency,
            StartDate = startDate,
            EndDate = endDate
        };


        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            CategoryId = category.Id,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            Description = "Test description",
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var recurringTransaction = await _context.RecurringTransactions.Where(rt => rt.UserId == user.Id).FirstAsync();
        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);

        Assert.Equal(recurringTransaction.Id, body!.Id);
        Assert.Equal(account.Id, body.AccountId);
        Assert.Equal(category.Id, body.CategoryId);
        Assert.Equal("Test recurring transaction", body.Name);
        Assert.Equal(RecurringTransactionType.Income, body.Type);
        Assert.Equal(500, body.Amount);
        Assert.Equal("Test description", body.Description);
        Assert.Equal(Frequency.Daily, body.Frequency);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), body.StartDate);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), body.EndDate);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), body.NextRunDate);
        Assert.Equal(RecurringTransactionStatus.Active, body.Status);
    }

    [Fact]
    public async Task Create_WithoutEndDate_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);  

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Null(body!.EndDate);
    }

    [Fact]
    public async Task Create_WithUserOwnedCategory_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            CategoryId = category.Id,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);  

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal(category.Id, body!.CategoryId);
    }

    [Fact]
    public async Task Create_WithSystemDefaultCategory_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var category = await CategoryFactory.CreateAsync(_context);
        
        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            CategoryId = category.Id,
            Name = "Test recurring transaction",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);  

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal(category.Id, body!.CategoryId);
    }

    [Fact]
    public async Task Create_WithSameNameDifferentUser_ReturnsCreated()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        var accountA = await AccountFactory.CreateAsync(_context, userA.Id);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id);

        await RecurringTransactionFactory.CreateAsync(_context, userB.Id, accountB.Id, name: "Test 1");
        
        _client.ActAsUser(userA);

        var request = new
        {
            AccountId = accountA.Id,
            Name = "Test 1",
            Type = RecurringTransactionType.Income,
            Frequency = Frequency.Daily,
            Amount = 500,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };
        
        var response = await _client.PostAsJsonAsync("/api/recurringtransactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);  

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal("Test 1", body!.Name);
    }

    [Fact]
    public async Task Show_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id, name: "Test 1");

        var response = await _client.GetAsync("/api/recurringtransactions/" + recurringTransaction.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Show_WithNonExistentRecurringTransaction_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/recurringtransactions/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_WithRecurringTransactionBelongsToOtherUser_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        
        _client.ActAsUser(userA);
        
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, userB.Id, account.Id, name: "Test 1");

        var response = await _client.GetAsync("/api/recurringtransactions/" + recurringTransaction.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id, name: "Test 1");
        
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/recurringtransactions/" + recurringTransaction.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);

        Assert.Equal(recurringTransaction.Id, body!.Id);
        Assert.Equal(account.Id, body.AccountId);
        Assert.Null(body.CategoryId);
        Assert.Equal("Test 1", body.Name);
        Assert.Equal(RecurringTransactionType.Income, body.Type);
        Assert.Equal(500, body.Amount);
        Assert.Null(body.Description);
        Assert.Equal(Frequency.Daily, body.Frequency);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), body.StartDate);
        Assert.Null(body.EndDate);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), body.NextRunDate);
        Assert.Equal(RecurringTransactionStatus.Active, body.Status);
    }

    [Fact]
    public async Task Update_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var request = new
        {
            Name = "Test 1",
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentRecurringTransaction_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test 1",
            Amount = 501,
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/999999", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithRecurringTransactionBelongsToOtherUser_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, userB.Id, account.Id);

        _client.ActAsUser(userA);

        var request = new
        {
            Name = "Test 1",
            Amount = 501,
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithDuplicateName_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id, name: "Test 1");
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id, name: "Other test");

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test 1",
            Amount = 501,
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithEndDateSameWithStartDate_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test 1",
            Amount = 501,
            EndDate =  DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentCategory_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test 1",
            Amount = 501,
            CategoryId =  9999
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithCategoryBelongsToOtherUser_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var category = await CategoryFactory.CreateAsync(_context, userB.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test 1",
            Amount = 501,
            CategoryId =  category.Id
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Updated name",
            CategoryId =  category.Id,
            Amount = 5000,
            Description = "Updated description",
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _context.Entry(recurringTransaction).ReloadAsync();
        
        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);

        Assert.Equal(recurringTransaction.Id, body!.Id);
        Assert.Equal(account.Id, body.AccountId);
        Assert.Equal(category.Id, body.CategoryId);
        Assert.Equal("Updated name", body.Name);
        Assert.Equal(recurringTransaction.Type, body.Type);
        Assert.Equal(5000, body.Amount);
        Assert.Equal("Updated description", body.Description);
        Assert.Equal(recurringTransaction.Frequency, body.Frequency);
        Assert.Equal(recurringTransaction.StartDate, body.StartDate);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), body.EndDate);
        Assert.Equal(recurringTransaction.NextRunDate, body.NextRunDate);
        Assert.Equal(recurringTransaction.Status, body.Status);
    }

    [Fact]
    public async Task Update_WithSameName_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id, name: "Test 1");

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test 1",
            Amount = 501,
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNoEndDate_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, name: "Test 1", endDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
        );

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test 1",
            Amount = 501,
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);

        Assert.Null(body!.EndDate);
    }

    [Fact]
    public async Task Update_EnsuresDoesntTouchStatus_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, name: "Test 1", endDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)), status: RecurringTransactionStatus.Cancelled
        );

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test 1",
            Amount = 501,
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))
        };

        var response = await _client.PutAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal(recurringTransaction.Status, body!.Status);
    }

    [Fact]
    public async Task Patch_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Paused" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithNonExistentRecurringTransaction_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);

        _client.ActAsUser(user);
        
        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/99999/status", new { Status = "Paused" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithRecurringTransactionBelongsToOtherUser_ReturnsForbidden()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, userB.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Paused" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithNonExistentStatus_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Test Sttus" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithCompletedStatus_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Completed" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Paused" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        await _context.Entry(recurringTransaction).ReloadAsync();

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal(recurringTransaction.NextRunDate, body!.NextRunDate);
    }

    [Fact]
    public async Task Patch_WithResume_ReturnsOk()
    {
        var startDate = new DateOnly(2026, 08, 20);

        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, status: RecurringTransactionStatus.Paused, startDate: startDate
        );

        
        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Active" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), body!.NextRunDate);
    }

    [Fact]
    public async Task Patch_ResumesInPastEndDate_ReturnsOk()
    {
        var startDate = new DateOnly(2026, 08, 19);
        var endDte = new DateOnly(2026, 08, 21);

        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, status: RecurringTransactionStatus.Paused, 
            startDate: startDate, endDate: endDte, nextRunDate: startDate
        );
        
        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Active" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal(startDate, body!.NextRunDate);
        Assert.Equal(RecurringTransactionStatus.Completed, body!.Status);
    }

    [Fact]
    public async Task Patch_WithActiveToCancelled_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Cancelled" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        await _context.Entry(recurringTransaction).ReloadAsync();

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal(RecurringTransactionStatus.Cancelled, body!.Status);
    }

    [Fact]
    public async Task Patch_WithPausedToCancelled_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id, status: RecurringTransactionStatus.Paused);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Cancelled" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        await _context.Entry(recurringTransaction).ReloadAsync();

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal(RecurringTransactionStatus.Cancelled, body!.Status);
    }

    [Fact]
    public async Task Patch_ResumesToInActiveAccount_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, isActive: false);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id, status: RecurringTransactionStatus.Paused);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/recurringtransactions/" + recurringTransaction.Id + "/status", new { Status = "Active" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        await _context.Entry(recurringTransaction).ReloadAsync();

        var body = await response.Content.ReadFromJsonAsync<RecurringTransactionResource>(TestJsonOptions.Default);
        Assert.Equal(RecurringTransactionStatus.Active, body!.Status);
    }

    [Fact]
    public async Task Delete_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id );

        var response = await _client.DeleteAsync("api/recurringtransactions/" + recurringTransaction.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_BelongsToOtherUser_ReturnsForbidden()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, userB.Id, account.Id );

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/recurringtransactions/" + recurringTransaction.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id );
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, recurringTransactionId: recurringTransaction.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/recurringtransactions/" + recurringTransaction.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.False(await _context.RecurringTransactions.AnyAsync(rt => rt.Id == recurringTransaction.Id));

        await _context.Transactions.Entry(transaction).ReloadAsync();
        Assert.Null(transaction.RecurringTransactionId);

    }

    public void Dispose() => _scope.Dispose();
}