using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Inputs;

public record TransactionQueryInput(
    int? AccountId,
    int? CategoryId,
    TransactionType? Type,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize
);
