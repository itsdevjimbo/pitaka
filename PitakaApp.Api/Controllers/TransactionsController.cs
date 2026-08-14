namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

[TypeFilter(typeof(ResolveCurrentUserFilter))]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TransactionsController : ControllerBase
{
    private readonly AccountService _accountService;
    private readonly TransactionService _transactionService;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public TransactionsController(
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
        var transactions = await _transactionService.GetAllForUser(user);

        return Ok(TransactionResource.Collection(transactions));
    }

    [HttpPost]
    public async Task<IActionResult> Post(CreateTransactionRequest request)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetByIdForUser(user, request.AccountId);

        if (account == null)
        {
            return BadRequest();
        }
        
        if (!await _transactionService.VerifyCategoryExistence(user, request.CategoryId))
        {
            return BadRequest();
        }

        if (!await _transactionService.IsValidTransferTransaction(user, request.Type, request.TransferToAccountId))
        {
            return BadRequest();
        }

        try
        {
            var transaction = await _transactionService.CreateAsync(request.ToInput(account));
            return StatusCode(StatusCodes.Status201Created, TransactionResource.FromModel(transaction));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("This account was updated by another request. Please try again.");
        }

    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id)
    {
        var user = _currentUserAccessor.User!;
        var transaction = await _transactionService.GetByIdForUser(user, id);

        if (transaction == null)
        {
            return NotFound();
        }

        return Ok(TransactionResource.FromModel(transaction));
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateTransactionRequest request)
    {
        var user = _currentUserAccessor.User!;
        var transaction = await _transactionService.GetTrackedByIdAsync(id);

        if (transaction == null)
        {
            return NotFound();
        }

        if (transaction!.UserId != user.Id)
        {
            return Forbid();
        }
        
        if (!await _transactionService.VerifyCategoryExistence(user, request.CategoryId))
        {
            return BadRequest();
        }

        await _transactionService.UpdateAsync(transaction, request.ToInput());
        return Ok(TransactionResource.FromModel(transaction));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        var transaction = await _transactionService.GetTrackedByIdAsync(id);

        if (transaction == null)
        {
            return NotFound();
        }

        if (transaction!.UserId != user.Id)
        {
            return Forbid();
        }

        try
        {
            await _transactionService.DeleteAsync(transaction);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("This account was updated by another request. Please try again.");
        }
    }
}