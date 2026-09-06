using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Controllers;
using PitakaApp.Api.Data;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

// Ticket 02 of the email-change feature, driven the way the spec asks: through the real
// JwtBearerHandler (RealAuthWebApplicationFactory) and the recording email sender, so a
// session genuinely survives the request. Asserts only what a person or client can see —
// status codes, which addresses sign in, what landed in the outbox. Redeeming the link
// is ticket 03; these tests stop at "the link is in the outbox".
[Collection("RealAuthDatabase collection")]
public class ProfileControllerRealAuthTest : IDisposable
{
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;
    private readonly RecordingEmailSender _emailSender;

    public ProfileControllerRealAuthTest(RealAuthWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
        _emailSender = factory.EmailSender;
    }

    [Fact]
    public async Task RequestEmailChange_WithCurrentPasswordAndFreshAddress_SendsLinkAndLeavesLiveAddressWorking()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var token = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var response = await Send(HttpMethod.Post, "/api/profile/email-change", token, new
        {
            newEmail,
            currentPassword = password,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // A confirmation link reached the new address, and the copy says Profile —
        // never User, never Account (CONTEXT.md).
        var message = Assert.Single(_emailSender.To(newEmail));
        Assert.Contains("Profile", message.Body);
        Assert.DoesNotContain("User", message.Body);
        Assert.DoesNotContain("Account", message.Body);
        Assert.DoesNotContain("User", message.Subject);
        Assert.DoesNotContain("Account", message.Subject);
        Assert.Matches(@"userId=\d+&token=\S+", message.Body);

        // The old address got nothing new — its only message is still the sign-up
        // confirmation from registration. The courtesy notice is ticket 07.
        var toOld = Assert.Single(_emailSender.To(oldEmail));
        Assert.Equal("Confirm your Pitaka Profile", toOld.Subject);

        // The live address still signs in; the pending one does not.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(oldEmail, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(newEmail, password)).StatusCode);
    }

    [Fact]
    public async Task RequestEmailChange_WithWrongCurrentPassword_IsRefusedAndSendsNothing()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var token = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var response = await Send(HttpMethod.Post, "/api/profile/email-change", token, new
        {
            newEmail,
            currentPassword = "not-the-password",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_emailSender.To(newEmail));
        Assert.Equal(HttpStatusCode.OK, (await LogIn(oldEmail, password)).StatusCode);
    }

    [Fact]
    public async Task RequestEmailChange_WithoutBearerToken_ReturnsUnauthorized()
    {
        var newEmail = _faker.Internet.Email();

        var response = await _client.PostAsJsonAsync("/api/profile/email-change", new
        {
            newEmail,
            currentPassword = "TestPass123!",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_emailSender.To(newEmail));
    }

    [Fact]
    public async Task RequestEmailChange_ToTheProfilesOwnCurrentAddress_IsRefusedAndSendsNothing()
    {
        const string password = "TestPass123!";
        var email = _faker.Internet.Email();
        var token = await RegisterConfirmAndLogInAsync(email, password);

        var response = await Send(HttpMethod.Post, "/api/profile/email-change", token, new
        {
            newEmail = email,
            currentPassword = password,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Only the sign-up confirmation — no "confirm your new email" was sent.
        var toSelf = Assert.Single(_emailSender.To(email));
        Assert.Equal("Confirm your Pitaka Profile", toSelf.Subject);
    }

    [Fact]
    public async Task RequestEmailChange_ToAddressHeldByAnotherProfile_ReturnsConflict()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var token = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var otherProfile = await UserFactory.CreateAsync(_context);

        var response = await Send(HttpMethod.Post, "/api/profile/email-change", token, new
        {
            newEmail = otherProfile.Email,
            currentPassword = password,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_emailSender.To(otherProfile.Email!));
    }

    // Register, pull the confirmation link out of the outbox, confirm, then log in —
    // the same arc AuthControllerRealAuthTest proves, reused here to get a real bearer
    // token for a confirmed Profile.
    private async Task<string> RegisterConfirmAndLogInAsync(string email, string password)
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = _faker.Person.FullName,
            email,
            password,
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var confirmMessage = Assert.Single(_emailSender.To(email));
        var match = Regex.Match(confirmMessage.Body, @"userId=(?<userId>\d+)&token=(?<token>[^\s]+)");
        Assert.True(match.Success, $"No confirm-email link in:\n{confirmMessage.Body}");

        var confirmResponse = await _client.PostAsJsonAsync("/api/auth/confirm-email", new
        {
            userId = int.Parse(match.Groups["userId"].Value),
            token = Uri.UnescapeDataString(match.Groups["token"].Value),
        });
        Assert.Equal(HttpStatusCode.NoContent, confirmResponse.StatusCode);

        var loginResponse = await LogIn(email, password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private Task<HttpResponseMessage> LogIn(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private Task<HttpResponseMessage> Send(HttpMethod method, string uri, string bearerToken, object body)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return _client.SendAsync(request);
    }

    public void Dispose() => _scope.Dispose();
}
