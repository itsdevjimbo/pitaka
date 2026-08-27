using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Controllers;

[TypeFilter(typeof(ResolveCurrentUserFilter))]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GoalContributionsController : ControllerBase
{
    private readonly GoalContributionService _goalContributionService;

    private readonly GoalService _goalService;

    private readonly AccountService _accountService;

    private readonly CurrentUserAccessor _currentUserAccessor;

    public GoalContributionsController(
        GoalContributionService goalContributionService,
        GoalService goalService,
        AccountService accountService,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _goalContributionService = goalContributionService;
        _goalService = goalService;
        _accountService = accountService;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _currentUserAccessor.User!;
        var goalContributions = await _goalContributionService.GetAllForUser(user);

        return Ok(GoalContributionResource.Collection(goalContributions));
    }
    
    [HttpPost]
    public async Task<IActionResult> Create(CreateGoalContributionRequest request)
    {
        var user = _currentUserAccessor.User!;
        var goal = await _goalService.GetByIdForUser(user, request.GoalId);
        var account = await _accountService.GetByIdForUser(user, request.AccountId);

        if (account == null)
        {
            return Problem(detail: "Account does not exist", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!account.IsActive)
        {
            return Problem(detail: "Account is inactive", statusCode: StatusCodes.Status400BadRequest);
        }

        if (goal == null)
        {
            return Problem(detail: "Goal does not exist", statusCode: StatusCodes.Status400BadRequest);
        }

        if (goal.IsAbandoned())
        {
            return Problem(detail: "Cannot make contributions to an abandoned goal", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await _goalContributionService.CanEarmarkTransaction(account.Id, request.TransactionId))
        {
            return Problem(detail: "Cannot make a contribution based on this transaction", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await _goalContributionService.CanEarmarkAmount(account, request.Amount))
        {
            return Problem(detail: "Contributions cannot exceed the account's balance", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var goalContribution = await _goalContributionService.CreateAsync(goal, account, request.ToInput());
            return StatusCode(StatusCodes.Status201Created, GoalContributionResource.FromModel(goalContribution));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(detail: "This account was updated by another request. Please try again.", statusCode: StatusCodes.Status409Conflict);
        }
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id)
    {
        var user = _currentUserAccessor.User!;
        var goalContribution = await _goalContributionService.GetByIdForUser(user, id);

        if (goalContribution == null)
        {
            return NotFound();
        }

        return Ok(GoalContributionResource.FromModel(goalContribution));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateGoalContributionRequest request)
    {
        var user = _currentUserAccessor.User!;
        var goalContribution = await _goalContributionService.GetTrackedByIdAsync(id);

        if (goalContribution == null)
        {
            return NotFound();
        }
        
        if (goalContribution.Goal.UserId != user.Id)
        {
            return Forbid();
        }
        
        await _goalContributionService.UpdateAsync(goalContribution, request.ToInput());

        return Ok(GoalContributionResource.FromModel(goalContribution));
    }

    [HttpDelete("{id}")] 
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        var goalContribution = await _goalContributionService.GetTrackedByIdAsync(id);

        if (goalContribution == null)
        {
            return NotFound();
        }
        
        if (goalContribution.Goal.UserId != user.Id)
        {
            return Forbid();
        }

        await _goalContributionService.DeleteAsync(goalContribution);
        return NoContent();
    }
}