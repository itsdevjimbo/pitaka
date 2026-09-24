using System.ComponentModel.DataAnnotations;
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
public class TransactionsController(
    AccountService accountService,
    TransactionService transactionService,
    LinkedContributionReadService linkedContributionReadService,
    LinkedContributionSplitService linkedContributionSplitService,
    TagService tagService,
    VerifyTransactionCategory verifyTransactionCategory,
    CurrentUserAccessor currentUserAccessor
) : ControllerBase
{
    private readonly AccountService _accountService = accountService;
    private readonly TransactionService _transactionService = transactionService;
    private readonly LinkedContributionReadService _linkedContributionReadService =
        linkedContributionReadService;
    private readonly LinkedContributionSplitService _linkedContributionSplitService =
        linkedContributionSplitService;

    private readonly TagService _tagService = tagService;

    private readonly VerifyTransactionCategory _verifyTransactionCategory =
        verifyTransactionCategory;
    private readonly CurrentUserAccessor _currentUserAccessor = currentUserAccessor;

    // Income transactions file under Income categories, Expense under Expense. A Transfer
    // never reaches here: it cannot carry a category (#63), refused before this point on
    // both write paths.
    private static CategoryType ExpectedCategoryType(TransactionType type) =>
        type switch
        {
            TransactionType.Income => CategoryType.Income,
            TransactionType.Expense => CategoryType.Expense,
            _ => throw new InvalidOperationException($"{type} has no matching CategoryType."),
        };

    // Maps VerifyTransactionCategory's verdict to the 400 to send, or null when the category
    // is acceptable. The existence wording is copied verbatim from the other write
    // rejections — the same failure should not read two ways across endpoints.
    private IActionResult? RejectTransactionCategory(TransactionCategoryVerdict verdict) =>
        verdict switch
        {
            TransactionCategoryVerdict.NotFound => Problem(
                detail: "Category does not exist",
                statusCode: StatusCodes.Status400BadRequest
            ),
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

        if (
            request.CategoryId is int categoryId
            && RejectTransactionCategory(
                await _verifyTransactionCategory.VerifyAsync(
                    user,
                    categoryId,
                    ExpectedCategoryType(request.Type)
                )
            )
                is { } rejection
        )
        {
            return rejection;
        }

        if (
            request.Type == Enums.TransactionType.Transfer
            && !await _transactionService.IsValidTransferTransaction(
                user,
                request.TransferToAccountId
            )
        )
        {
            return Problem(
                detail: "Transfer destination is not a valid account",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        if (distinctTagIds != null)
        {
            tags = await _tagService.GetByTagsIdsForUser(user, distinctTagIds);
        }

        if (tags?.Count != distinctTagIds?.Length)
        {
            return Problem(
                detail: "One or more tags do not exist",
                statusCode: StatusCodes.Status400BadRequest
            );
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

    [HttpGet("{transactionId}/linked-contributions")]
    public async Task<IActionResult> GetLinkedContributions(int transactionId)
    {
        var user = _currentUserAccessor.User!;
        var snapshot = await _linkedContributionReadService.GetForTransactionAsync(
            user.Id,
            transactionId
        );

        if (snapshot is null)
        {
            return NotFound();
        }

        return Ok(LinkedContributionSnapshotResource.FromSnapshot(snapshot));
    }

    [HttpPost("{transactionId}/linked-contributions")]
    [ProducesResponseType(typeof(SplitSuccessSnapshot), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(LinkedContributionSplitConflictProblemDetails),
        StatusCodes.Status409Conflict
    )]
    [ProducesResponseType(
        typeof(LinkedContributionSplitOutcomeUnknownProblemDetails),
        StatusCodes.Status503ServiceUnavailable
    )]
    public async Task<IActionResult> CreateLinkedContributions(
        int transactionId,
        [FromHeader(Name = "Idempotency-Key"), Required] Guid? idempotencyKey,
        CreateLinkedContributionSplitRequest request,
        CancellationToken cancellationToken
    )
    {
        var user = _currentUserAccessor.User!;
        var result = await _linkedContributionSplitService.ExecuteAsync(
            user.Id,
            transactionId,
            idempotencyKey!.Value,
            request.ToInput(),
            cancellationToken
        );

        return result switch
        {
            SplitSucceeded success => new ContentResult
            {
                StatusCode = success.StatusCode,
                ContentType = "application/json",
                Content = success.ResponseBody,
            },
            SplitInvalid invalid => ValidationProblem(
                new ValidationProblemDetails(
                    invalid.Errors.ToDictionary(error => error.Key, error => error.Value)
                )
            ),
            SplitRefused refusal => SplitConflict(refusal),
            SplitIdempotencyMismatch mismatch => Problem(
                detail: "This Idempotency-Key was already used with a different request.",
                statusCode: mismatch.StatusCode,
                extensions: new Dictionary<string, object?>
                {
                    ["reason"] = mismatch.Reason,
                    ["key"] = mismatch.Key,
                }
            ),
            SplitOutcomeUnknown unknown => Problem(
                detail: "The operation outcome could not be established. Retry explicitly with the same key and unchanged request.",
                statusCode: unknown.StatusCode,
                extensions: new Dictionary<string, object?> { ["reason"] = unknown.Reason }
            ),
            _ => throw new InvalidOperationException($"Unknown split result: {result}"),
        };
    }

    private ObjectResult SplitConflict(SplitRefused refusal) =>
        Problem(
            detail: "No Linked Contributions were created.",
            statusCode: refusal.StatusCode,
            extensions: new Dictionary<string, object?>
            {
                ["reason"] = refusal.Reason,
                ["created"] = refusal.Created,
                ["failures"] = refusal
                    .Failures.Select(LinkedContributionSplitFailureResource.FromFailure)
                    .ToArray(),
            }
        );

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        int id,
        UpdateTransactionRequest request,
        CancellationToken cancellationToken
    )
    {
        var user = _currentUserAccessor.User!;
        var transaction = await _transactionService.GetTrackedByIdForUserAsync(
            user.Id,
            id,
            cancellationToken
        );

        List<Tag>? tags = null;
        var distinctTagIds = request.TagIds?.Distinct().ToArray();

        if (transaction == null)
        {
            return NotFound();
        }

        if (transaction.Type == Enums.TransactionType.Transfer && request.CategoryId != null)
        {
            ModelState.AddModelError(
                nameof(request.CategoryId),
                "A transfer cannot be assigned a category."
            );
            return ValidationProblem(ModelState);
        }

        // Type is read from the stored transaction, not the request: a Transaction's type is
        // immutable and UpdateTransactionRequest carries no Type, but it does carry a mutable
        // CategoryId, so a PUT can still move a row onto a mismatched category. Enforced
        // whenever a category is supplied, not only when it changes — the body describes a
        // desired end state (ADR 0003 / #67).
        if (
            request.CategoryId is int categoryId
            && RejectTransactionCategory(
                await _verifyTransactionCategory.VerifyAsync(
                    user,
                    categoryId,
                    ExpectedCategoryType(transaction.Type),
                    cancellationToken
                )
            )
                is { } rejection
        )
        {
            return rejection;
        }

        if (distinctTagIds != null)
        {
            tags = await _tagService.GetByTagsIdsForUser(user, distinctTagIds, cancellationToken);
        }

        if (tags?.Count != distinctTagIds?.Length)
        {
            return Problem(
                detail: "One or more tags do not exist",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        await _transactionService.UpdateAsync(
            transaction,
            request.ToInput(),
            tags,
            cancellationToken
        );
        return Ok(TransactionResource.FromModel(transaction));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var user = _currentUserAccessor.User!;
        var transaction = await _transactionService.GetTrackedByIdForUserAsync(
            user.Id,
            id,
            cancellationToken
        );

        if (transaction == null)
        {
            return NotFound();
        }

        var result = await _transactionService.DeleteAsync(transaction, cancellationToken);

        return result switch
        {
            TransactionDeleted => NoContent(),
            TransactionHasLinkedContributions conflict => Problem(
                detail: "Remove every Linked Contribution before deleting this Transaction.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["reason"] = "transaction_has_linked_contributions",
                    ["transactionId"] = conflict.TransactionId,
                    ["linkedContributions"] = conflict.LinkedContributions,
                }
            ),
            _ => throw new InvalidOperationException($"Unknown deletion result: {result}"),
        };
    }
}
