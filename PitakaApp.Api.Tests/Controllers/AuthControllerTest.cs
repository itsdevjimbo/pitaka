using System.Net;
using System.Net.Http.Json;
using Bogus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Controllers;
using PitakaApp.Api.Data;
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

    public AuthControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();   
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

    public void Dispose() => _scope.Dispose();
}