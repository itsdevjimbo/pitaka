namespace PitakaApp.Api.Inputs;

public record RegisterInput (
    string Name,
    string Email,
    string Password
);
