using Microsoft.AspNetCore.Mvc;
using ReceiptScanner.API.Models;
using ReceiptScanner.API.Services;

namespace ReceiptScanner.API.Controllers;

/// <summary>
/// Receipt scanning endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReceiptsController : ControllerBase
{
    private readonly IReceiptScannerService _scannerService;
    private readonly ILogger<ReceiptsController> _logger;

    public ReceiptsController(
        IReceiptScannerService scannerService,
        ILogger<ReceiptsController> logger)
    {
        _scannerService = scannerService;
        _logger = logger;
    }

    /// <summary>
    /// Scan a receipt image and extract line items
    /// </summary>
    /// <param name="request">Scan request with base64 image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scanned receipt result with line items</returns>
    [HttpPost("scan")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ScannedReceiptResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ScannedReceiptResult>> ScanReceipt(
        [FromBody] ScanReceiptRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ImageBase64))
            {
                return BadRequest(new { error = "ImageBase64 is required" });
            }

            // Decode base64 image
            byte[] imageData;
            try
            {
                imageData = Convert.FromBase64String(request.ImageBase64);
            }
            catch (FormatException)
            {
                return BadRequest(new { error = "Invalid base64 image data" });
            }

            // Validate image size (max 10MB)
            if (imageData.Length > 10_000_000)
            {
                return BadRequest(new { error = "Image size exceeds 10MB limit" });
            }

            // Scan receipt
            var result = await _scannerService.ScanReceiptAsync(
                imageData,
                request.Language,
                request.Provider,
                cancellationToken);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid scan request");
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Scanner not available");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receipt scan failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Receipt scan failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Scan a receipt from uploaded file (multipart/form-data)
    /// </summary>
    /// <param name="file">Receipt image file</param>
    /// <param name="language">Language code (default: fi)</param>
    /// <param name="provider">Provider to use (default: auto)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scanned receipt result</returns>
    [HttpPost("scan-file")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [RequestSizeLimit(10_000_000)] // 10MB
    [ProducesResponseType(typeof(ScannedReceiptResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScannedReceiptResult>> ScanReceiptFile(
        IFormFile file,
        [FromQuery] string language = "fi",
        [FromQuery] string provider = "auto",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded" });
            }

            // Validate content type
            var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType?.ToLower()))
            {
                return BadRequest(new
                {
                    error = $"Invalid file type: {file.ContentType}. Allowed: {string.Join(", ", allowedTypes)}"
                });
            }

            // Read file data
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            var imageData = ms.ToArray();

            // Scan receipt
            var result = await _scannerService.ScanReceiptAsync(
                imageData,
                language,
                provider,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File upload scan failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Receipt scan failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Get list of available scanner providers
    /// </summary>
    /// <returns>List of provider names</returns>
    [HttpGet("providers")]
    [Produces("application/json")]
    public ActionResult<IEnumerable<string>> GetProviders()
    {
        var providers = _scannerService.GetAvailableProviders();
        return Ok(new { providers, count = providers.Count() });
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    public ActionResult Health()
    {
        var providers = _scannerService.GetAvailableProviders().ToList();
        return Ok(new
        {
            status = "healthy",
            availableProviders = providers,
            providerCount = providers.Count,
            timestamp = DateTime.UtcNow
        });
    }
}
