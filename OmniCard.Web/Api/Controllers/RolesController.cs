using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Settings;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api.Controllers;

/// <summary>
/// Role management for the Administration ▸ Roles tab. Admin-only (the built-in Admin account is
/// always an admin). Roles are reusable permission bundles assigned to users; system roles
/// (Administrator/Viewer/Staff) can't be deleted. Any change invalidates the permission cache so it
/// takes effect on affected users' next request.
/// </summary>
[ApiAuth(RequireAdmin = true)]
public sealed class RolesController(UserService users, PermissionService permissions) : ApiControllerBase
{
    private static RoleDto ToDto(Role r) => new(r.Id, r.Name, r.IsSystem, r.Permissions);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List()
    {
        var list = await users.ListRolesAsync();
        return Ok(list.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create([FromBody] SaveRoleRequest request)
    {
        try
        {
            var role = await users.CreateRoleAsync(request.Name, request.Permissions);
            return ToDto(role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RoleDto>> Update(int id, [FromBody] SaveRoleRequest request)
    {
        try
        {
            var role = await users.UpdateRoleAsync(id, request.Name, request.Permissions);
            if (role is null)
                return NotFound(new { error = "Role not found." });
            // A role is shared by many users — clear the whole cache so everyone re-resolves.
            permissions.InvalidateAll();
            return ToDto(role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ok = await users.DeleteRoleAsync(id);
        if (!ok)
            return BadRequest(new { error = "That role can't be deleted." });
        permissions.InvalidateAll();
        return NoContent();
    }
}
