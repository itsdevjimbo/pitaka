namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

[TypeFilter(typeof(ResolveCurrentUserFilter))]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TagsController : ControllerBase
{
    private readonly TagService _tagService;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public TagsController(
        TagService tagService,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _tagService = tagService;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _currentUserAccessor.User!;
        var tags = await _tagService.GetAllForUser(user);

        return Ok(TagResource.Collection(tags));
    }
    
    [HttpPost]
    public async Task<IActionResult> Create(TagRequest request)
    {
        var user = _currentUserAccessor.User!;

        if (await _tagService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Problem(detail: "A tag with this name already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var tag = await _tagService.CreateAsync(user, request.ToInput());

        return StatusCode(StatusCodes.Status201Created, TagResource.FromModel(tag));
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id)
    {
        var user = _currentUserAccessor.User!;
        var tag = await _tagService.GetByIdForUser(user, id);

        if (tag == null)
        {
            return NotFound();
        }

        return Ok(TagResource.FromModel(tag));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, TagRequest request)
    {
        var user = _currentUserAccessor.User!;
        var tag = await _tagService.GetTrackedByIdAsync(id);

        if (tag == null)
        {
            return NotFound();
        }
        
        if (tag.UserId != user.Id)
        {
            return Forbid();
        }

        if (await _tagService.NameExistsForUserAsync(user.Id, request.Name, excludeId: id))
        {
            return Problem(detail: "A tag with this name already exists.", statusCode: StatusCodes.Status409Conflict);
        }
        
        await _tagService.UpdateAsync(tag, request.ToInput());

        return Ok(TagResource.FromModel(tag));
    }

    [HttpDelete("{id}")] 
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        var tag = await _tagService.GetTrackedByIdAsync(id);

        if (tag == null)
        {
            return NotFound();
        }
        
        if (tag.UserId != user.Id)
        {
            return Forbid();
        }

        await _tagService.DeleteAsync(tag);
        return NoContent();
    }
}