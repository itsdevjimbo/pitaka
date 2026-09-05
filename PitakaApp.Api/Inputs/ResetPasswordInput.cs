namespace PitakaApp.Api.Inputs;

public record ResetPasswordInput(
    int UserId,
    string Token,
    string Password
);
