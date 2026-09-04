using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Infra;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public static class UserFactory
{
    // Known default so tests can log in without needing the plaintext password
    // handed back to them — mirrors Laravel factories defaulting to a fixed
    // hashed 'password'.
    public const string DefaultPassword = "TestPass123!";

    // Equivalent to User::factory()->make() — builds a fake user, doesn't persist it.
    // Leaves PasswordHash null: only CreateAsync produces an authenticatable Profile, so
    // there is one hashing path (UserManager's) in the test suite.
    public static User Make(string? email = null)
    {
        var faker = new Faker();
        var resolvedEmail = email ?? faker.Internet.Email();

        return new User
        {
            Name = faker.Person.FullName,
            Email = resolvedEmail,
            UserName = resolvedEmail,
        };
    }

    // Equivalent to User::factory()->create() — builds, hashes and saves it through
    // Identity's own store, exactly as RegisterUser does, and confirms the email by
    // default so the ~25 files that build a Profile and act as it are unaffected by S2's
    // confirmation gate landing later.
    public static async Task<User> CreateAsync(PitakaDbContext context, string? email = null, string? password = null)
    {
        var user = Make(email);
        var userManager = BuildUserManager(context);

        var result = await userManager.CreateAsync(user, password ?? DefaultPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"UserFactory.CreateAsync failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        user.EmailConfirmed = true;
        await context.SaveChangesAsync();

        return user;
    }

    // A UserManager<User> scoped to the given context, built by hand rather than
    // resolved from DI — UserFactory takes only a PitakaDbContext (unchanged across ~25
    // call sites), not a service scope. Shares IdentityExtensions.ConfigureIdentityOptions
    // with the real store's registration so the two can't silently drift apart on what
    // counts as a valid password.
    private static UserManager<User> BuildUserManager(PitakaDbContext context)
    {
        var store = new UserOnlyStore<User, PitakaDbContext, int>(context);
        var identityOptions = new IdentityOptions();
        IdentityExtensions.ConfigureIdentityOptions(identityOptions);
        var options = Microsoft.Extensions.Options.Options.Create(identityOptions);

        return new UserManager<User>(
            store,
            options,
            new PasswordHasher<User>(),
            new[] { new UserValidator<User>() },
            new[] { new PasswordValidator<User>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<User>>.Instance);
    }
}
