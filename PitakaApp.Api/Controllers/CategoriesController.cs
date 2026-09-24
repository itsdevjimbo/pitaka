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
public class CategoriesController(
    GetCurrentUser getCurrentUser,
    CategoryService categoryService,
    CurrentUserAccessor currentUserAccessor
) : ControllerBase
{
    private readonly GetCurrentUser _getCurrentUser = getCurrentUser;
    private readonly CategoryService _categoryService = categoryService;
    private readonly CurrentUserAccessor _currentUserAccessor = currentUserAccessor;

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
    public async Task<IActionResult> Create(CreateCategoryRequest request)
    {
        var user = _currentUserAccessor.User!;

        if (await _categoryService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Problem(
                detail: "A category with this name already exists.",
                statusCode: StatusCodes.Status409Conflict
            );
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
    public async Task<IActionResult> Update(
        int id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken
    )
    {
        var user = _currentUserAccessor.User!;
        var category = await _categoryService.GetTrackedByIdForUserAsync(
            user.Id,
            id,
            cancellationToken
        );

        if (category == null)
        {
            return await NotFoundUnlessSystemDefaultAsync(id, cancellationToken);
        }

        if (
            await _categoryService.NameExistsForUserAsync(
                user.Id,
                request.Name,
                excludeId: id,
                cancellationToken: cancellationToken
            )
        )
        {
            return Problem(
                detail: "A category with this name already exists.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        category = await _categoryService.UpdateAsync(
            category,
            request.ToInput(),
            cancellationToken
        );
        return Ok(CategoryResource.FromModel(category));
    }

    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> PatchStatus(
        int id,
        PatchCategoryActiveStatusRequest request,
        CancellationToken cancellationToken
    )
    {
        var user = _currentUserAccessor.User!;
        var category = await _categoryService.GetTrackedByIdForUserAsync(
            user.Id,
            id,
            cancellationToken
        );

        if (category == null)
        {
            return await NotFoundUnlessSystemDefaultAsync(id, cancellationToken);
        }

        category = await _categoryService.PatchActiveStatusAsync(
            category,
            request.ToInput(),
            cancellationToken
        );
        return Ok(CategoryResource.FromModel(category));
    }

    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var user = _currentUserAccessor.User!;
        var category = await _categoryService.GetTrackedByIdForUserAsync(
            user.Id,
            id,
            cancellationToken
        );

        if (category == null)
        {
            return await NotFoundUnlessSystemDefaultAsync(id, cancellationToken);
        }

        if (await _categoryService.IsInUseAsync(id, cancellationToken))
        {
            return Problem(
                detail: "This category is in use and cannot be deleted.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        await _categoryService.DeleteAsync(category, cancellationToken);

        return NoContent();
    }

    private async Task<IActionResult> NotFoundUnlessSystemDefaultAsync(
        int id,
        CancellationToken cancellationToken
    )
    {
        if (await _categoryService.IsDefaultAsync(id, cancellationToken))
        {
            return Forbid();
        }

        return NotFound();
    }
}
