namespace PitakaApp.Api.Inputs;

public record ResetPasswordInput(
    string Token,
    string Password
);
