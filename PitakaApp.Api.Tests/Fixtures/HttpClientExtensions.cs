using System.Net;
using System.Text.Json.Nodes;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Fixtures;

public static class HttpClientExtensions
{
    public static void ActAsUser(this HttpClient client, User user)
    {
        client.DefaultRequestHeaders.Remove(TestAuthHandler.UserIdHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.Id.ToString());
    }

    public static async Task AssertNotFoundEquivalentToAsync(
        this HttpResponseMessage response,
        HttpResponseMessage missingResponse
    )
    {
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(missingResponse.StatusCode, response.StatusCode);
        var missingProblem = JsonNode
            .Parse(await missingResponse.Content.ReadAsStringAsync())!
            .AsObject();
        var responseProblem = JsonNode
            .Parse(await response.Content.ReadAsStringAsync())!
            .AsObject();
        missingProblem.Remove("traceId");
        responseProblem.Remove("traceId");
        Assert.Equal(missingProblem.ToJsonString(), responseProblem.ToJsonString());
    }
}
