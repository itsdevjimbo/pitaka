namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly AccountService _accountService;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public AccountsController(
        AccountService accountService,
        CurrentUserAccessor currentUserAccessor
    )
    {
        _accountService = accountService;
        _currentUserAccessor = currentUserAccessor;
    }
    
    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = _currentUserAccessor.User!;
        var accounts = await _accountService.GetAllForUser(user);

        return Ok(AccountResource.Collection(accounts));
    }
    
    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountRequest request)
    {
        var user = _currentUserAccessor.User!;

        if (await _accountService.NameExistsForUserAsync(user.Id, request.Name))
        {
            return Conflict("An account with this name already exists.");
        }

        var account = await _accountService.CreateAsync(request.ToInput(user));

        return StatusCode(StatusCodes.Status201Created, AccountResource.FromModel(account));
    }
    
    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [Authorize]
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


    
    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateAccountRequest request)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetTrackedByIdAsync(id);

        if (account == null)
        {
            return NotFound();
        }
        
        if (account.UserId != user.Id)
        {
            return Forbid();
        }

        if (await _accountService.NameExistsForUserAsync(user.Id, request.Name, excludeId: id))
        {
            return Conflict("An account with this name already exists.");
        }
        
        account = await _accountService.UpdateAsync(account, request.ToInput());

        return Ok(AccountResource.FromModel(account));
    }


    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [Authorize]
    [HttpDelete("{id}")] 
    public async Task<IActionResult> Delete(int id)
    {
        var user = _currentUserAccessor.User!;
        var account = await _accountService.GetTrackedByIdAsync(id);

        if (account == null)
        {
            return NotFound();
        }
        
        if (account.UserId != user.Id)
        {
            return Forbid();
        }

        await _accountService.DeleteAsync(account);
        return NoContent();
    }
}