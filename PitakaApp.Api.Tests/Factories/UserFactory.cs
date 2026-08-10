namespace PitakaApp.Api.Tests.Factories;

using Bogus;
using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

public static class UserFactory
{
    // Known default so tests can log in without needing the plaintext password
    // handed back to them — mirrors Laravel factories defaulting to a fixed
    // hashed 'password'.
    public const string DefaultPassword = "TestPass123!";

    private static readonly PasswordHasher<User> Hasher = new();

    // Equivalent to User::factory()->make() — builds a fake user, doesn't persist it.
    public static User Make(string? email = null, string? password = null)
    {
        var faker = new Faker();

        return new User
        {
            Name = faker.Person.FullName,
            Email = email ?? faker.Internet.Email(),
            PasswordHash = Hasher.HashPassword(null!, password ?? DefaultPassword),
        };
    }

    // Equivalent to User::factory()->create() — builds and saves it.
    public static async Task<User> CreateAsync(PitakaDbContext context, string? email = null, string? password = null)
    {
        var user = Make(email, password);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user;
    }
}
