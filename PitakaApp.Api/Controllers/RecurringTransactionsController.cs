using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Controllers;

[TypeFilter(typeof(ResolveCurrentUserFilter))]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RecurringTransactionsController : ControllerBase
{
    private readonly RecurringTransactionService _recurringTransactionService;

    private readonly VerifyTransactionCategory _verifyTransactionCategory;

    private readonly AccountService _accountService;

    private readonly CurrentUserAccessor _currentUserAccessor;


    public RecurringTransactionsController(
        RecurringTransactionService recurringTransactionService,
        VerifyTransactionCategory verifyTransactionCategory,
        AccountService accountService,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _recurringTransactionService = recurringTransactionService;
        _verifyTransactionCategory = verifyTransactionCategory;
        _accountService = accountService;
        _currentUserAccessor = currentUserAccessor;
    }

    // A RecurringTransaction's type maps 1:1 to a CategoryType — no Transfer case to carve
    // out — so an Income recurring transaction files under an Income category and an Expense
    // one under an Expense category.
    private static CategoryType ExpectedCategoryType(RecurringTransactionType type) => type switch
    {
        RecurringTransactionType.Income => CategoryType.Income,
        RecurringTransactionType.Expense => CategoryType.Expense,
        _ => throw new InvalidOperationException($"{type} has no matching CategoryType."),
    };

    // Maps VerifyTransactionCategory's verdict to the 400 to send, or null when the category
    // is acceptable. The existence wording is copied verbatim from TransactionsController;
    // the mismatch wording says "recurring transaction" but is otherwise the same shape.
    private IActionResult? RejectRecurringTransactionCategory(TransactionCategoryVerdict verdict) => verdict switch
    {
        TransactionCategoryVerdict.NotFound =>
            Problem(detail: "Category does not exist", statusCode: StatusCodes.Status400BadRequest),
        TransactionCategoryVerdict.TypeMismatch => Problem(
            detail: "A recurring transaction's category must be of the same type as the transaction.",
            statusCode: StatusCodes.Status400BadRequest
        ),
        _ => null,
    };

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
            return Problem(detail: "Account does not exist", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!account.IsActive)
        {
            return Problem(detail: "Account is inactive", statusCode: StatusCodes.Status400BadRequest);
        }
        
        if (request.CategoryId is int categoryId
            && RejectRecurringTransactionCategory(
                await _verifyTransactionCategory.VerifyAsync(user, categoryId, ExpectedCategoryType(request.Type))
            ) is { } rejection)
        {
            return rejection;
        }

        if (await _recurringTransactionService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Problem(detail: "A recurring transaction with this name already exists.", statusCode: StatusCodes.Status409Conflict);
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
        
        // Type is read from the stored row: UpdateRecurringTransactionRequest carries no
        // Type, but its CategoryId is mutable, so a PUT can still move the row onto a
        // mismatched category. Enforced whenever a category is supplied (ADR 0003 / #67).
        if (request.CategoryId is int categoryId
            && RejectRecurringTransactionCategory(
                await _verifyTransactionCategory.VerifyAsync(user, categoryId, ExpectedCategoryType(recurringTransaction.Type))
            ) is { } rejection)
        {
            return rejection;
        }

        if(request.EndDate is DateOnly endDate && !recurringTransaction.CanSetEndDate(endDate))
        {
            return Problem(detail: "End date must be after start date", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await _recurringTransactionService.NameExistsForUserAsync(user.Id, request.Name, excludeId: id))
        {
            return Problem(detail: "A recurringTransaction with this name already exists.", statusCode: StatusCodes.Status409Conflict);
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