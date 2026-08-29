namespace PitakaApp.Api.Services;

// The first interface abstraction in this codebase. It exists because the test
// suite must observe what was sent without an SMTP server, and asserting on a
// concrete sender's internals is the coupling the tests are meant to avoid.
//
// One method, shaped for its single caller: a plain-text message to one address
// with a subject and a body. It widens when a second caller appears.
public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default);
}
