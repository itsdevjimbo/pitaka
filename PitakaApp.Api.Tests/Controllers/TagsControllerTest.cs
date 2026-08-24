
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class TagsControllerTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public TagsControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task Get_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/tags");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithLoggedInUser_ReturnsTheirTags()
    {
        var user = await UserFactory.CreateAsync(_context);

        await TagFactory.CreateAsync(_context, user.Id, "Tag 1");
        await TagFactory.CreateAsync(_context, user.Id, "Tag 2");
        await TagFactory.CreateAsync(_context, user.Id, "Tag 3");

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/tags");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TagResource>>();
        Assert.Equal(3, body!.Count);
    }

    [Fact]
    public async Task Create_WithNoLoggedInUser_ReturnsUnauthorized()
    {
        var request = new
        {
            Name = "Test tag 1"
        };
        
        var response = await _client.PostAsJsonAsync("/api/tags", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await TagFactory.CreateAsync(_context, user.Id, "Duplicate name");

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Duplicate name"
        };
        
        var response = await _client.PostAsJsonAsync("/api/tags", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameDifferentUser_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        await TagFactory.CreateAsync(_context, userB.Id, "Duplicate name");

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Duplicate name"
        };
        
        var response = await _client.PostAsJsonAsync("/api/tags", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithLoggedInUser_ReturnsCreatedStatusCode()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "pitaka-app"
        };
        
        var response = await _client.PostAsJsonAsync("/api/tags", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var tag = await _context.Tags.SingleAsync(t => t.UserId == user.Id);
        var body = await response.Content.ReadFromJsonAsync<TagResource>();
        
        Assert.Equal(tag.Id, body!.Id);
        Assert.Equal("pitaka-app", body!.Name);
    }

    [Fact]
    public async Task Show_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var tag = await TagFactory.CreateAsync(_context, user.Id);

        var response = await _client.GetAsync("/api/tags/" + tag.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Show_NonExistentTag_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        var response = await _client.GetAsync("/api/tags/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_TagBelongsToOtherUser_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var tag = await TagFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/tags/" + tag.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var tag = await TagFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/tags/" + tag.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var tag = await TagFactory.CreateAsync(_context, user.Id);

        var response = await _client.PutAsJsonAsync("/api/tags/" + tag.Id, new { Name = "Update tag" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentTag_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        var response = await _client.PutAsJsonAsync("/api/tags/9999", new { Name = "Update tag" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_BelongsToOtherUsersTag_ReturnsForbidden()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var tag = await TagFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(user);

        var response = await _client.PutAsJsonAsync("/api/tags/" + tag.Id, new { Name = "Update tag" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await TagFactory.CreateAsync(_context, user.Id, "Duplicate Name");
        var tag = await TagFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var response = await _client.PutAsJsonAsync("/api/tags/" + tag.Id, new { Name = "Duplicate Name"});
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var tag = await TagFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var response = await _client.PutAsJsonAsync("/api/tags/" + tag.Id, new { Name = "Update Name" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _context.Entry(tag).ReloadAsync();
        Assert.Equal("Update Name", tag.Name);
    }

    [Fact]
    public async Task Delete_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var tag = await TagFactory.CreateAsync(_context, user.Id);

        var response = await _client.DeleteAsync("/api/tags/" + tag.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithNonExistentTag_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/tags/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_BelongsToOtherUsersTag_ReturnsForbidden()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var tag = await TagFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/tags/" + tag.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        var tag = await TagFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/tags/" + tag.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.False(await _context.Tags.AnyAsync(t => t.Id == tag.Id));
    }

    public void Dispose() => _scope.Dispose();
}