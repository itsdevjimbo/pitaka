using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using PitakaApp.Api.Options;

namespace PitakaApp.Api.Services;

// MailKit rather than System.Net.Mail.SmtpClient because Microsoft's own docs
// steer away from SmtpClient for new code, and MailKit is what a .NET developer
// meets in real work.
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOption _emailOption;

    public SmtpEmailSender(IOptions<EmailOption> emailOption)
    {
        _emailOption = emailOption.Value;
    }

    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_emailOption.FromName, _emailOption.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        // None: the dev target is smtp4dev on a trusted local network. Delivery
        // guarantees, auth, and TLS negotiation are out of scope for this slice.
        await client.ConnectAsync(_emailOption.Host, _emailOption.Port, SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
