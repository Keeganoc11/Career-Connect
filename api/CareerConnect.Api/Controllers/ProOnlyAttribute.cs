using System.Security.Claims;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CareerConnect.Api.Controllers;

/// <summary>
/// Refuses the request unless the caller is on Pro. Hiding a button in the
/// client is a courtesy; this is the part that actually decides, so every
/// endpoint that spends model credits or runs automation carries it.
///
/// Answers 402 Payment Required rather than 403: the caller is who they say
/// they are and nothing is wrong with the request — they just haven't paid for
/// this feature. The client keys its upgrade prompt off that status, which
/// keeps a genuine 403 meaning what it always meant.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ProOnlyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string Message = "That's a Pro feature. Upgrade to use it.";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var idClaim = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.HttpContext.User.FindFirstValue("sub");

        // [Authorize] runs first and would already have rejected an anonymous
        // caller, so a missing or unparsable id here means something stranger
        // than an expired token — refuse rather than guess.
        if (!Guid.TryParse(idClaim, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var plans = context.HttpContext.RequestServices.GetRequiredService<IPlanService>();
        if (await plans.IsProAsync(userId, context.HttpContext.RequestAborted))
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Title = Message,
            Status = StatusCodes.Status402PaymentRequired,
        })
        {
            StatusCode = StatusCodes.Status402PaymentRequired,
        };
    }
}
