namespace PitakaApp.Api.Requests;

// One home for what counts as a valid password. DataAnnotations take compile-time
// constants, so these are `const` and read identically on RegisterRequest and
// ResetPasswordRequest — slice 2's "one place to change the number" survives its
// second consumer. Length only: 8–128, no complexity rules. The 8 is slice 2's
// deliberately-easy-to-tighten placeholder, inherited verbatim.
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;
}
