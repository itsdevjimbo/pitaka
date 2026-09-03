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

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Equal(3, body!.Data.Count);
        Assert.Equal(3, body!.TotalCount);
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

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Equal(
            new[] { new DateTime(2026, 9, 15), new DateTime(2026, 4, 2), new DateTime(2026, 2, 20) },
            body!.Data.Select(t => t.TransactionDate.Date));
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

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Equal(new[] { later.Id, backDated.Id, earlier.Id }, body!.Data.Select(t => t.Id));
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

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Equal(new[] { second.Id, first.Id }, body!.Data.Select(t => t.Id));
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
    public async Task Get_WrapsTheListInAPageEnvelope_WithDefaultPageAndSize()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        for (var i = 0; i < 3; i++)
        {
            await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        }

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Equal(1, body!.Page);
        Assert.Equal(50, body!.PageSize);
        Assert.Equal(3, body!.TotalCount);
        Assert.Equal(3, body!.Data.Count);
    }

    [Fact]
    public async Task Get_TotalCountIsTheWholeMatchingSet_EvenWhenItExceedsOnePage()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        for (var i = 0; i < 4; i++)
        {
            await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        }

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?pageSize=2");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(2, body!.Data.Count);
        Assert.Equal(4, body!.TotalCount);
    }

    [Fact]
    public async Task Get_PagingPreservesNewestFirstOrder_WithTheIdTiebreakDecidingAcrossThePageBoundary()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var shared = new DateTime(2026, 4, 1);
        // Seeded oldest-first so insertion order is the opposite of the expected order.
        // midA and midB share a date, so only the Id tiebreak orders them — and with
        // pageSize 2 they straddle the page-1/page-2 boundary.
        var oldest = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 1, 1));
        var older = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 2, 1));
        var midA = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: shared);
        var midB = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: shared);
        var newest = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 5, 1));

        _client.ActAsUser(user);

        var page1 = await (await _client.GetAsync("/api/transactions?page=1&pageSize=2"))
            .Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        var page2 = await (await _client.GetAsync("/api/transactions?page=2&pageSize=2"))
            .Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        var page3 = await (await _client.GetAsync("/api/transactions?page=3&pageSize=2"))
            .Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        // Full order: newest, midB (higher Id), midA, older, oldest.
        Assert.Equal(new[] { newest.Id, midB.Id }, page1!.Data.Select(t => t.Id));
        Assert.Equal(new[] { midA.Id, older.Id }, page2!.Data.Select(t => t.Id));
        Assert.Equal(new[] { oldest.Id }, page3!.Data.Select(t => t.Id));
        Assert.All(new[] { page1, page2, page3 }, p => Assert.Equal(5, p!.TotalCount));
    }

    [Fact]
    public async Task Get_WithNoQueryString_CapsDataAtTheDefaultPageSize_WhileTotalCountReportsTheWholeSet()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        for (var i = 0; i < 51; i++)
        {
            await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        }

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(50, body!.Data.Count);
        Assert.Equal(50, body!.PageSize);
        Assert.Equal(51, body!.TotalCount);
    }

    [Fact]
    public async Task Get_WithAPageNumberLargeEnoughToOverflowTheOffset_ReturnsEmptyData_NotAnError()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?page=2000000000&pageSize=200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Empty(body!.Data);
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_PagePastTheEnd_ReturnsEmptyDataWithARealTotalCount()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?page=5&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Empty(body!.Data);
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByAccountId_IncludesTransfersArrivingByTransferToAccountId_Once()
    {
        var user = await UserFactory.CreateAsync(_context);
        var wanted = await AccountFactory.CreateAsync(_context, user.Id);
        var other = await AccountFactory.CreateAsync(_context, user.Id);

        var onAccount = await TransactionFactory.CreateAsync(_context, user.Id, wanted.Id);
        var transferIn = await TransactionFactory.CreateAsync(
            _context, user.Id, other.Id, type: TransactionType.Transfer, transferToAccountId: wanted.Id);
        // Noise: entirely on the other account.
        await TransactionFactory.CreateAsync(_context, user.Id, other.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?accountId=" + wanted.Id);
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { transferIn.Id, onAccount.Id }.OrderByDescending(x => x),
            body!.Data.Select(t => t.Id).OrderByDescending(x => x));
        Assert.Equal(2, body!.TotalCount);
        Assert.Single(body!.Data, t => t.Id == transferIn.Id);
    }

    [Fact]
    public async Task Get_FilterByCategoryId_ExcludesTransactionsWithoutThatCategory_IncludingTransfers()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var other = await AccountFactory.CreateAsync(_context, user.Id);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);

        var matched = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, categoryId: category.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id, type: TransactionType.Transfer, transferToAccountId: other.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?categoryId=" + category.Id);
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { matched.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByType_ReturnsOnlyThatDirection()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var expense = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, type: TransactionType.Expense);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, type: TransactionType.Income);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?type=Expense");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { expense.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByTypeTransfer_ReturnsOnlyTransfers()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var other = await AccountFactory.CreateAsync(_context, user.Id);

        var transfer = await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id, type: TransactionType.Transfer, transferToAccountId: other.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, type: TransactionType.Income);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, type: TransactionType.Expense);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?type=Transfer");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { transfer.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByFrom_IsInclusiveOfARowExactlyAtThatInstant()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var before = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 4, 30));
        var atBoundary = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 5, 1));
        var after = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 5, 2));

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?from=2026-05-01T00:00:00Z");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { after.Id, atBoundary.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(2, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByTo_IsExclusiveOfARowExactlyAtThatInstant()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var before = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 5, 31));
        var atBoundary = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 6, 1));
        var after = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 6, 2));

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?to=2026-06-01T00:00:00Z");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { before.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByFromAndTo_ReturnsTheHalfOpenInterval()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 4, 15));
        var inMay1 = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 5, 1));
        var inMay2 = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 5, 20));
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, transactionDate: new DateTime(2026, 6, 1));

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?from=2026-05-01T00:00:00Z&to=2026-06-01T00:00:00Z");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { inMay2.Id, inMay1.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(2, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FiltersCombine_AccountIdAndType_AreBothApplied()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var other = await AccountFactory.CreateAsync(_context, user.Id);

        var wanted = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, type: TransactionType.Expense);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, type: TransactionType.Income); // wrong type
        await TransactionFactory.CreateAsync(_context, user.Id, other.Id, type: TransactionType.Expense);  // wrong account

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?accountId=" + account.Id + "&type=Expense");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { wanted.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterAndPagingTogether_TotalCountIsTheFilteredCount_NotTheWholeHistory()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        for (var i = 0; i < 3; i++)
        {
            await TransactionFactory.CreateAsync(_context, user.Id, account.Id, type: TransactionType.Expense);
        }
        for (var i = 0; i < 5; i++)
        {
            await TransactionFactory.CreateAsync(_context, user.Id, account.Id, type: TransactionType.Income);
        }

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?type=Expense&pageSize=2");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(2, body!.Data.Count);
        Assert.Equal(3, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByAnotherUsersAccountId_ReturnsEmptyPage_NotError()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var stranger = await UserFactory.CreateAsync(_context);
        var strangerAccount = await AccountFactory.CreateAsync(_context, stranger.Id);
        await TransactionFactory.CreateAsync(_context, stranger.Id, strangerAccount.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?accountId=" + strangerAccount.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Empty(body!.Data);
        Assert.Equal(0, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByDescription_NarrowsToRowsWhoseDescriptionContainsTheValue_CaseInsensitively()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var upper = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, description: "Weekly COFFEE run");
        var lower = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, description: "office coffee beans");
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, description: "train ticket");

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?description=coffee");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { lower.Id, upper.Id }.OrderByDescending(x => x),
            body!.Data.Select(t => t.Id).OrderByDescending(x => x));
        Assert.Equal(2, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByDescription_NeverMatchesARowWithANullDescription()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var noted = await TransactionFactory.CreateAsync(_context, user.Id, account.Id, description: "lunch with Sam");
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, description: null);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?description=lunch");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { noted.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByDescription_MatchesTheDescriptionOnly_NotACategoryOrAccountName()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, name: "Groceries card");
        var category = await CategoryFactory.CreateAsync(_context, user.Id, name: "Groceries");

        var matched = await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id, categoryId: category.Id, description: "weekly groceries");
        // Same category and account — whose names contain the needle — but a description that does not.
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id, categoryId: category.Id, description: "parking");

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?description=groceries");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { matched.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByDescription_CombinesWithTypeAndPaging_TotalCountIsTheFilteredCount()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        for (var i = 0; i < 3; i++)
        {
            await TransactionFactory.CreateAsync(
                _context, user.Id, account.Id, type: TransactionType.Expense, description: "taxi home");
        }
        // Right description, wrong type.
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id, type: TransactionType.Income, description: "taxi refund");
        // Right type, wrong description.
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id, type: TransactionType.Expense, description: "groceries");

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?description=taxi&type=Expense&pageSize=2");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(2, body!.Data.Count);
        Assert.Equal(3, body!.TotalCount);
        Assert.All(body!.Data, t => Assert.Contains("taxi", t.Description));
    }

    [Theory]
    [InlineData("description=")]
    [InlineData("description=%20%20")]
    public async Task Get_FilterByDescription_EmptyOrWhitespaceOnly_IsTreatedAsAbsent(string query)
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, description: "one");
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, description: null);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Equal(2, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterByDescription_MatchingNoRow_ReturnsEmptyPageWithZeroTotalCount_NotA404()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, description: "coffee");

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?description=nothingmatchesthis");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);
        Assert.Empty(body!.Data);
        Assert.Equal(0, body!.TotalCount);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    [InlineData("from=2026-06-01T00:00:00Z&to=2026-05-01T00:00:00Z")]
    [InlineData("from=2026-05-01T00:00:00Z&to=2026-05-01T00:00:00Z")]
    [InlineData("type=nonsense")]
    // A bound with no zone designator: the server would otherwise read it in its own zone.
    [InlineData("from=2026-09-01T00:00:00")]
    [InlineData("to=2026-09-01T00:00:00")]
    // A bound that is not a timestamp at all.
    [InlineData("from=lunchtime")]
    // Two bounds, two zones.
    [InlineData("from=2026-09-01T00:00:00%2B08:00&to=2026-09-30T00:00:00-05:00")]
    public async Task Get_WithInvalidQueryParameters_ReturnsBadRequest(string query)
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_FilterByFrom_WithoutAZoneDesignator_IsRejected_ForTheMissingDesignator()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?from=2026-09-01T00:00:00");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var messages = problem!.Errors.SelectMany(e => e.Value);
        Assert.Contains(messages, m => m.Contains("zone designator", StringComparison.OrdinalIgnoreCase));
    }

    // The guard is a model binder wired up by one line in Program.cs. Detach it — delete the
    // registration, or turn Insert(0, …) into Add(…) so the default binder wins — and a bare
    // timestamp binds silently to the server's zone. This is the case that must stay a 400,
    // so this test fails loudly if the guard is ever left doing nothing. (Issue 02, AC7.)
    [Theory]
    [InlineData("from=2026-09-01T00:00:00")]
    [InlineData("to=2026-09-01T00:00:00")]
    [InlineData("from=2026-09-01T00:00:00&to=2026-10-01T00:00:00")]
    public async Task Get_FilterBoundWithoutADesignator_IsNeverBoundAsTheServersLocalTime(string query)
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_FilterByFrom_ThatIsNotATimestamp_IsRejected_ForNotBeingATimestamp_NotTheDesignator()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/transactions?from=lunchtime");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var messages = problem!.Errors.SelectMany(e => e.Value).ToList();
        Assert.Contains(messages, m => m.Contains("ISO-8601 timestamp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(messages, m => m.Contains("zone designator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_FilterRange_WithTwoDifferentOffsets_IsRejected()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync(
            "/api/transactions?from=2026-09-01T00:00:00%2B08:00&to=2026-09-30T00:00:00-05:00");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var messages = problem!.Errors.SelectMany(e => e.Value);
        Assert.Contains(messages, m => m.Contains("name the same zone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_FilterRange_WithTwoDifferentOffsetsAndAlsoInverted_ReportsOnlyTheZoneMismatch()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        // Instants invert (05:00Z on 2 May is after 00:00Z on 1 May) and the offsets differ.
        // Only the zone mismatch is worth saying: the range cannot be ordered until it is in
        // one zone. See ADR 0005.
        var response = await _client.GetAsync(
            "/api/transactions?from=2026-05-02T00:00:00%2B08:00&to=2026-05-01T00:00:00-05:00");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var messages = problem!.Errors.SelectMany(e => e.Value).ToList();
        Assert.Contains(messages, m => m.Contains("name the same zone", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(messages, m => m.Contains("strictly earlier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_FilterRange_Inverted_IsRejected_ForTheInversion_NotAMissingDesignator()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync(
            "/api/transactions?from=2026-06-01T00:00:00Z&to=2026-05-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var messages = problem!.Errors.SelectMany(e => e.Value).ToList();
        Assert.Contains(messages, m => m.Contains("strictly earlier", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(messages, m => m.Contains("zone designator", StringComparison.OrdinalIgnoreCase));
    }

    // --- from/to carry their own offset; each frame is filtered in its own terms (issue #72, ADR 0005) ---
    //
    // TransactionDate holds two frames: a recorded Transaction is a real UTC instant, a
    // generated one is a wall-clock midnight with no offset. A DateTimeOffset bound carries
    // both readings, so the recorded frame is compared against the bound's instant and the
    // generated frame against its wall-clock. Every boundary case below runs at a positive
    // and a negative offset: the recorded cases fail against an unshifted compare, the
    // generated cases against one that treats every row as an instant.

    [Fact]
    public async Task Get_FilterRange_RecordedTransactionInLocalEarlyMorning_FallsInThatLocalDay_PositiveOffset()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        // 02:00 on 1 Sep local (UTC+8) — belongs to local September, stored the previous day in UTC.
        var earlyMorning = await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 8, 31, 18, 0, 0, DateTimeKind.Utc));
        // 18:00 on 31 Aug local (UTC+8) — genuinely local August, must not be pulled in.
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc));

        _client.ActAsUser(user);

        var response = await _client.GetAsync(
            "/api/transactions?from=2026-09-01T00:00:00%2B08:00&to=2026-10-01T00:00:00%2B08:00");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { earlyMorning.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterRange_RecordedTransactionInLocalLateEvening_FallsInThatLocalDay_NegativeOffset()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        // 23:00 on 30 Sep local (UTC-5) — belongs to local September, stored the next day in UTC.
        var lateEvening = await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 10, 1, 4, 0, 0, DateTimeKind.Utc));
        // 01:00 on 1 Oct local (UTC-5) — genuinely local October, must not be pulled in.
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc));

        _client.ActAsUser(user);

        var response = await _client.GetAsync(
            "/api/transactions?from=2026-09-01T00:00:00-05:00&to=2026-10-01T00:00:00-05:00");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { lateEvening.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Theory]
    [InlineData("%2B08:00")]
    [InlineData("-05:00")]
    public async Task Get_FilterRange_GeneratedTransaction_FallsOnTheDayItIsDated_RegardlessOfOffset(string offset)
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var schedule = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var datedSep1 = await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 9, 1),
            recurringTransactionId: schedule.Id);
        // Dated the last day of August and the first of October — neither may leak in.
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 8, 31),
            recurringTransactionId: schedule.Id);
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 10, 1),
            recurringTransactionId: schedule.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync(
            $"/api/transactions?from=2026-09-01T00:00:00{offset}&to=2026-10-01T00:00:00{offset}");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(new[] { datedSep1.Id }, body!.Data.Select(t => t.Id));
        Assert.Equal(1, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterRange_RecordedAndGeneratedOnTheSameLocalDay_BothComeBack()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var schedule = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        // Recorded at 02:00 on 1 Sep local (UTC+8); stored 31 Aug 18:00 UTC.
        var recorded = await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 8, 31, 18, 0, 0, DateTimeKind.Utc));
        // Generated, dated 1 Sep, wall-clock midnight with no offset.
        var generated = await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 9, 1),
            recurringTransactionId: schedule.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync(
            "/api/transactions?from=2026-09-01T00:00:00%2B08:00&to=2026-10-01T00:00:00%2B08:00");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Equal(
            new[] { generated.Id, recorded.Id }.OrderByDescending(id => id),
            body!.Data.Select(t => t.Id).OrderByDescending(id => id));
        Assert.Equal(2, body!.TotalCount);
    }

    [Fact]
    public async Task Get_FilterRange_WithUtcBounds_TreatsEveryRowAsAnInstant()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        // 31 Aug 18:00 UTC. With a UTC bound the instant is 31 Aug, so a UTC-September filter excludes it.
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 8, 31, 18, 0, 0, DateTimeKind.Utc));

        _client.ActAsUser(user);

        var response = await _client.GetAsync(
            "/api/transactions?from=2026-09-01T00:00:00Z&to=2026-10-01T00:00:00Z");
        var body = await response.Content.ReadFromJsonAsync<TransactionPageResource>(TestJsonOptions.Default);

        Assert.Empty(body!.Data);
        Assert.Equal(0, body!.TotalCount);
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

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Account does not exist", problem!.Detail);
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

        // Ownership must stay indistinguishable from absence.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Account does not exist", problem!.Detail);
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

        // Ownership of the destination must stay indistinguishable from absence.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Transfer destination is not a valid account", problem!.Detail);
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

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Category does not exist", problem!.Detail);
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

        // Ownership must stay indistinguishable from absence.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Category does not exist", problem!.Detail);
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

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Transfer destination is not a valid account", problem!.Detail);
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

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Account is inactive", problem!.Detail);
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

        // An inactive destination folds into the same reason as absence.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Transfer destination is not a valid account", problem!.Detail);
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

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Category does not exist", problem!.Detail);
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

        // Ownership must stay indistinguishable from absence.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Category does not exist", problem!.Detail);
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

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("One or more tags do not exist", problem!.Detail);
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

        // A tag that isn't the caller's folds into the same reason as absence.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("One or more tags do not exist", problem!.Detail);
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

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("One or more tags do not exist", problem!.Detail);
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

        // A tag that isn't the caller's folds into the same reason as absence.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("One or more tags do not exist", problem!.Detail);
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

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("TransferToAccountId", problem!.Errors.Keys);
        Assert.Contains(
            "A transfer's destination must be a different account from its source.",
            problem!.Errors["TransferToAccountId"]);

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

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("CategoryId", problem!.Errors.Keys);
        Assert.Contains("A transfer cannot be assigned a category.", problem!.Errors["CategoryId"]);
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
    public async Task Create_AccountNotOwned_IsIndistinguishableFromNonExistentAccount()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var otherAccount = await AccountFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);

        var notOwned = await _client.PostAsJsonAsync("/api/transactions", new
        {
            AccountId = otherAccount.Id,
            Type = TransactionType.Income,
            Amount = 5000
        });

        var nonExistent = await _client.PostAsJsonAsync("/api/transactions", new
        {
            AccountId = 99999,
            Type = TransactionType.Income,
            Amount = 5000
        });

        Assert.Equal(HttpStatusCode.BadRequest, notOwned.StatusCode);
        Assert.Equal(nonExistent.StatusCode, notOwned.StatusCode);

        var notOwnedProblem = await notOwned.Content.ReadFromJsonAsync<ProblemDetails>();
        var nonExistentProblem = await nonExistent.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(nonExistentProblem!.Title, notOwnedProblem!.Title);
        Assert.Equal(nonExistentProblem!.Detail, notOwnedProblem!.Detail);
    }

    [Fact]
    public async Task Create_CategoryNotOwned_IsIndistinguishableFromNonExistentCategory()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userA.Id);
        var otherCategory = await CategoryFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);

        var notOwned = await _client.PostAsJsonAsync("/api/transactions", new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
            CategoryId = otherCategory.Id
        });

        var nonExistent = await _client.PostAsJsonAsync("/api/transactions", new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
            CategoryId = 99999
        });

        Assert.Equal(HttpStatusCode.BadRequest, notOwned.StatusCode);
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

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("CategoryId", problem!.Errors.Keys);
        Assert.Contains("A transfer cannot be assigned a category.", problem!.Errors["CategoryId"]);
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