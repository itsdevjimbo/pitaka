namespace PitakaApp.Api.Models;

// Backs the Data Protection key ring persisted via EfDataProtectionKeyRepository. The
// default per-machine ring lives at ~/.aspnet/DataProtection-Keys, which a container
// redeploy wipes, taking every outstanding confirmation/reset token with it (ADR 0011,
// ADR 0013). Shape matches Microsoft.AspNetCore.DataProtection.EntityFrameworkCore's own
// DataProtectionKey — that package cannot be referenced directly here (see
// EfDataProtectionKeyRepository), but the row shape is deliberately compatible with it.
public class DataProtectionKey
{
    public int Id { get; set; }

    public string? FriendlyName { get; set; }

    public string? Xml { get; set; }
}
