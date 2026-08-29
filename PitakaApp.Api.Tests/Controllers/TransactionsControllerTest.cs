using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class TransactionsControllerTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public TransactionsControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task Get_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/transactions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithLoggedInUser_ReturnsTheirOwnTransactions()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(_context, userA.Id);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id);

        await TransactionFactory.CreateAsync(_context, userB.Id, accountB.Id);
        await TransactionFactory.CreateAsync(_context, userA.Id, accountA.Id);
        await TransactionFactory.CreateAsync(_context, userA.Id, accountA.Id);
        await TransactionFactory.CreateAsync(_context, userA.Id, accountA.Id);
        
        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TransactionResource>>(TestJsonOptions.Default);
        Assert.Equal(3, body!.Count);
    }

    [Fact]
    public async Task Get_ReturnsNewestFirstByTransactionDate()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 4, 2));
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 9, 15));
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 2, 20));

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TransactionResource>>(TestJsonOptions.Default);
        Assert.Equal(
            new[] { new DateTime(2026, 9, 15), new DateTime(2026, 4, 2), new DateTime(2026, 2, 20) },
            body!.Select(t => t.TransactionDate.Date));
    }

    [Fact]
    public async Task Get_BackDatedTransaction_SortsByItsDateNotWhenItWasCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var later = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 6, 3));
        var earlier = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 5, 31));
        // Entered last, but dated between the two above — it must not land at the bottom.
        var backDated = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 6, 1));

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TransactionResource>>(TestJsonOptions.Default);
        Assert.Equal(new[] { later.Id, backDated.Id, earlier.Id }, body!.Select(t => t.Id));
    }

    [Fact]
    public async Task Get_TransactionsSharingADate_OrderedByIdDescending()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var sameDate = new DateTime(2026, 7, 7);
        var first = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: sameDate);
        var second = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: sameDate);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TransactionResource>>(TestJsonOptions.Default);
        Assert.Equal(new[] { second.Id, first.Id }, body!.Select(t => t.Id));
    }

    [Fact]
    public async Task GetForAccount_CarriesTheSameNewestFirstOrderWithIdTiebreak()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var otherAccount = await AccountFactory.CreateAsync(_context, user.Id);

        // Noise on another account — must not appear in the scoped list.
        await TransactionFactory.CreateAsync(_context, user.Id, otherAccount.Id, transactionDate: new DateTime(2026, 12, 1));

        var sharedDate = new DateTime(2026, 8, 8);
        var middle = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 6, 15));
        var newestLowId = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: sharedDate);
        var oldest = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 1, 20));
        var newestHighId = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: sharedDate);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/" + account.Id + "/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TransactionResource>>(TestJsonOptions.Default);
        Assert.Equal(
            new[] { newestHighId.Id, newestLowId.Id, middle.Id, oldest.Id },
            body!.Select(t => t.Id));
    }

    [Fact]
    public async Task Create_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        
        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ToNonExistentAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);

        _client.ActAsUser(user);
        
        var request = new
        {
            AccountId = 99999,
            Type = TransactionType.Income,
            Amount = 5000
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ToAnotherUserAccount_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);
        
        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_TransferToAnotherUserAccount_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(_context, userA.Id, initialBalance: 5000);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id, initialBalance: 3000);

        _client.ActAsUser(userA);
        
        var request = new
        {
            AccountId = accountA.Id,
            Type = TransactionType.Transfer,
            Amount = 1500,
            TransferToAccountId = accountB.Id
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
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
            Type = TransactionType.Income,
            Amount = 5000,
            CategoryId = 99999
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithCategoryNotOwnedByUser_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userA.Id);

        var userB = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);
        
        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
            CategoryId = category.Id
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNonExistentTransferAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);
        
        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Transfer,
            Amount = 5000,
            TransferToAccountId = 99999
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    
    [Fact]
    public async Task Create_WithInActiveAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, isActive: false);

        _client.ActAsUser(user);
        
        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    
    [Fact]
    public async Task Create_TransferWithInActiveAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(_context, user.Id);
        var accountB = await AccountFactory.CreateAsync(_context, user.Id, isActive: false);

        _client.ActAsUser(user);
        
        var request = new
        {
            AccountId = accountA.Id,
            Type = TransactionType.Transfer,
            Amount = 5000,
            TransferToAccountId = accountB.Id
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_TransferTransaction_WithoutTransferAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);
        
        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Transfer,
            Amount = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidRequest_ReturnsCreated_And_IncreaseInAccountBalance()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updateAccount = await _context.Accounts.AsNoTracking().Where(a => a.Id == account.Id).FirstOrDefaultAsync();
        Assert.Equal(5000, updateAccount!.CurrentBalance);
    }

    [Fact]
    public async Task Create_WithValidRequest_ReturnsCreated_And_DecreaseInAccountBalance()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Expense,
            Amount = 2000
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updateAccount = await _context.Accounts.AsNoTracking().Where(a => a.Id == account.Id).FirstOrDefaultAsync();
        Assert.Equal(1000, updateAccount!.CurrentBalance);
    }

    [Fact]
    public async Task Create_TransferTransaction_UpdatesBothAccountBalance()
    {
        var user = await UserFactory.CreateAsync(_context);
        var sourceAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);
        var targetAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = sourceAccount.Id,
            Type = TransactionType.Transfer,
            Amount = 2000,
            TransferToAccountId = targetAccount.Id
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updatedSourceAccount = await _context.Accounts.AsNoTracking().Where(a => a.Id == sourceAccount.Id).FirstOrDefaultAsync();
        Assert.Equal(1000, updatedSourceAccount!.CurrentBalance);

        var updatedTargetAccount = await _context.Accounts.AsNoTracking().Where(a => a.Id == targetAccount.Id).FirstOrDefaultAsync();
        Assert.Equal(7000, updatedTargetAccount!.CurrentBalance);
    }

    [Fact]
    public async Task Show_WithoutLoggedInuser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var response = await _client.GetAsync("/api/transactions/" + transaction.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Show_NonExistentTransaction_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_UserNotOwnedTransaction_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, userB.Id, account.Id);

        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/transactions/" + transaction.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_WithOwnTransaction_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions/" + transaction.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        
        var request = new
        {
            CategoryId = 1
        };
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentTransaction_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        
        var request = new
        {
            CategoryId = 1
        };
        
        var response = await _client.PutAsJsonAsync("/api/transactions/9999", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_AnotherUserTransaction_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, userB.Id, account.Id);
        
        _client.ActAsUser(userA);

        var request = new
        {
            CategoryId = 1
        };
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentCategory_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            CategoryId = 9999
        };
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_CategoryNotOwnedByUser_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userA.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, userA.Id, account.Id);
        
        var userB = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);

        var request = new
        {
            CategoryId = category.Id,
            Amount = 5000
        };
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithValidRequest_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            CategoryId = category.Id,
            Amount = 5000
        };
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);
        Assert.Equal(category.Id, body!.CategoryId);

        var updateAccount = await _context.Accounts.AsNoTracking().Where(a => a.Id == account.Id).FirstOrDefaultAsync();
        Assert.Equal(0, updateAccount!.CurrentBalance);
    }

    [Fact]
    public async Task Delete_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var response = await _client.DeleteAsync("/api/transactions/" + transaction.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithNonExistentTransaction_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/transactions/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OtherUserTransaction_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, userB.Id, account.Id);

        _client.ActAsUser(userA);

        var response = await _client.DeleteAsync("/api/transactions/" + transaction.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_TransferTransaction_AfterDeletedTargetAccount_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);
        var targetAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 2000);
        
        _client.ActAsUser(user);

        var transaction = await TransactionFactory.CreateAsync(
            _context, 
            userId: user.Id, 
            accountId: account.Id, 
            type: TransactionType.Transfer,
            transferToAccountId: targetAccount.Id
        );

        await _client.DeleteAsync("/api/accounts/" + targetAccount!.Id);

        var deleteResponse = await _client.DeleteAsync("/api/transactions/" + transaction!.Id);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_IncomeTransaction_ReturnsNoContent_AndReversedAccountBalance()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 3000
        };

        var postResponse = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var body = await postResponse.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);

        var response = await _client.DeleteAsync("/api/transactions/" + body!.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var exists = await _context.Transactions.AsNoTracking().AnyAsync(a => a.Id == body!.Id);
        Assert.False(exists);

        var updateAccount = await _context.Accounts.AsNoTracking().Where(a => a.Id == account.Id).FirstOrDefaultAsync();
        Assert.Equal(1000, updateAccount!.CurrentBalance);
    }

    [Fact]
    public async Task Delete_ExpenseTransaction_ReturnsNoContent_AndReversedAccountBalance()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Expense,
            Amount = 3000
        };

        var postResponse = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var body = await postResponse.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);

        var response = await _client.DeleteAsync("/api/transactions/" + body!.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var exists = await _context.Transactions.AsNoTracking().AnyAsync(a => a.Id == body!.Id);
        Assert.False(exists);

        var updateAccount = await _context.Accounts.AsNoTracking().Where(a => a.Id == account.Id).FirstOrDefaultAsync();
        Assert.Equal(5000, updateAccount!.CurrentBalance);
    }

    [Fact]
    public async Task Delete_TransferTransaction_ReturnsNoContent_AndReversedAccountBalance()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);
        var targetAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 2000);
        
        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Transfer,
            Amount = 1000,
            TransferToAccountId = targetAccount.Id
        };

        var postResponse = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var body = await postResponse.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);

        var response = await _client.DeleteAsync("/api/transactions/" + body!.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var exists = await _context.Transactions.AsNoTracking().AnyAsync(a => a.Id == body!.Id);
        Assert.False(exists);

        var updatedAccount = await _context.Accounts.AsNoTracking().Where(a => a.Id == account.Id).FirstOrDefaultAsync();
        Assert.Equal(3000, updatedAccount!.CurrentBalance);
        
        var updatedTargetAccount = await _context.Accounts.AsNoTracking().Where(a => a.Id == targetAccount.Id).FirstOrDefaultAsync();
        Assert.Equal(2000, updatedTargetAccount!.CurrentBalance);
    }

    [Fact]
    public async Task Delete_EnsuresContributionsAreDeleted()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, transactionId: transaction.Id);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, transactionId: transaction.Id);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, transactionId: transaction.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/transactions/" + transaction.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await _context.GoalContributions.AsNoTracking().Where(gc => gc.TransactionId == transaction.Id).ToListAsync());
        Assert.Null(await _context.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transaction.Id));
    }

    [Fact]
    public async Task Create_NormalTransactionWithTransferToAccountId_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var sourceAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);
        var targetAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 2000);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = sourceAccount.Id,
            Type = TransactionType.Income,
            Amount = 2000,
            TransferToAccountId = targetAccount.Id
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNonExistentTags_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
            TagIds = new int[] { 9999, 9998}
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithTagsThatDoesntBelongToUser_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var tagA = await TagFactory.CreateAsync(_context, user.Id);
        var tagB = await TagFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
            TagIds = new int[] { tagA.Id, tagB.Id }
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithTags_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var tag = await TagFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
            TagIds = new int[] { tag.Id, tag.Id }
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);
        Assert.NotEmpty(body!.Tags);
        Assert.Single(body!.Tags);
        Assert.Contains(body!.Tags, tagResource => tagResource.Id == tag.Id);
    }

    [Fact]
    public async Task Create_WithoutTags_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);
        Assert.Empty(body!.Tags);
    }

    [Fact]
    public async Task Update_WithNonExistentTags_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        
        _client.ActAsUser(user);
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, new { TagIds = new int[] { 9999, 9998 } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithTagsThatDoesntBelongToUser_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        var tagA = await TagFactory.CreateAsync(_context, user.Id);
        var tagB = await TagFactory.CreateAsync(_context, userB.Id);
        
        _client.ActAsUser(user);
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, new { TagIds = new int[] { tagA.Id, tagB.Id }});
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithTags_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var tagA = await TagFactory.CreateAsync(_context, user.Id, "Test 1");
        var tagB = await TagFactory.CreateAsync(_context, user.Id, "Test 2");

        await _context.Entry(transaction).Collection(t => t.Tags).LoadAsync();

        transaction.Tags.Add(tagA);
        transaction.Tags.Add(tagB);
        await _context.SaveChangesAsync();

        _client.ActAsUser(user);
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, new { TagIds = new int[] { tagA.Id, tagA.Id }});
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);
        Assert.NotEmpty(body!.Tags);
        Assert.Single(body!.Tags);
        Assert.DoesNotContain(body!.Tags, tagResource => tagResource.Id == tagB.Id);
    }

    [Fact]
    public async Task Update_WithEmptyTags_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var tagA = await TagFactory.CreateAsync(_context, user.Id, "Test 1");
        var tagB = await TagFactory.CreateAsync(_context, user.Id, "Test 2");

        await _context.Entry(transaction).Collection(t => t.Tags).LoadAsync();

        transaction.Tags.Add(tagA);
        transaction.Tags.Add(tagB);

        await _context.SaveChangesAsync();

        _client.ActAsUser(user);
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, new { TagIds = new int[] { }});
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);
        Assert.Empty(body!.Tags);
        Assert.DoesNotContain(body!.Tags, tagResource => tagResource.Id == tagA.Id);
        Assert.DoesNotContain(body!.Tags, tagResource => tagResource.Id == tagB.Id);
    }

    [Fact]
    public async Task Update_WithNullTags_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var tag = await TagFactory.CreateAsync(_context, user.Id, "Test 1");

        await _context.Entry(transaction).Collection(t => t.Tags).LoadAsync();

        transaction.Tags.Add(tag);

        await _context.SaveChangesAsync();

        _client.ActAsUser(user);
        
        var response = await _client.PutAsJsonAsync("/api/transactions/" + transaction.Id, new { Amount = 400});
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);
        Assert.Single(body!.Tags);
        Assert.Contains(body!.Tags, tagResource => tagResource.Id == tag.Id);
    }

    [Fact]
    public async Task Create_TransferToSourceAccount_ReturnsBadRequestWithReason_AndNothingMoves()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Transfer,
            Amount = 1500,
            TransferToAccountId = account.Id
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("A transfer's destination must be a different account from its source.", problem!.Detail);

        var unchanged = await _context.Accounts.AsNoTracking().FirstAsync(a => a.Id == account.Id);
        Assert.Equal(5000, unchanged.CurrentBalance);
        Assert.False(await _context.Transactions.AsNoTracking().AnyAsync(t => t.AccountId == account.Id));
    }

    [Fact]
    public async Task Create_TransferToDifferentOwnedActiveAccount_Succeeds_AndMovesBothBalances()
    {
        var user = await UserFactory.CreateAsync(_context);
        var source = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);
        var destination = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = source.Id,
            Type = TransactionType.Transfer,
            Amount = 1200,
            TransferToAccountId = destination.Id
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updatedSource = await _context.Accounts.AsNoTracking().FirstAsync(a => a.Id == source.Id);
        var updatedDestination = await _context.Accounts.AsNoTracking().FirstAsync(a => a.Id == destination.Id);
        Assert.Equal(1800, updatedSource.CurrentBalance);
        Assert.Equal(2200, updatedDestination.CurrentBalance);
    }

    [Fact]
    public async Task Create_TransferCarryingCategory_ReturnsBadRequestWithReasonNamingCategory()
    {
        var user = await UserFactory.CreateAsync(_context);
        var source = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);
        var destination = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = source.Id,
            Type = TransactionType.Transfer,
            Amount = 1000,
            TransferToAccountId = destination.Id,
            CategoryId = category.Id
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("A transfer cannot be assigned a category.", problem!.Detail);
        Assert.False(await _context.Transactions.AsNoTracking().AnyAsync(t => t.AccountId == source.Id));
    }

    [Fact]
    public async Task Create_IncomeWithOwnedCategory_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
            CategoryId = category.Id
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);
        Assert.Equal(category.Id, body!.CategoryId);
    }

    [Fact]
    public async Task Create_TransferDestinationNotOwned_IsIndistinguishableFromNonExistentDestination()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(_context, userA.Id, initialBalance: 5000);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id, initialBalance: 3000);

        _client.ActAsUser(userA);

        var notOwned = await _client.PostAsJsonAsync("/api/transactions", new
        {
            AccountId = accountA.Id,
            Type = TransactionType.Transfer,
            Amount = 1500,
            TransferToAccountId = accountB.Id
        });

        var nonExistent = await _client.PostAsJsonAsync("/api/transactions", new
        {
            AccountId = accountA.Id,
            Type = TransactionType.Transfer,
            Amount = 1500,
            TransferToAccountId = 99999
        });

        Assert.Equal(nonExistent.StatusCode, notOwned.StatusCode);

        var notOwnedProblem = await notOwned.Content.ReadFromJsonAsync<ProblemDetails>();
        var nonExistentProblem = await nonExistent.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(nonExistentProblem!.Title, notOwnedProblem!.Title);
        Assert.Equal(nonExistentProblem!.Detail, notOwnedProblem!.Detail);
    }

    [Fact]
    public async Task Update_AttachingCategoryToTransfer_ReturnsBadRequestWithReasonNamingCategory()
    {
        var user = await UserFactory.CreateAsync(_context);
        var source = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);
        var destination = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var transfer = await TransactionFactory.CreateAsync(
            _context, user.Id, source.Id,
            type: TransactionType.Transfer,
            transferToAccountId: destination.Id);

        _client.ActAsUser(user);

        var response = await _client.PutAsJsonAsync(
            "/api/transactions/" + transfer.Id, new { CategoryId = category.Id });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("A transfer cannot be assigned a category.", problem!.Detail);
    }

    [Fact]
    public async Task Update_TransferDateAndDescription_WithoutCategory_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var source = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);
        var destination = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);
        var transfer = await TransactionFactory.CreateAsync(
            _context, user.Id, source.Id,
            type: TransactionType.Transfer,
            transferToAccountId: destination.Id);

        _client.ActAsUser(user);

        var response = await _client.PutAsJsonAsync(
            "/api/transactions/" + transfer.Id,
            new { Description = "Moved to savings", TransactionDate = "2026-05-01T00:00:00Z" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionResource>(TestJsonOptions.Default);
        Assert.Equal("Moved to savings", body!.Description);
        Assert.Equal(new DateTime(2026, 5, 1), body!.TransactionDate.Date);
    }

    public static IEnumerable<object?[]> InvalidAmounts()
    {
        yield return new object?[] { 0m };
        yield return new object?[] { -100m };
    }

    [Theory]
    [MemberData(nameof(InvalidAmounts))]
    public async Task Create_WithInvalidAmount_ReturnsBadRequest(decimal amount)
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = amount,
        };

        var response = await _client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose() => _scope.Dispose();
}