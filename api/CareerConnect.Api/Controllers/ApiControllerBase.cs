using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The authenticated caller's id, taken from the JWT subject claim.</summary>
    protected Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Authenticated user has no id claim."));
}
