namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly GetCurrentUser _getCurrentUser;
    private readonly CategoryService _categoryService;

    public CategoriesController(
        GetCurrentUser getCurrentUser,
        CategoryService categoryService
    )
    {
        _getCurrentUser = getCurrentUser;
        _categoryService = categoryService;
    }

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

    [HttpPost]
    public async Task<IActionResult> Create(CategoryRequest request)
    {
        var user = await _getCurrentUser.ExecuteAsync(User);
        
        if (user == null)
        {
            return Unauthorized();    
        }

        var category = await _categoryService.CreateUserOwnedAsync(request.ToCreateInput(user));

        return StatusCode(StatusCodes.Status201Created, CategoryResource.FromModel(category));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id)
    {
        var user = await _getCurrentUser.ExecuteAsync(User);
        
        if (user == null)
        {
            return Unauthorized();    
        }
    
        var category = await _categoryService.GetByIdForUser(user, id);

        if (category == null)
        {
            return NotFound();    
        }

        return Ok(CategoryResource.FromModel(category));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, CategoryRequest request)
    {
        var user = await _getCurrentUser.ExecuteAsync(User);
        
        if (user == null)
        {
            return Unauthorized();    
        }

        var category = await _categoryService.GetTrackedByIdAsync(id);

        if (category == null)
        {
            return NotFound();
        }

        if (category.UserId != user.Id)
        {
            return Forbid();
        }

        category = await _categoryService.UpdateAsync(category, request.ToUpdateInput());
        return Ok(CategoryResource.FromModel(category));
    }
}