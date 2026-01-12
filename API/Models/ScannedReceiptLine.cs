namespace ReceiptScanner.API.Models;

/// <summary>
/// A single line item from a scanned receipt
/// </summary>
public record ScannedReceiptLine
{
    /// <summary>
    /// Item description/name
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Quantity of items
    /// </summary>
    public decimal Quantity { get; init; } = 1;

    /// <summary>
    /// Price per unit
    /// </summary>
    public decimal UnitPrice { get; init; } = 0;

    /// <summary>
    /// Total price for this line (Quantity × UnitPrice)
    /// </summary>
    public decimal LineTotal { get; init; } = 0;

    /// <summary>
    /// Confidence score (0.0 to 1.0) if available
    /// </summary>
    public decimal? Confidence { get; init; }
}
