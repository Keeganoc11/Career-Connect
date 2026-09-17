using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

public class DeleteAccountRequest
{
    /// <summary>The account's own email, typed by the user — the confirmation for something with no undo.</summary>
    [Required, EmailAddress, MaxLength(320)]
    public required string Email { get; init; }
}

/// <summary>
/// Getting your data out, and getting rid of it. Both belong to the person who
/// owns the account rather than to any one feature, so they live here rather
/// than on AuthController.
/// </summary>
[ApiController]
[Authorize]
[Route("api/account")]
public class AccountController(IAccountService account) : ApiControllerBase
{
    private static readonly JsonSerializerOptions ExportJson = new()
    {
        WriteIndented = true,
        // Matches every other response the API sends, so the file reads like
        // the API rather than like the C# behind it.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // A file a person might actually open and read, so enums are names.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Everything in the account, as a JSON file.</summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var data = await account.ExportAsync(UserId, cancellationToken);
        if (data is null)
        {
            return NotFound();
        }

        var json = JsonSerializer.Serialize(data, ExportJson);
        var name = $"career-connect-export-{DateTime.UtcNow:yyyy-MM-dd}.json";
        return File(Encoding.UTF8.GetBytes(json), "application/json", name);
    }

    /// <summary>
    /// Deletes the account and everything in it, and hands back the Google
    /// grant. There is no undo, which is why it takes the email as a body
    /// rather than being a bare DELETE.
    /// </summary>
    [HttpPost("delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(DeleteAccountRequest request, CancellationToken cancellationToken) =>
        await account.DeleteAsync(UserId, request.Email, cancellationToken) switch
        {
            DeleteAccountOutcome.Deleted => NoContent(),
            DeleteAccountOutcome.ConfirmationMismatch => BadRequest(new ProblemDetails
            {
                Title = "That isn't the email address on this account.",
                Status = StatusCodes.Status400BadRequest,
            }),
            _ => NotFound(),
        };
}
