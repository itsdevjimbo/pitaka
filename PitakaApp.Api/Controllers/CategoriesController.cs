using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Models;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly GetCurrentUser _getCurrentUser;
    private readonly CategoryService _categoryService;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public CategoriesController(
        GetCurrentUser getCurrentUser,
        CategoryService categoryService,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _getCurrentUser = getCurrentUser;
        _categoryService = categoryService;
        _currentUserAccessor = currentUserAccessor;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await _getCurrentUser.ExecuteAsync(User);
        List<Category> categories;
        
        if (user != null)
        {
            categories = await _categoryService.GetAllForUser(user);
            return Ok(CategoryResource.Collection(categories));
        }

        categories = await _categoryService.GetSystemDefaults();
        return Ok(CategoryResource.Collection(categories));
    }

    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [HttpPost]
    public async Task<IActionResult> Create(CategoryRequest request)
    {
        var user = _currentUserAccessor.User!;

        if (await _categoryService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Problem(detail: "A category with this name already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        if (request.ParentId != null && !await _categoryService.IsValidParentAsync(user, request.ParentId.Value))
        {
            return Problem(detail: "Invalid parent category.", statusCode: StatusCodes.Status400BadRequest);
        }

        var category = await _categoryService.CreateUserOwnedAsync(user, request.ToInput());
        return StatusCode(StatusCodes.Status201Created, CategoryResource.FromModel(category));
    }

    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id)
    {
        var category = await _categoryService.GetByIdForUser(_currentUserAccessor.User!, id);

        if (category == null)
        {
            return NotFound();    
        }

        return Ok(CategoryResource.FromModel(category));
    }

    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, CategoryRequest request)
    {
        var user = _currentUserAccessor.User!;
        var category = await _categoryService.GetTrackedByIdAsync(id);

        if (category == null)
        {
            return NotFound();
        }
        
        if (category.UserId != user.Id)
        {
            return Forbid();
        }

        if (await _categoryService.NameExistsForUserAsync(user.Id, request.Name, excludeId: id))
        {
            return Problem(detail: "A category with this name already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        if (request.ParentId != null && !await _categoryService.IsValidParentAsync(user, request.ParentId.Value, excludeId: id))
        {
            return Problem(detail: "Invalid parent category.", statusCode: StatusCodes.Status400BadRequest);
        }

        category = await _categoryService.UpdateAsync(category, request.ToInput());
        return Ok(CategoryResource.FromModel(category));
    }

    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        var category = await _categoryService.GetTrackedByIdAsync(id);

        if (category == null)
        {
            return NotFound();
        }
        
        if (category.UserId != user.Id)
        {
            return Forbid();
        }

        await _categoryService.DeleteAsync(category);

        return NoContent();   
    }
}