namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Models;
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

    private readonly TagService _tagService;

    private readonly VerifyCategoryExistence _verifyCategoryExistence;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public TransactionsController(
        AccountService accountService,
        TransactionService transactionService,
        TagService tagService,
        VerifyCategoryExistence verifyCategoryExistence,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _accountService = accountService;
        _transactionService = transactionService;
        _tagService = tagService;
        _verifyCategoryExistence = verifyCategoryExistence;
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

        List<Tag>? tags = null; 
        var distictTagIds = request.TagIds?.Distinct().ToArray();
        
        if (account == null)
        {
            return BadRequest();
        }

        if (!account.IsActive)
        {
            return BadRequest();
        }
        
        if (request.CategoryId is int categoryId  && !await _verifyCategoryExistence.VerifyAsync(user, categoryId))
        {
            return BadRequest();
        }

        if (request.Type == Enums.TransactionType.Transfer && !await _transactionService.IsValidTransferTransaction(user, request.TransferToAccountId))
        {
            return BadRequest();
        }

        if (distictTagIds != null)
        {
            tags = await _tagService.GetByTagsIdsForUser(user, distictTagIds);
        }

        if (tags?.Count != distictTagIds?.Length)
        {
            return BadRequest();
        }

        try
        {
            var transaction = await _transactionService.CreateAsync(account, request.ToInput(), tags);
            return StatusCode(StatusCodes.Status201Created, TransactionResource.FromModel(transaction));
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
        var distictTagIds = request.TagIds?.Distinct().ToArray();

        if (transaction == null)
        {
            return NotFound();
        }

        if (transaction!.UserId != user.Id)
        {
            return Forbid();
        }
        
        if (request.CategoryId is int categoryId  && !await _verifyCategoryExistence.VerifyAsync(user, categoryId))
        {
            return BadRequest();
        }

        if (distictTagIds != null)
        {
            tags = await _tagService.GetByTagsIdsForUser(user, distictTagIds);
        }

        if (tags?.Count != distictTagIds?.Length)
        {
            return BadRequest();
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

        try
        {
            await _transactionService.DeleteAsync(transaction);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(detail: "This account was updated by another request. Please try again.", statusCode: StatusCodes.Status409Conflict);
        }
    }
}