using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Models;
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
    private readonly CategoryService _categoryService;
    private readonly GetBudgetWithSpend _getBudgetWithSpend;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public BudgetsController(
        BudgetService budgetService,
        CategoryService categoryService,
        GetBudgetWithSpend getBudgetWithSpend,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _budgetService = budgetService;
        _categoryService = categoryService;
        _getBudgetWithSpend = getBudgetWithSpend;
        _currentUserAccessor = currentUserAccessor;
    }

    // A Budget's Category, when present, must be an expense category: only expenses count
    // against a Budget, so an income-narrowed Budget reports zero spent forever. Same
    // visibility rule as elsewhere (own or system default); returns the rejection to send,
    // or null when the category is acceptable.
    private async Task<IActionResult?> RejectNonExpenseCategory(User user, int categoryId)
    {
        var category = await _categoryService.GetByIdForUser(user, categoryId);

        if (category == null)
        {
            return Problem(detail: "Category does not exist", statusCode: StatusCodes.Status400BadRequest);
        }

        if (category.Type != CategoryType.Expense)
        {
            return Problem(
                detail: "A budget can only be narrowed to an expense category.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        return null;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _currentUserAccessor.User!;
        var budgets = await _budgetService.GetAllForUser(user);

        // Per-Budget aggregate: each Budget has its own window and category, so the sum stops
        // being one uniform Where. GetBudgetWithSpend enriches one Budget and is the same
        // collaborator Show uses, so AmountSpent is identical read from either endpoint. Round
        // trips are bounded by the number of Budgets; each sum runs in the database.
        var resources = new List<BudgetWithSpendResource>(budgets.Count);
        foreach (var budget in budgets)
        {
            resources.Add(await _getBudgetWithSpend.ForBudgetAsync(budget));
        }

        return Ok(resources);
    }
    
    [HttpPost]
    public async Task<IActionResult> Create(BudgetRequest request)
    {
        var user = _currentUserAccessor.User!;

        if (await _budgetService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Problem(detail: "A budget with this name already exists.", statusCode: StatusCodes.Status409Conflict);
        }
        
        if (request.CategoryId is int categoryId && await RejectNonExpenseCategory(user, categoryId) is { } rejection)
        {
            return rejection;
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

        return Ok(await _getBudgetWithSpend.ForBudgetAsync(budget));
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
        
        if (await _budgetService.NameExistsForUserAsync(user.Id, request.Name, excludeId: id))
        {
            return Problem(detail: "A budget with this name already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        if (request.CategoryId is int categoryId && await RejectNonExpenseCategory(user, categoryId) is { } rejection)
        {
            return rejection;
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