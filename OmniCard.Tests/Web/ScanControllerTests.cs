using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Web.Api;
using OmniCard.Web.Hubs;
using Microsoft.AspNetCore.Mvc;

namespace OmniCard.Tests.Web;

public class ScanControllerTests
{
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly ScanController _controller;

    public ScanControllerTests()
    {
        _mockClientProxy = new Mock<IClientProxy>();

        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(_mockClientProxy.Object);

        var mockHubContext = new Mock<IHubContext<ScanHub>>();
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        _controller = new ScanController(
            mockHubContext.Object,
            NullLogger<ScanController>.Instance);
    }

    private static IFormFile CreateFormFile(
        byte[]? content = null,
        string contentType = "image/jpeg",
        string fileName = "test.jpg",
        long? overrideLength = null)
    {
        content ??= [0xFF, 0xD8, 0xFF, 0xE0]; // JPEG magic bytes
        var stream = new MemoryStream(content);
        var file = new FormFile(stream, 0, overrideLength ?? content.Length, "image", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
        return file;
    }

    [Fact]
    public async Task Upload_ValidJpeg_Returns200WithSize()
    {
        var imageBytes = new byte[1024];
        var file = CreateFormFile(imageBytes, "image/jpeg");

        var result = await _controller.Upload(file);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode ?? 200);

        // Verify SignalR broadcast was invoked
        _mockClientProxy.Verify(
            c => c.SendCoreAsync("ImageReceived",
                It.Is<object?[]>(a => a != null && a.Length == 1 && a[0] is byte[]),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Upload_NoFile_Returns400()
    {
        var result = await _controller.Upload(null!);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public async Task Upload_WrongContentType_Returns400()
    {
        var file = CreateFormFile(contentType: "text/plain", fileName: "test.txt");

        var result = await _controller.Upload(file);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public async Task Upload_OversizedFile_Returns400()
    {
        // Create a FormFile that reports > 10 MB
        var file = CreateFormFile(
            content: new byte[1],
            overrideLength: 11 * 1024 * 1024);

        var result = await _controller.Upload(file);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }
}
