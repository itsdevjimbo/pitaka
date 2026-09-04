namespace PitakaApp.Api.Models;

// Split out of TimestampedEntity so User can carry CreatedAt/UpdatedAt without extending
// it — IdentityUser<int> takes the base-class slot instead. TimestampedEntity implements
// this too, so the SaveChanges timestamp override can target one interface and reach both.
public interface ITimestamped
{
    DateTime CreatedAt { get; set; }

    DateTime? UpdatedAt { get; set; }
}
