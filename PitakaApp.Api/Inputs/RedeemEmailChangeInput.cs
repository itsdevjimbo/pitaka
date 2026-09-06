namespace PitakaApp.Api.Inputs;

public record RedeemEmailChangeInput(
    int UserId,
    string Token
);
