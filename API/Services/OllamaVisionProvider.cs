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
            You are a receipt parser. Extract line items from this receipt image.
            {{langInstruction}}

            Return ONLY a JSON object with this exact structure (no additional text):
            {
              "merchantName": "Store name",
              "date": "YYYY-MM-DD",
              "items": [
                {"description": "Item name", "quantity": 1, "unitPrice": 1.50, "lineTotal": 1.50}
              ],
              "total": 10.50
            }

            Rules:
            - Extract ALL line items with prices
            - If quantity is not shown, use 1
            - If only total price shown, calculate unitPrice = lineTotal / quantity
            - Round prices to 2 decimals
            - Skip non-product lines (headers, footers, payment info)
            - Return valid JSON only
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
            var jsonMatch = Regex.Match(text, @"\{[\s\S]*\}", RegexOptions.Multiline);
            if (!jsonMatch.Success)
            {
                warnings.Add("Could not find JSON in Ollama response");
                return new ScannedReceiptResult
                {
                    Lines = lines,
                    Warnings = warnings,
                    RawText = text
                };
            }

            var json = jsonMatch.Value;
            using var doc = JsonDocument.Parse(json);
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
            if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsProp.EnumerateArray())
                {
                    if (!item.TryGetProperty("description", out var descProp))
                        continue;

                    var description = descProp.GetString();
                    if (string.IsNullOrWhiteSpace(description))
                        continue;

                    var quantity = 1m;
                    if (item.TryGetProperty("quantity", out var qtyProp))
                    {
                        quantity = qtyProp.ValueKind == JsonValueKind.Number
                            ? qtyProp.GetDecimal()
                            : 1m;
                    }

                    var unitPrice = 0m;
                    if (item.TryGetProperty("unitPrice", out var priceProp))
                    {
                        unitPrice = priceProp.ValueKind == JsonValueKind.Number
                            ? priceProp.GetDecimal()
                            : 0m;
                    }

                    var lineTotal = 0m;
                    if (item.TryGetProperty("lineTotal", out var totalProp2))
                    {
                        lineTotal = totalProp2.ValueKind == JsonValueKind.Number
                            ? totalProp2.GetDecimal()
                            : quantity * unitPrice;
                    }
                    else
                    {
                        lineTotal = quantity * unitPrice;
                    }

                    lines.Add(new ScannedReceiptLine
                    {
                        Description = description.Trim(),
                        Quantity = quantity,
                        UnitPrice = unitPrice,
                        LineTotal = lineTotal
                    });
                }
            }

            if (lines.Count == 0)
            {
                warnings.Add("No line items extracted from receipt");
            }
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

    private record OllamaResponse
    {
        public string? Response { get; init; }
        public string? Model { get; init; }
    }
}
