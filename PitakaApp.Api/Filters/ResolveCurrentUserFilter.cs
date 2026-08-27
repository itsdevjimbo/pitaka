// Filters/ResolveCurrentUserFilter.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Filters;

public class ResolveCurrentUserFilter : IAsyncActionFilter
{
    private readonly GetCurrentUser _getCurrentUser;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public ResolveCurrentUserFilter(GetCurrentUser getCurrentUser, CurrentUserAccessor currentUserAccessor)
    {
        _getCurrentUser = getCurrentUser;
        _currentUserAccessor = currentUserAccessor;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = await _getCurrentUser.ExecuteAsync(context.HttpContext.User);

        if (user == null)
        {
            context.Result = new UnauthorizedResult();
            return; 
        }

        _currentUserAccessor.User = user;
        await next();
    }
}