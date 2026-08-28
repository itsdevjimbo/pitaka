using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

// Exercises the request pipeline — the CORS policy — rather than a controller, but reaches
// it through the same HTTP seam every controller test uses: the shared web application
// factory and its client. Assertions are about what a browser can observe (response
// status and CORS response headers for a request carrying an Origin), never about which
// middleware was registered, that an options type was bound, or the policy's name.
//
// AuthControllerRealAuthTest is the model for a class that tests pipeline behaviour and
// needs a differently-configured host for some cases; AccountsControllerTest is the model
// for the plain request-and-assert shape. No request here is authenticated — CORS is
// evaluated before authorization, so the unauthenticated 401 these endpoints return does
// not get in the way.
[Collection("Database collection")]
public class CorsPipelineTest
{
    private const string AllowedOrigin = "http://localhost:4200";
    private const string UnlistedOrigin = "https://malicious.example.com";
    private const string Endpoint = "/api/accounts";

    private const string AllowOrigin = "Access-Control-Allow-Origin";
    private const string AllowCredentials = "Access-Control-Allow-Credentials";
    private const string AllowHeaders = "Access-Control-Allow-Headers";

    private readonly HttpClient _client;

    public CorsPipelineTest(PitakaWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preflight_FromAllowedOrigin_IsAnsweredAndEchoesTheOrigin()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, Endpoint);
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues(AllowOrigin)));
    }

    [Fact]
    public async Task Preflight_RequestingAuthorizationAndContentTypeHeaders_IsAllowed()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, Endpoint);
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "authorization, content-type");

        var response = await _client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues(AllowOrigin)));

        var allowedHeaders = Assert.Single(response.Headers.GetValues(AllowHeaders));
        Assert.Contains("authorization", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("content-type", allowedHeaders, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealRequest_FromAllowedOrigin_CarriesTheAllowOriginHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);

        var response = await _client.SendAsync(request);

        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues(AllowOrigin)));
    }

    [Fact]
    public async Task RealRequest_FromUnlistedOrigin_HasNoAllowOriginHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.TryAddWithoutValidation("Origin", UnlistedOrigin);

        var response = await _client.SendAsync(request);

        // The request still completes — the browser is what blocks, not the server —
        // so the assertion is the absence of the header, not a status code.
        Assert.False(response.Headers.Contains(AllowOrigin));
    }

    [Fact]
    public async Task Response_NeverCarriesAllowCredentials()
    {
        var preflight = new HttpRequestMessage(HttpMethod.Options, Endpoint);
        preflight.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
        var preflightResponse = await _client.SendAsync(preflight);

        var real = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        real.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        var realResponse = await _client.SendAsync(real);

        Assert.False(preflightResponse.Headers.Contains(AllowCredentials));
        Assert.False(realResponse.Headers.Contains(AllowCredentials));
    }

    [Theory]
    [InlineData]                                 // empty origin list
    [InlineData("http://localhost:4200/")]       // trailing slash
    [InlineData("http://localhost:4200/app")]    // path
    [InlineData("localhost:4200")]               // not a scheme-and-authority absolute URI
    public void Startup_WithMalformedOriginConfiguration_FailsToStart(params string[] origins)
    {
        using var factory = FactoryWithConfiguredOrigins(origins);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("AllowedOrigins", exception!.ToString());
    }

    // A throwaway host whose configuration is entirely under this test's control, so the
    // origin list can be set to anything — including empty, which an override layered on
    // top of appsettings.json cannot express. Built off a fresh WebApplicationFactory
    // rather than WithWebHostBuilder on the shared fixture: a derived host that fails
    // startup validation leaves the shared fixture's lazily-built host faulted for every
    // later test in the collection. No new factory subclass and no new collection.
    // Clearing the sources drops the defaults too, so the handful of keys the host needs
    // to get as far as CORS validation are re-added.
    private static WebApplicationFactory<Program> FactoryWithConfiguredOrigins(string[] origins) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();

                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = PitakaWebApplicationFactory.TestConnectionString,
                    ["Jwt:Key"] = PitakaWebApplicationFactory.TestJwtKey,
                    ["Jwt:Issuer"] = "PitakaApp",
                    ["Jwt:Audience"] = "PitakaAppUsers",
                    ["Jwt:ExpiryMinutes"] = "60",
                    ["RecurringTransaction:Enabled"] = "false",
                };

                for (var i = 0; i < origins.Length; i++)
                {
                    settings[$"Cors:AllowedOrigins:{i}"] = origins[i];
                }

                config.AddInMemoryCollection(settings);
            }));
}
