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
    private readonly VerifyBudgetCategory _verifyBudgetCategory;
    private readonly GetBudgetWithSpend _getBudgetWithSpend;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public BudgetsController(
        BudgetService budgetService,
        VerifyBudgetCategory verifyBudgetCategory,
        GetBudgetWithSpend getBudgetWithSpend,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _budgetService = budgetService;
        _verifyBudgetCategory = verifyBudgetCategory;
        _getBudgetWithSpend = getBudgetWithSpend;
        _currentUserAccessor = currentUserAccessor;
    }

    // Maps VerifyBudgetCategory's verdict to the 400 to send, or null when the category is
    // acceptable. The existence wording is copied verbatim from TransactionsController — the
    // same failure should not read two ways across endpoints.
    private IActionResult? RejectBudgetCategory(BudgetCategoryVerdict verdict) => verdict switch
    {
        BudgetCategoryVerdict.NotFound =>
            Problem(detail: "Category does not exist", statusCode: StatusCodes.Status400BadRequest),
        BudgetCategoryVerdict.NotExpense => Problem(
            detail: "A budget can only be narrowed to an expense category.",
            statusCode: StatusCodes.Status400BadRequest
        ),
        _ => null,
    };

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
        
        if (request.CategoryId is int categoryId
            && RejectBudgetCategory(await _verifyBudgetCategory.VerifyAsync(user, categoryId)) is { } rejection)
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

        if (request.CategoryId is int categoryId
            && RejectBudgetCategory(await _verifyBudgetCategory.VerifyAsync(user, categoryId)) is { } rejection)
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