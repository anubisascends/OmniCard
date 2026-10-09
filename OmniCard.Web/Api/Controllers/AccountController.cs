using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// The Account page: any signed-in user reads and edits their OWN details here (no permission needed).
/// Managing other accounts stays admin-only on <see cref="UsersController"/>. The password change lives on
/// <see cref="AuthController"/> (<c>/api/auth/change-password</c>).
/// </summary>
public sealed class AccountController(UserService users) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AccountDto>> Get()
    {
        var user = AppAuthGate.CurrentUserId(HttpContext) is int id ? await users.FindByIdAsync(id) : null;
        if (user is null)
            return Unauthorized(new { error = "Not authenticated." });
        return new AccountDto(user.Id, user.Username, user.Email, user.IsAdmin || user.IsSystem, user.CreatedAt);
    }

    [HttpPut("email")]
    public async Task<ActionResult<AccountDto>> ChangeEmail([FromBody] ChangeEmailRequest request)
    {
        if (AppAuthGate.CurrentUserId(HttpContext) is not int id)
            return Unauthorized(new { error = "Not authenticated." });

        try
        {
            if (!await users.ChangeOwnEmailAsync(id, request.CurrentPassword, request.Email))
                return BadRequest(new { error = "Current password is incorrect." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        return await Get();
    }
}
