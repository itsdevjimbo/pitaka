namespace PitakaApp.Api.Tests.Fixtures;

using PitakaApp.Api.Models;

public static class HttpClientExtensions
{
    public static void ActAsUser(this HttpClient client, User user)
    {
        client.DefaultRequestHeaders.Remove(TestAuthHandler.UserIdHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.Id.ToString());
    }
}
