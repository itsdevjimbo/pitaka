using Bogus;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Actions.Auth;

[Collection("Database collection")]
public class RegisterUserTest : IDisposable
{    
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly RegisterUser _registerUser;
    private readonly PitakaDbContext _context;

    public RegisterUserTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _registerUser = _scope.ServiceProvider.GetRequiredService<RegisterUser>();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task Register_UniqueEmail_ReturnsSucceededWithUser()
    {
        var result = await _registerUser.ExecuteAsync(
            new RegisterInput(_faker.Person.FullName, _faker.Internet.Email(), "TestPass123!"));

        Assert.Equal(RegisterOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.User);
    }

    [Fact]
    public async Task Register_NotUniqueEmail_ReturnsEmailTaken()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var result = await _registerUser.ExecuteAsync(
            new RegisterInput(_faker.Person.FullName, email, "Password123"));

        Assert.Equal(RegisterOutcome.EmailTaken, result.Outcome);
        Assert.Null(result.User);
    }

    // An apostrophe is legal in an email local part and clears [EmailAddress] at the
    // request edge, but pre-fix it tripped Identity's UserName validator (UserName
    // mirrors Email here) and landed on the same branch as a real duplicate.
    [Fact]
    public async Task Register_EmailWithApostrophe_Succeeds()
    {
        var email = $"o'{_faker.Internet.UserName()}@example.com";

        var result = await _registerUser.ExecuteAsync(
            new RegisterInput(_faker.Person.FullName, email, "TestPass123!"));

        Assert.Equal(RegisterOutcome.Succeeded, result.Outcome);
    }

    // [EmailAddress] at the request edge only requires one non-leading, non-trailing
    // '@' — far looser than the store's UserName validator, even widened. A parenthesis
    // clears the former and trips the latter, so this is not a duplicate and must not be
    // reported as one.
    [Fact]
    public async Task Register_EmailWithCharacterOutsideStoreCharset_ReturnsFailedNotEmailTaken()
    {
        var email = $"o(comment){_faker.Internet.UserName()}@example.com";

        var result = await _registerUser.ExecuteAsync(
            new RegisterInput(_faker.Person.FullName, email, "TestPass123!"));

        Assert.Equal(RegisterOutcome.Failed, result.Outcome);
        Assert.NotEmpty(result.Errors!);
        Assert.DoesNotContain(result.Errors!, e => e.Code is "DuplicateUserName" or "DuplicateEmail");
    }

    public void Dispose() => _scope.Dispose();
}