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

        CreateUserOwnedCategoryInput input = new (
            User: user,
            Name: request.Name,
            Type: request.Type,
            Description: request.Description,
            Icon: request.Icon,
            Color: request.Color
        );

        var category = await _categoryService.CreateUserOwnedAsync(input);

        return StatusCode(StatusCodes.Status201Created, CategoryResource.FromModel(category));
    }
}