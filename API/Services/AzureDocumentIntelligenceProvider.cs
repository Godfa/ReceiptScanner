using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using ReceiptScanner.API.Models;

namespace ReceiptScanner.API.Services;

/// <summary>
/// Receipt scanner using Azure AI Document Intelligence (Form Recognizer)
/// </summary>
public class AzureDocumentIntelligenceProvider : IReceiptScannerProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureDocumentIntelligenceProvider> _logger;
    private DocumentAnalysisClient? _client;

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
            using var stream = new MemoryStream(imageData);
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-receipt",
                stream,
                cancellationToken: cancellationToken);

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
                    && merchantField.FieldType == DocumentFieldType.String)
                {
                    merchantName = merchantField.Value.AsString();
                }

                // Extract transaction date
                if (document.Fields.TryGetValue("TransactionDate", out var dateField)
                    && dateField.FieldType == DocumentFieldType.Date)
                {
                    var dateOnly = dateField.Value.AsDate();
                    receiptDate = new DateTime(dateOnly.Year, dateOnly.Month, dateOnly.Day);
                }

                // Extract total
                if (document.Fields.TryGetValue("Total", out var totalField)
                    && totalField.FieldType == DocumentFieldType.Double)
                {
                    totalAmount = (decimal)totalField.Value.AsDouble();
                }

                // Extract line items
                if (document.Fields.TryGetValue("Items", out var itemsField)
                    && itemsField.FieldType == DocumentFieldType.List)
                {
                    foreach (var item in itemsField.Value.AsList())
                    {
                        if (item.FieldType != DocumentFieldType.Dictionary)
                            continue;

                        var itemDict = item.Value.AsDictionary();

                        string? description = null;
                        decimal? quantity = null;
                        decimal? price = null;
                        decimal? totalPrice = null;
                        decimal? confidence = null;

                        if (itemDict.TryGetValue("Description", out var descField)
                            && descField.FieldType == DocumentFieldType.String)
                        {
                            description = descField.Value.AsString();
                            confidence = (decimal?)descField.Confidence;
                        }

                        if (itemDict.TryGetValue("Quantity", out var qtyField)
                            && qtyField.FieldType == DocumentFieldType.Double)
                        {
                            quantity = (decimal)qtyField.Value.AsDouble();
                        }

                        if (itemDict.TryGetValue("Price", out var priceField)
                            && priceField.FieldType == DocumentFieldType.Double)
                        {
                            price = (decimal)priceField.Value.AsDouble();
                        }

                        if (itemDict.TryGetValue("TotalPrice", out var totalPriceField)
                            && totalPriceField.FieldType == DocumentFieldType.Double)
                        {
                            totalPrice = (decimal)totalPriceField.Value.AsDouble();
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

    private DocumentAnalysisClient CreateClient()
    {
        var endpoint = _configuration["Azure:DocumentIntelligence:Endpoint"]!;
        var apiKey = _configuration["Azure:DocumentIntelligence:ApiKey"]!;

        return new DocumentAnalysisClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));
    }
}
