namespace PitakaApp.Api.Services;

// The first interface abstraction in this codebase. It exists because the test
// suite must observe what was sent without an SMTP server, and asserting on a
// concrete sender's internals is the coupling the tests are meant to avoid.
//
// One method carrying both representations of a message explicitly. The interface
// widened when callers needed HTML alongside plain text; requiring both keeps a caller
// from silently falling back to the old text-only delivery.
public interface IEmailSender
{
    Task SendAsync(
        string toAddress,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken = default);
}
