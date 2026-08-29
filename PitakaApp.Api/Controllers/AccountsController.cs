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
public class AccountsController : ControllerBase
{
    private readonly AccountService _accountService;
    private readonly TransactionService _transactionService;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public AccountsController(
        AccountService accountService,
        TransactionService transactionService,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _accountService = accountService;
        _transactionService = transactionService;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _currentUserAccessor.User!;
        var accounts = await _accountService.GetAllForUser(user);

        return Ok(AccountResource.Collection(accounts));
    }
    
    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountRequest request)
    {
        var user = _currentUserAccessor.User!;

        if (await _accountService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Problem(detail: "An account with this name already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var account = await _accountService.CreateAsync(user, request.ToInput());

        return StatusCode(StatusCodes.Status201Created, AccountResource.FromModel(account));
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetByIdForUser(user, id);

        if (account == null)
        {
            return NotFound();
        }

        return Ok(AccountResource.FromModel(account));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateAccountRequest request)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetTrackedByIdForUserAsync(user, id);

        if (account == null)
        {
            return NotFound();
        }

        if (await _accountService.NameExistsForUserAsync(user.Id, request.Name, excludeId: id))
        {
            return Problem(detail: "An account with this name already exists.", statusCode: StatusCodes.Status409Conflict);
        }
        
        await _accountService.UpdateAsync(account, request.ToInput());

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
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetTrackedByIdForUserAsync(user, id);

        if (account == null)
        {
            return NotFound();
        }

        if (await _accountService.HasTransactionHistoryAsync(id))
        {
            return Problem(detail: "This account has transaction history and cannot be deleted.", statusCode: StatusCodes.Status409Conflict);
        }

        if (await _accountService.HasGoalContributionsAsync(id))
        {
            return Problem(detail: "This account contains funds allocated toward a specific goal.", statusCode: StatusCodes.Status409Conflict);
        }

        await _accountService.DeleteAsync(account);
        return NoContent();
    }
    
    [HttpGet("{id}/transactions")]
    public async Task<IActionResult> GetTransactions(int id)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetByIdForUser(user, id);

        if (account == null)
        {
            return NotFound();
        }
        
        var transactions = await _transactionService.GetAllForAccount(account);
        return Ok(TransactionResource.Collection(transactions));
    }
}