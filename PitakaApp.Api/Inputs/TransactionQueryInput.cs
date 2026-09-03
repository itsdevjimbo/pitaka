using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Inputs;

public record TransactionQueryInput(
    int? AccountId,
    int? CategoryId,
    TransactionType? Type,
    string? Description,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize
);
