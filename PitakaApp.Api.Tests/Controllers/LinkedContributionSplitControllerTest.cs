using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public sealed class LinkedContributionSplitControllerTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public LinkedContributionSplitControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_CreatesOrderedLinkedContributionsAndReturnsTheSuccessfulSnapshot()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            name: "Savings",
            initialBalance: 1000
        );
        var transaction = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id,
            amount: 500
        );
        var firstGoal = await GoalFactory.CreateAsync(_context, user.Id, name: "Emergency fund");
        var secondGoal = await GoalFactory.CreateAsync(_context, user.Id, name: "New laptop");
        _client.ActAsUser(user);
        using var request = CreateSplitRequest(
            transaction.Id,
            new
            {
                contributionDate = "2026-09-19",
                contributions = new object[]
                {
                    new
                    {
                        goalId = firstGoal.Id,
                        amount = 125.50m,
                        note = "  first  ",
                        acknowledgeTargetOverrun = false,
                    },
                    new { goalId = secondGoal.Id, amount = 74.50m },
                },
            }
        );

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SplitSuccessSnapshot>(
            TestJsonOptions.Default
        );
        Assert.NotNull(body);
        Assert.Equal(transaction.Id, body.TransactionId);
        Assert.Equal(500, body.TransactionAmount);
        Assert.Equal(200, body.LinkedTotal);
        Assert.Equal(300, body.RemainingCapacity);
        Assert.Equal(account.Id, body.Account.Id);
        Assert.Equal("Savings", body.Account.Name);
        Assert.Equal(1000, body.Account.CurrentBalance);
        Assert.Equal(200, body.Account.EarmarkedTotal);
        Assert.Equal(800, body.Account.AvailableHeadroom);
        Assert.True(body.Account.Active);
        Assert.Equal([firstGoal.Id, secondGoal.Id], body.Contributions.Select(row => row.GoalId));
        Assert.Equal([125.50m, 74.50m], body.Contributions.Select(row => row.Amount));
        Assert.All(
            body.Contributions,
            row => Assert.Equal(new DateOnly(2026, 9, 19), row.ContributionDate)
        );
        Assert.Equal("  first  ", body.Contributions[0].Note);
        Assert.Null(body.Contributions[1].Note);

        _context.ChangeTracker.Clear();
        var persisted = await _context
            .GoalContributions.Where(row => row.TransactionId == transaction.Id)
            .OrderBy(row => row.Id)
            .ToListAsync();
        Assert.Equal(body.Contributions.Select(row => row.Id), persisted.Select(row => row.Id));
    }

    [Fact]
    public async Task Post_EmptyRows_ReturnsValidationProblemDetails()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        using var request = CreateSplitRequest(
            1,
            new { contributionDate = "2026-09-19", contributions = Array.Empty<object>() }
        );

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(
            TestJsonOptions.Default
        );
        Assert.NotNull(body);
        Assert.Equal(["At least one Contribution is required."], body.Errors["contributions"]);
    }

    [Fact]
    public async Task Post_StateFailures_ReturnsOneAtomicConflictWithEverySafeFailure()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            name: "Retired account",
            initialBalance: 50,
            isActive: false
        );
        var transaction = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id,
            type: TransactionType.Expense,
            amount: 25
        );
        var goal = await GoalFactory.CreateAsync(
            _context,
            user.Id,
            name: "Finished goal",
            targetAmount: 10,
            status: GoalStatus.Completed
        );
        _client.ActAsUser(user);
        using var request = CreateSplitRequest(
            transaction.Id,
            new
            {
                contributionDate = "2026-09-19",
                contributions = new[] { new { goalId = goal.Id, amount = 30m } },
            }
        );

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("split_rejected", root.GetProperty("reason").GetString());
        Assert.False(root.GetProperty("created").GetBoolean());
        var failures = root.GetProperty("failures").EnumerateArray().ToArray();
        Assert.Equal(
            [
                "account_inactive",
                "transaction_ineligible",
                "transaction_capacity_exceeded",
                "goal_inactive",
                "target_overrun_acknowledgement_required",
            ],
            failures.Select(failure => failure.GetProperty("reason").GetString())
        );
        Assert.Equal(account.Id, failures[0].GetProperty("accountId").GetInt32());
        Assert.Equal("Retired account", failures[0].GetProperty("accountName").GetString());
        Assert.Equal(0, failures[3].GetProperty("rowIndex").GetInt32());
        Assert.Equal(goal.Id, failures[3].GetProperty("goalId").GetInt32());
        Assert.Equal("Finished goal", failures[3].GetProperty("goalName").GetString());
        Assert.Equal(30, failures[4].GetProperty("proposedProgress").GetDecimal());

        _context.ChangeTracker.Clear();
        Assert.False(
            await _context.GoalContributions.AnyAsync(row => row.TransactionId == transaction.Id)
        );
    }

    [Fact]
    public async Task Post_ChangedPayloadForUsedKey_ReturnsIdempotencyMismatchWithoutCreationClaim()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);
        var transaction = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id,
            amount: 500
        );
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var key = Guid.NewGuid();
        _client.ActAsUser(user);

        async Task<HttpResponseMessage> SendAsync(decimal amount)
        {
            var request = CreateSplitRequest(
                transaction.Id,
                new
                {
                    contributionDate = "2026-09-19",
                    contributions = new[] { new { goalId = goal.Id, amount } },
                },
                key.ToString("D")
            );
            return await _client.SendAsync(request);
        }

        using var first = await SendAsync(100);
        using var mismatch = await SendAsync(101);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        using var body = JsonDocument.Parse(await mismatch.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("idempotency_mismatch", root.GetProperty("reason").GetString());
        Assert.Equal(key, root.GetProperty("key").GetGuid());
        Assert.False(root.TryGetProperty("created", out _));
        _context.ChangeTracker.Clear();
        Assert.Single(
            await _context
                .GoalContributions.Where(row => row.TransactionId == transaction.Id)
                .ToListAsync()
        );
    }

    [Fact]
    public async Task Post_WithoutAuthenticatedUser_ReturnsUnauthorized()
    {
        using var request = CreateSplitRequest(
            1,
            new
            {
                contributionDate = "2026-09-19",
                contributions = new[] { new { goalId = 1, amount = 1m } },
            }
        );

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-uuid")]
    public async Task Post_MissingOrInvalidIdempotencyKey_ReturnsValidationProblemDetails(
        string? key
    )
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        using var request = CreateSplitRequest(
            1,
            new
            {
                contributionDate = "2026-09-19",
                contributions = new[] { new { goalId = 1, amount = 1m } },
            },
            key,
            includeIdempotencyKey: key is not null
        );

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("{\"contributions\":[{\"goalId\":1,\"amount\":1}]}")]
    [InlineData("{\"contributionDate\":null,\"contributions\":[{\"goalId\":1,\"amount\":1}]}")]
    [InlineData(
        "{\"contributionDate\":\"not-a-date\",\"contributions\":[{\"goalId\":1,\"amount\":1}]}"
    )]
    [InlineData("{\"contributionDate\":\"2026-09-19\"}")]
    [InlineData("{\"contributionDate\":\"2026-09-19\",\"contributions\":null}")]
    [InlineData("{\"contributionDate\":\"2026-09-19\",\"contributions\":[null]}")]
    public async Task Post_MissingNullOrInvalidRequiredBodyFields_ReturnsValidationProblemDetails(
        string json
    )
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        using var request = CreateSplitRequest(
            1,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        );

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_InvalidAmountsAndDuplicateGoals_ReturnsIndexedValidationErrors()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        using var request = CreateSplitRequest(
            1,
            new
            {
                contributionDate = "2026-09-19",
                contributions = new[]
                {
                    new { goalId = 12, amount = 0m },
                    new { goalId = 12, amount = 1.001m },
                    new { goalId = 13, amount = 1000000000000m },
                },
            }
        );

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(
            TestJsonOptions.Default
        );
        Assert.NotNull(body);
        Assert.Equal(
            [
                "contributions[0].amount",
                "contributions[0].goalId",
                "contributions[1].amount",
                "contributions[1].goalId",
                "contributions[2].amount",
            ],
            body.Errors.Keys.Order()
        );
    }

    [Fact]
    public async Task Post_UnavailableGoal_ReturnsOnlyAnIndexedValidationError()
    {
        var user = await UserFactory.CreateAsync(_context);
        var otherUser = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);
        var transaction = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id,
            amount: 500
        );
        var foreignGoal = await GoalFactory.CreateAsync(
            _context,
            otherUser.Id,
            name: "Private goal"
        );
        _client.ActAsUser(user);
        using var request = CreateSplitRequest(
            transaction.Id,
            new
            {
                contributionDate = "2026-09-19",
                contributions = new[] { new { goalId = foreignGoal.Id, amount = 10m } },
            }
        );

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(
            TestJsonOptions.Default
        );
        Assert.NotNull(body);
        Assert.Equal(["Goal is unavailable."], body.Errors["contributions[0].goalId"]);
        Assert.DoesNotContain("Private goal", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_MissingOrForeignTransaction_ReturnsTheSameFactLimitedConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var otherUser = await UserFactory.CreateAsync(_context);
        var otherAccount = await AccountFactory.CreateAsync(
            _context,
            otherUser.Id,
            name: "Private account",
            initialBalance: 1000
        );
        var otherTransaction = await TransactionFactory.CreateAsync(
            _context,
            otherUser.Id,
            otherAccount.Id,
            amount: 500
        );
        _client.ActAsUser(user);

        async Task<(HttpStatusCode Status, JsonDocument Body, string Text)> SendAsync(
            int transactionId
        )
        {
            using var request = CreateSplitRequest(
                transactionId,
                new
                {
                    contributionDate = "2026-09-19",
                    contributions = new[] { new { goalId = 1, amount = 1m } },
                }
            );
            using var response = await _client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, JsonDocument.Parse(text), text);
        }

        var missing = await SendAsync(999999);
        var foreign = await SendAsync(otherTransaction.Id);

        foreach (var result in new[] { missing, foreign })
        {
            using (result.Body)
            {
                Assert.Equal(HttpStatusCode.Conflict, result.Status);
                var root = result.Body.RootElement;
                Assert.Equal("transaction_missing", root.GetProperty("reason").GetString());
                Assert.False(root.GetProperty("created").GetBoolean());
                var failure = Assert.Single(
                    root.GetProperty("failures").EnumerateArray().ToArray()
                );
                Assert.Equal("transaction_missing", failure.GetProperty("reason").GetString());
                Assert.True(failure.TryGetProperty("transactionId", out _));
                Assert.Equal(2, failure.EnumerateObject().Count());
                Assert.DoesNotContain("Private account", result.Text);
            }
        }
    }

    [Fact]
    public async Task Post_SemanticReplayAfterDeletion_ReturnsTheExactSavedSuccessWithoutRecreation()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);
        var transaction = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id,
            amount: 500
        );
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 50);
        var key = Guid.NewGuid();
        _client.ActAsUser(user);
        using var firstRequest = CreateSplitRequest(
            transaction.Id,
            new
            {
                contributionDate = "2026-09-19",
                contributions = new[]
                {
                    new
                    {
                        goalId = goal.Id,
                        amount = 100.0m,
                        acknowledgeTargetOverrun = true,
                    },
                },
            },
            key.ToString("D")
        );

        using var firstResponse = await _client.SendAsync(firstRequest);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        using var firstSnapshot = JsonDocument.Parse(firstBody);
        var contributionId = firstSnapshot
            .RootElement.GetProperty("contributions")[0]
            .GetProperty("id")
            .GetInt32();
        using var deletion = await _client.DeleteAsync($"/api/goal-contributions/{contributionId}");
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        using var replayRequest = CreateSplitRequest(
            transaction.Id,
            new
            {
                contributions = new[]
                {
                    new
                    {
                        note = (string?)null,
                        acknowledgeTargetOverrun = true,
                        amount = 100.00m,
                        goalId = goal.Id,
                    },
                },
                contributionDate = "2026-09-19",
            },
            key.ToString("D").ToUpperInvariant()
        );

        using var replayResponse = await _client.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal(firstBody, await replayResponse.Content.ReadAsStringAsync());
        _context.ChangeTracker.Clear();
        Assert.False(
            await _context.GoalContributions.AnyAsync(row => row.TransactionId == transaction.Id)
        );
    }

    [Fact]
    public async Task Post_WhenDuplicateOutcomeCannotBeEstablished_ReturnsUncertainWithoutCreationClaim()
    {
        var user = await UserFactory.CreateAsync(_context);
        var key = Guid.NewGuid();
        _client.ActAsUser(user);
        await using var unresolvedOperation = await _context.Database.BeginTransactionAsync();
        _context.LinkedContributionOperations.Add(
            new LinkedContributionOperation
            {
                UserId = user.Id,
                Key = key.ToString("D"),
                Fingerprint = new string('A', 64),
            }
        );
        await _context.SaveChangesAsync();
        using var request = CreateSplitRequest(
            1,
            new
            {
                contributionDate = "2026-09-19",
                contributions = new[] { new { goalId = 1, amount = 1m } },
            },
            key.ToString("D")
        );

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("operation_outcome_unknown", root.GetProperty("reason").GetString());
        Assert.False(root.TryGetProperty("created", out _));
    }

    [Fact]
    public async Task OpenApi_DescribesThePublicSplitRequestAndResponseContract()
    {
        using var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document
            .RootElement.GetProperty("paths")
            .GetProperty("/api/transactions/{transactionId}/linked-contributions")
            .GetProperty("post");
        var transactionId = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "transactionId");
        Assert.Equal("path", transactionId.GetProperty("in").GetString());
        Assert.True(transactionId.GetProperty("required").GetBoolean());
        var idempotencyKey = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", idempotencyKey.GetProperty("in").GetString());
        Assert.True(idempotencyKey.GetProperty("required").GetBoolean());
        Assert.Equal(
            "uuid",
            idempotencyKey.GetProperty("schema").GetProperty("format").GetString()
        );
        Assert.True(operation.GetProperty("requestBody").GetProperty("required").GetBoolean());
        var responses = operation.GetProperty("responses");
        Assert.Equal(
            "#/components/schemas/SplitSuccessSnapshot",
            responses
                .GetProperty("201")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()
        );
        Assert.Equal(
            "#/components/schemas/ValidationProblemDetails",
            responses
                .GetProperty("400")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()
        );
        Assert.Equal(
            "#/components/schemas/LinkedContributionSplitConflictProblemDetails",
            responses
                .GetProperty("409")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()
        );
        Assert.Equal(
            "#/components/schemas/LinkedContributionSplitOutcomeUnknownProblemDetails",
            responses
                .GetProperty("503")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()
        );

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("CreateLinkedContributionSplitRequest");
        Assert.Equal(
            ["contributionDate", "contributions"],
            request.GetProperty("required").EnumerateArray().Select(item => item.GetString())
        );
        var contributions = request.GetProperty("properties").GetProperty("contributions");
        Assert.Equal(1, contributions.GetProperty("minItems").GetInt32());
        Assert.Contains(
            "Each Goal ID may appear only once",
            contributions.GetProperty("description").GetString()
        );
        var amount = schemas
            .GetProperty("CreateLinkedContributionSplitRowRequest")
            .GetProperty("properties")
            .GetProperty("amount");
        Assert.Equal(0.01m, amount.GetProperty("minimum").GetDecimal());
        Assert.Equal(999999999999.99m, amount.GetProperty("maximum").GetDecimal());
        Assert.Equal(0.01m, amount.GetProperty("multipleOf").GetDecimal());

        var conflict = schemas.GetProperty("LinkedContributionSplitConflictProblemDetails");
        Assert.Contains(
            "reason",
            conflict.GetProperty("required").EnumerateArray().Select(item => item.GetString())
        );
        Assert.Equal(
            ["created", "failures", "key", "reason"],
            conflict
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name is "created" or "failures" or "key" or "reason")
                .Order()
        );
        var failure = schemas
            .GetProperty("LinkedContributionSplitFailureResource")
            .GetProperty("properties");
        Assert.Equal(
            "#/components/schemas/GoalStatus",
            failure
                .GetProperty("currentState")
                .GetProperty("oneOf")[1]
                .GetProperty("$ref")
                .GetString()
        );
        Assert.Equal(
            "#/components/schemas/TransactionType",
            failure.GetProperty("direction").GetProperty("oneOf")[1].GetProperty("$ref").GetString()
        );
        Assert.Equal(
            ["Active", "Completed", "Abandoned"],
            schemas
                .GetProperty("GoalStatus")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString())
        );
        Assert.Equal(
            ["Income", "Expense", "Transfer"],
            schemas
                .GetProperty("TransactionType")
                .GetProperty("enum")
                .EnumerateArray()
                .Where(value => value.ValueKind != JsonValueKind.Null)
                .Select(value => value.GetString())
        );
        var unknown = schemas.GetProperty("LinkedContributionSplitOutcomeUnknownProblemDetails");
        Assert.True(unknown.GetProperty("properties").TryGetProperty("reason", out _));
        Assert.False(unknown.GetProperty("properties").TryGetProperty("created", out _));
    }

    private static HttpRequestMessage CreateSplitRequest(
        int transactionId,
        object body,
        string? idempotencyKey = null,
        bool includeIdempotencyKey = true
    )
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/transactions/{transactionId}/linked-contributions"
        );
        if (includeIdempotencyKey)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("D"));
        }
        request.Content =
            body as HttpContent ?? JsonContent.Create(body, inputType: body.GetType());
        return request;
    }

    public void Dispose() => _scope.Dispose();
}
