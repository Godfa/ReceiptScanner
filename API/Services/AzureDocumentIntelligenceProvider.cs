using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using ReceiptScanner.API.Models;

namespace ReceiptScanner.API.Services;

/// <summary>
/// Receipt scanner using Azure AI Document Intelligence
/// </summary>
public class AzureDocumentIntelligenceProvider : IReceiptScannerProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureDocumentIntelligenceProvider> _logger;
    private DocumentIntelligenceClient? _client;

    public string ProviderName => "azure";

    public AzureDocumentIntelligenceProvider(
        IConfiguration configuration,
        ILogger<AzureDocumentIntelligenceProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsAvailable()
    {
        var enabled = _configuration.GetValue<bool>("Azure:DocumentIntelligence:Enabled", true);
        if (!enabled)
        {
            _logger.LogDebug("Azure provider is disabled in configuration");
            return false;
        }

        var endpoint = _configuration["Azure:DocumentIntelligence:Endpoint"];
        var apiKey = _configuration["Azure:DocumentIntelligence:ApiKey"];
        return !string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(apiKey);
    }

    public async Task<ScannedReceiptResult> ScanReceiptAsync(
        byte[] imageData,
        string language,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!IsAvailable())
            {
                throw new InvalidOperationException("Azure Document Intelligence is not configured");
            }

            _client ??= CreateClient();

            // Use prebuilt receipt model
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-receipt",
                BinaryData.FromBytes(imageData),
                cancellationToken);

            var result = operation.Value;
            var lines = new List<ScannedReceiptLine>();
            var warnings = new List<string>();
            decimal? totalAmount = null;
            string? merchantName = null;
            DateTime? receiptDate = null;

            // Process each receipt (usually just one)
            foreach (var document in result.Documents)
            {
                // Extract merchant name
                if (document.Fields.TryGetValue("MerchantName", out var merchantField)
                    && merchantField.ValueString != null)
                {
                    merchantName = merchantField.ValueString;
                }

                // Extract transaction date
                if (document.Fields.TryGetValue("TransactionDate", out var dateField)
                    && dateField.ValueDate.HasValue)
                {
                    var dateOnly = dateField.ValueDate.Value;
                    receiptDate = new DateTime(dateOnly.Year, dateOnly.Month, dateOnly.Day);
                }

                // Extract total
                if (document.Fields.TryGetValue("Total", out var totalField))
                {
                    totalAmount = GetAmount(totalField);
                }

                // Extract line items
                if (document.Fields.TryGetValue("Items", out var itemsField)
                    && itemsField.ValueList != null)
                {
                    foreach (var item in itemsField.ValueList)
                    {
                        var itemDict = item.ValueDictionary;
                        if (itemDict == null)
                            continue;

                        string? description = null;
                        decimal? quantity = null;
                        decimal? price = null;
                        decimal? totalPrice = null;
                        decimal? confidence = null;

                        if (itemDict.TryGetValue("Description", out var descField)
                            && descField.ValueString != null)
                        {
                            description = descField.ValueString;
                            confidence = (decimal?)descField.Confidence;
                        }

                        if (itemDict.TryGetValue("Quantity", out var qtyField))
                        {
                            quantity = GetAmount(qtyField);
                        }

                        if (itemDict.TryGetValue("Price", out var priceField))
                        {
                            price = GetAmount(priceField);
                        }

                        if (itemDict.TryGetValue("TotalPrice", out var totalPriceField))
                        {
                            totalPrice = GetAmount(totalPriceField);
                        }

                        // Skip items without description
                        if (string.IsNullOrWhiteSpace(description))
                        {
                            warnings.Add("Skipped item without description");
                            continue;
                        }

                        // Calculate missing values
                        var qty = quantity ?? 1;
                        var unitPrice = price ?? (totalPrice.HasValue ? totalPrice.Value / qty : 0);
                        var lineTotal = totalPrice ?? (unitPrice * qty);

                        // Add warning for low confidence
                        if (confidence.HasValue && confidence.Value < 0.7m)
                        {
                            warnings.Add($"Low confidence on item: {description} ({confidence.Value:P0})");
                        }

                        lines.Add(new ScannedReceiptLine
                        {
                            Description = description.Trim(),
                            Quantity = qty,
                            UnitPrice = unitPrice,
                            LineTotal = lineTotal,
                            Confidence = confidence
                        });
                    }
                }
            }

            stopwatch.Stop();

            return new ScannedReceiptResult
            {
                Provider = ProviderName,
                Lines = lines,
                Total = totalAmount,
                MerchantName = merchantName,
                ReceiptDate = receiptDate,
                Warnings = warnings,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Azure Document Intelligence scan failed");

            return new ScannedReceiptResult
            {
                Provider = ProviderName,
                Lines = new List<ScannedReceiptLine>(),
                Warnings = new List<string> { $"Azure scan error: {ex.Message}" },
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Reads a numeric or currency field as a decimal (prebuilt-receipt returns prices as currency values).
    /// </summary>
    private static decimal? GetAmount(DocumentField field)
    {
        if (field.ValueCurrency != null)
            return (decimal)field.ValueCurrency.Amount;
        if (field.ValueDouble.HasValue)
            return (decimal)field.ValueDouble.Value;
        return null;
    }

    private DocumentIntelligenceClient CreateClient()
    {
        var endpoint = _configuration["Azure:DocumentIntelligence:Endpoint"]!;
        var apiKey = _configuration["Azure:DocumentIntelligence:ApiKey"]!;

        return new DocumentIntelligenceClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));
    }
}
