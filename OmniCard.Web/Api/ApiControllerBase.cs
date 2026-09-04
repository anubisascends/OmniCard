using Microsoft.AspNetCore.Mvc;

namespace OmniCard.Web.Api;

/// <summary>
/// Base class for the JSON API controllers of the SPA: routes under <c>/api/[controller]</c> and
/// applies the site-wide passphrase gate. <see cref="AuthController"/> deliberately does NOT derive
/// from this (its login/status endpoints must stay reachable while locked).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[ApiAuth]
public abstract class ApiControllerBase : ControllerBase
{
}
