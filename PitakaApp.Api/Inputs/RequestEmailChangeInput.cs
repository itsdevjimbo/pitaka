namespace PitakaApp.Api.Inputs;

public record RequestEmailChangeInput(
    string NewEmail,
    string CurrentPassword
);
