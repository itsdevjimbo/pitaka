using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Bogus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Controllers;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class AuthControllerTest : IDisposable
{
    
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;
    private readonly RecordingEmailSender _emailSender;

    public AuthControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
        _emailSender = factory.EmailSender;
    }

    [Fact]
    public async Task Login_WithExistingUser_ReturnsOkWithUser()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var request = new { email, password = UserFactory.DefaultPassword };

        var response = await _client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(body);
        Assert.NotNull(body.Token);
        Assert.Equal(request.email, body!.User.Email);
    }

    [Fact]
    public async Task Login_WithInvalidCredential_ReturnsProblemDetailsUnauthorized()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var wrongEmailResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "wrong@email.com",
            password = UserFactory.DefaultPassword,
        });

        var wrongPasswordResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "WrongPassword123!",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongEmailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);

        // ProblemDetails, not a bare quoted string — Content-Type and a populated detail.
        Assert.Equal("application/problem+json", wrongEmailResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", wrongPasswordResponse.Content.Headers.ContentType?.MediaType);

        var wrongEmailProblem = await wrongEmailResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var wrongPasswordProblem = await wrongPasswordResponse.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal("Invalid email or password.", wrongEmailProblem!.Detail);

        // The two failure modes stay indistinguishable — same status, same title, same
        // detail. (A per-request traceId is the only thing that differs, by design.)
        Assert.Equal(wrongEmailProblem.Status, wrongPasswordProblem!.Status);
        Assert.Equal(wrongEmailProblem.Title, wrongPasswordProblem.Title);
        Assert.Equal(wrongEmailProblem.Detail, wrongPasswordProblem.Detail);
    }

    [Theory]
    [InlineData("missing email")]
    [InlineData("missing password")]
    public async Task Login_WithMissingField_ReturnsBadRequestNotUnauthorized(string missing)
    {
        object request = missing == "missing email"
            ? new { password = UserFactory.DefaultPassword }
            : new { email = _faker.Internet.Email() };

        var response = await _client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithNewEmail_ReturnsCreatedWithProfileOnly_AndNoToken()
    {
        var request = new
        {
            name = _faker.Person.FullName,
            email = _faker.Internet.Email(),
            password = "TestPass123!",
        };

        var response = await _client.PostAsJsonAsync("/api/Auth/register", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(body);
        Assert.Equal(request.email, body!.Email);
        Assert.Equal(request.name, body.Name);
    }

    [Fact]
    public async Task Register_WithNewEmail_DeliversConfirmationLinkToConfiguredConfirmUrl()
    {
        var email = _faker.Internet.Email();
        var request = new
        {
            name = _faker.Person.FullName,
            email,
            password = "TestPass123!",
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var message = Assert.Single(_emailSender.To(email));
        Assert.Contains(ConfiguredConfirmUrl, message.Body);
        Assert.Contains("Profile", message.Body);
    }

    [Fact]
    public async Task Register_TwiceWithSameEmail_SecondReturnsConflict()
    {
        var request = new
        {
            name = _faker.Person.FullName,
            email = _faker.Internet.Email(),
            password = "TestPass123!",
        };

        var first = await _client.PostAsJsonAsync("/api/auth/register", request);
        var second = await _client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("A user with this email already exists.", problem!.Detail);
    }

    [Theory]
    [InlineData("name", "Name")]
    [InlineData("email", "Email")]
    [InlineData("password", "Password")]
    public async Task Register_WithInvalidField_ReturnsBadRequestNamingTheField(string field, string expectedKey)
    {
        var request = new Dictionary<string, string>
        {
            ["name"] = _faker.Person.FullName,
            ["email"] = _faker.Internet.Email(),
            ["password"] = "TestPass123!",
        };

        request[field] = field switch
        {
            "name" => "",
            "email" => "not-an-email",
            "password" => "short12", // 7 characters — under the floor
            _ => request[field],
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(expectedKey, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Register_WithLongPassword_IsRejectedForLengthOnly()
    {
        // A 129-character password fails the ceiling; a compliant-length password with no
        // digits, symbols or case mix must still succeed — length only, no complexity.
        var tooLong = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = _faker.Person.FullName,
            email = _faker.Internet.Email(),
            password = new string('a', 129),
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var tooLongProblem = await tooLong.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Password", tooLongProblem!.Errors.Keys);

        var noComplexity = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = _faker.Person.FullName,
            email = _faker.Internet.Email(),
            password = "aaaaaaaa",
        });
        Assert.Equal(HttpStatusCode.Created, noComplexity.StatusCode);
    }

    [Fact]
    public async Task Register_WithExistingEmail_ReturnsConflict()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var request = new
        {
            Name = _faker.Person.FullName,
            Email = email,
            Password = "SomePassword123!",
        };

        var response = await _client.PostAsJsonAsync("/api/Auth/register", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal("A user with this email already exists.", problem!.Detail);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsCurrentUser()
    {
        var email = _faker.Internet.Email();

        var user = await UserFactory.CreateAsync(_context, email);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(email, body!.Email);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The reset URL the test host serves — the appsettings.json default, since the test
    // factory overrides only the connection string, the JWT key and the worker flag.
    private const string ConfiguredResetUrl = "http://localhost:4200/reset-password";

    // The confirm-email URL the test host serves — the appsettings.json default.
    private const string ConfiguredConfirmUrl = "http://localhost:4200/confirm-email";

    [Fact]
    public async Task ForgotPassword_KnownEmail_Returns202_AndDeliversLinkToResetUrl()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var message = Assert.Single(_emailSender.To(email));
        Assert.Contains(ConfiguredResetUrl, message.Body);
        Assert.Contains("Profile", message.Body);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_IsIndistinguishable_AndDeliversNothing()
    {
        var knownEmail = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, knownEmail);
        var unknownEmail = _faker.Internet.Email();

        var known = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = knownEmail });
        var unknown = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = unknownEmail });

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);

        // Byte-identical responses — same status, same (empty) body.
        Assert.Equal(await known.Content.ReadAsByteArrayAsync(), await unknown.Content.ReadAsByteArrayAsync());

        Assert.Empty(_emailSender.To(unknownEmail));
    }

    [Fact]
    public async Task ForgotPassword_MalformedEmail_Returns400NamingEmail()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Email", problem!.Errors.Keys);
    }

    [Fact]
    public async Task ResetPassword_TheArc_NewPasswordWorks_OldPasswordDoesNot()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);
        const string newPassword = "a-fresh-password";

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var (userId, token) = ResetLinkDeliveredTo(email);

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password", new { userId, token, password = newPassword });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        // No session handed back — the body is empty and no auth cookie is set.
        Assert.Empty(await reset.Content.ReadAsByteArrayAsync());
        Assert.DoesNotContain(reset.Headers, h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));

        var withNew = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = newPassword });
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);

        var withOld = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = UserFactory.DefaultPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_SameTokenTwice_SecondFailsAsProblemDetails()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var (userId, token) = ResetLinkDeliveredTo(email);

        var first = await _client.PostAsJsonAsync("/api/auth/reset-password", new { userId, token, password = "first-new-password" });
        var second = await _client.PostAsJsonAsync("/api/auth/reset-password", new { userId, token, password = "second-new-password" });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("This password reset link is invalid or has expired.", problem!.Detail);
    }

    [Fact]
    public async Task ResetPassword_UnknownUserId_FailsIdenticallyToAUsedToken()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var (userId, token) = ResetLinkDeliveredTo(email);
        await _client.PostAsJsonAsync("/api/auth/reset-password", new { userId, token, password = "used-up-password" });

        var usedResponse = await _client.PostAsJsonAsync("/api/auth/reset-password", new { userId, token, password = "another-password" });
        var unknownResponse = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { userId = -1, token, password = "another-password" });

        Assert.Equal(HttpStatusCode.BadRequest, usedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
        Assert.Equal("application/problem+json", unknownResponse.Content.Headers.ContentType?.MediaType);

        var usedProblem = await usedResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var unknownProblem = await unknownResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(usedProblem!.Status, unknownProblem!.Status);
        Assert.Equal(usedProblem.Title, unknownProblem.Title);
        Assert.Equal(usedProblem.Detail, unknownProblem.Detail);
    }

    [Fact]
    public async Task ResetPassword_TooShortPassword_Returns400NamingPassword_AndTokenStillWorks()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var (userId, token) = ResetLinkDeliveredTo(email);

        var tooShort = await _client.PostAsJsonAsync("/api/auth/reset-password", new { userId, token, password = "short12" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        var problem = await tooShort.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Password", problem!.Errors.Keys);

        // The rejected attempt must not have burned the link.
        var retry = await _client.PostAsJsonAsync("/api/auth/reset-password", new { userId, token, password = "a-long-enough-password" });
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_UsingTheSecondToken_InvalidatesTheFirst()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        var delivered = _emailSender.To(email);
        Assert.Equal(2, delivered.Count);
        var (firstUserId, firstToken) = ExtractUserIdAndToken(delivered[0].Body);
        var (secondUserId, secondToken) = ExtractUserIdAndToken(delivered[1].Body);

        var usingSecond = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { userId = secondUserId, token = secondToken, password = "the-winning-password" });
        Assert.Equal(HttpStatusCode.NoContent, usingSecond.StatusCode);

        var usingFirst = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { userId = firstUserId, token = firstToken, password = "the-losing-password" });
        Assert.Equal(HttpStatusCode.BadRequest, usingFirst.StatusCode);
    }

    [Fact]
    public async Task ConfirmEmail_TheArc_ThenLoginSucceeds()
    {
        var email = _faker.Internet.Email();
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = _faker.Person.FullName,
            email,
            password = "TestPass123!",
        });

        var (userId, token) = ConfirmationDeliveredTo(email);

        var confirm = await _client.PostAsJsonAsync("/api/auth/confirm-email", new { userId, token });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "TestPass123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ConfirmEmail_WithGarbageToken_ReturnsOneProblemDetails400()
    {
        var email = _faker.Internet.Email();
        var user = await UserFactory.CreateAsync(_context, email);
        user.EmailConfirmed = false;
        await _context.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/confirm-email",
            new { userId = user.Id, token = "this-token-was-never-issued" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("This confirmation link is invalid or has expired.", problem!.Detail);
    }

    [Fact]
    public async Task Login_BeforeConfirming_Returns403WithDetail()
    {
        var email = _faker.Internet.Email();
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = _faker.Person.FullName,
            email,
            password = "TestPass123!",
        });

        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "TestPass123!" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Confirm your email to sign in.", problem!.Detail);
    }

    [Fact]
    public async Task Login_AfterFiveFailedAttempts_Returns423Locked()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPassword123!" });
        }

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = UserFactory.DefaultPassword });

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ResendConfirmation_KnownUnconfirmedEmail_Returns202_AndDeliversFreshLink()
    {
        var email = _faker.Internet.Email();
        var user = await UserFactory.CreateAsync(_context, email);
        user.EmailConfirmed = false;
        await _context.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotEmpty(_emailSender.To(email));
    }

    [Fact]
    public async Task ResendConfirmation_UnknownEmail_IsIndistinguishable_AndDeliversNothing()
    {
        var knownEmail = _faker.Internet.Email();
        var known = await UserFactory.CreateAsync(_context, knownEmail);
        known.EmailConfirmed = false;
        await _context.SaveChangesAsync();
        var unknownEmail = _faker.Internet.Email();

        var knownResponse = await _client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = knownEmail });
        var unknownResponse = await _client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = unknownEmail });

        Assert.Equal(HttpStatusCode.Accepted, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);
        Assert.Equal(await knownResponse.Content.ReadAsByteArrayAsync(), await unknownResponse.Content.ReadAsByteArrayAsync());

        Assert.Empty(_emailSender.To(unknownEmail));
    }

    [Fact]
    public async Task ResendConfirmation_AlreadyConfirmedEmail_IsIndistinguishable_AndDeliversNothing()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email); // EmailConfirmed = true by default

        var response = await _client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(_emailSender.To(email));
    }

    private (int UserId, string Token) ResetLinkDeliveredTo(string email)
    {
        var messages = _emailSender.To(email);
        Assert.NotEmpty(messages);
        return ExtractUserIdAndToken(messages[^1].Body);
    }

    private (int UserId, string Token) ConfirmationDeliveredTo(string email)
    {
        var messages = _emailSender.To(email);
        Assert.NotEmpty(messages);
        return ExtractUserIdAndToken(messages[^1].Body);
    }

    private static (int UserId, string Token) ExtractUserIdAndToken(string body)
    {
        var match = Regex.Match(body, @"userId=(?<userId>\d+)&token=(?<token>\S+)");
        Assert.True(match.Success, $"No userId/token link found in email body:\n{body}");

        return (int.Parse(match.Groups["userId"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    public void Dispose() => _scope.Dispose();
}