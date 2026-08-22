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
public class RecurringTransactionsController : ControllerBase
{
    private readonly RecurringTransactionService _recurringTransactionService;

    private readonly VerifyCategoryExistence _verifyCategoryExistence;
    
    private readonly AccountService _accountService;

    private readonly CurrentUserAccessor _currentUserAccessor;


    public RecurringTransactionsController(
        RecurringTransactionService recurringTransactionService,
        VerifyCategoryExistence verifyCategoryExistence,
        AccountService accountService,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _recurringTransactionService = recurringTransactionService;
        _verifyCategoryExistence = verifyCategoryExistence;
        _accountService = accountService;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _currentUserAccessor.User!;
        var recurringTransactions = await _recurringTransactionService.GetAllForUser(user);

        return Ok(RecurringTransactionResource.Collection(recurringTransactions));
    }
    
    [HttpPost]
    public async Task<IActionResult> Create(CreateRecurringTransactionRequest request)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetByIdForUser(user, request.AccountId);

        if (account == null)
        {
            return BadRequest("Account doesnt exists");
        }

        if (!account.IsActive)
        {
            return BadRequest("Account is inactive");
        }
        
        if (request.CategoryId is int categoryId  && !await _verifyCategoryExistence.VerifyAsync(user, categoryId))
        {
            return BadRequest("Category doesnt exists");
        }

        if (await _recurringTransactionService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Conflict("A recurring transaction with this name already exists.");
        }

        var recurringTransaction = await _recurringTransactionService.CreateAsync(account, request.ToInput());

        return StatusCode(StatusCodes.Status201Created, RecurringTransactionResource.FromModel(recurringTransaction));
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> Show(int id)
    {
        var user = _currentUserAccessor.User!;
        var recurringTransaction = await _recurringTransactionService.GetByIdForUser(user, id);

        if (recurringTransaction == null)
        {
            return NotFound();
        }

        return Ok(RecurringTransactionResource.FromModel(recurringTransaction));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateRecurringTransactionRequest request)
    {
        var user = _currentUserAccessor.User!;
        var recurringTransaction = await _recurringTransactionService.GetTrackedByIdAsync(id);

        if (recurringTransaction == null)
        {
            return NotFound();
        }
        
        if (recurringTransaction.UserId != user.Id)
        {
            return Forbid();
        }
        
        if (request.CategoryId is int categoryId  && !await _verifyCategoryExistence.VerifyAsync(user, categoryId))
        {
            return BadRequest();
        }

        if(request.EndDate is DateOnly endDate && !recurringTransaction.CanSetEndDate(endDate))
        {
            return BadRequest("End date must be after start date");
        }

        if (await _recurringTransactionService.NameExistsForUserAsync(user.Id, request.Name, excludeId: id))
        {
            return Conflict("A recurringTransaction with this name already exists.");
        }
        
        await _recurringTransactionService.UpdateAsync(recurringTransaction, request.ToInput());

        return Ok(RecurringTransactionResource.FromModel(recurringTransaction));
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> Patch(int id, RecurringTransactionPatchRequest request)
    {
        var user = _currentUserAccessor.User!;
        var recurringTransaction = await _recurringTransactionService.GetTrackedByIdAsync(id);

        if (recurringTransaction == null)
        {
            return NotFound();
        }
        
        if (recurringTransaction.UserId != user.Id)
        {
            return Forbid();
        }

        await _recurringTransactionService.PatchStatusAsync(recurringTransaction, request.Status);
        return Ok(RecurringTransactionResource.FromModel(recurringTransaction));
    }

    [HttpDelete("{id}")] 
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        var recurringTransaction = await _recurringTransactionService.GetTrackedByIdAsync(id);

        if (recurringTransaction == null)
        {
            return NotFound();
        }
        
        if (recurringTransaction.UserId != user.Id)
        {
            return Forbid();
        }

        await _recurringTransactionService.DeleteAsync(recurringTransaction);
        return NoContent();
    }
}