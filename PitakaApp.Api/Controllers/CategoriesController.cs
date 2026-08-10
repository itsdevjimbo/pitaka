namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Models;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

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

    [Authorize]
    [HttpGet("")]
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
}