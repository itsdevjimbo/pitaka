namespace PitakaApp.Api.Models;

public abstract class TimestampedEntity : ITimestamped
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
}