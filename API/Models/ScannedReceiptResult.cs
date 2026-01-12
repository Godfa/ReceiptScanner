namespace ReceiptScanner.API.Models;

/// <summary>
/// Result from scanning a receipt
/// </summary>
public record ScannedReceiptResult
{
    /// <summary>
    /// Provider that processed the receipt ("azure", "ollama", "composite")
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// List of line items extracted from the receipt
    /// </summary>
    public List<ScannedReceiptLine> Lines { get; init; } = new();

    /// <summary>
    /// Total amount from the receipt (if detected)
    /// </summary>
    public decimal? Total { get; init; }

    /// <summary>
    /// Raw OCR text (for debugging/fallback)
    /// </summary>
    public string? RawText { get; init; }

    /// <summary>
    /// Warnings or issues encountered during scanning
    /// </summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>
    /// Merchant/store name if detected
    /// </summary>
    public string? MerchantName { get; init; }

    /// <summary>
    /// Receipt date if detected
    /// </summary>
    public DateTime? ReceiptDate { get; init; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public long ProcessingTimeMs { get; init; }
}
