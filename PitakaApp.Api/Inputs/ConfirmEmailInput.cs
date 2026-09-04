namespace PitakaApp.Api.Inputs;

public record ConfirmEmailInput(
    int UserId,
    string Token
);
