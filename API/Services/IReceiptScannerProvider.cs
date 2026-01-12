using ReceiptScanner.API.Models;

namespace ReceiptScanner.API.Services;

/// <summary>
/// Interface for receipt scanning providers (Azure, Ollama, etc.)
/// </summary>
public interface IReceiptScannerProvider
{
    /// <summary>
    /// Provider name (e.g., "azure", "ollama")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Check if this provider is available/configured
    /// </summary>
    bool IsAvailable();

    /// <summary>
    /// Scan a receipt image and extract line items
    /// </summary>
    /// <param name="imageData">Raw image bytes</param>
    /// <param name="language">Language code (e.g., "fi", "en")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scanned receipt result</returns>
    Task<ScannedReceiptResult> ScanReceiptAsync(
        byte[] imageData,
        string language,
        CancellationToken cancellationToken = default);
}
