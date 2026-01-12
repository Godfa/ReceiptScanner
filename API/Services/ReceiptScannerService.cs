using ReceiptScanner.API.Models;

namespace ReceiptScanner.API.Services;

/// <summary>
/// Main receipt scanner service with provider selection and fallback logic
/// </summary>
public class ReceiptScannerService : IReceiptScannerService
{
    private readonly IEnumerable<IReceiptScannerProvider> _providers;
    private readonly ILogger<ReceiptScannerService> _logger;

    public ReceiptScannerService(
        IEnumerable<IReceiptScannerProvider> providers,
        ILogger<ReceiptScannerService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<ScannedReceiptResult> ScanReceiptAsync(
        byte[] imageData,
        string language,
        string providerName = "auto",
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Scanning receipt with provider: {Provider}, language: {Language}, size: {Size} bytes",
            providerName,
            language,
            imageData.Length);

        if (providerName.ToLower() == "auto")
        {
            return await ScanWithFallbackAsync(imageData, language, cancellationToken);
        }

        // Use specific provider
        var provider = _providers.FirstOrDefault(p =>
            p.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            throw new ArgumentException($"Unknown provider: {providerName}");
        }

        if (!provider.IsAvailable())
        {
            throw new InvalidOperationException($"Provider {providerName} is not available/configured");
        }

        return await provider.ScanReceiptAsync(imageData, language, cancellationToken);
    }

    public IEnumerable<string> GetAvailableProviders()
    {
        return _providers
            .Where(p => p.IsAvailable())
            .Select(p => p.ProviderName)
            .ToList();
    }

    /// <summary>
    /// Try providers in order with fallback logic
    /// Priority: Azure > Ollama
    /// </summary>
    private async Task<ScannedReceiptResult> ScanWithFallbackAsync(
        byte[] imageData,
        string language,
        CancellationToken cancellationToken)
    {
        var availableProviders = _providers
            .Where(p => p.IsAvailable())
            .OrderBy(p => GetProviderPriority(p.ProviderName))
            .ToList();

        if (availableProviders.Count == 0)
        {
            throw new InvalidOperationException("No receipt scanner providers are configured");
        }

        _logger.LogInformation(
            "Using auto mode with {Count} available providers: {Providers}",
            availableProviders.Count,
            string.Join(", ", availableProviders.Select(p => p.ProviderName)));

        ScannedReceiptResult? lastResult = null;
        var errors = new List<string>();

        foreach (var provider in availableProviders)
        {
            try
            {
                _logger.LogInformation("Trying provider: {Provider}", provider.ProviderName);

                var result = await provider.ScanReceiptAsync(imageData, language, cancellationToken);

                // Check if result is good enough
                if (IsResultGoodEnough(result))
                {
                    _logger.LogInformation(
                        "Provider {Provider} succeeded with {LineCount} line items",
                        provider.ProviderName,
                        result.Lines.Count);

                    return result with
                    {
                        Provider = $"composite:{provider.ProviderName}",
                        Warnings = result.Warnings
                            .Concat(errors.Count > 0
                                ? new[] { $"Fallback used after failures: {string.Join(", ", errors)}" }
                                : Array.Empty<string>())
                            .ToList()
                    };
                }

                lastResult = result;
                errors.Add($"{provider.ProviderName}: insufficient results");

                _logger.LogWarning(
                    "Provider {Provider} returned insufficient results, trying next provider",
                    provider.ProviderName);
            }
            catch (Exception ex)
            {
                errors.Add($"{provider.ProviderName}: {ex.Message}");
                _logger.LogError(ex, "Provider {Provider} failed", provider.ProviderName);
            }
        }

        // All providers tried, return best result we got
        if (lastResult != null)
        {
            _logger.LogWarning("All providers tried, returning last result with {LineCount} items",
                lastResult.Lines.Count);

            return lastResult with
            {
                Provider = "composite:fallback",
                Warnings = lastResult.Warnings
                    .Concat(new[] { $"All providers tried: {string.Join("; ", errors)}" })
                    .ToList()
            };
        }

        // Complete failure
        throw new Exception($"All providers failed: {string.Join("; ", errors)}");
    }

    /// <summary>
    /// Check if scan result is good enough to use without fallback
    /// </summary>
    private static bool IsResultGoodEnough(ScannedReceiptResult result)
    {
        // Must have at least one line item
        if (result.Lines.Count == 0)
            return false;

        // Must not have critical errors
        var hasCriticalErrors = result.Warnings.Any(w =>
            w.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            w.Contains("failed", StringComparison.OrdinalIgnoreCase));

        if (hasCriticalErrors)
            return false;

        // Check that most lines have valid prices
        var linesWithPrices = result.Lines.Count(l => l.LineTotal > 0);
        var priceRatio = (double)linesWithPrices / result.Lines.Count;

        return priceRatio >= 0.5; // At least 50% of lines should have prices
    }

    /// <summary>
    /// Provider priority for auto mode (lower = higher priority)
    /// </summary>
    private static int GetProviderPriority(string providerName)
    {
        return providerName.ToLower() switch
        {
            "azure" => 1,   // Azure first (most reliable)
            "ollama" => 2,  // Ollama second (local/free)
            _ => 999
        };
    }
}
