using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReceiptScanner.API.Models;

namespace ReceiptScanner.API.Services;

/// <summary>
/// Receipt scanner using Ollama vision models (e.g., llama3.2-vision)
/// </summary>
public class OllamaVisionProvider : IReceiptScannerProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OllamaVisionProvider> _logger;
    private readonly HttpClient _httpClient;

    public string ProviderName => "ollama";

    public OllamaVisionProvider(
        IConfiguration configuration,
        ILogger<OllamaVisionProvider> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Ollama");
    }

    public bool IsAvailable()
    {
        var enabled = _configuration.GetValue<bool>("Ollama:Enabled", true);
        if (!enabled)
        {
            _logger.LogDebug("Ollama provider is disabled in configuration");
            return false;
        }

        var baseUrl = _configuration["Ollama:BaseUrl"];
        var model = _configuration["Ollama:Model"];
        return !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(model);
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
                throw new InvalidOperationException("Ollama is not configured");
            }

            var baseUrl = _configuration["Ollama:BaseUrl"]!;
            var model = _configuration["Ollama:Model"] ?? "llama3.2-vision";

            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(120);

            // Convert image to base64
            var base64Image = Convert.ToBase64String(imageData);

            // Create prompt for structured extraction
            var prompt = BuildPrompt(language);

            // Call Ollama API
            var request = new
            {
                model = model,
                prompt = prompt,
                images = new[] { base64Image },
                stream = false,
                options = new
                {
                    temperature = 0.1,
                    top_p = 0.9
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/generate",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseContent);

            if (ollamaResponse?.Response == null)
            {
                throw new Exception("Empty response from Ollama");
            }

            // Parse the structured response
            var result = ParseReceiptFromText(ollamaResponse.Response);
            result = result with
            {
                Provider = ProviderName,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };

            stopwatch.Stop();
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Ollama vision scan failed");

            return new ScannedReceiptResult
            {
                Provider = ProviderName,
                Lines = new List<ScannedReceiptLine>(),
                Warnings = new List<string> { $"Ollama scan error: {ex.Message}" },
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private static string BuildPrompt(string language)
{
    var langInstruction = language.ToLower() switch
    {
        "fi" => "The receipt is in Finnish.",
        "sv" => "The receipt is in Swedish.",
        "en" => "The receipt is in English.",
        _ => ""
    };

    return $$"""
You are a receipt line-item extraction engine.
{{langInstruction}}

Your task is to extract purchasable items ONLY.

Return output wrapped EXACTLY like this:
<json>
{ ... }
</json>

The JSON structure MUST be:
{
  "merchantName": string | null,
  "date": string | null,        // ISO YYYY-MM-DD, or null
  "items": [
    {
      "description": string,
      "quantity": number,
      "unitPrice": number,
      "lineTotal": number
    }
  ],
  "total": number | null
}

STRICT RULES:
- Do NOT guess merchant name or date; return null if unclear
- Extract ONLY product/service rows (no VAT, totals, payments, footers)
- Quantity defaults to 1 if not explicitly shown
- If only lineTotal is shown, calculate unitPrice = lineTotal / quantity
- Prices must be numeric decimals with max 2 decimal places
- Refunds or returns may have negative values
- If math is inconsistent, use the numbers printed on the receipt
- Return VALID JSON only inside <json> tags
- No explanations, no commentary outside <json>

Failure to follow format is an error.
""";
}


    private ScannedReceiptResult ParseReceiptFromText(string text)
    {
        var warnings = new List<string>();
        var lines = new List<ScannedReceiptLine>();
        decimal? total = null;
        string? merchantName = null;
        DateTime? receiptDate = null;

        try
        {
            // Try to extract JSON from the response
            var jsonPayload = ExtractJsonPayload(text);
           if (jsonPayload == null)
            {
                warnings.Add("JSON sentinel tags not found in Ollama response");
                return new ScannedReceiptResult
                {
                    Lines = lines,
                    Warnings = warnings,
                    RawText = text
                };
            }

            using var doc = JsonDocument.Parse(jsonPayload);
            var root = doc.RootElement;

            // Extract merchant name
            if (root.TryGetProperty("merchantName", out var merchantProp))
            {
                merchantName = merchantProp.GetString();
            }

            // Extract date
            if (root.TryGetProperty("date", out var dateProp))
            {
                var dateStr = dateProp.GetString();
                if (DateTime.TryParse(dateStr, out var parsedDate))
                {
                    receiptDate = parsedDate;
                }
            }

            // Extract total
            if (root.TryGetProperty("total", out var totalProp))
            {
                if (totalProp.ValueKind == JsonValueKind.Number)
                {
                    total = totalProp.GetDecimal();
                }
            }

            // Extract items
            if (root.TryGetProperty("items", out var itemsProp) &&
                itemsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsProp.EnumerateArray())
                {
                    if (!item.TryGetProperty("description", out var descProp))
                        continue;

                    var description = descProp.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(description))
                        continue;

                    // Quantity (default = 1)
                    decimal quantity = 1m;
                    if (item.TryGetProperty("quantity", out var qtyProp) &&
                        qtyProp.ValueKind == JsonValueKind.Number)
                    {
                        quantity = qtyProp.GetDecimal();
                    }

                        // Unit price (optional)
                    decimal? unitPrice = null;
                    if (item.TryGetProperty("unitPrice", out var unitProp) &&
                        unitProp.ValueKind == JsonValueKind.Number)
                    {
                        unitPrice = unitProp.GetDecimal();
                    }

                    // Line total (optional)
                    decimal? lineTotal = null;
                    if (item.TryGetProperty("lineTotal", out var totalProp2) &&
                        totalProp2.ValueKind == JsonValueKind.Number)
                    {
                        lineTotal = totalProp2.GetDecimal();
                    }

                    // Infer missing values
                    if (lineTotal == null && unitPrice != null)
                        lineTotal = Math.Round(quantity * unitPrice.Value, 2);

                    if (unitPrice == null && lineTotal != null && quantity != 0)
                        unitPrice = Math.Round(lineTotal.Value / quantity, 2);

                        // Final safety net
                    unitPrice ??= 0m;
                    lineTotal ??= 0m;

                    // Sanity checks
                    if (quantity <= 0)
                            warnings.Add($"Invalid quantity for item '{description}'");

                    var expected = Math.Round(quantity * unitPrice.Value, 2);
                    if (Math.Abs(expected - lineTotal.Value) > 0.02m)
                    {
                        warnings.Add(
                            $"Line math mismatch for '{description}': " +
                            $"{quantity} x {unitPrice:F2} ≠ {lineTotal:F2}"
                        );
                    }

                    lines.Add(new ScannedReceiptLine
                        {
                            Description = description,
                            Quantity = quantity,
                            UnitPrice = unitPrice.Value,
                            LineTotal = lineTotal.Value
                        });
                }
            }

            if (lines.Count == 0)
            {
                warnings.Add("No line items extracted from receipt");
            }

            // Reconcile totals
            var computedSum = Math.Round(lines.Sum(l => l.LineTotal), 2);
            if (total.HasValue)
            {
                var delta = Math.Abs(computedSum - total.Value);
                if (delta > 0.05m)
                {
                    warnings.Add(
                        $"Total mismatch: sum(items)={computedSum:F2} vs receipt total={total:F2}"
                    );
                }
            }
            else
            {
                warnings.Add("Receipt total not provided by model");
            }

            return new ScannedReceiptResult
            {
                Lines = lines,
                Total = total,
                MerchantName = merchantName,
                ReceiptDate = receiptDate,
                Warnings = warnings,
                RawText = text
            };  
        }
        catch (JsonException ex)
        {
            warnings.Add($"JSON parse error: {ex.Message}");
            return new ScannedReceiptResult
            {
                Lines = lines,
                Warnings = warnings,
                RawText = text
            };
        }
    }

    private static string? ExtractJsonPayload(string text)
    {
        const string start = "<json>";
        const string end = "</json>";

        var startIdx = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        var endIdx = text.IndexOf(end, StringComparison.OrdinalIgnoreCase);

        if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
            return null;

        return text.Substring(
            startIdx + start.Length,
            endIdx - (startIdx + start.Length)
        ).Trim();
    }


    private record OllamaResponse
    {
        public string? Response { get; init; }
        public string? Model { get; init; }
    }
}
