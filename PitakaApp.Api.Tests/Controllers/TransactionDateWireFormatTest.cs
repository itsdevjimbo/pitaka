using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

// These tests assert the raw JSON string on the wire, not a deserialised DateTime.
// A person-recorded Transaction is a real UTC instant; a generated transaction is a
// wall-clock day. Once both are round-tripped through MySQL datetime(6) they come back
// Kind=Unspecified and the two become byte-identical unless the serialiser is told which
// frame each row is in. See issue #71.
[Collection("Database collection")]
public class TransactionDateWireFormatTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public TransactionDateWireFormatTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }

    private static string RawTransactionDate(JsonElement transaction) =>
        transaction.GetProperty("transactionDate").GetString()!;

    [Fact]
    public async Task Get_PersonRecordedTransaction_SerialisesTransactionDateWithItsZoneDesignator()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        // Round-trips through TransactionService.CreateAsync, not a directly-written model.
        var createResponse = await _client.PostAsJsonAsync("/api/transactions", new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
            TransactionDate = "2026-08-31T23:30:00+08:00"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var createBody = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var writeWireDate = RawTransactionDate(createBody.RootElement);

        var listResponse = await _client.GetAsync("/api/transactions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listBody = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listWireDate = RawTransactionDate(listBody.RootElement.GetProperty("data")[0]);

        Assert.Equal("2026-08-31T15:30:00Z", listWireDate);
        // The list read must match the write response byte-for-byte on this field.
        Assert.Equal(writeWireDate, listWireDate);
    }

    [Fact]
    public async Task GetForAccount_PersonRecordedTransaction_SerialisesTransactionDateWithItsZoneDesignator()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var createResponse = await _client.PostAsJsonAsync("/api/transactions", new
        {
            AccountId = account.Id,
            Type = TransactionType.Income,
            Amount = 5000,
            TransactionDate = "2026-08-31T23:30:00+08:00"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await _client.GetAsync("/api/accounts/" + account.Id + "/transactions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listBody = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listWireDate = RawTransactionDate(listBody.RootElement[0]);

        Assert.Equal("2026-08-31T15:30:00Z", listWireDate);
    }

    [Fact]
    public async Task Get_GeneratedTransaction_SerialisesTransactionDateWithoutAZoneDesignator()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var schedule = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        // A generated transaction: a wall-clock midnight, with a RecurringTransactionId
        // and no instant behind it.
        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 8, 31),
            recurringTransactionId: schedule.Id);

        _client.ActAsUser(user);

        var listResponse = await _client.GetAsync("/api/transactions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listBody = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listWireDate = RawTransactionDate(listBody.RootElement.GetProperty("data")[0]);

        Assert.Equal("2026-08-31T00:00:00", listWireDate);
        Assert.DoesNotContain("Z", listWireDate);
        Assert.DoesNotContain("+", listWireDate);
    }

    [Fact]
    public async Task GetForAccount_GeneratedTransaction_SerialisesTransactionDateWithoutAZoneDesignator()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var schedule = await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id);

        await TransactionFactory.CreateAsync(
            _context, user.Id, account.Id,
            transactionDate: new DateTime(2026, 8, 31),
            recurringTransactionId: schedule.Id);

        _client.ActAsUser(user);

        var listResponse = await _client.GetAsync("/api/accounts/" + account.Id + "/transactions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listBody = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listWireDate = RawTransactionDate(listBody.RootElement[0]);

        Assert.Equal("2026-08-31T00:00:00", listWireDate);
        Assert.DoesNotContain("Z", listWireDate);
        Assert.DoesNotContain("+", listWireDate);
    }

    public void Dispose() => _scope.Dispose();
}
