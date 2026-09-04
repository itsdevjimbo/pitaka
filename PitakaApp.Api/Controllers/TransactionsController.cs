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
public class TransactionsController : ControllerBase
{
    private readonly AccountService _accountService;
    private readonly TransactionService _transactionService;

    private readonly TagService _tagService;

    private readonly VerifyTransactionCategory _verifyTransactionCategory;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public TransactionsController(
        AccountService accountService,
        TransactionService transactionService,
        TagService tagService,
        VerifyTransactionCategory verifyTransactionCategory,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _accountService = accountService;
        _transactionService = transactionService;
        _tagService = tagService;
        _verifyTransactionCategory = verifyTransactionCategory;
        _currentUserAccessor = currentUserAccessor;
    }

    // Income transactions file under Income categories, Expense under Expense. A Transfer
    // never reaches here: it cannot carry a category (#63), refused before this point on
    // both write paths.
    private static CategoryType ExpectedCategoryType(TransactionType type) => type switch
    {
        TransactionType.Income => CategoryType.Income,
        TransactionType.Expense => CategoryType.Expense,
        _ => throw new InvalidOperationException($"{type} has no matching CategoryType."),
    };

    // Maps VerifyTransactionCategory's verdict to the 400 to send, or null when the category
    // is acceptable. The existence wording is copied verbatim from the other write
    // rejections — the same failure should not read two ways across endpoints.
    private IActionResult? RejectTransactionCategory(TransactionCategoryVerdict verdict) => verdict switch
    {
        TransactionCategoryVerdict.NotFound =>
            Problem(detail: "Category does not exist", statusCode: StatusCodes.Status400BadRequest),
        TransactionCategoryVerdict.TypeMismatch => Problem(
            detail: "A transaction's category must be of the same type as the transaction.",
            statusCode: StatusCodes.Status400BadRequest
        ),
        _ => null,
    };

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] TransactionQueryRequest request)
    {
        var user = _currentUserAccessor.User!;
        var query = request.ToInput();
        var (items, totalCount) = await _transactionService.GetPageForUser(user, query);

        return Ok(TransactionPageResource.From(items, query.Page, query.PageSize, totalCount));
    }

    [HttpPost]
    public async Task<IActionResult> Post(CreateTransactionRequest request)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetByIdForUser(user, request.AccountId);

        List<Tag>? tags = null; 
        var distinctTagIds = request.TagIds?.Distinct().ToArray();
        
        if (account == null)
        {
            return Problem(detail: "Account does not exist", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!account.IsActive)
        {
            return Problem(detail: "Account is inactive", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.CategoryId is int categoryId
            && RejectTransactionCategory(
                await _verifyTransactionCategory.VerifyAsync(user, categoryId, ExpectedCategoryType(request.Type))
            ) is { } rejection)
        {
            return rejection;
        }

        if (request.Type == Enums.TransactionType.Transfer && !await _transactionService.IsValidTransferTransaction(user, request.TransferToAccountId))
        {
            return Problem(detail: "Transfer destination is not a valid account", statusCode: StatusCodes.Status400BadRequest);
        }

        if (distinctTagIds != null)
        {
            tags = await _tagService.GetByTagsIdsForUser(user, distinctTagIds);
        }

        if (tags?.Count != distinctTagIds?.Length)
        {
            return Problem(detail: "One or more tags do not exist", statusCode: StatusCodes.Status400BadRequest);
        }

        var transaction = await _transactionService.CreateAsync(account, request.ToInput(), tags);
        return StatusCode(StatusCodes.Status201Created, TransactionResource.FromModel(transaction));
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
        
        List<Tag>? tags = null; 
        var distinctTagIds = request.TagIds?.Distinct().ToArray();

        if (transaction == null)
        {
            return NotFound();
        }

        if (transaction!.UserId != user.Id)
        {
            return Forbid();
        }

        if (transaction.Type == Enums.TransactionType.Transfer && request.CategoryId != null)
        {
            ModelState.AddModelError(nameof(request.CategoryId), "A transfer cannot be assigned a category.");
            return ValidationProblem(ModelState);
        }

        // Type is read from the stored transaction, not the request: a Transaction's type is
        // immutable and UpdateTransactionRequest carries no Type, but it does carry a mutable
        // CategoryId, so a PUT can still move a row onto a mismatched category. Enforced
        // whenever a category is supplied, not only when it changes — the body describes a
        // desired end state (ADR 0003 / #67).
        if (request.CategoryId is int categoryId
            && RejectTransactionCategory(
                await _verifyTransactionCategory.VerifyAsync(user, categoryId, ExpectedCategoryType(transaction.Type))
            ) is { } rejection)
        {
            return rejection;
        }

        if (distinctTagIds != null)
        {
            tags = await _tagService.GetByTagsIdsForUser(user, distinctTagIds);
        }

        if (tags?.Count != distinctTagIds?.Length)
        {
            return Problem(detail: "One or more tags do not exist", statusCode: StatusCodes.Status400BadRequest);
        }


        await _transactionService.UpdateAsync(transaction, request.ToInput(), tags);
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

        await _transactionService.DeleteAsync(transaction);
        return NoContent();
    }
}