using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Controllers;

[TypeFilter(typeof(ResolveCurrentUserFilter))]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BudgetsController : ControllerBase
{
    private readonly BudgetService _budgetService;
    private readonly VerifyCategoryExistence _verifyCategoryExistence;
    private readonly GetBudgetCycle _getBudgetCycle;
    private readonly GetBudgetAmountSpent _getBudgetAmountSpent;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public BudgetsController(
        BudgetService budgetService,
        VerifyCategoryExistence verifyCategoryExistence,
        GetBudgetCycle getBudgetCycle,
        GetBudgetAmountSpent getBudgetAmountSpent,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _budgetService = budgetService;
        _verifyCategoryExistence = verifyCategoryExistence;
        _getBudgetCycle = getBudgetCycle;
        _getBudgetAmountSpent = getBudgetAmountSpent;
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
            return Problem(detail: "A budget with this name already exists.", statusCode: StatusCodes.Status409Conflict);
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

        var (cycleStart, cycleEnd) = _getBudgetCycle.ForBudget(budget);
        var amountSpent = await _getBudgetAmountSpent.GetAsync(budget, cycleStart, cycleEnd);

        return Ok(BudgetWithSpendResource.FromModel(budget, amountSpent, cycleStart, cycleEnd));
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
            return Problem(detail: "A budget with this name already exists.", statusCode: StatusCodes.Status409Conflict);
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