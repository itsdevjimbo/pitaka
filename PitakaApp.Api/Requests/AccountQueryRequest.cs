using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record AccountQueryRequest(
    AccountType? Type,
    bool? IsActive
)
{
    public AccountQueryInput ToInput() => new(Type, IsActive);
}
