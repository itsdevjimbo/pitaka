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
    public async Task Register_WithNewEmail_ReturnsCreatedWithSession()
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

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal(request.email, body.User.Email);
        Assert.Equal(request.name, body.User.Name);
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
        var token = TokenDeliveredTo(email);

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = newPassword });
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
        var token = TokenDeliveredTo(email);

        var first = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "first-new-password" });
        var second = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "second-new-password" });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("This password reset link is invalid or has expired.", problem!.Detail);
    }

    [Fact]
    public async Task ResetPassword_UnknownToken_FailsIdenticallyToAUsedToken()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var token = TokenDeliveredTo(email);
        await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "used-up-password" });

        var usedResponse = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "another-password" });
        var unknownResponse = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { token = "this-token-was-never-issued", password = "another-password" });

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
        var token = TokenDeliveredTo(email);

        var tooShort = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "short12" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        var problem = await tooShort.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Password", problem!.Errors.Keys);

        // The rejected attempt must not have burned the link.
        var retry = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "a-long-enough-password" });
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
        var firstToken = ExtractToken(delivered[0].Body);
        var secondToken = ExtractToken(delivered[1].Body);

        var usingSecond = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { token = secondToken, password = "the-winning-password" });
        Assert.Equal(HttpStatusCode.NoContent, usingSecond.StatusCode);

        var usingFirst = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { token = firstToken, password = "the-losing-password" });
        Assert.Equal(HttpStatusCode.BadRequest, usingFirst.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_StoresOnlyAHashOfTheEmailedToken()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var emailedToken = TokenDeliveredTo(email);
        var stored = await _context.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.User.Email == email);

        // Deliberately below the HTTP seam: this is the one property no observable-behaviour
        // assertion can express, and it is the entire reason the store exists — the plaintext
        // token is in the email and never in the database.
        Assert.NotEqual(emailedToken, stored.TokenHash);
        Assert.Equal(PasswordResetToken.Hash(emailedToken), stored.TokenHash);
    }

    private string TokenDeliveredTo(string email)
    {
        var messages = _emailSender.To(email);
        Assert.NotEmpty(messages);
        return ExtractToken(messages[^1].Body);
    }

    private static string ExtractToken(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_-]+)");
        Assert.True(match.Success, $"No token found in email body:\n{body}");
        return match.Groups[1].Value;
    }

    public void Dispose() => _scope.Dispose();
}