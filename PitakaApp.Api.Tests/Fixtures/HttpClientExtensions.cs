using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Fixtures;

public static class HttpClientExtensions
{
    public static void ActAsUser(this HttpClient client, User user)
    {
        client.DefaultRequestHeaders.Remove(TestAuthHandler.UserIdHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.Id.ToString());
    }
}
