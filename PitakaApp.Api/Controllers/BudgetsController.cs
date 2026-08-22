namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

[TypeFilter(typeof(ResolveCurrentUserFilter))]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BudgetsController : ControllerBase
{
    private readonly BudgetService _budgetService;
    private readonly VerifyCategoryExistence _verifyCategoryExistence;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public BudgetsController(
        BudgetService budgetService,
        VerifyCategoryExistence verifyCategoryExistence,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _budgetService = budgetService;
        _verifyCategoryExistence = verifyCategoryExistence;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _currentUserAccessor.User!;
        var budgets = await _budgetService.GetAllForUser(user);

        return Ok(BudgetResource.Collection(budgets));
    }
    
    [HttpPost]
    public async Task<IActionResult> Create(BudgetRequest request)
    {
        var user = _currentUserAccessor.User!;

        if (await _budgetService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Conflict("A budget with this name already exists.");
        }
        
        if (request.CategoryId is int categoryId  && !await _verifyCategoryExistence.VerifyAsync(user, categoryId))
        {
            return BadRequest();
        }

        var budget = await _budgetService.CreateAsync(user, request.ToInput());

        return StatusCode(StatusCodes.Status201Created, BudgetResource.FromModel(budget));
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id)
    {
        var user = _currentUserAccessor.User!;
        var budget = await _budgetService.GetByIdForUser(user, id);

        if (budget == null)
        {
            return NotFound();
        }

        return Ok(BudgetResource.FromModel(budget));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, BudgetRequest request)
    {
        var user = _currentUserAccessor.User!;
        var budget = await _budgetService.GetTrackedByIdAsync(id);

        if (budget == null)
        {
            return NotFound();
        }
        
        if (budget.UserId != user.Id)
        {
            return Forbid();
        }
        
        if (request.CategoryId is int categoryId  && !await _verifyCategoryExistence.VerifyAsync(user, categoryId))
        {
            return BadRequest();
        }

        if (await _budgetService.NameExistsForUserAsync(user.Id, request.Name, excludeId: id))
        {
            return Conflict("A budget with this name already exists.");
        }
        
        await _budgetService.UpdateAsync(budget, request.ToInput());

        return Ok(BudgetResource.FromModel(budget));
    }

    [HttpDelete("{id}")] 
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        var budget = await _budgetService.GetTrackedByIdAsync(id);

        if (budget == null)
        {
            return NotFound();
        }
        
        if (budget.UserId != user.Id)
        {
            return Forbid();
        }

        await _budgetService.DeleteAsync(budget);
        return NoContent();
    }
}