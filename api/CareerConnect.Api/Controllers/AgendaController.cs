using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Route("api/agenda")]
[Authorize]
public class AgendaController(IAgendaService agenda) : ApiControllerBase
{
    /// <summary>Interviews coming up and applications that have gone quiet.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AgendaResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await agenda.GetAsync(UserId, cancellationToken));
}
