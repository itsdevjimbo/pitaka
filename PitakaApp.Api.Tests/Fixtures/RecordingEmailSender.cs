using System.Collections.Concurrent;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Tests.Fixtures;

// Substituted for the real IEmailSender in PitakaWebApplicationFactory.ConfigureTestServices,
// exactly as TestAuthHandler is substituted for JWT auth in the same method. It lets the
// suite observe what was sent without an SMTP server and without asserting on the sender.
//
// It is a collection-scoped singleton, so it accumulates across every test in the
// "Database collection". Tests find their message by recipient address — Bogus generates
// a unique one per test — never by assuming an empty inbox.
public class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<RecordedEmail> _sent = new();

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(new RecordedEmail(toAddress, subject, body));
        return Task.CompletedTask;
    }

    public IReadOnlyList<RecordedEmail> To(string address) =>
        _sent.Where(m => m.ToAddress == address).ToList();
}

public record RecordedEmail(string ToAddress, string Subject, string Body);
