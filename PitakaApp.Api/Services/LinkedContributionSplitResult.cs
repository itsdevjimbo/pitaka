using System.Text.Json;

namespace PitakaApp.Api.Services;

public sealed record LinkedContributionSplitInput(
    DateOnly ContributionDate,
    IReadOnlyList<LinkedContributionSplitRow> Contributions
);

public sealed record LinkedContributionSplitRow(
    int GoalId,
    decimal Amount,
    string? Note = null,
    bool AcknowledgeTargetOverrun = false
);

public abstract record LinkedContributionSplitResult;

public sealed record SplitSucceeded(string ResponseBody, int StatusCode = 201)
    : LinkedContributionSplitResult
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The HTTP adapter writes ResponseBody unchanged; Response is a typed view of that durable body.
    public SplitSuccessSnapshot Response =>
        JsonSerializer.Deserialize<SplitSuccessSnapshot>(ResponseBody, JsonOptions)!;

    internal static SplitSucceeded FromSnapshot(SplitSuccessSnapshot response) =>
        new(JsonSerializer.Serialize(response, JsonOptions));
}

public sealed record SplitSuccessSnapshot(
    int TransactionId,
    decimal TransactionAmount,
    decimal LinkedTotal,
    decimal RemainingCapacity,
    SplitAccountSnapshot Account,
    IReadOnlyList<SplitContributionSnapshot> Contributions
);

public sealed record SplitAccountSnapshot(
    int Id,
    string Name,
    decimal CurrentBalance,
    decimal EarmarkedTotal,
    decimal AvailableHeadroom,
    bool Active
);

public sealed record SplitContributionSnapshot(
    int Id,
    int GoalId,
    int AccountId,
    int TransactionId,
    decimal Amount,
    DateOnly ContributionDate,
    string? Note
);

public sealed record SplitFailure(
    string Reason,
    IReadOnlyDictionary<string, object?> Facts,
    int? RowIndex = null,
    int? GoalId = null
);

public sealed record SplitRefused(IReadOnlyList<SplitFailure> Failures)
    : LinkedContributionSplitResult
{
    public int StatusCode => 409;
    public bool Created => false;
    public string Reason => Failures.Count == 1 ? Failures[0].Reason : "split_rejected";
}

public sealed record SplitInvalid(IReadOnlyDictionary<string, string[]> Errors)
    : LinkedContributionSplitResult
{
    public int StatusCode => 400;
}

public sealed record SplitIdempotencyMismatch(Guid Key) : LinkedContributionSplitResult
{
    public int StatusCode => 409;
    public string Reason => "idempotency_mismatch";
}

public sealed record SplitOutcomeUnknown : LinkedContributionSplitResult
{
    public string Reason => "operation_outcome_unknown";
    public int StatusCode => 503;
}
