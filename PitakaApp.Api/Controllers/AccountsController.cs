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
public class AccountsController(
    AccountService accountService,
    TransactionService transactionService,
    CurrentUserAccessor currentUserAccessor
) : ControllerBase
{
    private readonly AccountService _accountService = accountService;
    private readonly TransactionService _transactionService = transactionService;
    private readonly CurrentUserAccessor _currentUserAccessor = currentUserAccessor;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] AccountQueryRequest request)
    {
        var user = _currentUserAccessor.User!;
        var accounts = await _accountService.GetAllForUser(user, request.ToInput());

        return Ok(AccountResource.Collection(accounts));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateAccountRequest request,
        CancellationToken cancellationToken
    )
    {
        var user = _currentUserAccessor.User!;

        if (
            await _accountService.NameExistsForUserAsync(
                user.Id,
                request.Name,
                cancellationToken: cancellationToken
            )
        )
        {
            return Problem(
                detail: "An account with this name already exists.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        var account = await _accountService.CreateAsync(user, request.ToInput(), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, AccountResource.FromModel(account));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id, CancellationToken cancellationToken)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetByIdForUserAsync(user, id, cancellationToken);

        if (account == null)
        {
            return NotFound();
        }

        return Ok(AccountResource.FromModel(account));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        int id,
        UpdateAccountRequest request,
        CancellationToken cancellationToken
    )
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetTrackedByIdForUserAsync(user, id, cancellationToken);

        if (account == null)
        {
            return NotFound();
        }

        if (
            await _accountService.NameExistsForUserAsync(
                user.Id,
                request.Name,
                excludeId: id,
                cancellationToken: cancellationToken
            )
        )
        {
            return Problem(
                detail: "An account with this name already exists.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        await _accountService.UpdateAsync(account, request.ToInput(), cancellationToken);
        return Ok(AccountResource.FromModel(account));
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> Patch(int id, PatchAccountActiveStatusRequest request)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetTrackedByIdForUserAsync(user, id);

        if (account == null)
        {
            return NotFound();
        }

        await _accountService.PatchActiveStatus(account, request.ToInput());
        return Ok(AccountResource.FromModel(account));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var user = _currentUserAccessor.User!;
        var result = await _accountService.DeleteAsync(user.Id, id, cancellationToken);

        return result switch
        {
            AccountDeletionResult.Deleted => NoContent(),
            AccountDeletionResult.NotFound => NotFound(),
            AccountDeletionResult.HasTransactionHistory => Problem(
                detail: "This account has transaction history and cannot be deleted.",
                statusCode: StatusCodes.Status409Conflict
            ),
            AccountDeletionResult.HasGoalContributions => Problem(
                detail: "This account contains funds allocated toward a specific goal.",
                statusCode: StatusCodes.Status409Conflict
            ),
            AccountDeletionResult.HasGeneratedRecurringTransactions => Problem(
                detail: "This account has recurring transactions with generated history and cannot be deleted.",
                statusCode: StatusCodes.Status409Conflict
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        };
    }

    [HttpGet("{id}/transactions")]
    public async Task<IActionResult> GetTransactions(int id, CancellationToken cancellationToken)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetByIdForUserAsync(user, id, cancellationToken);

        if (account == null)
        {
            return NotFound();
        }

        var transactions = await _transactionService.GetAllForAccountAsync(
            account,
            cancellationToken
        );
        return Ok(TransactionResource.Collection(transactions));
    }
}
