using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Controllers;

[TypeFilter(typeof(ResolveCurrentUserFilter))]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GoalContributionsController(
    GoalContributionService goalContributionService,
    GoalService goalService,
    AccountService accountService,
    CurrentUserAccessor currentUserAccessor
) : ControllerBase
{
    private readonly GoalContributionService _goalContributionService = goalContributionService;

    private readonly GoalService _goalService = goalService;

    private readonly AccountService _accountService = accountService;

    private readonly CurrentUserAccessor _currentUserAccessor = currentUserAccessor;

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

        if (request.TransactionId is not null)
        {
            return Problem(
                detail: "Linked contributions must be created through the transaction split operation",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var account = await _accountService.GetByIdForUserAsync(user, request.AccountId);
        var goal = await _goalService.GetByIdForUser(user, request.GoalId);

        if (account == null)
        {
            return Problem(
                detail: "Account does not exist",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        if (!account.IsActive)
        {
            return Problem(
                detail: "Account is inactive",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        if (goal == null)
        {
            return Problem(
                detail: "Goal does not exist",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        if (goal.IsAbandoned())
        {
            return Problem(
                detail: "Cannot make contributions to an abandoned goal",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        if (!await _goalContributionService.CanEarmarkAmount(account, request.Amount))
        {
            return Problem(
                detail: "Contributions cannot exceed the account's balance",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var goalContribution = await _goalContributionService.CreateAsync(
            goal,
            account,
            request.ToInput()
        );
        return StatusCode(
            StatusCodes.Status201Created,
            GoalContributionResource.FromModel(goalContribution)
        );
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
    public async Task<IActionResult> Update(
        int id,
        UpdateGoalContributionRequest request,
        CancellationToken cancellationToken
    )
    {
        var user = _currentUserAccessor.User!;
        var goalContribution = await _goalContributionService.GetTrackedByIdForUserAsync(
            user.Id,
            id,
            cancellationToken
        );

        if (goalContribution == null)
        {
            return NotFound();
        }

        await _goalContributionService.UpdateAsync(
            goalContribution,
            request.ToInput(),
            cancellationToken
        );

        return Ok(GoalContributionResource.FromModel(goalContribution));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        return await _goalContributionService.DeleteForUserAsync(user.Id, id)
            ? NoContent()
            : NotFound();
    }
}
