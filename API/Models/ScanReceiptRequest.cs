namespace ReceiptScanner.API.Models;

/// <summary>
/// Request to scan a receipt image
/// </summary>
public record ScanReceiptRequest
{
    /// <summary>
    /// Base64 encoded image data
    /// </summary>
    public string ImageBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Language code for OCR (e.g., "fi", "en", "sv")
    /// </summary>
    public string Language { get; init; } = "fi";

    /// <summary>
    /// Vision provider to use: "auto", "azure", "ollama"
    /// </summary>
    public string Provider { get; init; } = "auto";
}
