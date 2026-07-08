// OmniCard.Web/Api/ScanController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OmniCard.Web.Hubs;

namespace OmniCard.Web.Api;

[ApiController]
[Route("api/[controller]")]
public class ScanController : ControllerBase
{
    private readonly IHubContext<ScanHub> _hubContext;
    private readonly ILogger<ScanController> _logger;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png"
    };

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public ScanController(IHubContext<ScanHub> hubContext, ILogger<ScanController> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Upload(IFormFile image)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "No image provided" });

        if (!AllowedContentTypes.Contains(image.ContentType))
            return BadRequest(new { error = "Only JPEG and PNG images are accepted" });

        if (image.Length > MaxFileSize)
            return BadRequest(new { error = "Image exceeds 10 MB limit" });

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms);
        var imageData = ms.ToArray();

        _logger.LogInformation("Received scan image: {Size} bytes, {ContentType}", imageData.Length, image.ContentType);

        await _hubContext.Clients.All.SendAsync("ImageReceived", imageData);

        return Ok(new { status = "ok", size = imageData.Length });
    }
}
