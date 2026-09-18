// Filters/ResolveCurrentUserFilter.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Filters;

public class ResolveCurrentUserFilter(
    GetCurrentUser getCurrentUser,
    CurrentUserAccessor currentUserAccessor
) : IAsyncActionFilter
{
    private readonly GetCurrentUser _getCurrentUser = getCurrentUser;
    private readonly CurrentUserAccessor _currentUserAccessor = currentUserAccessor;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next
    )
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
