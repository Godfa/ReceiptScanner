using ReceiptScanner.API.Models;

namespace ReceiptScanner.API.Services;

/// <summary>
/// Main service interface for receipt scanning with provider selection
/// </summary>
public interface IReceiptScannerService
{
    /// <summary>
    /// Scan a receipt using the specified provider
    /// </summary>
    /// <param name="imageData">Raw image bytes</param>
    /// <param name="language">Language code</param>
    /// <param name="providerName">Provider to use ("auto", "azure", "ollama")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scanned receipt result</returns>
    Task<ScannedReceiptResult> ScanReceiptAsync(
        byte[] imageData,
        string language,
        string providerName = "auto",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of available providers
    /// </summary>
    IEnumerable<string> GetAvailableProviders();
}
