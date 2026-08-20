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
public class GoalsController : ControllerBase
{
    private readonly GoalService _goalService;

    private readonly GetGoalCurrentAmount _getGoalCurrentAmount;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public GoalsController(
        GoalService goalService,
        GetGoalCurrentAmount getGoalCurrentAmount,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _goalService = goalService;
        _getGoalCurrentAmount = getGoalCurrentAmount;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _currentUserAccessor.User!;
        var goals = await _goalService.GetAllForUser(user);

        return Ok(GoalWithCurrentAmountResource.FromDtoCollection(goals));
    }
    
    [HttpPost]
    public async Task<IActionResult> Create(GoalRequest request)
    {
        var user = _currentUserAccessor.User!;

        if (await _goalService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Conflict("An goal with this name already exists.");
        }

        var goal = await _goalService.CreateAsync(user, request.ToInput());
        var currentAmount = await _getGoalCurrentAmount.GetAsync(goal);
        return StatusCode(StatusCodes.Status201Created, GoalWithCurrentAmountResource.FromModel(goal, currentAmount));
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id)
    {
        var user = _currentUserAccessor.User!;
        var goal = await _goalService.GetByIdForUser(user, id);

        if (goal == null)
        {
            return NotFound();
        }

        var currentAmount = await _getGoalCurrentAmount.GetAsync(goal);
        return Ok(GoalWithCurrentAmountResource.FromModel(goal, currentAmount));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, GoalRequest request)
    {
        var user = _currentUserAccessor.User!;
        var goal = await _goalService.GetTrackedByIdAsync(id);

        if (goal == null)
        {
            return NotFound();
        }
        
        if (goal.UserId != user.Id)
        {
            return Forbid();
        }

        if (await _goalService.NameExistsForUserAsync(user.Id, request.Name, excludeId: id))
        {
            return Conflict("A goal with this name already exists.");
        }
        
        await _goalService.UpdateAsync(goal, request.ToInput());
        var currentAmount = await _getGoalCurrentAmount.GetAsync(goal);
        return Ok(GoalWithCurrentAmountResource.FromModel(goal, currentAmount));
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> Patch(int id, GoalStatusPatchRequest request)
    {
        var user = _currentUserAccessor.User!;
        var goal = await _goalService.GetTrackedByIdAsync(id);

        if (goal == null)
        {
            return NotFound();
        }
        
        if (goal.UserId != user.Id)
        {
            return Forbid();
        }

        await _goalService.PatchStatusAsync(goal, request.Status);
        var currentAmount = await _getGoalCurrentAmount.GetAsync(goal);
        return Ok(GoalWithCurrentAmountResource.FromModel(goal, currentAmount));
    }

    [HttpDelete("{id}")] 
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        var goal = await _goalService.GetTrackedByIdAsync(id);

        if (goal == null)
        {
            return NotFound();
        }
        
        if (goal.UserId != user.Id)
        {
            return Forbid();
        }

        await _goalService.DeleteAsync(goal);
        return NoContent();
    }
}