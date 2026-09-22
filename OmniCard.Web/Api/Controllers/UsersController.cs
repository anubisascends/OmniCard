using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Web.Services;
using OmniCard.Shared.Settings;
using OmniCard.Web.Api.Infrastructure;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// User management for the Administration ▸ Users tab. Every endpoint requires an admin (the built-in
/// Admin account is always an admin). Self-service password change lives on <see cref="AuthController"/>
/// so any signed-in user can reach it.
/// </summary>
[ApiAuth(RequireAdmin = true)]
public sealed class UsersController(UserService users, PermissionService permissions) : ApiControllerBase
{
    private static UserDto ToDto(User u) =>
        new(u.Id, u.Username, u.IsSystem, u.IsAdmin, u.CreatedAt, u.RoleId, u.Overrides.Grant, u.Overrides.Deny);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List()
    {
        var list = await users.ListAsync();
        return Ok(list.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserRequest request)
    {
        try
        {
            var user = await users.CreateAsync(request.Username, request.Password, request.IsAdmin,
                request.RoleId, ToOverrides(request.Grant, request.Deny));
            return ToDto(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var user = await users.UpdateUserAsync(id, request.RoleId,
                ToOverrides(request.Grant, request.Deny), request.IsAdmin);
            if (user is null)
                return NotFound(new { error = "User not found." });
            // Apply immediately: the user's next request re-resolves their permissions from the DB.
            permissions.Invalidate(id);
            return ToDto(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ok = await users.DeleteAsync(id);
        if (!ok)
            return BadRequest(new { error = "That user can't be deleted." });
        permissions.Invalidate(id);
        return NoContent();
    }

    private static PermissionOverrides ToOverrides(IReadOnlyList<string>? grant, IReadOnlyList<string>? deny) =>
        new() { Grant = grant?.ToList() ?? [], Deny = deny?.ToList() ?? [] };

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest request)
    {
        try
        {
            var ok = await users.ResetPasswordAsync(id, request.NewPassword);
            if (!ok)
                return NotFound(new { error = "User not found." });
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
