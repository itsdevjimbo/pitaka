namespace PitakaApp.Api.Options;

public class RecurringTransactionGenerationOption
{
    public const string SectionName = "RecurringTransaction";
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; }
}